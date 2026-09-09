using System;
using System.Linq;
using UnityEngine;

namespace Remielle.Controller
{
    // Explicit single-clip transport used to qualify authored translation.
    // The caller owns clip selection, time, transitions, loops and root rotation.
    // Those policies are deliberately not inferred from a looping sample jump.
    public sealed class SingleClipRootTransport : IDisposable
    {
        readonly RemielleNativeAnimation driver;
        readonly Transform visual;
        readonly RemielleAuthoredMotor motor;
        readonly string clip;
        readonly RemielleNativeAnimation.BoneLink root;
        readonly Vector3 visualOrigin, rootOrigin;
        readonly Matrix4x4 rootToWorld;
        readonly Quaternion orientation;
        readonly bool driverEnabled, animationEnabled, autoplay;
        Vector3 previousDisplacement;
        float previousTime;
        long sequence;
        bool disposed;
        public Vector3 AuthoredDisplacement { get; private set; }

        public SingleClipRootTransport(RemielleNativeAnimation driver, Transform visual,
            RemielleAuthoredMotor motor, string clip, long firstSequence)
        {
            if (!driver || !visual || !motor || visual.parent != motor.transform ||
                !driver.transform.IsChildOf(visual)) throw new ArgumentException("Expected motor -> visual -> original driver hierarchy");
            if (!driver.nativeAnimation || driver.nativeAnimation[clip] == null) throw new ArgumentException("Missing native clip");
            this.driver=driver;this.visual=visual;this.motor=motor;this.clip=clip;sequence=firstSequence;
            root=driver.bones.Single(x=>x.source==driver.nativeAnimation.transform.Find("Root"));
            visualOrigin=visual.localPosition;orientation=motor.transform.rotation;
            driverEnabled=driver.enabled;animationEnabled=driver.nativeAnimation.enabled;autoplay=driver.autoplay;
            driver.enabled=false;driver.autoplay=false;driver.nativeAnimation.enabled=false;
            driver.Sample(clip,0);rootOrigin=root.source.localPosition;
            rootToWorld=root.target.parent.localToWorldMatrix*root.parentBasisInverse;
        }

        public void Sample(float time)
        {
            if(disposed)throw new ObjectDisposedException(nameof(SingleClipRootTransport));
            if(!float.IsFinite(time)||time<previousTime||time>=driver.nativeAnimation[clip].length)
                throw new ArgumentOutOfRangeException(nameof(time),"Single clip transport requires increasing time before the loop boundary");
            if(Quaternion.Angle(orientation,motor.transform.rotation)>.001f)
                throw new InvalidOperationException("Root rotation/steering requires a separate qualified policy");
            driver.Sample(clip,time);
            Vector3 displacement=rootToWorld.MultiplyVector(root.source.localPosition-rootOrigin);
            motor.ApplyAuthoredDelta(sequence,displacement-previousDisplacement);
            // Root and pelvis are siblings carrying the same travel. Compensate
            // the whole presentation, retaining their relative authored motion.
            visual.localPosition=visualOrigin-motor.transform.InverseTransformVector(displacement);
            previousDisplacement=displacement;AuthoredDisplacement=displacement;previousTime=time;sequence++;
        }

        public void Dispose()
        {
            if(disposed)return;
            disposed=true;
            visual.localPosition=visualOrigin;
            driver.ResetSourcePose();driver.ApplyPose();
            driver.enabled=driverEnabled;driver.nativeAnimation.enabled=animationEnabled;driver.autoplay=autoplay;
        }
    }
}
