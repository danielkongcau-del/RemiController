using System;
using Newtonsoft.Json.Linq;

namespace Remielle.ControllerRuntime
{
    public readonly struct NativeStateClockOptions
    {
        public readonly bool Current, Loop, CarryFromNext, TimeOverrideEnabled, OverrideIsSeconds;
        public readonly bool SubsystemCorrectionFlag, TimeManagerCorrectionFlag;
        public readonly float TimeOverride;

        public NativeStateClockOptions(bool current, bool loop, bool carryFromNext,
            bool timeOverrideEnabled, float timeOverride, bool overrideIsSeconds,
            bool subsystemCorrectionFlag, bool timeManagerCorrectionFlag)
        {
            Current = current; Loop = loop; CarryFromNext = carryFromNext;
            TimeOverrideEnabled = timeOverrideEnabled; TimeOverride = timeOverride;
            OverrideIsSeconds = overrideIsSeconds;
            SubsystemCorrectionFlag = subsystemCorrectionFlag; TimeManagerCorrectionFlag = timeManagerCorrectionFlag;
        }
    }

    public readonly struct NativeStateClockResult
    {
        public readonly float PreviousNormalized, Normalized, FrameCount, EffectiveDuration, EffectiveSpeed;
        public readonly bool CorrectedFrameCount, CorrectionDiagnostic;
        public NativeStateClockResult(float previous, float normalized, float frames, float duration,
            float speed, bool corrected, bool diagnostic)
        {
            PreviousNormalized = previous; Normalized = normalized; FrameCount = frames;
            EffectiveDuration = duration; EffectiveSpeed = speed;
            CorrectedFrameCount = corrected; CorrectionDiagnostic = diagnostic;
        }
    }

    // Original clock arithmetic after the motion-length query, before playable
    // application. The caller must supply the qualified blended motion length;
    // this class does not guess it from a state name or average clip durations.
    public sealed class NativeStateClock
    {
        readonly float stateSpeed;
        readonly uint speedParameter;

        public NativeStateClock(JObject state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            stateSpeed = (float)state["speed"];
            speedParameter = (uint)state["sp"];
        }

        public NativeStateClockResult Advance(NativeLayerTransitionState state,
            NativeTransitionStartCommand command, NativeParameterBank parameters,
            float blendedMotionLength, float deltaSeconds, float animatorSpeed, NativeStateClockOptions options)
        {
            if (state == null || command == null || parameters == null) throw new ArgumentNullException();
            Finite(blendedMotionLength, nameof(blendedMotionLength));
            Finite(deltaSeconds, nameof(deltaSeconds)); Finite(animatorSpeed, nameof(animatorSpeed));
            if (blendedMotionLength < 0f) throw new ArgumentOutOfRangeException(nameof(blendedMotionLength));
            float length = blendedMotionLength == 0f ? 1f : blendedMotionLength;
            // Keep the native mulss rounding before divisions and frame-limit
            // arithmetic. A nested C# expression can retain extra precision.
            float lengthFrames = length * 60f;
            if (!options.Current) state.CachedDestinationLength = length;
            float multiplier = 1f;
            if (speedParameter != 0 && parameters.TryGetKind(speedParameter, out int kind))
            {
                if (kind != 1) throw new ArgumentException("State speed parameter is not a float");
                multiplier = parameters.GetFloat(speedParameter);
            }
            // Native cfdb30 preserves this multiplication order and takes the
            // absolute authored state speed. Clip-direction binding is separate.
            float speed = Math.Abs(stateSpeed) * multiplier;
            speed *= animatorSpeed;
            float duration = speed == 0f ? float.PositiveInfinity : length / Math.Abs(speed);
            if (options.Current) { state.SourceDuration = duration; state.CurrentSpeedMultiplier = multiplier; }
            else { state.DestinationDuration = duration; state.NextSpeedMultiplier = multiplier; }
            float timeOverride = -1f;
            if (options.TimeOverrideEnabled)
            {
                timeOverride = options.TimeOverride;
                if (timeOverride >= 0f && options.OverrideIsSeconds) timeOverride /= length;
            }
            float delta = speed * deltaSeconds;
            delta /= length;
            if (state.NeedsDestinationStart && command.OffsetIsFrames)
            {
                command.Offset /= lengthFrames;
                command.OffsetIsFrames = false;
            }
            float previous = options.Current ? state.CurrentNormalizedTime : state.NextNormalizedTime;
            float frames = options.Current ? state.CurrentFrameCount : state.NextFrameCount;
            float normalized;
            bool corrected = false, diagnostic = false;
            if (state.NeedsDestinationStart && command.Command == 0)
            {
                float carry = CopySignBitXor(command.OvershootSeconds, speed);
                normalized = carry / duration;
                normalized += command.Offset;
                previous = normalized - delta;
                frames = length * normalized;
                frames *= 60f;
                state.NeedsDestinationStart = false;
                command.OvershootSeconds = 0f;
            }
            else if (options.CarryFromNext)
            {
                normalized = command.OvershootSeconds / duration;
                normalized += state.NextNormalizedTime;
                previous = normalized - delta;
                frames = length * normalized;
                frames *= 60f;
                command.OvershootSeconds = 0f;
            }
            else
            {
                normalized = delta + previous;
                float previousFrames = frames;
                float seconds = length * normalized;
                frames = seconds * 60f;
                if (options.SubsystemCorrectionFlag && options.TimeManagerCorrectionFlag && speed == 1f)
                {
                    float deltaFrames = deltaSeconds * 60f;
                    float rounded = deltaFrames + .5f;
                    if (rounded < 0f) rounded -= 0.9999999403953552f;
                    int step = TruncateInt32(rounded);
                    if (Math.Abs(deltaFrames - (float)step) < 0.0001f &&
                        unchecked(TruncateInt32(frames) - TruncateInt32(previousFrames)) != step)
                    {
                        frames = (float)step + previousFrames;
                        normalized = frames / lengthFrames;
                        corrected = true;
                        float correctedSeconds = length * normalized;
                        diagnostic = Math.Abs(correctedSeconds - seconds) > .001f;
                    }
                }
                if (!options.Loop)
                {
                    normalized = Math.Min(1f, Math.Max(0f, normalized));
                    float roundedLengthFrames = lengthFrames + .5f;
                    frames = Math.Min(Math.Max(frames, 0f), (float)Math.Floor((double)roundedLengthFrames));
                }
            }
            if (timeOverride >= 0f)
            {
                normalized = timeOverride;
                frames = timeOverride * length;
                frames *= 60f;
            }
            if (options.Current) { state.CurrentNormalizedTime = normalized; state.CurrentFrameCount = frames; }
            else { state.NextNormalizedTime = normalized; state.NextFrameCount = frames; }
            return new NativeStateClockResult(previous, normalized, frames, duration, speed, corrected, diagnostic);
        }

        static float CopySignBitXor(float value, float sign)
        {
            int raw = BitConverter.SingleToInt32Bits(value) ^ (BitConverter.SingleToInt32Bits(sign) & int.MinValue);
            return BitConverter.Int32BitsToSingle(raw);
        }
        internal static int TruncateInt32(float value) => float.IsNaN(value) || value >= 2147483648f || value < -2147483648f
            ? int.MinValue : unchecked((int)value);
        static void Finite(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentOutOfRangeException(name);
        }
    }

    public readonly struct NativeTransitionProgressResult
    {
        public readonly float PreviousWeight, Weight, InverseDuration;
        public NativeTransitionProgressResult(float previous, float weight, float inverseDuration)
        { PreviousWeight = previous; Weight = weight; InverseDuration = inverseDuration; }
    }

    public static class NativeTransitionProgress
    {
        // Original d00140..d00208. These are caller-supplied timing inputs, not
        // a replacement for candidate-list interruption ordering or layer blend.
        public static NativeTransitionProgressResult Advance(NativeLayerTransitionState state,
            float scaledDeltaSeconds, float exitOvershootNormalized, bool suppressDelta,
            bool priorTransitionFixedDuration)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            bool fixedDuration = state.FixedDuration || priorTransitionFixedDuration;
            float divisor = fixedDuration ? 1f : state.SourceDuration;
            float delta = suppressDelta ? 0f : scaledDeltaSeconds / (divisor == 0f ? 1f : divisor);
            float carryScale = fixedDuration && state.SourceDuration != float.PositiveInfinity ? state.SourceDuration : 1f;
            float previous = state.TransitionElapsed;
            float increment;
            if (state.TransitionDuration == 0f) increment = 1f;
            else
            {
                increment = carryScale * exitOvershootNormalized;
                increment += delta;
                increment /= state.TransitionDuration;
            }
            state.TransitionElapsed = increment + previous;
            return new NativeTransitionProgressResult(Clamp(previous), Clamp(state.TransitionElapsed),
                state.TransitionDuration == 0f ? 0f : 1f / state.TransitionDuration);
        }

        // Called after destination clocks and layer outputs have been updated.
        // The original leaves next-state fields, duration and fixed-mode stored.
        public static bool TryComplete(NativeLayerTransitionState state, out uint eventCode)
        {
            eventCode = 0;
            if (!state.InTransition || !(state.TransitionElapsed >= 1f)) return false;
            state.PreviousNormalizedTime = state.CurrentNormalizedTime;
            state.PreviousFrameCount = state.CurrentFrameCount;
            state.PreviousState = state.CurrentState;
            state.PreviousDuration = state.SourceDuration;
            state.PreviousSpeedMultiplier = state.CurrentSpeedMultiplier;
            state.CurrentState = state.NextState;
            state.CurrentNormalizedTime = state.NextNormalizedTime;
            state.CurrentFrameCount = state.NextFrameCount;
            state.SourceDuration = state.DestinationDuration;
            state.CurrentSpeedMultiplier = state.NextSpeedMultiplier;
            state.InTransition = false;
            state.InterruptionActive = false;
            state.TransitionElapsed = 0f;
            state.TransitionIndex = -1;
            state.TransitionSourceState = -1;
            state.NeedsDestinationStart = false;
            state.DestinationOffset = 0f;
            state.EndTransitionMarker = true;
            eventCode = 0x1a;
            return true;
        }

        static float Clamp(float value) => Math.Min(1f, Math.Max(0f, value));
    }
}
