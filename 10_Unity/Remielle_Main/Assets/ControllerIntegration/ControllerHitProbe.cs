using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Remielle.Controller
{
    [DefaultExecutionOrder(-300)]
    public sealed class ControllerHitProbe : MonoBehaviour
    {
        public RemielleSourceController controller;
        public string reportDirectory;
        public bool completed,passed;
        Keyboard keyboard;Mouse mouse;
        InputSettings originalSettings,testSettings;
        float originalCaptureDelta;
        int frame,phase,phaseFrame,prepared;
        bool entered,captured;
        int feedbackPixels;
        float feedbackPositionDelta;
        bool expectedFrozen;
        int frozenChecks;
        int frozenParticleChecks;
        SourceHitParticles.Sample? frozenParticle;
        readonly JObject particlePixels=new JObject();
        float frozenActionFrame;
        double frozenInputClock;
        Vector3 frozenMotor;
        Vector3[] frozenBones;
        double probeStarted;
        SourceTrainingTarget first,second,outside;
        readonly JArray receipts=new JArray();
        void Start()
        {
            originalSettings=InputSystem.settings;testSettings=Instantiate(originalSettings);InputSystem.settings=testSettings;
            testSettings.updateMode=InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
            testSettings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            originalCaptureDelta=Time.captureDeltaTime;Time.captureDeltaTime=1f/60;
            keyboard=InputSystem.AddDevice<Keyboard>("HitProbeKeyboard");mouse=InputSystem.AddDevice<Mouse>("HitProbeMouse");controller.RestrictInputDevices(keyboard,mouse);
            prepared=controller.Session.PreparedSamplerCount;
            probeStarted=Time.realtimeSinceStartupAsDouble;
            frozenBones=new Vector3[controller.driver.bones.Length];
            foreach(var target in FindObjectsByType<SourceTrainingTarget>(FindObjectsSortMode.None))target.gameObject.SetActive(false);
            first=SourceTrainingTarget.Create("HitFixtureA",new Vector3(100,1,0));second=SourceTrainingTarget.Create("HitFixtureB",new Vector3(100,1,1));
            outside=SourceTrainingTarget.Create("HitFixtureOutside",new Vector3(150,1,0));
            var extra=new GameObject("DuplicateHurtbox");extra.layer=SourceHitQuery.TargetLayer;extra.transform.SetParent(first.transform,false);extra.AddComponent<SphereCollider>().isTrigger=true;
            controller.HitQuery.Hit+=OnHit;
        }
        void OnHit(SourceHitQuery.Receipt e)=>receipts.Add(new JObject{["state"]=e.State,["entry"]=e.Entry,["frame"]=e.Frame,["target"]=e.Target.name,["generation"]=e.Generation,["key"]=e.Key});
        void Update()
        {
            if(completed)return;
            try
            {
                frame++;phaseFrame++;
                if(!controller.Ready)throw new Exception(controller.Error??"Controller not ready");
                if(Time.realtimeSinceStartupAsDouble-probeStarted>60)throw new Exception("Hit probe timed out in phase "+phase);
                var session=controller.Session;
                if(expectedFrozen)
                {
                    if(session.ActionFrames!=frozenActionFrame||controller.motor.transform.position!=frozenMotor)throw new Exception("Hitstop advanced action clock or motor");
                    if(controller.InputBuffer.Clock!=frozenInputClock)throw new Exception("Hitstop consumed input buffer lifetime");
                    for(int i=0;i<frozenBones.Length;i++)if((controller.driver.bones[i].target.position-frozenBones[i]).sqrMagnitude>1e-10f)throw new Exception("Hitstop changed a sampled bone pose");
                    frozenChecks++;
                    if(frozenParticle.HasValue)
                    {
                        var previous=frozenParticle.Value;
                        foreach(var now in controller.HitParticles.Snapshot())if(now.Root==previous.Root)
                        {if(now.Age<=previous.Age)throw new Exception("Owner hitstop also froze world-clock particles");frozenParticleChecks++;}
                    }
                }
                expectedFrozen=controller.HitStop.RemainingSeconds>=Time.deltaTime-1e-8&&controller.HitStop.RemainingSeconds>0;
                if(expectedFrozen)
                {
                    var particles=controller.HitParticles.Snapshot();frozenParticle=particles.Count>0?particles[0]:(SourceHitParticles.Sample?)null;
                    frozenActionFrame=session.ActionFrames;frozenMotor=controller.motor.transform.position;
                    frozenInputClock=controller.InputBuffer.Clock;
                    for(int i=0;i<frozenBones.Length;i++)frozenBones[i]=controller.driver.bones[i].target.position;
                }
                if(session.PreparedSamplerCount!=prepared)throw new Exception("Unexpected runtime motion loading");
                foreach(var bone in controller.driver.bones)
                    if(!float.IsFinite(bone.target.position.x)||!float.IsFinite(bone.target.position.y)||!float.IsFinite(bone.target.position.z))throw new Exception("Nonfinite hit probe pose");
                if(session.CurrentState!=32||session.IsBlending)entered=true;
                if(entered&&session.CurrentState==32&&!session.IsBlending)
                {
                    if(phase==0&&controller.HitQuery.Hits!=0)throw new Exception("Miss produced a target hit");
                    if(phase==0&&controller.HitFeedback.Activations!=0)throw new Exception("Miss produced hit shake");
                    if(phase==0&&controller.HitStop.Requests!=0)throw new Exception("Miss produced hitstop");
                    if(phase==0&&controller.HitParticles.Activations!=0)throw new Exception("Miss produced hit particles");
                    if(phase==1&&(first.Hits!=2||second.Hits!=2))throw new Exception("Normal attack must hit each owner twice");
                    if(phase==2&&(first.Hits!=11||second.Hits!=11))throw new Exception("EX attack must add nine hits per owner");
                    if(phase==3)
                    {
                        if(first.Hits!=15||second.Hits!=15||outside.Hits!=0)throw new Exception("Normal special count or out-of-range filtering differs");
                        if(controller.HitFeedback.ActiveCount>0||controller.HitParticles.ActiveCount>0)return;
                        Finish(null);return;
                    }
                    phase++;phaseFrame=1;entered=false;
                }
                // Moving hurtboxes are a test fixture only. The ordinary
                // preview targets remain fixed in world space.
                if(phase>0)
                {
                    first.transform.position=controller.motor.transform.TransformPoint(new Vector3(-.8f,1,2));
                    second.transform.position=controller.motor.transform.TransformPoint(new Vector3(.8f,1,2));
                }
                if(controller.HitQuery.Hits>0&&!captured){CaptureFeedbackComparison();captured=true;}
                foreach(var particle in controller.HitParticles.Snapshot())
                    if(particle.Age<.08f&&particle.Particles>0&&particlePixels[particle.Key]==null)CaptureParticleComparison(particle.Key);
                bool attack=phase<2&&phaseFrame==1,special=phase>=2&&phaseFrame==1;
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(special?new[]{Key.E}:Array.Empty<Key>()));
                InputSystem.QueueStateEvent(mouse,new MouseState().WithButton(MouseButton.Left,attack));
            }
            catch(Exception ex){Finish(ex.ToString());}
        }
        void Finish(string error)
        {
            if(error==null&&(controller.HitQuery.UnsupportedEvents!=0||controller.SpecialResource.Energy!=0||controller.SpecialResource.Charges!=1||!captured))error="Unexpected unsupported event/resource/capture result";
            var feedback=controller.HitFeedback;
            if(error==null&&(feedback.Activations!=15||feedback.Completed!=15||feedback.Cancelled!=0||feedback.MissesSkipped!=2||feedback.UnsupportedKeys!=0||feedback.ActiveCount!=0))error="Hit feedback lifecycle/count differs";
            if(error==null&&(feedbackPixels<20||feedbackPositionDelta<.00001f))error="Hit feedback did not change actual camera/GPU output";
            var stop=controller.HitStop;
            var particles=controller.HitParticles;
            var damage=controller.TrainingDamage;
            if(error==null&&(damage.Applications!=30||damage.TotalApplied!=30||damage.TotalStagger!=30||
                first.Health!=85||second.Health!=85||outside.Health!=100||first.StaggerTaken!=15||second.StaggerTaken!=15))
                error="Fixed one-point training damage or stagger differs";
            if(error==null&&(controller.SpecialResource.NormalSpecialUses!=1||controller.SpecialResource.NormalSpecialsTowardCharge!=1||controller.SpecialResource.Recharges!=0))
                error="Normal special recharge counted hits instead of casts";
            if(error==null&&(particles.Activations!=30||particles.Completed!=30||particles.Cancelled!=0||particles.Unsupported!=0||particles.ActiveCount!=0||particles.Reused==0||frozenParticleChecks==0))error="Hit particle lifecycle/count/clock differs";
            if(error==null&&particles.Created!=particles.Prewarmed)error="Ordinary hit flow exceeded prewarmed particle pool";
            if(error==null&&(particlePixels.Count!=2||particlePixels.Properties().Any(p=>(int)p.Value<20)))error="Hit particle GPU comparison missing or invisible";
            if(error==null&&(stop.Requests!=11||stop.ZeroFrameHits!=4||Math.Abs(stop.ConsumedSeconds-.5)>1e-6||stop.RemainingSeconds!=0||frozenChecks<15))error="Actor hitstop duration/zero-frame/pose verification differs";
            completed=true;passed=error==null;controller.readDevices=false;Directory.CreateDirectory(reportDirectory);
            File.WriteAllText(Path.Combine(reportDirectory,"controller-hit-verification.json"),new JObject{
                ["pass"]=passed,["error"]=error,["frames"]=frame,["hits"]=controller.HitQuery.Hits,["queries"]=controller.HitQuery.Queries,["misses"]=controller.HitQuery.Misses,
                ["firstTargetHits"]=first.Hits,["secondTargetHits"]=second.Hits,["outsideTargetHits"]=outside.Hits,["captured"]=captured,
                ["virtualInputSystemDevices"]=true,["directParameterInjection"]=false,["movingTargetFixture"]=true,
                ["preparedSamplers"]=prepared,["finalSamplers"]=controller.Session.PreparedSamplerCount,["receipts"]=receipts,
                ["hitFeedbackActivations"]=feedback.Activations,["hitFeedbackCompleted"]=feedback.Completed,["hitFeedbackCancelled"]=feedback.Cancelled,
                ["hitFeedbackMissesSkipped"]=feedback.MissesSkipped,["hitFeedbackActiveAtEnd"]=feedback.ActiveCount,
                ["hitFeedbackGpuChangedPixels"]=feedbackPixels,["hitFeedbackCameraPositionDelta"]=feedbackPositionDelta,
                ["hitstopRequests"]=stop.Requests,["hitstopZeroFrameHits"]=stop.ZeroFrameHits,["hitstopConsumedSeconds"]=stop.ConsumedSeconds,
                ["hitstopRemaining"]=stop.RemainingSeconds,["frozenPoseChecks"]=frozenChecks,
                ["hitParticleActivations"]=particles.Activations,["hitParticleCompleted"]=particles.Completed,["hitParticleCancelled"]=particles.Cancelled,
                ["hitParticleCreated"]=particles.Created,["hitParticleReused"]=particles.Reused,["hitParticleActiveAtEnd"]=particles.ActiveCount,
                ["hitParticlePrewarmed"]=particles.Prewarmed,["hitParticlePreviewScale"]=controller.hitParticleScale,
                ["hitParticleGpuChangedPixels"]=particlePixels,["hitParticleAdvancedDuringHitstopChecks"]=frozenParticleChecks,
                ["trainingDamage"]=damage.TotalApplied,["trainingStagger"]=damage.TotalStagger,["firstTargetHealth"]=first.Health,["secondTargetHealth"]=second.Health,
                ["normalSpecialUses"]=controller.SpecialResource.NormalSpecialUses,["chargeProgress"]=controller.SpecialResource.NormalSpecialsTowardCharge,
                ["projectRechargeImplemented"]=true,["nativeEnergyRecoveryImplemented"]=false,["nativeCoordinateParity"]=false}.ToString());
            if(error!=null)Debug.LogError(error);
        }
        void CaptureFeedbackComparison()
        {
            if(controller.ActionShake.Activations!=0)throw new Exception("Hit GPU comparison includes an unrelated action shake");
            var listener=controller.layoutCamera.GetComponent<Unity.Cinemachine.CinemachineImpulseListener>();float gain=listener.Gain;
            try
            {
                controller.layoutCamera.InternalUpdateCameraState(Vector3.up,0);Capture("controller-hit.png");var position=controller.viewCamera.transform.position;
                listener.Gain=0;controller.layoutCamera.InternalUpdateCameraState(Vector3.up,0);Capture("controller-hit-feedback-off.png");
                feedbackPositionDelta=Vector3.Distance(position,controller.viewCamera.transform.position);
                var on=new Texture2D(2,2);var off=new Texture2D(2,2);
                try
                {
                    on.LoadImage(File.ReadAllBytes(Path.Combine(reportDirectory,"controller-hit.png")));off.LoadImage(File.ReadAllBytes(Path.Combine(reportDirectory,"controller-hit-feedback-off.png")));
                    var a=on.GetPixels32();var b=off.GetPixels32();for(int i=0;i<a.Length;i++)if(Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b)>3)feedbackPixels++;
                }
                finally{Destroy(on);Destroy(off);}
            }
            finally{listener.Gain=gain;controller.layoutCamera.InternalUpdateCameraState(Vector3.up,0);controller.brain.ManualUpdate();}
        }
        void CaptureParticleComparison(string key)
        {
            controller.brain.ManualUpdate();string onName="controller-particles-"+key+".png",offName="controller-particles-"+key+"-off.png";
            var on=new Texture2D(2,2);var off=new Texture2D(2,2);
            try
            {
                Capture(onName,false);controller.HitParticles.SetRenderingEnabled(false);Capture(offName,false);
                on.LoadImage(File.ReadAllBytes(Path.Combine(reportDirectory,onName)));off.LoadImage(File.ReadAllBytes(Path.Combine(reportDirectory,offName)));
                var a=on.GetPixels32();var b=off.GetPixels32();int changed=0;
                for(int i=0;i<a.Length;i++)if(Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b)>3)changed++;
                particlePixels[key]=changed;
            }
            finally{controller.HitParticles.SetRenderingEnabled(true);Destroy(on);Destroy(off);}
        }
        void Capture(string name,bool updateCamera=true)
        {
            Directory.CreateDirectory(reportDirectory);if(updateCamera)controller.brain.ManualUpdate();var camera=controller.viewCamera;
            var old=camera.targetTexture;var active=RenderTexture.active;var rt=RenderTexture.GetTemporary(1280,720,24);var texture=new Texture2D(1280,720,TextureFormat.RGB24,false);
            try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;texture.ReadPixels(new Rect(0,0,1280,720),0,0);texture.Apply();File.WriteAllBytes(Path.Combine(reportDirectory,name),texture.EncodeToPNG());}
            finally{camera.targetTexture=old;RenderTexture.active=active;RenderTexture.ReleaseTemporary(rt);Destroy(texture);}
        }
        void OnDestroy()
        {
            if(controller&&controller.HitQuery!=null)controller.HitQuery.Hit-=OnHit;
            if(keyboard!=null)InputSystem.RemoveDevice(keyboard);if(mouse!=null)InputSystem.RemoveDevice(mouse);
            if(originalSettings)InputSystem.settings=originalSettings;if(testSettings)Destroy(testSettings);
            Time.captureDeltaTime=originalCaptureDelta;
        }
    }
}
