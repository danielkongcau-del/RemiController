using System;
using System.Collections.Generic;
using System.Linq;

namespace Remielle.ControllerRuntime
{
    public readonly struct NativeMixerInputWeight
    {
        public readonly float Input, Start, Rate;
        public readonly double Timestamp, StartTime;
        public NativeMixerInputWeight(float input, float start, double timestamp, double startTime, float rate)
        { Input = input; Start = start; Timestamp = timestamp; StartTime = startTime; Rate = rate; }
    }

    public static class NativeTimedMixerWeights
    {
        // Complete original b0a6c0. Inputs are stored without clamping above one.
        // TimeManager fields 0x98 and 0x50 are supplied explicitly; this method
        // does not choose their producer or evaluate the stored history.
        public static bool Set(NativeMixerInputWeight[] inputs, int index, float value, float start, float rate,
            double timestamp, float timeManagerDelta)
        {
            if (inputs == null) throw new ArgumentNullException(nameof(inputs));
            if ((uint)index >= (uint)inputs.Length || value < 0f) return false;
            var old = inputs[index];
            double startTime = old.StartTime;
            float startValue = old.Start;
            if (timestamp > old.Timestamp)
            {
                startValue = start;
                double earliest = timestamp - (double)timeManagerDelta;
                startTime = old.Timestamp > earliest ? old.Timestamp : earliest;
            }
            inputs[index] = new NativeMixerInputWeight(value, startValue, timestamp, startTime, rate);
            return true;
        }
    }

    // These flags retain their original offsets until graph lifecycle producers
    // are qualified. They must not be inferred from a source state's name.
    public sealed class NativeStateInputWeights
    {
        readonly float[] current, next;
        public bool MixerFlag16c { get; }
        public NativeStateInputWeights(IReadOnlyList<float> current, IReadOnlyList<float> next, bool mixerFlag16c)
        { this.current = current?.ToArray(); this.next = next?.ToArray(); MixerFlag16c = mixerFlag16c; }
        internal bool CurrentPresent => Present(current);
        internal bool NextPresent => Present(next);
        static bool Present(float[] values)
        {
            if (values != null) foreach (float v in values) if (v != 0f) return true;
            return false;
        }
    }

    public sealed class NativeStateOutputWeights
    {
        readonly float[] outputs;
        readonly NativeMixerInputWeight[][] inputs;
        public int MotionSetCount => outputs.Length;

        public NativeStateOutputWeights(IReadOnlyList<NativeMixerInputWeight[]> initialInputs)
        {
            if (initialInputs == null) throw new ArgumentNullException(nameof(initialInputs));
            outputs = new float[initialInputs.Count]; inputs = new NativeMixerInputWeight[initialInputs.Count][];
            for (int i = 0; i < inputs.Length; i++)
            {
                if (initialInputs[i] == null || initialInputs[i].Length != 2)
                    throw new ArgumentException("Each motion set needs current and next mixer input records");
                inputs[i] = (NativeMixerInputWeight[])initialInputs[i].Clone();
            }
        }
        public float[] OutputWeights() => (float[])outputs.Clone();
        public NativeMixerInputWeight GetInput(int set, int input) => inputs[set][input];

        // Original empty-machine output loop, cff727..cff74a.
        public void ClearOutputs() => Array.Clear(outputs, 0, outputs.Length);

        // cff9fd..cffab5: all child input records count, including a default-pose
        // input if present. Both signed zeros are absent; NaN is present.
        public void BeginOutputs(IReadOnlyList<NativeStateInputWeights> sets, bool runtimeFlag80)
        {
            Check(sets);
            for (int i = 0; i < outputs.Length; i++) outputs[i] = runtimeFlag80 || sets[i].CurrentPresent ? 1f : 0f;
        }

        // d0025e..d0043b, after next-state clock evaluation. This is the output
        // and timed weight stage, not the complete tick or interruption routing.
        public void ApplyTransition(IReadOnlyList<NativeStateInputWeights> sets, bool runtimeFlag80,
            NativeTransitionProgressResult progress, double timestamp, float timeManagerDelta)
        {
            Check(sets);
            for (int i = 0; i < outputs.Length; i++)
            {
                float previous = progress.PreviousWeight, weight = progress.Weight, rate = progress.InverseDuration;
                bool current = !sets[i].MixerFlag16c && (runtimeFlag80 || sets[i].CurrentPresent);
                bool next = sets[i].NextPresent;
                if (current && !next)
                {
                    outputs[i] = (float)(1f - weight);
                    previous = weight = rate = 0f;
                }
                else if (!current)
                {
                    if (next) { outputs[i] = weight; previous = weight = 1f; rate = 0f; }
                    else outputs[i] = 0f;
                }
                float negativeRate = BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(rate) ^ int.MinValue);
                NativeTimedMixerWeights.Set(inputs[i], 0, (float)(1f - weight), (float)(1f - previous), negativeRate, timestamp, timeManagerDelta);
                NativeTimedMixerWeights.Set(inputs[i], 1, weight, previous, rate, timestamp, timeManagerDelta);
            }
        }
        void Check(IReadOnlyList<NativeStateInputWeights> sets)
        {
            if (sets == null || sets.Count != MotionSetCount || sets.Any(s => s == null))
                throw new ArgumentException("State motion-set input dimensions differ");
        }
    }
}
