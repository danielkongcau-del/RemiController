using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using nickmaltbie.OpenKCC.Character;
using nickmaltbie.OpenKCC.Utils.ColliderCast;
using Remielle.ControllerRuntime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Remielle.Controller.Editor
{
    // Walk→Run/Fly 正式接入审计：批处理策略单测（T1/T2/T8/reset）+ PlayMode 全链剧本（T1-T7）。
    // 证据边界：60_Experiments/Movement/WalkToFly/FINAL_TRIGGER_REPORT.md（RUNTIME_RECONSTRUCTED）。
    public static class SourceWalkToRunAudit
    {
        public const string Output = "E:/ZZZ/ZCode/90_Builds/ControllerDependencies/implementation";
        const string Key = "RemielleWalkToRunFrameAudit";
        static double started;
        readonly static List<string> passed = new List<string>();

        [InitializeOnLoadMethod]
        static void Resume()
        {
            if (!SessionState.GetBool(Key, false)) return;
            started = EditorApplication.timeSinceStartup;
            EditorApplication.update -= Watch;
            EditorApplication.update += Watch;
        }

        static void Watch()
        {
            if (EditorApplication.timeSinceStartup - started > 240)
            { SessionState.SetBool(Key, false); EditorApplication.Exit(2); return; }
            if (!EditorApplication.isPlaying) return;
            var probe = UnityEngine.Object.FindFirstObjectByType<SourceWalkToRunProbe>();
            if (!probe || !probe.completed) return;
            SessionState.SetBool(Key, false);
            Debug.Log("REMIELLE_WALK_TO_RUN_FRAME_" + (probe.passed ? "PASS" : "FAIL"));
            EditorApplication.Exit(probe.passed ? 0 : 1);
        }

        static void Check(bool test, string name)
        {
            if (!test) throw new Exception(name);
            passed.Add(name);
        }

        public static void Run()
        {
            Directory.CreateDirectory(Output);
            try
            {
                passed.Clear();
                // ---- 策略纯单测（T8 帧率无关性 + T1/T2 语义 + reset 策略）----
                foreach (float fps in new[] { 30f, 60f, 120f })
                {
                    var policy = new SourceWalkToRunPolicy();
                    float delta = 1f / fps; int ticks = 0; int rises = 0; double riseTime = -1;
                    while (ticks < (int)(fps * 4) && rises == 0)
                    {
                        if (policy.Tick(delta, true, false)) { rises++; riseTime = (ticks + 1) * delta; }
                        ticks++;
                    }
                    Check(rises == 1, "T8@" + fps + "fps 恰好一次请求");
                    Check(Math.Abs(riseTime - 2.0) <= delta + 1e-4, "T8@" + fps + "fps 触发时间 " + riseTime.ToString("F4") + "s ≈2.0（不随渲染帧率漂移）");
                    Check(!policy.Tick(delta, true, false), "T8@" + fps + "fps 后续帧不再发请求（latch）");
                }
                {
                    var policy = new SourceWalkToRunPolicy { ThresholdSeconds = 2f };
                    for (int i = 0; i < 89; i++) policy.Tick(1f / 60f, true, false);
                    Check(!policy.Requested, "T1-unit 1.483s 未请求");
                    // 停止宽限：moving=false 且 delay 未落 → 计时保持
                    for (int i = 0; i < 3; i++) policy.Tick(1f / 60f, false, false);
                    Check(!policy.Requested && policy.Elapsed > 1.4f, "宽限窗口内暂停累计且不清零");
                    // reset（IsMovingDelay==false 语义）：清零
                    policy.Tick(1f / 60f, false, true);
                    Check(policy.Elapsed == 0 && !policy.Requested, "reset 信号清零计时与请求");
                    for (int i = 0; i < 130; i++) policy.Tick(1f / 60f, true, false);
                    Check(policy.Requested, "reset 后重新计满 2.0s 再次请求");
                }
                {
                    var delay = new SourceIsMovingDelay { GraceSeconds = .1f };
                    for (int i = 0; i < 6; i++) delay.Tick(1f / 60f, true);
                    Check(delay.Value, "IsMovingDelay 移动即为 true");
                    for (int i = 0; i < 5; i++) Check(delay.Tick(1f / 60f, false), "停止 0.083s 内保持（宽限）");
                    Check(!delay.Tick(1f / 60f, false), "停止 ≈0.1s 后落下");
                }
                {
                    var crt = new SourceChangeRunType(1) { CheckIntervalSeconds = 1f, Odds = 1f };
                    bool fired = false;
                    for (int i = 0; i < 130; i++) if (crt.Tick(1f / 60f, true)) fired = true;
                    Check(fired, "ChangeRunType odds=1 时 1s 间隔内必触发");
                    crt.Reset();
                    bool idleFired = false;
                    for (int i = 0; i < 130; i++) if (crt.Tick(1f / 60f, false)) idleFired = true;
                    Check(!idleFired, "ChangeRunType 非 RunLoop 态不计时");
                }
                // ---- Router 级 T3-T7（恢复的原生状态机语义，批处理 -quit 通道）----
                var src = new NativeControllerSource(File.ReadAllText("Assets/ControllerRuntime/Data/source-controller-pack.json"));
                var timing = new NativeTransitionTimingPolicy(true, false);
                const string Main = "Avatar_Female_Size02_RemielleOrigin_Controller";
                int IndexOf(string name)
                {
                    var states = (JArray)src.GetController(Main)["machines"][0]["states"];
                    return states.Select((s, i) => (s, i)).Single(x => (string)x.s["name"] == name).i;
                }
                float routerPrev = 0f;
                QualifiedStateRouter.Decision PreviewFrom(NativeParameterBank p, QualifiedStateRouter router, int state, float normalized, bool loop, float duration)
                {
                    return router.Preview(p, routerPrev, normalized, duration, 1, loop, timing, callbackContextQualified: true);
                }                int walkLoop = IndexOf("Walk_Loop"), w2r = IndexOf("Walk_To_RunLoop_01"), run1 = IndexOf("RunLoop_01"),
                    bridge = IndexOf("RunLoop_01_To_02"), evadeToRun = IndexOf("Evade_To_RunLoop_01"), runEnd = IndexOf("Run_End"), rush = IndexOf("Attack_Rush");
                // T3：Walk_Loop + Bool_WalkToRun=true 在循环末位（≥0.99999）经原生边进入 Walk_To_RunLoop_01
                {
                    var p = src.CreateParameters(Main);
                    p.SetBool(p.Hash("Bool_WalkToRun"), true); p.SetBool(p.Hash("Bool_IsMoving"), true);
                    var router = new QualifiedStateRouter(src, Main, 0, walkLoop);
                    var mid = PreviewFrom(p, router, walkLoop, .5f, true, .6f);
                    Check(!mid.Accepted || (int)mid.Transition.NextState != w2r, "T3 循环中段（0.5）不触发 Walk→Run（受 exit gate）");
                    var end = PreviewFrom(p, router, walkLoop, .999999f, true, .6f);
                    Check(end.Accepted && (int)end.Transition.NextState == w2r && Math.Abs(end.Transition.TransitionDuration - .2f) < .001f,
                        "T3 循环末位由原生边 Walk_Loop→Walk_To_RunLoop_01（0.2s）");
                    var off = src.CreateParameters(Main); off.SetBool(off.Hash("Bool_IsMoving"), true);
                    var no = PreviewFrom(off, router, walkLoop, .999999f, true, .6f);
                    Check(!(no.Accepted && (int)no.Transition.NextState == w2r), "T3 无 Bool_WalkToRun 时循环末位不进入（参数是必要条件）");
                }
                // T4a：Walk_To_RunLoop_01 在 0.9227 处无条件进 RunLoop_01（诊断 v2）
                {
                    var p = src.CreateParameters(Main); p.SetBool(p.Hash("Bool_IsMoving"), true);
                    var router = new QualifiedStateRouter(src, Main, 0, w2r);
                    var early = PreviewFrom(p, router, w2r, .97f, false, 2.9f);
                    Check(!(early.Accepted && (int)early.Transition.NextState == run1), "T4a Walk_To_Run 片尾帧前不进 RunLoop（useFrameCount=174/174）");
                    routerPrev = .98f; var exit = PreviewFrom(p, router, w2r, 1f, false, 2.9f); routerPrev = 0f;
                    Check(exit.Accepted && (int)exit.Transition.NextState == run1,
                        "T4a Walk_To_RunLoop_01→RunLoop_01（帧比门 174/174=片尾）实际: accepted=" + exit.Accepted +
                        " blocked=" + exit.BlockedReason + " next=" + (exit.Transition != null ? ((int)exit.Transition.NextState).ToString() : "null") +
                        " gateEligible=" + (exit.Result != null ? exit.Result.LastGateEligible.ToString() : "?") +
                        " visited=" + (exit.Result != null ? string.Join(",", exit.Result.VisitedCandidates) : "?") +
                        " overshoot=" + (exit.Result != null ? exit.Result.LastGateOvershoot.ToString() : "?"));
                }
                // T4b/T-variant：RunLoop_01 + Trigger_ChangeRunType → 01_To_02 桥
                {
                    var p = src.CreateParameters(Main); p.SetBool(p.Hash("Bool_IsMoving"), true); p.SetBool(p.Hash("Bool_IsMovingDelay"), true);
                    p.SetTrigger(p.Hash("Trigger_ChangeRunType"));
                    var router = new QualifiedStateRouter(src, Main, 0, run1);
                    var d = PreviewFrom(p, router, run1, .2f, true, 3.5f);
                    Check(d.Accepted && (int)d.Transition.NextState == bridge, "T4b RunLoop_01 --Trigger_ChangeRunType--> RunLoop_01_To_02");
                }
                // T5：RunLoop_01 + IsMovingDelay=false → Run_End（宽限退出）
                {
                    var p = src.CreateParameters(Main); p.SetBool(p.Hash("Bool_IsMoving"), false); p.SetBool(p.Hash("Bool_IsMovingDelay"), false);
                    var router = new QualifiedStateRouter(src, Main, 0, run1);
                    var d = PreviewFrom(p, router, run1, .2f, true, 3.5f);
                    Check(d.Accepted && (int)d.Transition.NextState == runEnd, "T5 RunLoop_01 --IsMovingDelay==false--> Run_End");
                }
                // T6：Evade_To_RunLoop_01 在片尾帧（210/210）无条件进 RunLoop_01（不经 Bool_WalkToRun）
                {
                    var p = src.CreateParameters(Main);
                    p.SetBool(p.Hash("Bool_IsMoving"), true); p.SetBool(p.Hash("Bool_IsMovingDelay"), true); // 闪避后持续移动上下文
                    var router = new QualifiedStateRouter(src, Main, 0, evadeToRun);
                    routerPrev = .98f; var d = PreviewFrom(p, router, evadeToRun, 1f, false, 3.5f); routerPrev = 0f;
                    Check(d.Accepted && (int)d.Transition.NextState == run1, "T6 Evade_To_RunLoop_01→RunLoop_01（无 WalkToRun 参与，帧比门）next=" +
                        (d.Transition != null ? ((int)d.Transition.NextState).ToString() : "null") + " blocked=" + d.BlockedReason);
                }
                // T7：RunLoop_01 + PressAttackA → Attack_Rush（原生 T5 边）
                {
                    var p = src.CreateParameters(Main); p.SetBool(p.Hash("Bool_IsMoving"), true);
                    p.SetTrigger(p.Hash("Trigger_PressAttackA"));
                    var router = new QualifiedStateRouter(src, Main, 0, run1);
                    var d = PreviewFrom(p, router, run1, .2f, true, 3.5f);
                    Check(d.Accepted && (int)d.Transition.NextState == rush, "T7 RunLoop_01 --PressAttackA--> Attack_Rush");
                }
                File.WriteAllText(Output + "/walk-to-run-policy-unit.json", new JObject {
                    ["pass"] = true, ["checks"] = new JArray(passed), ["count"] = passed.Count,
                    ["scope"] = "纯策略单测：T8 帧率无关（30/60/120）、latch、宽限/reset、IsMovingDelay 真值、ChangeRunType" }.ToString());
                Debug.Log("REMIELLE_WALK_TO_RUN_POLICY_PASS " + passed.Count);
            }
            catch (Exception ex)
            {
                File.WriteAllText(Output + "/walk-to-run-policy-unit.json",
                    new JObject { ["pass"] = false, ["error"] = ex.ToString(), ["passedBeforeFailure"] = new JArray(passed) }.ToString());
                throw;
            }
        }

        public static void RunFrames()
        {
            Directory.CreateDirectory(Output);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var actor = new GameObject("WalkToRunActor");
            var capsule = actor.AddComponent<CapsuleCollider>(); capsule.center = new Vector3(0, .9f, 0); capsule.height = 1.8f; capsule.radius = .3f;
            actor.AddComponent<CapsuleColliderCast>(); actor.AddComponent<KCCMovementEngine>();
            actor.GetComponent<Rigidbody>().isKinematic = true;
            var motor = actor.AddComponent<RemielleAuthoredMotor>();
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/V3/Remielle_V3_Animated.prefab"));
            visual.transform.SetParent(actor.transform, false);
            var driver = visual.GetComponentInChildren<RemielleNativeAnimation>();
            driver.autoplay = false; driver.enabled = false; driver.nativeAnimation.enabled = false;
            var light = new GameObject("WalkToRunLight").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1;
            light.transform.rotation = Quaternion.Euler(40, -30, 0);
            RenderSettings.ambientLight = new Color(.4f, .4f, .4f);
            var probe = new GameObject("SourceWalkToRunProbe").AddComponent<SourceWalkToRunProbe>();
            probe.driver = driver; probe.visual = visual.transform; probe.motor = motor;
            probe.reportDirectory = Output;
            probe.sourcePack = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/ControllerRuntime/Data/source-controller-pack.json");
            probe.timeSettings = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/ControllerRuntime/Data/source-time-settings.json");
            Directory.CreateDirectory("Assets/ControllerIntegration/Scenes");
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), "Assets/ControllerIntegration/Scenes/Remielle_WalkToRunProbe.unity");
            SessionState.SetBool(Key, true);
            Resume();
            EditorApplication.EnterPlaymode();
        }
    }
}
