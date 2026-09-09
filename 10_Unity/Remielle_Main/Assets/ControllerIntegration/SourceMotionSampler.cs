using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Remielle.ControllerRuntime;
using UnityEngine;

namespace Remielle.Controller
{
    // Runtime clip sampling uses the complete ACL database at the requested
    // time. State identity selects the slot; a clip name never selects a source.
    public sealed class SourceMotionSampler : IDisposable
    {
        readonly NativeMotionCodec codec;
        readonly NativeMotionPoseBinding binding;
        readonly Transform root;
        readonly float[] transforms, scalars;
        readonly Vector3 start, cycle;
        bool disposed;
        public string AssetID { get; }
        public float Duration => codec.Duration;
        public bool Loop { get; }
        public Vector3 RootPosition { get; private set; }
        public Vector3 UnwrappedRoot { get; private set; }
        public Vector3 CycleDisplacement => cycle;
        public float ClipTime { get; private set; }
        public long CompletedCycles { get; private set; }

        public SourceMotionSampler(string directory, NativeMotionBank bank,
            string controller, int clipIndex, Transform driverRoot, JObject profile)
        {
            AssetID=bank.ResolveSlot(controller,clipIndex);
            var archive=bank.Load(AssetID);
            binding=new NativeMotionPoseBinding(archive,driverRoot,profile);
            if(binding.UnboundTransformHashes.Count!=0 || binding.UnboundMorphTracks.Count!=0)
                throw new InvalidDataException("Motion has unresolved original Avatar channels: "+AssetID);
            root=driverRoot.Find("Root");
            if(!root)throw new InvalidDataException("Original Root path missing");
            if(!Enumerable.Range(0,archive.TransformCount).Any(i=>archive.TransformHash(i)==unchecked((uint)Animator.StringToHash("Root"))))
                throw new InvalidDataException("Motion has no explicit Root track");
            codec=new NativeMotionCodec(directory,bank.Record(AssetID));
            Loop=archive.Loop;
            transforms=new float[codec.TransformCount*10];scalars=new float[codec.ScalarCount];
            try
            {
                Apply(0);start=root.localPosition;
                Apply(Duration);cycle=root.localPosition-start;
                Apply(0);
            }
            catch {codec.Dispose();throw;}
        }

        void Apply(float time)
        {
            binding.ResetDefaults();
            codec.SampleTransforms(time,transforms);codec.SampleScalars(time,scalars);
            binding.ApplyTransformSamples(transforms);binding.ApplyScalarSamples(scalars);
        }

        // An explicit elapsed time handles multiple loop crossings in one
        // update. The end sample is decoded at Duration, not the guard frame.
        public void Sample(double elapsed)
        {
            if(disposed)throw new ObjectDisposedException(nameof(SourceMotionSampler));
            if(double.IsNaN(elapsed)||double.IsInfinity(elapsed)||elapsed<0)throw new ArgumentOutOfRangeException(nameof(elapsed));
            double cycles=Loop&&Duration>0?Math.Floor(elapsed/Duration):0;
            if(cycles>int.MaxValue)throw new ArgumentOutOfRangeException(nameof(elapsed));
            CompletedCycles=(long)cycles;
            ClipTime=Loop&&Duration>0?(float)(elapsed-cycles*Duration):(float)Math.Min(elapsed,Duration);
            Apply(ClipTime);RootPosition=root.localPosition;
            UnwrappedRoot=RootPosition-start+cycle*(float)cycles;
        }

        public void Dispose(){if(disposed)return;disposed=true;codec.Dispose();}
    }
}
