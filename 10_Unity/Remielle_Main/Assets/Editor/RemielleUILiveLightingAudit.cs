using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

public static class RemielleUILiveLightingAudit
{
    const string Root="E:/ZZZ/local-only/RemielleRenderingReview/20260905/";
    const string WriteRoot = "E:/ZZZ/ZCode/90_Builds/RenderingReview/20260905/"; // D1-c 写根（读根保留 A 类）
    const string Out = "E:/ZZZ/ZCode/90_Builds/RenderingReview/20260905/ui-live-binding/live-lighting/";
    static string Sha(string p){using var f=File.OpenRead(p);using var h=SHA256.Create();return BitConverter.ToString(h.ComputeHash(f)).Replace("-","").ToLowerInvariant();}
    static JObject Ref(string p)=>new(){["path"]=Path.GetFullPath(p),["sha256"]=Sha(p)};
    public static void RunAll(){RemielleUILiveRasterAudit.Run();Run();}
    public static void Run()
    {
        Directory.CreateDirectory(Out);
        var data=JObject.Parse(File.ReadAllText(Root+"captured-deferred-character-ui.json"));
        var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/CapturedDeferredCharacter523.shader");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        var root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/V3/Remielle_V3_Animated.prefab"));var driver=root.GetComponent<RemielleNativeAnimation>();
        var cameraObject=new GameObject("Native UI HDR review");var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.aspect=16f/9;camera.fieldOfView=38;camera.nearClipPlane=.1f;camera.farClipPlane=50;
        var results=new JArray();
        try
        {
            foreach(string name in new[]{"display","store"})
            {
                var row=data["cases"].Single(c=>(string)c["key"]==name);
                var source=row["pixelConstantSource"];if(Sha((string)source["path"])!=(string)source["sha256"])throw new Exception("UI light constant source changed");
                var floats=row["pixelConstants"].Select(v=>(float)v).ToArray();var constants=new Vector4[185];for(int i=0;i<185;i++)constants[i]=new Vector4(floats[i*4],floats[i*4+1],floats[i*4+2],floats[i*4+3]);
                var profile=AssetDatabase.LoadAssetAtPath<RemielleNativeUIProfile>(RemielleUILiveProfileBuild.Assets+name+".asset");
                var lutSource=row["inputs"].Single(i=>(int)i["slot"]==5);
                var lut=AssetDatabase.LoadAssetAtPath<Texture>(RemielleUILiveProfileBuild.Assets+"Textures/"+lutSource["sourceSha256"]+".asset");
                using var geometry=new RemielleNativeUIRenderer(profile,driver,camera);
                using var lighting=new RemielleNativeUILighting(shader,lut,constants);
                driver.ResetSourcePose();driver.ApplyPose();
                foreach(float yaw in new[]{0f,65f,180f})
                {
                    RemielleUILiveRasterAudit.FrameModel(geometry,camera,yaw);geometry.Prepare(1920,1080);geometry.Render();lighting.Render(geometry,camera);
                    var colors=new NativeArray<ushort>(1920*1080*4,Allocator.Persistent,NativeArrayOptions.UninitializedMemory);
                    try
                    {
                        var read=AsyncGPUReadback.RequestIntoNativeArray(ref colors,lighting.Hdr,0);read.WaitForCompletion();if(read.hasError)throw new Exception("Native UI HDR readback failed");
                        string stem=Out+name+"-"+(int)yaw;File.WriteAllBytes(stem+".rgba16f",colors.Reinterpret<byte>(2).ToArray());
                        var pixels=new Color32[1920*1080];int finite=0,visible=0;float maximum=0;
                        for(int y=0;y<1080;y++)for(int x=0;x<1920;x++)
                        {
                            int i=y*1920+x;var c=new Color(Mathf.HalfToFloat(colors[i*4]),Mathf.HalfToFloat(colors[i*4+1]),Mathf.HalfToFloat(colors[i*4+2]),Mathf.HalfToFloat(colors[i*4+3]));
                            if(float.IsFinite(c.r)&&float.IsFinite(c.g)&&float.IsFinite(c.b)&&float.IsFinite(c.a))finite++;else throw new Exception("Nonfinite native UI HDR color");
                            if(c.r!=0||c.g!=0||c.b!=0)visible++;maximum=Mathf.Max(maximum,c.r,c.g,c.b);
                            // SetPixels32 consumes rows bottom-to-top. Unity's
                            // PNG encoder applies the file orientation itself.
                            c.a=1;pixels[i]=c.gamma;
                        }
                        if(visible<10000)throw new Exception("Native UI HDR character coverage is unexpectedly small");
                        var png=new Texture2D(1920,1080,TextureFormat.RGBA32,false,false);try{png.SetPixels32(pixels);png.Apply(false);File.WriteAllBytes(stem+"-linear-to-srgb.png",png.EncodeToPNG());}finally{Object.DestroyImmediate(png);}
                        results.Add(new JObject{["profile"]=name,["yaw"]=yaw,["width"]=1920,["height"]=1080,["finitePixels"]=finite,["visiblePixels"]=visible,["maximumHdrChannel"]=maximum,["hdr"]=Ref(stem+".rgba16f"),["preview"]=Ref(stem+"-linear-to-srgb.png"),["constants"]=new JArray(lighting.Constants.SelectMany(v=>new[]{v.x,v.y,v.z,v.w}))});
                    }
                    finally {colors.Dispose();}
                }
            }
            foreach(var m in ShaderUtil.GetShaderMessages(shader))if(m.severity.ToString()=="Error")throw new Exception(m.message);
            var files=new JArray();foreach(string p in new[]{"Assets/Editor/RemielleUILiveLightingAudit.cs","Assets/Editor/RemielleUILiveRasterAudit.cs","Assets/RenderingReview/Runtime/RemielleNativeUILighting.cs","Assets/RenderingReview/Runtime/RemielleNativeUIRenderer.cs","Assets/Shaders/CapturedDeferredCharacter523.shader",Root+"captured-deferred-character-ui.json"})files.Add(Ref(p));
            File.WriteAllText(Out+"readback.json",new JObject{["schema"]="remielle-ui-live-lighting-readback-v1",["cases"]=results,["implementationFiles"]=files,["boundary"]="Actual 1920x1080 native UI geometry plus character Deferred and source character LUT. Preview is vertically oriented linear-to-sRGB only, with HDR values clipped for PNG. Bloom/final post, changing light producers and standalone Player are not certified by this readback."}.ToString());
            Debug.Log("REMIELLE_UI_LIVE_HDR_READBACK "+results.Count);
        }
        finally {Object.DestroyImmediate(cameraObject);Object.DestroyImmediate(root);}
    }
}
