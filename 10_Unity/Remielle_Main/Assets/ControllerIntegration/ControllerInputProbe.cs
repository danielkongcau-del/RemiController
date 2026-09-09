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
    public sealed class ControllerInputProbe : MonoBehaviour
    {
        public RemielleSourceController controller;
        public string reportDirectory;
        public bool completed,passed;
        public bool dashScenario;
        public bool comboScenario;
        readonly System.Collections.Generic.HashSet<int> comboStates=new System.Collections.Generic.HashSet<int>();
        readonly System.Collections.Generic.HashSet<int> comboPressed=new System.Collections.Generic.HashSet<int>();
        bool capturedBothTrails;
        float peakLeftVisibility;
        Keyboard keyboard;Mouse mouse;
        InputSettings originalSettings,testSettings;
        float previousCaptureDelta;
        int frame,phase,phaseFrame,walkLoopFrames;
        bool sawEvade,sawAttack,sawAttackEnd,sawInterruptedEvade;
        bool sawDashStart,sawDashLoop,sawDashEvade,sawDashLoop02,sawDashEnd,sawRush,sawRushEnd;
        int preparedAtStart;
        bool capturedTrail;
        float peakWeaponVisibility;
        float peakCameraFov;
        bool capturedDashCamera;
        JObject dashCameraFraming;
        float peakShakeCorrection,shakeCapturePositionDelta;
        int shakeChangedPixels;
        bool capturedShake;
        void Start()
        {
            originalSettings=InputSystem.settings;testSettings=Instantiate(originalSettings);InputSystem.settings=testSettings;
            previousCaptureDelta=Time.captureDeltaTime;
            testSettings.updateMode=InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
            testSettings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            testSettings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            Time.captureDeltaTime=1f/60f;
            keyboard=InputSystem.AddDevice<Keyboard>("RemielleProbeKeyboard");mouse=InputSystem.AddDevice<Mouse>("RemielleProbeMouse");
            controller.RestrictInputDevices(keyboard,mouse);
            preparedAtStart=controller.Session.PreparedSamplerCount;
        }
        void Update()
        {
            if(completed)return;
            try
            {
                frame++;phaseFrame++;
                if(!controller.Ready)throw new Exception(controller.Error??"Controller not ready");
                if(frame>(dashScenario?3600:2100))throw new Exception("Input trajectory timed out in phase "+phase);
                var session=controller.Session;
                peakShakeCorrection=Math.Max(peakShakeCorrection,controller.layoutCamera.State.PositionCorrection.magnitude);
                if(!capturedShake&&controller.ActionShake!=null&&controller.ActionShake.ActiveCount>0&&controller.layoutCamera.State.PositionCorrection.magnitude>.01f)
                    CaptureShakeComparison();
                peakCameraFov=Math.Max(peakCameraFov,controller.viewCamera.fieldOfView);
                if(dashScenario&&!capturedDashCamera&&controller.ActionCamera!=null&&controller.ActionCamera.FieldOfView>=64)
                {
                    Capture("controller-dash-camera.png");
                    var head=controller.visual.GetComponentInChildren<RemielleFaceLighting>().head;
                    var point=controller.viewCamera.WorldToViewportPoint(head.position);
                    dashCameraFraming=new JObject{["headViewport"]=new JArray(point.x,point.y,point.z),
                        ["focusWorld"]=new JArray(controller.layoutCamera.Follow.position.x,controller.layoutCamera.Follow.position.y,controller.layoutCamera.Follow.position.z)};
                    if(point.z<=0||point.x<.05f||point.x>.95f||point.y<.05f||point.y>.95f)throw new Exception("Dash camera cropped the head during the close view");
                    capturedDashCamera=true;
                }
                if(controller.WeaponSurface!=null)peakWeaponVisibility=Math.Max(peakWeaponVisibility,controller.WeaponSurface.Visibility);
                if(controller.LeftWeaponSurface!=null)peakLeftVisibility=Math.Max(peakLeftVisibility,controller.LeftWeaponSurface.Visibility);
                if(!dashScenario&&!capturedTrail&&controller.WeaponTrail!=null&&controller.WeaponTrail.Vertices>0&&session.CurrentState==46&&session.ActionFrames>=16)
                {Capture("controller-attack-trail.png");capturedTrail=true;}
                if(comboScenario){UpdateCombo(session);return;}
                if(dashScenario){UpdateDash(session);return;}
                if(phase==0&&session.CurrentState==13&&!session.IsBlending&&++walkLoopFrames>=40)ChangePhase(1);
                else if(phase==1&&session.CurrentState==32&&!session.IsBlending)ChangePhase(2);
                else if(phase==2)
                {
                    sawEvade|=session.CurrentState==9;
                    if(sawEvade&&session.CurrentState==32&&!session.IsBlending)ChangePhase(3);
                }
                else if(phase==3)
                {
                    sawAttack|=session.CurrentState==46;sawAttackEnd|=session.CurrentState==40;
                    if(sawAttackEnd&&session.CurrentState==32&&!session.IsBlending)ChangePhase(4);
                }
                else if(phase==4&&session.NextState==12)ChangePhase(5);
                else if(phase==5&&session.NextState==14)ChangePhase(6);
                else if(phase==6)
                {
                    sawInterruptedEvade|=session.NextState==9||session.CurrentState==9;
                    if(sawInterruptedEvade&&session.CurrentState==32&&!session.IsBlending){Finish(null);return;}
                }
                var keys=phase==0||phase==4?new[]{Key.W}:(phase==2||phase==6)&&phaseFrame==0?new[]{Key.Space}:Array.Empty<Key>();
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys));
                var mouseState=new MouseState();if(phase==3&&phaseFrame==0)mouseState=mouseState.WithButton(MouseButton.Left);
                // Normal PlayerLoop pumping; the temporary settings clone
                // permits our two virtual devices in the unfocused batch editor.
                InputSystem.QueueStateEvent(mouse,mouseState);
            }
            catch(Exception ex){Finish(ex.ToString());}
        }
        void UpdateCombo(SourceActionSession session)
        {
            int state=session.NextState??session.CurrentState;comboStates.Add(state);
            bool press=frame==1;
            int threshold=state==46?26:state==41?34:state==39?30:int.MaxValue;
            if(!session.IsBlending&&session.ActionFrames>=threshold&&comboPressed.Add(state))press=true;
            if(state==42&&controller.LeftWeaponTrail.Vertices>0&&controller.WeaponTrail.Vertices>0&&session.ActionFrames>=18&&!capturedBothTrails)
            {Capture("controller-combo-both-trails.png");capturedBothTrails=true;}
            if(comboStates.Contains(42)&&session.CurrentState==32&&!session.IsBlending){Finish(null);return;}
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());
            var buttons=new MouseState();if(press)buttons=buttons.WithButton(MouseButton.Left);InputSystem.QueueStateEvent(mouse,buttons);
        }
        void UpdateDash(SourceActionSession session)
        {
            int state=session.NextState??session.CurrentState;
            sawDashStart|=state==25;sawDashLoop|=state==26;sawDashEvade|=state==29;
            sawDashLoop02|=state==30;sawDashEnd|=state==27;sawRush|=state==10;sawRushEnd|=state==17;
            if(session.PreparedSamplerCount!=preparedAtStart)throw new Exception("Interactive dash trajectory loaded a new sampler");
            if(phase==0&&session.CurrentState==26&&!session.IsBlending)ChangePhase(1);
            else if(phase==1)
            {
                if(state!=26)throw new Exception("Releasing evade while moving incorrectly stopped dash");
                if(phaseFrame>=20)ChangePhase(2);
            }
            else if(phase==2&&session.CurrentState==30&&!session.IsBlending)ChangePhase(3);
            else if(phase==3&&session.CurrentState==29&&session.ActionFrames>=23)ChangePhase(4);
            else if(phase==4&&phaseFrame>=14)ChangePhase(5);
            else if(phase==5&&sawRushEnd&&session.CurrentState==32&&!session.IsBlending)ChangePhase(6);
            else if(phase==6&&session.CurrentState==25&&session.ActionFrames>=12)ChangePhase(7);
            else if(phase==7&&sawDashEnd&&session.CurrentState==32&&!session.IsBlending){Finish(null);return;}
            bool moving=phase<=4||phase==6;
            bool evade=phase==0||phase==6||(phase>=2&&phase<=4&&phaseFrame==0);
            var keys=moving?(evade?new[]{Key.W,Key.Space}:new[]{Key.W}):Array.Empty<Key>();
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys));
            var buttons=new MouseState();if(phase==5&&phaseFrame==0)buttons=buttons.WithButton(MouseButton.Left);
            InputSystem.QueueStateEvent(mouse,buttons);
        }
        void ChangePhase(int value){phase=value;phaseFrame=0;}
        void Finish(string error)
        {
            int interruptions=controller.Session.Trace.Count(t=>(bool?)t["interrupted"]==true);
            if(error==null)
            {
                if(comboScenario)
                {
                    if(!new[]{46,41,39,42}.All(comboStates.Contains)||!capturedBothTrails||peakLeftVisibility<.999f)error="Four-hit source combo did not activate both weapon families";
                }
                else if(dashScenario)
                {
                    int evades=controller.Session.Trace.Count(t=>(string)t["event"]=="transition-start"&&(int)t["targetState"]==29);
                    if(!sawDashStart||!sawDashLoop||!sawDashEvade||!sawDashLoop02||!sawDashEnd||!sawRush||!sawRushEnd||evades<3)
                        error="Dash input chain did not visit all required states and repeated evades";
                }
                else if(!sawEvade||!sawAttack||!sawAttackEnd||!sawInterruptedEvade||interruptions<2)error="Input chain did not visit expected states and repeated interruptions";
            }
            if(error==null)
            {
                if(controller.ActionEvents==null||controller.ActionEvents.ActiveScopes!=1)error="Action event host did not return to one idle owner";
                else if(controller.ActionShake==null||controller.ActionShake.ActiveCount!=0||controller.ActionShake.Activations!=controller.ActionShake.Completed+controller.ActionShake.Cancelled)error="Camera shake did not release all impulses";
                else if(controller.ActionShake.Activations!=(comboScenario?3:dashScenario?1:0)||controller.ActionShake.UnsupportedEvents!=0)error="Authored shake count differs for actual input trajectory";
                else if((comboScenario||dashScenario)&&(!capturedShake||shakeChangedPixels<100||shakeCapturePositionDelta<.001f))error="Camera shake did not alter rendered output";
                else if(controller.DroppedEventTraceEntries!=0||controller.Session.DroppedTraceEntries!=0)error="Probe diagnostics truncated";
                else if(controller.ActionCamera==null||!controller.ActionCamera.AtBaseline||Math.Abs(controller.viewCamera.fieldOfView-controller.ActionCamera.BaselineFov)>.001f)error="Action camera did not restore the rendered baseline";
                else if(dashScenario&&(!capturedDashCamera||peakCameraFov<64||controller.ActionCamera.Activations<2||
                    controller.ActionCamera.Releases!=controller.ActionCamera.Activations||controller.ActionCamera.ForcedReleases<1))error="Dash camera missed natural or interrupted ownership through actual input";
                else if(!dashScenario&&controller.ActionCamera.Activations!=0)error="Dash camera incorrectly activated during another input family";
                else if(controller.WeaponTrail==null||controller.WeaponTrail.ActiveOwners!=0||controller.WeaponTrail.Vertices!=0)error="Trail did not return to idle cleanup";
                else if(!dashScenario&&!comboScenario&&(!capturedTrail||controller.WeaponTrail.Activations!=1||controller.WeaponTrail.Releases!=1))error="Attack input did not create and release one trail instance";
                else if(controller.WeaponEmission==null||controller.WeaponEmission.IsPulsing||controller.WeaponEmission.ActiveOwners!=0)error="Weapon emission did not restore idle properties";
                else if(!dashScenario&&!comboScenario&&(controller.WeaponEmission.Pulses!=2||controller.WeaponEmission.PeakColor<.25f))error="Attack input did not generate both source emission pulses";
                else if(controller.WeaponSurface==null||controller.WeaponSurface.Visibility!=0||controller.WeaponSurface.IsTransitioning)error="Weapon surfaces did not stay hidden after their exit curve";
                else if(!dashScenario&&peakWeaponVisibility<.999f)error="Actual attack input never revealed the source weapon surfaces";
                else if(controller.LeftWeaponEmission==null||controller.LeftWeaponEmission.ActiveOwners!=0||controller.LeftWeaponEmission.IsPulsing||
                    controller.LeftWeaponTrail.ActiveOwners!=0||controller.LeftWeaponTrail.Vertices!=0||controller.LeftWeaponSurface.Visibility!=0||controller.LeftWeaponSurface.IsTransitioning||
                    controller.LargeWeaponWindow.ActiveOwners!=0||controller.LargeWeaponTrail.Vertices!=0||controller.LargeWeaponSurface.Visibility!=0||controller.LargeWeaponSurface.IsTransitioning)error="Additional weapon families did not clean up";
                else
                {
                    var events=controller.EventTrace.Where(e=>e["entry"]!=null).ToArray();
                    if(events.GroupBy(e=>((long)e["generation"],(int)e["entry"],(long)e["cycle"])).Any(g=>g.Count()!=1))error="Duplicate per-action event";
                    int attack=dashScenario?10:46;
                    if(!events.Any(e=>(int)e["state"]==attack&&(int)e["entry"]==0)||!events.Any(e=>(int)e["state"]==attack&&(int)e["entry"]==1))error="Original attack property events not reached through input";
                    if(dashScenario)
                    {
                        var evades=events.Where(e=>(int)e["state"]==29).GroupBy(e=>(long)e["generation"]).ToArray();
                        if(evades.Length<3||evades.Any(g=>!g.Select(e=>(int)e["entry"]).OrderBy(x=>x).SequenceEqual(new[]{0,1,2,3,4})))error="Repeated dash evade did not clean every event owner";
                        if(!events.Any(e=>(int)e["state"]==25&&(int)e["entry"]==1&&((string)e["reason"]).StartsWith("leave:")))error="Early dash stop missed forced camera cleanup";
                    }
                }
            }
            if(error==null)
            {
                try{Capture();}catch(Exception ex){error="Player render capture failed: "+ex;}
            }
            completed=true;passed=error==null;controller.readDevices=false;
            Directory.CreateDirectory(reportDirectory);
            File.WriteAllText(Path.Combine(reportDirectory,comboScenario?"controller-combo-verification.json":dashScenario?"controller-dash-verification.json":"controller-input-verification.json"),new JObject{
                ["pass"]=passed,["error"]=error,["frames"]=frame,["actualPlayMode"]=true,["virtualInputSystemDevices"]=true,
                ["scenario"]=comboScenario?"source-four-hit-combo":dashScenario?"dash-hold-release-repeat-attack":"walk-evade-attack-interrupt",
                ["prewarmedSamplers"]=preparedAtStart,["finalSamplers"]=controller.Session.PreparedSamplerCount,["warmupMilliseconds"]=controller.WarmupMilliseconds,
                ["directParameterInjection"]=false,["trace"]=new JArray(controller.Session.Trace),
                ["actionEvents"]=new JArray(controller.EventTrace),["activeEventScopes"]=controller.ActionEvents?.ActiveScopes,
                ["visibilityRenderers"]=controller.Visibility?.RendererCount,["droppedEventTraceEntries"]=controller.DroppedEventTraceEntries,
                ["trailActivations"]=controller.WeaponTrail?.Activations,["trailReleases"]=controller.WeaponTrail?.Releases,["trailPeakVertices"]=controller.WeaponTrail?.PeakVertices,
                ["trailOwnersAtEnd"]=controller.WeaponTrail?.ActiveOwners,["trailVerticesAtEnd"]=controller.WeaponTrail?.Vertices,["trailCapture"]=capturedTrail?"controller-attack-trail.png":null,
                ["emissionPulses"]=controller.WeaponEmission?.Pulses,["emissionPeakColor"]=controller.WeaponEmission?.PeakColor,["emissionOwnersAtEnd"]=controller.WeaponEmission?.ActiveOwners,["emissionActiveAtEnd"]=controller.WeaponEmission?.IsPulsing,
                ["weaponSurfaceTargets"]=controller.WeaponSurface?.TargetCount,["weaponVisibilityAtEnd"]=controller.WeaponSurface?.Visibility,["weaponSurfaceTransitionAtEnd"]=controller.WeaponSurface?.IsTransitioning,
                ["peakWeaponVisibility"]=peakWeaponVisibility,
                ["leftTrailActivations"]=controller.LeftWeaponTrail?.Activations,["leftTrailReleases"]=controller.LeftWeaponTrail?.Releases,["leftTrailPeakVertices"]=controller.LeftWeaponTrail?.PeakVertices,
                ["leftEmissionPulses"]=controller.LeftWeaponEmission?.Pulses,["leftVisibilityAtEnd"]=controller.LeftWeaponSurface?.Visibility,["peakLeftVisibility"]=peakLeftVisibility,
                ["largeTrailActivations"]=controller.LargeWeaponTrail?.Activations,["largeVisibilityAtEnd"]=controller.LargeWeaponSurface?.Visibility,["capturedBothTrails"]=capturedBothTrails,
                ["cameraActivations"]=controller.ActionCamera?.Activations,["cameraReleases"]=controller.ActionCamera?.Releases,["cameraForcedReleases"]=controller.ActionCamera?.ForcedReleases,
                ["cameraAtBaseline"]=controller.ActionCamera?.AtBaseline,["cameraPeakRenderedFov"]=peakCameraFov,["cameraRenderedFovAtEnd"]=controller.viewCamera.fieldOfView,
                ["cameraUnsupportedEvents"]=controller.ActionCamera?.UnsupportedEvents,["dashCameraCapture"]=capturedDashCamera?"controller-dash-camera.png":null,
                ["nativeCameraComposeParity"]=false,
                ["dashCameraFraming"]=dashCameraFraming,
                ["shakeActivations"]=controller.ActionShake?.Activations,["shakeCompleted"]=controller.ActionShake?.Completed,["shakeCancelled"]=controller.ActionShake?.Cancelled,
                ["shakeActiveAtEnd"]=controller.ActionShake?.ActiveCount,["shakeUnsupportedEvents"]=controller.ActionShake?.UnsupportedEvents,["peakShakeCorrection"]=peakShakeCorrection,
                ["shakeChangedPixels"]=shakeChangedPixels,["shakeCapturePositionDelta"]=shakeCapturePositionDelta,["nativeShakeSignalParity"]=false,
                ["renderCapture"]=passed?(comboScenario?"controller-combo-preview.png":dashScenario?"controller-dash-preview.png":"controller-preview.png"):null,
                ["actorPosition"]=new JArray(controller.motor.transform.position.x,controller.motor.transform.position.y,controller.motor.transform.position.z),
                ["inputExpiryQualified"]=false,["blendInterruptions"]=interruptions,["sourceQueueIntegrated"]=true,
                ["nativeGraphParityVerified"]=false,["gameplayParityVerified"]=false}.ToString());
            if(error!=null)Debug.LogError(error);
        }
        void CaptureShakeComparison()
        {
            var listener=controller.layoutCamera.GetComponent<Unity.Cinemachine.CinemachineImpulseListener>();float gain=listener.Gain;
            string prefix=comboScenario?"controller-combo-shake":"controller-dash-shake";
            try
            {
                Capture(prefix+"-on.png");var onPosition=controller.viewCamera.transform.position;
                // CM normally evaluates once per frame. This isolated GPU
                // comparison explicitly re-evaluates the pipeline at delta 0;
                // calling Brain.ManualUpdate twice alone would reuse its cache.
                listener.Gain=0;controller.layoutCamera.InternalUpdateCameraState(Vector3.up,0);
                Capture(prefix+"-off.png");shakeCapturePositionDelta=Vector3.Distance(onPosition,controller.viewCamera.transform.position);
                var on=new Texture2D(2,2);var off=new Texture2D(2,2);
                try
                {
                    on.LoadImage(File.ReadAllBytes(Path.Combine(reportDirectory,prefix+"-on.png")));off.LoadImage(File.ReadAllBytes(Path.Combine(reportDirectory,prefix+"-off.png")));
                    var a=on.GetPixels32();var b=off.GetPixels32();for(int i=0;i<a.Length;i++)if(Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b)>3)shakeChangedPixels++;
                }
                finally{Destroy(on);Destroy(off);}
                capturedShake=true;
            }
            finally{listener.Gain=gain;controller.layoutCamera.InternalUpdateCameraState(Vector3.up,0);controller.brain.ManualUpdate();}
        }
        void Capture(string name=null)
        {
            Directory.CreateDirectory(reportDirectory);controller.brain.ManualUpdate();
            var camera=controller.viewCamera;var oldTarget=camera.targetTexture;var oldActive=RenderTexture.active;
            var target=RenderTexture.GetTemporary(1280,720,24,RenderTextureFormat.ARGB32);
            var texture=new Texture2D(1280,720,TextureFormat.RGB24,false);
            try
            {
                camera.targetTexture=target;camera.Render();RenderTexture.active=target;
                texture.ReadPixels(new Rect(0,0,1280,720),0,0);texture.Apply();
                File.WriteAllBytes(Path.Combine(reportDirectory,name??(comboScenario?"controller-combo-preview.png":dashScenario?"controller-dash-preview.png":"controller-preview.png")),texture.EncodeToPNG());
            }
            finally{camera.targetTexture=oldTarget;RenderTexture.active=oldActive;RenderTexture.ReleaseTemporary(target);Destroy(texture);}
        }
        void OnDestroy()
        {
            if(keyboard!=null)InputSystem.RemoveDevice(keyboard);if(mouse!=null)InputSystem.RemoveDevice(mouse);
            if(originalSettings)InputSystem.settings=originalSettings;if(testSettings)Destroy(testSettings);Time.captureDeltaTime=previousCaptureDelta;
        }
    }
}
