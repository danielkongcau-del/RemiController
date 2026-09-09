using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

// Consumes the live native-layout buffers with the exact recovered draw 523
// program. This remains off-screen while material/scene producers are partial.
[DefaultExecutionOrder(600)]
[RequireComponent(typeof(Camera))]
public class RemielleNativeDeferredProbe : MonoBehaviour
{
    [Serializable] class CapturedData { public float[] pixelConstants; }

    public RemielleNativeGBufferProbe gBuffer;
    public Shader deferredShader;
    public TextAsset capturedData;
    public Texture2D sceneLut;
    public Light keyLight;
    public bool liveUpdate = true;
    public string status;
    public int lastWidth, lastHeight;
    public RenderTexture Hdr => hdr;
    public RenderTexture Auxiliary => auxiliary;
    public RenderTexture ExpandedNormal => expandedNormal;
    public RenderTexture ExpandedMotionFlagsClass => expandedMotionFlagsClass;

    Camera cameraComponent;
    Material material;
    CommandBuffer commands;
    Vector4[] constants;
    RenderTexture expandedNormal, expandedMotionFlagsClass, hdr, auxiliary;

    static RenderTexture Target(string name,int width,int height,GraphicsFormat format)
    {
        var descriptor=new RenderTextureDescriptor(width,height,format,GraphicsFormat.None){msaaSamples=1,useMipMap=false,autoGenerateMips=false};
        var target=new RenderTexture(descriptor){name=name,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp,hideFlags=HideFlags.HideAndDontSave};
        if(!target.Create())throw new InvalidOperationException("Could not create "+name+" / "+format);
        return target;
    }

    void Prepare()
    {
        if(material)return;
        cameraComponent=GetComponent<Camera>();
        if(!gBuffer||!deferredShader||!deferredShader.isSupported||!capturedData||!sceneLut||!keyLight)
            throw new InvalidOperationException("Live native Deferred probe references missing");
        var parsed=JsonUtility.FromJson<CapturedData>(capturedData.text);
        if(parsed?.pixelConstants==null||parsed.pixelConstants.Length!=185*4)
            throw new InvalidOperationException("Captured draw 523 constants are unavailable");
        constants=new Vector4[185];
        for(int i=0;i<constants.Length;i++)constants[i]=new Vector4(parsed.pixelConstants[i*4],parsed.pixelConstants[i*4+1],parsed.pixelConstants[i*4+2],parsed.pixelConstants[i*4+3]);
        material=new Material(deferredShader){hideFlags=HideFlags.HideAndDontSave};
        material.SetInt("_LiveStencilRef",128);material.SetInt("_LiveStencilReadMask",128);material.SetInt("_LiveStencilComp",(int)CompareFunction.Equal);
        commands=new CommandBuffer{name="Remielle live captured draw 523 resolve"};
    }

    static void Release(ref RenderTexture target)
    {
        if(!target)return;
        // Readback helpers may leave one of these probe targets selected until
        // the end of the frame. Unity warns if an active target is released.
        if(RenderTexture.active==target)RenderTexture.active=null;
        target.Release();if(Application.isPlaying)Destroy(target);else DestroyImmediate(target);target=null;
    }
    void ReleaseTargets(){Release(ref expandedNormal);Release(ref expandedMotionFlagsClass);Release(ref hdr);Release(ref auxiliary);lastWidth=lastHeight=0;}
    void EnsureTargets(int width,int height)
    {
        if(hdr&&lastWidth==width&&lastHeight==height)return;
        ReleaseTargets();
        expandedNormal=Target("Remielle live expanded GBuffer3",width,height,GraphicsFormat.R32G32B32A32_SFloat);
        expandedMotionFlagsClass=Target("Remielle live expanded GBuffer2",width,height,GraphicsFormat.R32G32B32A32_SFloat);
        hdr=Target("Remielle live draw 523 HDR",width,height,GraphicsFormat.B10G11R11_UFloatPack32);
        auxiliary=Target("Remielle live draw 523 auxiliary",width,height,GraphicsFormat.A2B10G10R10_UNormPack32);
        lastWidth=width;lastHeight=height;
    }

    void UpdateCameraConstants(int width,int height)
    {
        float near=cameraComponent.nearClipPlane,far=cameraComponent.farClipPlane;
        Vector4 depth=constants[62];
        if(SystemInfo.usesReversedZBuffer){depth.z=1f/near-1f/far;depth.w=1f/far;}
        else{depth.z=1f/far-1f/near;depth.w=1f/near;}
        constants[62]=depth;
        constants[30]=new Vector4(cameraComponent.transform.position.x,cameraComponent.transform.position.y,cameraComponent.transform.position.z,0);
        Matrix4x4 view=cameraComponent.worldToCameraMatrix;
        Matrix4x4 inverse=(GL.GetGPUProjectionMatrix(cameraComponent.projectionMatrix,true)*view).inverse;
        constants[78]=view.GetRow(0);constants[79]=view.GetRow(1);constants[80]=view.GetRow(2);
        constants[134]=inverse.GetColumn(0);constants[135]=inverse.GetColumn(1);constants[136]=inverse.GetColumn(2);constants[137]=inverse.GetColumn(3);
        constants[138]=new Vector4(width,height,1f/width,1f/height);
        Vector3 light=-keyLight.transform.forward;constants[6]=new Vector4(light.x,light.y,light.z,0);
        Vector4 lut=constants[183];lut.x=1f/sceneLut.width;lut.y=1f/sceneLut.height;lut.z=sceneLut.height-1;constants[183]=lut;
    }

    public void RenderNow(int width,int height)
    {
        Prepare();
        if(!gBuffer.Primary||gBuffer.lastWidth!=width||gBuffer.lastHeight!=height)gBuffer.RenderNow(width,height);
        EnsureTargets(width,height);
        // Unity's internal Blit conversion is separately read back by the
        // Player audit; it is the bridge required because this D3D11 device
        // cannot bind packed R10 targets as ordinary sampled textures.
        Graphics.Blit(gBuffer.WorldNormal,expandedNormal);
        Graphics.Blit(gBuffer.MotionFlagsClass,expandedMotionFlagsClass);
        UpdateCameraConstants(width,height);
        material.SetTexture("_T0",expandedNormal);material.SetTexture("_T2",gBuffer.Primary);material.SetTexture("_T3",gBuffer.Auxiliary);material.SetTexture("_T4",expandedMotionFlagsClass);material.SetTexture("_T5",sceneLut);
        material.SetVectorArray("P0",constants);
        var targets=new RenderTargetIdentifier[]{hdr,auxiliary};var depthId=new RenderTargetIdentifier(gBuffer.DepthStencil);
        commands.Clear();commands.SetGlobalTexture("_T1",depthId,RenderTextureSubElement.Depth);commands.SetRenderTarget(targets,gBuffer.DepthStencil);
        commands.ClearRenderTarget(RTClearFlags.Color,Color.clear,1,0);
        commands.DrawProcedural(Matrix4x4.identity,material,0,MeshTopology.Triangles,3);
        Graphics.ExecuteCommandBuffer(commands);
        status="draw 523 live resolve "+width+"x"+height+" / captured scene constants";
    }

    void LateUpdate(){if(liveUpdate&&gBuffer&&gBuffer.Primary)RenderNow(gBuffer.lastWidth,gBuffer.lastHeight);}
    void OnDisable(){ReleaseTargets();}
    void OnDestroy(){ReleaseTargets();commands?.Release();if(material){if(Application.isPlaying)Destroy(material);else DestroyImmediate(material);}}
}
