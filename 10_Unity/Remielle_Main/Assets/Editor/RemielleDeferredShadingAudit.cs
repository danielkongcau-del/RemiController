using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

public static class RemielleDeferredShadingAudit
{
    const string Out = RemielleRenderReviewBuild.Out;

    static Texture2D Load(JToken item)
    {
        int width = (int)item["width"];
        int height = (int)item["height"];
        string kind = (string)item["kind"];
        bool linear = (bool)item["linear"];
        if (kind == "packedR10" || kind == "packedR11")
        {
            GraphicsFormat graphicsFormat = kind == "packedR10"
                ? GraphicsFormat.A2B10G10R10_UNormPack32
                : GraphicsFormat.B10G11R11_UFloatPack32;
            var packed = new Texture2D(width, height, graphicsFormat, TextureCreationFlags.None);
            packed.LoadRawTextureData(File.ReadAllBytes((string)item["path"]));
            packed.Apply(false, false);
            packed.wrapMode = TextureWrapMode.Clamp;
            packed.filterMode = FilterMode.Bilinear;
            return packed;
        }
        TextureFormat format = kind == "rgbaHalf" ? TextureFormat.RGBAHalf
            : kind == "rgba32" ? TextureFormat.RGBA32
            : kind == "r8" ? TextureFormat.R8
            : kind == "rFloat" ? TextureFormat.RFloat
            : TextureFormat.RGBAFloat;
        var texture = new Texture2D(width, height, format, false, linear);
        texture.LoadRawTextureData(File.ReadAllBytes((string)item["path"]));
        texture.Apply(false, false);
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        return texture;
    }

    static Vector4[] Vectors(JArray values)
    {
        if (values.Count % 4 != 0) throw new Exception("Deferred CB is not float4 aligned");
        var rows = new Vector4[values.Count / 4];
        for (int i = 0; i < rows.Length; i++)
            rows[i] = new Vector4((float)values[i * 4], (float)values[i * 4 + 1],
                (float)values[i * 4 + 2], (float)values[i * 4 + 3]);
        return rows;
    }

    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        AssetDatabase.Refresh();
        var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/CapturedDeferredShading531.shader");
        if (!shader || !shader.isSupported || ShaderUtil.ShaderHasError(shader))
            throw new Exception("Captured draw 531 shader unavailable");
        var data = JObject.Parse(File.ReadAllText(Out + "/captured-deferred-shading.json"));
        int width = (int)data["width"], height = (int)data["height"];
        byte stencilRef = (byte)data["stencilReference"];
        byte[] stencil = File.ReadAllBytes((string)data["stencilMask"]["path"]);
        if (stencil.Length != width * height) throw new Exception("Stencil fixture size mismatch");

        var material = new Material(shader);
        var inputs = new List<Texture2D>();
        var expected = Load(data["expected"]);
        var target = new RenderTexture(width, height, 0, RenderTextureFormat.RGB111110Float,
            RenderTextureReadWrite.Linear);
        var readback = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBFloat,
            RenderTextureReadWrite.Linear);
        var pixels = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
        var previous = RenderTexture.active;
        bool oldSrgb = GL.sRGBWrite;
        try
        {
            foreach (var item in data["inputs"])
            {
                var texture = Load(item);
                inputs.Add(texture);
                material.SetTexture("_T" + (int)item["slot"], texture);
            }
            material.SetVectorArray("P0", Vectors((JArray)data["pixelConstants"]));
            target.Create();
            readback.Create();
            Graphics.SetRenderTarget(target);
            GL.sRGBWrite = false;
            GL.Clear(false, true, Color.clear);
            if (!material.SetPass(0)) throw new Exception("Captured draw 531 pass unavailable");
            Graphics.DrawProceduralNow(MeshTopology.Triangles, 3);
            Graphics.Blit(target, readback);
            RenderTexture.active = readback;
            pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            pixels.Apply();

            Color[] actual = pixels.GetPixels();
            Color[] truth = expected.GetPixels();
            float maxError = 0, maxUnits = 0;
            double sumError = 0;
            int values = 0, selectedPixels = 0, outliers = 0;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                if (stencil[index] != stencilRef) continue;
                selectedPixels++;
                for (int channel = 0; channel < 3; channel++)
                {
                    float a = actual[index][channel], e = truth[index][channel];
                    if (!float.IsFinite(a)) throw new Exception("Nonfinite draw 531 output");
                    float error = Mathf.Abs(e - a);
                    float quantum = Mathf.Pow(2, Mathf.Max(-14,
                        Mathf.Floor(Mathf.Log(Mathf.Max(e, 1e-10f), 2)) - (channel == 2 ? 5 : 6)));
                    float units = error / quantum;
                    maxError = Mathf.Max(maxError, error);
                    maxUnits = Mathf.Max(maxUnits, units);
                    sumError += error;
                    values++;
                    if (units > 2.1f) outliers++;
                }
            }
            bool pass = selectedPixels > 3000000 && outliers == 0;
            var report = new JObject
            {
                ["pass"] = pass,
                ["utc"] = DateTime.UtcNow.ToString("O"),
                ["device"] = SystemInfo.graphicsDeviceName,
                ["call"] = 531,
                ["stencilReference"] = stencilRef,
                ["selectedPixels"] = selectedPixels,
                ["values"] = values,
                ["maxAbsoluteError"] = maxError,
                ["meanAbsoluteError"] = values == 0 ? 0 : sumError / values,
                ["maxNativeFormatUnits"] = maxUnits,
                ["outliers"] = outliers,
                ["scope"] = data["scope"]
            };
            File.WriteAllText(Out + "/deferred-shading-gpu-verification.json", report.ToString());
            // The native draw is stencil restricted.  Outside stencil 32 the
            // standalone shader output is undefined for this audit, so the
            // review PNG keeps the captured target there and replaces only the
            // pixels this draw owns.
            var composite = (Color[])actual.Clone();
            for (int i = 0; i < composite.Length; i++)
                if (stencil[i] != stencilRef) composite[i] = truth[i];
            pixels.SetPixels(composite);
            pixels.Apply();
            File.WriteAllBytes(Out + "/deferred-shading-native-replay.png", pixels.EncodeToPNG());
            File.WriteAllBytes(Out + "/deferred-shading-native-truth.png", expected.EncodeToPNG());
            if (!pass) throw new Exception("Captured draw 531 native replay mismatch: " + report);
            Debug.Log("REMIELLE_DEFERRED_SHADING_GPU_VERIFIED");
        }
        finally
        {
            RenderTexture.active = previous;
            GL.sRGBWrite = oldSrgb;
            foreach (var texture in inputs) UnityEngine.Object.DestroyImmediate(texture);
            UnityEngine.Object.DestroyImmediate(expected);
            target.Release();
            readback.Release();
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(readback);
            UnityEngine.Object.DestroyImmediate(pixels);
            UnityEngine.Object.DestroyImmediate(material);
        }
    }
}
