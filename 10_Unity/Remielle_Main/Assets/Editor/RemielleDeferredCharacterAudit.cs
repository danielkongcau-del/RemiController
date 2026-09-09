using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

public static class RemielleDeferredCharacterAudit
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
        if (values.Count % 4 != 0) throw new Exception("Character deferred CB is not float4 aligned");
        var rows = new Vector4[values.Count / 4];
        for (int i = 0; i < rows.Length; i++)
            rows[i] = new Vector4((float)values[i * 4], (float)values[i * 4 + 1],
                (float)values[i * 4 + 2], (float)values[i * 4 + 3]);
        return rows;
    }

    static Color[] Read(RenderTexture source, Texture2D pixels)
    {
        var staging = new RenderTexture(source.width, source.height, 0,
            RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var previous = RenderTexture.active;
        try
        {
            staging.Create();
            Graphics.Blit(source, staging);
            RenderTexture.active = staging;
            pixels.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            pixels.Apply();
            return pixels.GetPixels();
        }
        finally
        {
            RenderTexture.active = previous;
            staging.Release();
            UnityEngine.Object.DestroyImmediate(staging);
        }
    }

    static float HalfQuantum(float value)
    {
        float magnitude = Mathf.Abs(value);
        if (magnitude < Mathf.Pow(2f, -14f)) return Mathf.Pow(2f, -24f);
        return Mathf.Pow(2f, Mathf.Floor(Mathf.Log(magnitude, 2f)) - 10f);
    }

    static JObject RunCase(JObject data, Material material, string label, bool savePreview)
    {
        int width = (int)data["width"], height = (int)data["height"];
        byte[] mask = File.ReadAllBytes((string)data["writtenMask"]["path"]);
        if (mask.Length != width * height) throw new Exception(label + " ownership mask size mismatch");

        var inputs = new List<Texture2D>();
        var expected0 = Load(data["expected"][0]);
        var expected1 = Load(data["expected"][1]);
        string target0Kind = (string)data["target0Kind"] ?? "packedR11";
        int target0Channels = (int?)data["target0Channels"] ?? 3;
        var target0 = new RenderTexture(width, height, 24,
            target0Kind == "rgbaHalf" ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.RGB111110Float,
            RenderTextureReadWrite.Linear);
        var target1 = new RenderTexture(new RenderTextureDescriptor(
            width, height, GraphicsFormat.A2B10G10R10_UNormPack32, GraphicsFormat.None));
        var pixels0 = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
        var pixels1 = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
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
            target0.Create();
            target1.Create();
            Graphics.SetRenderTarget(new[] { target0.colorBuffer, target1.colorBuffer }, target0.depthBuffer);
            GL.sRGBWrite = false;
            GL.Clear(true, true, Color.clear);
            if (!material.SetPass(0)) throw new Exception(label + " deferred character pass unavailable");
            Graphics.DrawProceduralNow(MeshTopology.Triangles, 3);

            Color[] actual0 = Read(target0, pixels0);
            Color[] actual1 = Read(target1, pixels1);
            Color[] truth0 = expected0.GetPixels();
            Color[] truth1 = expected1.GetPixels();
            float maxError0 = 0, maxUnits0 = 0, maxError1 = 0, maxUnits1 = 0;
            double sumError0 = 0, sumError1 = 0;
            int values0 = 0, values1 = 0, selectedPixels = 0, outliers0 = 0, outliers1 = 0;
            for (int i = 0; i < mask.Length; i++)
            {
                if (mask[i] == 0) continue;
                selectedPixels++;
                for (int channel = 0; channel < target0Channels; channel++)
                {
                    float a = actual0[i][channel], e = truth0[i][channel];
                    if (!float.IsFinite(a)) throw new Exception("Nonfinite " + label + " HDR output");
                    float error = Mathf.Abs(e - a);
                    float quantum = target0Kind == "rgbaHalf"
                        ? HalfQuantum(e)
                        : Mathf.Pow(2, Mathf.Max(-14,
                            Mathf.Floor(Mathf.Log(Mathf.Max(e, 1e-10f), 2)) - (channel == 2 ? 5 : 6)));
                    float units = error / quantum;
                    maxError0 = Mathf.Max(maxError0, error);
                    maxUnits0 = Mathf.Max(maxUnits0, units);
                    sumError0 += error;
                    values0++;
                    if (units > (target0Kind == "rgbaHalf" ? 1.1f : 2.1f)) outliers0++;
                }
                for (int channel = 0; channel < 4; channel++)
                {
                    float a = actual1[i][channel], e = truth1[i][channel];
                    if (!float.IsFinite(a)) throw new Exception("Nonfinite " + label + " auxiliary output");
                    float error = Mathf.Abs(e - a);
                    float quantum = channel == 3 ? 1f / 3f : 1f / 1023f;
                    float units = error / quantum;
                    maxError1 = Mathf.Max(maxError1, error);
                    maxUnits1 = Mathf.Max(maxUnits1, units);
                    sumError1 += error;
                    values1++;
                    if (units > 1.1f) outliers1++;
                }
            }
            bool pass = selectedPixels == (int)data["writtenPixels"] && outliers0 == 0 && outliers1 == 0;
            var report = new JObject
            {
                ["pass"] = pass,
                ["key"] = label,
                ["call"] = data["call"],
                ["baseCall"] = data["baseCall"],
                ["selectedPixels"] = selectedPixels,
                ["hdr"] = new JObject {
                ["nativeFormat"] = (string)data["target0NativeFormat"] ?? "R11G11B10_FLOAT",
                    ["values"] = values0, ["maxAbsoluteError"] = maxError0,
                    ["meanAbsoluteError"] = values0 == 0 ? 0 : sumError0 / values0,
                    ["maxNativeFormatUnits"] = maxUnits0, ["outliers"] = outliers0
                },
                ["auxiliary"] = new JObject {
                    ["values"] = values1, ["maxAbsoluteError"] = maxError1,
                    ["meanAbsoluteError"] = values1 == 0 ? 0 : sumError1 / values1,
                    ["maxNativeFormatUnits"] = maxUnits1, ["outliers"] = outliers1
                },
                ["scope"] = data["scope"]
            };

            if (savePreview)
            {
                var composite = (Color[])actual0.Clone();
                for (int i = 0; i < composite.Length; i++) if (mask[i] == 0) composite[i] = truth0[i];
                pixels0.SetPixels(composite);
                pixels0.Apply();
                string prefix = label == "battle"
                    ? "deferred-character"
                    : "deferred-character-ui-" + label;
                File.WriteAllBytes(Out + "/" + prefix + "-native-replay.png", pixels0.EncodeToPNG());
                File.WriteAllBytes(Out + "/" + prefix + "-native-truth.png", expected0.EncodeToPNG());
            }
            if (!pass) throw new Exception("Captured " + label + " deferred replay mismatch: " + report);
            return report;
        }
        finally
        {
            RenderTexture.active = previous;
            GL.sRGBWrite = oldSrgb;
            foreach (var texture in inputs) UnityEngine.Object.DestroyImmediate(texture);
            UnityEngine.Object.DestroyImmediate(expected0);
            UnityEngine.Object.DestroyImmediate(expected1);
            target0.Release();
            target1.Release();
            UnityEngine.Object.DestroyImmediate(target0);
            UnityEngine.Object.DestroyImmediate(target1);
            UnityEngine.Object.DestroyImmediate(pixels0);
            UnityEngine.Object.DestroyImmediate(pixels1);
        }
    }

    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        AssetDatabase.Refresh();
        var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/CapturedDeferredCharacter523.shader");
        if (!shader || !shader.isSupported || ShaderUtil.ShaderHasError(shader))
            throw new Exception("Captured deferred character shader unavailable");
        var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            var battle = JObject.Parse(File.ReadAllText(Out + "/captured-deferred-character.json"));
            JObject battleReport = RunCase(battle, material, "battle", true);
            var ui = JObject.Parse(File.ReadAllText(Out + "/captured-deferred-character-ui.json"));
            var uiReports = new JArray();
            foreach (JObject item in (JArray)ui["cases"])
                uiReports.Add(RunCase(item, material, (string)item["key"], true));

            bool pass = (bool)battleReport["pass"];
            foreach (JObject item in uiReports) pass &= (bool)item["pass"];
            var report = new JObject
            {
                ["pass"] = pass,
                ["utc"] = DateTime.UtcNow.ToString("O"),
                ["device"] = SystemInfo.graphicsDeviceName,
                ["call"] = battleReport["call"],
                ["baseCall"] = battleReport["baseCall"],
                ["selectedPixels"] = battleReport["selectedPixels"],
                ["hdr"] = battleReport["hdr"],
                ["auxiliary"] = battleReport["auxiliary"],
                ["scope"] = battleReport["scope"],
                ["uiCases"] = uiReports,
                ["summary"] = new JObject {
                    ["caseCount"] = 1 + uiReports.Count,
                    ["battlePixels"] = battleReport["selectedPixels"],
                    ["uiPixels"] = (int)uiReports[0]["selectedPixels"] + (int)uiReports[1]["selectedPixels"]
                }
            };
            File.WriteAllText(Out + "/deferred-character-gpu-verification.json", report.ToString());
            if (!pass) throw new Exception("Deferred character replay suite failed: " + report);
            Debug.Log("REMIELLE_DEFERRED_CHARACTER_GPU_VERIFIED cases=" + (1 + uiReports.Count));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(material);
        }
    }
}
