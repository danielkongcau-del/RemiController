using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

public static class RemielleUITemporalPreviewBuild
{
    const string Root="E:/ZZZ/local-only/RemielleRenderingReview/20260905/";
    const string WriteRoot = "E:/ZZZ/ZCode/90_Builds/RenderingReview/20260905/"; // D1-c 写根（读根保留 A 类）
    const string Out = "E:/ZZZ/ZCode/90_Builds/RenderingReview/20260905/ui-live-binding/temporal-preview/";
    static JObject Ref(string p)=>RemielleUINativePostBuild.Ref(p);
    public static void RunAll(){RemielleUITemporalCaptureAudit.Run();RemielleUILiveTemporalAudit.Run();Run();}
    public static void Run()
    {
        Directory.CreateDirectory(Out);var data=JObject.Parse(File.ReadAllText(WriteRoot+"captured-deferred-character-ui.json"));var cases=new JArray();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);var root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/V3/Remielle_V3_Animated.prefab"));var driver=root.GetComponent<RemielleNativeAnimation>();
        var go=new GameObject("Native temporal HD review");var camera=go.AddComponent<Camera>();camera.enabled=false;camera.aspect=16f/9;camera.fieldOfView=38;camera.nearClipPlane=.1f;camera.farClipPlane=50;
        try
        {
            foreach(string name in new[]{"display","store"})
            {
                var row=data["cases"].Single(c=>(string)c["key"]==name);var values=row["pixelConstants"].Select(v=>(float)v).ToArray();var lutSource=row["inputs"].Single(i=>(int)i["slot"]==5);
                var lut=AssetDatabase.LoadAssetAtPath<Texture>(RemielleUILiveProfileBuild.Assets+"Textures/"+lutSource["sourceSha256"]+".asset");
                using var geometry=new RemielleNativeUIRenderer(AssetDatabase.LoadAssetAtPath<RemielleNativeUIProfile>(RemielleUILiveProfileBuild.Assets+name+".asset"),driver,camera);
                using var lighting=new RemielleNativeUILighting(AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/CapturedDeferredCharacter523.shader"),lut,RemielleReviewBloom.Vectors(values,185));
                using var temporal=new RemielleNativeUITemporal(AssetDatabase.LoadAssetAtPath<RemielleNativeUITemporalProfile>(RemielleUITemporalCaptureAudit.Assets+name+".asset"));
                using var post=new RemielleNativeUIPost(AssetDatabase.LoadAssetAtPath<RemielleNativeUIPostProfile>(RemielleUINativePostBuild.Assets+name+".asset"));
                using var late=new RemielleNativeUILateBody(AssetDatabase.LoadAssetAtPath<RemielleNativeUILateBodyProfile>(RemielleUILateBodyBuild.Assets+name+".asset"));
                for(int view=0;view<3;view++)
                {
                    string sample=new[]{"__rest","Idle_Loop","Walk_Start"}[view];if(view==0){driver.ResetSourcePose();driver.ApplyPose();}else{var state=driver.nativeAnimation[sample];driver.Sample(sample,state.length*.37f);}
                    float yaw=new[]{0f,65f,180f}[view];RemielleUILiveRasterAudit.FrameModel(geometry,camera,yaw);
                    for(int frame=0;frame<16;frame++){var jitter=temporal.BeginFrame(1920,1080,frame==0);geometry.Prepare(1920,1080,jitter);late.Prepare(geometry,driver);geometry.Render();lighting.Render(geometry,camera);late.Draw(lighting.Hdr,geometry.Depth);temporal.Render(lighting.SampledDepth,lighting.Auxiliary,lighting.Hdr);}
                    post.Render(lighting.Hdr,temporal.Color);string id=name+"-"+view;var bytes=RemielleUINativePostAudit.Read(post.FinalColor,1920*1080*4);
                    string png=Out+id+".png",raw=Out+id+".rgba8",hdr=Out+id+".rgba16f";File.WriteAllBytes(raw,bytes);File.WriteAllBytes(hdr,RemielleUINativePostAudit.Read(temporal.Color,1920*1080*8));
                    var texture=new Texture2D(1920,1080,TextureFormat.RGBA32,false,false);try{texture.LoadRawTextureData(bytes);texture.Apply(false);File.WriteAllBytes(png,texture.EncodeToPNG());}finally{Object.DestroyImmediate(texture);}
                    // Controlled comparison uses the same complete Body_1 chain
                    // and held pose, with an unjittered frame and TAA disabled.
                    geometry.Prepare(1920,1080);late.Prepare(geometry,driver);geometry.Render();lighting.Render(geometry,camera);late.Draw(lighting.Hdr,geometry.Depth);post.Render(lighting.Hdr);
                    string noTaa=Out+id+"-no-taa.png";var noTaaBytes=RemielleUINativePostAudit.Read(post.FinalColor,1920*1080*4);
                    var noTaaTexture=new Texture2D(1920,1080,TextureFormat.RGBA32,false,false);try{noTaaTexture.LoadRawTextureData(noTaaBytes);noTaaTexture.Apply(false);File.WriteAllBytes(noTaa,noTaaTexture.EncodeToPNG());}finally{Object.DestroyImmediate(noTaaTexture);}
                    cases.Add(new JObject{["id"]=id,["profile"]=name,["sample"]=sample,["yaw"]=yaw,["width"]=1920,["height"]=1080,["accumulatedFrames"]=16,["preview"]=Ref(png),["raw"]=Ref(raw),["temporalHdr"]=Ref(hdr),["noTaaPreview"]=Ref(noTaa),["lateBodyIntegrated"]=true});
                }
            }
            var files=new JArray();foreach(string p in new[]{"Assets/Editor/RemielleUITemporalPreviewBuild.cs","Assets/RenderingReview/Runtime/RemielleNativeUITemporal.cs","Assets/RenderingReview/Runtime/RemielleNativeUIPost.cs","Assets/RenderingReview/Runtime/RemielleNativeUIConstants.cs","Assets/RenderingReview/Runtime/RemielleNativeUIRenderer.cs","Assets/RenderingReview/Runtime/RemielleNativeUILighting.cs"})files.Add(Ref(p));
            foreach(string p in new[]{"Assets/RenderingReview/Runtime/RemielleNativeUILateBody.cs",RemielleUILateBodyBuild.Assets+"display.asset",RemielleUILateBodyBuild.Assets+"store.asset"})files.Add(Ref(p));
            File.WriteAllText(Out+"readback.json",new JObject{["schema"]="remielle-native-ui-temporal-preview-v1",["cases"]=cases,["implementationFiles"]=files,["lateBodyIntegrated"]=true,["boundary"]="Current native renderer, source Deferred/LUT, late Body_1, 16 temporal samples, pre-TAA Bloom and full final post. Both comparison sides include the late pass. These are integration review images, not final dynamic-light/auxiliary/Player acceptance."}.ToString());Debug.Log("REMIELLE_UI_TEMPORAL_HD_PREVIEW "+cases.Count);
        }
        finally{Object.DestroyImmediate(go);Object.DestroyImmediate(root);}
    }
}
