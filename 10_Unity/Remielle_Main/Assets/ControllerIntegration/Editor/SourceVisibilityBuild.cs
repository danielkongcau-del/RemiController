using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Remielle.Controller.Editor
{
    public static class SourceVisibilityBuild
    {
        const string Root="E:/ZZZ/";
        const string Contract="local-only/RemielleControllerPreparation/20260906/visibility-events.json";
        public const string AssetPath="Assets/ControllerIntegration/Data/source-visibility-events.json";
        static string Sha(string path){using var f=File.OpenRead(path);using var h=SHA256.Create();return BitConverter.ToString(h.ComputeHash(f)).Replace("-","").ToLowerInvariant();}
        static void Verify(string path,string expected){if(Sha(path)!=expected)throw new Exception("Visibility source hash differs: "+path);}
        public static SourceRendererVisibility.Binding[] Prepare(GameObject visual)
        {
            var contract=JObject.Parse(File.ReadAllText(Root+Contract));
            foreach(var row in contract["actions"])
            {
                string path=(string)row["source"]["path"];Verify(path,(string)row["source"]["sha256"]);
                JToken original=JObject.Parse(File.ReadAllText(path));
                foreach(string part in ((string)row["pointer"]).Split('/').Skip(1))
                    original=original is JArray a?a[int.Parse(part)]:original[part.Replace("~1","/").Replace("~0","~")];
                if(!JToken.DeepEquals(original,row["action"]))throw new Exception("Visibility action differs from its source pointer");
            }
            var selection=JObject.Parse(File.ReadAllText(Root+"local-only/RemielleRuntimeRepair/20260904/runtime-source-selection.json"));
            var targets=contract["actions"].SelectMany(a=>a["rendererTargets"]).GroupBy(t=>(string)t["name"]).Select(g=>g.First()).ToArray();
            var all=visual.GetComponentsInChildren<SkinnedMeshRenderer>(true);var evidence=new JArray();
            var bindings=targets.Select(t=>
            {
                string name=(string)t["name"];
                var row=selection["meshes"].Single(m=>(string)m["name"]==name);
                string rendererPath=(string)row["rendererSource"];Verify(rendererPath,(string)row["rendererSourceSha256"]);
                var source=JObject.Parse(File.ReadAllText(rendererPath));var dir=new FileInfo(rendererPath).Directory;
                if(dir.Name!=(string)t["rendererPathID"]||dir.Parent.Name!=(string)t["cab"]||dir.Parent.Parent.Name!=Path.GetFileNameWithoutExtension((string)t["sourceBlock"])||
                    (long)row["rendererPathID"]!=(long)t["rendererPathID"]||(string)source["m_GameObject"]["Name"]!=name)throw new Exception("Renderer source identity mismatch");
                var renderer=all.Single(r=>r.name=="SMR_"+name&&r.enabled);
                string meshPath="Assets/SourceAssets/Meshes/"+name+".glb";
                if(AssetDatabase.GetAssetPath(renderer.sharedMesh)!=meshPath||renderer.sharedMesh.vertexCount!=(int)row["vertexCount"]||
                    renderer.sharedMesh.subMeshCount!=(int)row["submeshCount"]||renderer.bones.Length!=(int)row["boneCount"])throw new Exception("Current presentation mesh does not match the selected binding: "+name);
                evidence.Add(new JObject{["name"]=name,["sourceBlock"]=t["sourceBlock"],["cab"]=t["cab"],["rendererPathID"]=t["rendererPathID"],
                    ["rendererSourceSha256"]=row["rendererSourceSha256"],["currentImportedGlb"]=meshPath,["currentImportedGlbSha256"]=Sha(meshPath)});
                return new SourceRendererVisibility.Binding{sourceBlock=(string)t["sourceBlock"],cab=(string)t["cab"],pathID=(string)t["rendererPathID"],renderer=renderer,importedMesh=renderer.sharedMesh};
            }).ToArray();
            if(bindings.Length!=27||all.Length!=29)throw new Exception("Unexpected model renderer set");
            File.WriteAllText(AssetPath,contract.ToString());AssetDatabase.ImportAsset(AssetPath);
            File.WriteAllText(QualifiedIntegrationAudit.Output+"/source-visibility-bindings.json",new JObject{["pass"]=true,["sourceContractSha256"]=Sha(Root+Contract),["bindings"]=evidence,
                ["scope"]="Current canonical presentation mesh references and source renderer identities; no new live Mesh pointer claim"}.ToString());
            return bindings;
        }
    }
}
