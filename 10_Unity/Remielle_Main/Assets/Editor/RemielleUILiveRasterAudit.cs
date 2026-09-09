using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

public static class RemielleUILiveRasterAudit
{
    const string Out=RemielleUILiveProfileBuild.Live+"live-raster/";
    static string Sha(string p){using var f=File.OpenRead(p);using var h=SHA256.Create();return BitConverter.ToString(h.ComputeHash(f)).Replace("-","").ToLowerInvariant();}
    static JObject Ref(string p)=>new(){["path"]=Path.GetFullPath(p),["sha256"]=Sha(p)};
    static string Quote(string p)=>"\""+p.Replace('\\','/')+"\"";
    static byte[] Bytes<T>(T[] data)where T:struct{var bytes=new byte[data.Length*Marshal.SizeOf<T>()];var pin=GCHandle.Alloc(data,GCHandleType.Pinned);try{Marshal.Copy(pin.AddrOfPinnedObject(),bytes,0,bytes.Length);}finally{pin.Free();}return bytes;}
    static void ExportMesh(Mesh mesh,string stem)
    {
        var values=new Vector4[mesh.vertexCount*9];var p=mesh.vertices;var n=mesh.normals;var t=mesh.tangents;var colors=mesh.colors;
        for(int v=0;v<mesh.vertexCount;v++){values[v*9]=new Vector4(p[v].x,p[v].y,p[v].z,1);values[v*9+1]=new Vector4(n[v].x,n[v].y,n[v].z,1);values[v*9+2]=t[v];values[v*9+3]=colors.Length>0?(Vector4)colors[v]:Vector4.one;}
        for(int channel=0;channel<5;channel++){var uv=new List<Vector4>();mesh.GetUVs(channel,uv);for(int v=0;v<mesh.vertexCount;v++)values[v*9+4+channel]=uv.Count>0?uv[v]:new Vector4(0,0,0,1);}
        File.WriteAllBytes(stem+"-vb.buf",Bytes(values));File.WriteAllBytes(stem+"-ib.buf",Bytes(mesh.GetIndices(0).Select(i=>checked((ushort)i)).ToArray()));
    }
    public static void FrameModel(RemielleNativeUIRenderer renderer,Camera camera,float yaw)
    {
        // Use actual current skinned positions, not same-frame stale SMR bounds.
        var positions=new List<Vector3>();
        foreach(var mesh in renderer.Meshes){mesh.Prepare();positions.AddRange(mesh.Positions.Select(p=>mesh.ObjectToWorld.MultiplyPoint3x4(p)));}
        var bounds=new Bounds(positions[0],Vector3.zero);foreach(var p in positions)bounds.Encapsulate(p);
        var offset=Quaternion.Euler(0,yaw,0)*new Vector3(0,.15f,3.2f).normalized;
        var rotation=Quaternion.LookRotation(-offset,Vector3.up);var inverse=Quaternion.Inverse(rotation);
        float tanY=Mathf.Tan(camera.fieldOfView*Mathf.Deg2Rad*.5f),tanX=tanY*camera.aspect,distance=0;
        foreach(var p in positions){var local=inverse*(p-bounds.center);distance=Mathf.Max(distance,Mathf.Abs(local.x)/tanX-local.z,Mathf.Abs(local.y)/tanY-local.z);}
        camera.transform.SetPositionAndRotation(bounds.center+offset*(distance*1.1f),rotation);
    }
    public static void BuildAndRun(){RemielleUILiveProfileBuild.Run();Run();}
    public static void Run()
    {
        Directory.CreateDirectory(Out+"inputs");Directory.CreateDirectory(Out+"unity");
        var manifest=JObject.Parse(File.ReadAllText(RemielleUILiveProfileBuild.Capture+"manifest.json"));
        var shaders=manifest["shaders"].ToDictionary(s=>(string)s["hash"]);
        var states=File.ReadAllLines(RemielleUILiveProfileBuild.Capture+"sequence-states.txt").Where(s=>s.Length>0).ToDictionary(s=>s.Split(' ')[0]);
        var rows=manifest["cases"].ToDictionary(d=>(string)d["id"]);
        var inputs=new StringBuilder();var stateLines=new StringBuilder();var cases=new JArray();
        const int width=960,height=540;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        var root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/V3/Remielle_V3_Animated.prefab"));
        var driver=root.GetComponent<RemielleNativeAnimation>();
        var cameraObject=new GameObject("Native UI live raster camera");var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;
        try
        {
            foreach(string name in new[]{"display","store"})
            {
                var profile=AssetDatabase.LoadAssetAtPath<RemielleNativeUIProfile>(RemielleUILiveProfileBuild.Assets+name+".asset");
                using var renderer=new RemielleNativeUIRenderer(profile,driver,camera);
                driver.ResetSourcePose();driver.ApplyPose();
                var clips=driver.nativeAnimation.Cast<AnimationState>().ToArray();
                var samples=new[]{(name:"__rest",time:0f),(name:clips[0].name,time:clips[0].length*.37f),(name:clips[clips.Length-1].name,time:clips[clips.Length-1].length*.63f)};
                for(int sampleIndex=0;sampleIndex<samples.Length;sampleIndex++)
                {
                    var sample=samples[sampleIndex];if(sample.name!="__rest")driver.Sample(sample.name,sample.time);
                    float yaw=new[]{0f,65f,180f}[sampleIndex];
                    camera.aspect=(float)width/height;camera.fieldOfView=38;camera.nearClipPlane=.1f;camera.farClipPlane=50;
                    FrameModel(renderer,camera,yaw);
                    renderer.Prepare(width,height);
                    var sampleRows=new List<JObject>();
                    foreach(var draw in renderer.Draws)
                    {
                        var source=rows[draw.source.captureID];string id=name+"-"+sampleIndex+"-"+draw.source.relative;
                        string stem=Out+"inputs/"+id;ExportMesh(draw.mesh,stem);
                        string vs=(string)source["vs"],ps=(string)source["ps"];
                        inputs.AppendLine(id+" "+draw.mesh.GetIndexCount(0)+" 0 "+Quote((string)(shaders[vs]["dxbc"] as JObject)?["path"]??"-")+" "+Quote((string)shaders[vs]["translation"]["path"])+" "+Quote((string)(shaders[ps]["dxbc"] as JObject)?["path"]??"-")+" "+Quote((string)shaders[ps]["translation"]["path"]));
                        inputs.AppendLine("9\nPOSITION 0 6 0 0\nNORMAL 0 6 0 16\nTANGENT 0 2 0 32\nCOLOR 0 2 0 48\nTEXCOORD 0 2 0 64\nTEXCOORD 1 2 0 80\nTEXCOORD 2 2 0 96\nTEXCOORD 3 2 0 112\nTEXCOORD 4 6 0 128");
                        inputs.AppendLine("1\n0 144 "+Quote(stem+"-vb.buf"));inputs.AppendLine(Quote(stem+"-ib.buf"));inputs.AppendLine(draw.constants.Length.ToString());
                        var constants=new JArray();foreach(var cb in draw.constants)
                        {
                            string path=stem+"-"+cb.source.stage+cb.source.slot+".buf";File.WriteAllBytes(path,cb.bytes);
                            inputs.AppendLine((cb.source.stage=="vs"?0:1)+" "+cb.source.slot+" "+Quote(path));constants.Add(Ref(path));
                        }
                        inputs.AppendLine(source["resources"].Count().ToString());
                        foreach(var r in source["resources"])inputs.AppendLine(((string)r["stage"]=="vs"?0:1)+" "+((string)r["slot"]).Substring(1)+" "+r["kind"]+" "+r["width"]+" "+r["height"]+" "+r["mips"]+" "+r["layers"]+" "+r["format"]+" "+Quote((string)r["path"]));
                        inputs.AppendLine(source["samplers"].Count().ToString());
                        foreach(var s in source["samplers"])inputs.AppendLine(((string)s["stage"]=="vs"?0:1)+" "+((string)s["slot"]).Substring(1)+" "+s["group"]);
                        string state=states[draw.source.captureID];stateLines.AppendLine(id+state.Substring(state.IndexOf(' ')));
                        sampleRows.Add(new JObject{["id"]=id,["profile"]=name,["sample"]=sample.name,["time"]=sample.time,["yaw"]=yaw,["relative"]=draw.source.relative,["sourceID"]=draw.source.captureID,["vs"]=vs,["ps"]=ps,["vertices"]=draw.mesh.vertexCount,["vertex"]=Ref(stem+"-vb.buf"),["index"]=Ref(stem+"-ib.buf"),["constants"]=constants});
                    }
                    renderer.Render(relative=>
                    {
                        var row=sampleRows[relative];string id=(string)row["id"];
                        row["unity"]=RemielleUILiveAssetFactory.SaveSequenceReadback(renderer.Targets,renderer.DepthRead,Out+"unity/"+id,id,width,height);cases.Add(row);
                    });
                    foreach(var m in ShaderUtil.GetShaderMessages(profile.orderedShader))if(m.severity.ToString()=="Error")throw new Exception("Live UI raster shader error: "+m.message);
                    Debug.Log("REMIELLE_UI_LIVE_RASTER_FRAME "+name+" "+sample.name+" 24 draws");
                }
            }
            File.WriteAllText(Out+"inputs.txt",cases.Count+" "+width+" "+height+"\n"+inputs,new UTF8Encoding(false));
            File.WriteAllText(Out+"states.txt",stateLines.ToString(),new UTF8Encoding(false));
            var files=new JArray();foreach(var p in new[]{"Assets/Editor/RemielleUILiveRasterAudit.cs","Assets/Editor/RemielleUILiveAssetFactory.cs","Assets/Editor/RemielleUILiveProfileBuild.cs","Assets/RenderingReview/Runtime/RemielleNativeUIRenderer.cs","Assets/RenderingReview/Runtime/RemielleNativeUIProfile.cs","Assets/RenderingReview/Runtime/RemielleNativeUIConstants.cs","Assets/RenderingReview/Runtime/RemielleNativeUIMeshBinding.cs","Assets/V3/Remielle_V3_Animated.prefab",RemielleUILiveProfileBuild.Live+"runtime-profiles.json"})files.Add(Ref(p));
            File.WriteAllText(Out+"unity.json",new JObject{["schema"]="remielle-ui-live-raster-v1",["width"]=width,["height"]=height,["cases"]=cases,["implementationFiles"]=files,["inputs"]=Ref(Out+"inputs.txt"),["states"]=Ref(Out+"states.txt"),["boundary"]="Current skin and camera, 24 ordered native draws. Captured lighting preset with head-local sphere update; dynamic light/shadow producers, accessory policy and final visible color chain are separate gates."}.ToString());
            Debug.Log("REMIELLE_UI_LIVE_RASTER_READBACK "+cases.Count);
        }
        finally {Object.DestroyImmediate(cameraObject);Object.DestroyImmediate(root);}
    }
}
