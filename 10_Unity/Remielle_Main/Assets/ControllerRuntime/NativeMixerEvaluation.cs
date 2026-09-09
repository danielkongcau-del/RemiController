using System;
using System.Collections.Generic;
using System.Linq;

namespace Remielle.ControllerRuntime
{
    public readonly struct NativeMixerLink
    {
        public readonly int Node, Port;
        public NativeMixerLink(int node, int port) { Node=node; Port=port; }
    }

    public sealed class NativeMixerNode
    {
        public uint Type { get; }
        public double Delay { get; }
        public IReadOnlyList<NativeMixerLink> Inputs { get; }
        public NativeMixerNode(uint type, double delay, IReadOnlyList<NativeMixerLink> inputs)
        {
            if(inputs==null) throw new ArgumentNullException(nameof(inputs));
            Type=type; Delay=delay; Inputs=Array.AsReadOnly(inputs.ToArray());
        }
    }

    public readonly struct NativeMixerContribution
    {
        public readonly int Node;
        public readonly float Weight;
        public NativeMixerContribution(int node, float weight) { Node=node; Weight=weight; }
    }

    public enum NativeMixerDispatch { Empty, Single, Blend }

    public sealed class NativeMixerEvaluationResult
    {
        public bool Interpolate { get; }
        public bool Extrapolate { get; }
        public NativeMixerDispatch Dispatch { get; }
        public IReadOnlyList<NativeMixerContribution> Contributions { get; }
        internal NativeMixerEvaluationResult(bool interpolate, bool extrapolate, List<NativeMixerContribution> contributions)
        {
            Interpolate=interpolate; Extrapolate=extrapolate; Contributions=contributions.AsReadOnly();
            Dispatch=contributions.Count==0 ? NativeMixerDispatch.Empty :
                contributions.Count==1 && contributions[0].Weight==1f ? NativeMixerDispatch.Single : NativeMixerDispatch.Blend;
        }
    }

    public static class NativeMixerEvaluation
    {
        // Original cc5020 prefix used by both cc5fc0 and cc7de0. Evaluation
        // stops at pose dispatch: these weights are not normalized here.
        // Offset sign, TimeManager timestamp/delta and graph are explicit inputs.
        public static NativeMixerEvaluationResult Evaluate(IReadOnlyList<NativeMixerInputWeight> weights,
            IReadOnlyList<NativeMixerLink> links, IReadOnlyList<NativeMixerNode> nodes,
            float evaluationOffset, double timestamp, float timeManagerDelta)
        {
            if(weights==null || links==null || nodes==null) throw new ArgumentNullException();
            if(weights.Count!=links.Count) throw new ArgumentException("Mixer input dimensions differ");
            bool interpolate=evaluationOffset<0f, extrapolate=evaluationOffset>0f;
            if(interpolate || extrapolate)
                foreach(var input in weights)
                    if(input.Input>0f && input.StartTime==0d) { interpolate=false; extrapolate=false; }
            var contributions=new List<NativeMixerContribution>();
            for(int i=0;i<weights.Count;i++)
            {
                var input=weights[i]; float weight=input.Input;
                if(interpolate && input.StartTime!=0d)
                {
                    float span=(float)(input.Timestamp-input.StartTime);
                    if(timeManagerDelta>=span && span>0f)
                    {
                        double query=timestamp-(double)timeManagerDelta;
                        double duration=input.Timestamp-input.StartTime;
                        query-=input.StartTime;
                        float phase=(float)(query/duration);
                        phase=phase<0f ? 0f : Min(1f,phase);
                        float right=(float)(input.Input*phase);
                        float left=(float)(1f-phase);
                        left=(float)(left*input.Start);
                        weight=(float)(left+right);
                    }
                }
                else if(extrapolate && input.Rate!=0f)
                {
                    float elapsed=(float)(timestamp-input.Timestamp);
                    float delta=elapsed<0f ? 0f : Min(timeManagerDelta,elapsed);
                    float change=(float)(delta*input.Rate);
                    float value=(float)(change+weight);
                    if(value<0f) continue;
                    weight=Min(1f,value);
                }
                if(!(weight>0f)) continue;
                int child=Resolve(links[i],nodes);
                if(child<0 || nodes[child].Delay>0d) continue;
                contributions.Add(new NativeMixerContribution(child,weight));
            }
            return new NativeMixerEvaluationResult(interpolate,extrapolate,contributions);
        }

        // cd5580/cce310: native type zero is directly consumable; other types
        // follow the record's signed input port. Null/out-of-range ports resolve
        // to no input. Corrupt indices/cycles are rejected at the adapter boundary.
        static int Resolve(NativeMixerLink link, IReadOnlyList<NativeMixerNode> nodes)
        {
            var visited=new HashSet<(int,int)>();
            while(link.Node>=0)
            {
                if(link.Node>=nodes.Count || nodes[link.Node]==null) throw new ArgumentException("Invalid mixer graph node");
                var node=nodes[link.Node];
                if(node.Type==0) return link.Node;
                if((uint)link.Port>=(uint)node.Inputs.Count) return -1;
                if(!visited.Add((link.Node,link.Port))) throw new ArgumentException("Cyclic forwarded mixer input");
                link=node.Inputs[link.Port];
            }
            return -1;
        }

        // SSE minss selects its second operand on equality and unordered input.
        static float Min(float first, float second) => first<second ? first : second;
    }
}
