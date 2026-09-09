using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

// Reconstructed from captured runtime-generated assembly. The four original
// shader binaries are unavailable; captured outputs are a separate oracle.
public sealed class RemielleNativeUIDepthHierarchy : IDisposable
{
    public RenderTexture LinearDepth {get;private set;}
    public RenderTexture NormalAndSample {get;private set;}
    public RenderTexture Range {get;private set;}
    public RenderTexture Depth {get;private set;}
    public int Width=>LinearDepth?LinearDepth.width:0;
    public int Height=>LinearDepth?LinearDepth.height:0;
    readonly Material material;
    readonly CommandBuffer commands=new(){name="Remielle native UI depth hierarchy"};
    readonly Vector4[] current=new Vector4[168];
    RenderTexture sampledDepth,rangeCopy;
    bool disposed;
    public RemielleNativeUIDepthHierarchy(Shader shader)
    {
        if(!shader||!shader.isSupported)throw new ArgumentException("Depth hierarchy shader unavailable");
        material=new Material(shader){hideFlags=HideFlags.HideAndDontSave};
    }
    public void Render(RemielleNativeUIRenderer geometry,Camera camera)
    {
        if(geometry.CurrentFrame==null||!geometry.DepthRead||!camera)throw new ArgumentException("Render current geometry before the depth hierarchy");
        int w=geometry.Width,h=geometry.Height;
        if(!sampledDepth||sampledDepth.width!=w||sampledDepth.height!=h){Release(sampledDepth);sampledDepth=Target(w,h,GraphicsFormat.R32_SFloat);}
        bool srgb=GL.sRGBWrite;
        try
        {
            GL.sRGBWrite=false;Graphics.Blit(geometry.DepthRead,sampledDepth);
            Array.Clear(current,0,current.Length);current[62]=new Vector4(camera.farClipPlane/camera.nearClipPlane-1,1,0,0);
            current[138]=new Vector4(w,h,1f/w,1f/h);current[167]=new Vector4(0,0,Mathf.Max(1,w/2),Mathf.Max(1,h/2));
            Render(sampledDepth,geometry.Targets[3],current);
        }
        finally{GL.sRGBWrite=srgb;}
    }
    public void Render(Texture depthSource,Texture normalSource,Vector4[] constants)
    {
        if(disposed)throw new ObjectDisposedException(nameof(RemielleNativeUIDepthHierarchy));
        if(!depthSource||!normalSource||constants?.Length!=168||depthSource.width!=normalSource.width||depthSource.height!=normalSource.height)throw new ArgumentException("Depth hierarchy input mismatch");
        if(constants[138].x!=depthSource.width||constants[138].y!=depthSource.height)throw new ArgumentException("Depth hierarchy source dimensions differ from constants");
        int w=Mathf.Max(1,depthSource.width/2),h=Mathf.Max(1,depthSource.height/2);Ensure(w,h);
        material.SetVectorArray("P0",constants);material.SetTexture("_DepthSource",depthSource);material.SetTexture("_NormalSource",normalSource);
        bool srgb=GL.sRGBWrite;
        try
        {
            GL.sRGBWrite=false;commands.Clear();commands.SetRenderTarget(new RenderTargetIdentifier[]{LinearDepth,NormalAndSample,Range},Depth);
            commands.SetViewport(new Rect(0,0,w,h));commands.ClearRenderTarget(RTClearFlags.All,Color.clear,1,0);
            commands.DrawProcedural(Matrix4x4.identity,material,0,MeshTopology.Triangles,3);Graphics.ExecuteCommandBuffer(commands);
            // The original draw reads a distinct base-level copy, while it
            // writes mip 1. Avoid overlapping input/output resource views.
            Graphics.CopyTexture(Range,0,0,rangeCopy,0,0);material.SetTexture("_RangeSource",rangeCopy);
            commands.Clear();commands.SetRenderTarget(Range,1);commands.SetViewport(new Rect(0,0,Mathf.Max(1,w/2),Mathf.Max(1,h/2)));
            commands.DrawProcedural(Matrix4x4.identity,material,1,MeshTopology.Triangles,3);Graphics.ExecuteCommandBuffer(commands);
        }
        finally{GL.sRGBWrite=srgb;}
    }
    static RenderTexture Target(int w,int h,GraphicsFormat format,bool mip=false)
    {
        var d=new RenderTextureDescriptor(w,h,format,GraphicsFormat.None){useMipMap=mip,autoGenerateMips=false,mipCount=mip?2:1,msaaSamples=1};
        var t=new RenderTexture(d){hideFlags=HideFlags.HideAndDontSave,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
        if(!t.Create()){Destroy(t);throw new InvalidOperationException("Depth hierarchy target unavailable");}return t;
    }
    void Ensure(int w,int h)
    {
        if(Width==w&&Height==h)return;ReleaseOutputs();
        LinearDepth=Target(w,h,GraphicsFormat.R32_SFloat);NormalAndSample=Target(w,h,GraphicsFormat.A2B10G10R10_UNormPack32);
        Range=Target(w,h,GraphicsFormat.R16G16_SFloat,true);rangeCopy=Target(w,h,GraphicsFormat.R16G16_SFloat);
        var d=new RenderTextureDescriptor(w,h){graphicsFormat=GraphicsFormat.None,depthStencilFormat=GraphicsFormat.D32_SFloat_S8_UInt,stencilFormat=GraphicsFormat.R8_UInt,msaaSamples=1,mipCount=1};
        Depth=new RenderTexture(d){hideFlags=HideFlags.HideAndDontSave};if(!Depth.Create())throw new InvalidOperationException("Half-resolution depth target unavailable");
    }
    static void Destroy(Object o){if(!o)return;if(Application.isPlaying)Object.Destroy(o);else Object.DestroyImmediate(o);}
    static void Release(RenderTexture t){if(!t)return;if(RenderTexture.active==t)RenderTexture.active=null;t.Release();Destroy(t);}
    void ReleaseOutputs(){Release(LinearDepth);Release(NormalAndSample);Release(Range);Release(Depth);Release(rangeCopy);LinearDepth=NormalAndSample=Range=Depth=rangeCopy=null;}
    public void Dispose(){if(disposed)return;disposed=true;ReleaseOutputs();Release(sampledDepth);sampledDepth=null;commands.Release();Destroy(material);}
}
