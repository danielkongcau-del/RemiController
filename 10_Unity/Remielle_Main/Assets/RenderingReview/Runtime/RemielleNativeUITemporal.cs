using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

// Original UI temporal resolve with private current/history textures. Jitter
// samples follow the Halton pattern observed in both captures. The game wrap
// length is unknown; this implementation continues the sequence until reset.
public sealed class RemielleNativeUITemporal : IDisposable
{
    public Vector4[] Constants {get;private set;}
    public Vector2 JitterPixels {get;private set;}
    public RenderTexture Color {get;private set;}
    public RenderTexture Tag {get;private set;}
    public RenderTexture HistoryColorUsed {get;private set;}
    public RenderTexture HistoryTagUsed {get;private set;}
    public bool HasHistory {get;private set;}
    public uint SampleIndex {get;private set;}
    public int Width {get;private set;}
    public int Height {get;private set;}
    readonly RemielleNativeUITemporalProfile profile;
    readonly Material material;
    readonly RenderTexture[] colors=new RenderTexture[2],tags=new RenderTexture[2];
    readonly CommandBuffer command=new(){name="Remielle original UI temporal resolve"};
    int current;
    bool pending,disposed;
    public RemielleNativeUITemporal(RemielleNativeUITemporalProfile source)
    {
        if(!source||!source.shader||!source.shader.isSupported||source.constants?.Length!=168)throw new ArgumentException("Incomplete native temporal profile");
        profile=source;Constants=new Vector4[168];material=new Material(source.shader){hideFlags=HideFlags.HideAndDontSave};
    }
    public Vector2 BeginFrame(int width,int height,bool reset=false)
    {
        if(disposed||pending||width<1||height<1)throw new InvalidOperationException("Invalid native temporal frame order");
        if(width!=Width||height!=Height)
        {
            Release();Width=width;Height=height;
            for(int i=0;i<2;i++){colors[i]=Target(width,height,GraphicsFormat.R16G16B16A16_SFloat);tags[i]=Target(width,height,GraphicsFormat.R8_UNorm);}
            reset=true;
        }
        if(reset){HasHistory=false;SampleIndex=0;current=0;}
        Array.Copy(profile.constants,Constants,168);
        JitterPixels=new Vector2(Halton((ulong)SampleIndex+1,2)-.5f,Halton((ulong)SampleIndex+1,3)-.5f);
        Constants[138]=new Vector4(width,height,1f/width,1f/height);
        var frame=Constants[139];frame.z=SampleIndex;Constants[139]=frame;
        Constants[140]=new Vector4(JitterPixels.x,JitterPixels.y,JitterPixels.x/width,JitterPixels.y/height);
        var first=Constants[167];first.x=HasHistory?0:1;Constants[167]=first;
        pending=true;return JitterPixels;
    }
    public void Render(Texture depth,Texture motion,Texture hdr)
    {
        if(disposed||!pending||!depth||!motion||!hdr||depth.width!=Width||depth.height!=Height||motion.width!=Width||motion.height!=Height||hdr.width!=Width||hdr.height!=Height)throw new InvalidOperationException("Temporal inputs do not match the pending frame");
        bool srgb=GL.sRGBWrite;
        try
        {
            GL.sRGBWrite=false;
            if(!HasHistory)
            {
                // Deterministic bootstrap: seed from the current HDR and tag.
                // This is an explicit local lifecycle policy; no undefined or
                // stale texture is read after resize, profile switch or seek.
                Graphics.CopyTexture(hdr,colors[current]);
                material.SetTexture("_T1",motion);command.Clear();command.SetRenderTarget(tags[current]);command.SetViewport(new Rect(0,0,Width,Height));command.DrawProcedural(Matrix4x4.identity,material,1,MeshTopology.Triangles,3);Graphics.ExecuteCommandBuffer(command);
            }
            HistoryColorUsed=colors[current];HistoryTagUsed=tags[current];int next=1-current;
            Draw(material,Constants,depth,motion,hdr,HistoryColorUsed,HistoryTagUsed,colors[next],tags[next],command);
            current=next;Color=colors[current];Tag=tags[current];HasHistory=true;pending=false;SampleIndex++;
        }
        finally{GL.sRGBWrite=srgb;}
    }
    public static void Draw(Material material,Vector4[] constants,Texture depth,Texture motion,Texture hdr,Texture history,Texture historyTag,RenderTexture color,RenderTexture tag,CommandBuffer command)
    {
        material.SetVectorArray("P0",constants);material.SetTexture("_T0",depth);material.SetTexture("_T1",motion);material.SetTexture("_T2",hdr);material.SetTexture("_T3",history);material.SetTexture("_T4",historyTag);
        bool srgb=GL.sRGBWrite;
        try
        {
            GL.sRGBWrite=false;command.Clear();command.SetRenderTarget(new RenderTargetIdentifier[]{color,tag},BuiltinRenderTextureType.None);command.SetViewport(new Rect(0,0,color.width,color.height));
            command.ClearRenderTarget(false,true,UnityEngine.Color.clear);command.DrawProcedural(Matrix4x4.identity,material,0,MeshTopology.Triangles,3);Graphics.ExecuteCommandBuffer(command);
        }
        finally{GL.sRGBWrite=srgb;}
    }
    public static float Halton(ulong index,uint radix)
    {
        double result=0,scale=1;while(index!=0){scale/=radix;result+=scale*(index%radix);index/=radix;}return (float)result;
    }
    static RenderTexture Target(int w,int h,GraphicsFormat format)
    {
        var r=new RenderTexture(new RenderTextureDescriptor(w,h,format,GraphicsFormat.None)){hideFlags=HideFlags.HideAndDontSave,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
        if(!r.Create()){Destroy(r);throw new InvalidOperationException("Native temporal target unavailable");}return r;
    }
    static void Destroy(Object o){if(!o)return;if(Application.isPlaying)Object.Destroy(o);else Object.DestroyImmediate(o);}
    void Release(){for(int i=0;i<2;i++){foreach(var r in new[]{colors[i],tags[i]}){if(!r)continue;if(RenderTexture.active==r)RenderTexture.active=null;r.Release();Destroy(r);}colors[i]=tags[i]=null;}Color=Tag=HistoryColorUsed=HistoryTagUsed=null;}
    public void Dispose(){if(disposed)return;disposed=true;Release();command.Release();Destroy(material);}
}
