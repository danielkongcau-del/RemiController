using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;
using nickmaltbie.OpenKCC.Character;
using nickmaltbie.OpenKCC.Utils.ColliderCast;

namespace Remielle.Controller.Editor
{
    public static class SourceControllerBuild
    {
        public const string Scene="Assets/ControllerIntegration/Scenes/Remielle_ControllerPreview.unity";
        public static void BuildPlayer()
        {
            Run();
            const string destination="E:/ZZZ/local-only/RemielleControllerDependencies/Player/RemielleControllerPreview.exe";
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{Scene},locationPathName=destination,target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development});
            File.WriteAllText(QualifiedIntegrationAudit.Output+"/controller-player-build.json",new Newtonsoft.Json.Linq.JObject{
                ["pass"]=report.summary.result==BuildResult.Succeeded,["result"]=report.summary.result.ToString(),
                ["output"]=destination,["bytes"]=report.summary.totalSize,["errors"]=report.summary.totalErrors,
                ["warnings"]=report.summary.totalWarnings}.ToString());
            if(report.summary.result!=BuildResult.Succeeded)throw new System.Exception("Controller preview Player build failed");
        }
        public static void Run()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var actor=new GameObject("RemielleController");
            var capsule=actor.AddComponent<CapsuleCollider>();capsule.center=new Vector3(0,.9f,0);capsule.height=1.8f;capsule.radius=.3f;
            actor.AddComponent<CapsuleColliderCast>();actor.AddComponent<KCCMovementEngine>();actor.GetComponent<Rigidbody>().isKinematic=true;
            var motor=actor.AddComponent<RemielleAuthoredMotor>();
            var visual=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/V3/Remielle_V3_Animated.prefab"));visual.transform.SetParent(actor.transform,false);
            var driver=visual.GetComponentInChildren<RemielleNativeAnimation>();driver.autoplay=false;driver.enabled=false;driver.nativeAnimation.enabled=false;
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="PreviewFloor";floor.transform.position=new Vector3(0,-.15f,0);floor.transform.localScale=new Vector3(100,.3f,100);
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.name="PreviewWall";wall.transform.position=new Vector3(0,1.5f,-5);wall.transform.localScale=new Vector3(6,3,.3f);
            var focus=new GameObject("CameraFocus").transform;focus.SetParent(actor.transform,false);focus.localPosition=new Vector3(0,1.1f,0);
            var light=new GameObject("PreviewLight").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1;light.transform.rotation=Quaternion.Euler(40,-30,0);
            RenderSettings.ambientLight=new Color(.4f,.4f,.4f);
            var camera=new GameObject("PreviewCamera").AddComponent<Camera>();camera.tag="MainCamera";camera.fieldOfView=40;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.07f,.08f,.10f);
            camera.transform.position=focus.position+new Vector3(0,1,6);camera.transform.LookAt(focus);
            var brain=camera.gameObject.AddComponent<CinemachineBrain>();brain.UpdateMethod=CinemachineBrain.UpdateMethods.ManualUpdate;
            var vcam=new GameObject("PreviewFollow").AddComponent<CinemachineCamera>();vcam.Follow=focus;vcam.LookAt=focus;
            var follow=vcam.gameObject.AddComponent<CinemachineFollow>();follow.FollowOffset=new Vector3(0,1,6);follow.TrackerSettings.PositionDamping=Vector3.zero;follow.TrackerSettings.BindingMode=BindingMode.WorldSpace;
            vcam.gameObject.AddComponent<CinemachineRotationComposer>().Damping=Vector2.zero;
            var controller=actor.AddComponent<RemielleSourceController>();controller.driver=driver;controller.visual=visual.transform;controller.motor=motor;controller.viewCamera=camera;controller.brain=brain;
            controller.layoutCamera=vcam;controller.actionCameraPack=SourceCameraBuild.Prepare();
            controller.actionShakePack=SourceCameraBuild.Prepare("Assets/ControllerIntegration/Data/source-action-shake.json");
            controller.specialResourcePack=SourceCameraBuild.Prepare("Assets/ControllerIntegration/Data/source-special-resource.json");
            controller.hitQueryPack=SourceCameraBuild.Prepare("Assets/ControllerIntegration/Data/source-hit-query.json");
            controller.hitFeedbackPack=SourceCameraBuild.Prepare("Assets/ControllerIntegration/Data/source-hit-feedback.json");
            controller.hitStopPack=SourceCameraBuild.Prepare("Assets/ControllerIntegration/Data/source-hitstop.json");
            controller.hitParticlePack=SourceCameraBuild.Prepare("Assets/ControllerIntegration/Data/source-hit-particles-runtime.json");
            controller.specialEffectEventPack=SourceCameraBuild.Prepare("Assets/ControllerIntegration/Data/source-special-effect-events.json");
            controller.specialParticlePack=SourceCameraBuild.Prepare("Assets/ControllerIntegration/Data/source-special-particles-runtime.json");
            controller.specialParticlePrefabs=new[]{
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ControllerIntegration/Effects/SpecialFlash/Eff_RemielleOrigin_Attack_Special_05_Flash.prefab"),
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ControllerIntegration/Effects/SpecialFlash/Eff_RemielleOrigin_Attack_Special_06_Flash.prefab"),
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ControllerIntegration/Effects/SpecialBurst/Eff_RemielleOrigin_Attack_Special_02_Burst.prefab"),
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ControllerIntegration/Effects/SpecialBurst/Eff_RemielleOrigin_Attack_Special_14_Burst.prefab"),
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ControllerIntegration/Effects/SpecialSmoke/Eff_RemielleOrigin_Attack_Special_15_Smoke.prefab"),
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ControllerIntegration/Effects/SpecialSmoke/Eff_RemielleOrigin_Attack_Special_16_Smoke.prefab"),
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ControllerIntegration/Effects/SpecialTrail/Eff_RemielleOrigin_Attack_Special_04_Trail.prefab"),
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ControllerIntegration/Effects/SpecialTrail/Eff_RemielleOrigin_Attack_Special_13_Trail.prefab")};
            if(System.Array.Exists(controller.specialParticlePrefabs,p=>!p))throw new System.Exception("Build special particle prefabs first");
            controller.hitParticlePrefabs=new[]{
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ControllerIntegration/Effects/HitParticles/Hit_Slash_Large_Remielle.prefab"),
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ControllerIntegration/Effects/HitParticles/Hit_Smash_Large_Remielle.prefab")};
            if(System.Array.Exists(controller.hitParticlePrefabs,p=>!p))throw new System.Exception("Build and verify hit particle prefabs first");
            SourceTrainingTarget.Create("TrainingTargetLeft",new Vector3(-2,1,3));
            SourceTrainingTarget.Create("TrainingTargetRight",new Vector3(2,1,3));
            var impulse=vcam.gameObject.AddComponent<CinemachineImpulseListener>();impulse.ChannelMask=SourceCameraShake.Channel;
            impulse.Gain=1;impulse.UseCameraSpace=true;impulse.ApplyAfter=CinemachineCore.Stage.Noise;
            impulse.ReactionSettings=new CinemachineImpulseListener.ImpulseReaction();
            controller.sourcePack=AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/ControllerRuntime/Data/source-controller-pack.json");
            controller.timeSettings=AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/ControllerRuntime/Data/source-time-settings.json");
            controller.actionEventPack=AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/ControllerIntegration/Data/source-action-events.json");
            if(!controller.actionEventPack)throw new System.InvalidOperationException("Generate the source action event pack before building");
            controller.visibilityBindings=SourceVisibilityBuild.Prepare(visual);
            controller.visibilityEventPack=AssetDatabase.LoadAssetAtPath<TextAsset>(SourceVisibilityBuild.AssetPath);
            controller.weaponTrailMaterial=SourceTrailBuild.Prepare();
            controller.weaponTrailPack=AssetDatabase.LoadAssetAtPath<TextAsset>(SourceTrailBuild.Pack);
            controller.weaponEmissionTexture=SourceWeaponMaterialBuild.Prepare();
            controller.weaponMaterialPack=AssetDatabase.LoadAssetAtPath<TextAsset>(SourceWeaponMaterialBuild.Pack);
            controller.weaponOutlineTexture=SourceWeaponMaterialBuild.PrepareSurface();
            controller.weaponSurfaceShader=AssetDatabase.LoadAssetAtPath<Shader>(SourceWeaponMaterialBuild.SurfaceShader);
            SourceWeaponMaterialBuild.VerifyFamilies();
            controller.leftWeaponMaterialPack=AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/ControllerIntegration/Data/source-weapon-material-L.json");
            controller.leftWeaponTrailPack=AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/ControllerIntegration/Data/source-weapon-trail-L.json");
            controller.largeWeaponMaterialPack=AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/ControllerIntegration/Data/source-weapon-material-B.json");
            controller.largeWeaponTrailPack=AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/ControllerIntegration/Data/source-weapon-trail-B.json");
            Directory.CreateDirectory(Path.GetDirectoryName(Scene));EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(),Scene);
            Debug.Log("REMIELLE_CONTROLLER_PREVIEW_SCENE_SAVED");
        }
    }
}
