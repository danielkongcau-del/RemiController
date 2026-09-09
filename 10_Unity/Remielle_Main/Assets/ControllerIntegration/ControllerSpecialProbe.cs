using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Remielle.ControllerRuntime;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Remielle.Controller
{
    [DefaultExecutionOrder(-300)]
    public sealed class ControllerSpecialProbe : MonoBehaviour
    {
        public RemielleSourceController controller;
        public string reportDirectory;
        public bool completed,passed;
        Keyboard keyboard;Mouse mouse;
        InputSettings originalSettings,testSettings;
        float originalCaptureDelta;
        int frame,phase,phaseFrame,prepared;
        bool phaseEntered,verifiedRechargeCycle;
        bool sawEx,sawExEnd,sawNormal,sawNormalEnd,sawEvade,sawMovingSpecial,capturedEx,capturedNormal;
        string[] names;
        int skillChangedPixels,peakSkillParticles;bool capturedFlash;
        int burstChangedPixels,peakBurstParticles;bool capturedBurst;
        int smokeChangedPixels;bool capturedSmoke;
        int trailChangedPixels;bool capturedTrail;
        readonly JArray faceChecks=new();
        readonly JArray hdrChecks=new();
        void Start()
        {
            originalSettings=InputSystem.settings;testSettings=Instantiate(originalSettings);InputSystem.settings=testSettings;
            testSettings.updateMode=InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;testSettings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            originalCaptureDelta=Time.captureDeltaTime;Time.captureDeltaTime=1f/60f;
            keyboard=InputSystem.AddDevice<Keyboard>("SpecialProbeKeyboard");mouse=InputSystem.AddDevice<Mouse>("SpecialProbeMouse");controller.RestrictInputDevices(keyboard,mouse);
            prepared=controller.Session.PreparedSamplerCount;
            names=new NativeControllerSource(controller.sourcePack.text).GetController("Avatar_Female_Size02_RemielleOrigin_Controller")["machines"][0]["states"].Select(s=>(string)s["name"]).ToArray();
        }
        int NormalEntries=>controller.EventTrace.Count(e=>e["entry"]!=null&&(int)e["state"]==54&&(int)e["entry"]==4);
        void Next(){phase++;phaseFrame=0;phaseEntered=false;}
        void Update()
        {
            if(completed)return;
            try
            {
                frame++;phaseFrame++;
                if(!controller.Ready)throw new Exception(controller.Error??"Controller not ready");
                if(frame>4200)throw new Exception("Special input timed out in phase "+phase);
                var s=controller.Session;string state=names[s.CurrentState];
                if(frame==5&&Environment.GetCommandLineArgs().Contains("--controller-presentation-probe"))CaptureFace("idle");
                if(controller.SkillParticles!=null)
                {
                    peakSkillParticles=Math.Max(peakSkillParticles,controller.SkillParticles.ParticleCount);
                    peakBurstParticles=Math.Max(peakBurstParticles,controller.SkillParticles.ParticleCountFor("burst"));
                    if(!capturedFlash&&state=="Attack_Special"&&s.ActionFrames>=14&&controller.SkillParticles.ParticleCount>0)
                    {
                        var on=Capture("controller-special-flash-on.png");
                        controller.SkillParticles.SetRenderingEnabled(false);
                        Color32[] off;
                        try
                        {
                            off=Capture("controller-special-flash-off.png",false);
                            var repeat=Capture("controller-special-flash-off-repeat.png",false);
                            if(Enumerable.Range(0,off.Length).Any(i=>!off[i].Equals(repeat[i])))throw new Exception("GPU comparison baseline changed between renders");
                        }
                        finally{controller.SkillParticles.SetRenderingEnabled(true);}
                        skillChangedPixels=Enumerable.Range(0,on.Length).Count(i=>!on[i].Equals(off[i]));capturedFlash=true;
                        if(skillChangedPixels<5)throw new Exception("Attached skill flash not visible on actual GPU frame");
                    }
                    if(!capturedSmoke&&state=="Attack_Special"&&s.ActionFrames>=23&&controller.SkillParticles.ParticleCountFor("smoke")>0)
                    {
                        var on=Capture("controller-special-smoke-on.png");Color32[] off;
                        controller.SkillParticles.SetKindRenderingEnabled("smoke",false);
                        try{off=Capture("controller-special-smoke-off.png",false);}
                        finally{controller.SkillParticles.SetKindRenderingEnabled("smoke",true);}
                        smokeChangedPixels=Enumerable.Range(0,on.Length).Count(i=>!on[i].Equals(off[i]));capturedSmoke=true;
                        if(smokeChangedPixels<5)throw new Exception("Smoke not visible on Player GPU frame");
                    }
                    if(!capturedTrail&&state=="Attack_Special"&&s.ActionFrames>=23&&controller.SkillParticles.ParticleCountFor("trail")>0)
                    {
                        var on=Capture("controller-special-trail-on.png");Color32[] off;
                        controller.SkillParticles.SetKindRenderingEnabled("trail",false);
                        try{off=Capture("controller-special-trail-off.png",false);}
                        finally{controller.SkillParticles.SetKindRenderingEnabled("trail",true);}
                        trailChangedPixels=Enumerable.Range(0,on.Length).Count(i=>!on[i].Equals(off[i]));capturedTrail=true;
                        if(trailChangedPixels<5)throw new Exception("Trail not visible on Player GPU frame");
                    }
                    if(!capturedBurst&&state=="Attack_Special"&&s.ActionFrames>=27&&controller.SkillParticles.ParticleCountFor("burst")>0)
                    {
                        var isolated=controller.SkillParticles.ActiveRoots.Where(r=>r.name.Contains("Special_02_Burst")).SelectMany(r=>r.GetComponentsInChildren<ParticleSystemRenderer>()).Where(r=>r.enabled&&r.GetComponent<ParticleSystem>().particleCount>0).ToArray();
                        var on=Capture("controller-special-burst-on.png");controller.SkillParticles.SetKindRenderingEnabled("burst",false);
                        Color32[] off;
                        try
                        {
                            off=Capture("controller-special-burst-off.png",false);
                            var repeat=Capture("controller-special-burst-off-repeat.png",false);
                            if(Enumerable.Range(0,off.Length).Any(i=>!off[i].Equals(repeat[i])))throw new Exception("Burst GPU baseline changed");
                            if(Environment.GetCommandLineArgs().Contains("--controller-burst-isolation"))
                            {
                                var rows=new JArray();int n=0;
                                foreach(var renderer in isolated)
                                {
                                    renderer.enabled=true;string file="burst-isolate-"+(n++)+".png";
                                    Color32[] pixels;try{pixels=Capture(file,false);}finally{renderer.enabled=false;}
                                    int white=Enumerable.Range(0,pixels.Length).Count(i=>pixels[i].r>250&&pixels[i].g>250&&pixels[i].b>250&&(off[i].r<=250||off[i].g<=250||off[i].b<=250));
                                    rows.Add(new JObject{["file"]=file,["node"]=renderer.name,["material"]=renderer.sharedMaterial.name,["addedWhitePixels"]=white});
                                }
                                File.WriteAllText(Path.Combine(reportDirectory,"burst-isolation.json"),rows.ToString());
                            }
                        }
                        finally{controller.SkillParticles.SetKindRenderingEnabled("burst",true);}
                        burstChangedPixels=Enumerable.Range(0,on.Length).Count(i=>!on[i].Equals(off[i]));capturedBurst=true;
                        if(burstChangedPixels<5)throw new Exception("Burst not visible on Player GPU frame");
                        if(Environment.GetCommandLineArgs().Contains("--controller-presentation-probe"))CaptureFace("special");
                    }
                }
                if(state!="Idle")phaseEntered=true;
                if(s.PreparedSamplerCount!=prepared)throw new Exception("Special input loaded an unprepared source motion");
                foreach(var bone in controller.driver.bones)
                    if(!float.IsFinite(bone.target.position.x)||!float.IsFinite(bone.target.position.y)||!float.IsFinite(bone.target.position.z))throw new Exception("Special motion produced a nonfinite bone pose");
                sawEx|=state=="Attack_ExSpecial";sawExEnd|=state=="Attack_ExSpecial_End";
                sawNormal|=state=="Attack_Special";sawNormalEnd|=state=="Attack_Special_End";
                if(state=="Attack_ExSpecial"&&s.ActionFrames<14&&controller.SpecialResource.Energy!=60)throw new Exception("EX charged before its authored frame");
                if(state=="Attack_ExSpecial"&&s.ActionFrames>=14&&controller.SpecialResource.Energy!=0)throw new Exception("EX did not charge at its authored frame");
                if(state=="Attack_ExSpecial"&&s.ActionFrames>=28&&!capturedEx){Capture("controller-special-ex.png");capturedEx=true;}
                if(state=="Attack_Special"&&s.ActionFrames>=25&&!capturedNormal){Capture("controller-special-normal.png");capturedNormal=true;}
                if(phase<=3&&phaseEntered&&state=="Idle"&&!s.IsBlending)
                {
                    var resource=controller.SpecialResource;
                    if(phase==0&&(resource.Charges!=1||resource.Energy!=0))throw new Exception("Initial EX charge failed");
                    if(phase==1&&(resource.NormalSpecialUses!=1||resource.NormalSpecialsTowardCharge!=1||resource.Energy!=0))throw new Exception("First normal special did not retain one cast of progress");
                    if(phase==2&&(resource.NormalSpecialUses!=2||resource.Recharges!=1||resource.Energy!=60||resource.NormalSpecialsTowardCharge!=0))throw new Exception("Two normal specials did not restore exactly one EX charge");
                    if(phase==3)
                    {
                        if(resource.Charges!=2||resource.TotalSpent!=120||resource.Energy!=0)throw new Exception("Recharged EX was not selectable through actual E input");
                        verifiedRechargeCycle=true;
                    }
                    Next();
                }
                else if(phase==4&&NormalEntries>=4&&state=="Attack_Special"&&s.ActionFrames>=10)Next();
                else if(phase==5)
                {
                    sawEvade|=state.StartsWith("Evade_");
                    if(sawEvade&&state=="Idle"&&!s.IsBlending)Next();
                }
                else if(phase==6&&state=="Walk_Loop"&&!s.IsBlending)Next();
                else if(phase==7)
                {
                    sawMovingSpecial|=state=="Attack_Special"||state=="Attack_ExSpecial";
                    if(sawMovingSpecial&&state=="Idle"&&!s.IsBlending&&(controller.SkillParticles==null||controller.SkillParticles.ActiveCount==0)){Finish(null);return;}
                }
                bool special=phase==0&&frame==1||(phase==1||phase==2||phase==3||phase==7)&&phaseFrame==0||phase==4;
                bool moving=phase==6;bool evade=phase==5&&phaseFrame==0;
                var keys=new System.Collections.Generic.List<Key>();if(special)keys.Add(Key.E);if(moving)keys.Add(Key.W);if(evade)keys.Add(Key.Space);
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys.ToArray()));InputSystem.QueueStateEvent(mouse,new MouseState());
            }
            catch(Exception ex){Finish(ex.ToString());}
        }
        void Finish(string error)
        {
            var r=controller.SpecialResource;var s=controller.Session;
            if(error==null)
            {
                if(!sawEx||!sawExEnd||!sawNormal||!sawNormalEnd||!sawEvade||!sawMovingSpecial||!capturedEx||!capturedNormal)error="Special input coverage incomplete";
                else if(!verifiedRechargeCycle||r.NormalSpecialsPerCharge!=2||r.TotalSpent!=r.Charges*60||r.Energy!=60+r.TotalRestored-r.TotalSpent)error="Resource lifecycle differs";
                else if(!s.Trace.Any(t=>(string)t["event"]=="transition-start"&&(int)t["sourceState"]==54&&(int)t["targetState"]==54&&(float)t["sourceFrames"]>=36))error="Held E did not use the authored self-transition gate";
                else if(controller.ActionEvents.ActiveScopes!=1||controller.ActionShake.ActiveCount!=0||!controller.ActionCamera.AtBaseline)error="Skill presentation owners did not return to idle";
                else if(controller.WeaponTrail.ActiveOwners!=0||controller.LeftWeaponTrail.ActiveOwners!=0||controller.LargeWeaponTrail.ActiveOwners!=0)error="Special input left unrelated weapon owners";
                else if(controller.SkillParticles!=null&&(!capturedFlash||controller.SkillParticles.ActiveCount!=0||controller.SkillParticles.Activations!=controller.SkillParticles.Completed+controller.SkillParticles.Cancelled||controller.SkillParticles.Created!=controller.SkillParticles.Prewarmed))error="Special flash coverage or cleanup failed";
                else if(controller.SkillParticles!=null&&(!capturedSmoke||controller.SkillParticles.SmokeActivations<4||controller.SkillParticles.SmokeCompleted!=controller.SkillParticles.SmokeActivations||controller.SkillParticles.SmokeCancelled!=0))error="Smoke coverage or completion failed";
                else if(controller.SkillParticles!=null&&(!capturedTrail||controller.SkillParticles.TrailActivations<4||controller.SkillParticles.TrailCompleted!=controller.SkillParticles.TrailActivations||controller.SkillParticles.TrailCancelled!=0))error="Trail coverage or completion failed";
                else if(controller.SkillParticles!=null&&(!capturedBurst||controller.SkillParticles.BurstActivations<4||controller.SkillParticles.BurstCompleted!=controller.SkillParticles.BurstActivations||controller.SkillParticles.BurstCancelled!=0))error="Detached burst coverage or completion failed";
            }
            completed=true;passed=error==null;controller.readDevices=false;Directory.CreateDirectory(reportDirectory);
            File.WriteAllText(Path.Combine(reportDirectory,"controller-special-verification.json"),new JObject{
                ["pass"]=passed,["error"]=error,["frames"]=frame,["virtualInputSystemDevices"]=true,["directParameterInjection"]=false,
                ["prewarmedSamplers"]=prepared,["finalSamplers"]=s.PreparedSamplerCount,["charges"]=r?.Charges,["totalSpent"]=r?.TotalSpent,["energyAtEnd"]=r?.Energy,
                ["normalSkillEntries"]=NormalEntries,["capturedEx"]=capturedEx,["capturedNormal"]=capturedNormal,
                ["shakeActivations"]=controller.ActionShake?.Activations,["shakeCompleted"]=controller.ActionShake?.Completed,["shakeCancelled"]=controller.ActionShake?.Cancelled,
                ["activeEventScopes"]=controller.ActionEvents?.ActiveScopes,["trace"]=new JArray(s.Trace),["actionEvents"]=new JArray(controller.EventTrace),
                ["verifiedRechargeCycle"]=verifiedRechargeCycle,["normalSpecialUses"]=r.NormalSpecialUses,["normalSpecialsPerCharge"]=r.NormalSpecialsPerCharge,
                ["recharges"]=r.Recharges,["totalRestored"]=r.TotalRestored,["chargeProgress"]=r.NormalSpecialsTowardCharge,
                ["flashActivations"]=controller.SkillParticles?.Activations-controller.SkillParticles?.BurstActivations-controller.SkillParticles?.SmokeActivations-controller.SkillParticles?.TrailActivations,["flashCompleted"]=controller.SkillParticles?.Completed-controller.SkillParticles?.BurstCompleted-controller.SkillParticles?.SmokeCompleted-controller.SkillParticles?.TrailCompleted,["flashCancelled"]=controller.SkillParticles?.Cancelled-controller.SkillParticles?.BurstCancelled-controller.SkillParticles?.SmokeCancelled-controller.SkillParticles?.TrailCancelled,
                ["burstActivations"]=controller.SkillParticles?.BurstActivations,["burstCompleted"]=controller.SkillParticles?.BurstCompleted,["burstCancelled"]=controller.SkillParticles?.BurstCancelled,
                ["smokeActivations"]=controller.SkillParticles?.SmokeActivations,["smokeCompleted"]=controller.SkillParticles?.SmokeCompleted,["smokeCancelled"]=controller.SkillParticles?.SmokeCancelled,["smokeGpuChangedPixels"]=smokeChangedPixels,
                ["trailActivations"]=controller.SkillParticles?.TrailActivations,["trailCompleted"]=controller.SkillParticles?.TrailCompleted,["trailCancelled"]=controller.SkillParticles?.TrailCancelled,["trailGpuChangedPixels"]=trailChangedPixels,
                ["burstGpuChangedPixels"]=burstChangedPixels,["peakBurstParticles"]=peakBurstParticles,
                ["flashCreated"]=controller.SkillParticles?.Created,["flashPrewarmed"]=controller.SkillParticles?.Prewarmed,["flashActiveAtEnd"]=controller.SkillParticles?.ActiveCount,
                ["flashGpuChangedPixels"]=skillChangedPixels,["peakFlashParticles"]=peakSkillParticles,["flashAttachmentError"]=controller.SkillParticles?.MaximumAttachmentError,
                ["hitDetectionImplemented"]=controller.HitQuery!=null,["projectRechargeImplemented"]=true,["nativeEnergyRecoveryImplemented"]=false,["fullSkillVfxImplemented"]=false}.ToString());
            if(error!=null)Debug.LogError(error);
        }
        Color32[] Capture(string name,bool updateCamera=true)
        {
            Directory.CreateDirectory(reportDirectory);if(updateCamera)controller.brain.ManualUpdate();var camera=controller.viewCamera;
            var old=camera.targetTexture;var active=RenderTexture.active;var rt=RenderTexture.GetTemporary(1280,720,24);var texture=new Texture2D(1280,720,TextureFormat.RGB24,false);
            ControllerHdrProbeTap tap=null;
            try
            {
                camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;texture.ReadPixels(new Rect(0,0,1280,720),0,0);texture.Apply();
                File.WriteAllBytes(Path.Combine(reportDirectory,name),texture.EncodeToPNG());var original=texture.GetPixels32();
                if(Environment.GetCommandLineArgs().Contains("--controller-hdr-probe"))
                {
                    tap=camera.gameObject.AddComponent<ControllerHdrProbeTap>();tap.Observe=source=>CaptureHdr(source,name);
                    camera.Render();RenderTexture.active=rt;texture.ReadPixels(new Rect(0,0,1280,720),0,0);texture.Apply();
                    var observed=texture.GetPixels32();int changed=original.Zip(observed,(a,b)=>a.Equals(b)?0:1).Sum();
                    ((JObject)hdrChecks.Last)["tapRerenderChangedPixels"]=changed;
                    File.WriteAllText(Path.Combine(reportDirectory,"hdr-verification.json"),hdrChecks.ToString());
                }
                return original;
            }
            finally{if(tap){tap.enabled=false;Destroy(tap);}camera.targetTexture=old;RenderTexture.active=active;RenderTexture.ReleaseTemporary(rt);Destroy(texture);}
        }
        void CaptureHdr(RenderTexture source,string name)
        {
            var active=RenderTexture.active;
            var copy=RenderTexture.GetTemporary(source.width,source.height,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            var cpu=new Texture2D(source.width,source.height,TextureFormat.RGBAFloat,false,true);
            var review=new Texture2D(source.width,source.height,TextureFormat.RGB24,false,false);
            try
            {
                Graphics.Blit(source,copy);RenderTexture.active=copy;
                cpu.ReadPixels(new Rect(0,0,source.width,source.height),0,0);cpu.Apply();
                var pixels=cpu.GetPixels();int aboveOne=0,allAboveOne=0,invalid=0;float maximum=0;
                for(int i=0;i<pixels.Length;i++)
                {
                    var c=pixels[i];if(!float.IsFinite(c.r)||!float.IsFinite(c.g)||!float.IsFinite(c.b)){invalid++;continue;}
                    float peak=Mathf.Max(c.r,Mathf.Max(c.g,c.b));maximum=Mathf.Max(maximum,peak);
                    if(peak>1)aboveOne++;if(c.r>1&&c.g>1&&c.b>1)allAboveOne++;
                    pixels[i]=new Color(Display(c.r),Display(c.g),Display(c.b),1);
                }
                review.SetPixels(pixels);review.Apply();
                File.WriteAllBytes(Path.Combine(reportDirectory,name.Replace(".png","-before-lut-reinhard.png")),review.EncodeToPNG());
                hdrChecks.Add(new JObject{["capture"]=name,["sourceFormat"]=source.graphicsFormat.ToString(),["sourceSrgb"]=source.sRGB,
                    ["cameraAllowHdr"]=controller.viewCamera.allowHDR,["width"]=source.width,["height"]=source.height,
                    ["maxRgb"]=maximum,["anyChannelAboveOne"]=aboveOne,["allChannelsAboveOne"]=allAboveOne,["nonFinite"]=invalid,
                    ["scope"]="same-frame diagnostic rerender with an added passthrough image effect; original screenshot rendered first without it; not a screen-backbuffer capture",
                    ["reviewCurve"]="Reinhard per channel then linear-to-sRGB; diagnostic only"});
                File.WriteAllText(Path.Combine(reportDirectory,"hdr-verification.json"),hdrChecks.ToString());
                if(invalid!=0)throw new Exception("Non-finite pre-LUT GPU pixels");
            }
            finally{RenderTexture.active=active;RenderTexture.ReleaseTemporary(copy);Destroy(cpu);Destroy(review);}
        }
        static float Display(float value){value=Mathf.Max(0,value);return Mathf.LinearToGammaSpace(value/(1+value));}
        void CaptureFace(string name)
        {
            var all=controller.visual.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var face=all.Single(r=>r.name=="SMR_Remielle_Face"&&r.enabled);var brow=all.Single(r=>r.name=="SMR_Remielle_Eyebrow"&&r.enabled);
            var camera=controller.viewCamera;var position=camera.transform.position;var rotation=camera.transform.rotation;float fov=camera.fieldOfView;
            try
            {
                camera.fieldOfView=40;camera.transform.position=face.bounds.center+Vector3.forward*.55f;camera.transform.LookAt(face.bounds.center);
                var on=Capture("face-"+name+".png",false);brow.enabled=false;var off=Capture("face-"+name+"-no-brow.png",false);brow.enabled=true;
                int pixels=on.Zip(off,(a,b)=>a.Equals(b)?0:1).Sum();
                faceChecks.Add(new JObject{["pose"]=name,["state"]=controller.Session.CurrentState,["frame"]=controller.Session.ActionFrames,["browPixelDifference"]=pixels,["actualPlayerGpu"]=true});
                File.WriteAllText(Path.Combine(reportDirectory,"face-verification.json"),faceChecks.ToString());
                if(pixels<5)throw new Exception("Eyebrow missing from Player face comparison: "+name);
            }
            finally{brow.enabled=true;camera.fieldOfView=fov;camera.transform.SetPositionAndRotation(position,rotation);}
        }
        void OnDestroy()
        {
            if(keyboard!=null)InputSystem.RemoveDevice(keyboard);if(mouse!=null)InputSystem.RemoveDevice(mouse);
            if(originalSettings)InputSystem.settings=originalSettings;if(testSettings)Destroy(testSettings);Time.captureDeltaTime=originalCaptureDelta;
        }
    }
}
