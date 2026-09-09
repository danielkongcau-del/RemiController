using System;
using System.Linq;
using System.Runtime.InteropServices;

namespace Remielle.ControllerRuntime
{
    public sealed class NativePoseData
    {
        internal readonly float[][] Floats;
        internal readonly uint[] Discrete;
        internal readonly int[] Counts;
        public NativePoseData(float[] positions, float[] rotations, float[] scales, float[] scalars, uint[] discrete)
        {
            if(positions==null||rotations==null||scales==null||scalars==null||discrete==null)throw new ArgumentNullException();
            if(positions.Length%4!=0||rotations.Length%4!=0||scales.Length%4!=0)throw new ArgumentException("Pose vectors need four lanes");
            Floats=new[]{(float[])positions.Clone(),(float[])rotations.Clone(),(float[])scales.Clone(),(float[])scalars.Clone()};
            Discrete=(uint[])discrete.Clone();Counts=new[]{positions.Length/4,rotations.Length/4,scales.Length/4,scalars.Length,discrete.Length};
        }
        public float[] FloatChannel(int channel)=>(float[])Floats[channel].Clone();
        public uint[] DiscreteChannel()=>(uint[])Discrete.Clone();
        public int[] ChannelCounts()=>(int[])Counts.Clone();
        public NativePoseData Clone()=>new NativePoseData(Floats[0],Floats[1],Floats[2],Floats[3],Discrete);
    }

    public static class NativePoseMath
    {
        [DllImport("RemiellePoseMath",EntryPoint="remielle_pose_normalize",CallingConvention=CallingConvention.Cdecl)]
        static extern int NormalizeNative([In,Out] float[] quaternions,[In] byte[] active,int count);
        [DllImport("RemiellePoseMath",EntryPoint="remielle_pose_math_abi",CallingConvention=CallingConvention.Cdecl)]
        public static extern int Abi();

        // Independent SSE implementation of cf946a..cf94bd. The approximate
        // rsqrt instruction is intentionally retained; a precise sqrt/divide
        // replacement would change the native result on the current CPU.
        public static void Normalize(float[] quaternions,byte[] active)
        {
            if(quaternions==null||active==null||quaternions.Length!=checked(active.Length*4))throw new ArgumentException("Quaternion dimensions differ");
            if(Abi()!=1||NormalizeNative(quaternions,active,active.Length)!=0)throw new InvalidOperationException("Native pose normalization failed");
        }
    }

    // Original cf9ac0/cf9330 math on explicit pose/mask layouts. Initialization,
    // defaults, source binding and callback scheduling belong to the caller.
    public sealed class NativePoseAccumulator
    {
        readonly NativePoseData pose;
        readonly byte[][] mask;
        readonly float[][] weights;
        public NativePoseAccumulator(NativePoseData initial,byte[][] initialMask,float[][] initialWeights)
        {
            if(initial==null)throw new ArgumentNullException(nameof(initial));
            pose=initial.Clone();CheckMask(initialMask);
            if(initialWeights==null||initialWeights.Length!=5)throw new ArgumentException("Pose weight dimensions differ");
            mask=initialMask.Select(r=>(byte[])r.Clone()).ToArray();weights=initialWeights.Select(r=>r==null?null:(float[])r.Clone()).ToArray();
            for(int g=0;g<5;g++)if(weights[WeightGroup(g)]==null||weights[WeightGroup(g)].Length!=pose.Counts[g])throw new ArgumentException("Pose weight channel differs");
        }
        public NativePoseData Pose()=>pose.Clone();
        public byte[][] Mask()=>mask.Select(r=>(byte[])r.Clone()).ToArray();
        // Original wire order is translation, rotation, scale, discrete, scalar.
        public float[][] Weights()=>weights.Select(r=>(float[])r.Clone()).ToArray();

        public void Accumulate(NativePoseData source,byte[][] sourceMask,float weight)
        {
            CheckPose(source);CheckMask(sourceMask);
            for(int g=0;g<5;g++)for(int i=0;i<pose.Counts[g];i++)
            {
                if(sourceMask[g][i]==0)continue;
                int wg=WeightGroup(g),off=i*(g<3?4:1);
                if(mask[g][i]==0)
                {
                    weights[wg][i]=g==4?-1f:0f;
                    if(g==4)pose.Discrete[i]=0;
                    else Array.Clear(pose.Floats[g],off,g<3?4:1);
                    mask[g][i]=1;
                }
                if(g==4)
                {
                    if(weight>weights[wg][i]){weights[wg][i]=weight;pose.Discrete[i]=source.Discrete[i];}
                    continue;
                }
                weights[wg][i]=(float)(weight+weights[wg][i]);
                if(g==1){AddQuaternion(pose.Floats[g],source.Floats[g],off,weight);continue;}
                // Translation/scale preserve the fourth lane of existing storage.
                for(int lane=0;lane<(g<3?3:1);lane++)
                {
                    float contribution=(float)(weight*source.Floats[g][off+lane]);
                    pose.Floats[g][off+lane]=(float)(contribution+pose.Floats[g][off+lane]);
                }
            }
        }

        public void Finish(NativePoseData defaults)
        {
            if(defaults!=null)CheckPose(defaults);
            if(defaults!=null)
                for(int g=0;g<4;g++)for(int i=0;i<pose.Counts[g];i++)
                {
                    float sum=weights[WeightGroup(g)][i];
                    if(mask[g][i]==0||!(sum<1f))continue;
                    float remaining=(float)(1f-sum);int off=i*(g<3?4:1);
                    if(g==1){AddQuaternion(pose.Floats[g],defaults.Floats[g],off,remaining);continue;}
                    for(int lane=0;lane<(g<3?3:1);lane++)
                    {
                        float contribution=(float)(remaining*defaults.Floats[g][off+lane]);
                        pose.Floats[g][off+lane]=(float)(contribution+pose.Floats[g][off+lane]);
                    }
                }
            NativePoseMath.Normalize(pose.Floats[1],mask[1]);
        }

        static void AddQuaternion(float[] target,float[] source,int o,float weight)
        {
            float p0=(float)(source[o]*target[o]),p1=(float)(source[o+1]*target[o+1]);
            float p2=(float)(source[o+2]*target[o+2]),p3=(float)(source[o+3]*target[o+3]);
            float s0=(float)(p1+p0),s1=(float)(p2+p1),s2=(float)(p3+p2),s3=(float)(p0+p3);
            AddLane(target,source,o,weight,(float)(s2+s0));
            AddLane(target,source,o+1,weight,(float)(s3+s1));
            AddLane(target,source,o+2,weight,(float)(s0+s2));
            AddLane(target,source,o+3,weight,(float)(s1+s3));
        }
        static void AddLane(float[] target,float[] source,int i,float weight,float dot)
        {
            float contribution=(float)(weight*source[i]);
            int sign=BitConverter.SingleToInt32Bits(dot)&int.MinValue;
            contribution=BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(contribution)^sign);
            target[i]=(float)(contribution+target[i]);
        }
        static int WeightGroup(int group)=>group==3?4:group==4?3:group;
        void CheckPose(NativePoseData other)
        {if(other==null||!pose.Counts.SequenceEqual(other.Counts))throw new ArgumentException("Pose channel dimensions differ");}
        void CheckMask(byte[][] other)
        {
            if(other==null||other.Length!=5)throw new ArgumentException("Pose mask dimensions differ");
            for(int i=0;i<5;i++)if(other[i]==null||other[i].Length!=pose.Counts[i])throw new ArgumentException("Pose mask channel differs");
        }
    }
}
