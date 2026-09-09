using System;
using System.IO;
using Newtonsoft.Json.Linq;
using Remielle.ControllerRuntime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Remielle.Controller.Editor
{
    public static class SimplifiedCombatBuild
    {
        public static void Run()
        {
            var report=new JObject{["pass"]=false};var cases=new JArray();report["cases"]=cases;
            void Check(bool ok,string message){if(!ok)throw new Exception(message);}
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                var pack=SourceCameraBuild.Prepare("Assets/ControllerIntegration/Data/source-special-resource.json").text;
                var source=new NativeControllerSource(File.ReadAllText("Assets/ControllerRuntime/Data/source-controller-pack.json"));
                var eventJson=File.ReadAllText("Assets/ControllerIntegration/Data/source-action-events.json");
                const string name="Avatar_Female_Size02_RemielleOrigin_Controller";
                foreach(int hz in new[]{30,60,120})foreach(int n in new[]{1,2,3})
                {
                    var events=new SourceFrameEvents(eventJson,source,name);
                    var parameters=source.CreateParameters(name);
                    using var resource=new SourceSpecialResource(pack,parameters,events,n);
                    long generation=0;
                    void Cast(int state,double endFrame)
                    {
                        long id=++generation;
                        void Send(SourceActionPhase phase,double frame)=>events.Observe(new SourceActionNotice(phase,id,state,frame,180,false,"simplified-combat-audit"));
                        Send(SourceActionPhase.Enter,0);
                        for(double frame=60.0/hz;frame<endFrame;frame+=60.0/hz){Send(SourceActionPhase.Sample,frame);Send(SourceActionPhase.Sample,frame);}
                        Send(SourceActionPhase.Sample,endFrame);Send(SourceActionPhase.Sample,endFrame);
                        Send(SourceActionPhase.Leave,endFrame);Send(SourceActionPhase.Exit,endFrame);
                    }
                    Cast(55,13);Check(resource.Energy==60&&resource.Charges==0,"Early EX cancellation consumed a charge");
                    Cast(55,14);Check(resource.Energy==0&&resource.Charges==1,"EX did not consume its charge at frame 14");
                    Cast(54,18);Check(resource.NormalSpecialUses==0,"Cancelled normal special counted before frame 19");
                    for(int i=1;i<=n;i++)
                    {
                        Cast(54,30);
                        Check(resource.NormalSpecialUses==i,"A multi-hit normal special counted more than once");
                        Check(resource.Energy==(i==n?60:0),"Recharge occurred at the wrong cast count");
                        Check(resource.NormalSpecialsTowardCharge==(i==n?0:i),"Recharge progress differs");
                    }
                    Check(resource.Recharges==1&&resource.TotalRestored==60&&resource.BranchIndex==1,"Recharge did not publish the EX selector parameter");
                    Cast(54,30);Check(resource.Energy==60&&resource.Recharges==1&&resource.NormalSpecialsTowardCharge==0,"Full bank accumulated an extra charge");
                    Cast(55,14);Check(resource.Charges==2&&resource.Energy==0,"Recharged EX failed to spend");
                    int uses=resource.NormalSpecialUses;resource.Dispose();Cast(54,30);
                    Check(resource.NormalSpecialUses==uses,"Disposed recharge owner remained subscribed");
                    cases.Add(new JObject{["hz"]=hz,["normalSpecialsPerCharge"]=n,["recharges"]=resource.Recharges,["normalSpecialUses"]=uses,["noHitQueryRequired"]=true,["pass"]=true});
                }
                var hitEvents=new SourceFrameEvents(eventJson,source,name);
                var actor=new GameObject("FixedDamageActor");
                var target=SourceTrainingTarget.Create("FixedDamageTarget",new Vector3(0,1,2));target.ResetHealth(100);
                var extra=new GameObject("ExtraHurtbox");extra.layer=SourceHitQuery.TargetLayer;extra.transform.SetParent(target.transform,false);extra.AddComponent<SphereCollider>().isTrigger=true;
                using(var hit=new SourceHitQuery(SourceCameraBuild.Prepare("Assets/ControllerIntegration/Data/source-hit-query.json").text,hitEvents,actor.transform))
                using(var damage=new SourceTrainingDamage(hit))
                {
                    void Send(long id,SourceActionPhase phase,double frame)=>hitEvents.Observe(new SourceActionNotice(phase,id,46,frame,90,false,"fixed-damage-audit"));
                    Send(1,SourceActionPhase.Enter,0);Send(1,SourceActionPhase.Sample,24);Send(1,SourceActionPhase.Sample,24);
                    Check(hit.Hits==2&&damage.Applications==2&&damage.TotalApplied==2&&target.Health==98&&target.StaggerTaken==2,"Fixed one-point hit rule or target deduplication failed");
                    Send(1,SourceActionPhase.Exit,24);target.transform.position=new Vector3(100,1,0);
                    Send(2,SourceActionPhase.Enter,0);Send(2,SourceActionPhase.Sample,24);Send(2,SourceActionPhase.Exit,24);
                    Check(hit.Misses==2&&damage.TotalApplied==2,"Miss applied training damage");
                    damage.Dispose();target.transform.position=new Vector3(0,1,2);
                    Send(3,SourceActionPhase.Enter,0);Send(3,SourceActionPhase.Sample,24);Send(3,SourceActionPhase.Exit,24);
                    Check(target.Health==98,"Disposed damage owner remained subscribed");
                    report["fixedDamageApplications"]=damage.Applications;report["fixedDamage"]=damage.TotalApplied;report["fixedStagger"]=damage.TotalStagger;
                }
                UnityEngine.Object.DestroyImmediate(target.gameObject);UnityEngine.Object.DestroyImmediate(actor);
                report["pass"]=true;report["nativeRechargeFormulaUsed"]=false;report["receiverStateMachineAdded"]=false;
                Debug.Log("REMIELLE_SIMPLIFIED_COMBAT_PASS");
            }
            catch(Exception ex){report["error"]=ex.ToString();Debug.LogException(ex);}
            finally{File.WriteAllText(QualifiedIntegrationAudit.Output+"/simplified-combat-verification.json",report.ToString());}
            if(!(bool)report["pass"]){EditorApplication.Exit(1);return;}
            SourceControllerBuild.BuildPlayer();
        }
    }
}
