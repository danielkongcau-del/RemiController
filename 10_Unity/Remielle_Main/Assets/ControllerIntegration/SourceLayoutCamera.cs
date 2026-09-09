using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;
using UnityEngine;

namespace Remielle.Controller
{
    // Authored layout events and curves, composed by our Cinemachine host.
    // Only Dash_Start is enabled. Native orbit/elevation composition is not
    // inferred from field names; the preview coordinate policy is in the pack.
    public sealed class SourceLayoutCamera : IDisposable
    {
        public const string DashKey="RemielleOrigin_Dash_Start_Layout";
        readonly SourceFrameEvents events;
        readonly CinemachineCamera camera;
        readonly CinemachineFollow follow;
        readonly CinemachineRotationComposer composer;
        readonly Vector3 baseOffset,baseAim,targetOffset,targetShift;
        readonly float baseFov,targetFov,inDuration,outDuration;
        readonly AnimationCurve inCurve,outCurve;
        Vector3 currentOffset,currentShift,startOffset,startShift,endOffset,endShift;
        float currentFov,startFov,endFov,duration;
        double elapsed;
        AnimationCurve blendCurve;
        long owner;
        bool disposed,transitioning;
        public int Activations { get; private set; }
        public int Releases { get; private set; }
        public int ForcedReleases { get; private set; }
        public int UnsupportedEvents { get; private set; }
        public bool IsTransitioning=>transitioning;
        public bool HasOwner=>owner!=0;
        public float FieldOfView=>currentFov;
        public float BaselineFov=>baseFov;
        public bool AtBaseline=>!HasOwner&&!transitioning&&currentFov==baseFov&&currentOffset==baseOffset&&currentShift==Vector3.zero;

        public SourceLayoutCamera(string json,SourceFrameEvents events,CinemachineCamera camera)
        {
            var p=JObject.Parse(json);
            if((string)p["schema"]!="remielle-action-camera-v1"||!p["supportedKeys"].Values<string>().SequenceEqual(new[]{DashKey}))
                throw new ArgumentException("Unsupported camera pack");
            this.events=events??throw new ArgumentNullException(nameof(events));
            this.camera=camera?camera:throw new ArgumentNullException(nameof(camera));
            follow=camera.GetComponent<CinemachineFollow>();composer=camera.GetComponent<CinemachineRotationComposer>();
            if(!follow||!composer||!camera.Follow||camera.Follow!=camera.LookAt||
                follow.TrackerSettings.BindingMode!=BindingMode.WorldSpace||
                follow.TrackerSettings.PositionDamping!=Vector3.zero||composer.Damping!=Vector2.zero)
                throw new ArgumentException("Camera adapter requires the configured world-space preview rig");
            var d=p["layouts"][DashKey];
            if((bool)d["DisableFollowPosition"]||!(bool)d["DisableFollowRotation"]||
                (float)d["PositionDamping"]!=0||(float)d["RotationDamping"]!=0||
                (float)d["PitchAngle"]!=0||(float)d["YawAngle"]!=0||(float)d["RollAngle"]!=0)
                throw new ArgumentException("Dash layout no longer fits the supported host policy");
            baseOffset=follow.FollowOffset;baseAim=composer.TargetOffset;baseFov=camera.Lens.FieldOfView;
            if(baseOffset.sqrMagnitude<.01f)throw new ArgumentException("Missing camera orbit offset");
            var viewRotation=Quaternion.LookRotation(-baseOffset.normalized,Vector3.up);
            targetShift=viewRotation*new Vector3((float)d["CameraOffset"][0],(float)d["CameraOffset"][1],0);
            targetOffset=baseOffset.normalized*(float)d["Radius"]+targetShift;
            targetFov=(float)d["FieldOfView"];inDuration=(float)d["BlendInDuration"];outDuration=(float)d["BlendOutDuration"];
            inCurve=ReadCurve(p["curves"][(string)d["BlendInCurveKey"]]["curve"]);
            outCurve=ReadCurve(p["curves"][(string)d["BlendOutCurveKey"]]["curve"]);
            currentOffset=baseOffset;currentFov=baseFov;
            events.Emitted+=OnEvent;events.Released+=OnReleased;
        }
        public static AnimationCurve ReadCurve(JToken data)
        {
            var keys=data["keys"].Select(k=>new Keyframe((float)k["time"],(float)k["value"],
                (float)k["inTangent"],(float)k["outTangent"],(float)k["inWeight"],(float)k["outWeight"])
                {weightedMode=(WeightedMode)(int)k["weightedMode"]}).ToArray();
            if(keys.Length<2||keys[0].time!=0||keys[keys.Length-1].time!=1||keys[0].value!=0||keys[keys.Length-1].value!=1)
                throw new ArgumentException("Camera blend curve must cover normalized endpoints");
            // The host only samples [0,1]. Serialized infinity enum metadata is
            // retained, rather than incorrectly casting it to Unity WrapMode.
            return new AnimationCurve(keys){preWrapMode=WrapMode.ClampForever,postWrapMode=WrapMode.ClampForever};
        }
        void OnEvent(SourceFrameEvents.Emission e)
        {
            bool enter=e.Type=="AnimatorEventEnterAvatarLayoutCameraEntry";
            if(!enter&&e.Type!="AnimatorEventExitAvatarLayoutCameraEntry")return;
            if((string)e.Fields["CameraKey"]!=DashKey){UnsupportedEvents++;return;}
            if(enter)
            {
                if(owner==e.Generation)return;
                if(owner!=0){Releases++;ForcedReleases++;}
                owner=e.Generation;Activations++;
                Blend(targetOffset,targetShift,targetFov,inDuration,inCurve);
            }
            else if(owner==e.Generation)Release(e.Reason!="frame");
        }
        void OnReleased(long generation,string reason){if(owner==generation)Release(true);}
        void Release(bool forced)
        {
            owner=0;Releases++;if(forced)ForcedReleases++;
            Blend(baseOffset,Vector3.zero,baseFov,outDuration,outCurve);
        }
        void Blend(Vector3 offset,Vector3 shift,float fov,float seconds,AnimationCurve curve)
        {
            // Capture the displayed state, including an interrupted blend.
            startOffset=currentOffset;startShift=currentShift;startFov=currentFov;
            endOffset=offset;endShift=shift;endFov=fov;duration=seconds;blendCurve=curve;elapsed=0;transitioning=true;
            if(seconds<=0)Advance(0);
        }
        public void Advance(float delta)
        {
            if(disposed)throw new ObjectDisposedException(nameof(SourceLayoutCamera));
            if(!float.IsFinite(delta)||delta<0)throw new ArgumentOutOfRangeException(nameof(delta));
            if(transitioning)
            {
                elapsed+=delta;
                if(elapsed+1e-7>=duration)
                {currentOffset=endOffset;currentShift=endShift;currentFov=endFov;transitioning=false;}
                else
                {
                    float t=blendCurve.Evaluate((float)(elapsed/duration));
                    currentOffset=Vector3.LerpUnclamped(startOffset,endOffset,t);
                    currentShift=Vector3.LerpUnclamped(startShift,endShift,t);
                    currentFov=Mathf.LerpUnclamped(startFov,endFov,t);
                }
            }
            follow.FollowOffset=currentOffset;camera.Lens.FieldOfView=currentFov;
            // Composer offset is target-local; maintain the authored preview
            // world direction when camera-relative input turns the actor.
            composer.TargetOffset=baseAim+camera.LookAt.InverseTransformVector(currentShift);
        }
        public void Dispose()
        {
            if(disposed)return;
            events.Emitted-=OnEvent;events.Released-=OnReleased;
            if(owner!=0){owner=0;Releases++;ForcedReleases++;}
            transitioning=false;currentOffset=baseOffset;currentShift=Vector3.zero;currentFov=baseFov;
            if(follow)follow.FollowOffset=baseOffset;if(composer)composer.TargetOffset=baseAim;if(camera)camera.Lens.FieldOfView=baseFov;
            disposed=true;
        }
    }
}
