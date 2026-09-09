using System;
using System.IO;
using Newtonsoft.Json.Linq;
using Remielle.ControllerRuntime;
using UnityEditor;
using UnityEngine;

namespace Remielle.Controller.Editor
{
    public static class SourceInputBufferBuild
    {
        public static void Run()
        {
            var report=new JObject{["pass"]=false};var cases=new JArray();report["cases"]=cases;
            void Check(bool ok,string message){if(!ok)throw new Exception(message);}
            try
            {
                var source=new NativeControllerSource(File.ReadAllText("Assets/ControllerRuntime/Data/source-controller-pack.json"));
                foreach(int hz in new[]{30,60,120})
                {
                    var p=source.CreateParameters("Avatar_Female_Size02_RemielleOrigin_Controller");
                    uint attack=p.Hash("Trigger_PressAttackA"),special=p.Hash("Trigger_PressAttackB");
                    using var buffer=new SourceInputBuffer(p,.2f);
                    buffer.Press(attack);for(int i=0;i<hz/5;i++)buffer.Advance(1f/hz);
                    Check(p.GetTrigger(attack)&&buffer.PendingCount==1,"Input expired before its inclusive boundary");
                    buffer.Advance(1f/hz);Check(!p.GetTrigger(attack)&&buffer.Expired==1,"Stale input survived expiry");
                    buffer.Press(attack);for(int i=0;i<hz;i++)buffer.Advance(0);
                    Check(p.GetTrigger(attack)&&buffer.Expired==1,"Hitstop/pause consumed input lifetime");
                    for(int i=0;i<hz/10;i++)buffer.Advance(1f/hz);
                    buffer.Press(attack);Check(buffer.Refreshed==1,"A fresh press did not refresh its lifetime");
                    for(int i=0;i<hz/5;i++)buffer.Advance(1f/hz);
                    Check(p.GetTrigger(attack),"Refreshed press expired at the old deadline");
                    buffer.Press(special);p.ResetTrigger(attack);buffer.AfterCommit();
                    Check(buffer.Consumed==1&&buffer.PendingCount==1&&p.GetTrigger(special),"Commit consumed an unrelated pending intent");
                    buffer.Clear();buffer.Advance(1);Check(!p.GetTrigger(attack)&&!p.GetTrigger(special)&&buffer.PendingCount==0,"Disable/clear left a deferred input");
                    buffer.Press(attack);buffer.Dispose();Check(!p.GetTrigger(attack),"Dispose retained an input");
                    cases.Add(new JObject{["hz"]=hz,["expired"]=buffer.Expired,["consumed"]=buffer.Consumed,["refreshed"]=buffer.Refreshed,["pass"]=true});
                }
                var zeroParameters=source.CreateParameters("Avatar_Female_Size02_RemielleOrigin_Controller");
                uint zeroHash=zeroParameters.Hash("Trigger_PressAttackA");
                using(var zero=new SourceInputBuffer(zeroParameters,0))
                {zero.Press(zeroHash);Check(zeroParameters.GetTrigger(zeroHash),"Zero duration lost the current-tick press");zero.Advance(.001f);Check(!zeroParameters.GetTrigger(zeroHash),"Zero duration retained an old press");}
                report["pass"]=true;report["durationSeconds"]=.2;report["nativeBufferDurationClaimed"]=false;
                Debug.Log("REMIELLE_INPUT_BUFFER_PASS");
            }
            catch(Exception ex){report["error"]=ex.ToString();Debug.LogException(ex);}
            finally{File.WriteAllText(QualifiedIntegrationAudit.Output+"/input-buffer-verification.json",report.ToString());}
            if(!(bool)report["pass"]){EditorApplication.Exit(1);return;}
            SourceControllerBuild.BuildPlayer();
        }
    }
}
