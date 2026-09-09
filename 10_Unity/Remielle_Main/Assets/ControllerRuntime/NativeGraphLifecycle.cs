using System;
using System.Collections.Generic;
using System.Linq;

namespace Remielle.ControllerRuntime
{
    // Explicit independent storage for the original graph fields used by
    // fixed-capacity rewiring. Engine allocation and graph jobs are separate.
    public sealed class NativeGraphContext { public byte FlagsA0; }

    public sealed class NativeGraphInput
    {
        public NativeGraphNode Node { get; internal set; }
        public int OutputPort { get; internal set; }
        public NativeMixerInputWeight Weight { get; set; }
    }

    public sealed class NativeGraphNode
    {
        readonly NativeGraphInput[] inputs;
        readonly NativeGraphNode[] outputs;
        byte flag100;
        public string Name { get; }
        public NativeGraphContext Context { get; }
        public uint Type { get; set; }
        public uint Flags38 { get; set; }
        public byte Flag100 { get=>Pose==null?flag100:Pose.NodeFlag100; set {if(Pose==null)flag100=value;else Pose.NodeFlag100=value;} }
        public byte Flag102 { get; set; }
        public byte Flag103 { get; set; }
        public NativeStateClipBindings Clips { get; }
        public NativeDefaultPoseInput Pose { get; }
        public IReadOnlyList<NativeGraphInput> Inputs { get; }
        public IReadOnlyList<NativeGraphNode> Outputs { get; }

        internal NativeGraphNode(string name,NativeGraphContext context,int inputCount,int outputCount,
            NativeStateClipBindings clips=null,NativeDefaultPoseInput pose=null)
        {
            Name=name;Context=context;Clips=clips;Pose=pose;
            inputs=Enumerable.Range(0,inputCount).Select(_=>new NativeGraphInput()).ToArray();outputs=new NativeGraphNode[outputCount];
            Inputs=Array.AsReadOnly(inputs);Outputs=Array.AsReadOnly(outputs);
            if(Pose!=null)
            {
                Pose.AttachGraphWeight(()=>ParentInput().Weight,w=>ParentInput().Weight=w);
                Pose.ResourceDirtyChanged=dirty=>Flag102=(byte)(dirty?1:0);
            }
        }
        NativeGraphInput ParentInput()
        {
            if(outputs.Length!=1 || outputs[0]==null)throw new InvalidOperationException("Pose input is detached");
            return outputs[0].inputs.Single(i=>ReferenceEquals(i.Node,this));
        }
        internal void InitializeInput(int input,NativeGraphNode child)
        {inputs[input].Node=child;inputs[input].OutputPort=0;child.outputs[0]=this;}
        void ConnectionsChanged(){Flags38|=0x100;Context.FlagsA0|=0x38;}
        internal void Disconnect(int input)
        {
            var link=inputs[input];var child=link.Node;int port=link.OutputPort;var old=link.Weight;
            // b08490 replaces only Input, preserving the rest of its history.
            link.Weight=new NativeMixerInputWeight(1f,old.Start,old.Timestamp,old.StartTime,old.Rate);
            link.Node=null;link.OutputPort=-1;ConnectionsChanged();
            if(child!=null && port!=-1){child.outputs[port]=null;child.ConnectionsChanged();child.Flag100=1;}
        }
        internal void Connect(int input,NativeGraphNode child)
        {
            if(child.outputs.Length!=1 || child.outputs[0]!=null)throw new InvalidOperationException("Source output must be free before rewiring");
            child.outputs[0]=this;child.ConnectionsChanged();inputs[input].Node=child;inputs[input].OutputPort=0;
            ConnectionsChanged();Flag100=1;
        }
        internal void Immediate(int input,float value,double timestamp)
        {inputs[input].Weight=new NativeMixerInputWeight(value,value,timestamp,0d,0f);}
        internal void SourceChanged()
        {
            Flag102=1;NativeGraphNode candidate=Type==0?this:null,node=this;
            var visited=new HashSet<NativeGraphNode>{this};
            while(node.outputs.Length==1 && node.outputs[0]!=null)
            {
                node=node.outputs[0];if(!visited.Add(node))throw new InvalidOperationException("Graph parent cycle");
                if(node.Type==0)candidate=node;
            }
            if(candidate!=null && !ReferenceEquals(candidate,this))candidate.Flag103=1;
        }
    }

    // Three physical inputs: two motion mixers and a cached-pose node. The
    // cache moves between ports 2 and 0; it is not a third animation state.
    public sealed class NativeMotionSetGraph
    {
        readonly NativeGraphNode[] nodes;
        public NativeGraphNode Root { get; }
        public NativeGraphNode FirstMixer { get; }
        public NativeGraphNode SecondMixer { get; }
        public NativeGraphNode CacheNode { get; }
        public IReadOnlyList<NativeGraphNode> Nodes { get; }
        public int CachePort { get; private set; }=2;
        public byte Flag16c { get; set; }
        public NativeStateClipBindings CurrentClips=>Root.Inputs[0].Node.Clips;
        public NativeStateClipBindings NextClips=>Root.Inputs[1].Node.Clips;

        internal NativeMotionSetGraph(int index,int capacity,NativeGraphContext context,NativeStateClipBindings first,NativeStateClipBindings second)
        {
            var list=new List<NativeGraphNode>();string prefix=index+":";
            Root=new NativeGraphNode(prefix+"outer",context,3,0);list.Add(Root);
            CacheNode=new NativeGraphNode(prefix+"cache",context,0,1,pose:new NativeDefaultPoseInput(default));list.Add(CacheNode);
            var mixers=new NativeGraphNode[2];
            for(int side=0;side<2;side++)
            {
                var clips=side==0?first:second;var mixer=new NativeGraphNode(prefix+"mixer"+side,context,capacity+1,1,clips);mixers[side]=mixer;list.Add(mixer);
                var leaves=new NativeGraphNode[capacity];
                for(int input=0;input<=capacity;input++)
                {
                    var child=new NativeGraphNode(prefix+"mixer"+side+":input"+input,context,0,1,
                        pose:input==capacity?new NativeDefaultPoseInput(default):null);
                    list.Add(child);mixer.InitializeInput(input,child);
                    if(input<capacity){leaves[input]=child;clips.Slots[input].ResourcePrepared=()=>child.Flag102=0;}
                }
                clips.SourceChanged=i=>leaves[i].SourceChanged();Root.InitializeInput(side,mixer);
            }
            FirstMixer=mixers[0];SecondMixer=mixers[1];Root.InitializeInput(2,CacheNode);nodes=list.ToArray();Nodes=Array.AsReadOnly(nodes);
        }
        // Complete cd4c80 including both cache-position branches, Clip release,
        // connected dirty propagation and immediate TimeManager weight writes.
        public void FinishTransition(double timestamp)
        {
            var oldCurrent=Root.Inputs[0].Node;var oldNext=Root.Inputs[1].Node;
            if(CachePort==0)
            {
                var oldThird=Root.Inputs[2].Node;
                Root.Disconnect(0);Root.Flag100=0;Root.Disconnect(1);Root.Flag100=0;Root.Disconnect(2);Root.Flag100=0;
                Root.Connect(0,oldNext);Root.Flag100=0;oldNext.Flag100=0;
                Root.Connect(1,oldThird);Root.Flag100=0;oldThird.Flag100=0;
                Root.Connect(2,oldCurrent);Root.Flag100=0;oldCurrent.Flag100=0;
                CachePort=2;CacheNode.Pose.ApplyFootIK=false;Flag16c=0;
            }
            else
            {
                Root.Disconnect(0);Root.Flag100=0;Root.Disconnect(1);Root.Flag100=0;
                Root.Connect(0,oldNext);Root.Flag100=0;oldNext.Flag100=0;
                Root.Connect(1,oldCurrent);Root.Flag100=0;oldCurrent.Flag100=0;
                for(int i=0;i<oldCurrent.Clips.Slots.Count;i++)
                {oldCurrent.Immediate(i,0f,timestamp);oldCurrent.Clips.ClearSource(i);}
            }
            Root.Immediate(1,0f,timestamp);Root.Immediate(2,0f,timestamp);
        }
        // cd9640 with the explicitly qualified free-cache topology. Re-entry
        // with cache already current depends on the upper scheduler and is not
        // silently accepted as an ordinary valid connection operation here.
        public void SelectCachedCurrentPose(bool footIK,bool flag16c,double timestamp)
        {
            if(CachePort!=2)throw new InvalidOperationException("Cached pose is already current; the upper transition scheduler must resolve re-entry");
            var oldCurrent=Root.Inputs[0].Node;var cache=Root.Inputs[CachePort].Node;
            Flag16c=(byte)(flag16c?1:0);cache.Pose.ApplyFootIK=footIK;
            Root.Disconnect(0);Root.Flag100=0;Root.Disconnect(CachePort);Root.Flag100=0;
            Root.Connect(0,cache);Root.Flag100=0;cache.Flag100=0;
            Root.Connect(2,oldCurrent);Root.Flag100=0;oldCurrent.Flag100=0;
            Root.Immediate(2,0f,timestamp);CachePort=0;
        }
    }
}
