using System;
using UnityEngine;

namespace Remielle.Controller
{
    // RunLoop_01/02 随机换型（Trigger_ChangeRunType 写入者复刻）。
    // 证据等级：STATIC_BYTE——ability RemielleOrigin_ChangeRunType：
    //   AttachStateWithModifierMixin 挂 RunLoop_01/RunLoop_02；ChangeRunTypeTimerModifier
    //   ThinkInterval=1s，RandomOperator Odds=AS_ChangeRunType_Odds=0.25。
    //   specials AS_ChangeRunType_MaxTime=8 / MinTime=5 的用途未接入（如实登记，未证）。
    // 语义：仅当处于 RunLoop_01/02（当前或混合目标）时计时；每 CheckIntervalSeconds 判定一次，
    // 命中概率 Odds；返回 true 时宿主 SetTrigger(Trigger_ChangeRunType)，由恢复的
    // RunLoop_01↔RunLoop_02 桥接 transition 消费（本项目不直接切动画）。
    public sealed class SourceChangeRunType
    {
        public float CheckIntervalSeconds = 1f;
        [Range(0, 1)] public float Odds = .25f;
        readonly System.Random random;
        float elapsed;

        public SourceChangeRunType(int seed) { random = new System.Random(seed); }
        public SourceChangeRunType() : this(Environment.TickCount) { }

        public void Reset() => elapsed = 0;

        /// <summary>返回 true 表示本判定命中（应发 Trigger_ChangeRunType）。</summary>
        public bool Tick(float delta, bool inRunLoop)
        {
            if (!inRunLoop) { elapsed = 0; return false; }
            elapsed += delta;
            if (elapsed < CheckIntervalSeconds) return false;
            elapsed = 0;
            return random.NextDouble() < Odds;
        }
    }
}
