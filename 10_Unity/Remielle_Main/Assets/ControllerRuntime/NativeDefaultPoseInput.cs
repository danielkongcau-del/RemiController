using System;
using System.Collections.Generic;

namespace Remielle.ControllerRuntime
{
    // The independent last pose input of a state motion mixer. Allocation and
    // the caller's evaluation order remain separate from this node's behavior.
    public sealed class NativeDefaultPoseInput
    {
        public const int RootStorageSize=0x160;
        NativePoseStream cached;
        byte[] rootState;
        NativePoseResources resources;
        internal Action<bool> ResourceDirtyChanged;
        public bool ResourceReady { get; private set; }
        public bool BindingsDirty { get; private set; }
        // Original generic graph byte. Its producer belongs to the outer graph;
        // this leaf clears it after the previous-pose callback even without a read.
        public byte NodeFlag100 { get; set; }
        public NativePoseResources PreparedResources=>resources;
        NativeMixerInputWeight weight;
        Func<NativeMixerInputWeight> graphWeightReader;
        Action<NativeMixerInputWeight> graphWeightWriter;
        public NativeMixerInputWeight Weight
        {
            get=>graphWeightReader==null?weight:graphWeightReader();
            private set {if(graphWeightWriter==null)weight=value;else graphWeightWriter(value);}
        }
        internal void AttachGraphWeight(Func<NativeMixerInputWeight> reader,Action<NativeMixerInputWeight> writer)
        {graphWeightReader=reader;graphWeightWriter=writer;}
        public bool MustReadPreviousPose { get; private set; }
        // d000b5 and the default-input policy both schedule this same read.
        public void RequestPreviousPose(){MustReadPreviousPose=true;}
        public bool ReadDefaultPose { get; set; }
        public bool ApplyFootIK { get; set; }
        public NativePoseStream CachedPose { get {EnsureReady();return new NativePoseStream(cached.Pose(),cached.Mask(),cached.StreamFlag,
            cached.ReferenceFlag0,cached.ReferenceFlag1,cached.BindingLayout);} }
        public byte[] CachedRootState { get {EnsureReady();return (byte[])rootState.Clone();} }

        public NativeDefaultPoseInput(NativeMixerInputWeight weight){Weight=weight;}

        public NativeDefaultPoseInput(NativePoseStream initial,byte[] initialRoot,NativeMixerInputWeight weight)
        {
            if(initial==null)throw new ArgumentNullException(nameof(initial));
            CheckRoot(initialRoot);
            cached=new NativePoseStream(initial.Pose(),initial.Mask(),initial.StreamFlag,
                initial.ReferenceFlag0,initial.ReferenceFlag1,initial.BindingLayout);
            rootState=(byte[])initialRoot.Clone();Weight=weight;ResourceReady=true;
        }

        // cd3500: ready resources are retained even if a later context changes.
        // Managed allocation replaces the engine's allocator; source layout and
        // the two additional-pose flags are supplied by the graph owner.
        public bool PrepareResources(NativePoseBindingLayout layout,bool context82=false,bool context88=false)
        {
            bool created=!ResourceReady;
            if(created)
            {
                resources=NativePoseResources.ForLayout(layout,context82,context88);
                cached=resources.Stream;rootState=resources.Root;
            }
            ResourceReady=true;BindingsDirty=false;ResourceDirtyChanged?.Invoke(false);return created;
        }
        public void MarkResourcesDirty(){BindingsDirty=true;ResourceDirtyChanged?.Invoke(true);}

        // cff220..cff23d -> cc8480 -> b0a7c0. Disabling the input does not
        // cancel a pending previous-pose read. Timestamp is original TimeManager
        // time supplied by the graph owner, not animation normalized time.
        public bool ApplyStatePolicy(bool anyPositiveClipWeight,bool stateWriteDefaults,double timestamp)
        {
            bool enabled=!anyPositiveClipWeight&&!stateWriteDefaults;
            float value=enabled?1f:0f;
            Weight=new NativeMixerInputWeight(value,value,timestamp,0d,0f);
            if(enabled)MustReadPreviousPose=true;
            return enabled;
        }

        public bool DispatchState(NativeStateClipBindings clips,IReadOnlyList<float> weights,
            NativeStateClipFields state,double timestamp)
        {
            if(clips==null||state==null)throw new ArgumentNullException();
            return ApplyStatePolicy(clips.DispatchPolicy(weights),state.WriteDefaultValues,timestamp);
        }

        // Full cd68c0 leaf path, context+82=false. Previous input data is copied
        // even for inactive channels; stream/reference flags are not copied.
        // The auxiliary 0x160-byte root structure is retained by original ranges,
        // never applied as a second Transform/root-motion displacement here.
        public bool ReadPreviousPose(NativePoseStream previous,byte[] previousRoot,bool prepareTransforms,
            int translation,int rotation,int scale)
        {
            if(!MustReadPreviousPose){NodeFlag100=0;return false;}
            CheckStream(previous);CheckRoot(previousRoot);
            var selected=CheckSelected(translation,rotation,scale);
            cached.Data=previous.Data.Clone();
            for(int g=0;g<5;g++)Array.Copy(previous.Active[g],cached.Active[g],cached.Active[g].Length);
            Array.Copy(previousRoot,0x10,rootState,0x10,0x24);
            Array.Copy(previousRoot,0x120,rootState,0x120,0x30);
            if(prepareTransforms)for(int g=0;g<3;g++)if(selected[g]>=0)cached.Active[g][selected[g]]=1;
            MustReadPreviousPose=false;NodeFlag100=0;
            return true;
        }

        // Full cd6b80 ordinary pose callback with context+82=false. Existing
        // active output channels also receive this node's values after mask OR.
        public void Gather(NativePoseStream contextDefaults,NativePoseStream overrideDefaults,
            NativePoseStream output,bool prepareTransforms,int translation,int rotation,int scale)
        {
            CheckStream(output);
            var selected=CheckSelected(translation,rotation,scale);
            NativePoseStream source=cached;
            if(ReadDefaultPose)
            {
                source=overrideDefaults??contextDefaults;
                CheckStream(source);
            }
            for(int g=0;g<5;g++)for(int i=0;i<cached.Active[g].Length;i++)
                output.Active[g][i]=(byte)((output.Active[g][i]!=0||cached.Active[g][i]!=0)?1:0);
            if(prepareTransforms)for(int g=0;g<3;g++)if(selected[g]>=0)output.Active[g][selected[g]]=0;
            for(int g=0;g<5;g++)for(int i=0;i<cached.Active[g].Length;i++)
            {
                if(output.Active[g][i]==0)continue;
                if(g==4)output.Data.Discrete[i]=source.Data.Discrete[i];
                else Array.Copy(source.Data.Floats[g],i*(g<3?4:1),output.Data.Floats[g],i*(g<3?4:1),g<3?4:1);
            }
        }

        void CheckStream(NativePoseStream stream)
        {
            EnsureReady();
            if(stream==null)throw new ArgumentNullException(nameof(stream));
            cached.CheckPose(stream.Data);
            if(!ReferenceEquals(cached.BindingLayout,stream.BindingLayout))throw new ArgumentException("Default pose binding identity differs");
        }
        void EnsureReady(){if(!ResourceReady)throw new InvalidOperationException("Default pose resources are not prepared");}
        int[] CheckSelected(int translation,int rotation,int scale)
        {
            var values=new[]{translation,rotation,scale};
            for(int g=0;g<3;g++)if(values[g]<-1||values[g]>=cached.Data.Counts[g])throw new ArgumentOutOfRangeException("Selected pose channel");
            return values;
        }
        static void CheckRoot(byte[] value)
        {
            if(value==null||value.Length!=RootStorageSize)throw new ArgumentException("Expected original root structure storage extent 0x160");
        }
    }
}
