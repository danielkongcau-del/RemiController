// Temporary read-only audit entrypoint. No AssetDatabase mutation or prefab saving.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class RepairAssetAudit
{
    const string Output = "E:/ZZZ/ZCode/90_Builds/RuntimeRepair/20260904/unity-import.json";
    static float F(JToken v) => (float)v;
    static JArray Vec(Vector3 v) => new JArray(v.x, v.y, v.z);
    public static void Run()
    {
        RunTo(Output);
    }
    public static void RunTo(string output)
    {
        var result = new JObject();
        result["unityVersion"] = Application.unityVersion;
        var meshes = new JArray();
        foreach (var path in Directory.GetFiles("Assets/SourceAssets/Meshes", "*.glb"))
        {
            var row = new JObject { ["path"] = path };
            try
            {
                var bytes = File.ReadAllBytes(path);
                var g = JObject.Parse(System.Text.Encoding.UTF8.GetString(bytes,20,BitConverter.ToInt32(bytes,12)));
                var source = (string)g["extras"]["remielleRecovery"]["source"];
                var src = JObject.Parse(File.ReadAllText(source));
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var smr = prefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
                var m = smr.sharedMesh;
                var verts = m.vertices;
                float posDev = 0, normalDev = 0, tangentDev = 0, colorDev = 0;
                int normalWidth = ((JArray)src["m_Normals"]).Count / verts.Length;
                for (int i = 0; i < verts.Length; ++i)
                {
                    var want = new Vector3(-F(src["m_Vertices"][3*i]),F(src["m_Vertices"][3*i+1]),-F(src["m_Vertices"][3*i+2]));
                    posDev = Mathf.Max(posDev,(verts[i]-want).magnitude);
                }
                var normals = m.normals;
                for (int i=0;i<normals.Length;++i)
                {
                    var want = new Vector3(-F(src["m_Normals"][normalWidth*i]),F(src["m_Normals"][normalWidth*i+1]),-F(src["m_Normals"][normalWidth*i+2]));
                    normalDev = Mathf.Max(normalDev,(normals[i]-want).magnitude);
                }
                var tangents=m.tangents;
                for(int i=0;i<tangents.Length;++i)
                {
                    var want=new Vector4(-F(src["m_Tangents"][4*i]),F(src["m_Tangents"][4*i+1]),-F(src["m_Tangents"][4*i+2]),F(src["m_Tangents"][4*i+3]));
                    tangentDev=Mathf.Max(tangentDev,(tangents[i]-want).magnitude);
                }
                var colors=m.colors;
                for(int i=0;i<colors.Length;++i)
                {
                    var want=new Color(F(src["m_Colors"][4*i]),F(src["m_Colors"][4*i+1]),F(src["m_Colors"][4*i+2]),F(src["m_Colors"][4*i+3]));
                    colorDev=Mathf.Max(colorDev,((Vector4)colors[i]-(Vector4)want).magnitude);
                }
                var uvs = new JArray();
                for(int channel=0;channel<8;++channel)
                {
                    var values=src["m_UV"+channel] as JArray;
                    if(values==null||values.Count==0)continue;
                    var uv=new List<Vector2>();m.GetUVs(channel,uv);
                    float rawDev=0,flipDev=0;
                    for(int i=0;i<uv.Count;++i)
                    {
                        var want=new Vector2(F(values[2*i]),F(values[2*i+1]));
                        rawDev=Mathf.Max(rawDev,(uv[i]-want).magnitude);
                        want.y=1-want.y;flipDev=Mathf.Max(flipDev,(uv[i]-want).magnitude);
                    }
                    uvs.Add(new JObject{["channel"]=channel,["count"]=uv.Count,["sourceCount"]=values.Count/2,["rawMaxError"]=rawDev,["flippedMaxError"]=flipDev});
                }
                row["vertices"]=verts.Length;row["sourceVertices"]=(int)src["m_VertexCount"];
                row["submeshes"]=m.subMeshCount;row["blendshapes"]=m.blendShapeCount;
                row["positionMaxError"]=posDev;row["normalMaxError"]=normalDev;row["tangentMaxError"]=tangentDev;row["colorMaxError"]=colorDev;
                row["uvs"]=uvs;row["importedRootBone"]=smr.rootBone?.name;
            }
            catch(Exception ex){row["error"]=ex.ToString();}
            meshes.Add(row);
        }
        result["meshes"]=meshes;
        var prefabs=new JArray();
        foreach(var p in new[]{"Assets/V2a/Remielle_V2a_Assembled.prefab","Assets/V2b/Remielle_V2b_Materials.prefab"})
        {
            var root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(p));
            try
            {
                var row=new JObject{["path"]=p};var skinRows=new JArray();
                foreach(var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var m=smr.sharedMesh;var verts=m.vertices;var bps=m.bindposes;var bones=smr.bones;var ws=m.boneWeights;
                    float maxDev=0;int invalid=0;
                    for(int i=0;i<verts.Length;++i)
                    {
                        var v=verts[i];var w=ws[i];var acc=Vector3.zero;
                        var ids=new[]{w.boneIndex0,w.boneIndex1,w.boneIndex2,w.boneIndex3};var weights=new[]{w.weight0,w.weight1,w.weight2,w.weight3};
                        for(int k=0;k<4;++k)if(weights[k]>0)
                        {
                            if(ids[k]>=bones.Length||bones[ids[k]]==null){invalid++;continue;}
                            acc+=weights[k]*(bones[ids[k]].localToWorldMatrix*bps[ids[k]]).MultiplyPoint3x4(v);
                        }
                        maxDev=Mathf.Max(maxDev,(acc-smr.transform.localToWorldMatrix.MultiplyPoint3x4(v)).magnitude);
                    }
                    skinRows.Add(new JObject{["name"]=smr.name,["vertices"]=verts.Length,["rootBone"]=smr.rootBone?.name,["invalidBones"]=invalid,["allVertexSkinMaxError"]=maxDev,["submeshes"]=m.subMeshCount,["materialNames"]=new JArray(smr.sharedMaterials.Select(x=>x?x.name:"NULL"))});
                }
                var ts=root.GetComponentsInChildren<Transform>(true);
                row["skin"]=skinRows;row["head"]=Vec(ts.First(x=>x.name=="Bip001 Head").position);row["pelvis"]=Vec(ts.First(x=>x.name=="Bip001 Pelvis").position);row["mouth"]=Vec(ts.First(x=>x.name=="Skn_M_Mouth").position);
                prefabs.Add(row);
            }
            finally{Object.DestroyImmediate(root);}
        }
        result["prefabs"]=prefabs;
        var materials=new JArray();
        foreach(var p in Directory.GetFiles("Assets/V2b/MaterialJson","*.mat"))
        {
            var m=AssetDatabase.LoadAssetAtPath<Material>(p);var floats=new JObject();var colors=new JObject();var textures=new JObject();var typeMismatches=new JArray();
            var staged=JObject.Parse(File.ReadAllText(Path.ChangeExtension(p,".json")))["m_SavedProperties"];
            foreach(var kv in (JObject)staged["m_Floats"])if(m.HasProperty(kv.Key))floats[kv.Key]=m.GetFloat(kv.Key);
            foreach(var kv in (JObject)staged["m_Colors"])if(m.HasProperty(kv.Key))
            {
                var kind=m.shader.GetPropertyType(m.shader.FindPropertyIndex(kv.Key)).ToString();
                if(kind!="Color"&&kind!="Vector"){typeMismatches.Add(new JObject{["property"]=kv.Key,["gameType"]="Color/Vector4",["shaderType"]=kind});continue;}
                var c=m.GetColor(kv.Key);colors[kv.Key]=new JArray(c.r,c.g,c.b,c.a);
            }
            foreach(var kv in (JObject)staged["m_TexEnvs"])
            {
                if(!m.HasProperty(kv.Key)){textures[kv.Key]=new JObject{["hasProperty"]=false};continue;}
                var t=m.GetTexture(kv.Key);var sc=m.GetTextureScale(kv.Key);var off=m.GetTextureOffset(kv.Key);
                textures[kv.Key]=new JObject{["hasProperty"]=true,["name"]=t?t.name:null,["path"]=t?AssetDatabase.GetAssetPath(t):null,["width"]=t?t.width:0,["height"]=t?t.height:0,["scale"]=new JArray(sc.x,sc.y),["offset"]=new JArray(off.x,off.y)};
                if(t is Texture2D tex){textures[kv.Key]["format"]=tex.format.ToString();textures[kv.Key]["mipCount"]=tex.mipmapCount;}
            }
            var messages=new JArray();
            var method=typeof(ShaderUtil).GetMethods(BindingFlags.Public|BindingFlags.Static).FirstOrDefault(x=>x.Name=="GetShaderMessages"&&x.GetParameters().Length==1);
            if(method!=null)foreach(var msg in (Array)method.Invoke(null,new object[]{m.shader}))
            {
                var t=msg.GetType();messages.Add(new JObject{["severity"]=(t.GetField("severity")?.GetValue(msg)??t.GetProperty("severity")?.GetValue(msg))?.ToString(),["message"]=(t.GetField("message")?.GetValue(msg)??t.GetProperty("message")?.GetValue(msg))?.ToString()});
            }
            materials.Add(new JObject{["name"]=m.name,["shader"]=m.shader.name,["renderQueue"]=m.renderQueue,["floats"]=floats,["colors"]=colors,["textures"]=textures,["propertyTypeMismatches"]=typeMismatches,["shaderMessages"]=messages});
        }
        result["materials"]=materials;
        var errors = new JArray();
        foreach (var row in meshes)
        {
            if (row["error"] != null) { errors.Add(row["error"].ToString()); continue; }
            foreach (var key in new[] { "positionMaxError", "normalMaxError", "tangentMaxError", "colorMaxError" })
                if ((float)row[key] > 1e-5f) errors.Add(row["path"] + ": " + key);
            if ((int)row["vertices"] != (int)row["sourceVertices"]) errors.Add("vertex count");
            foreach (var uv in row["uvs"])
                if ((int)uv["count"] != (int)uv["sourceCount"] || (float)uv["rawMaxError"] > 1e-5f) errors.Add(row["path"] + ": UV " + uv["channel"]);
        }
        foreach (var row in prefabs)
        {
            if ((float)row["head"][1] <= (float)row["pelvis"][1] || (float)row["mouth"][2] <= (float)row["head"][2]) errors.Add("orientation");
            foreach (var skin in row["skin"])
                if ((int)skin["invalidBones"] != 0 || (float)skin["allVertexSkinMaxError"] > 1e-5f) errors.Add("skinning: " + skin["name"]);
        }
        foreach (var row in materials)
        {
            var staged = JObject.Parse(File.ReadAllText("Assets/V2b/MaterialJson/" + row["name"] + ".json"))["m_SavedProperties"];
            foreach (var kv in (JObject)row["floats"])
                if (Math.Abs((float)kv.Value - (float)staged["m_Floats"][kv.Key]) > 1e-4f) errors.Add(row["name"] + ": " + kv.Key);
            foreach (var kv in (JObject)row["colors"])
            {
                int i=0; foreach (var c in new[]{"r","g","b","a"})
                    if (Math.Abs((float)kv.Value[i++] - (float)staged["m_Colors"][kv.Key][c]) > 1e-4f) errors.Add(row["name"] + ": " + kv.Key);
            }
            if (((JArray)row["propertyTypeMismatches"]).Count > 0) errors.Add("material type mismatch");
            foreach (var msg in row["shaderMessages"]) if ((string)msg["severity"] == "Error") errors.Add(msg["message"].ToString());
        }
        var textureRows = new JArray();
        foreach (var path in Directory.GetFiles("Assets/SourceAssets/Textures", "*.png"))
        {
            var bytes=File.ReadAllBytes(path);
            int width=(bytes[16]<<24)|(bytes[17]<<16)|(bytes[18]<<8)|bytes[19];
            int height=(bytes[20]<<24)|(bytes[21]<<16)|(bytes[22]<<8)|bytes[23];
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            textureRows.Add(new JObject{["path"]=path,["sourceWidth"]=width,["sourceHeight"]=height,["importedWidth"]=texture.width,["importedHeight"]=texture.height,["maxSize"]=importer.maxTextureSize});
            if (texture.width!=width||texture.height!=height||importer.maxTextureSize<Math.Max(width,height))errors.Add("texture resolution: "+path);
        }
        result["textureResolution"]=textureRows;
        result["errors"] = errors; result["pass"] = errors.Count == 0;
        File.WriteAllText(output,result.ToString());
        if (errors.Count > 0) throw new InvalidOperationException(errors.ToString());
        Debug.Log("WORKSPACE_ASSET_AUDIT_COMPLETE "+output);
    }
}
