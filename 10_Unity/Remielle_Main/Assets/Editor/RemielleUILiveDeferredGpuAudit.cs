using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

public static class RemielleUILiveDeferredGpuAudit
{
    const string Root="E:/ZZZ/local-only/RemielleRenderingReview/20260905/";
    const string WriteRoot = "E:/ZZZ/ZCode/90_Builds/RenderingReview/20260905/"; // D1-c 写根（读根保留 A 类）
    const string Acq="E:/ZZZ/local-only/RemielleDataAcquisition/20260904/";
    const string Out = "E:/ZZZ/ZCode/90_Builds/RenderingReview/20260905/ui-live-binding/live-deferred-gpu/";
    static string Sha(string p){using var f=File.OpenRead(p);using var h=SHA256.Create();return BitConverter.ToString(h.ComputeHash(f)).Replace("-","").ToLowerInvariant();}
    static JObject Ref(string p)=>new(){["path"]=Path.GetFullPath(p),["sha256"]=Sha(p)};
    static string Quote(string p)=>"\""+p.Replace('\\','/')+"\"";
    static byte[] Bytes<T>(T[] a)where T:struct{var b=new byte[a.Length*Marshal.SizeOf<T>()];var pin=GCHandle.Alloc(a,GCHandleType.Pinned);try{Marshal.Copy(pin.AddrOfPinnedObject(),b,0,b.Length);}finally{pin.Free();}return b;}
    static byte[] Read(Texture texture,int count)
    {
        var storage=new NativeArray<byte>(count,Allocator.Persistent,NativeArrayOptions.UninitializedMemory);
        try{var request=AsyncGPUReadback.RequestIntoNativeArray(ref storage,texture,0);request.WaitForCompletion();if(request.hasError)throw new Exception("Deferred input readback failed");return storage.ToArray();}finally{storage.Dispose();}
    }
    public static void Run()
    {
        Directory.CreateDirectory(Out+"inputs");Directory.CreateDirectory(Out+"unity");
        var source=JObject.Parse(File.ReadAllText(Root+"captured-deferred-character-ui.json"));
        var shaders=JObject.Parse(File.ReadAllText(Acq+"shader-evidence.json"));
        string vs=Acq+(string)shaders["c85e3fc3d2f75d34"]["artifacts"]["dxbc"],ps=Acq+(string)shaders["fbb07bad65276f2d"]["artifacts"]["dxbc"];
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        var root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/V3/Remielle_V3_Animated.prefab"));var driver=root.GetComponent<RemielleNativeAnimation>();
        var cameraObject=new GameObject("Native UI independent Deferred camera");var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;
        var cases=new JArray();var lines=new StringBuilder();
        try
        {
            foreach(string profileName in new[]{"display","store"})
            {
                var row=source["cases"].Single(c=>(string)c["key"]==profileName);
                var values=row["pixelConstants"].Select(v=>(float)v).ToArray();var constants=new Vector4[185];for(int i=0;i<185;i++)constants[i]=new Vector4(values[i*4],values[i*4+1],values[i*4+2],values[i*4+3]);
                var lutSource=row["inputs"].Single(i=>(int)i["slot"]==5);
                var lut=AssetDatabase.LoadAssetAtPath<Texture2D>(RemielleUILiveProfileBuild.Assets+"Textures/"+lutSource["sourceSha256"]+".asset");
                var profile=AssetDatabase.LoadAssetAtPath<RemielleNativeUIProfile>(RemielleUILiveProfileBuild.Assets+profileName+".asset");
                using var geometry=new RemielleNativeUIRenderer(profile,driver,camera);
                var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/CapturedDeferredCharacter523.shader");
                using var lighting=new RemielleNativeUILighting(shader,lut,constants);
                driver.ResetSourcePose();driver.ApplyPose();
                string[] samples={"__rest","Idle_Loop","Walk_Start"};
                for(int sample=0;sample<3;sample++)
                {
                    if(sample!=0){var state=driver.nativeAnimation[samples[sample]];if(state==null)throw new Exception("Required native action unavailable: "+samples[sample]);driver.Sample(state.name,state.length*.37f);}
                    int width=new[]{960,800,1280}[sample],height=new[]{540,600,720}[sample];float yaw=new[]{0f,65f,180f}[sample];
                    camera.aspect=(float)width/height;camera.fieldOfView=30+8*sample;camera.nearClipPlane=sample==1?.3f:.1f;camera.farClipPlane=50;
                    RemielleUILiveRasterAudit.FrameModel(geometry,camera,yaw);geometry.Prepare(width,height);geometry.Render();lighting.Render(geometry,camera);
                    string id=profileName+"-"+sample,stem=Out+"inputs/"+id;
                    var inputs=new JArray();var formats=new[]{2,41,10,29,2,10};
                    var rawDepth=Read(geometry.DepthRead,width*height*16);var depth=new byte[width*height*4];var mask=new byte[width*height];
                    for(int p=0;p<mask.Length;p++){Buffer.BlockCopy(rawDepth,p*16,depth,p*4,4);mask[p]=(byte)((uint)BitConverter.ToSingle(rawDepth,p*16+4)&128);}
                    if(!depth.SequenceEqual(Read(lighting.SampledDepth,width*height*4)))throw new Exception("Sampled depth copy is not bit-exact");
                    File.WriteAllBytes(stem+"-mask.r8",mask);File.WriteAllBytes(stem+"-cb0.buf",Bytes(lighting.Constants));
                    var payloads=new[]{Read(lighting.ExpandedNormal,width*height*16),depth,Read(geometry.Targets[0],width*height*8),Read(geometry.Targets[1],width*height*4),Read(lighting.ExpandedMotion,width*height*16),lut.GetRawTextureData<byte>().ToArray()};
                    lines.AppendLine(id+" "+width+" "+height+" 3 4 "+Quote(vs)+" "+Quote(ps)+" 2\n10 \"-\"\n24 \"-\"\n2 1 2 1 15\n2 1 2 1 15\n1\n1 0 "+Quote(stem+"-cb0.buf")+"\n6");
                    for(int slot=0;slot<6;slot++)
                    {
                        string path=stem+"-t"+slot+".raw";File.WriteAllBytes(path,payloads[slot]);int w=slot==5?lut.width:width,h=slot==5?lut.height:height;
                        lines.AppendLine("1 "+slot+" "+w+" "+h+" "+formats[slot]+" "+Quote(path));inputs.Add(new JObject{["slot"]=slot,["format"]=formats[slot],["width"]=w,["height"]=h,["file"]=Ref(path)});
                    }
                    lines.AppendLine("2\n1 0 21 3\n1 1 0 3");
                    string hdr=Out+"unity/"+id+"-o0.raw",aux=Out+"unity/"+id+"-o1.raw";File.WriteAllBytes(hdr,Read(lighting.Hdr,width*height*8));File.WriteAllBytes(aux,Read(lighting.Auxiliary,width*height*4));
                    cases.Add(new JObject{["id"]=id,["width"]=width,["height"]=height,["sample"]=samples[sample],["yaw"]=yaw,["inputs"]=inputs,["mask"]=Ref(stem+"-mask.r8"),["constants"]=Ref(stem+"-cb0.buf"),["hdr"]=Ref(hdr),["auxiliary"]=Ref(aux)});
                }
            }
            File.WriteAllText(Out+"inputs.txt",cases.Count+"\n"+lines,new UTF8Encoding(false));
            var files=new JArray();foreach(string path in new[]{"Assets/Editor/RemielleUILiveDeferredGpuAudit.cs","Assets/RenderingReview/Runtime/RemielleNativeUILighting.cs","Assets/RenderingReview/Runtime/RemielleNativeUIRenderer.cs","Assets/Shaders/CapturedDeferredCharacter523.shader",vs,ps,Root+"captured-deferred-character-ui.json"})files.Add(Ref(path));
            File.WriteAllText(Out+"unity.json",new JObject{["schema"]="remielle-ui-live-deferred-readback-v1",["cases"]=cases,["implementationFiles"]=files,["inputList"]=Ref(Out+"inputs.txt"),["boundary"]="Current native geometry and camera, varied resolution/near plane/action. Original DXBC comparison consumes the exported input buffers; game light producer behavior remains distinct."}.ToString());
            Debug.Log("REMIELLE_UI_LIVE_DEFERRED_EXPORTED "+cases.Count);
        }
        finally {Object.DestroyImmediate(cameraObject);Object.DestroyImmediate(root);}
    }
}
