using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

// Full native display/store Bloom and UberPost. The preceding character LUT
// belongs to Deferred; this program has no independent final LUT input.
public sealed class RemielleNativeUIPost : IDisposable
{
    public RenderTexture Bloom {get;private set;}
    public RenderTexture FinalColor {get;private set;}
    public Vector4[] Pixel0 {get;private set;}
    public Vector4[] Pixel1 {get;private set;}
    public readonly RemielleNativeUIPostProfile Profile;
    readonly Material bloom,final;
    readonly CapturedBloomData data;
    readonly Vector4[][] vs,ps,ps1;
    readonly CommandBuffer cmd=new(){name="Remielle complete native UI UberPost"};
    RenderTexture a,b,c,d;
    bool disposed;
    public RemielleNativeUIPost(RemielleNativeUIPostProfile profile)
    {
        if(!profile||!profile.finalShader||!profile.bloomShader||!profile.fullscreenQuad||!profile.bloomData)throw new ArgumentException("Incomplete native UI post profile");
        Profile=profile;data=JsonUtility.FromJson<CapturedBloomData>(profile.bloomData.text);
        if(data.steps?.Length!=13||profile.pixel0?.Length!=139||profile.pixel1?.Length!=30)throw new ArgumentException("Invalid native UI post layout");
        bloom=new Material(profile.bloomShader){hideFlags=HideFlags.HideAndDontSave};final=new Material(profile.finalShader){hideFlags=HideFlags.HideAndDontSave};
        vs=new Vector4[13][];ps=new Vector4[13][];ps1=new Vector4[13][];
        for(int i=0;i<13;i++){vs[i]=RemielleReviewBloom.Vectors(data.steps[i].vertex);ps[i]=RemielleReviewBloom.Vectors(data.steps[i].pixel);ps1[i]=RemielleReviewBloom.Vectors(data.steps[i].pixel1);}
        Pixel0=new Vector4[139];Pixel1=new Vector4[30];
    }
    public void Render(RenderTexture hdr,Texture temporalColor=null)
    {
        if(disposed||!hdr)throw new InvalidOperationException("Native UI HDR is required");
        if(temporalColor&&(temporalColor.width!=hdr.width||temporalColor.height!=hdr.height))throw new ArgumentException("Temporal color dimensions differ from the native HDR source");
        EnsureTargets(hdr.width,hdr.height);
        var previous=RenderTexture.active;bool srgb=GL.sRGBWrite;
        try
        {
            vs[0][175]=new Vector4(1f/hdr.width,1f/hdr.height,hdr.width,hdr.height);
            Draw(0,hdr,null,a);Draw(1,a,null,b);Draw(2,b,null,a);
            Clear(c);for(int i=3;i<=5;i++)Draw(i,a,null,c);
            Clear(d);for(int i=6;i<=8;i++)Draw(i,c,null,d);
            Clear(c);for(int i=9;i<=11;i++)Draw(i,d,null,c);
            Draw(12,a,c,Bloom);
            Array.Copy(Profile.pixel0,Pixel0,139);Array.Copy(Profile.pixel1,Pixel1,30);
            Pixel0[138]=new Vector4(hdr.width,hdr.height,1f/hdr.width,1f/hdr.height);
            // Preserve captured disabled grain/vignette/distortion/VR switches.
            // Grain tiling follows pixel dimensions if the authored switch is used.
            var grain=Pixel1[8];if(Profile.grain){grain.x=(float)hdr.width/Profile.grain.width;grain.y=(float)hdr.height/Profile.grain.height;}Pixel1[8]=grain;
            // Game Bloom runs before TAA; only final UberPost consumes the
            // temporally resolved color. Never feed history back into Bloom.
            DrawFinal(final,Profile,temporalColor?temporalColor:hdr,Bloom,FinalColor,Pixel0,Pixel1,cmd);
        }
        finally {RenderTexture.active=previous;GL.sRGBWrite=srgb;}
    }
    void Draw(int index,Texture source,Texture second,RenderTexture destination)=>RemielleReviewBloom.Draw(bloom,data.steps[index].passIndex,source,second,destination,vs[index],ps[index],ps1[index]);
    static void Clear(RenderTexture target){Graphics.SetRenderTarget(target);GL.Clear(false,true,Color.clear);}
    public static void DrawFinal(Material material,RemielleNativeUIPostProfile profile,Texture source,Texture bloom,RenderTexture target,Vector4[] p0,Vector4[] p1,CommandBuffer command)
    {
        material.SetVectorArray("V0",profile.vertex0);material.SetVectorArray("V1",profile.vertex1);
        material.SetVectorArray("P0",p0);material.SetVectorArray("P1",p1);
        material.SetTexture("_T0",source);material.SetTexture("_T1",profile.grain);material.SetTexture("_T2",profile.distortion);material.SetTexture("_T3",bloom);material.SetTexture("_T4",profile.dirt);
        // Bloom leaves sRGBWrite disabled for its floating-point targets.
        // Explicitly restore the final sRGB RTV encoding for this draw.
        bool previousSrgb=GL.sRGBWrite;
        try
        {
            GL.sRGBWrite=target.sRGB;
            command.Clear();command.SetRenderTarget(target);command.SetViewport(new Rect(0,0,target.width,target.height));command.ClearRenderTarget(false,true,Color.clear);
            command.DrawMesh(profile.fullscreenQuad,Matrix4x4.identity,material,0,0);Graphics.ExecuteCommandBuffer(command);
        }
        finally{GL.sRGBWrite=previousSrgb;}
    }
    void EnsureTargets(int width,int height)
    {
        if(!Bloom)
        {
            var s=data.steps;a=Target(s[0].width,s[0].height,GraphicsFormat.B10G11R11_UFloatPack32);b=Target(a.width,a.height,a.graphicsFormat);
            c=Target(s[3].width,s[3].height,a.graphicsFormat);d=Target(c.width,c.height,c.graphicsFormat);Bloom=Target(s[12].width,s[12].height,a.graphicsFormat);
        }
        if(FinalColor&&FinalColor.width==width&&FinalColor.height==height)return;
        Release(FinalColor);FinalColor=Target(width,height,GraphicsFormat.R8G8B8A8_SRGB);
    }
    static RenderTexture Target(int w,int h,GraphicsFormat format)
    {
        var r=new RenderTexture(new RenderTextureDescriptor(w,h,format,GraphicsFormat.None)){hideFlags=HideFlags.HideAndDontSave,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
        if(!r.Create()){Destroy(r);throw new InvalidOperationException("Native UI post target unavailable");}return r;
    }
    static void Destroy(Object o){if(!o)return;if(Application.isPlaying)Object.Destroy(o);else Object.DestroyImmediate(o);}
    static void Release(RenderTexture r){if(!r)return;if(RenderTexture.active==r)RenderTexture.active=null;r.Release();Destroy(r);}
    public void Dispose(){if(disposed)return;disposed=true;foreach(var r in new[]{a,b,c,d,Bloom,FinalColor})Release(r);cmd.Release();Destroy(bloom);Destroy(final);a=b=c=d=Bloom=FinalColor=null;}
}
