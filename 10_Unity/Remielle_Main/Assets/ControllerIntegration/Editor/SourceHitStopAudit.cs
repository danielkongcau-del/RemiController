using System;
using System.IO;
using Newtonsoft.Json.Linq;
using Remielle.ControllerRuntime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Remielle.Controller.Editor
{
    public static class SourceHitStopAudit
    {
        public static void Run()
        {
            var report=new JObject{["pass"]=false};var cases=new JArray();report["cases"]=cases;
            void Check(bool ok,string message){if(!ok)throw new Exception(message);}
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                string pack=SourceCameraBuild.Prepare("Assets/ControllerIntegration/Data/source-hitstop.json").text;
                string hitPack=SourceCameraBuild.Prepare("Assets/ControllerIntegration/Data/source-hit-query.json").text;
                var source=new NativeControllerSource(File.ReadAllText("Assets/ControllerRuntime/Data/source-controller-pack.json"));
                string eventPack=File.ReadAllText("Assets/ControllerIntegration/Data/source-action-events.json");
                var actor=new GameObject("HitStopAuditActor");var target=SourceTrainingTarget.Create("First",new Vector3(0,1,2));
                var second=SourceTrainingTarget.Create("Second",new Vector3(1,1,2));float scale=Time.timeScale;
                foreach(int hz in new[]{30,60,120})
                {
                    var events=new SourceFrameEvents(eventPack,source,"Avatar_Female_Size02_RemielleOrigin_Controller");
                    using(var hits=new SourceHitQuery(hitPack,events,actor.transform))
                    using(var stop=new SourceHitStop(pack,hits))
                    {
                        void Send(SourceActionPhase phase,long gen,int state,double frame)=>events.Observe(new SourceActionNotice(phase,gen,state,frame,200,false,"hitstop-audit"));
                        void Drain(int expectedFrames)
                        {
                            double before=stop.ConsumedSeconds;float step=1f/hz;int ticks=0;double free=0;
                            Check(stop.Consume(0)==0,"Zero delta did not preserve pause");
                            while(stop.RemainingSeconds>0){free+=stop.Consume(step);if(++ticks>60)throw new Exception("Pause did not end");}
                            Check(Math.Abs(stop.ConsumedSeconds-before-expectedFrames/60.0)<1e-8,"Wrong consumed source duration");
                            Check(Math.Abs(ticks*(double)step-free-expectedFrames/60.0)<1e-7,"Lost residual simulation delta");
                        }
                        target.vulnerable=second.vulnerable=false;
                        Send(SourceActionPhase.Enter,1,46,0);Send(SourceActionPhase.Sample,1,46,24);
                        Check(stop.Requests==0&&stop.MissesSkipped==2,"Miss stopped the actor");
                        Send(SourceActionPhase.Leave,1,46,24);Send(SourceActionPhase.Exit,1,46,24);
                        target.vulnerable=second.vulnerable=true;
                        Send(SourceActionPhase.Enter,2,46,0);Send(SourceActionPhase.Sample,2,46,13);
                        Check(hits.Hits==2&&stop.Requests==1,"Multi-target hit multiplied pause");Drain(2);
                        Send(SourceActionPhase.Sample,2,46,13);Check(stop.RemainingSeconds==0,"Duplicate event restarted stop");
                        Send(SourceActionPhase.Sample,2,46,23);Drain(3);
                        Send(SourceActionPhase.Leave,2,46,23);Send(SourceActionPhase.Exit,2,46,23);
                        Send(SourceActionPhase.Enter,3,41,0);Send(SourceActionPhase.Sample,3,41,17);Drain(1);
                        Send(SourceActionPhase.Leave,3,41,17);Send(SourceActionPhase.Exit,3,41,17);
                        int requests=stop.Requests;
                        Send(SourceActionPhase.Enter,4,55,50);Send(SourceActionPhase.Sample,4,55,51);
                        Check(stop.Requests==requests&&stop.ZeroFrameHits==1&&stop.RemainingSeconds==0,"Custom zero was replaced by a nonzero standard");
                        Send(SourceActionPhase.Leave,4,55,51);Send(SourceActionPhase.Exit,4,55,51);
                        Send(SourceActionPhase.Enter,5,46,0);Send(SourceActionPhase.Sample,5,46,24);
                        Check(Math.Abs(stop.RemainingSeconds-3/60.0)<1e-9,"Overlapping stops summed instead of max remaining duration");
                        Drain(3);Check(Time.timeScale==scale,"Changed global time scale");
                        stop.Dispose();Send(SourceActionPhase.Leave,5,46,24);Send(SourceActionPhase.Exit,5,46,24);
                        Send(SourceActionPhase.Enter,6,46,0);Send(SourceActionPhase.Sample,6,46,24);
                        Check(stop.RemainingSeconds==0,"Disposed stop remained subscribed");
                        cases.Add(new JObject{["hz"]=hz,["requests"]=stop.Requests,["zeroFrameHits"]=stop.ZeroFrameHits,["consumedSeconds"]=stop.ConsumedSeconds,["pass"]=true});
                    }
                }
                UnityEngine.Object.DestroyImmediate(target.gameObject);UnityEngine.Object.DestroyImmediate(second.gameObject);UnityEngine.Object.DestroyImmediate(actor);
                report["pass"]=true;report["nativeSchedulerParity"]=false;report["targetHitstopImplemented"]=false;
                Debug.Log("REMIELLE_SOURCE_HITSTOP_PASS");
            }
            catch(Exception ex){report["error"]=ex.ToString();Debug.LogException(ex);}
            finally{File.WriteAllText(QualifiedIntegrationAudit.Output+"/source-hitstop-verification.json",report.ToString());}
            if(!(bool)report["pass"])EditorApplication.Exit(1);
        }
    }
}
