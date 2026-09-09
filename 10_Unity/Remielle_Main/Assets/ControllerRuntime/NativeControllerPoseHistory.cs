using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Remielle.ControllerRuntime
{
    public sealed class NativeControllerLayerGraph
    {
        public int LayerIndex { get; }
        public int MachineIndex { get; }
        public int MotionSetIndex { get; }
        public NativeMotionSetGraph MotionGraph { get; }
        internal NativeControllerLayerGraph(int layer,int machine,int set,NativeMotionSetGraph graph)
        {LayerIndex=layer;MachineIndex=machine;MotionSetIndex=set;MotionGraph=graph;}
    }

    // Own each state machine once, and refer to its motion graphs in source
    // layer order. Synchronized layers share the same machine, not copied state.
    public sealed class NativeControllerGraphBindings
    {
        public IReadOnlyList<NativeStateGraphBindings> Machines { get; }
        public IReadOnlyList<NativeControllerLayerGraph> Layers { get; }
        public string Controller { get; }
        public NativeControllerGraphBindings(NativeControllerSource source,NativeMotionBank bank,string controller)
        {
            if(source==null||bank==null)throw new ArgumentNullException();
            Controller=controller;var c=source.GetController(controller);
            var machines=((JArray)c["machines"]).Select((m,i)=>NativeStateGraphBindings.ForSource(source,bank,controller,i)).ToArray();
            Machines=Array.AsReadOnly(machines);var seen=new HashSet<(int,int)>();var layers=new List<NativeControllerLayerGraph>();
            foreach(var layer in c["layers"])
            {
                int machine=(int)layer["smIdx"],set=(int)layer["smms"];
                if((uint)machine>=(uint)machines.Length || (uint)set>=(uint)machines[machine].MotionSetCount || !seen.Add((machine,set)))
                    throw new InvalidDataException("Invalid or duplicate source layer graph mapping");
                layers.Add(new NativeControllerLayerGraph(layers.Count,machine,set,machines[machine].MotionSets[set]));
            }
            if(seen.Count!=machines.Sum(m=>m.MotionSetCount))throw new InvalidDataException("Source layer mapping omits a motion graph");
            Layers=layers.AsReadOnly();
        }
        public NativeControllerPoseHistory CreatePoseHistory(IReadOnlyList<NativePoseStream> previous,IReadOnlyList<byte[]> roots)
        {
            if(previous==null||roots==null||previous.Count!=Layers.Count||roots.Count!=Layers.Count)
                throw new ArgumentException("Previous-pose arrays must follow every source layer");
            return new NativeControllerPoseHistory(Layers.Select((l,i)=>new NativeControllerPoseInput(l.MotionGraph.Root,previous[i],roots[i])).ToArray());
        }
    }

    // References owned by the aggregate graph. Replacing/publishing a completed
    // frame is explicit; it is not inferred from the current Clip sample.
    public sealed class NativeControllerPoseInput
    {
        public NativeGraphNode Node { get; set; }
        public NativePoseStream PreviousPose { get; set; }
        public byte[] PreviousRoot { get; set; }
        public NativeControllerPoseInput(NativeGraphNode node,NativePoseStream previousPose,byte[] previousRoot)
        {Node=node;PreviousPose=previousPose;PreviousRoot=previousRoot;}
    }

    public sealed class NativeControllerPoseHistory
    {
        public IReadOnlyList<NativeControllerPoseInput> Inputs { get; }
        // b11a10 on owner+110 checks owner+138 for a live controller-state
        // allocation. Its original producer and owner graph construction are
        // separate from this consumer; availability is an explicit input here.
        public bool ControllerStateAvailable { get; set; }
        public ulong OwnerInputCount { get; set; }
        public byte OwnerFlag101 { get; set; }
        public NativeControllerPoseHistory(IReadOnlyList<NativeControllerPoseInput> inputs)
        {
            if(inputs==null||inputs.Any(i=>i==null))throw new ArgumentNullException(nameof(inputs));
            Inputs=Array.AsReadOnly(inputs.ToArray());
        }
        // Complete cce7d0, including the real owner gate, null layer inputs and
        // post-traversal flag clearing. Neither layer weights nor the owner's
        // caller-provided pose argument select the previous-pose array entries.
        public int ReadPreviousPoses(bool prepareTransforms,int translation,int rotation,int scale,
            Action<int,NativeGraphNode> visit=null)
        {
            if(!ControllerStateAvailable || OwnerInputCount==0)return 0;
            int captured=0;
            for(int i=0;i<Inputs.Count;i++)
            {
                var input=Inputs[i];if(input.Node==null)continue;int layer=i;
                captured+=NativeGraphPoseSchedule.ReadPreviousPose(input.Node,input.PreviousPose,input.PreviousRoot,
                    prepareTransforms,translation,rotation,scale,visit==null?null:n=>visit(layer,n));
            }
            OwnerFlag101=0;return captured;
        }
    }
}
