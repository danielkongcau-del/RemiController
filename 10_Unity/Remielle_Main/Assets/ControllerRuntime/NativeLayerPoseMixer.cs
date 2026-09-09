using System;
using System.Collections.Generic;
using System.Linq;

namespace Remielle.ControllerRuntime
{
    // Ordinary query payload with native pointer fields represented as managed
    // references. Non-pointer bytes are preserved across the 0x60-byte copy.
    public sealed class NativeLayerPoseQuery
    {
        readonly byte[] payload,bodyMask;
        public NativePoseData OverrideDefaults { get; }
        public NativePoseData EvaluationDefaults { get; }
        public bool Additive=>payload[0x18]!=0;
        public byte[] Payload()=>(byte[])payload.Clone();
        public byte[] BodyMask()=>(byte[])bodyMask.Clone();
        public NativeLayerPoseQuery(byte[] payload,byte[] bodyMask,NativePoseData overrideDefaults,NativePoseData evaluationDefaults)
        {
            if(payload==null||payload.Length!=0x60||bodyMask==null||bodyMask.Length!=12)
                throw new ArgumentException("Layer query dimensions differ");
            foreach(int offset in new[]{0x20,0x30,0x38})
                for(int i=0;i<8;i++)if(payload[offset+i]!=0)throw new ArgumentException("Query pointer fields must use managed references");
            this.payload=(byte[])payload.Clone();this.bodyMask=(byte[])bodyMask.Clone();
            OverrideDefaults=overrideDefaults;EvaluationDefaults=evaluationDefaults;
        }
        internal NativeLayerPoseQuery ForLayer(bool additive,byte[] mask,NativePoseData defaults)
        {
            var copy=Payload();copy[0x18]=(byte)(additive?1:0);
            return new NativeLayerPoseQuery(copy,mask,OverrideDefaults,defaults);
        }
    }

    // Complete cd6ad0/cd6050 ordinary pose route (context+82=false). The caller
    // provides prepared per-layer resources and child pose producers explicitly.
    public sealed class NativeLayerPoseMixer
    {
        readonly NativePoseData scratch;
        readonly byte[][][] layerMasks;
        readonly byte[][] bodyMasks;
        bool evaluating;
        public IReadOnlyList<NativePoseStream> History { get; }
        public NativePoseData Scratch()=>scratch.Clone();

        public NativeLayerPoseMixer(IReadOnlyList<NativePoseStream> history,NativePoseData initialScratch,
            IReadOnlyList<byte[][]> layerMasks,IReadOnlyList<byte[]> bodyMasks)
        {
            if(history==null||initialScratch==null||layerMasks==null||bodyMasks==null||
                history.Count!=layerMasks.Count||history.Count!=bodyMasks.Count||history.Any(h=>h==null))
                throw new ArgumentException("Prepared layer resources differ");
            if(history.Distinct().Count()!=history.Count)throw new ArgumentException("Layers need distinct history streams");
            History=Array.AsReadOnly(history.ToArray());scratch=initialScratch.Clone();
            this.layerMasks=layerMasks.Select((m,i)=>{
                history[i].CheckPose(initialScratch);if(m==null)return null;
                history[i].CheckMask(m);return m.Select(r=>(byte[])r.Clone()).ToArray();
            }).ToArray();
            this.bodyMasks=bodyMasks.Select(m=>m!=null&&m.Length==12?(byte[])m.Clone():throw new ArgumentException("Body mask needs 12 bytes")).ToArray();
        }

        public int Evaluate(IReadOnlyList<NativeLayerInputWeight> weights,IReadOnlyList<NativeMixerLink> links,
            IReadOnlyList<NativeMixerNode> nodes,NativePoseData sourceDefaults,NativeLayerPoseQuery query,
            NativePoseStream output,Action<int,int,NativeLayerPoseQuery,NativePoseStream> evaluateChild)
        {
            if(weights==null||links==null||nodes==null||query==null||output==null||evaluateChild==null)
                throw new ArgumentNullException();
            if(weights.Count!=History.Count||links.Count!=History.Count)throw new ArgumentException("Layer inputs differ from prepared resources");
            output.CheckPose(scratch);output.CheckPose(sourceDefaults);
            if(query.OverrideDefaults!=null)output.CheckPose(query.OverrideDefaults);
            foreach(var h in History)
                if(ReferenceEquals(h,output)||!ReferenceEquals(h.BindingLayout,output.BindingLayout))
                    throw new ArgumentException("Layer histories and output must be distinct streams in the same layout");
            if(evaluating)throw new InvalidOperationException("A layer mixer cannot evaluate itself recursively");
            evaluating=true;
            try
            {
                if(weights.Count==1&&!weights[0].Additive)
                {
                    // The native single-layer path does not filter its weight
                    // or the resolved child's delay. A null child returns as-is.
                    int node=Resolve(links[0],nodes);if(node<0)return 0;
                    var childQuery=query.ForLayer(false,bodyMasks[0],History[0].Data);
                    evaluateChild(0,node,childQuery,output);
                    Intersect(output,layerMasks[0]);CopyActive(output,History[0].Data);
                    for(int g=0;g<5;g++)for(int i=0;i<output.Active[g].Length;i++)
                        History[0].Active[g][i]=(byte)(History[0].Active[g][i]!=0||output.Active[g][i]!=0?1:0);
                    // cf8110 unions masks. Ordinary single-layer finalization
                    // does not copy stream or temporary reference flags.
                    return 1;
                }
                output.ClearMask();output.StreamFlag=0;
                if(weights.Count==0)return 0;
                CopyAll(output.Data,scratch);int calls=0;
                for(int layer=0;layer<weights.Count;layer++)
                {
                    float weight=weights[layer].Input;if(!(weight>0f))continue;
                    int node=Resolve(links[layer],nodes);if(node<0||nodes[node].Delay>0d)continue;
                    CopyActive(output,scratch);
                    var childQuery=query.ForLayer(weights[layer].Additive,bodyMasks[layer],scratch);
                    var history=History[layer];history.ReferenceFlag0=0;history.ReferenceFlag1=0;
                    evaluateChild(layer,node,childQuery,history);calls++;
                    Intersect(history,layerMasks[layer]);
                    NativeLayerPoseMath.Compose(query.OverrideDefaults??sourceDefaults,history,weight,weights[layer].Additive,output);
                }
                return calls;
            }
            finally{evaluating=false;}
        }

        static void Intersect(NativePoseStream stream,byte[][] mask)
        {
            if(mask==null)return;
            for(int g=0;g<5;g++)for(int i=0;i<stream.Active[g].Length;i++)
                stream.Active[g][i]=(byte)(stream.Active[g][i]!=0&&mask[g][i]!=0?1:0);
        }
        static void CopyAll(NativePoseData source,NativePoseData target)
        {
            for(int g=0;g<4;g++)Array.Copy(source.Floats[g],target.Floats[g],source.Floats[g].Length);
            Array.Copy(source.Discrete,target.Discrete,source.Discrete.Length);
        }
        static void CopyActive(NativePoseStream source,NativePoseData target)
        {
            for(int g=0;g<5;g++)for(int i=0;i<source.Active[g].Length;i++)
            {
                if(source.Active[g][i]==0)continue;
                if(g==4)target.Discrete[i]=source.Data.Discrete[i];
                else{int width=g<3?4:1;Array.Copy(source.Data.Floats[g],i*width,target.Floats[g],i*width,width);}
            }
        }
        // Same typed-port resolution used by cce310. This layer path never
        // invokes state-mixer time interpolation or normalizes input weights.
        static int Resolve(NativeMixerLink link,IReadOnlyList<NativeMixerNode> nodes)
        {
            var visited=new HashSet<(int,int)>();
            while(link.Node>=0)
            {
                if(link.Node>=nodes.Count||nodes[link.Node]==null)throw new ArgumentException("Invalid layer graph node");
                var node=nodes[link.Node];if(node.Type==0)return link.Node;
                if((uint)link.Port>=(uint)node.Inputs.Count)return -1;
                if(!visited.Add((link.Node,link.Port)))throw new ArgumentException("Cyclic layer input");
                link=node.Inputs[link.Port];
            }
            return -1;
        }
    }
}
