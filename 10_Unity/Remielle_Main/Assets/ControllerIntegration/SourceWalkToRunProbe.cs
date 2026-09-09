using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Remielle.Controller
{
    // Walk→Run/Fly 接入的 PlayMode 剧本探针：以固定 1/60 步长驱动 RemielleSourceController.TickInput，
    // 覆盖 T1-T7（T8 与策略纯单测在 SourceWalkToRunAudit.Run 的批处理段）。
    // 断言依据：60_Experiments/Movement/WalkToFly/FINAL_TRIGGER_REPORT.md。
    public sealed class SourceWalkToRunProbe : MonoBehaviour
    {
        public TextAsset sourcePack, timeSettings;
        public RemielleNativeAnimation driver;
        public Transform visual;
        public RemielleAuthoredMotor motor;
        public string reportDirectory;
        public bool completed, passed;

        const float Step = 1f / 60f;
        RemielleSourceController controller;
        readonly List<string> failures = new List<string>();
        readonly List<string> checks = new List<string>();
        readonly List<JObject> timeline = new List<JObject>();
        int tick, phase, traceBase, requestRises;
        double requestTime = -1, walkToRunEnterTime = -1, runLoopEnterTime = -1, variantSwitchTime = -1;

        void Start()
        {
            var host = new GameObject("WalkToRunControllerHost");
            host.SetActive(false); // 先赋引用再激活，避免 AddComponent 立即触发 Awake
            controller = host.AddComponent<RemielleSourceController>();
            controller.driver = driver; controller.visual = visual; controller.motor = motor;
            controller.sourcePack = sourcePack; controller.timeSettings = timeSettings;
            controller.readDevices = false;
            host.SetActive(true);            // Awake 在此运行，策略在 Awake 内创建
            controller.ChangeRunType.Odds = 1f; // 确定性换型验证（原值 0.25，STATIC_BYTE）
            Mark("sessionReady " + CurrentNames());
        }

        static Vector2 Move => new Vector2(0, 1);

        void Drive(bool moving, bool evade = false, bool attack = false)
        {
            bool wasRequested = controller.WalkToRun.Requested;
            controller.TickInput(Step, moving ? Move : Vector2.zero, evade, false, attack, false, false);
            tick++;
            if (!wasRequested && controller.WalkToRun.Requested)
            {
                requestRises++; requestTime = TotalSeconds();
                timeline.Add(new JObject { ["event"] = "walkToRunRequest", ["t"] = requestTime });
            }
        }

        void EnterPhase(int next, string name)
        {
            globalTickBase += tick; tick = 0; traceBase = controller.Session.Trace.Count;
            timeline.Add(new JObject { ["event"] = name, ["t"] = TotalSeconds(), ["state"] = CurrentNames() });
        }

        double TotalSeconds() => (globalTickBase + tick) * Step;
        int globalTickBase; // 单调：阶段切换时累计，时间戳不回卷

        void Mark(string name) => timeline.Add(new JObject { ["event"] = name, ["t"] = TotalSeconds(), ["state"] = CurrentNames() });

        string CurrentNames()
        {
            var s = controller.Session;
            return s.StateName(s.CurrentState) + (s.NextState.HasValue ? "->" + s.StateName(s.NextState.Value) : "");
        }

        bool InStateOrBlending(string name)
        {
            var s = controller.Session;
            return s.StateName(s.CurrentState) == name || (s.NextState.HasValue && s.StateName(s.NextState.Value) == name);
        }

        List<(string from, string to)> TraceSincePhaseStart() => controller.Session.Trace
            .Skip(traceBase)
            .Where(e => (string)e["event"] == "transition-start")
            .Select(e => (from: controller.Session.StateName((int)e["sourceState"]), to: controller.Session.StateName((int)e["targetState"])))
            .ToList();

        bool Saw(string from, string to) => TraceSincePhaseStart().Any(p => p.from == from && p.to == to);

        void Update()
        {
            if (completed) return;
            if (controller == null || !controller.Ready)
            {
                if (controller != null && controller.Error != null) Fail("controller error: " + controller.Error.Split('\n')[0]);
                if (controller == null || !controller.Ready) return;
            }
            for (int i = 0; i < 6 && !completed; i++) StepOnce();
        }

        void StepOnce()
        {
            switch (phase)
            {
                case 0: // 冷启动
                    Drive(false);
                    if (tick >= 30) EnterPhase(1, "phaseA_move1.5s");
                    break;
                case 1: // T1：持续移动 1.5s 不得请求
                    Drive(true);
                    if (controller.WalkToRun.Requested) { Fail("T1: 1.5s 内出现 WalkToRun 请求"); EnterPhase(99, "abort"); break; }
                    if (tick * Step >= 1.5f) { Check(true, "T1 1.5s 无请求（elapsed=" + controller.WalkToRun.Elapsed.ToString("F2") + "）"); EnterPhase(2, "phaseB_continueToRun"); }
                    break;
                case 2: // T2/T3/T4：请求恰好一次；经原生边进入 W2R→RunLoop_01；变体切换
                    Drive(true);
                    if (requestTime >= 0 && walkToRunEnterTime < 0 && InStateOrBlending("Walk_To_RunLoop_01"))
                    { walkToRunEnterTime = TotalSeconds(); Mark("enter Walk_To_RunLoop_01（transition start）"); }
                    if (runLoopEnterTime < 0 && InStateOrBlending("RunLoop_01"))
                    { runLoopEnterTime = TotalSeconds(); Mark("enter RunLoop_01（视觉进入悬浮）"); }
                    if (variantSwitchTime < 0 && Saw("RunLoop_01", "RunLoop_01_To_02"))
                    { variantSwitchTime = TotalSeconds(); Mark("variantSwitch RunLoop_01→01_To_02"); }
                    if (requestTime >= 0 && TotalSeconds() - requestTime > 12f) { Fail("T3/T4: 请求后 12s 未完成 RunLoop 进入/换型"); EnterPhase(99, "abort"); break; }
                    if (runLoopEnterTime >= 0 && variantSwitchTime >= 0)
                    {
                        Check(Saw("Walk_Loop", "Walk_To_RunLoop_01"), "T3 由原生边 Walk_Loop→Walk_To_RunLoop_01 进入（非代码直切）");
                        Check(Saw("Walk_To_RunLoop_01", "RunLoop_01"), "T4a Walk_To_RunLoop_01→RunLoop_01");
                        Check(true, "T4b RunLoop_01 长期维持并发生 01→02 变体切换（odds=1 验证）");
                        EnterPhase(3, "phaseC_stop");
                    }
                    if (tick * Step > 30f) { Fail("phaseB 超时（当前 " + CurrentNames() + "）"); EnterPhase(99, "abort"); }
                    break;
                case 3: // T5：停止 → IsMovingDelay 宽限后 Run_End
                    Drive(false);
                    if (InStateOrBlending("Run_End"))
                    {
                        Check(true, "T5 停止后进入 Run_End（经 Bool_IsMovingDelay 宽限）");
                        Check(!controller.WalkToRun.Requested, "T5 停止后请求已清除（REPLICA_LIFECYCLE_POLICY）");
                        EnterPhase(4, "phaseD_evade");
                    }
                    else if (tick * Step > 3f) { Fail("T5: 停止 3s 未进入 Run_End（当前 " + CurrentNames() + "）"); EnterPhase(99, "abort"); }
                    break;
                case 4: // T6：短移 + 闪避 → Evade_To_RunLoop_01 → RunLoop_01，不经 WalkToRun
                    Drive(true, evade: Math.Abs(tick * Step - .4f) < Step * .5f);
                    if (InStateOrBlending("RunLoop_01") || InStateOrBlending("RunLoop_02"))
                    {
                        if (Saw("Evade_To_RunLoop_01", "RunLoop_01"))
                        {
                            Check(!Saw("Walk_Loop", "Walk_To_RunLoop_01"), "T6 本阶段未发生 WalkToRun 转换（闪避独立入口）");
                            Check(true, "T6 Evade_To_RunLoop_01→RunLoop_01");
                            EnterPhase(5, "phaseE_attack");
                        }
                        else { Fail("T6: 进入 RunLoop 但缺 Evade_To_RunLoop_01→RunLoop_01 边；实际边=" + string.Join(",", TraceSincePhaseStart().Select(p => p.from + "->" + p.to))); EnterPhase(99, "abort"); }
                    }
                    else if (tick * Step > 16f) { Fail("T6 超时（当前 " + CurrentNames() + "）"); EnterPhase(99, "abort"); }
                    break;
                case 5: // T7：Run 态直出 Attack_Rush
                    Drive(true, attack: tick == 2);
                    if (Saw("RunLoop_01", "Attack_Rush") || Saw("RunLoop_02", "Attack_Rush"))
                    { Check(true, "T7 Run 态直出 Attack_Rush"); EnterPhase(6, "finish"); }
                    else if (tick * Step > 2.5f) { Fail("T7: 2.5s 未从 RunLoop 进入 Attack_Rush（当前 " + CurrentNames() + "）"); EnterPhase(99, "abort"); }
                    break;
                case 6: case 99: Finish(); break;
            }
        }

        void Check(bool ok, string name) { if (ok) checks.Add(name); else Fail(name); }
        void Fail(string message) => failures.Add(message);

        void Finish()
        {
            completed = true;
            passed = failures.Count == 0;
            if (requestRises == 1) checks.Add("T2 WalkToRun 请求恰好一次");
            else Fail("T2 请求次数=" + requestRises);
            if (requestTime >= 0)
                checks.Add("T-threshold 移动起点→请求 = " + (requestTime - 30 * Step).ToString("F3") + "s");
            var report = new JObject {
                ["pass"] = passed,
                ["checks"] = new JArray(checks),
                ["failures"] = new JArray(failures),
                ["timeline"] = new JArray(timeline),
                ["requestRises"] = requestRises,
                ["thresholdSeconds"] = controller ? controller.WalkToRun.ThresholdSeconds : 2f,
                ["threeTimes"] = new JObject {
                    ["thresholdTime"] = requestTime, ["walkToRunTransitionStart"] = walkToRunEnterTime,
                    ["runLoopEnter"] = runLoopEnterTime, ["variantSwitch"] = variantSwitchTime },
                ["scope"] = "T1-T7 PlayMode 剧本（固定 1/60 步长）；T8 帧率无关性在批处理段" };
            try
            {
                System.IO.Directory.CreateDirectory(reportDirectory);
                System.IO.File.WriteAllText(System.IO.Path.Combine(reportDirectory, "walk-to-run-playmode-verification.json"), report.ToString());
            }
            catch (Exception ex) { Debug.LogException(ex); passed = false; }
            Debug.Log("REMIELLE_WALK_TO_RUN_" + (passed ? "PASS" : "FAIL") + " checks=" + checks.Count + " failures=" + failures.Count);
        }
    }
}
