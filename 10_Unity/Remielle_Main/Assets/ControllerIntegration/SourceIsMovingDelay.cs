using System;

namespace Remielle.Controller
{
    // Bool_IsMovingDelay 写入者复刻（ability: RemielleOrigin_IsMovingDelay）。
    // 语义：跟随 Bool_IsMoving；下降沿带 GraceSeconds 宽限（持续停止约 0.1s 后才为 false）。
    // 证据等级：RUNTIME_OBSERVED 真值表（FINAL_TRIGGER_REPORT §2.4：上升 2-4 帧内跟随、
    // 下降典型 0.098-0.115s）× STATIC_BYTE（ability DelayTimer Duration=0.1s）交叉。
    // 本类只做纯逻辑；由宿主每帧把 Value 泵入 NativeParameterBank。
    public sealed class SourceIsMovingDelay
    {
        public float GraceSeconds = .1f;
        public bool Value { get; private set; }
        float stoppedFor;

        public bool Tick(float delta, bool isMoving)
        {
            if (isMoving) { Value = true; stoppedFor = 0; }
            else if (Value)
            {
                stoppedFor += delta;
                if (stoppedFor >= GraceSeconds) Value = false;
            }
            return Value;
        }

        public void Reset() { Value = false; stoppedFor = 0; }
    }
}
