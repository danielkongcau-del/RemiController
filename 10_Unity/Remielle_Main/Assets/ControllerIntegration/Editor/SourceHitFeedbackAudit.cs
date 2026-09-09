using System;
using System.IO;
using Newtonsoft.Json.Linq;
using Remielle.ControllerRuntime;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Remielle.Controller.Editor
{
    public static class SourceHitFeedbackAudit
    {
        public static void Run()
        {
            var report=new JObject{["pass"]=false};var cases=new JArray();report["cases"]=cases;
            float oldTime=CinemachineCore.CurrentTimeOverride;
            void Check(bool ok,string error){if(!ok)throw new Exception(error);}
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                string feedback=SourceCameraBuild.Prepare("Assets/ControllerIntegration/Data/source-hit-feedback.json").text;
                string hitPack=SourceCameraBuild.Prepare("Assets/ControllerIntegration/Data/source-hit-query.json").text;
                var source=new NativeControllerSource(File.ReadAllText("Assets/ControllerRuntime/Data/source-controller-pack.json"));
                string eventPack=File.ReadAllText("Assets/ControllerIntegration/Data/source-action-events.json");
                var actor=new GameObject("FeedbackAuditActor");
                var first=SourceTrainingTarget.Create("First",new Vector3(-.5f,1,2));
                var second=SourceTrainingTarget.Create("Second",new Vector3(.5f,1,2));
                var manager=CinemachineImpulseManager.Instance;bool originalIgnore=manager.IgnoreTimeScale;
                foreach(int hz in new[]{30,60,120})foreach(float scale in new[]{0f,.1f,1f,3f})
                {
                    double real=0;float start=2000+cases.Count*10;CinemachineCore.CurrentTimeOverride=start;
                    var events=new SourceFrameEvents(eventPack,source,"Avatar_Female_Size02_RemielleOrigin_Controller");
                    using(var hits=new SourceHitQuery(hitPack,events,actor.transform))
                    using(var host=new SourceHitFeedback(feedback,hits,actor.transform,()=>real))
                    {
                        void Send(SourceActionPhase phase,long id,double frame)=>events.Observe(new SourceActionNotice(phase,id,46,frame,90,false,"feedback-audit"));
                        Vector3 Value(){manager.GetImpulseAt(new Vector3(0,0,6),false,SourceCameraShake.Channel,out var p,out _);return p;}
                        first.vulnerable=second.vulnerable=false;
                        Send(SourceActionPhase.Enter,1,0);Send(SourceActionPhase.Sample,1,24);
                        Check(hits.Misses==2&&host.Activations==0&&host.MissesSkipped==2&&Value()==Vector3.zero,"Miss generated camera feedback");
                        Send(SourceActionPhase.Leave,1,24);Send(SourceActionPhase.Exit,1,24);
                        first.vulnerable=second.vulnerable=true;
                        Send(SourceActionPhase.Enter,2,0);Send(SourceActionPhase.Sample,2,13);
                        Check(hits.Hits==2&&host.Activations==1,"Multiple targets multiplied camera shake");
                        var initial=Value();Check(initial.magnitude>.01f,"CM listener did not receive paused-clock impulse");
                        Send(SourceActionPhase.Sample,2,13);Check(host.Activations==1,"Duplicate sample repeated feedback");
                        Send(SourceActionPhase.Leave,2,13);Send(SourceActionPhase.Exit,2,13);
                        Check(host.ActiveCount==1,"Committed hit shake vanished when attack left");
                        float variation=0;
                        for(int i=1;i<=hz;i++)
                        {
                            real=i/(double)hz;CinemachineCore.CurrentTimeOverride=start+(float)real*scale;host.Advance();
                            variation=Mathf.Max(variation,(Value()-initial).magnitude);
                        }
                        Check(host.Completed==1&&host.ActiveCount==0&&Value()==Vector3.zero&&variation>.001f,"Unscaled decay failed at rate "+scale);
                        Send(SourceActionPhase.Enter,3,0);Send(SourceActionPhase.Sample,3,13);Check(host.ActiveCount==1,"New hit did not restart");
                        host.Dispose();Check(Value()==Vector3.zero&&host.Cancelled==1,"Dispose did not mute/recycle while paused");
                        // The old consumer must not clear unrelated pooled events.
                        var c=JObject.Parse(feedback)["configs"]["RemielleOrigin_Attack_Normal_01_CamShake_A_01"];
                        var curve=SourceCameraShake.ReadCurve(JObject.Parse(feedback)["curves"][(string)c["CurveKey"]]);
                        var foreign=manager.NewImpulseEvent();var wave=new SourceCameraShake.Signal(c,curve);
                        foreign.SignalSource=wave;foreign.Position=Vector3.zero;foreign.Channel=SourceCameraShake.Channel;foreign.Radius=100;
                        foreign.PropagationSpeed=float.MaxValue;foreign.Envelope=new CinemachineImpulseManager.EnvelopeDefinition{SustainTime=1};
                        manager.AddImpulseEvent(foreign);foreign.StartTime-=.001f;host.Dispose();
                        Check(!wave.Cancelled&&Value().magnitude>.001f,"Repeated dispose cancelled a foreign event");
                        wave.Cancelled=true;foreign.Envelope=new CinemachineImpulseManager.EnvelopeDefinition{SustainTime=.001f};foreign.StartTime=manager.CurrentTime-1;Value();
                        Check(manager.IgnoreTimeScale==originalIgnore,"Changed the global Cinemachine clock");
                        cases.Add(new JObject{["hz"]=hz,["scaledClockRate"]=scale,["hitTargets"]=hits.Hits,["activations"]=host.Activations,
                            ["completed"]=host.Completed,["cancelled"]=host.Cancelled,["pass"]=true});
                    }
                }
                UnityEngine.Object.DestroyImmediate(first.gameObject);UnityEngine.Object.DestroyImmediate(second.gameObject);UnityEngine.Object.DestroyImmediate(actor);
                report["pass"]=true;report["nativeWaveformParity"]=false;Debug.Log("REMIELLE_HIT_FEEDBACK_PASS");
            }
            catch(Exception ex){report["error"]=ex.ToString();Debug.LogException(ex);}
            finally{CinemachineCore.CurrentTimeOverride=oldTime;File.WriteAllText(QualifiedIntegrationAudit.Output+"/source-hit-feedback-verification.json",report.ToString());}
            if(!(bool)report["pass"])EditorApplication.Exit(1);
        }
    }
}
