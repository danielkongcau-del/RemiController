using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

public static class RemielleUILiveTemporalAudit
{
    const string Root="E:/ZZZ/local-only/RemielleRenderingReview/20260905/";
    const string Out=Root+"ui-live-binding/live-temporal/";
    static JObject Ref(string p)=>RemielleUINativePostBuild.Ref(p);
    static string Quote(string p)=>"\""+p.Replace('\\','/')+"\"";
    static byte[] Bytes(Vector4[] a){var b=new byte[a.Length*16];var pin=GCHandle.Alloc(a,GCHandleType.Pinned);try{Marshal.Copy(pin.AddrOfPinnedObject(),b,0,b.Length);}finally{pin.Free();}return b;}
    static JArray Matrix(Matrix4x4 m)=>new JArray(Enumerable.Range(0,16).Select(i=>m[i]));
    static int PrivateTargets()=>Resources.FindObjectsOfTypeAll<RenderTexture>().Count(t=>t.hideFlags==HideFlags.HideAndDontSave);
    public static void Run()
    {
        Directory.CreateDirectory(Out+"inputs");Directory.CreateDirectory(Out+"unity");
        var deferred=JObject.Parse(File.ReadAllText(Root+"captured-deferred-character-ui.json"));var source=JObject.Parse(File.ReadAllText(RemielleUITemporalCaptureAudit.Root+"manifest.json"));
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);var root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/V3/Remielle_V3_Animated.prefab"));var driver=root.GetComponent<RemielleNativeAnimation>();
        var go=new GameObject("Native temporal audit camera");var camera=go.AddComponent<Camera>();camera.enabled=false;camera.fieldOfView=38;camera.nearClipPlane=.1f;camera.farClipPlane=50;
        var rows=new JArray();var lifecycle=new JArray();var lines=new StringBuilder("48\n");
        try
        {
            foreach(string name in new[]{"display","store"})
            {
                int before=PrivateTargets();
                var row=deferred["cases"].Single(c=>(string)c["key"]==name);var f=row["pixelConstants"].Select(v=>(float)v).ToArray();var lutSource=row["inputs"].Single(i=>(int)i["slot"]==5);
                var lut=AssetDatabase.LoadAssetAtPath<Texture>(RemielleUILiveProfileBuild.Assets+"Textures/"+lutSource["sourceSha256"]+".asset");var profile=AssetDatabase.LoadAssetAtPath<RemielleNativeUIProfile>(RemielleUILiveProfileBuild.Assets+name+".asset");
                var temporalProfile=AssetDatabase.LoadAssetAtPath<RemielleNativeUITemporalProfile>(RemielleUITemporalCaptureAudit.Assets+name+".asset");
                using(var geometry=new RemielleNativeUIRenderer(profile,driver,camera))
                using(var lighting=new RemielleNativeUILighting(AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/CapturedDeferredCharacter523.shader"),lut,RemielleReviewBloom.Vectors(f,185)))
                using(var temporal=new RemielleNativeUITemporal(temporalProfile))
                using(var late=new RemielleNativeUILateBody(AssetDatabase.LoadAssetAtPath<RemielleNativeUILateBodyProfile>(RemielleUILateBodyBuild.Assets+name+".asset")))
                {
                    driver.ResetSourcePose();driver.ApplyPose();camera.aspect=16f/9;RemielleUILiveRasterAudit.FrameModel(geometry,camera,0);
                    for(int frame=0;frame<24;frame++)
                    {
                        int w=frame<21?640:800,h=frame<21?360:600;bool reset=frame==0||frame==22;string sample="__rest";float time=0;
                        if(frame>=16&&frame<22){sample="Walk_Start";time=(frame-16)/60f;driver.Sample(sample,time);}
                        else if(frame>=22){sample="Idle_Loop";time=.7f+(frame-22)/60f;driver.Sample(sample,time);}
                        camera.aspect=(float)w/h;if(frame>=20)RemielleUILiveRasterAudit.FrameModel(geometry,camera,20);
                        bool historyBefore=temporal.HasHistory;Vector2 jitter=temporal.BeginFrame(w,h,reset);bool first=temporal.Constants[167].x>0;uint sampleIndex=temporal.SampleIndex;
                        geometry.Prepare(w,h,jitter);late.Prepare(geometry,driver);geometry.Render();lighting.Render(geometry,camera);late.Draw(lighting.Hdr,geometry.Depth);
                        // Source payload lineage proves t1 is the Deferred
                        // auxiliary, not the pre-resolve material flag target.
                        temporal.Render(lighting.SampledDepth,lighting.Auxiliary,lighting.Hdr);
                        string id=name+"-"+frame.ToString("D2"),stem=Out+"inputs/"+id;var inputs=new JArray();var textures=new Texture[]{lighting.SampledDepth,lighting.Auxiliary,lighting.Hdr,temporal.HistoryColorUsed,temporal.HistoryTagUsed};int[] formats={41,24,10,10,61},pixelBytes={4,4,8,8,1};
                        string cb=stem+"-cb0.buf";File.WriteAllBytes(cb,Bytes(temporal.Constants));
                        lines.AppendLine(id+" "+w+" "+h+" 3 4 "+Quote((string)source["originalVS"]["path"])+" "+Quote((string)source["originalPS"]["path"])+" 2\n10 \"-\"\n61 \"-\"\n2 1 2 1 15\n2 1 2 1 1\n1\n1 0 "+Quote(cb)+"\n5");
                        for(int slot=0;slot<5;slot++)
                        {
                            string path=stem+"-t"+slot+".raw";File.WriteAllBytes(path,RemielleUINativePostAudit.Read(textures[slot],w*h*pixelBytes[slot]));inputs.Add(new JObject{["slot"]=slot,["format"]=formats[slot],["file"]=Ref(path)});lines.AppendLine("1 "+slot+" "+w+" "+h+" "+formats[slot]+" "+Quote(path));
                        }
                        lines.AppendLine("2\n1 0 21 3\n1 1 0 3");
                        string color=Out+"unity/"+id+"-o0.raw",tag=Out+"unity/"+id+"-o1.raw";File.WriteAllBytes(color,RemielleUINativePostAudit.Read(temporal.Color,w*h*8));File.WriteAllBytes(tag,RemielleUINativePostAudit.Read(temporal.Tag,w*h));
                        var current=geometry.CurrentFrame;rows.Add(new JObject{["id"]=id,["profile"]=name,["frame"]=frame,["width"]=w,["height"]=h,["sample"]=sample,["time"]=time,["firstFrame"]=first,["hadHistoryBeforeBegin"]=historyBefore,["explicitReset"]=reset,["sampleIndex"]=sampleIndex,["jitter"]=new JArray(jitter.x,jitter.y),["inputs"]=inputs,["constants"]=Ref(cb),["color"]=Ref(color),["tag"]=Ref(tag),["projection"]=Matrix(current.projection),["nonJitteredProjection"]=Matrix(current.nonJitteredProjection),["nonJitteredVP"]=Matrix(current.nonJitteredVP),["previousVP"]=Matrix(current.previousVP)});
                    }
                }
                int after=PrivateTargets();if(after!=before)throw new Exception("Temporal/native private target leak: "+before+" -> "+after);lifecycle.Add(new JObject{["profile"]=name,["before"]=before,["after"]=after});
            }
            File.WriteAllText(Out+"inputs.txt",lines.ToString(),new UTF8Encoding(false));var files=new JArray();foreach(string path in new[]{"Assets/Editor/RemielleUILiveTemporalAudit.cs","Assets/RenderingReview/Runtime/RemielleNativeUITemporal.cs","Assets/RenderingReview/Runtime/RemielleNativeUIConstants.cs","Assets/RenderingReview/Runtime/RemielleNativeUIRenderer.cs","Assets/RenderingReview/Runtime/RemielleNativeUILighting.cs","Assets/RenderingReview/Shader/GeneratedNativeUIReplay/NativeUITemporal.shader",RemielleUITemporalCaptureAudit.Assets+"display.asset",RemielleUITemporalCaptureAudit.Assets+"store.asset"})files.Add(Ref(path));
            foreach(string path in new[]{"Assets/RenderingReview/Runtime/RemielleNativeUILateBody.cs",RemielleUILateBodyBuild.Assets+"display.asset",RemielleUILateBodyBuild.Assets+"store.asset"})files.Add(Ref(path));
            File.WriteAllText(Out+"unity.json",new JObject{["schema"]="remielle-native-ui-live-temporal-readback-v1",["cases"]=rows,["lifecycle"]=lifecycle,["implementationFiles"]=files,["manifest"]=Ref(RemielleUITemporalCaptureAudit.Root+"manifest.json"),["inputList"]=Ref(Out+"inputs.txt"),["lateBodyIntegrated"]=true,["boundary"]="Current native geometry/Deferred, post-Deferred Body_1 and original temporal resolve. Includes jitter-only static frames, animation, camera movement, resize and explicit seek reset. Dynamic light/auxiliary producers and final visible Player remain pending."}.ToString());Debug.Log("REMIELLE_UI_LIVE_TEMPORAL_READBACK "+rows.Count);
        }
        finally{Object.DestroyImmediate(go);Object.DestroyImmediate(root);}
    }
}
