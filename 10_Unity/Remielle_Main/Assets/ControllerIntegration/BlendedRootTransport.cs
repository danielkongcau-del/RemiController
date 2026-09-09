using System;
using System.Linq;
using UnityEngine;

namespace Remielle.Controller
{
    public sealed class BlendedRootTransport : IDisposable
    {
        readonly RemielleNativeAnimation driver;
        readonly RemielleAuthoredMotor motor;
        readonly Transform visual;
        readonly SourcePoseMixer mixer;
        readonly Matrix4x4 sourceToMotor;
        readonly Vector3 origin,rootRest;
        readonly bool driverEnabled,animationEnabled,autoplay;
        SourceMotionSampler current,next;
        Vector3 previousCurrent,previousNext;
        double currentTime,nextTime;
        float previousWeight;
        long sequence;
        bool disposed,frozen;
        public bool IsBlending => next!=null;
        public BlendedRootTransport(SourceMotionSampler initial,RemielleNativeAnimation driver,
            Transform visual,RemielleAuthoredMotor motor,long firstSequence,double initialTime=0)
        {
            if(initial==null||!driver||!visual||!motor||visual.parent!=motor.transform||!driver.transform.IsChildOf(visual))throw new ArgumentException("Motor/visual hierarchy");
            this.driver=driver;this.visual=visual;this.motor=motor;current=initial;sequence=firstSequence;
            var root=driver.bones.Single(x=>x.source==driver.nativeAnimation.transform.Find("Root"));
            rootRest=root.sourceRestPosition;origin=visual.localPosition;
            sourceToMotor=motor.transform.worldToLocalMatrix*root.target.parent.localToWorldMatrix*root.parentBasisInverse;
            mixer=new SourcePoseMixer(driver);
            driverEnabled=driver.enabled;animationEnabled=driver.nativeAnimation.enabled;autoplay=driver.autoplay;
            driver.enabled=false;driver.nativeAnimation.enabled=false;driver.autoplay=false;
            mixer.Sample(current,initialTime,null,0,0);currentTime=initialTime;previousCurrent=current.UnwrappedRoot;Compensate();
        }
        void RequireLive(){if(disposed)throw new ObjectDisposedException(nameof(BlendedRootTransport));}
        public void RefreshPose()
        {
            RequireLive();
            mixer.Sample(current,currentTime,next,nextTime,previousWeight);Compensate();
        }
        public void BeginBlend(SourceMotionSampler destination,double destinationTime)
        {
            RequireLive();
            if(IsBlending)throw new InvalidOperationException("Interruption requires an explicit controller decision");
            if(destination==null||ReferenceEquals(destination,current))throw new ArgumentException("A blend needs independent source and destination samplers, including self transitions");
            destination.Sample(destinationTime);
            next=destination;nextTime=destinationTime;previousNext=next.UnwrappedRoot;previousWeight=0;
            // Starting the next sampler must not leave its pose on the live rig.
            mixer.Sample(current,currentTime,next,nextTime,0);Compensate();
        }
        public SourcePoseMixer.Pose CaptureInterruption()
        {
            RequireLive();
            if(!IsBlending)throw new InvalidOperationException("No transition to interrupt");
            return mixer.Capture();
        }
        public void InterruptBlend(SourceMotionSampler destination,double destinationTime,SourcePoseMixer.Pose pose)
        {
            RequireLive();
            if(!IsBlending||pose==null)throw new InvalidOperationException("Interruption requires the live mixed pose");
            if(destination==null||ReferenceEquals(destination,current)||ReferenceEquals(destination,next))
                throw new ArgumentException("Interrupted destination requires its own sampler");
            next=destination;nextTime=destinationTime;next.Sample(nextTime);
            previousNext=next.UnwrappedRoot;previousWeight=0;frozen=true;mixer.Freeze(pose);
            mixer.Sample(current,currentTime,next,nextTime,0);Compensate();
        }
        public void Sample(double sourceTime,double destinationTime=0,float weight=0)
        {
            RequireLive();
            if(!double.IsFinite(sourceTime)||sourceTime<currentTime||!float.IsFinite(weight)||weight<0||weight>1||
                (IsBlending?(!double.IsFinite(destinationTime)||destinationTime<nextTime||weight<previousWeight):weight!=0))
                throw new ArgumentOutOfRangeException("Blend time/weight must advance monotonically");
            mixer.Sample(current,sourceTime,next,destinationTime,weight);
            // A captured static pose emits no authored travel. The new target's
            // increments fade in; this is a practical presentation policy,
            // not a claim about the game's native graph root-velocity mixer.
            Vector3 delta=frozen?Vector3.zero:current.UnwrappedRoot-previousCurrent;
            if(IsBlending)
            {
                // Mix travel increments, never differentiate weighted absolute
                // positions from clips that have unrelated origins/cycle counts.
                delta=Vector3.Lerp(delta,next.UnwrappedRoot-previousNext,(previousWeight+weight)*.5f);
                previousNext=next.UnwrappedRoot;nextTime=destinationTime;
            }
            motor.ApplyAuthoredDelta(sequence,motor.transform.TransformVector(sourceToMotor.MultiplyVector(delta)));
            sequence++;previousCurrent=current.UnwrappedRoot;currentTime=sourceTime;previousWeight=weight;Compensate();
        }
        void Compensate(){visual.localPosition=origin-sourceToMotor.MultiplyVector(mixer.RootPosition-rootRest);}
        public void CompleteBlend()
        {
            RequireLive();
            if(!IsBlending||previousWeight!=1)throw new InvalidOperationException("Destination has not reached full weight");
            current=next;currentTime=nextTime;previousCurrent=previousNext;next=null;previousWeight=0;
            frozen=false;mixer.Freeze(null);
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            if(visual)visual.localPosition=origin;
            if(driver)
            {
                if(driver.bones.All(x=>x.source&&x.target)){driver.ResetSourcePose();driver.ApplyPose();}
                driver.enabled=driverEnabled;if(driver.nativeAnimation)driver.nativeAnimation.enabled=animationEnabled;driver.autoplay=autoplay;
            }
        }
    }
}
