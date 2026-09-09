using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using UnityEngine.SceneManagement;

public static class RemielleRenderReviewBuild
{
    public const string Out="E:/ZZZ/local-only/RemielleRenderingReview/20260905";
    public const string ScenePath="Assets/RenderingReview/Remielle_LightingReview.unity";
    public static void Run()
    {
        RemielleBloomAudit.Run();RemielleCombatPostAudit.Run();RemielleHighQualityBloomAudit.Run();
        RemielleDeferredCharacterAudit.Run();RemielleDeferredShadingAudit.Run();RemielleNativeMrtCapabilityAudit.Run();
        RemielleNativeMaterialCompileAudit.Run();RemielleNativeTextureImportAudit.Run();
        BuildScene();RemielleRuntimeMaterialAudit.Run();RemielleNoseLineAudit.Run();
        // Force sequential clip reads before the build collector walks them.
        // This does not reserialize, resample or change any animation asset.
        var driver=UnityEngine.Object.FindFirstObjectByType<RemielleNativeAnimation>();
        foreach(AnimationState state in driver.nativeAnimation){Debug.Log("Preload native clip: "+state.name+" / "+state.length);}
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        EditorUtility.UnloadUnusedAssetsImmediate();GC.Collect();
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {scenes=new[]{ScenePath},locationPathName=Out+"/Player/RemielleLighting.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development});
        var summary=report.summary;
        if(ShaderUtil.ShaderHasError(AssetDatabase.LoadAssetAtPath<Shader>("Assets/RenderingReview/Shader/HoyoToonZenlessZoneZero.shader")))throw new Exception("Review character shader has compilation errors");
        File.WriteAllText(Out+"/build-result.json",new JObject{["pass"]=summary.result==BuildResult.Succeeded,["errors"]=summary.totalErrors,["warnings"]=summary.totalWarnings,["bytes"]=summary.totalSize,["seconds"]=summary.totalTime.TotalSeconds}.ToString());
        if(summary.result!=BuildResult.Succeeded)throw new Exception("Lighting player build failed");
        Debug.Log("REMIELLE_RENDER_REVIEW_BUILD_VERIFIED");
    }
    public static void BuildScene()
    {
        Directory.CreateDirectory(Out);Directory.CreateDirectory("Assets/RenderingReview/Materials");AssetDatabase.Refresh();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var root=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/V3/Remielle_V3_Animated.prefab"));
        const string runtimeTextureRoot="Assets/RenderingReview/CapturedRuntimeTextures/";
        var runtimeResources=root.AddComponent<RemielleCapturedBattleResources>();runtimeResources.profile=root.GetComponent<RemielleRuntimeProfile>();
        runtimeResources.capturedWhite=AssetDatabase.LoadAssetAtPath<Texture2D>(runtimeTextureRoot+"CapturedWhite_b7ff7a6e.dds");
        runtimeResources.secondaryWings=AssetDatabase.LoadAssetAtPath<Texture2D>(runtimeTextureRoot+"Secondary_Wings_fe0b2cda.dds");
        runtimeResources.secondaryWeapon01=AssetDatabase.LoadAssetAtPath<Texture2D>(runtimeTextureRoot+"Secondary_Weapon01_98ed549a.dds");
        runtimeResources.secondaryMaskWeapon01=AssetDatabase.LoadAssetAtPath<Texture2D>(runtimeTextureRoot+"SecondaryMask_Weapon01_58f4101b.dds");
        runtimeResources.secondaryMaskBody2=AssetDatabase.LoadAssetAtPath<Texture2D>(runtimeTextureRoot+"SecondaryMask_Body2_1c5eb158.dds");
        runtimeResources.specialWeapon01=AssetDatabase.LoadAssetAtPath<Texture2D>(runtimeTextureRoot+"SpecialWeapon_Weapon01_40e51db9.dds");
        runtimeResources.Apply();
        var source=AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/RenderingReview/captured-lighting.json");
        var data=JsonUtility.FromJson<RemielleCapturedLights>(source.text);var p=data.profiles[0];
        var mapped=new Dictionary<Material,Material>();
        foreach(var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var materials=renderer.sharedMaterials;
            for(int i=0;i<materials.Length;i++)
            {
                var original=materials[i];
                if(original==null){if(renderer.enabled)throw new Exception("Visible renderer has an empty material slot: "+renderer.name);continue;}
                if(!mapped.TryGetValue(original,out var copy))
                {
                    string path="Assets/RenderingReview/Materials/"+original.name+".mat";
                    copy=AssetDatabase.LoadAssetAtPath<Material>(path);
                    if(copy==null){copy=new Material(original);AssetDatabase.CreateAsset(copy,path);}else EditorUtility.CopySerialized(original,copy);
                    copy.shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/RenderingReview/Shader/HoyoToonZenlessZoneZero.shader");
                    if(copy.shader==null)throw new Exception("Review character shader missing");
                    RemielleLightingReview.SetMaterialEnvironment(copy,data,0,true,false);EditorUtility.SetDirty(copy);mapped.Add(original,copy);
                }
                materials[i]=copy;
            }
            renderer.sharedMaterials=materials;
        }
        var light=new GameObject("CapturedKeyLight").AddComponent<Light>();light.type=LightType.Directional;
        light.color=RemielleLightingReview.GammaColor(p.lightColor);light.intensity=1;light.shadows=LightShadows.None;light.renderMode=LightRenderMode.ForcePixel;
        light.transform.rotation=Quaternion.LookRotation(-RemielleLightingReview.V(p.overrideLight?p.bodyLightDirection:p.lightDirection),Vector3.up);
        RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=RemielleLightingReview.GammaColor(p.ambient);
        RenderSettings.fog=false;RenderSettings.skybox=null;RenderSettings.sun=light;
        var camera=new GameObject("LightingReviewCamera").AddComponent<Camera>();camera.tag="MainCamera";camera.renderingPath=RenderingPath.Forward;
        camera.allowHDR=true;camera.allowMSAA=true;camera.allowDynamicResolution=false;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.nearClipPlane=.03f;camera.farClipPlane=30;
        camera.fieldOfView=24;camera.transform.position=new Vector3(0,1.19f,2.7f);camera.transform.rotation=Quaternion.Euler(0,180,0);
        camera.depthTextureMode=DepthTextureMode.Depth;
        // Image effects execute in component order. Menu Bloom and battle UberPost
        // are exclusive profiles; the latter already contains the final post LUT.
        var bloom=camera.gameObject.AddComponent<RemielleReviewBloom>();bloom.shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/CapturedMenuBloom.shader");bloom.capturedData=AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/RenderingReview/captured-bloom.json");bloom.amount=1;
        var hq=camera.gameObject.AddComponent<RemielleHighQualityBloom>();hq.shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/CapturedHighQualityBloom.shader");
        hq.maskShader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/RenderingReview/Shader/ReviewBloomMask.shader");
        hq.capturedData=AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/RenderingReview/captured-hq-bloom.json");hq.model=root.transform;
        var nativeShadow=camera.gameObject.AddComponent<RemielleNativeShadowProbe>();nativeShadow.shadowShader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/RenderingReview/Shader/NativeShadowDepth.shader");nativeShadow.model=root.transform;nativeShadow.keyLight=light;nativeShadow.reviewCamera=camera;
        var nativeGBuffer=camera.gameObject.AddComponent<RemielleNativeGBufferProbe>();nativeGBuffer.shadowProbe=nativeShadow;
        nativeGBuffer.adapterShader=hq.maskShader;nativeGBuffer.model=root.transform;nativeGBuffer.keyLight=light;
        var capturedDeferred=AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/RenderingReview/captured-deferred-character.json");
        const string deferredLutPath="Assets/RenderingReview/RuntimeDeferredCharacterLut.asset";
        var deferredLut=AssetDatabase.LoadAssetAtPath<Texture2D>(deferredLutPath);
        if(!deferredLut){
            var captured=JObject.Parse(capturedDeferred.text);var item=((JArray)captured["inputs"]).Cast<JObject>().First(x=>(int)x["slot"]==5);
            deferredLut=new Texture2D((int)item["width"],(int)item["height"],TextureFormat.RGBAHalf,false,true){name="Captured draw 523 scene LUT",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
            deferredLut.LoadRawTextureData(File.ReadAllBytes((string)item["path"]));deferredLut.Apply(false,false);AssetDatabase.CreateAsset(deferredLut,deferredLutPath);
        }
        var nativeDeferred=camera.gameObject.AddComponent<RemielleNativeDeferredProbe>();nativeDeferred.gBuffer=nativeGBuffer;nativeDeferred.deferredShader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/CapturedDeferredCharacter523.shader");nativeDeferred.capturedData=capturedDeferred;nativeDeferred.sceneLut=deferredLut;nativeDeferred.keyLight=light;
        var nativeMaterialBindings=camera.gameObject.AddComponent<RemielleNativeMaterialConstantBufferProbe>();nativeMaterialBindings.shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/RenderingReview/Shader/GeneratedNative/CapturedNativeMaterialBodies.shader");nativeMaterialBindings.readbackShader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/RenderingReview/Shader/CapturedNativeConstantBufferProbe.shader");nativeMaterialBindings.capturedBuffers=Enumerable.Range(0,5).Select(i=>AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/RenderingReview/CapturedNativeMaterial/Draw374Cb"+i+".bytes")).ToArray();
        const string sceneLutPath="Assets/RenderingReview/RuntimeSceneLut.asset";
        hq.sceneLut=AssetDatabase.LoadAssetAtPath<Texture2D>(sceneLutPath);
        if(!hq.sceneLut){var fixture=JObject.Parse(File.ReadAllText(Out+"/hq-bloom-fixtures.json"))["fixtures"][20]["inputs"][0];hq.sceneLut=RemielleCombatPostAudit.Load(fixture);hq.sceneLut.name="Captured combat scene LUT";AssetDatabase.CreateAsset(hq.sceneLut,sceneLutPath);}
        var post=camera.gameObject.AddComponent<RemielleCapturedCombatPost>();post.shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/CapturedCombatPost.shader");
        post.capturedData=AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/RenderingReview/captured-combat-post.json");post.lut=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/SourceAssets/Runtime/RuntimePostBattle.asset");
        var floor=GameObject.CreatePrimitive(PrimitiveType.Plane);floor.name="OptionalShadowGround";floor.transform.localScale=Vector3.one;floor.transform.position=new Vector3(0,-.02f,0);
        string floorPath="Assets/RenderingReview/Materials/ReviewGround.mat";var ground=AssetDatabase.LoadAssetAtPath<Material>(floorPath);
        if(ground==null){ground=new Material(Shader.Find("Standard"));AssetDatabase.CreateAsset(ground,floorPath);}
        ground.color=new Color(.25f,.25f,.27f);ground.SetFloat("_Glossiness",0);floor.GetComponent<Renderer>().sharedMaterial=ground;floor.SetActive(false);
        hq.ground=floor.GetComponent<Renderer>();
        var controls=camera.gameObject.AddComponent<RemielleLightingReview>();controls.capturedLighting=source;controls.model=root.GetComponent<RemielleNativeAnimation>();controls.runtimeProfile=root.GetComponent<RemielleRuntimeProfile>();controls.combatPostProcess=post;
        controls.bloom=bloom;controls.reviewCamera=camera;controls.keyLight=light;controls.floor=floor;
        controls.highQualityBloom=hq;
        controls.nativeGBufferProbe=nativeGBuffer;
        controls.nativeDeferredProbe=nativeDeferred;
        controls.diagnosticShader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/RemielleLightUniformProbe.shader");
        if(!bloom.shader||!post.shader||!post.lut||!post.capturedData||!controls.diagnosticShader||!nativeShadow.shadowShader||!nativeGBuffer.adapterShader||!nativeDeferred.deferredShader||!nativeDeferred.sceneLut||!nativeMaterialBindings.shader||!nativeMaterialBindings.readbackShader||nativeMaterialBindings.capturedBuffers.Any(x=>!x))throw new Exception("Missing serialized shader or post source");
        AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(SceneManager.GetActiveScene(),ScenePath);
        File.WriteAllText(Out+"/scene-build.json",new JObject{["pass"]=true,["scene"]=ScenePath,["sourcePrefab"]="Assets/V3/Remielle_V3_Animated.prefab",["derivedMaterials"]=mapped.Count,["profiles"]=data.profiles.Length,["pipeline"]="Built-in Forward",["nativeShadowProbe"]="Off-screen dynamic 4x2048 cascade plus 1x2048 per-object R16 producer",["nativeGBufferProbe"]="Off-screen live MRT field producer",["nativeDeferredProbe"]="Off-screen exact draw 523 consumer with live camera constants",["nativeMaterialBindings"]="Compile and cb0-cb4 binding smoke test for six captured PS bodies",["nativeGBufferVisiblePath"]="disabled",["bloomDefault"]=true,["geometryShadowDefault"]=false,["sourceAssetsModified"]=false}.ToString());
    }
}
