using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

// UI display/store character resolve, before Bloom and final post processing.
// Consumes current native geometry buffers; does not substitute HoyoToon color.
public sealed class RemielleNativeUILighting : IDisposable
{
    public RenderTexture Hdr {get;private set;}
    public RenderTexture Auxiliary {get;private set;}
    public Vector4[] Constants {get;private set;}
    public RenderTexture ExpandedNormal {get;private set;}
    public RenderTexture ExpandedMotion {get;private set;}
    public RenderTexture SampledDepth {get;private set;}
    readonly Material material;
    readonly Texture lut;
    readonly Vector4[] template;
    readonly CommandBuffer cmd=new(){name="Remielle native UI character resolve"};
    bool disposed;
    public RemielleNativeUILighting(Shader shader,Texture characterLut,Vector4[] capturedConstants)
    {
        if(!shader||!shader.isSupported||!characterLut||capturedConstants?.Length!=185)throw new ArgumentException("Incomplete UI lighting profile");
        lut=characterLut;template=(Vector4[])capturedConstants.Clone();Constants=new Vector4[185];
        material=new Material(shader){hideFlags=HideFlags.HideAndDontSave};
        material.SetInt("_LiveStencilRef",128);material.SetInt("_LiveStencilReadMask",128);material.SetInt("_LiveStencilComp",(int)CompareFunction.Equal);
    }
    public void Render(RemielleNativeUIRenderer geometry,Camera camera,RemielleNativeUILightRig lightRig=null)
    {
        if(disposed||geometry.CurrentFrame==null||!geometry.Depth)throw new InvalidOperationException("Render UI geometry before lighting");
        int width=geometry.Width,height=geometry.Height;EnsureTargets(width,height);
        Array.Copy(template,Constants,template.Length);
        var frame=geometry.CurrentFrame;
        lightRig?.PatchDeferred(Constants,frame);
        Constants[30]=frame.cameraPosition;
        var z=Constants[62];z.z=1f/camera.nearClipPlane-1f/camera.farClipPlane;z.w=1f/camera.farClipPlane;Constants[62]=z;
        // Original unity_WorldToCamera is the unflipped camera transform.
        // The game capture differs from unity_MatrixV by its third row sign.
        // Both matrices are serialized by columns, not by rows.
        var worldToCamera=Matrix4x4.Scale(new Vector3(1,1,-1))*frame.view;
        for(int i=0;i<4;i++)Constants[78+i]=worldToCamera.GetColumn(i);
        var inverse=frame.vp.inverse;for(int i=0;i<4;i++)Constants[134+i]=inverse.GetColumn(i);
        Constants[138]=frame.screenSize;
        var lp=Constants[183];lp.x=1f/lut.width;lp.y=1f/lut.height;lp.z=lut.height-1;Constants[183]=lp;
        Graphics.Blit(geometry.Targets[3],ExpandedNormal);Graphics.Blit(geometry.Targets[2],ExpandedMotion);
        // The original depth remains bound for stencil testing. Sample a
        // bit-preserving R32 copy, avoiding simultaneous DSV/SRV aliasing.
        Graphics.Blit(geometry.DepthRead,SampledDepth);
        material.SetTexture("_T0",ExpandedNormal);material.SetTexture("_T1",SampledDepth);
        material.SetTexture("_T2",geometry.Targets[0]);material.SetTexture("_T3",geometry.Targets[1]);material.SetTexture("_T4",ExpandedMotion);material.SetTexture("_T5",lut);
        material.SetVectorArray("P0",Constants);
        cmd.Clear();cmd.SetRenderTarget(new RenderTargetIdentifier[]{Hdr,Auxiliary},geometry.Depth);cmd.SetViewport(new Rect(0,0,width,height));
        cmd.ClearRenderTarget(RTClearFlags.Color,Color.clear,1,0);
        cmd.DrawProcedural(Matrix4x4.identity,material,0,MeshTopology.Triangles,3);Graphics.ExecuteCommandBuffer(cmd);
    }
    void EnsureTargets(int width,int height)
    {
        if(Hdr&&Hdr.width==width&&Hdr.height==height)return;
        Release();Hdr=Target(width,height,GraphicsFormat.R16G16B16A16_SFloat);Auxiliary=Target(width,height,GraphicsFormat.A2B10G10R10_UNormPack32);
        ExpandedNormal=Target(width,height,GraphicsFormat.R32G32B32A32_SFloat);ExpandedMotion=Target(width,height,GraphicsFormat.R32G32B32A32_SFloat);
        SampledDepth=Target(width,height,GraphicsFormat.R32_SFloat);
    }
    static RenderTexture Target(int width,int height,GraphicsFormat format)
    {
        var t=new RenderTexture(new RenderTextureDescriptor(width,height,format,GraphicsFormat.None)){hideFlags=HideFlags.HideAndDontSave,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
        if(!t.Create()){Destroy(t);throw new InvalidOperationException("UI lighting target unavailable");}return t;
    }
    static void Destroy(Object o){if(!o)return;if(Application.isPlaying)Object.Destroy(o);else Object.DestroyImmediate(o);}
    static void Release(RenderTexture t){if(!t)return;if(RenderTexture.active==t)RenderTexture.active=null;t.Release();Destroy(t);}
    void Release(){Release(Hdr);Release(Auxiliary);Release(ExpandedNormal);Release(ExpandedMotion);Release(SampledDepth);Hdr=Auxiliary=ExpandedNormal=ExpandedMotion=SampledDepth=null;}
    public void Dispose(){if(disposed)return;disposed=true;Release();cmd.Release();Destroy(material);}
}
