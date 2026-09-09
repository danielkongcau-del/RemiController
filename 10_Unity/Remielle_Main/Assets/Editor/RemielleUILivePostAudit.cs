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

public static class RemielleUILivePostAudit
{
    const string Root="E:/ZZZ/local-only/RemielleRenderingReview/20260905/";
    const string Out=Root+"ui-live-binding/live-post/";
    static JObject Ref(string path)=>RemielleUINativePostBuild.Ref(path);
    static string Quote(string p)=>"\""+p.Replace('\\','/')+"\"";
    static byte[] Bytes(Vector4[] a){var b=new byte[a.Length*16];var pin=GCHandle.Alloc(a,GCHandleType.Pinned);try{Marshal.Copy(pin.AddrOfPinnedObject(),b,0,b.Length);}finally{pin.Free();}return b;}
    public static void RunAll(){RemielleUILiveDeferredGpuAudit.Run();RemielleUINativePostAudit.Run();RemielleUILiveLightingAudit.Run();Run();}
    public static void Run()
    {
        Directory.CreateDirectory(Out+"inputs");Directory.CreateDirectory(Out+"unity");
        var deferred=JObject.Parse(File.ReadAllText(Root+"captured-deferred-character-ui.json"));var postManifest=JObject.Parse(File.ReadAllText(RemielleUINativePostBuild.Root+"manifest.json"));
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        var root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/V3/Remielle_V3_Animated.prefab"));var driver=root.GetComponent<RemielleNativeAnimation>();
        var cameraObject=new GameObject("Native complete UI color review");var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.aspect=16f/9;camera.fieldOfView=38;camera.nearClipPlane=.1f;camera.farClipPlane=50;
        const int w=1920,h=1080;var cases=new JArray();var lines=new StringBuilder("6\n");
        try
        {
            foreach(string name in new[]{"display","store"})
            {
                var row=deferred["cases"].Single(c=>(string)c["key"]==name);var captured=postManifest["cases"].Single(c=>(string)c["id"]==name);
                var floats=row["pixelConstants"].Select(v=>(float)v).ToArray();var constants=RemielleReviewBloom.Vectors(floats,185);
                var lutSource=row["inputs"].Single(i=>(int)i["slot"]==5);var lut=AssetDatabase.LoadAssetAtPath<Texture>(RemielleUILiveProfileBuild.Assets+"Textures/"+lutSource["sourceSha256"]+".asset");
                var profile=AssetDatabase.LoadAssetAtPath<RemielleNativeUIProfile>(RemielleUILiveProfileBuild.Assets+name+".asset");
                var postProfile=AssetDatabase.LoadAssetAtPath<RemielleNativeUIPostProfile>(RemielleUINativePostBuild.Assets+name+".asset");
                using var geometry=new RemielleNativeUIRenderer(profile,driver,camera);
                using var lighting=new RemielleNativeUILighting(AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/CapturedDeferredCharacter523.shader"),lut,constants);
                using var post=new RemielleNativeUIPost(postProfile);
                driver.ResetSourcePose();driver.ApplyPose();string[] samples={"__rest","Idle_Loop","Walk_Start"};
                for(int sample=0;sample<3;sample++)
                {
                    if(sample!=0){var state=driver.nativeAnimation[samples[sample]];if(state==null)throw new Exception("Required native action missing");driver.Sample(state.name,state.length*.37f);}
                    float yaw=new[]{0f,65f,180f}[sample];RemielleUILiveRasterAudit.FrameModel(geometry,camera,yaw);geometry.Prepare(w,h);geometry.Render();lighting.Render(geometry,camera);post.Render(lighting.Hdr);
                    string id=name+"-"+sample,stem=Out+"inputs/"+id;var inputs=new JArray();
                    string hdr=stem+"-hdr.raw",bloom=stem+"-bloom.raw",p0=stem+"-p0.buf",p1=stem+"-p1.buf";
                    File.WriteAllBytes(hdr,RemielleUINativePostAudit.Read(lighting.Hdr,w*h*8));File.WriteAllBytes(bloom,RemielleUINativePostAudit.Read(post.Bloom,post.Bloom.width*post.Bloom.height*4));File.WriteAllBytes(p0,Bytes(post.Pixel0));File.WriteAllBytes(p1,Bytes(post.Pixel1));
                    var sourceVs=captured["constants"].Where(c=>(string)c["stage"]=="vs").OrderBy(c=>(int)c["slot"]).ToArray();
                    lines.AppendLine(id+" "+w+" "+h+" 0 4 "+Quote((string)postManifest["referenceVS"]["path"])+" "+Quote((string)postManifest["originalPS"]["path"])+" 1\n29 \"-\"\n2 1 2 1 15\n4");
                    foreach(var v in sourceVs)lines.AppendLine("0 "+v["slot"]+" "+Quote((string)v["file"]["path"]));
                    lines.AppendLine("1 0 "+Quote(p0)+"\n1 1 "+Quote(p1)+"\n5");
                    for(int slot=0;slot<5;slot++)
                    {
                        string file;int tw,th,fmt;
                        if(slot==0){file=hdr;tw=w;th=h;fmt=10;}
                        else if(slot==3){file=bloom;tw=post.Bloom.width;th=post.Bloom.height;fmt=26;}
                        else{var i=captured["inputs"].Single(i=>(int)i["slot"]==slot);file=(string)i["raw"]["path"];tw=(int)i["width"];th=(int)i["height"];fmt=(int)i["format"];RemielleUINativePostBuild.ReadRef(i["raw"]);}
                        lines.AppendLine("1 "+slot+" "+tw+" "+th+" "+fmt+" "+Quote(file));inputs.Add(new JObject{["slot"]=slot,["width"]=tw,["height"]=th,["format"]=fmt,["file"]=Ref(file)});
                    }
                    lines.AppendLine("4\n1 0 21 3\n1 1 21 1\n1 2 21 3\n1 3 21 3\n"+Quote((string)captured["vertexBuffer"]["path"])+" "+Quote((string)captured["indexBuffer"]["path"])+" 6");
                    byte[] output=RemielleUINativePostAudit.Read(post.FinalColor,w*h*4);string raw=Out+"unity/"+id+"-o0.raw",png=Out+id+".png";File.WriteAllBytes(raw,output);
                    var texture=new Texture2D(w,h,TextureFormat.RGBA32,false,false);try{texture.LoadRawTextureData(output);texture.Apply(false);File.WriteAllBytes(png,texture.EncodeToPNG());}finally{Object.DestroyImmediate(texture);}
                    cases.Add(new JObject{["id"]=id,["profile"]=name,["sample"]=samples[sample],["yaw"]=yaw,["width"]=w,["height"]=h,["inputs"]=inputs,["constants"]=new JArray(Ref(p0),Ref(p1)),["output"]=Ref(raw),["preview"]=Ref(png),["pipeline"]="current native geometry -> character Deferred/LUT -> 13 native menu Bloom draws -> complete original UI UberPost -> sRGB"});
                }
            }
            File.WriteAllText(Out+"inputs.txt",lines.ToString(),new UTF8Encoding(false));
            var files=new JArray();foreach(string path in new[]{"Assets/Editor/RemielleUILivePostAudit.cs","Assets/RenderingReview/Runtime/RemielleNativeUIPost.cs","Assets/RenderingReview/Runtime/RemielleNativeUILighting.cs","Assets/RenderingReview/Runtime/RemielleNativeUIRenderer.cs","Assets/RenderingReview/Shader/GeneratedNativeUIReplay/NativeUIFinalPost.shader","Assets/Shaders/CapturedMenuBloom.shader",RemielleUINativePostBuild.Assets+"display.asset",RemielleUINativePostBuild.Assets+"store.asset",Root+"captured-deferred-character-ui.json"})files.Add(Ref(path));
            File.WriteAllText(Out+"unity.json",new JObject{["schema"]="remielle-native-ui-live-post-readback-v1",["cases"]=cases,["implementationFiles"]=files,["manifest"]=Ref(RemielleUINativePostBuild.Root+"manifest.json"),["inputList"]=Ref(Out+"inputs.txt"),["boundary"]="Live geometry, character Deferred/LUT, native packed Bloom and complete UberPost. Temporal antialiasing, dynamic scene light/auxiliary producers and final Player remain separate work."}.ToString());
            Debug.Log("REMIELLE_NATIVE_UI_LIVE_POST_READBACK "+cases.Count);
        }
        finally{Object.DestroyImmediate(cameraObject);Object.DestroyImmediate(root);}
    }
}
