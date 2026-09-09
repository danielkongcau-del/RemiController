using System;
using System.Collections.Generic;

namespace Remielle.ControllerRuntime
{
    // Motion-set graph context +8/+18/+1a. The caller supplies the qualified
    // transition decision; this object does not invent its upstream producer.
    public sealed class NativeGraphTransitionContext
    {
        public uint EventCode { get; set; }
        public bool FootIK { get; set; }
        public bool RequestCachedCurrent { get; set; }
    }

    public static class NativeGraphPoseSchedule
    {
        // cfffce..d000c0. Repeated interruptions request a new previous pose
        // without rewiring an already selected cache or changing its IK policy.
        public static bool ScheduleInterruption(NativeStateGraphBindings graph,
            NativeLayerTransitionState state,double timestamp)
        {
            if(graph==null||state==null)throw new ArgumentNullException();
            var context=graph.TransitionContext;
            if(!context.RequestCachedCurrent)return false;
            context.EventCode=0x1a;
            if(state.GraphFlag80==0)
            {
                foreach(var set in graph.MotionSets)
                {
                    var current=set.Root.Inputs[0].Node;
                    bool anyNonzero=false;
                    if(current!=null)foreach(var input in current.Inputs)
                        // Native ucomiss counts unordered, negative and tiny
                        // nonzero values too; this is not a positive-weight test.
                        if(input.Weight.Input!=0f){anyNonzero=true;break;}
                    set.SelectCachedCurrentPose(context.FootIK,!anyNonzero,timestamp);
                }
                state.GraphFlag80=1;
            }
            foreach(var set in graph.MotionSets)
                set.Root.Inputs[set.CachePort].Node.Pose.RequestPreviousPose();
            return true;
        }

        // cc5650 -> cd5580/cce310 -> virtual +90, including full cd68c0
        // pose leaves. The caller supplies the previous completed pose for this
        // motion-set; whole-frame production and job ordering remain separate.
        public static int ReadPreviousPose(NativeGraphNode root,NativePoseStream previous,
            byte[] previousRoot,bool prepareTransforms,int translation,int rotation,int scale,
            Action<NativeGraphNode> visit=null)
        {
            if(root==null)throw new ArgumentNullException(nameof(root));
            return Read(root,new HashSet<NativeGraphNode>());
            int Read(NativeGraphNode node,HashSet<NativeGraphNode> active)
            {
                if(!active.Add(node))throw new ArgumentException("Cyclic previous-pose graph");
                visit?.Invoke(node);
                int captures=0;
                if(node.Pose!=null)
                    captures=node.Pose.ReadPreviousPose(previous,previousRoot,prepareTransforms,translation,rotation,scale)?1:0;
                else
                {
                    node.Flag100=0;
                    // This callback intentionally traverses zero-weight inputs.
                    foreach(var input in node.Inputs)
                    {
                        var child=Resolve(input);
                        if(child!=null)captures+=Read(child,active);
                    }
                }
                active.Remove(node);return captures;
            }
        }
        static NativeGraphNode Resolve(NativeGraphInput input)
        {
            var visited=new HashSet<(NativeGraphNode,int)>();
            while(input.Node!=null)
            {
                var node=input.Node;
                if(node.Type==0)return node;
                if((uint)input.OutputPort>=(uint)node.Inputs.Count)return null;
                if(!visited.Add((node,input.OutputPort)))throw new ArgumentException("Cyclic forwarded previous-pose input");
                input=node.Inputs[input.OutputPort];
            }
            return null;
        }
    }
}
