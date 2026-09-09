using System;
using System.Linq;

namespace Remielle.ControllerRuntime
{
    // Explicit original callback inputs. Flag offsets identify proven consumers;
    // graph/Avatar producers must be qualified before assigning higher-level names.
    public sealed class NativeClipPoseContext
    {
        public NativePoseStream SourceDefaults { get; }
        public NativePoseStream OverrideDefaults { get; }
        public NativePoseStream EvaluationDefaults { get; }
        public bool Evaluation18 { get; }
        public bool Clip194 { get; }
        public bool PrepareTransforms { get; }
        public bool PrepareScalars { get; }
        readonly int[] selected;
        internal readonly byte[][] WriteMask;
        public int[] SelectedTransformChannels() => (int[])selected.Clone();
        public NativeClipPoseContext(NativePoseStream sourceDefaults, NativePoseStream overrideDefaults,
            NativePoseStream evaluationDefaults, bool evaluation18, bool clip194,
            bool prepareTransforms, bool prepareScalars, int translation, int rotation, int scale,
            byte[][] writeMask = null)
        {
            SourceDefaults = sourceDefaults ?? throw new ArgumentNullException(nameof(sourceDefaults));
            OverrideDefaults=overrideDefaults; EvaluationDefaults=evaluationDefaults;
            foreach(var other in new[]{overrideDefaults,evaluationDefaults})
                if(other!=null && (!ReferenceEquals(other.BindingLayout,sourceDefaults.BindingLayout) ||
                    !other.Data.Counts.SequenceEqual(sourceDefaults.Data.Counts)))
                    throw new ArgumentException("Clip defaults must share their declared binding layout");
            Evaluation18=evaluation18; Clip194=clip194;
            PrepareTransforms=prepareTransforms; PrepareScalars=prepareScalars;
            selected=new[]{translation,rotation,scale};
            for(int g=0;g<3;g++) if(selected[g]<-1 || selected[g]>=sourceDefaults.Data.Counts[g])
                throw new ArgumentException("Selected transform channel is outside its group");
            if(writeMask!=null){sourceDefaults.CheckMask(writeMask);WriteMask=writeMask.Select(r=>(byte[])r.Clone()).ToArray();}
        }
        internal NativePoseStream Defaults => !Evaluation18 && !Clip194 && EvaluationDefaults!=null ?
            EvaluationDefaults : OverrideDefaults ?? SourceDefaults;
        internal int Selected(int group)=>selected[group];
        internal void CheckOutput(NativePoseStream output)
        {
            if(output==null || !ReferenceEquals(output.BindingLayout,SourceDefaults.BindingLayout))
                throw new ArgumentException("Clip context and output use different binding layouts");
            output.CheckPose(SourceDefaults.Data);
        }
    }

    // Original ordinary-channel stages inside cc6730 and cc63c0. These methods
    // expose their boundaries; they do not claim the intervening root corrections.
    public static class NativeClipPoseChannels
    {
        public static void Prepare(NativePoseSampleMap map, float[] samples, NativeClipPoseContext context, NativePoseStream output)
        {
            if(map==null || context==null)throw new ArgumentNullException();
            context.CheckOutput(output);map.Check(samples,context.Defaults,output);
            PrepareSamples(samples,map.SourceOffsets,context,output);
        }
        public static void Gather(NativePoseSampleMap map, float[] samples, NativeClipPoseContext context, NativePoseStream output)
        {
            if(map==null || context==null)throw new ArgumentNullException();
            context.CheckOutput(output);
            map.Gather(samples,context.Defaults,output,!context.Clip194,context.WriteMask);
        }
        // Public numeric stage also supports independently verified wire fixtures.
        public static void PrepareSamples(float[] samples, int[][] offsets, NativeClipPoseContext context, NativePoseStream output)
        {
            if(context==null)throw new ArgumentNullException(nameof(context));
            context.CheckOutput(output);
            var defaults=context.Defaults.Data;
            NativeSamplePoseGather.Validate(samples,offsets,defaults,output,context.WriteMask);
            if(!context.PrepareTransforms && !context.PrepareScalars)return;
            // cc69dc..cc6ab2: either flag enters this stage. T/Q/S writes are
            // selected individually and ignore the scalar/ordinary write mask.
            output.ClearMask();
            for(int g=0;g<3;g++)
            {
                int i=context.Selected(g);
                if(i>=0)NativeSamplePoseGather.WriteChannel(samples,offsets,defaults,output,!context.Clip194,g,i);
            }
            if(context.PrepareScalars)for(int i=0;i<output.Data.Counts[3];i++)
                if(context.WriteMask==null || context.WriteMask[3][i]!=0)
                    NativeSamplePoseGather.WriteChannel(samples,offsets,defaults,output,!context.Clip194,3,i);
        }
        public static void GatherSamples(float[] samples, int[][] offsets, NativeClipPoseContext context, NativePoseStream output)
        {
            if(context==null)throw new ArgumentNullException(nameof(context));
            context.CheckOutput(output);
            NativeSamplePoseGather.Gather(samples,offsets,context.Defaults.Data,output,!context.Clip194,context.WriteMask);
        }
        // Complete no-MuscleClip branch of cc63c0, including original helpers.
        public static void WriteEmpty(NativeClipPoseContext context, NativePoseStream output)
        {
            if(context==null)throw new ArgumentNullException(nameof(context));
            context.CheckOutput(output);
            if(context.Clip194){output.ClearMask();return;}
            if(context.EvaluationDefaults==null || context.WriteMask==null)
                throw new ArgumentException("Original empty-clip defaults path needs evaluation defaults and scalar mask");
            var mask=output.Data.Counts.Select(n=>Enumerable.Repeat((byte)1,n).ToArray()).ToArray();
            for(int g=0;g<3;g++)if(context.Selected(g)>=0)mask[g][context.Selected(g)]=0;
            Array.Copy(context.WriteMask[3],mask[3],mask[3].Length);
            output.WriteMasked(context.EvaluationDefaults.Data,mask,output.StreamFlag,output.ReferenceFlag0,output.ReferenceFlag1);
        }
    }
}
