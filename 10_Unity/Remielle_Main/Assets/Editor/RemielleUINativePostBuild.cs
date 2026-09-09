using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

public static class RemielleUINativePostBuild
{
    public const string Root="E:/ZZZ/local-only/RemielleRenderingReview/20260905/ui-live-binding/native-post/";
    public const string WriteRoot = "E:/ZZZ/ZCode/90_Builds/RenderingReview/20260905/ui-live-binding/native-post/"; // D1-c 写根（读根保留 A 类）
    public const string Assets="Assets/RenderingReview/NativeUILive/Post/";
    public static string Sha(string p){using var f=File.OpenRead(p);using var h=SHA256.Create();return BitConverter.ToString(h.ComputeHash(f)).Replace("-","").ToLowerInvariant();}
    public static JObject Ref(string p)=>new(){["path"]=Path.GetFullPath(p),["sha256"]=Sha(p)};
    public static byte[] ReadRef(JToken r){string p=(string)r["path"];if(Sha(p)!=(string)r["sha256"])throw new Exception("Post source changed: "+p);return File.ReadAllBytes(p);}
    public static Vector4[] Vectors(byte[] bytes,int count)
    {
        if(bytes.Length<count*16)throw new Exception("Post constant buffer truncated");var result=new Vector4[count];for(int i=0;i<count;i++)result[i]=new Vector4(BitConverter.ToSingle(bytes,i*16),BitConverter.ToSingle(bytes,i*16+4),BitConverter.ToSingle(bytes,i*16+8),BitConverter.ToSingle(bytes,i*16+12));return result;
    }
    static T Save<T>(T value,string path)where T:Object
    {
        value.hideFlags=HideFlags.None;value.name=Path.GetFileNameWithoutExtension(path);var old=AssetDatabase.LoadAssetAtPath<T>(path);
        if(old){EditorUtility.CopySerialized(value,old);EditorUtility.SetDirty(old);Object.DestroyImmediate(value);return old;}
        AssetDatabase.CreateAsset(value,path);return value;
    }
    public static Texture2D Texture(JToken row)
    {
        ReadRef(row["source"]);var bytes=ReadRef(row["unity"]);int format=(int)row["unityFormat"];
        var tf=format switch{2=>TextureFormat.RGBAFloat,10=>TextureFormat.RGBAHalf,29=>TextureFormat.RGBA32,_=>throw new Exception("Unsupported post fixture format")};
        var t=new Texture2D((int)row["width"],(int)row["height"],tf,false,format!=29){hideFlags=HideFlags.HideAndDontSave,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
        t.LoadRawTextureData(bytes);t.Apply(false,false);return t;
    }
    public static void Run()
    {
        Directory.CreateDirectory(Assets);AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var manifest=JObject.Parse(File.ReadAllText(Root+"manifest.json"));ReadRef(manifest["shader"]);
        var assets=new JArray();
        foreach(var row in manifest["cases"])
        {
            string id=(string)row["id"];var p=ScriptableObject.CreateInstance<RemielleNativeUIPostProfile>();p.profileName=id;p.sourceManifestSha256=Sha(Root+"manifest.json");
            p.finalShader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/RenderingReview/Shader/GeneratedNativeUIReplay/NativeUIFinalPost.shader");p.bloomShader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/CapturedMenuBloom.shader");
            var vb=ReadRef(row["vertexBuffer"]);var ib=ReadRef(row["indexBuffer"]);if(vb.Length!=80||ib.Length!=12)throw new Exception("Unexpected source post quad");
            var v=new Vector3[4];var uv=new Vector2[4];var ix=new int[6];for(int i=0;i<4;i++){v[i]=new Vector3(BitConverter.ToSingle(vb,i*20),BitConverter.ToSingle(vb,i*20+4),BitConverter.ToSingle(vb,i*20+8));uv[i]=new Vector2(BitConverter.ToSingle(vb,i*20+12),BitConverter.ToSingle(vb,i*20+16));}for(int i=0;i<6;i++)ix[i]=BitConverter.ToUInt16(ib,i*2);
            var mesh=new Mesh{vertices=v,uv=uv,triangles=ix};p.fullscreenQuad=Save(mesh,Assets+id+"-quad.asset");
            Vector4[] Constant(string stage,int slot,int n)=>Vectors(ReadRef(row["constants"].Single(c=>(string)c["stage"]==stage&&(int)c["slot"]==slot)["file"]),n);
            p.vertex0=Constant("vs",0,106);p.vertex1=Constant("vs",1,4);p.pixel0=Constant("ps",0,139);p.pixel1=Constant("ps",1,30);
            Texture2D StaticTexture(int slot){var r=row["inputs"].Single(i=>(int)i["slot"]==slot);string path=Assets+(string)r["source"]["sha256"]+".asset";return Save(Texture(r),path);}
            p.grain=StaticTexture(1);p.distortion=StaticTexture(2);p.dirt=StaticTexture(4);
            var bloomBytes=ReadRef(row["bloom"]);var bloomJson=JObject.Parse(System.Text.Encoding.UTF8.GetString(bloomBytes));foreach(var e in bloomJson["evidence"])ReadRef(e);
            string bloomPath=Assets+id+"-bloom.json";File.WriteAllBytes(bloomPath,bloomBytes);AssetDatabase.ImportAsset(bloomPath,ImportAssetOptions.ForceSynchronousImport);p.bloomData=AssetDatabase.LoadAssetAtPath<TextAsset>(bloomPath);
            Save(p,Assets+id+".asset");assets.Add(Ref(Assets+id+".asset"));
        }
        AssetDatabase.SaveAssets();
        // Serialize before hashing, because existing profiles may have been updated.
        for(int i=0;i<assets.Count;i++)assets[i]=Ref((string)assets[i]["path"]);
        File.WriteAllText(WriteRoot+"profiles.json",new JObject{["profiles"]=assets,["manifest"]=Ref(WriteRoot+"manifest.json"),["builder"]=Ref("Assets/Editor/RemielleUINativePostBuild.cs")}.ToString());
        Debug.Log("REMIELLE_NATIVE_UI_POST_PROFILES_READY");
    }
    public static void RunAudit(){Run();RemielleUINativePostAudit.Run();}
}
