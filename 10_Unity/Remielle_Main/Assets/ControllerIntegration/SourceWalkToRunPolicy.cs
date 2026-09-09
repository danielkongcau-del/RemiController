using System;

namespace Remielle.Controller
{
    // 普通移动 Walk→Run/Fly 的触发策略（Bool_WalkToRun 写入者复刻）。
    //
    // 证据等级与来源（60_Experiments/Movement/WalkToFly/FINAL_TRIGGER_REPORT.md）：
    // - ThresholdSeconds=2.0：RUNTIME_RECONSTRUCTED——9 个独立回合 Δt(IsMoving↑→WTR↑)
    //   =2.026~2.081s、stdev 0.018s（authored 60Hz 域）。既非静态恢复的原始常量，也非猜测值。
    // - 以秒累计而非 120 帧：60fps 只是动画 authored 采样率，未证明原 writer 使用 FrameCount；
    //   实现必须帧率无关（E2 未执行，帧率无关性为 replica 设计选择）。
    //
    // Reset 策略（Reset 信号由宿主提供）：REPLICA_POLICY_PENDING_RUNTIME_CONFIRMATION——
    // 当前采用 Bool_IsMovingDelay==false（持续停止 ≈0.1s）重置。运行时已证 ≥0.413s 停止重置；
    // <0.413s 边界未证。若后续运行时证据要求立即重置，只改宿主的 reset 信号。
    //
    // 请求语义：rising-edge/latch——达到阈值当帧返回一次 true（Requested 闩锁），不实现
    // `IsMoving && elapsed>=T` 的持续组合 Bool；原作 WTR→false 为惰性/事件驱动
    // （0.015s~124s），宿主按 REPLICA_LIFECYCLE_POLICY 将参数与 Requested 同步
    // （停止宽限后一并落回），不冒充已恢复原作 false writer。
    public sealed class SourceWalkToRunPolicy
    {
        public float ThresholdSeconds = 2f;
        public float Elapsed { get; private set; }
        public bool Requested { get; private set; }

        /// <summary>每帧求值。返回 true 表示本帧恰好越过阈值（上升沿，仅此一次）。</summary>
        public bool Tick(float delta, bool movingEligible, bool resetSignal)
        {
            if (resetSignal) { Reset(); return false; }
            if (!movingEligible) return false; // 停止但不满足 reset 时：暂停累计（宽限窗口；E4 边界未证）
            Elapsed += delta;
            if (!Requested && Elapsed >= ThresholdSeconds) { Requested = true; return true; }
            return false;
        }

        public void Reset() { Elapsed = 0; Requested = false; }
    }
}
