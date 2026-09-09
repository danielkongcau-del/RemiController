using System;
using System.Linq;
using UnityEngine;

namespace Remielle.Controller
{
    // A two-pose presentation adapter. Transition order, duration, interruption,
    // clocks and events belong to the controller, not this interpolator.
    public sealed class SourcePoseMixer
    {
        readonly RemielleNativeAnimation driver;
        readonly Vector3[] positions,scales;
        readonly Quaternion[] rotations;
        readonly (SkinnedMeshRenderer renderer,int index)[] morphs;
        readonly float[] morphValues;
        public sealed class Pose
        {
            internal SourcePoseMixer Owner;
            internal Vector3[] Positions,Scales;
            internal Quaternion[] Rotations;
            internal float[] MorphValues;
            internal Vector3 Root;
        }
        Pose frozen;
        internal Pose Capture()
        {
            return new Pose { Owner=this,Positions=driver.bones.Select(x=>x.source.localPosition).ToArray(),
                Scales=driver.bones.Select(x=>x.source.localScale).ToArray(),
                Rotations=driver.bones.Select(x=>x.source.localRotation).ToArray(),
                MorphValues=morphs.Select(x=>x.renderer.GetBlendShapeWeight(x.index)).ToArray(),Root=RootPosition };
        }
        internal void Freeze(Pose pose)
        {
            if(pose!=null&&!ReferenceEquals(pose.Owner,this))throw new ArgumentException("Snapshot belongs to another rig mixer");
            frozen=pose;
        }
        public Vector3 RootPosition { get; private set; }
        public SourcePoseMixer(RemielleNativeAnimation driver)
        {
            this.driver=driver;
            positions=new Vector3[driver.bones.Length];scales=new Vector3[positions.Length];rotations=new Quaternion[positions.Length];
            morphs=driver.morphs.SelectMany(x=>Enumerable.Range(0,x.source.sharedMesh.blendShapeCount).Select(i=>(x.source,i))).ToArray();
            morphValues=new float[morphs.Length];
        }
        public void Sample(SourceMotionSampler from,double fromTime,SourceMotionSampler to,double toTime,float weight)
        {
            if(!float.IsFinite(weight)||weight<0||weight>1||to==null&&weight!=0)throw new ArgumentOutOfRangeException(nameof(weight));
            from.Sample(fromTime);RootPosition=from.RootPosition;
            if(to!=null)
            {
                for(int i=0;i<positions.Length;i++)
                {var t=driver.bones[i].source;positions[i]=frozen==null?t.localPosition:frozen.Positions[i];
                    rotations[i]=frozen==null?t.localRotation:frozen.Rotations[i];scales[i]=frozen==null?t.localScale:frozen.Scales[i];}
                for(int i=0;i<morphs.Length;i++)morphValues[i]=frozen==null?morphs[i].renderer.GetBlendShapeWeight(morphs[i].index):frozen.MorphValues[i];
                if(frozen!=null)RootPosition=frozen.Root;
                to.Sample(toTime);RootPosition=Vector3.Lerp(RootPosition,to.RootPosition,weight);
                for(int i=0;i<positions.Length;i++)
                {
                    var t=driver.bones[i].source;
                    t.localPosition=Vector3.Lerp(positions[i],t.localPosition,weight);
                    t.localRotation=Quaternion.Slerp(rotations[i],t.localRotation,weight);
                    t.localScale=Vector3.Lerp(scales[i],t.localScale,weight);
                }
                for(int i=0;i<morphs.Length;i++)
                {var m=morphs[i];m.renderer.SetBlendShapeWeight(m.index,Mathf.Lerp(morphValues[i],m.renderer.GetBlendShapeWeight(m.index),weight));}
            }
            driver.ApplyPose();
        }
    }
}
