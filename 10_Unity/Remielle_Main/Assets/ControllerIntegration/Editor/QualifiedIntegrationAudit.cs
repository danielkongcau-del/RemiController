using System;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.Cinemachine;
using Remielle.ControllerRuntime;
using nickmaltbie.OpenKCC.Character;
using nickmaltbie.OpenKCC.Utils;
using nickmaltbie.OpenKCC.Utils.ColliderCast;
using Object = UnityEngine.Object;

namespace Remielle.Controller.Editor
{
    public static class QualifiedIntegrationAudit
    {
        public const string Output = "E:/ZZZ/ZCode/90_Builds/ControllerDependencies/implementation";
        const string Main = "Avatar_Female_Size02_RemielleOrigin_Controller";
        const string Air = "Avatar_Female_Size02_RemielleOrigin_AirCombat_Controller";
        static readonly NativeTransitionTimingPolicy Timing = new NativeTransitionTimingPolicy(true, false);
        static readonly List<string> passed = new List<string>();
        const string FrameAuditKey = "RemielleQualifiedFrameAudit";
        const string CharacterAuditKey = "RemielleCharacterFrameAudit";
        static double frameAuditStarted;
        [InitializeOnLoadMethod]
        static void ResumeFrameAudit()
        {
            if(!SessionState.GetBool(FrameAuditKey,false)&&!SessionState.GetBool(CharacterAuditKey,false))return;
            frameAuditStarted=EditorApplication.timeSinceStartup;
            EditorApplication.update-=WatchFrameAudit;
            EditorApplication.update+=WatchFrameAudit;
        }
        static void WatchFrameAudit()
        {
            if(EditorApplication.timeSinceStartup-frameAuditStarted>120)
            { SessionState.SetBool(FrameAuditKey,false);SessionState.SetBool(CharacterAuditKey,false);EditorApplication.Exit(2);return; }
            if(!EditorApplication.isPlaying)return;
            if(SessionState.GetBool(CharacterAuditKey,false))
            {
                var character=Object.FindFirstObjectByType<SourceMotionFrameProbe>();
                if(!character||!character.completed)return;
                SessionState.SetBool(CharacterAuditKey,false);
                Debug.Log("REMIELLE_CHARACTER_FRAME_"+(character.passed?"PASS":"FAIL"));
                EditorApplication.Exit(character.passed?0:1);return;
            }
            var probe=Object.FindFirstObjectByType<RemielleAdapterFrameProbe>();
            if(!probe||!probe.completed)return;
            SessionState.SetBool(FrameAuditKey,false);
            Debug.Log("REMIELLE_FRAME_ADAPTER_"+(probe.passed?"PASS":"FAIL"));
            EditorApplication.Exit(probe.passed?0:1);
        }
        public static void RunFrames()
        {
            Directory.CreateDirectory(Output);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var actor=GameObject.CreatePrimitive(PrimitiveType.Capsule);actor.name="SyntheticAuthoredDeltaProbe";
            actor.transform.position=new Vector3(0,2,0);
            actor.AddComponent<CapsuleColliderCast>();actor.AddComponent<KCCMovementEngine>();
            actor.GetComponent<Rigidbody>().isKinematic=true;
            var motor=actor.AddComponent<RemielleAuthoredMotor>();
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.name="MotorCollisionBoundary";
            wall.transform.position=new Vector3(1.5f,2,0);wall.transform.localScale=new Vector3(.2f,5,5);
            var light=new GameObject("ProbeLight").AddComponent<Light>();light.type=LightType.Directional;
            light.transform.rotation=Quaternion.Euler(40,-30,0);
            var cam=new GameObject("ProbeCamera").AddComponent<Camera>();
            cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.07f,.09f,.13f);
            var brain=cam.gameObject.AddComponent<CinemachineBrain>();brain.UpdateMethod=CinemachineBrain.UpdateMethods.ManualUpdate;
            var vcam=new GameObject("ProbeFollow").AddComponent<CinemachineCamera>();
            vcam.Follow=actor.transform;vcam.LookAt=actor.transform;
            vcam.gameObject.AddComponent<CinemachineFollow>().FollowOffset=new Vector3(0,2,-5);
            vcam.gameObject.AddComponent<CinemachineRotationComposer>();
            var probe=new GameObject("SourceAdapterFrameProbe").AddComponent<RemielleAdapterFrameProbe>();
            probe.sourcePack=AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/ControllerRuntime/Data/source-controller-pack.json");
            probe.motor=motor;probe.brain=brain;probe.reportDirectory=Output;
            Directory.CreateDirectory("Assets/ControllerIntegration/Scenes");
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(),"Assets/ControllerIntegration/Scenes/Remielle_SourceAdapterProbe.unity");
            SessionState.SetBool(FrameAuditKey,true);ResumeFrameAudit();EditorApplication.EnterPlaymode();
        }
        public static void RunCharacterFrames()
        {
            Directory.CreateDirectory(Output);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var actor=new GameObject("CharacterMotionMotor");
            var capsule=actor.AddComponent<CapsuleCollider>();capsule.center=new Vector3(0,.9f,0);capsule.height=1.8f;capsule.radius=.3f;
            actor.AddComponent<CapsuleColliderCast>();actor.AddComponent<KCCMovementEngine>();actor.GetComponent<Rigidbody>().isKinematic=true;
            var motor=actor.AddComponent<RemielleAuthoredMotor>();
            var visual=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/V3/Remielle_V3_Animated.prefab"));
            visual.transform.SetParent(actor.transform,false);
            var driver=visual.GetComponentInChildren<RemielleNativeAnimation>();
            driver.autoplay=false;driver.enabled=false;driver.nativeAnimation.enabled=false;
            var focus=new GameObject("CameraFocus").transform;focus.SetParent(actor.transform,false);focus.localPosition=new Vector3(0,1.1f,0);
            var light=new GameObject("MotionProbeLight").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1;
            light.transform.rotation=Quaternion.Euler(40,-30,0);RenderSettings.ambientLight=new Color(.4f,.4f,.4f);
            var camera=new GameObject("CharacterProbeCamera").AddComponent<Camera>();camera.fieldOfView=35;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.07f,.08f,.10f);
            var brain=camera.gameObject.AddComponent<CinemachineBrain>();brain.UpdateMethod=CinemachineBrain.UpdateMethods.ManualUpdate;
            var vcam=new GameObject("CharacterProbeFollow").AddComponent<CinemachineCamera>();vcam.Follow=focus;vcam.LookAt=focus;
            var follow=vcam.gameObject.AddComponent<CinemachineFollow>();follow.FollowOffset=new Vector3(0,.2f,6);
            follow.TrackerSettings.PositionDamping=Vector3.zero;
            vcam.gameObject.AddComponent<CinemachineRotationComposer>().Damping=Vector2.zero;
            var probe=new GameObject("SourceMotionFrameProbe").AddComponent<SourceMotionFrameProbe>();
            probe.driver=driver;probe.visual=visual.transform;probe.motor=motor;probe.brain=brain;probe.reportDirectory=Output;
            probe.sourcePack=AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/ControllerRuntime/Data/source-controller-pack.json");
            Directory.CreateDirectory("Assets/ControllerIntegration/Scenes");
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(),"Assets/ControllerIntegration/Scenes/Remielle_SourceMotionProbe.unity");
            SessionState.SetBool(CharacterAuditKey,true);ResumeFrameAudit();EditorApplication.EnterPlaymode();
        }
        static void Check(bool test, string name) { if (!test) throw new Exception(name); passed.Add(name); }
        static void Throws(Action action, string name)
        { bool caught = false; try { action(); } catch (InvalidOperationException) { caught = true; } Check(caught, name); }

        public static void Run()
        {
            Directory.CreateDirectory(Output);
            try
            {
                passed.Clear();
                var source = new NativeControllerSource(File.ReadAllText("Assets/ControllerRuntime/Data/source-controller-pack.json"));
                // Known source condition edge: ReleaseFloater FrameCount >= 90.
                foreach (int frame in new[] { 89, 90, 91 })
                {
                    var p = source.CreateParameters(Air); p.SetInt(p.Hash("FrameCount"), frame);
                    var router = new QualifiedStateRouter(source, Air, 0, 26);
                    var result = router.Preview(p, 0, 0, 2, 1, false, Timing, callbackContextQualified: true);
                    Check(result.Accepted == (frame >= 90), "source FrameCount boundary " + frame);
                    if (result.Accepted)
                    {
                        Check(result.Transition.NextState == 0 && result.Transition.TransitionDuration == .25f, "source release target and duration " + frame);
                        router.CommitLifecycle(result);
                        Check(router.CurrentState == 0 && router.EnterCount == 2, "HFSM explicit source commit " + frame);
                        Throws(() => router.CommitLifecycle(result), "no duplicate lifecycle commit " + frame);
                    }
                }
                {
                    var p = source.CreateParameters(Main); p.SetTrigger(p.Hash("Trigger_Hit")); p.SetTrigger(p.Hash("Trigger_PressEvade"));
                    var router = new QualifiedStateRouter(source, Main, 0, 32);
                    var result = router.Preview(p, 0, 0, 2, 1, true, Timing, callbackContextQualified: true);
                    Check(result.Accepted && result.Transition.TransitionIndex == 0, "Idle local Hit/Evade preserves source first candidate");
                    Check(p.GetTrigger(p.Hash("Trigger_Hit")) && p.GetTrigger(p.Hash("Trigger_PressEvade")), "preview does not consume input triggers");
                    p.SetTrigger(p.Hash("Trigger_Die"));
                    Throws(() => router.CommitLifecycle(result), "changed parameter snapshot rejected");
                    var conflict = router.Preview(p, 0, 0, 2, 1, true, Timing, callbackContextQualified: true);
                    Check(conflict.Accepted && conflict.SourceList=="Any" && conflict.Transition.NextState==1, "Original planner gives Any Die priority over Current Hit/Evade");
                    Check(router.CurrentState == 32 && router.EnterCount == 1, "preview does not change source state");
                }
                {
                    var p = source.CreateParameters(Main); p.SetTrigger(p.Hash("Trigger_PressAttackA"));
                    var router = new QualifiedStateRouter(source, Main, 0, 22);
                    var result = router.Preview(p, 0, 0, 2, 1, true, Timing, callbackContextQualified: true);
                    var c = source.GetController(Main);
                    Check(result.Accepted && result.Transition.TransitionIndex == 5 &&
                        (string)c["machines"][0]["states"][(int)result.Transition.NextState]["name"] == "Attack_Rush", "source RunLoop attack selects T5 not duplicate T6");
                    Check(!router.Preview(p,0,0,2,1,true,Timing,blending:true,callbackContextQualified:true).Accepted, "unqualified blend interruption rejected");
                    Check(!router.Preview(p,0,0,2,1,true,Timing).Accepted, "external callback requires explicit qualification");
                }
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var actor = new GameObject("AuthoredMotorProbe");
                actor.transform.position = new Vector3(0, 2, 0);
                actor.AddComponent<CapsuleCollider>();
                actor.AddComponent<CapsuleColliderCast>();
                var engine = actor.AddComponent<KCCMovementEngine>();
                actor.GetComponent<Rigidbody>().isKinematic = true;
                engine.Awake();
                var motor = actor.AddComponent<RemielleAuthoredMotor>();
                Physics.SyncTransforms();
                motor.ApplyAuthoredDelta(1, new Vector3(.25f,0,0));
                Check(Mathf.Abs(actor.transform.position.x-.25f)<.001f,"OpenKCC authored delta applied once");
                Throws(()=>motor.ApplyAuthoredDelta(1,new Vector3(.25f,0,0)),"duplicate root sample rejected");
                Check(Mathf.Abs(actor.transform.position.x-.25f)<.001f,"rejected sample did not move actor");
                var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.name="CollisionProbe";
                wall.transform.position=new Vector3(1.5f,2,0);wall.transform.localScale=new Vector3(.2f,5,5);
                Physics.SyncTransforms();motor.ApplyAuthoredDelta(2,new Vector3(3,0,0));
                Check(actor.transform.position.x<1.1f,"OpenKCC wall blocks authored displacement");
                var camera=new GameObject("IntegrationCamera").AddComponent<Camera>();
                var brain=camera.gameObject.AddComponent<CinemachineBrain>();brain.UpdateMethod=CinemachineBrain.UpdateMethods.ManualUpdate;
                var virtualCamera=new GameObject("IntegrationFollow").AddComponent<CinemachineCamera>();
                virtualCamera.Follow=actor.transform;virtualCamera.LookAt=actor.transform;
                virtualCamera.gameObject.AddComponent<CinemachineFollow>().FollowOffset=new Vector3(0,2,-5);
                virtualCamera.gameObject.AddComponent<CinemachineRotationComposer>();
                brain.ManualUpdate();
                Check(camera.transform.position.sqrMagnitude>1,"Cinemachine 3.1.7 updates actual Camera");
                Object.DestroyImmediate(wall);Object.DestroyImmediate(actor);
                File.WriteAllText(Output+"/unity-source-adapter-verification.json",new JObject {
                    ["pass"]=true,["checks"]=new JArray(passed),["count"]=passed.Count,
                    ["scope"]="source decision snapshots, actual HFSM lifecycle, Unity physics motor, camera update",
                    ["fullInputGameplayParity"]=false,["animationAndRootExtractionIntegrated"]=false,
                    ["playModeTested"]=false,["productionSceneSaved"]=false }.ToString());
                Debug.Log("REMIELLE_QUALIFIED_ADAPTER_PASS " + passed.Count);
            }
            catch(Exception ex)
            {
                File.WriteAllText(Output+"/unity-source-adapter-verification.json",new JObject{["pass"]=false,["error"]=ex.ToString(),["passedBeforeFailure"]=new JArray(passed)}.ToString());
                throw;
            }
        }
    }
}
