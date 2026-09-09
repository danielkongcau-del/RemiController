using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

public static class RemielleUILiveProfileBuild
{
    public const string Capture="E:/ZZZ/local-only/RemielleRenderingReview/20260905/ui-full-sequence/";
    public const string Live="E:/ZZZ/local-only/RemielleRenderingReview/20260905/ui-live-binding/";
    public const string Assets="Assets/RenderingReview/NativeUILive/";
    static JObject Load(string p)=>JObject.Parse(File.ReadAllText(p));
    static string Sha(string p){using var f=File.OpenRead(p);using var h=SHA256.Create();return BitConverter.ToString(h.ComputeHash(f)).Replace("-","").ToLowerInvariant();}
    static void Verify(string path,string expected){if(Sha(path)!=expected)throw new Exception("Native UI source hash mismatch: "+path);}
    static void Save(UnityEngine.Object value,string path)
    {
        value.hideFlags=HideFlags.None;value.name=Path.GetFileNameWithoutExtension(path);
        var old=AssetDatabase.LoadMainAssetAtPath(path);
        if(old){if(old.GetType()!=value.GetType())throw new Exception("Native UI generated asset type changed");EditorUtility.CopySerialized(value,old);UnityEngine.Object.DestroyImmediate(value);EditorUtility.SetDirty(old);}
        else AssetDatabase.CreateAsset(value,path);
    }
    public static void Run()
    {
        Directory.CreateDirectory(Assets+"Meshes");Directory.CreateDirectory(Assets+"Textures");AssetDatabase.Refresh();
        var manifest=Load(Capture+"manifest.json");var bindings=Load(Live+"mesh-bindings.json");var roots=Load(Live+"native-root-bindings.json");var uniform=Load(Live+"uniform-contract.json");
        if(!(bool)bindings["pass"]||!(bool)roots["pass"]||!(bool)uniform["pass"])throw new Exception("Native UI preparation gate failed");
        var contract=uniform["shaders"].ToDictionary(s=>(string)s["hash"]);
        var names=bindings["draws"].ToDictionary(d=>(string)d["id"],d=>(string)d["mesh"]);
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/V3/Remielle_V3_Animated.prefab");
        var renderers=prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var sources=bindings["meshes"].Select(m=>
        {
            string name=(string)m["name"];Verify((string)m["unityGlb"]["path"],(string)m["unityGlb"]["sha256"]);
            var smr=renderers.Single(r=>r.name=="SMR_"+name&&r.enabled);
            return new RemielleNativeUIProfile.SourceMesh{name=name,sourceBlock=(string)m["sourceBlock"],cab=(string)m["cab"],pathID=(string)m["pathID"],importedMesh=smr.sharedMesh,
                rootName=(string)roots["meshes"].Single(r=>(string)r["mesh"]==name)["rootName"]};
        }).ToArray();
        var textures=new Dictionary<string,Texture>();
        foreach(JObject row in manifest["textures"])
        {
            string path=(string)row["path"],asset=(string)row["asset"];
            Verify(path,(string)row["sha256"]);
            Texture texture;
            if(asset!=null)texture=AssetDatabase.LoadAssetAtPath<Texture>(asset);
            else
            {
                string target=Assets+"Textures/"+Sha(path)+".asset";
                Save(RemielleUILiveAssetFactory.LoadExtra(row),target);texture=AssetDatabase.LoadAssetAtPath<Texture>(target);
            }
            if(!texture)throw new Exception("Native UI source texture missing");textures.Add(path,texture);
        }
        var outputs=new JArray();
        foreach(string profileName in new[]{"display","store"})
        {
            var profile=ScriptableObject.CreateInstance<RemielleNativeUIProfile>();
            profile.profileName=profileName;profile.sourceManifestSha256=Sha(Capture+"manifest.json");profile.sourceMeshes=sources;
            profile.orderedShader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/RenderingReview/Shader/GeneratedNativeUIReplay/CapturedNativeUIReplay.shader");
            profile.depthReaderShader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/RenderingReview/Shader/GeneratedNativeUIReplay/CapturedUIReadDepthStencil.shader");
            profile.sceneToProfile=Matrix4x4.TRS(new Vector3(profileName=="display"?500:600,0,0),Quaternion.Euler(0,180,0),Vector3.one);
            var rows=manifest["cases"].Cast<JObject>().Where(d=>(string)d["profile"]==profileName).ToArray();
            var headCB=rows.Single(d=>(int)d["relative"]==4)["constantBuffers"].Single(c=>(string)c["stage"]=="vs"&&(int)c["slot"]==1);
            var bytes=File.ReadAllBytes((string)headCB["path"]);var head=Matrix4x4.zero;for(int i=0;i<16;i++)head[i]=BitConverter.ToSingle(bytes,i*4);profile.capturedHead=head;
            profile.draws=rows.Select(row=>
            {
                foreach(var vb in row["vertexBuffers"])Verify((string)vb["path"],(string)vb["sha256"]);
                var binding=bindings["draws"].Single(d=>(string)d["id"]==(string)row["id"]);
                Verify((string)binding["capturedIndices"]["path"],(string)binding["capturedIndices"]["sha256"]);
                string meshPath=Assets+"Meshes/"+row["id"]+".asset";Save(RemielleUILiveAssetFactory.BuildMesh(row),meshPath);
                var draw=new RemielleNativeUIProfile.Draw{captureID=(string)row["id"],relative=(int)row["relative"],role=(string)row["role"],
                    meshIndex=Array.FindIndex(sources,s=>s.name==names[(string)row["id"]]),template=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath),
                    samplerS2B=row["samplers"].Any(s=>(string)s["slot"]=="s2"&&(int)s["group"]==1)};
                draw.constants=row["constantBuffers"].Select(cb=>
                {
                    Verify((string)cb["path"],(string)cb["sha256"]);
                    string stage=(string)cb["stage"],hash=(string)row[stage];int slot=(int)cb["slot"];
                    var fields=contract.TryGetValue(hash,out var shader)?shader["constantBuffers"].Single(c=>(int)c["slot"]==slot)["variables"].Select(f=>new RemielleNativeUIConstants.Field{name=(string)f["name"],offsetBytes=(int)f["offsetBytes"],storageBytes=(int)f["storageBytes"]}).ToArray():Array.Empty<RemielleNativeUIConstants.Field>();
                    return new RemielleNativeUIProfile.Constant{stage=stage,shader=hash,slot=slot,fields=fields,template=File.ReadAllBytes((string)cb["path"]).Take((int)cb["declaredFloat4"]*16).ToArray()};
                }).ToArray();
                draw.resources=row["resources"].Select(r=>
                {
                    string path=(string)r["path"];Verify(path,(string)r["sha256"]);
                    return new RemielleNativeUIProfile.Resource{binding="_Seq"+((string)r["stage"]).ToUpperInvariant()+((string)r["slot"]).ToUpperInvariant(),texture=(int)r["kind"]==0?null:textures[path],structured=(int)r["kind"]==0?File.ReadAllBytes(path):Array.Empty<byte>()};
                }).ToArray();return draw;
            }).ToArray();
            string profilePath=Assets+profileName+".asset";Save(profile,profilePath);outputs.Add(profilePath);
        }
        AssetDatabase.SaveAssets();
        File.WriteAllText(Live+"runtime-profiles.json",new JObject{["schema"]="remielle-ui-runtime-profiles-v1",["profiles"]=new JArray(outputs.Select(p=>new JObject{["path"]=Path.GetFullPath((string)p),["sha256"]=Sha((string)p)})),["draws"]=48,["sourceMeshes"]=7,["boundary"]="Generated source-qualified runtime data. Original raw sampler descriptors and unobserved lighting behavior remain explicit boundaries."}.ToString());
        Debug.Log("REMIELLE_UI_LIVE_PROFILES_BUILT 2 profiles, 48 draws, 7 meshes");
    }
}
