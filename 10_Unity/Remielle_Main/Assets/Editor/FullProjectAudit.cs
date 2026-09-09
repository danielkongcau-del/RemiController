using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

public static class FullProjectAudit
{
    static string Out="E:/ZZZ/ZCode/90_Builds/ModelReadiness/20260904";
    static readonly List<string> errors=new();
    static void Require(bool pass,string message){if(!pass)errors.Add(message);}
    public static void Run() => RunChecks();
    public static void RunReadOnly(string output) { Out=output; RunChecks(); }
    static void RunChecks()
    {
        Directory.CreateDirectory(Out);errors.Clear();
        RepairAssetAudit.RunTo(Out+"/unity-import.json");
        RuntimeLutAudit.VerifyTo(Out+"/lut-gpu-audit.json");FaceRepairBuild.VerifyTo(Out+"/face");
        NativeRotationAudit.Audit(Out+"/rotation.json",true);
        var meshRows=new JArray();
        foreach(string path in Directory.GetFiles("Assets/SourceAssets/Meshes","*.glb"))
        {
            var bytes=File.ReadAllBytes(path);
            var glb=JObject.Parse(System.Text.Encoding.UTF8.GetString(bytes,20,BitConverter.ToInt32(bytes,12)));
            var source=JObject.Parse(File.ReadAllText((string)glb["extras"]["remielleRecovery"]["source"]));
            var mesh=AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMesh;
            int nv=mesh.vertexCount,cursor=0;string name=mesh.name;
            Require(mesh.normals.Length==((JArray)source["m_Normals"]).Count/(((JArray)source["m_Normals"]).Count/nv),name+" normals missing");
            Require(mesh.tangents.Length==((JArray)source["m_Tangents"]).Count/4,name+" tangents missing");
            Require(mesh.colors.Length==((JArray)source["m_Colors"]).Count/4,name+" colors missing");
            Require(mesh.subMeshCount==((JArray)source["m_SubMeshes"]).Count,name+" submesh count");
            for(int s=0;s<mesh.subMeshCount;s++)
            {
                var indices=mesh.GetIndices(s);Require(indices.Length==(int)source["m_SubMeshes"][s]["indexCount"],name+" index count");
                for(int i=0;i<indices.Length;i+=3)
                {
                    // S and C flip winding using different swaps, leaving a
                    // cyclic vertex rotation. Cyclic permutations preserve the
                    // oriented triangle; reversed winding must still fail.
                    var expected=Enumerable.Range(0,3).Select(k=>(int)source["m_Indices"][cursor+i+k]).ToArray();
                    bool same=Enumerable.Range(0,3).Any(r=>Enumerable.Range(0,3).All(k=>indices[i+k]==expected[(k+r)%3]));
                    if(!same){errors.Add(name+" oriented triangle mismatch at "+(cursor+i));break;}
                }
                cursor+=indices.Length;
            }
            var weights=mesh.boneWeights;Require(weights.Length==nv,name+" weights count");float weightError=0;
            for(int i=0;i<weights.Length;i++)
            {
                var w=weights[i];var ids=new[]{w.boneIndex0,w.boneIndex1,w.boneIndex2,w.boneIndex3};var ws=new[]{w.weight0,w.weight1,w.weight2,w.weight3};
                // Importers may reorder influences; compare summed weights by joint.
                var expected=new Dictionary<int,float>();var actual=new Dictionary<int,float>();
                for(int k=0;k<4;k++)
                {
                    int id=(int)source["m_Skin"][i]["boneIndex"][k];float value=(float)source["m_Skin"][i]["weight"][k];
                    expected[id]=expected.GetValueOrDefault(id)+value;actual[ids[k]]=actual.GetValueOrDefault(ids[k])+ws[k];
                }
                foreach(int id in expected.Keys.Union(actual.Keys))weightError=Mathf.Max(weightError,Mathf.Abs(expected.GetValueOrDefault(id)-actual.GetValueOrDefault(id)));
            }
            Require(weightError<1e-6,name+" skin influence mismatch");
            var channels=(JArray)source["m_Shapes"]["channels"];Require(mesh.blendShapeCount==channels.Count,name+" morph count");float morphError=0;
            for(int ch=0;ch<channels.Count;ch++)
            {
                var channel=channels[ch];Require(mesh.GetBlendShapeName(ch)==(string)channel["name"],name+" morph name");
                Require(mesh.GetBlendShapeFrameCount(ch)==(int)channel["frameCount"],name+" morph frames");
                for(int f=0;f<(int)channel["frameCount"];f++)
                {
                    var verts=new Vector3[nv];var normals=new Vector3[nv];var tangents=new Vector3[nv];mesh.GetBlendShapeFrameVertices(ch,f,verts,normals,tangents);
                    var frame=source["m_Shapes"]["shapes"][(int)channel["frameIndex"]+f];var expected=new Dictionary<int,JToken>();
                    for(int i=(int)frame["firstVertex"];i<(int)frame["firstVertex"]+(int)frame["vertexCount"];i++){var v=source["m_Shapes"]["vertices"][i];expected[(int)v["index"]]=v;}
                    for(int i=0;i<nv;i++)
                    {
                        expected.TryGetValue(i,out var row);
                        morphError=Mathf.Max(morphError,(verts[i]-Delta(row?["vertex"])).magnitude,(normals[i]-Delta(row?["normal"])).magnitude,(tangents[i]-Delta(row?["tangent"])).magnitude);
                    }
                    Require(Mathf.Abs(mesh.GetBlendShapeFrameWeight(ch,f)-100)<1e-5,name+" morph weight scale");
                }
            }
            Require(morphError<1e-5,name+" morph delta mismatch");
            meshRows.Add(new JObject{["mesh"]=name,["vertices"]=nv,["indices"]=cursor,["morphs"]=mesh.blendShapeCount,["influenceMaxError"]=weightError,["morphAllDeltaMaxError"]=morphError});
        }
        var root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/V3/Remielle_V3_Animated.prefab"));
        Require(root.GetComponentsInChildren<MonoBehaviour>(true).All(c=>c!=null),"Missing prefab MonoBehaviour");
        var bridge=root.GetComponent<RemielleNativeAnimation>();
        Require(bridge.bones.Length==486&&bridge.bones.All(b=>b.source&&b.target),"Native bone links");
        Require(bridge.morphs.Length==2&&bridge.morphs.All(m=>m.source&&m.target),"Morph links");
        var transitions=TransitionOrder(bridge);
        ProfileLifetime(root);
        Object.DestroyImmediate(root);
        var scene=EditorSceneManager.OpenScene("Assets/V3/Remielle_AnimationReview.unity");
        var post=Object.FindFirstObjectByType<RemiellePostLut>();
        Require(post!=null&&post.lutShader!=null&&post.lutShader.isSupported,"Post shader serialized reference/support");
        Require(AssetDatabase.GetDependencies(scene.path,true).Contains("Assets/Shaders/NativeRuntimeLut.shader"),"Player post shader dependency missing");
        Require(PlayerSettings.legacyClampBlendShapeWeights,"Native morph clamp setting");
        File.WriteAllText(Out+"/unity-full.json",new JObject{["pass"]=errors.Count==0,["utc"]=DateTime.UtcNow.ToString("O"),["meshes"]=meshRows,["orderedClipPairs"]=transitions,["errors"]=new JArray(errors)}.ToString());
        File.Copy(Out+"/face/verification.json",Out+"/face-regression.json",true);
        if(errors.Count>0)throw new InvalidOperationException(string.Join("\n",errors));
        Debug.Log("FULL_PROJECT_AUDIT_VERIFIED");
    }
    static Vector3 Delta(JToken p)=>p==null?Vector3.zero:new Vector3(-(float)p["X"],(float)p["Y"],-(float)p["Z"]);
    static JArray TransitionOrder(RemielleNativeAnimation driver)
    {
        var clips=driver.nativeAnimation.Cast<AnimationState>().ToArray();
        var nodes=driver.bones.Select(b=>b.source).ToArray();
        var rest=nodes.Select(t=>(position:t.localPosition,rotation:t.localRotation,scale:t.localScale)).ToArray();
        void Reset(){for(int i=0;i<nodes.Length;i++){nodes[i].localPosition=rest[i].position;nodes[i].localRotation=rest[i].rotation;nodes[i].localScale=rest[i].scale;}}
        Matrix4x4[] Snapshot()=>nodes.Select(t=>Matrix4x4.TRS(t.localPosition,t.localRotation,t.localScale)).ToArray();
        var cold=new Dictionary<string,Matrix4x4[]>();
        foreach(var clip in clips){Reset();driver.Sample(clip.name,clip.length*.5f);cold[clip.name]=Snapshot();}
        var rows=new JArray();
        foreach(var before in clips)foreach(var after in clips)
        {
            Reset();driver.Sample(before.name,before.length*.5f);driver.Sample(after.name,after.length*.5f);
            var got=Snapshot();float max=0;string worst="";
            for(int i=0;i<nodes.Length;i++)for(int c=0;c<16;c++){float e=Mathf.Abs(got[i][c]-cold[after.name][i][c]);if(e>max){max=e;worst=nodes[i].name;}}
            Require(max<.0001f,"History-dependent pose: "+before.name+" -> "+after.name+" / "+worst+" error "+max);
            rows.Add(new JObject{["from"]=before.name,["to"]=after.name,["maxLocalMatrixError"]=max,["worst"]=worst});
        }
        return rows;
    }
    static void ProfileLifetime(GameObject root)
    {
        var profile=root.GetComponent<RemielleRuntimeProfile>();var renderers=root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var original=renderers.Select(r=>r.sharedMaterials).ToArray();profile.Apply(true);
        var owned=renderers.SelectMany(r=>r.sharedMaterials).Where(m=>m&&m.name.EndsWith(" (Runtime Profile)")).Distinct().ToArray();
        Require(owned.Length>0&&owned.Length<=13,"Runtime profile instance count");
        for(int i=0;i<20;i++)profile.Apply(i%2==0);
        Require(renderers.SelectMany(r=>r.sharedMaterials).Where(m=>m&&m.name.EndsWith(" (Runtime Profile)")).Distinct().Count()==owned.Length,"Profile toggles allocate materials");
        Object.DestroyImmediate(profile);
        for(int i=0;i<renderers.Length;i++)Require(renderers[i].sharedMaterials.SequenceEqual(original[i]),"Profile removal failed to restore shared materials");
        Require(owned.All(m=>m==null),"Runtime profile material leak");
    }
}
