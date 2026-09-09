using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

public static class RemielleNativeUIPresentationBuild
{
    public const string Root="E:/ZZZ/local-only/RemielleRenderingReview/20260905/";
    const string WriteRoot = "E:/ZZZ/ZCode/90_Builds/RenderingReview/20260905/"; // D1-c 写根（读根保留 A 类）
    public const string Out = "E:/ZZZ/ZCode/90_Builds/RenderingReview/20260905/ui-live-binding/native-player/";
    public const string Assets="Assets/RenderingReview/NativeUILive/Presentation/";
    public const string ScenePath="Assets/RenderingReview/Remielle_NativeUIReview.unity";
    public const string PlayerPath=WriteRoot+"NativePlayer/RemielleNativeUI.exe";
    public static void BuildProfiles()
    {
        Directory.CreateDirectory(Out);Directory.CreateDirectory(Assets);AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var source=JObject.Parse(File.ReadAllText(Root+"captured-deferred-character-ui.json"));var rows=new JArray();
        foreach(string name in new[]{"display","store"})
        {
            var row=source["cases"].Single(c=>(string)c["key"]==name);var lutSource=row["inputs"].Single(i=>(int)i["slot"]==5);
            var p=ScriptableObject.CreateInstance<RemielleNativeUIPresentationProfile>();p.name=name;p.profileName=name;
            p.deferredSourceSha256=RemielleUINativePostBuild.Sha(Root+"captured-deferred-character-ui.json");
            p.geometry=AssetDatabase.LoadAssetAtPath<RemielleNativeUIProfile>(RemielleUILiveProfileBuild.Assets+name+".asset");
            p.lateBody=AssetDatabase.LoadAssetAtPath<RemielleNativeUILateBodyProfile>(RemielleUILateBodyBuild.Assets+name+".asset");
            p.temporal=AssetDatabase.LoadAssetAtPath<RemielleNativeUITemporalProfile>(RemielleUITemporalCaptureAudit.Assets+name+".asset");
            p.post=AssetDatabase.LoadAssetAtPath<RemielleNativeUIPostProfile>(RemielleUINativePostBuild.Assets+name+".asset");
            p.deferredShader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/CapturedDeferredCharacter523.shader");
            p.depthHierarchyShader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/RenderingReview/Shader/GeneratedNativeUIReplay/NativeUIDepthHierarchy.shader");
            p.characterLut=AssetDatabase.LoadAssetAtPath<Texture>(RemielleUILiveProfileBuild.Assets+"Textures/"+lutSource["sourceSha256"]+".asset");
            p.deferredConstants=RemielleReviewBloom.Vectors(row["pixelConstants"].Select(v=>(float)v).ToArray(),185);p.Validate();
            string path=Assets+name+".asset";var existing=AssetDatabase.LoadAssetAtPath<RemielleNativeUIPresentationProfile>(path);
            if(existing){EditorUtility.CopySerialized(p,existing);EditorUtility.SetDirty(existing);Object.DestroyImmediate(p);}else AssetDatabase.CreateAsset(p,path);
            rows.Add(new JObject{["profile"]=name,["asset"]=path,["sourceSha256"]=p?p.deferredSourceSha256:existing.deferredSourceSha256});
        }
        AssetDatabase.SaveAssets();File.WriteAllText(Out+"profiles.json",new JObject{["profiles"]=rows,["runtimeCaptureFileAccess"]=false}.ToString());
    }
    public static void BuildScene()
    {
        BuildProfiles();
        if(GraphicsSettings.defaultRenderPipeline||QualitySettings.renderPipeline||PlayerSettings.colorSpace!=ColorSpace.Linear)
            throw new Exception("Native presentation requires the existing Built-in Linear project");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var root=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/V3/Remielle_V3_Animated.prefab"));
        var driver=root.GetComponent<RemielleNativeAnimation>();driver.autoplay=true;driver.initialClip="Idle_Loop";
        driver.nativeAnimation.cullingType=AnimationCullingType.AlwaysAnimate;driver.ResetSourcePose();driver.ApplyPose();
        // The source model stays intact. Only this camera's native draws form
        // its visible image; standard Scene editing can still inspect the SMRs.
        var camera=new GameObject("RemielleNativeCamera").AddComponent<Camera>();camera.tag="MainCamera";camera.enabled=false;
        camera.cullingMask=0;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
        camera.renderingPath=RenderingPath.Forward;camera.allowHDR=true;camera.allowMSAA=false;camera.allowDynamicResolution=false;
        camera.fieldOfView=38;camera.aspect=16f/9;camera.nearClipPlane=.1f;camera.farClipPlane=100;
        var view=camera.gameObject.AddComponent<RemielleNativeUIPresentation>();view.model=driver;
        view.profiles=new[]{AssetDatabase.LoadAssetAtPath<RemielleNativeUIPresentationProfile>(Assets+"display.asset"),AssetDatabase.LoadAssetAtPath<RemielleNativeUIPresentationProfile>(Assets+"store.asset")};view.profileIndex=1;
        using(var graph=new RemielleNativeUIFrameGraph(view.profiles[1],driver,camera))graph.FrameCamera(0,-2.68f);
        camera.gameObject.AddComponent<RemielleNativeUIReviewControls>().presentation=view;
        RenderSettings.skybox=null;RenderSettings.fog=false;camera.enabled=true;
        PrefabUtility.RecordPrefabInstancePropertyModifications(driver);PrefabUtility.RecordPrefabInstancePropertyModifications(driver.nativeAnimation);
        AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(SceneManager.GetActiveScene(),ScenePath);
        File.WriteAllText(Out+"scene.json",new JObject{["scene"]=ScenePath,["sourcePrefab"]="Assets/V3/Remielle_V3_Animated.prefab",["nativeVisiblePath"]=true,["profiles"]=2,["controllerImplemented"]=false,["sourceModelRegenerated"]=false}.ToString());
    }
    public static void Run()
    {
        BuildScene();RemielleNativeUIPresentationAudit.Run();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorUtility.UnloadUnusedAssetsImmediate();GC.Collect();
        int oldWidth=PlayerSettings.defaultScreenWidth,oldHeight=PlayerSettings.defaultScreenHeight;
        bool oldResizable=PlayerSettings.resizableWindow,oldBackground=PlayerSettings.runInBackground;
        var oldMode=PlayerSettings.fullScreenMode;
        try
        {
            PlayerSettings.defaultScreenWidth=1920;PlayerSettings.defaultScreenHeight=1080;PlayerSettings.resizableWindow=true;PlayerSettings.runInBackground=true;PlayerSettings.fullScreenMode=FullScreenMode.Windowed;
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{ScenePath},locationPathName=PlayerPath,target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development});
            var summary=report.summary;
            File.WriteAllText(Out+"build.json",new JObject{["pass"]=summary.result==BuildResult.Succeeded,["errors"]=summary.totalErrors,["warnings"]=summary.totalWarnings,["bytes"]=summary.totalSize,["seconds"]=summary.totalTime.TotalSeconds,["player"]=PlayerPath,["defaultWidth"]=1920,["defaultHeight"]=1080,["resizable"]=true,["scene"]=ScenePath}.ToString());
            if(summary.result!=BuildResult.Succeeded)throw new Exception("Native presentation Player build failed");
        }
        finally{PlayerSettings.defaultScreenWidth=oldWidth;PlayerSettings.defaultScreenHeight=oldHeight;PlayerSettings.resizableWindow=oldResizable;PlayerSettings.runInBackground=oldBackground;PlayerSettings.fullScreenMode=oldMode;AssetDatabase.SaveAssets();}
        Debug.Log("REMIELLE_NATIVE_PRESENTATION_BUILD_OK");
    }
}
