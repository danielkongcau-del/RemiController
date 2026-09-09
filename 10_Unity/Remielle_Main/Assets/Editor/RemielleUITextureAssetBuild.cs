using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

// Only the source-identified UI MatCap texture array is written by this builder.
public static class RemielleUITextureAssetBuild
{
    public static void Run()
    {
        const string root = "E:/ZZZ/local-only/RemielleRenderingReview/20260905/ui-full-sequence/";
        var report = JObject.Parse(File.ReadAllText(root + "matcap-array-completion.json"));
        string path = (string)report["fullDDS"]["path"], asset = (string)report["asset"];
        var data = File.ReadAllBytes(path);
        using (var hash = System.Security.Cryptography.SHA256.Create())
            if (BitConverter.ToString(hash.ComputeHash(data)).Replace("-", "").ToLowerInvariant() != (string)report["fullDDS"]["sha256"])
                throw new Exception("UI MatCap source hash mismatch");
        if (BitConverter.ToUInt32(data, 12) != 256 || BitConverter.ToUInt32(data, 16) != 256 ||
            BitConverter.ToUInt32(data, 28) != 5 || BitConverter.ToUInt32(data, 128) != 99 || BitConverter.ToUInt32(data, 140) != 8)
            throw new Exception("Unexpected UI MatCap DDS layout");
        var texture = new Texture2DArray(256, 256, 8, TextureFormat.BC7, 5, false) { name = "UI_MatcapArray", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
        int offset = 148;
        for (int layer = 0; layer < 8; layer++) for (int mip = 0; mip < 5; mip++)
        {
            int edge = Math.Max(1, 256 >> mip), size = Math.Max(1, (edge + 3) / 4) * Math.Max(1, (edge + 3) / 4) * 16;
            if (offset + size > data.Length) throw new Exception("UI MatCap mip chain truncated");
            texture.SetPixelData(data, mip, layer, offset);
            offset += size;
        }
        if (offset != data.Length) throw new Exception("Unexpected UI MatCap trailing bytes");
        texture.Apply(false, false);
        Directory.CreateDirectory(Path.GetDirectoryName(asset));
        AssetDatabase.Refresh();
        var existing = AssetDatabase.LoadAssetAtPath<Texture2DArray>(asset);
        if (existing)
        {
            EditorUtility.CopySerialized(texture, existing);
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(texture);
        }
        else AssetDatabase.CreateAsset(texture, asset);
        AssetDatabase.SaveAssets();
        Debug.Log("REMIELLE_UI_MATCAP_ASSET_READY " + asset);
    }
}
