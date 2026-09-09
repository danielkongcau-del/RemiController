using System;
using System.IO;
using Newtonsoft.Json.Linq;
using Remielle.ControllerRuntime;
using UnityEditor;
using UnityEngine;

namespace Remielle.Controller.Editor
{
    public static class SourceSpecialAudit
    {
        public static void Run()
        {
            var report=new JObject{["pass"]=false};var cases=new JArray();report["cases"]=cases;
            void Check(bool pass,string message){if(!pass)throw new Exception(message);}
            try
            {
                string pack=SourceCameraBuild.Prepare("Assets/ControllerIntegration/Data/source-special-resource.json").text;
                var source=new NativeControllerSource(File.ReadAllText("Assets/ControllerRuntime/Data/source-controller-pack.json"));
                const string name="Avatar_Female_Size02_RemielleOrigin_Controller";
                foreach(int hz in new[]{30,60,120})
                {
                    var p=source.CreateParameters(name);var events=new SourceFrameEvents(File.ReadAllText("Assets/ControllerIntegration/Data/source-action-events.json"),source,name);
                    using(var resource=new SourceSpecialResource(pack,p,events))
                    {
                        Check(resource.Energy==60&&p.GetInt(p.Hash("Int_BranchIndex"))==1,"Startup source grant not reflected in selector input");
                        resource.SetEnergy(59.999f);Check(resource.BranchIndex==0,"Below-threshold SP chose EX");
                        resource.SetEnergy(60);Check(resource.BranchIndex==1,"Exact threshold failed EX condition");
                        resource.SetEnergy(60.001f);Check(resource.BranchIndex==1,"Above-threshold SP failed EX condition");resource.SetEnergy(60);
                        void Send(SourceActionPhase phase,long id,double frame)=>events.Observe(new SourceActionNotice(phase,id,55,frame,180,false,"special-audit"));
                        Send(SourceActionPhase.Enter,1,0);Send(SourceActionPhase.Leave,1,13);Send(SourceActionPhase.Exit,1,13);
                        Check(resource.Charges==0&&resource.Energy==60,"Interrupted action charged a future non-forced event");
                        Send(SourceActionPhase.Enter,2,0);
                        for(int i=1;i<=hz;i++)
                        {
                            double frame=i*60.0/hz;Send(SourceActionPhase.Sample,2,frame);
                            Check(resource.Energy==(frame<14?60:0),"SP spend frame differs at "+hz+" Hz");
                            Send(SourceActionPhase.Sample,2,frame);
                        }
                        Check(resource.Charges==1&&resource.TotalSpent==60&&p.GetInt(p.Hash("Int_BranchIndex"))==0,"SP event duplicated or branch was not updated");
                        Send(SourceActionPhase.Exit,2,60);resource.SetEnergy(120);Send(SourceActionPhase.Enter,3,0);Send(SourceActionPhase.Sample,3,14);
                        Check(resource.Energy==60&&resource.Charges==2&&resource.BranchIndex==1,"Independent source generation failed cost/equality handling");
                        Send(SourceActionPhase.Exit,3,14);resource.Dispose();Send(SourceActionPhase.Enter,4,0);Send(SourceActionPhase.Sample,4,14);
                        Check(resource.Charges==2&&resource.Energy==60,"Disposed resource remained subscribed");
                        cases.Add(new JObject{["hz"]=hz,["charges"]=resource.Charges,["spent"]=resource.TotalSpent,["pass"]=true});
                    }
                }
                report["pass"]=true;report["streamingGameCooldownImplemented"]=false;report["energyRecoveryImplemented"]=false;
                Debug.Log("REMIELLE_SOURCE_SPECIAL_PASS");
            }
            catch(Exception ex){report["error"]=ex.ToString();Debug.LogException(ex);}
            finally{File.WriteAllText(QualifiedIntegrationAudit.Output+"/source-special-verification.json",report.ToString());}
            if(!(bool)report["pass"])EditorApplication.Exit(1);
        }
    }
}
