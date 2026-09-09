using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

// Verifies that the active Unity/D3D11 runtime can create and bind the exact
// native material MRT formats and can expose depth and stencil as shader inputs.
// It validates the transport layer only; it does not claim that live native
// material payload formulas or scene auxiliary producers are implemented.
public static class RemielleNativeMrtCapabilityAudit
{
    const int Size = 64;
    const string ShaderPath = "Assets/Shaders/RemielleNativeMrtCapability.shader";

    static RenderTexture ColorTarget(string name, GraphicsFormat format)
    {
        var descriptor = new RenderTextureDescriptor(Size, Size, format, GraphicsFormat.None)
        {
            msaaSamples = 1,
            useMipMap = false,
            autoGenerateMips = false,
            depthBufferBits = 0
        };
        var target = new RenderTexture(descriptor) { name = name };
        if (!target.Create()) throw new Exception("Could not create " + name + " / " + format);
        return target;
    }

    static RenderTexture DepthStencilTarget()
    {
        var descriptor = new RenderTextureDescriptor(
            Size, Size, GraphicsFormat.None, GraphicsFormat.D32_SFloat_S8_UInt)
        {
            msaaSamples = 1,
            useMipMap = false,
            autoGenerateMips = false,
            stencilFormat = GraphicsFormat.R8_UInt
        };
        var target = new RenderTexture(descriptor) { name = "Native D32S8" };
        if (!target.Create()) throw new Exception("Could not create D32_SFloat_S8_UInt");
        return target;
    }

    static Color ReadCenter(RenderTexture source)
    {
        var staging = new RenderTexture(Size, Size, 0, RenderTextureFormat.ARGBFloat,
            RenderTextureReadWrite.Linear);
        var texture = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true);
        var previous = RenderTexture.active;
        try
        {
            staging.Create();
            Graphics.Blit(source, staging);
            RenderTexture.active = staging;
            texture.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            texture.Apply();
            return texture.GetPixel(Size / 2, Size / 2);
        }
        finally
        {
            RenderTexture.active = previous;
            staging.Release();
            UnityEngine.Object.DestroyImmediate(staging);
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    static JArray JsonColor(Color value) => new JArray(value.r, value.g, value.b, value.a);

    static float MaxError(Color actual, Color expected)
    {
        return Mathf.Max(Mathf.Abs(actual.r - expected.r), Mathf.Abs(actual.g - expected.g),
            Mathf.Abs(actual.b - expected.b), Mathf.Abs(actual.a - expected.a));
    }

    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        AssetDatabase.Refresh();
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        if (!shader || !shader.isSupported || ShaderUtil.ShaderHasError(shader))
            throw new Exception("Native MRT capability shader unavailable");
        if (SystemInfo.supportedRenderTargetCount < 4)
            throw new Exception("Four simultaneous render targets are unavailable");

        var formats = new[] {
            GraphicsFormat.R16G16B16A16_SFloat,
            GraphicsFormat.R8G8B8A8_SRGB,
            GraphicsFormat.A2B10G10R10_UNormPack32,
            GraphicsFormat.A2B10G10R10_UNormPack32
        };
        var expected = new[] {
            new Color(0.125f, 0.25f, 0.5f, 0.75f),
            new Color(0.2f, 0.4f, 0.6f, 0.8f),
            // Native material shaders write 0.34 here; the two-bit alpha
            // channel stores the nearest representable value, exactly 1/3.
            new Color(0.1f, 0.3f, 0.7f, 1f / 3f),
            new Color(0.25f, 0.5f, 0.75f, 1.0f)
        };
        var colors = new RenderTexture[4];
        RenderTexture depth = null, depthStencilRead = null;
        Material material = null;
        CommandBuffer write = null, read = null;
        try
        {
            for (int i = 0; i < colors.Length; i++)
                colors[i] = ColorTarget("Native MRT " + i, formats[i]);
            depth = DepthStencilTarget();
            depthStencilRead = ColorTarget("Depth stencil readback", GraphicsFormat.R16G16B16A16_SFloat);
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };

            var colorIds = new RenderTargetIdentifier[4];
            for (int i = 0; i < colors.Length; i++) colorIds[i] = colors[i];
            write = new CommandBuffer { name = "Remielle native MRT capability write" };
            write.SetRenderTarget(colorIds, depth);
            write.ClearRenderTarget(RTClearFlags.All, Color.clear, 1.0f, 0);
            write.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3);
            Graphics.ExecuteCommandBuffer(write);

            read = new CommandBuffer { name = "Remielle native depth stencil capability read" };
            var depthId = new RenderTargetIdentifier(depth);
            read.SetGlobalTexture("_DepthSurface", depthId, RenderTextureSubElement.Depth);
            read.SetGlobalTexture("_StencilSurface", depthId, RenderTextureSubElement.Stencil);
            read.SetRenderTarget(depthStencilRead);
            read.ClearRenderTarget(false, true, Color.clear);
            read.DrawProcedural(Matrix4x4.identity, material, 1, MeshTopology.Triangles, 3);
            Graphics.ExecuteCommandBuffer(read);

            var samples = new JArray();
            bool valuesPass = true;
            for (int i = 0; i < colors.Length; i++)
            {
                Color value = ReadCenter(colors[i]);
                float tolerance = i == 0 ? 0.001f : i == 1 ? 0.006f : 0.003f;
                float error = MaxError(value, expected[i]);
                valuesPass &= error <= tolerance;
                samples.Add(new JObject {
                    ["slot"] = i,
                    ["requestedFormat"] = formats[i].ToString(),
                    ["actualFormat"] = colors[i].graphicsFormat.ToString(),
                    ["sample"] = JsonColor(value),
                    ["expected"] = JsonColor(expected[i]),
                    ["maxAbsoluteError"] = error,
                    ["tolerance"] = tolerance
                });
            }
            Color ds = ReadCenter(depthStencilRead);
            bool formatsPass = colors[0].graphicsFormat == formats[0]
                && colors[1].graphicsFormat == formats[1]
                && colors[2].graphicsFormat == formats[2]
                && colors[3].graphicsFormat == formats[3]
                && depth.depthStencilFormat == GraphicsFormat.D32_SFloat_S8_UInt
                && depth.stencilFormat == GraphicsFormat.R8_UInt;
            bool depthPass = Mathf.Abs(ds.r - 0.25f) <= 0.0001f;
            bool stencilPass = Mathf.Abs(ds.g - 32f / 255f) <= 0.001f;
            bool pass = formatsPass && valuesPass && depthPass && stencilPass;
            var report = new JObject {
                ["pass"] = pass,
                ["utc"] = DateTime.UtcNow.ToString("O"),
                ["device"] = SystemInfo.graphicsDeviceName,
                ["graphicsApi"] = SystemInfo.graphicsDeviceType.ToString(),
                ["supportedRenderTargetCount"] = SystemInfo.supportedRenderTargetCount,
                ["formatCapabilities"] = new JObject {
                    ["packedR10Render"] = SystemInfo.IsFormatSupported(
                        GraphicsFormat.A2B10G10R10_UNormPack32, FormatUsage.Render),
                    ["packedR10Sample"] = SystemInfo.IsFormatSupported(
                        GraphicsFormat.A2B10G10R10_UNormPack32, FormatUsage.Sample),
                    ["packedR11Render"] = SystemInfo.IsFormatSupported(
                        GraphicsFormat.B10G11R11_UFloatPack32, FormatUsage.Render),
                    ["packedR11Sample"] = SystemInfo.IsFormatSupported(
                        GraphicsFormat.B10G11R11_UFloatPack32, FormatUsage.Sample),
                    ["stencilSampling"] = SystemInfo.IsFormatSupported(
                        GraphicsFormat.R8_UInt, FormatUsage.StencilSampling)
                },
                ["formatsPass"] = formatsPass,
                ["valuesPass"] = valuesPass,
                ["targets"] = samples,
                ["depthStencil"] = new JObject {
                    ["actualDepthStencilFormat"] = depth.depthStencilFormat.ToString(),
                    ["actualStencilViewFormat"] = depth.stencilFormat.ToString(),
                    ["sampledDepth"] = ds.r,
                    ["expectedDepth"] = 0.25f,
                    ["sampledStencilNormalized"] = ds.g,
                    ["expectedStencilNormalized"] = 32f / 255f,
                    ["depthPass"] = depthPass,
                    ["stencilPass"] = stencilPass
                },
                ["scope"] = "Transport/capability audit only: exact MRT formats, D32S8 and sampleable stencil. Native material payload formulas and live auxiliary producers are separate work."
            };
            File.WriteAllText(RemielleRenderReviewBuild.Out + "/native-mrt-capability.json", report.ToString());
            if (!pass) throw new Exception("Native MRT capability mismatch: " + report);
            Debug.Log("REMIELLE_NATIVE_MRT_CAPABILITY_VERIFIED");
        }
        finally
        {
            write?.Release();
            read?.Release();
            if (material) UnityEngine.Object.DestroyImmediate(material);
            foreach (var target in colors)
                if (target) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (depth) { depth.Release(); UnityEngine.Object.DestroyImmediate(depth); }
            if (depthStencilRead) { depthStencilRead.Release(); UnityEngine.Object.DestroyImmediate(depthStencilRead); }
        }
    }
}
