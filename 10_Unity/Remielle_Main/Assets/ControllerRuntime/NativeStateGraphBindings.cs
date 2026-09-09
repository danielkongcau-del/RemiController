using System;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Remielle.ControllerRuntime
{
    // d01080 selects a state and its per-motion-set tree, then binds the chosen
    // side using cc4180. Storage is preallocated here; native graph allocation,
    // port exchanges and parent dirty propagation are separate lifecycle work.
    public sealed class NativeStateGraphBindings
    {
        readonly NativeStateClipFields[] states;
        readonly JObject[][] trees;
        readonly Func<uint,string> resolveClip;
        readonly NativeMotionSetGraph[] motionSets;
        public IReadOnlyList<NativeStateClipBindings> Current { get; }
        public IReadOnlyList<NativeStateClipBindings> Next { get; }
        public IReadOnlyList<NativeMotionSetGraph> MotionSets { get; }
        public NativeGraphContext GraphContext { get; }=new NativeGraphContext();
        public NativeGraphTransitionContext TransitionContext { get; }=new NativeGraphTransitionContext();
        public int MotionSetCount => motionSets.Length;
        sealed class SideView:IReadOnlyList<NativeStateClipBindings>
        {
            readonly NativeMotionSetGraph[] sets;readonly bool current;
            public SideView(NativeMotionSetGraph[] sets,bool current){this.sets=sets;this.current=current;}
            public int Count=>sets.Length;
            public NativeStateClipBindings this[int index]=>current?sets[index].CurrentClips:sets[index].NextClips;
            public IEnumerator<NativeStateClipBindings> GetEnumerator(){for(int i=0;i<Count;i++)yield return this[i];}
            IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
        }

        public NativeStateGraphBindings(JObject machine,IReadOnlyList<int> capacities,Func<uint,string> resolveClip)
        {
            if(machine==null || capacities==null || resolveClip==null)throw new ArgumentNullException();
            if((int)machine["motionSetCount"]!=capacities.Count)throw new ArgumentException("Motion-set dimensions differ");
            this.resolveClip=resolveClip;
            var sourceStates=machine["states"].Cast<JObject>().ToArray();
            states=sourceStates.Select(s=>new NativeStateClipFields(s)).ToArray();
            trees=new JObject[states.Length][];
            for(int s=0;s<states.Length;s++)
            {
                var indices=(JArray)sourceStates[s]["blendTreeIndices"];var sourceTrees=(JArray)sourceStates[s]["trees"];
                if(indices.Count!=capacities.Count)throw new InvalidDataException("State motion-set mapping differs");
                trees[s]=new JObject[capacities.Count];
                for(int m=0;m<capacities.Count;m++)
                {
                    int index=(int)indices[m];if(index==-1)continue;
                    if(index<0 || index>=sourceTrees.Count)throw new InvalidDataException("Invalid source motion-set tree index");
                    if(sourceTrees[index].Type==JTokenType.Null)continue;
                    trees[s][m]=(JObject)sourceTrees[index].DeepClone();
                    int count=trees[s][m]["nodes"].Count(n=>!n["childIndices"].Any() && (uint)n["clipIndex"]!=uint.MaxValue);
                    if(count>capacities[m])throw new ArgumentException("Insufficient preallocated Clip slots");
                }
            }
            motionSets=capacities.Select((n,i)=>new NativeMotionSetGraph(i,n,GraphContext,new NativeStateClipBindings(n),new NativeStateClipBindings(n))).ToArray();
            MotionSets=Array.AsReadOnly(motionSets);Current=new SideView(motionSets,true);Next=new SideView(motionSets,false);
        }

        // Capacity is an independent storage policy sized from qualified source
        // leaves. This does not claim to execute the game's graph constructor.
        public static NativeStateGraphBindings ForSource(NativeControllerSource source,NativeMotionBank bank,string controller,int machine)
        {
            if(source==null || bank==null)throw new ArgumentNullException();
            var m=(JObject)source.GetController(controller)["machines"][machine];
            var capacities=new int[(int)m["motionSetCount"]];
            foreach(var s in m["states"])for(int set=0;set<capacities.Length;set++)
            {
                int ti=(int)s["blendTreeIndices"][set];if(ti<0)continue;
                var t=s["trees"][ti];if(t.Type==JTokenType.Null)continue;
                int count=t["nodes"].Count(n=>!n["childIndices"].Any() && (uint)n["clipIndex"]!=uint.MaxValue);
                capacities[set]=Math.Max(capacities[set],count);
            }
            return new NativeStateGraphBindings(m,capacities,i=>bank.ResolveSlot(controller,checked((int)i)));
        }

        public void Bind(NativeLayerTransitionState runtime,bool currentSide)
        {
            if(runtime==null)throw new ArgumentNullException(nameof(runtime));
            if(MotionSetCount==0)return;
            uint selected=currentSide?runtime.CurrentState:runtime.NextState;
            if(selected>=states.Length)throw new ArgumentOutOfRangeException(nameof(runtime),"Selected state is outside the source machine");
            var target=currentSide?Current:Next;
            if(target.Any(t=>t==null))throw new InvalidOperationException("Selected input is a cached pose; source scheduling must not bind it as a Clip mixer");
            for(int set=0;set<target.Count;set++)target[set].Apply(states[selected],trees[selected][set],resolveClip);
        }
        // cff807..cff844, after the original caller reads byte +0x85. Byte
        // +0x80 is distinct from the already named interruption flag +0x82.
        public bool ConsumeTransitionCompletion(NativeLayerTransitionState runtime,double timestamp)
        {
            if(runtime==null)throw new ArgumentNullException(nameof(runtime));
            if(!runtime.EndTransitionMarker)return false;
            foreach(var set in motionSets)set.FinishTransition(timestamp);
            runtime.EndTransitionMarker=false;runtime.GraphFlag80=0;return true;
        }
    }
}
