using System;
using System.IO;
using Newtonsoft.Json.Linq;
using Remielle.ControllerRuntime;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;

namespace Remielle.Controller.Editor
{
    public static class SourceShakeAudit
    {
        public static void Run()
        {
            var report=new JObject{["pass"]=false};var cases=new JArray();report["cases"]=cases;
            float oldTime=CinemachineCore.CurrentTimeOverride;var origin=new GameObject("ShakeAuditOrigin");
            void Check(bool pass,string text){if(!pass)throw new Exception(text);}
            try
            {
                string json=SourceCameraBuild.Prepare("Assets/ControllerIntegration/Data/source-action-shake.json").text;
                var pack=JObject.Parse(json);var c=pack["configs"]["RemielleOrigin_Attack_Normal_04_CamShake_E_01"];
                var row=pack["curves"][(string)c["CurveKey"]];var curve=SourceCameraShake.ReadCurve(row);
                var signal=new SourceCameraShake.Signal(c,curve);double error=0;
                var a=row["keys"][0];var b=row["keys"][1];double length=(double)b["time"];
                for(int i=0;i<600;i++)
                {
                    float time=i*.001f;double x=Math.Min(time/(float)c["ShakeTotalTime"],length),u=x/length;
                    double v=(2*u*u*u-3*u*u+1)*(double)a["value"]+(u*u*u-2*u*u+u)*length*(double)a["outTangent"]+
                        (-2*u*u*u+3*u*u)*(double)b["value"]+(u*u*u-u*u)*length*(double)b["inTangent"];
                    double expected=Math.Cos(2*Math.PI*(float)c["Frequency"]*time)*(float)c["RadiusLength"]*.5*v;
                    signal.GetSignal(time,out var pos,out var rot);error=Math.Max(error,Math.Abs(pos.y-expected));
                    Check(rot==Quaternion.identity&&Math.Abs(pos.x)<1e-6,"Unexpected source signal axis/rotation");
                }
                Check(error<1e-6,"Independent Hermite/oscillator comparison differs");report["independentSignalMaxError"]=error;
                foreach(int hz in new[]{30,60,120})
                {
                    float start=1000+hz;CinemachineCore.CurrentTimeOverride=start;
                    var source=new NativeControllerSource(File.ReadAllText("Assets/ControllerRuntime/Data/source-controller-pack.json"));
                    var events=new SourceFrameEvents(File.ReadAllText("Assets/ControllerIntegration/Data/source-action-events.json"),source,"Avatar_Female_Size02_RemielleOrigin_Controller");
                    var manager=CinemachineImpulseManager.Instance;
                    using(var host=new SourceCameraShake(json,events,origin.transform))
                    {
                        void Send(SourceActionPhase phase,long id,double frame)=>events.Observe(new SourceActionNotice(phase,id,42,frame,160,false,"shake-audit"));
                        Vector3 Value(){manager.GetImpulseAt(Vector3.zero,false,SourceCameraShake.Channel,out var p,out _);return p;}
                        Send(SourceActionPhase.Enter,1,0);Send(SourceActionPhase.Sample,1,7);Check(host.Activations==0,"Shake entered before authored frame");
                        Send(SourceActionPhase.Sample,1,8);Check(Value().magnitude>.049f,"Cinemachine did not receive the authored impulse");
                        float peak=0;
                        for(int i=0;i<=hz;i++){CinemachineCore.CurrentTimeOverride=start+i/(float)hz;host.Advance();peak=Math.Max(peak,Value().magnitude);}
                        Check(host.Completed==1&&host.ActiveCount==0&&Value()==Vector3.zero,"Natural impulse did not expire");
                        Send(SourceActionPhase.Leave,1,9);Send(SourceActionPhase.Exit,1,9);
                        Send(SourceActionPhase.Enter,2,0);Send(SourceActionPhase.Sample,2,8);
                        var paused=Value();Check(Value()==paused,"Repeated evaluation advanced signal time");
                        Send(SourceActionPhase.Leave,2,9);Check(Value()==Vector3.zero&&host.ActiveCount==0,"Same-tick interruption left an impulse");
                        Send(SourceActionPhase.Enter,3,0);Send(SourceActionPhase.Sample,3,8);Send(SourceActionPhase.Exit,2,10);
                        Check(host.ActiveCount==1&&Value().magnitude>.049f,"Stale generation cancelled new impulse");
                        CinemachineCore.CurrentTimeOverride+=.001f;host.Dispose();Check(Value()==Vector3.zero,"Disposal left a signal");
                        // Force manager recycling, then create a foreign source.
                        CinemachineCore.CurrentTimeOverride+=1;Value();
                        var foreign=manager.NewImpulseEvent();var foreignSignal=new SourceCameraShake.Signal(c,curve);
                        foreign.SignalSource=foreignSignal;foreign.Channel=SourceCameraShake.Channel;foreign.Radius=100;foreign.PropagationSpeed=float.MaxValue;
                        foreign.Envelope=new CinemachineImpulseManager.EnvelopeDefinition{SustainTime=1};manager.AddImpulseEvent(foreign);
                        host.Dispose();Check(Value().magnitude>.049f&&!foreignSignal.Cancelled,"Repeated disposal cleared another impulse owner");
                        foreignSignal.Cancelled=true;foreign.Cancel(manager.CurrentTime+.0001f,true);CinemachineCore.CurrentTimeOverride+=1;Value();
                        using(var staleOwner=new SourceCameraShake(json,events,origin.transform))
                        {
                            Send(SourceActionPhase.Enter,4,0);Send(SourceActionPhase.Sample,4,8);
                            // Let the listener recycle the event before this
                            // consumer gets its next Advance/Release callback.
                            CinemachineCore.CurrentTimeOverride+=1;Value();
                            var reused=manager.NewImpulseEvent();var reusedSignal=new SourceCameraShake.Signal(c,curve);
                            reused.SignalSource=reusedSignal;reused.Channel=SourceCameraShake.Channel;reused.Radius=100;reused.PropagationSpeed=float.MaxValue;
                            reused.Envelope=new CinemachineImpulseManager.EnvelopeDefinition{SustainTime=1};manager.AddImpulseEvent(reused);
                            staleOwner.Dispose();Check(Value().magnitude>.049f&&!reusedSignal.Cancelled,"Stale pooled-event reference cancelled a foreign source");
                            reusedSignal.Cancelled=true;reused.Cancel(manager.CurrentTime+.0001f,true);CinemachineCore.CurrentTimeOverride+=1;Value();
                        }
                        cases.Add(new JObject{["hz"]=hz,["activations"]=host.Activations,["completed"]=host.Completed,["cancelled"]=host.Cancelled,["peak"]=peak,["pass"]=true});
                    }
                }
                report["pass"]=true;report["nativeSignalParity"]=false;Debug.Log("REMIELLE_SOURCE_SHAKE_PASS");
            }
            catch(Exception ex){report["error"]=ex.ToString();Debug.LogException(ex);}
            finally{CinemachineCore.CurrentTimeOverride=oldTime;UnityEngine.Object.DestroyImmediate(origin);File.WriteAllText(QualifiedIntegrationAudit.Output+"/source-shake-verification.json",report.ToString());}
            if(!(bool)report["pass"])EditorApplication.Exit(1);
        }
    }
}
