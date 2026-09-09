using System;

namespace Remielle.ControllerRuntime
{
    public readonly struct NativeClipTimeResult
    {
        public readonly float Seconds, Phase, CycleCount;
        public NativeClipTimeResult(float seconds, float phase, float cycles)
        { Seconds = seconds; Phase = phase; CycleCount = cycles; }
    }

    public readonly struct NativeLeafTiming
    {
        public readonly float Speed, Cycle;
        public readonly bool Mirror;
        public NativeLeafTiming(float speed, float cycle, bool mirror)
        { Speed = speed; Cycle = cycle; Mirror = mirror; }

        // cc4840..cc4893, including cc4db0. State mirror contributes half a
        // cycle independently of the resulting leaf mirror flag.
        public static NativeLeafTiming Combine(float authoredStateSpeed, float stateCycle,
            bool stateMirror, NativeBlendLeaf leaf)
        {
            float cycle = (float)(stateCycle + leaf.CycleOffset);
            cycle = (float)((stateMirror ? .5f : 0f) + cycle);
            return new NativeLeafTiming((float)(authoredStateSpeed * leaf.SpeedScale),
                cycle, stateMirror ^ leaf.Mirror);
        }
    }

    public static class NativeClipTime
    {
        // ClipPlayable constructor cc37c2. This is a constructor default, not
        // proof that gameplay cannot subsequently enable time quantization.
        public const float DefaultSampleRate = -1f;

        // Original 12fea10 plus its complete modff helper at 1a87a08. The
        // negative-input flag comes from the caller's unshifted time; deriving
        // it from the shifted fractional part changes cycle-boundary behavior.
        public static NativeClipTimeResult Map(float normalized, NativeMotionInterval interval,
            float cycleOffset, bool loop, float signedSpeed, bool negativeInput, float sampleRate)
        {
            float span = (float)(interval.Stop - interval.Start);
            float shifted = (float)(normalized + cycleOffset);
            float fraction = Split(shifted, out float cycles);
            float phase;
            if (loop) phase = fraction;
            else
            {
                phase = 0f > normalized ? 0f : normalized;
                phase = 1f < phase ? 1f : phase;
                cycles = 0f;
            }
            if (loop && negativeInput) phase = (float)(phase + 1f);
            if (BitConverter.SingleToInt32Bits(signedSpeed) < 0) phase = (float)(1f - phase);
            float seconds = (float)(phase * span);
            seconds = (float)(seconds + interval.Start);
            if (sampleRate > 0f && span > 0f)
            {
                float value = (float)(seconds * sampleRate);
                // Native add/sub of 2^23; preserve each float32 rounding.
                if (value >= 0f) { value = (float)(value + 8388608f); value = (float)(value - 8388608f); }
                else { value = (float)(value - 8388608f); value = (float)(value + 8388608f); }
                seconds = (float)(value / sampleRate);
                float inverse = (float)(1f / span);
                phase = (float)(seconds - interval.Start);
                phase = (float)(phase * inverse);
                phase = 0f > phase ? 0f : phase;
                phase = 1f < phase ? 1f : phase;
            }
            return new NativeClipTimeResult(seconds, phase, cycles);
        }

        static float Split(float value, out float integral)
        {
            uint raw = unchecked((uint)BitConverter.SingleToInt32Bits(value));
            uint exponent = (raw >> 23) & 255u;
            float signedZero = BitConverter.Int32BitsToSingle(unchecked((int)(raw & 0x80000000u)));
            if (exponent < 127u) { integral = signedZero; return value; }
            if (exponent < 150u)
            {
                uint mask = uint.MaxValue << (int)(150u - exponent);
                integral = BitConverter.Int32BitsToSingle(unchecked((int)(raw & mask)));
                if (value == integral && value < 0f) return signedZero;
                return (float)(value - integral);
            }
            integral = value;
            return (raw & 0x7fffffffu) > 0x7f800000u ? (float)(value + value) : signedZero;
        }
    }

    public readonly struct NativeClipSampleInput
    {
        public readonly float Normalized, PreviousNormalized;
        public NativeClipSampleInput(float current, float previous)
        { Normalized = current; PreviousNormalized = previous; }
    }

    // Stateful timing for a qualified clip leaf. Graph allocation, non-clip
    // inputs, layer weights and recursive SetTime propagation are not inferred.
    public sealed class NativeClipPlayback
    {
        public NativeMotionInterval Interval { get; }
        public double Time { get; private set; }
        public double PreviousSetTime { get; private set; }
        public float PreviousSeconds { get; private set; }
        public bool HasSetTime { get; private set; }
        public bool RepeatedSetTime { get; private set; }
        public float OverrideOld { get; private set; } = -1f;
        public float OverrideNew { get; private set; } = -1f;

        public NativeClipPlayback(NativeMotionInterval interval) { Interval = interval; }

        public void ApplyClock(NativeStateClockResult clock) => ApplyClock(clock.Normalized, clock.PreviousNormalized, false);

        // cc81a0 dispatches through the original clip-length and SetTime
        // virtuals, then overwrites previous seconds from the state clock.
        public void ApplyClock(float sampleNormalized, float previousClockNormalized, bool timeParameterOverride)
        {
            if (timeParameterOverride)
            {
                if (sampleNormalized != OverrideOld) { OverrideOld = OverrideNew; OverrideNew = sampleNormalized; }
            }
            else OverrideOld = -1f;
            float length = (float)(Interval.Stop - Interval.Start);
            double next = (float)(length * sampleNormalized);
            RepeatedSetTime = HasSetTime && Time == next;
            if (!HasSetTime) PreviousSetTime = Time;
            Time = next; HasSetTime = true;
            PreviousSeconds = (float)(length * previousClockNormalized);
        }

        // cc6090. Evaluation offset and extrapolation speed remain explicit
        // runtime inputs; they are not substituted with Unity deltaTime.
        public NativeClipSampleInput GetSampleInput(float evaluationOffset, float extrapolationSpeed)
        {
            float seconds;
            if (evaluationOffset < 0f)
            {
                float amount = -evaluationOffset;
                amount = 1f < amount ? 1f : amount;
                float previous = 0f > PreviousSeconds ? 0f : PreviousSeconds;
                float remaining = (float)(1f - amount);
                float oldPart = (float)(remaining * previous);
                float newPart = (float)((float)Time * amount);
                seconds = (float)(newPart + oldPart);
            }
            else if (evaluationOffset > 0f)
            {
                float extra = (float)(evaluationOffset * extrapolationSpeed);
                seconds = (float)((double)extra + Time);
            }
            else seconds = (float)Time;
            float length = (float)(Interval.Stop - Interval.Start);
            return new NativeClipSampleInput(length == 0f ? 0f : (float)(seconds / length),
                length == 0f ? 0f : (float)(PreviousSeconds / length));
        }

        public NativeClipTimeResult GetSampleTime(NativeLeafTiming timing, float clipCycle, bool loop,
            float evaluationOffset, float extrapolationSpeed, float sampleRate)
        {
            var input = GetSampleInput(evaluationOffset, extrapolationSpeed);
            float cycle = (float)(clipCycle + timing.Cycle);
            return NativeClipTime.Map(input.Normalized, Interval, cycle, loop, timing.Speed, input.Normalized < 0f, sampleRate);
        }
    }
}
