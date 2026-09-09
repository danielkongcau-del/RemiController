using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

public static class RemielleUILateBodyBuild
{
    public const string Root="E:/ZZZ/local-only/RemielleRenderingReview/20260905/ui-live-binding/late-body/";
    public const string Assets="Assets/RenderingReview/NativeUILive/LateBody/";
    static JObject Load(string p)=>JObject.Parse(File.ReadAllText(p));
    public static void CheckRefs(JToken t)
    {
        if(t is JObject o)
        {
            if(o["path"]!=null&&o["sha256"]!=null)RemielleUINativePostBuild.ReadRef(o);
            foreach(var p in o.Properties())CheckRefs(p.Value);
        }
        else if(t is JArray a)foreach(var child in a)CheckRefs(child);
    }
    static void Save(Object value,string path)
    {
        value.hideFlags=HideFlags.None;value.name=Path.GetFileNameWithoutExtension(path);var old=AssetDatabase.LoadMainAssetAtPath(path);
        if(old){if(old.GetType()!=value.GetType())throw new Exception("Late asset type changed");EditorUtility.CopySerialized(value,old);EditorUtility.SetDirty(old);Object.DestroyImmediate(value);}
        else AssetDatabase.CreateAsset(value,path);
    }
    public static void Build()
    {
        var inputs=Load(Root+"isolated-inputs.json");var shaders=Load(Root+"unity-shader.json");CheckRefs(inputs);CheckRefs(shaders);
        if(!(bool)Load(Root+"verification.json")["pass"])throw new Exception("Native captured forward gate failed");
        Directory.CreateDirectory(Assets);AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var textureRows=Load(RemielleUILiveProfileBuild.Capture+"manifest.json")["textures"].ToDictionary(t=>(string)t["path"]);
        var contract=shaders["shaders"].ToDictionary(t=>(string)t["hash"]);var records=new JArray();
        foreach(var row in inputs["cases"].Cast<JObject>())
        {
            string name=(string)row["profile"];var profile=ScriptableObject.CreateInstance<RemielleNativeUILateBodyProfile>();
            profile.profileName=name;profile.sourceManifestSha256=RemielleUINativePostBuild.Sha(Root+"isolated-inputs.json");
            profile.geometryProfile=AssetDatabase.LoadAssetAtPath<RemielleNativeUIProfile>(RemielleUILiveProfileBuild.Assets+name+".asset");
            profile.bodyMeshIndex=Array.FindIndex(profile.geometryProfile.sourceMeshes,s=>s.name=="Remielle_Origin_Body_1");
            if(profile.bodyMeshIndex<0)throw new Exception("Qualified Body_1 source missing");
            profile.shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/RenderingReview/Shader/GeneratedNativeUILateBody/NativeUILateBody.shader");
            string meshPath=Assets+name+"-mesh.asset";Save(RemielleUILiveAssetFactory.BuildMesh(row),meshPath);profile.template=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            profile.constants=row["constantBuffers"].Select(cb=>
            {
                string stage=(string)cb["stage"],hash=(string)row[stage];int slot=(int)cb["slot"];
                var fields=contract[hash]["constantBuffers"].Single(b=>(int)b["slot"]==slot)["variables"].Select(f=>new RemielleNativeUIConstants.Field{name=(string)f["name"],offsetBytes=(int)f["offsetBytes"],storageBytes=(int)f["storageBytes"]}).ToArray();
                return new RemielleNativeUIProfile.Constant{stage=stage,shader=hash,slot=slot,fields=fields,template=RemielleUINativePostBuild.ReadRef(cb).Take((int)cb["declaredFloat4"]*16).ToArray()};
            }).ToArray();
            profile.resources=row["resources"].Select(t=>
            {
                string path=(string)t["path"];Texture texture=null;byte[] structured=Array.Empty<byte>();
                if((int)t["kind"]==0)structured=RemielleUINativePostBuild.ReadRef(t);
                else
                {
                    if(!textureRows.TryGetValue(path,out var source))throw new Exception("Late source texture not in verified UI library: "+path);
                    string asset=(string)source["asset"]??(RemielleUILiveProfileBuild.Assets+"Textures/"+RemielleUINativePostBuild.Sha(path)+".asset");
                    texture=AssetDatabase.LoadAssetAtPath<Texture>(asset);if(!texture)throw new Exception("Late texture asset missing: "+asset);
                }
                return new RemielleNativeUIProfile.Resource{binding="_Late"+((string)t["stage"]).ToUpperInvariant()+((string)t["slot"]).ToUpperInvariant(),texture=texture,structured=structured};
            }).ToArray();
            string profilePath=Assets+name+".asset";Save(profile,profilePath);records.Add(new JObject{["profile"]=RemielleUINativePostBuild.Ref(profilePath),["mesh"]=RemielleUINativePostBuild.Ref(meshPath)});
        }
        AssetDatabase.SaveAssets();
        // SaveAssets may update files referenced above; seal after the write.
        foreach(JObject row in records)foreach(string key in new[]{"profile","mesh"})row[key]=RemielleUINativePostBuild.Ref((string)row[key]["path"]);
        File.WriteAllText(Root+"runtime-profiles.json",new JObject{["profiles"]=records,["source"]=RemielleUINativePostBuild.Ref(Root+"isolated-inputs.json"),["fields"]=RemielleUINativePostBuild.Ref(Root+"unity-shader.json")}.ToString());
        Debug.Log("REMIELLE_UI_LATE_BODY_PROFILES_BUILT 2");
    }
    public static void RunAll(){Build();RemielleUILateBodyCaptureAudit.Run();RemielleUILiveLateBodyAudit.Run();}
}
