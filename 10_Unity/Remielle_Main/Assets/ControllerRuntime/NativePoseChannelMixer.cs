using System;
using System.Linq;

namespace Remielle.ControllerRuntime
{
    // An explicit pose-channel stream. The caller owns its binding layout;
    // optional native humanoid/root structures are not represented by this type.
    public sealed class NativePoseStream
    {
        internal NativePoseData Data;
        internal readonly byte[][] Active;
        public byte StreamFlag { get; set; }
        public byte ReferenceFlag0 { get; set; }
        public byte ReferenceFlag1 { get; set; }
        public NativePoseBindingLayout BindingLayout { get; }

        public NativePoseStream(NativePoseData pose, byte[][] mask, byte streamFlag = 0,
            byte referenceFlag0 = 0, byte referenceFlag1 = 0, NativePoseBindingLayout bindingLayout = null)
        {
            Data = pose?.Clone() ?? throw new ArgumentNullException(nameof(pose));
            if (bindingLayout != null && !bindingLayout.Counts().SequenceEqual(Data.Counts))
                throw new ArgumentException("Declared binding layout dimensions differ");
            BindingLayout = bindingLayout;
            CheckMask(mask);
            Active = mask.Select(x => (byte[])x.Clone()).ToArray();
            StreamFlag = streamFlag; ReferenceFlag0 = referenceFlag0; ReferenceFlag1 = referenceFlag1;
        }

        public NativePoseData Pose() => Data.Clone();
        public byte[][] Mask() => Active.Select(x => (byte[])x.Clone()).ToArray();

        // Source producer contract: replace the complete mask, and write active
        // values only. This does not normalize, fill defaults, or discard tracks.
        public void WriteMasked(NativePoseData source, byte[][] mask, byte streamFlag = 0,
            byte referenceFlag0 = 0, byte referenceFlag1 = 0)
        {
            CheckPose(source); CheckMask(mask);
            for (int g = 0; g < 5; g++)
            {
                Array.Copy(mask[g], Active[g], mask[g].Length);
                int width = g < 3 ? 4 : 1;
                for (int i = 0; i < Data.Counts[g]; i++)
                {
                    if (mask[g][i] == 0) continue;
                    if (g == 4) Data.Discrete[i] = source.Discrete[i];
                    else Array.Copy(source.Floats[g], i * width, Data.Floats[g], i * width, width);
                }
            }
            StreamFlag = streamFlag; ReferenceFlag0 = referenceFlag0; ReferenceFlag1 = referenceFlag1;
        }

        internal void ClearMask()
        {
            foreach (var channel in Active) Array.Clear(channel, 0, channel.Length);
        }

        internal void SetAccumulation(NativePoseAccumulator accumulation)
        {
            Data = accumulation.Pose();
            var mask = accumulation.Mask();
            for (int g = 0; g < 5; g++) Array.Copy(mask[g], Active[g], mask[g].Length);
        }

        internal void CheckPose(NativePoseData source)
        {
            if (source == null || !Data.Counts.SequenceEqual(source.Counts))
                throw new ArgumentException("Pose channel layouts have different dimensions");
        }

        internal void CheckMask(byte[][] mask)
        {
            if (mask == null || mask.Length != 5) throw new ArgumentException("Pose mask dimensions differ");
            for (int g = 0; g < 5; g++)
                if (mask[g] == null || mask[g].Length != Data.Counts[g])
                    throw new ArgumentException("Pose mask channel dimensions differ");
        }
    }

    // cc5fc0 pose-channel path with context+82=false and null additional pose
    // structures. Input selection uses NativeMixerEvaluation. Defaults, source
    // bindings and mask producers remain explicit rather than guessed here.
    public sealed class NativePoseChannelMixer
    {
        readonly NativePoseStream scratch;
        readonly byte[][] priorMask;
        float[][] weights;
        bool evaluating;

        public NativePoseChannelMixer(NativePoseStream initialScratch, byte[][] initialPriorMask,
            float[][] initialWeights)
        {
            if (initialScratch == null) throw new ArgumentNullException(nameof(initialScratch));
            scratch = new NativePoseStream(initialScratch.Pose(), initialScratch.Mask(), initialScratch.StreamFlag, bindingLayout: initialScratch.BindingLayout);
            scratch.CheckMask(initialPriorMask);
            priorMask = initialPriorMask.Select(x => (byte[])x.Clone()).ToArray();
            // Reuse the mathematical layer's independent wire-order validation.
            weights = new NativePoseAccumulator(scratch.Data, scratch.Active, initialWeights).Weights();
        }

        public NativePoseStream Scratch() => new NativePoseStream(scratch.Data, scratch.Active, scratch.StreamFlag, bindingLayout: scratch.BindingLayout);
        public byte[][] PriorMask() => priorMask.Select(x => (byte[])x.Clone()).ToArray();
        public float[][] Weights() => weights.Select(x => (float[])x.Clone()).ToArray();

        public void Evaluate(NativeMixerEvaluationResult selection, NativePoseStream output,
            NativePoseData defaults, NativePoseData overrideDefaults, bool preserveScalarMask,
            bool skipDefaults, Action<int, NativePoseStream> evaluateChild)
        {
            if (selection == null || output == null || evaluateChild == null) throw new ArgumentNullException();
            if (!ReferenceEquals(output.BindingLayout, scratch.BindingLayout))
                throw new ArgumentException("Mixer streams belong to different binding layouts");
            output.CheckPose(scratch.Data);
            if (defaults != null) output.CheckPose(defaults);
            if (overrideDefaults != null) output.CheckPose(overrideDefaults);
            if (evaluating) throw new InvalidOperationException("A pose mixer cannot recursively evaluate itself");
            evaluating = true;
            try
            {
                if (selection.Dispatch == NativeMixerDispatch.Empty)
                {
                    // cc61b0/cef150: stored channel values and reference flags
                    // remain; the channel mask and stream flag are cleared.
                    output.ClearMask(); output.StreamFlag = 0;
                    return;
                }
                if (selection.Dispatch == NativeMixerDispatch.Single)
                {
                    // The original direct child dispatch bypasses all mix math.
                    evaluateChild(selection.Contributions[0].Node, output);
                    return;
                }

                // cf7e50 copies only scalar masks from output into saved state.
                if (preserveScalarMask) Array.Copy(output.Active[3], priorMask[3], priorMask[3].Length);
                output.ClearMask();
                foreach (var channel in weights) Array.Clear(channel, 0, channel.Length);
                scratch.ClearMask();
                var accumulation = new NativePoseAccumulator(output.Data, output.Active, weights);
                foreach (var contribution in selection.Contributions)
                {
                    scratch.ReferenceFlag0 = 0; scratch.ReferenceFlag1 = 0;
                    evaluateChild(contribution.Node, scratch);
                    accumulation.Accumulate(scratch.Data, scratch.Active, contribution.Weight);
                    output.SetAccumulation(accumulation);
                    weights = accumulation.Weights();
                }
                accumulation.Finish(skipDefaults ? null : overrideDefaults ?? defaults);
                output.SetAccumulation(accumulation);
                // cf80c0 unions only the scalar mask, after default contribution.
                if (preserveScalarMask)
                    for (int i = 0; i < priorMask[3].Length; i++)
                        output.Active[3][i] = (byte)(priorMask[3][i] != 0 || output.Active[3][i] != 0 ? 1 : 0);
            }
            finally { evaluating = false; }
        }
    }
}
