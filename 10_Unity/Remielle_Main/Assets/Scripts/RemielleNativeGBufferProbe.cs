using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

// Produces the recovered native attachment layout from the live skinned model.
// It is review-only and stays off-screen: the visible camera still uses the
// validated forward HoyoToon path and captured post-processing.
[DefaultExecutionOrder(500)]
[RequireComponent(typeof(Camera))]
public class RemielleNativeGBufferProbe : MonoBehaviour
{
    public Shader adapterShader;
    public RemielleNativeShadowProbe shadowProbe;
    public Transform model;
    public Light keyLight;
    public bool liveUpdate = true;
    public int maxLiveDimension = 960;
    public int draws;
    public int motionRenderers;
    public long motionHistoryFrames;
    public int lastWidth, lastHeight;
    public string status;

    public RenderTexture Primary => primary;
    public RenderTexture Auxiliary => auxiliary;
    public RenderTexture MotionFlagsClass => motionFlagsClass;
    public RenderTexture WorldNormal => worldNormal;
    public RenderTexture DepthStencil => depthStencil;
    public RenderTexture DepthStencilReadback => depthStencilReadback;
    public RenderTexture ShadowDiagnostic => shadowDiagnostic;

    Camera cameraComponent;
    SkinnedMeshRenderer[] renderers;
    CommandBuffer writeCommands, readCommands;
    Material readMaterial;
    readonly List<Material> slots = new List<Material>();
    sealed class MotionState
    {
        public Mesh baked;
        public readonly List<Vector3> current = new List<Vector3>();
        public readonly List<Vector3> previous = new List<Vector3>();
        public GraphicsBuffer previousBuffer;
        public Matrix4x4 previousLocalToWorld;
        public bool initialized;
    }
    readonly Dictionary<long, Material> adapters = new Dictionary<long, Material>();
    readonly Dictionary<SkinnedMeshRenderer, MotionState> motionStates = new Dictionary<SkinnedMeshRenderer, MotionState>();
    readonly List<MotionState> renderedMotionStates = new List<MotionState>();
    Matrix4x4 previousVP;
    bool previousVPValid;
    RenderTexture primary, auxiliary, motionFlagsClass, worldNormal, depthStencil, depthStencilReadback,shadowDiagnostic;
    int writePass = -1, readPass = -1;

    static RenderTexture ColorTarget(string name, int width, int height, GraphicsFormat format)
    {
        var descriptor = new RenderTextureDescriptor(width, height, format, GraphicsFormat.None)
        {
            msaaSamples = 1,
            useMipMap = false,
            autoGenerateMips = false
        };
        var target = new RenderTexture(descriptor)
        {
            name = name,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        if (!target.Create()) throw new InvalidOperationException("Could not create " + name + " / " + format);
        return target;
    }

    static RenderTexture DepthTarget(int width, int height)
    {
        var descriptor = new RenderTextureDescriptor(width, height, GraphicsFormat.None, GraphicsFormat.D32_SFloat_S8_UInt)
        {
            msaaSamples = 1,
            useMipMap = false,
            autoGenerateMips = false,
            stencilFormat = GraphicsFormat.R8_UInt
        };
        var target = new RenderTexture(descriptor)
        {
            name = "Remielle live native D32S8",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        if (!target.Create()) throw new InvalidOperationException("Could not create live D32S8");
        return target;
    }

    void Prepare()
    {
        if (writeCommands != null) return;
        cameraComponent = GetComponent<Camera>();
        if (!adapterShader || !adapterShader.isSupported || !model || !keyLight)
            throw new InvalidOperationException("Native G-buffer probe references missing");
        if (!shadowProbe)throw new InvalidOperationException("Native shadow producer missing");
        if (SystemInfo.supportedRenderTargetCount < 5)
            throw new InvalidOperationException("Five simultaneous review render targets are unavailable");
        renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        readMaterial = new Material(adapterShader) { hideFlags = HideFlags.HideAndDontSave };
        writePass = readMaterial.FindPass("Review Native GBuffer");
        readPass = readMaterial.FindPass("Review Native Depth Stencil Readback");
        if (writePass < 0 || readPass < 0) throw new InvalidOperationException("Native G-buffer adapter passes missing");
        writeCommands = new CommandBuffer { name = "Remielle live native G-buffer" };
        readCommands = new CommandBuffer { name = "Remielle live native depth/stencil readback" };
    }

    void ReleaseTargets()
    {
        Release(ref primary); Release(ref auxiliary); Release(ref motionFlagsClass);
        Release(ref worldNormal); Release(ref depthStencil); Release(ref depthStencilReadback);
        Release(ref shadowDiagnostic);
        lastWidth = lastHeight = 0;
    }

    static void Release(ref RenderTexture target)
    {
        if (!target) return;
        target.Release();
        if (Application.isPlaying) Destroy(target); else DestroyImmediate(target);
        target = null;
    }

    void EnsureTargets(int width, int height)
    {
        if (primary && lastWidth == width && lastHeight == height) return;
        ReleaseTargets();
        primary = ColorTarget("Remielle live native GBuffer0", width, height, GraphicsFormat.R16G16B16A16_SFloat);
        auxiliary = ColorTarget("Remielle live native GBuffer1", width, height, GraphicsFormat.R8G8B8A8_SRGB);
        motionFlagsClass = ColorTarget("Remielle live native GBuffer2", width, height, GraphicsFormat.A2B10G10R10_UNormPack32);
        worldNormal = ColorTarget("Remielle live native GBuffer3", width, height, GraphicsFormat.A2B10G10R10_UNormPack32);
        depthStencil = DepthTarget(width, height);
        depthStencilReadback = ColorTarget("Remielle live native depth/stencil readback", width, height, GraphicsFormat.R16G16B16A16_SFloat);
        shadowDiagnostic = ColorTarget("Remielle live native shadow diagnostic",width,height,GraphicsFormat.R8G8B8A8_UNorm);
        lastWidth = width; lastHeight = height;
    }

    MotionState GetMotionState(SkinnedMeshRenderer renderer)
    {
        if (!motionStates.TryGetValue(renderer, out var state))
        {
            int count=renderer.sharedMesh.vertexCount;
            state=new MotionState
            {
                baked=new Mesh{name=renderer.name+" live motion snapshot",hideFlags=HideFlags.HideAndDontSave},
                previousBuffer=new GraphicsBuffer(GraphicsBuffer.Target.Structured,count,12)
            };
            state.current.Capacity=count;state.previous.Capacity=count;
            motionStates.Add(renderer,state);
        }
        return state;
    }

    Material Adapter(SkinnedMeshRenderer renderer, Material source, MotionState state, Matrix4x4 vp)
    {
        long key=((long)renderer.GetInstanceID()<<32)^(uint)source.GetInstanceID();
        if (!adapters.TryGetValue(key, out var adapter))
        {
            adapter = new Material(adapterShader) { name = renderer.name+" / "+source.name+" (native G-buffer adapter)", hideFlags = HideFlags.HideAndDontSave };
            adapters.Add(key, adapter);
        }
        adapter.CopyPropertiesFromMaterial(source);
        float type = source.HasProperty("_MaterialType") ? source.GetFloat("_MaterialType") : 0;
        bool eyeOrBrow = type == 2 || source.name.IndexOf("Eyebrow", StringComparison.OrdinalIgnoreCase) >= 0;
        bool body = source.name.IndexOf("Origin_Body_", StringComparison.OrdinalIgnoreCase) >= 0;
        adapter.SetInt("_NativeStencilRef", eyeOrBrow ? 144 : 128);
        adapter.SetFloat("_NativeBodySkinClass", body ? 1 : 0);
        // CommandBuffer.SetViewProjectionMatrices already supplies Unity's GPU projection
        // convention for this target.  Keep the empirically verified offscreen depth state;
        // the shadow-map pass has its own explicit reversed-Z comparison.
        adapter.SetInt("_NativeZTest",(int)CompareFunction.LessEqual);
        adapter.SetBuffer("_ReviewPreviousPositions",state.previousBuffer);
        adapter.SetMatrix("_ReviewPreviousObjectToWorld",state.initialized?state.previousLocalToWorld:renderer.localToWorldMatrix);
        adapter.SetMatrix("_ReviewPreviousGBufferVP",previousVPValid?previousVP:vp);
        adapter.SetFloat("_ReviewMotionEnabled",state.initialized&&previousVPValid?1:0);
        shadowProbe.ApplyTo(adapter);
        return adapter;
    }

    public void RenderNow(int width, int height)
    {
        Prepare();
        shadowProbe.RenderNow();
        width = Mathf.Max(8, width); height = Mathf.Max(8, height);
        EnsureTargets(width, height);

        var gpuProjection = GL.GetGPUProjectionMatrix(cameraComponent.projectionMatrix, true);
        var view = cameraComponent.worldToCameraMatrix;
        var vp = gpuProjection * view;
        Vector3 lightDirection = -keyLight.transform.forward;
        Color lightColor = keyLight.color.linear * keyLight.intensity;
        var colors = new RenderTargetIdentifier[] { primary, auxiliary, motionFlagsClass, worldNormal,shadowDiagnostic };

        writeCommands.Clear();
        writeCommands.SetViewProjectionMatrices(view, gpuProjection);
        writeCommands.SetRenderTarget(colors, depthStencil);
        writeCommands.ClearRenderTarget(RTClearFlags.All, Color.clear, 1.0f, 0);
        writeCommands.SetGlobalMatrix("_ReviewNativeGBufferVP", vp);
        writeCommands.SetGlobalVector("_WorldSpaceCameraPos", cameraComponent.transform.position);
        writeCommands.SetGlobalVector("_WorldSpaceLightPos0", new Vector4(lightDirection.x, lightDirection.y, lightDirection.z, 0));
        writeCommands.SetGlobalColor("_LightColor0", lightColor);
        draws = 0;motionRenderers=0;renderedMotionStates.Clear();
        foreach (var renderer in renderers)
        {
            if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy || !renderer.sharedMesh) continue;
            var state=GetMotionState(renderer);
            renderer.BakeMesh(state.baked,false);
            state.current.Clear();state.baked.GetVertices(state.current);
            if(state.current.Count!=renderer.sharedMesh.vertexCount)
                throw new InvalidOperationException("Native motion vertex count mismatch: "+renderer.name+" / "+state.current.Count+" / "+renderer.sharedMesh.vertexCount);
            if(!state.initialized){state.previous.Clear();state.previous.AddRange(state.current);state.previousLocalToWorld=renderer.localToWorldMatrix;}
            state.previousBuffer.SetData(state.previous);
            if(state.initialized&&previousVPValid)motionRenderers++;
            renderedMotionStates.Add(state);
            renderer.GetSharedMaterials(slots);
            if (slots.Count != renderer.sharedMesh.subMeshCount)
                throw new InvalidOperationException("Native G-buffer material/submesh count mismatch: " + renderer.name);
            for (int i = 0; i < slots.Count; i++)
            {
                var source = slots[i];
                if (!source) throw new InvalidOperationException("Missing live native G-buffer material: " + renderer.name);
                if (source.HasProperty("_MaterialType") && source.GetFloat("_MaterialType") == 3) continue;
                if (source.HasProperty("_ScreenImage") && source.GetFloat("_ScreenImage") > 0.5f)
                    throw new InvalidOperationException("Native screen-image input is not recovered: " + source.name);
                writeCommands.DrawRenderer(renderer, Adapter(renderer,source,state,vp), i, writePass);
                draws++;
            }
        }
        Graphics.ExecuteCommandBuffer(writeCommands);
        foreach(var pair in motionStates)
        {
            var state=pair.Value;
            if(!renderedMotionStates.Contains(state))continue;
            state.previous.Clear();state.previous.AddRange(state.current);
            state.previousLocalToWorld=pair.Key.localToWorldMatrix;state.initialized=true;
        }
        previousVP=vp;previousVPValid=true;motionHistoryFrames++;

        readCommands.Clear();
        var depthId = new RenderTargetIdentifier(depthStencil);
        readCommands.SetGlobalTexture("_ReviewNativeDepthSurface", depthId, RenderTextureSubElement.Depth);
        readCommands.SetGlobalTexture("_ReviewNativeStencilSurface", depthId, RenderTextureSubElement.Stencil);
        readCommands.SetRenderTarget(depthStencilReadback);
        readCommands.ClearRenderTarget(false, true, Color.clear);
        readCommands.DrawProcedural(Matrix4x4.identity, readMaterial, readPass, MeshTopology.Triangles, 3);
        Graphics.ExecuteCommandBuffer(readCommands);
        status = "MRT " + width + "x" + height + ", draws " + draws + ", motion renderers " + motionRenderers;
    }

    void LateUpdate()
    {
        if (!liveUpdate || !isActiveAndEnabled) return;
        int width = Mathf.Max(8, Screen.width), height = Mathf.Max(8, Screen.height);
        int largest = Mathf.Max(width, height);
        if (largest > maxLiveDimension)
        {
            float scale = maxLiveDimension / (float)largest;
            width = Mathf.Max(8, Mathf.RoundToInt(width * scale));
            height = Mathf.Max(8, Mathf.RoundToInt(height * scale));
        }
        RenderNow(width, height);
    }

    void OnDisable() { ReleaseTargets(); }
    void OnDestroy()
    {
        ReleaseTargets();
        writeCommands?.Release(); readCommands?.Release();
        foreach (var material in adapters.Values)
            if (material) { if (Application.isPlaying) Destroy(material); else DestroyImmediate(material); }
        adapters.Clear();
        foreach(var state in motionStates.Values)
        {
            state.previousBuffer?.Dispose();
            if(state.baked){if(Application.isPlaying)Destroy(state.baked);else DestroyImmediate(state.baked);}
        }
        motionStates.Clear();
        if (readMaterial) { if (Application.isPlaying) Destroy(readMaterial); else DestroyImmediate(readMaterial); }
    }
}
