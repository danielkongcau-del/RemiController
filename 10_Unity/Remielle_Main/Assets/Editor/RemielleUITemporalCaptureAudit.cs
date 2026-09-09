using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

public static class RemielleUITemporalCaptureAudit
{
    public const string Root="E:/ZZZ/local-only/RemielleRenderingReview/20260905/ui-live-binding/native-taa/";
    public const string Assets="Assets/RenderingReview/NativeUILive/Temporal/";
    static JObject Ref(string p)=>RemielleUINativePostBuild.Ref(p);
    public static Texture Load(JToken row)
    {
        RemielleUINativePostBuild.ReadRef(row["source"]);int f=(int)row["unityFormat"];
        var tf=f switch{2=>TextureFormat.RGBAFloat,10=>TextureFormat.RGBAHalf,41=>TextureFormat.RFloat,61=>TextureFormat.R8,_=>throw new Exception("Unknown temporal input format")};
        var t=new Texture2D((int)row["width"],(int)row["height"],tf,false,true){hideFlags=HideFlags.HideAndDontSave,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};t.LoadRawTextureData(RemielleUINativePostBuild.ReadRef(row["unity"]));t.Apply(false,false);
        if((int)row["format"]!=24)return t;
        var packed=new RenderTexture(new RenderTextureDescriptor(t.width,t.height,GraphicsFormat.A2B10G10R10_UNormPack32,GraphicsFormat.None)){hideFlags=HideFlags.HideAndDontSave,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};packed.Create();
        try{t.filterMode=FilterMode.Point;Graphics.Blit(t,packed);if(!RemielleUINativePostAudit.Read(packed,t.width*t.height*4).SequenceEqual(RemielleUINativePostBuild.ReadRef(row["raw"])))throw new Exception("Temporal packed motion upload mismatch");}
        finally{Object.DestroyImmediate(t);}return packed;
    }
    public static void Build()
    {
        Directory.CreateDirectory(Assets);AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);var m=JObject.Parse(File.ReadAllText(Root+"manifest.json"));RemielleUINativePostBuild.ReadRef(m["shader"]);
        foreach(var row in m["cases"])
        {
            string id=(string)row["id"],path=Assets+id+".asset";var p=ScriptableObject.CreateInstance<RemielleNativeUITemporalProfile>();p.profileName=id;p.sourceManifestSha256=RemielleUINativePostBuild.Sha(Root+"manifest.json");p.shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/RenderingReview/Shader/GeneratedNativeUIReplay/NativeUITemporal.shader");p.constants=RemielleUINativePostBuild.Vectors(RemielleUINativePostBuild.ReadRef(row["constants"]),168);p.name=id;
            var old=AssetDatabase.LoadAssetAtPath<RemielleNativeUITemporalProfile>(path);if(old){EditorUtility.CopySerialized(p,old);EditorUtility.SetDirty(old);Object.DestroyImmediate(p);}else AssetDatabase.CreateAsset(p,path);
        }
        AssetDatabase.SaveAssets();
    }
    public static void RunAll(){Build();Run();}
    public static void Run()
    {
        Directory.CreateDirectory(Root+"unity");var manifest=JObject.Parse(File.ReadAllText(Root+"manifest.json"));var rows=new JArray();
        foreach(var row in manifest["cases"])
        {
            string id=(string)row["id"];int w=(int)row["width"],h=(int)row["height"];var p=AssetDatabase.LoadAssetAtPath<RemielleNativeUITemporalProfile>(Assets+id+".asset");var material=new Material(p.shader);var inputs=row["inputs"].OrderBy(i=>(int)i["slot"]).Select(Load).ToArray();
            var color=new RenderTexture(new RenderTextureDescriptor(w,h,GraphicsFormat.R16G16B16A16_SFloat,GraphicsFormat.None));var tag=new RenderTexture(new RenderTextureDescriptor(w,h,GraphicsFormat.R8_UNorm,GraphicsFormat.None));color.Create();tag.Create();var cmd=new CommandBuffer();
            try
            {
                RemielleNativeUITemporal.Draw(material,p.constants,inputs[0],inputs[1],inputs[2],inputs[3],inputs[4],color,tag,cmd);
                string a=Root+"unity/"+id+"-o0.raw",b=Root+"unity/"+id+"-o1.raw";File.WriteAllBytes(a,RemielleUINativePostAudit.Read(color,w*h*8));File.WriteAllBytes(b,RemielleUINativePostAudit.Read(tag,w*h));
                rows.Add(new JObject{["id"]=id,["color"]=Ref(a),["tag"]=Ref(b),["profile"]=Ref(Assets+id+".asset")});
                foreach(var e in ShaderUtil.GetShaderMessages(p.shader))if(e.severity.ToString()=="Error")throw new Exception(e.message);
            }
            finally{cmd.Release();RenderTexture.active=null;color.Release();tag.Release();Object.DestroyImmediate(color);Object.DestroyImmediate(tag);Object.DestroyImmediate(material);foreach(var t in inputs){if(t is RenderTexture rt)rt.Release();Object.DestroyImmediate(t);}}
        }
        var files=new JArray();foreach(string path in new[]{"Assets/Editor/RemielleUITemporalCaptureAudit.cs","Assets/RenderingReview/Runtime/RemielleNativeUITemporal.cs","Assets/RenderingReview/Runtime/RemielleNativeUITemporalProfile.cs","Assets/RenderingReview/Shader/GeneratedNativeUIReplay/NativeUITemporal.shader"})files.Add(Ref(path));
        File.WriteAllText(Root+"unity.json",new JObject{["schema"]="remielle-native-ui-temporal-capture-readback-v1",["cases"]=rows,["manifest"]=Ref(Root+"manifest.json"),["implementationFiles"]=files}.ToString());
        Debug.Log("REMIELLE_UI_TEMPORAL_CAPTURE_READBACK "+rows.Count);
    }
}
