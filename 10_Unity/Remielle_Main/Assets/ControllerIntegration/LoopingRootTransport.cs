using System;
using System.Linq;
using UnityEngine;

namespace Remielle.Controller
{
    // Owns presentation compensation and monotonically consumed motor deltas.
    // Input, state transitions and root rotation remain the caller's policies.
    public sealed class LoopingRootTransport : IDisposable
    {
        readonly SourceMotionSampler sampler;
        readonly RemielleNativeAnimation driver;
        readonly RemielleAuthoredMotor motor;
        readonly Transform visual;
        readonly Vector3 visualOrigin,rootRest;
        readonly Matrix4x4 sourceToMotor;
        readonly bool driverEnabled,animationEnabled,autoplay;
        Vector3 previousRoot;
        double previousTime;
        long sequence;
        bool disposed;

        public LoopingRootTransport(SourceMotionSampler sampler,RemielleNativeAnimation driver,
            Transform visual,RemielleAuthoredMotor motor,long firstSequence)
        {
            if(sampler==null||!driver||!visual||!motor||visual.parent!=motor.transform||!driver.transform.IsChildOf(visual))
                throw new ArgumentException("Expected motor -> visual -> original driver hierarchy");
            this.sampler=sampler;this.driver=driver;this.visual=visual;this.motor=motor;sequence=firstSequence;
            var root=driver.bones.Single(x=>x.source==driver.nativeAnimation.transform.Find("Root"));
            visualOrigin=visual.localPosition;rootRest=root.sourceRestPosition;
            sourceToMotor=motor.transform.worldToLocalMatrix*root.target.parent.localToWorldMatrix*root.parentBasisInverse;
            driverEnabled=driver.enabled;animationEnabled=driver.nativeAnimation.enabled;autoplay=driver.autoplay;
            driver.enabled=false;driver.nativeAnimation.enabled=false;driver.autoplay=false;
            sampler.Sample(0);previousRoot=sampler.UnwrappedRoot;ApplyPose();
        }
        void ApplyPose()
        {
            driver.ApplyPose();
            visual.localPosition=visualOrigin-sourceToMotor.MultiplyVector(sampler.RootPosition-rootRest);
        }
        public void Sample(double elapsed)
        {
            if(disposed)throw new ObjectDisposedException(nameof(LoopingRootTransport));
            if(double.IsNaN(elapsed)||double.IsInfinity(elapsed)||elapsed<previousTime)throw new ArgumentOutOfRangeException(nameof(elapsed));
            sampler.Sample(elapsed);
            Vector3 worldDelta=motor.transform.TransformVector(sourceToMotor.MultiplyVector(sampler.UnwrappedRoot-previousRoot));
            motor.ApplyAuthoredDelta(sequence,worldDelta);sequence++;
            previousRoot=sampler.UnwrappedRoot;previousTime=elapsed;ApplyPose();
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            visual.localPosition=visualOrigin;driver.ResetSourcePose();driver.ApplyPose();
            driver.enabled=driverEnabled;driver.nativeAnimation.enabled=animationEnabled;driver.autoplay=autoplay;
            // The caller owns sampler lifetime so clips can be cached by identity.
        }
    }
}
