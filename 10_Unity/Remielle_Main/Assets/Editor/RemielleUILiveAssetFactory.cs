using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using System.Security.Cryptography;
using Unity.Collections;
using UnityEngine.Rendering;

// Native format decoding retained from the validated captured-stage fixture.
public static class RemielleUILiveAssetFactory
{
    static Vector4 Decode(byte[] raw, int at, int format)
    {
        var value = new Vector4(0, 0, 0, 1);
        if (format == 28) return new Vector4(raw[at] / 255f, raw[at + 1] / 255f, raw[at + 2] / 255f, raw[at + 3] / 255f);
        if (format == 34)
        {
            value.x = Mathf.HalfToFloat(BitConverter.ToUInt16(raw, at));
            value.y = Mathf.HalfToFloat(BitConverter.ToUInt16(raw, at + 2));
            return value;
        }
        int count = format == 2 ? 4 : format == 6 ? 3 : format == 16 ? 2 : 0;
        if (count == 0) throw new Exception("Unknown vertex format " + format);
        for (int i = 0; i < count; i++) value[i] = BitConverter.ToSingle(raw, at + i * 4);
        return value;
    }

    public static Mesh BuildMesh(JObject row)
    {
        int vertices = (int)row["vertices"], first = (int)row["firstIndex"], count = (int)row["indexCount"];
        var streams = new Dictionary<int, byte[]>();
        var strides = new Dictionary<int, int>();
        foreach (var vb in row["vertexBuffers"])
        {
            int slot = (int)vb["slot"];
            streams[slot] = File.ReadAllBytes((string)vb["path"]);
            strides[slot] = (int)vb["stride"];
        }
        var mesh = new Mesh { name = "UIStage_" + row["id"], indexFormat = IndexFormat.UInt32, hideFlags = HideFlags.HideAndDontSave };
        foreach (JArray e in row["inputLayout"])
        {
            string semantic = (string)e[0];
            int semanticIndex = (int)e[1], format = (int)e[2], slot = (int)e[3], offset = (int)e[4];
            var values = new List<Vector4>(vertices);
            for (int v = 0; v < vertices; v++) values.Add(Decode(streams[slot], v * strides[slot] + offset, format));
            if (semantic == "POSITION") mesh.SetVertices(values.Select(v => (Vector3)v).ToList());
            else if (semantic == "NORMAL") mesh.SetNormals(values.Select(v => (Vector3)v).ToList());
            else if (semantic == "TANGENT") mesh.SetTangents(values);
            else if (semantic == "COLOR") mesh.SetColors(values.Select(v => new Color(v.x, v.y, v.z, v.w)).ToList());
            else if (semantic == "TEXCOORD") mesh.SetUVs(semanticIndex, values);
            else throw new Exception("Unexpected vertex semantic");
        }
        var raw = File.ReadAllBytes((string)row["indexBuffer"]);
        var indices = new int[count];
        for (int i = 0; i < count; i++) indices[i] = BitConverter.ToUInt16(raw, (first + i) * 2);
        mesh.SetIndices(indices, MeshTopology.Triangles, 0, true);
        mesh.UploadMeshData(false);
        return mesh;
    }

    public static Texture LoadExtra(JObject row)
    {
        int w = (int)row["width"], h = (int)row["height"], format = (int)row["format"], mips = (int)row["mips"];
        if (mips < 1 || (int)row["layers"] != 1) throw new Exception("Unsupported extra texture layout");
        TextureFormat unityFormat;
        int bytesPerPixel;
        if (format == 10) { unityFormat = TextureFormat.RGBAHalf; bytesPerPixel = 8; }
        else if (format == 56) { unityFormat = TextureFormat.R16; bytesPerPixel = 2; }
        else if (format == 28 || format == 29) { unityFormat = TextureFormat.RGBA32; bytesPerPixel = 4; }
        else throw new Exception("Unsupported extra texture format " + format);
        var bytes = File.ReadAllBytes((string)row["path"]);
        int header = bytes[84] == 'D' && bytes[85] == 'X' && bytes[86] == '1' && bytes[87] == '0' ? 148 : 128;
        int expected = 0;
        for (int mip = 0; mip < mips; mip++) expected += Math.Max(1, w >> mip) * Math.Max(1, h >> mip) * bytesPerPixel;
        if (bytes.Length - header != expected) throw new Exception("Extra texture size mismatch");
        var texture = new Texture2D(w, h, unityFormat, mips, format != 29) { hideFlags = HideFlags.HideAndDontSave };
        var payload = new byte[expected];
        Buffer.BlockCopy(bytes, header, payload, 0, payload.Length);
        texture.LoadRawTextureData(payload);
        texture.Apply(false, false);
        return texture;
    }

    static string Sha(string path){using var f=File.OpenRead(path);using var h=SHA256.Create();return BitConverter.ToString(h.ComputeHash(f)).Replace("-", "").ToLowerInvariant();}
    static NativeArray<T> ReadTexture<T>(RenderTexture texture, int count) where T : struct
    {
        // Caller-owned storage avoids retaining one large internal staging array
        // per request until the editor returns to its next frame.
        var data = new NativeArray<T>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        var request = AsyncGPUReadback.RequestIntoNativeArray(ref data, texture, 0);
        request.WaitForCompletion();
        if (request.hasError) { data.Dispose(); throw new Exception("GPU readback failed: " + texture.graphicsFormat); }
        return data;
    }

    public static JObject SaveSequenceReadback(RenderTexture[] targets, RenderTexture depthRead, string stem, string id, int width, int height)
    {
        using (var ds = ReadTexture<uint>(depthRead, width * height * 4))
        {
        var dsFloats = ds.Reinterpret<float>();
        if (ds.Length != width * height * 4) throw new Exception("Packed depth/stencil size mismatch");
        var pixels = new List<uint>();
        for (int p = 0; p < width * height; p++) if (ds[p * 4] != 0 || ds[p * 4 + 1] != 0) pixels.Add((uint)p);
        var words = new uint[pixels.Count * 7];
        for (int p = 0; p < pixels.Count; p++)
        {
            words[p * 7 + 5] = ds[(int)pixels[p] * 4];
            words[p * 7 + 6] = (uint)dsFloats[(int)pixels[p] * 4 + 1];
        }
        for (int rt = 0; rt < targets.Length; rt++)
        {
            int stride = rt == 0 ? 2 : 1;
            using (var data = ReadTexture<uint>(targets[rt], width * height * stride))
            {
            if (data.Length != width * height * stride) throw new Exception("Packed color size mismatch");
            for (int p = 0; p < pixels.Count; p++) for (int ch = 0; ch < stride; ch++)
                words[p * 7 + (rt == 0 ? ch : rt + 1)] = data[(int)pixels[p] * stride + ch];
            }
        }
        var bytes = new byte[words.Length * 4];
        Buffer.BlockCopy(words, 0, bytes, 0, bytes.Length); File.WriteAllBytes(stem + ".packed", bytes);
        var indices = pixels.ToArray(); bytes = new byte[indices.Length * 4];
        Buffer.BlockCopy(indices, 0, bytes, 0, bytes.Length); File.WriteAllBytes(stem + ".u32", bytes);
        return new JObject { ["id"] = id, ["pixels"] = pixels.Count,
            ["output"] = stem + ".packed", ["outputSha256"] = Sha(stem + ".packed"),
            ["indices"] = stem + ".u32", ["indicesSha256"] = Sha(stem + ".u32") };
        }
    }

}
