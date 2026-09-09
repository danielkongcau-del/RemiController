using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

// Independent captured-stage fixture. Does not modify the saved model or scene.
public static class RemielleNativeUISequenceGpuAudit
{
    const string Root = "E:/ZZZ/local-only/RemielleRenderingReview/20260905/ui-full-sequence/";
    const string Shaders = "Assets/RenderingReview/Shader/GeneratedNativeUISequence/";
    [StructLayout(LayoutKind.Sequential)] struct Entity { public Vector4 a, b, c, d, e, f, g, h; }

    static string Sha(string path)
    {
        using (var s = File.OpenRead(path)) using (var h = SHA256.Create())
            return BitConverter.ToString(h.ComputeHash(s)).Replace("-", "").ToLowerInvariant();
    }

    static T[] ReadStructs<T>(byte[] bytes) where T : struct
    {
        int size = Marshal.SizeOf<T>();
        if (bytes.Length % size != 0) throw new Exception("Invalid structured bytes");
        var values = new T[bytes.Length / size];
        var pin = GCHandle.Alloc(values, GCHandleType.Pinned);
        try { Marshal.Copy(bytes, 0, pin.AddrOfPinnedObject(), bytes.Length); }
        finally { pin.Free(); }
        return values;
    }

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

    static Mesh BuildMesh(JObject row)
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

    static Texture LoadExtra(JObject row)
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

    static RenderTexture Target(int width, int height, GraphicsFormat color, GraphicsFormat depth)
    {
        var desc = new RenderTextureDescriptor(width, height)
        { graphicsFormat = color, depthStencilFormat = depth, msaaSamples = 1, mipCount = 1 };
        if (depth == GraphicsFormat.D32_SFloat_S8_UInt) desc.stencilFormat = GraphicsFormat.R8_UInt;
        var texture = new RenderTexture(desc) { hideFlags = HideFlags.HideAndDontSave };
        if (!texture.Create()) throw new Exception("Render target creation failed");
        return texture;
    }

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

    static JObject SaveSequenceReadback(RenderTexture[] targets, RenderTexture depthRead, string stem, string id, int width, int height)
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

    public static void Run() { Run(false); }
    public static void RunCapturedSequences() { Run(true); }

    static void Run(bool sequential)
    {
        if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D11 || !SystemInfo.usesReversedZBuffer)
            throw new Exception("D3D11 reversed Z required");
        var manifest = JObject.Parse(File.ReadAllText(Root + "manifest.json"));
        var generated = JObject.Parse(File.ReadAllText(Root + (sequential ? "unity-sequence-shader.json" : "unity-shader.json")));
        var oldAnisotropicFiltering = QualitySettings.anisotropicFiltering;
        var textures = new Dictionary<string, Texture>();
        var owned = new List<UnityEngine.Object>();
        var buffers = new List<RenderTexture>();
        var results = new JArray();
        var messages = new JArray();
        string output = Root + (sequential ? "unity-sequence-gpu" : "unity-gpu");
        Directory.CreateDirectory(output);
        int width = (int)manifest["width"], height = (int)manifest["height"];
        string shaderRoot = sequential ? "Assets/RenderingReview/Shader/GeneratedNativeUIReplay/" : Shaders;
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderRoot + (sequential ? "CapturedNativeUIReplay.shader" : "CapturedNativeUISequence.shader"));
        var depthShader = AssetDatabase.LoadAssetAtPath<Shader>(shaderRoot + (sequential ? "CapturedUIReadDepthStencil.shader" : "CapturedUIDepthRead.shader"));
        if (!shader || !shader.isSupported || !depthShader || !depthShader.isSupported)
            throw new Exception("UI stage shader unsupported");
        try
        {
            if (sequential)
            {
                QualitySettings.anisotropicFiltering = AnisotropicFiltering.Enable;
            }
            foreach (JObject row in manifest["textures"])
            {
                string asset = (string)row["asset"];
                Texture texture;
                if (asset != null)
                {
                    texture = AssetDatabase.LoadAssetAtPath<Texture>(asset);
                    if (sequential && texture)
                    {
                        texture = UnityEngine.Object.Instantiate(texture);
                        texture.hideFlags = HideFlags.HideAndDontSave; owned.Add(texture);
                    }
                }
                else { texture = LoadExtra(row); owned.Add(texture); }
                if (!texture) throw new Exception("Missing texture");
                if (sequential)
                {
                    texture.filterMode = FilterMode.Trilinear;
                    texture.wrapMode = TextureWrapMode.Repeat;
                    texture.anisoLevel = 8;
                }
                textures.Add((string)row["path"], texture);
            }
            var targets = new RenderTexture[4];
            var ids = new RenderTargetIdentifier[4];
            var nativeFormats = new[] { GraphicsFormat.R16G16B16A16_SFloat, GraphicsFormat.R8G8B8A8_SRGB,
                GraphicsFormat.A2B10G10R10_UNormPack32, GraphicsFormat.A2B10G10R10_UNormPack32 };
            for (int i = 0; i < 4; i++)
            {
                targets[i] = Target(width, height, sequential ? nativeFormats[i] : GraphicsFormat.R32G32B32A32_SFloat, GraphicsFormat.None);
                buffers.Add(targets[i]);
                ids[i] = new RenderTargetIdentifier(targets[i]);
            }
            var depth = Target(width, height, GraphicsFormat.None, sequential ? GraphicsFormat.D32_SFloat_S8_UInt : GraphicsFormat.D32_SFloat);
            var depthRead = Target(width, height, sequential ? GraphicsFormat.R32G32B32A32_SFloat : GraphicsFormat.R32_SFloat, GraphicsFormat.None);
            buffers.Add(depth); buffers.Add(depthRead);
            var depthMaterial = new Material(depthShader) { hideFlags = HideFlags.HideAndDontSave };
            owned.Add(depthMaterial);
            depthMaterial.SetTexture("_SeqDepth", depth, RenderTextureSubElement.Depth);
            if (sequential) depthMaterial.SetTexture("_SeqStencil", depth, RenderTextureSubElement.Stencil);
            foreach (JObject row in manifest["cases"])
            {
                var mesh = BuildMesh(row);
                var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                var bindings = new List<GraphicsBuffer>();
                var cmd = new CommandBuffer { name = "Remielle captured UI stage" };
                try
                {
                    if (row["samplers"].Any(s => (string)s["slot"] == "s2" && (int)s["group"] == 1))
                        material.EnableKeyword("UI_S2_FIXTURE_B");
                    foreach (var cb in row["constantBuffers"])
                    {
                        string stage = (string)cb["stage"], slot = cb["slot"].ToString();
                        int count = (int)cb["declaredFloat4"];
                        if (count != (int)generated["declarations"][(string)row[stage]][slot])
                            throw new Exception("Constant buffer declaration mismatch");
                        var values = ReadStructs<Vector4>(File.ReadAllBytes((string)cb["path"]));
                        if (values.Length < count) throw new Exception("Captured CB shorter than declaration");
                        var buffer = new GraphicsBuffer(GraphicsBuffer.Target.Constant, count, 16);
                        bindings.Add(buffer);
                        buffer.SetData(values, 0, 0, count);
                        material.SetConstantBuffer("Seq" + stage.ToUpperInvariant() + "Cb" + slot, buffer, 0, count * 16);
                    }
                    foreach (var resource in row["resources"])
                    {
                        string name = "_Seq" + ((string)resource["stage"]).ToUpperInvariant() + ((string)resource["slot"]).ToUpperInvariant();
                        if ((int)resource["kind"] == 0)
                        {
                            var entities = ReadStructs<Entity>(File.ReadAllBytes((string)resource["path"]));
                            var buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, entities.Length, 128);
                            bindings.Add(buffer); buffer.SetData(entities); material.SetBuffer(name, buffer);
                        }
                        else material.SetTexture(name, textures[(string)resource["path"]]);
                    }
                    int pass = material.FindPass(sequential ? "DRAW_" + row["relative"] : "PS_" + (string)row["ps"]);
                    if (pass < 0) throw new Exception("Missing UI stage pass");
                    cmd.SetRenderTarget(ids, new RenderTargetIdentifier(depth));
                    cmd.SetViewport(new Rect(0, 0, width, height));
                    if (sequential)
                    {
                        if ((int)row["relative"] == 0) cmd.ClearRenderTarget(RTClearFlags.All, Color.clear, 1, 0);
                    }
                    else cmd.ClearRenderTarget(true, true, new Color(-65504, -65504, -65504, -65504), 1);
                    cmd.SetInvertCulling(!sequential);
                    cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
                    cmd.DrawMesh(mesh, Matrix4x4.identity, material, 0, pass);
                    cmd.SetInvertCulling(false);
                    cmd.SetRenderTarget(depthRead);
                    cmd.DrawProcedural(Matrix4x4.identity, depthMaterial, 0, MeshTopology.Triangles, 3);
                    Graphics.ExecuteCommandBuffer(cmd);

                    if (sequential)
                    {
                        string id = (string)row["id"];
                        results.Add(SaveSequenceReadback(targets, depthRead, output + "/" + id, id, width, height));
                        Debug.Log("UNITY_UI_SEQUENCE_READBACK " + id);
                        continue;
                    }

                    using (var depthValues = ReadTexture<float>(depthRead, width * height))
                    {
                    if (depthValues.Length != width * height) throw new Exception("Depth readback size mismatch");
                    var pixels = new List<uint>();
                    for (int p = 0; p < depthValues.Length; p++) if (depthValues[p] != 0) pixels.Add((uint)p);
                    var valuesOut = new float[pixels.Count * 17];
                    for (int p = 0; p < pixels.Count; p++) valuesOut[p * 17 + 16] = depthValues[(int)pixels[p]];
                    for (int rt = 0; rt < 4; rt++)
                    {
                        using (var values = ReadTexture<float>(targets[rt], width * height * 4))
                        {
                        if (values.Length != width * height * 4) throw new Exception("Color readback size mismatch");
                        for (int p = 0; p < pixels.Count; p++) for (int ch = 0; ch < 4; ch++)
                            valuesOut[p * 17 + rt * 4 + ch] = values[(int)pixels[p] * 4 + ch];
                        }
                    }
                    string stem = output + "/" + (string)row["id"];
                    var bytes = new byte[valuesOut.Length * 4];
                    Buffer.BlockCopy(valuesOut, 0, bytes, 0, bytes.Length); File.WriteAllBytes(stem + ".f32", bytes);
                    var indices = pixels.ToArray(); bytes = new byte[indices.Length * 4];
                    Buffer.BlockCopy(indices, 0, bytes, 0, bytes.Length); File.WriteAllBytes(stem + ".u32", bytes);
                    results.Add(new JObject { ["id"] = (string)row["id"], ["pixels"] = pixels.Count,
                        ["output"] = stem + ".f32", ["outputSha256"] = Sha(stem + ".f32"),
                        ["indices"] = stem + ".u32", ["indicesSha256"] = Sha(stem + ".u32") });
                    Debug.Log("UNITY_UI_STAGE_READBACK " + row["id"] + " pixels=" + pixels.Count);
                    }
                }
                finally
                {
                    cmd.Release(); foreach (var buffer in bindings) buffer.Dispose();
                    UnityEngine.Object.DestroyImmediate(material); UnityEngine.Object.DestroyImmediate(mesh);
                }
            }
            foreach (var candidate in new[] { shader, depthShader }) foreach (var message in ShaderUtil.GetShaderMessages(candidate))
            {
                messages.Add(new JObject { ["severity"] = message.severity.ToString(), ["message"] = message.message, ["line"] = message.line });
                if (message.severity.ToString() == "Error") throw new Exception(message.message);
            }
            var files = new JArray(generated["files"].Select(x => x.DeepClone()));
            string script = "Assets/Editor/RemielleNativeUISequenceGpuAudit.cs";
            files.Add(new JObject { ["path"] = Path.GetFullPath(script), ["sha256"] = Sha(script) });
            foreach (var row in manifest["textures"])
            {
                string asset = (string)row["asset"];
                if (asset != null) files.Add(new JObject { ["path"] = Path.GetFullPath(asset), ["sha256"] = Sha(asset) });
            }
            File.WriteAllText(Root + (sequential ? "unity-sequence-readback.json" : "unity-readback.json"), new JObject {
                ["schema"] = sequential ? "remielle-ui-captured-sequence-unity-readback-v1" : "remielle-ui-all-stage-unity-readback-v1", ["device"] = SystemInfo.graphicsDeviceName,
                ["api"] = SystemInfo.graphicsDeviceType.ToString(), ["reversedZ"] = SystemInfo.usesReversedZBuffer,
                ["manifestSha256"] = Sha(Root + "manifest.json"), ["draws"] = results,
                ["implementationFiles"] = files, ["shaderMessages"] = messages,
                ["boundary"] = sequential ? "Ordered 24-draw chain per captured page with original formats, depth/stencil, blend and output-derived sampler behavior. Fixed captured geometry/constants; not live model integration or original descriptor recovery."
                    : "All 48 isolated stages with explicit sampler fixtures; 46 have original DXBC. Two assembly reconstructions require independent game-output validation. Cull, depth-write, blend and stencil here are fixture states, not full captured sequencing."
            }.ToString());
        }
        finally
        {
            QualitySettings.anisotropicFiltering = oldAnisotropicFiltering;
            foreach (var buffer in buffers) { buffer.Release(); UnityEngine.Object.DestroyImmediate(buffer); }
            foreach (var obj in owned) UnityEngine.Object.DestroyImmediate(obj);
        }
    }
}
