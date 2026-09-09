using System;
using UnityEngine;

[Serializable] public class CapturedBloomStep { public int call,passIndex,width,height;public float[] vertex,pixel,pixel1;public int[] inputDraws; }
[Serializable] public class CapturedBloomData { public CapturedBloomStep[] steps;public float intensity;public int sourceWidth,sourceHeight; }

// Exact menu pass kernels/uniforms; the scene explicitly disables this chain in combat.
[RequireComponent(typeof(Camera))]
public class RemielleReviewBloom : MonoBehaviour
{
    public Shader shader;public TextAsset capturedData;
    public float amount=1;public bool available=true;
    public int lastWidth,lastHeight,lastSourceSamples;
    Material material;CapturedBloomData data;
    public CapturedBloomData Data => data??=JsonUtility.FromJson<CapturedBloomData>(capturedData.text);
    public static Vector4[] Vectors(float[] values,int count=209)
    {
        var result=new Vector4[count];
        if(values!=null)for(int i=0;i<values.Length/4&&i<result.Length;i++)result[i]=new Vector4(values[i*4],values[i*4+1],values[i*4+2],values[i*4+3]);
        return result;
    }
    Vector4[][] vs,ps,ps1;
    void Prepare()
    {
        if(material==null)material=new Material(shader){hideFlags=HideFlags.HideAndDontSave};
        if(vs!=null)return;
        vs=new Vector4[Data.steps.Length][];ps=new Vector4[vs.Length][];ps1=new Vector4[vs.Length][];
        for(int i=0;i<vs.Length;i++){vs[i]=Vectors(Data.steps[i].vertex);ps[i]=Vectors(Data.steps[i].pixel);ps1[i]=Vectors(Data.steps[i].pixel1);}
    }
    public static void Draw(Material m,int pass,Texture source,Texture second,RenderTexture destination,Vector4[] vertex=null,Vector4[] pixel=null,Vector4[] pixel1=null)
    {
        m.SetTexture("_T0",source);m.SetTexture("_T1",second);
        if(vertex!=null)m.SetVectorArray("V0",vertex);
        if(pixel!=null)m.SetVectorArray("P0",pixel);
        if(pixel1!=null)m.SetVectorArray("P1",pixel1);
        Graphics.SetRenderTarget(destination);
        GL.sRGBWrite=destination==null?QualitySettings.activeColorSpace==ColorSpace.Linear:destination.sRGB;
        if(!m.SetPass(pass))throw new InvalidOperationException("Captured Bloom pass is unavailable: "+pass);
        Graphics.DrawProceduralNow(MeshTopology.Triangles,6);
    }
    static RenderTexture Temp(int w,int h)
    {
        var r=RenderTexture.GetTemporary(w,h,0,RenderTextureFormat.RGB111110Float,RenderTextureReadWrite.Linear);
        r.filterMode=FilterMode.Bilinear;r.wrapMode=TextureWrapMode.Clamp;return r;
    }
    static void Clear(RenderTexture r){Graphics.SetRenderTarget(r);GL.Clear(false,true,Color.clear);}
    void OnRenderImage(RenderTexture source,RenderTexture destination)
    {
        lastWidth=source.width;lastHeight=source.height;lastSourceSamples=source.antiAliasing;
        if(!available||amount<=0){Graphics.Blit(source,destination);return;}
        Prepare();Render(source,destination);
    }
    public void Render(RenderTexture source,RenderTexture destination)
    {
        Prepare();var s=Data.steps;
        var a=Temp(s[0].width,s[0].height);var b=Temp(a.width,a.height);
        var c=Temp(s[3].width,s[3].height);var d=Temp(c.width,c.height);var combined=Temp(s[12].width,s[12].height);
        var previous=RenderTexture.active;bool srgb=GL.sRGBWrite;
        try{
            vs[0][175]=new Vector4(1f/source.width,1f/source.height,source.width,source.height);
            Draw(material,0,source,null,a,vs[0],ps[0]);
            Draw(material,1,a,null,b,vs[1],ps[1]);Draw(material,1,b,null,a,vs[2],ps[2]);
            Clear(c);for(int i=3;i<=5;i++)Draw(material,2,a,null,c,vs[i],ps[i]);
            Clear(d);for(int i=6;i<=8;i++)Draw(material,s[i].passIndex,c,null,d,vs[i],ps[i]);
            Clear(c);for(int i=9;i<=11;i++)Draw(material,s[i].passIndex,d,null,c,vs[i],ps[i]);
            Draw(material,6,a,c,combined,null,ps[12],ps1[12]);
            material.SetFloat("_Intensity",Data.intensity*amount);Draw(material,7,source,combined,destination);
        }finally{
            RenderTexture.active=previous;GL.sRGBWrite=srgb;
            foreach(var rt in new[]{a,b,c,d,combined})RenderTexture.ReleaseTemporary(rt);
        }
    }
    void OnDestroy(){if(material!=null){if(Application.isPlaying)Destroy(material);else DestroyImmediate(material);}}
}
