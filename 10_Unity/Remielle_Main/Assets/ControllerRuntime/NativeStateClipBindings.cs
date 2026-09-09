using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Remielle.ControllerRuntime
{
    // Named original StateConstant fields, checked against its binary reader.
    public sealed class NativeStateClipFields
    {
        public uint NameID { get; } public uint PathID { get; } public uint FullPathID { get; } public uint TagID { get; }
        public uint SpeedParamID { get; } public uint MirrorParamID { get; } public uint CycleOffsetParamID { get; } public uint TimeParamID { get; }
        public float Speed { get; } public float CycleOffset { get; } public bool Mirror { get; }
        public bool IKOnFeet { get; } public bool WriteDefaultValues { get; } public bool Loop { get; }
        public NativeStateClipFields(JObject state)
        {
            if(state==null)throw new ArgumentNullException(nameof(state));
            NameID=(uint)state["nameid"];PathID=(uint)state["path"];FullPathID=(uint)state["full"];TagID=(uint)state["tag"];
            SpeedParamID=(uint)state["sp"];MirrorParamID=(uint)state["mp"];CycleOffsetParamID=(uint)state["cp"];TimeParamID=(uint)state["extra"];
            CycleOffset=(float)state["cycle"];Mirror=(bool)state["mir"];
            Speed=(float)state["speed"];IKOnFeet=(bool)state["ikf"];WriteDefaultValues=(bool)state["wdv"];Loop=(bool)state["loop"];
        }
        // Native/binary callers can supply float32 values without a JSON numeric
        // conversion (which can erase the sign of zero on the target runtime).
        public NativeStateClipFields(uint nameID,uint pathID,uint fullPathID,uint tagID,
            uint speedParamID,uint mirrorParamID,uint cycleOffsetParamID,uint timeParamID,
            float speed,float cycleOffset,bool ikOnFeet,bool writeDefaultValues,bool loop,bool mirror)
        {
            NameID=nameID;PathID=pathID;FullPathID=fullPathID;TagID=tagID;
            SpeedParamID=speedParamID;MirrorParamID=mirrorParamID;CycleOffsetParamID=cycleOffsetParamID;TimeParamID=timeParamID;
            Speed=speed;CycleOffset=cycleOffset;IKOnFeet=ikOnFeet;WriteDefaultValues=writeDefaultValues;Loop=loop;Mirror=mirror;
        }
    }

    public sealed class NativeStateClipSlot
    {
        internal Action ResourcePrepared;
        public string AssetID { get; internal set; }
        public uint NameID { get; internal set; } public uint PathID { get; internal set; }
        public uint FullPathID { get; internal set; } public uint TagID { get; internal set; }
        public bool StateLoop { get; internal set; }
        public bool BindingsDirty { get; internal set; }
        public bool ResourceReady { get; internal set; }
        public bool IKOnFeet { get; internal set; }
        public bool WriteDefaultValues { get; internal set; }
        public float StateSpeed { get; internal set; }
        public bool SamplingLoop { get; internal set; }

        // cc3ed1..cc3f20, after successful resource preparation. The state Loop
        // field is independent: sampling uses the source MuscleClip or override.
        public void CompleteResourceBinding(bool sourceLoop, bool hasLoopOverride, bool loopOverride)
        {
            if(AssetID==null)throw new InvalidOperationException("No source clip to prepare");
            SamplingLoop=hasLoopOverride?loopOverride:sourceLoop;
            ResourceReady=true;BindingsDirty=false;ResourcePrepared?.Invoke();
        }
        public NativeClipPoseContext CreatePoseContext(NativePoseStream defaults,NativePoseStream overrideDefaults,
            NativePoseStream evaluationDefaults,bool evaluation18,bool prepareTransforms,bool prepareScalars,
            int translation,int rotation,int scale,byte[][] writeMask=null)
        {
            if(AssetID==null)throw new InvalidOperationException("No source clip to evaluate");
            return new NativeClipPoseContext(defaults,overrideDefaults,evaluationDefaults,evaluation18,WriteDefaultValues,
                prepareTransforms,prepareScalars,translation,rotation,scale,writeMask);
        }
        public NativeClipTimeResult GetSampleTime(NativeClipPlayback playback,NativeLeafTiming timing,float clipCycle,
            float evaluationOffset,float extrapolationSpeed,float sampleRate=NativeClipTime.DefaultSampleRate)
        {
            if(playback==null || !ResourceReady || BindingsDirty || AssetID==null)
                throw new InvalidOperationException("Clip resources must be prepared before sampling");
            return playback.GetSampleTime(timing,clipCycle,SamplingLoop,evaluationOffset,extrapolationSpeed,sampleRate);
        }
    }

    // The caller owns the typed Clip input slots and the separate last default
    // input. This ports cc4180 and cc81a0 policy writes, not graph allocation or
    // parent dirty propagation. Clip identity is the qualified bank AssetID.
    public sealed class NativeStateClipBindings
    {
        readonly NativeStateClipSlot[] slots;
        internal Action<int> SourceChanged;
        public IReadOnlyList<NativeStateClipSlot> Slots { get; }
        public int BoundLeafCount { get; private set; }
        public bool IKOnFeet { get; private set; }
        public bool WriteDefaultValues { get; private set; }
        public float StateSpeed { get; private set; }
        public NativeStateClipBindings(int capacity)
        {
            if(capacity<0)throw new ArgumentOutOfRangeException(nameof(capacity));
            slots=Enumerable.Range(0,capacity).Select(_=>new NativeStateClipSlot()).ToArray();
            Slots=Array.AsReadOnly(slots);
        }
        public void ApplySource(JObject state,JObject tree,string controller,NativeMotionBank bank)
        {
            if(tree==null || state==null)return;
            if(bank==null)throw new ArgumentNullException(nameof(bank));
            Apply(new NativeStateClipFields(state),tree,i=>bank.ResolveSlot(controller,checked((int)i)));
        }
        public void Apply(NativeStateClipFields state,JObject tree,Func<uint,string> resolveClip)
        {
            if(tree==null || state==null)return;
            if(resolveClip==null)throw new ArgumentNullException(nameof(resolveClip));
            // Native order scans all serialized nodes and takes only childless
            // entries whose ClipID is not -1. It does not sort by clip name/id.
            var leaves=tree["nodes"].Where(n=>!n["childIndices"].Any() && (uint)n["clipIndex"]!=uint.MaxValue).ToArray();
            if(leaves.Length>slots.Length)throw new ArgumentException("Insufficient preallocated Clip input slots");
            var sources=leaves.Select(n=>resolveClip((uint)n["clipIndex"])).ToArray();
            for(int i=0;i<slots.Length;i++)
            {
                var slot=slots[i];string next=i<sources.Length?sources[i]:null;
                if(!String.Equals(slot.AssetID,next,StringComparison.Ordinal))
                {slot.AssetID=next;slot.BindingsDirty=true;SourceChanged?.Invoke(i);}
                if(i>=sources.Length)continue;
                slot.NameID=state.NameID;slot.PathID=state.PathID;slot.FullPathID=state.FullPathID;slot.TagID=state.TagID;slot.StateLoop=state.Loop;
            }
            BoundLeafCount=sources.Length;IKOnFeet=state.IKOnFeet;WriteDefaultValues=state.WriteDefaultValues;StateSpeed=state.Speed;
        }
        // cc8390: clearing a Clip source preserves metadata and ready state,
        // and only marks the node/connected ancestors when its identity changes.
        internal void ClearSource(int index)
        {
            var slot=slots[index];if(slot.AssetID==null)return;
            slot.AssetID=null;slot.BindingsDirty=true;SourceChanged?.Invoke(index);
        }
        // The complete native dispatch updates these policy fields only for a
        // nonzero-weight Clip with a source. NaN is nonzero; return is >0 only.
        public bool DispatchPolicy(IReadOnlyList<float> inputWeights)
        {
            if(inputWeights==null || inputWeights.Count!=slots.Length)throw new ArgumentException("Clip input weight dimensions differ");
            bool anyPositive=false;
            for(int i=0;i<slots.Length;i++)
            {
                float weight=inputWeights[i];anyPositive|=weight>0f;
                if(weight==0f || slots[i].AssetID==null)continue;
                slots[i].IKOnFeet=IKOnFeet;slots[i].WriteDefaultValues=WriteDefaultValues;slots[i].StateSpeed=StateSpeed;
            }
            return anyPositive;
        }
    }
}
