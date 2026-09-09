using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Remielle.ControllerRuntime;
using UnityEditor;
using UnityEngine;

namespace Remielle.Controller.Editor
{
    public static class SourceFrameEventsAudit
    {
        const string Main="Avatar_Female_Size02_RemielleOrigin_Controller";
        public static void Run()
        {
            var report=new JObject{["pass"]=false};var cases=new JArray();report["cases"]=cases;
            try
            {
                var source=new NativeControllerSource(File.ReadAllText("Assets/ControllerRuntime/Data/source-controller-pack.json"));
                string json=File.ReadAllText("Assets/ControllerIntegration/Data/source-action-events.json");
                void Check(bool condition,string message){if(!condition)throw new Exception(message);}
                void Case(string name,Action<SourceFrameEvents,List<SourceFrameEvents.Emission>,List<long>> test)
                {
                    var host=new SourceFrameEvents(json,source,Main);var emitted=new List<SourceFrameEvents.Emission>();var released=new List<long>();
                    host.Emitted+=emitted.Add;host.Released+=(id,reason)=>released.Add(id);test(host,emitted,released);
                    cases.Add(new JObject{["name"]=name,["events"]=emitted.Count,["releases"]=released.Count,["remainingScopes"]=host.ActiveScopes});
                }
                void Send(SourceFrameEvents h,SourceActionPhase phase,int state,double frame,long id=1,double length=100,bool loop=false)=>
                    h.Observe(new SourceActionNotice(phase,id,state,frame,length,loop,"audit"));
                Case("attack-crossed-frames-order-no-duplicates",(h,e,r)=>{
                    Send(h,SourceActionPhase.Enter,46,0);Send(h,SourceActionPhase.Sample,46,24);Send(h,SourceActionPhase.Sample,46,24);
                    Check(e.Select(x=>x.Index).SequenceEqual(new[]{2,0,1}),"Attack entries should be skill-start then frames 13/23 exactly once");
                    Check(e.Select(x=>x.AuthoredFrame).SequenceEqual(new double[]{0,13,23}),"Attack frame mapping changed");
                    Send(h,SourceActionPhase.Exit,46,24);Check(h.ActiveScopes==0&&r.Count==1,"Attack owner leaked");
                });
                Case("offset-only-catches-force-in",(h,e,r)=>{
                    Send(h,SourceActionPhase.Enter,46,20);Send(h,SourceActionPhase.Sample,46,24);
                    Check(e.Select(x=>x.Index).SequenceEqual(new[]{2,1}),"Offset replayed a skipped attack or lost skill start");
                });
                var pack=JObject.Parse(json);
                var future=pack["states"].SelectMany(s=>s["entries"].Select(e=>(state:(int)s["state"],entry:e)))
                    .First(x=>(bool)x.entry["forceIn"]&&(int)x.entry["frame"]>0&&!(bool)x.entry["maxFrame"]);
                Case("future-force-in-keeps-authored-time",(h,e,r)=>{
                    Send(h,SourceActionPhase.Enter,future.state,0);int index=(int)future.entry["index"];double due=(int)future.entry["frame"];
                    Check(!e.Any(x=>x.Index==index),"Future forceIn fired at zero");Send(h,SourceActionPhase.Sample,future.state,due);
                    Check(e.Count(x=>x.Index==index)==1,"Future forceIn did not fire at authored frame");
                });
                Case("early-exit-forced-camera-cleanup-once",(h,e,r)=>{
                    Send(h,SourceActionPhase.Enter,25,0);Send(h,SourceActionPhase.Sample,25,9);Send(h,SourceActionPhase.Leave,25,10);
                    Send(h,SourceActionPhase.Sample,25,50);Send(h,SourceActionPhase.Exit,25,50);
                    Check(e.Select(x=>x.Index).SequenceEqual(new[]{0,1}),"Leaving action emitted ordinary future events or missed camera cleanup");
                    Check(e[1].Reason.StartsWith("leave:")&&r.Count==1&&h.ActiveScopes==0,"Forced camera cleanup was not released once");
                });
                Case("natural-cleanup-not-repeated-on-exit",(h,e,r)=>{
                    Send(h,SourceActionPhase.Enter,25,0);Send(h,SourceActionPhase.Sample,25,39);Send(h,SourceActionPhase.Exit,25,39);
                    Check(e.Count(x=>x.Index==1)==1&&e.Single(x=>x.Index==1).Reason=="frame","Natural camera exit duplicated");
                });
                Case("max-frame-and-cancel-cleanup",(h,e,r)=>{
                    Send(h,SourceActionPhase.Enter,29,0);Send(h,SourceActionPhase.Sample,29,100);Send(h,SourceActionPhase.Exit,29,100);
                    Check(e.Count(x=>x.Index==2)==1&&e.Count(x=>x.Index==4)==1,"Terminal cleanup absent or duplicated");
                    Check(e.Where(x=>x.Index==2||x.Index==4).All(x=>x.AuthoredFrame==100&&x.Reason=="frame"),"maxFrame did not follow actual terminal frame");
                });
                Case("self-reentry-uses-independent-generation",(h,e,r)=>{
                    Send(h,SourceActionPhase.Enter,29,0);Send(h,SourceActionPhase.Leave,29,12);Send(h,SourceActionPhase.Enter,29,0,2);
                    Send(h,SourceActionPhase.Exit,29,20);Send(h,SourceActionPhase.Exit,29,4,2);
                    Check(e.GroupBy(x=>x.Generation).All(g=>g.Select(x=>x.Index).OrderBy(x=>x).SequenceEqual(new[]{0,1,2,3,4})),"Self reentry cross-cancelled another action");
                    Check(r.SequenceEqual(new long[]{1,2})&&h.ActiveScopes==0,"Self-reentry owner cleanup leaked");
                });
                Case("loop-catch-up-pause-and-boundary",(h,e,r)=>{
                    Send(h,SourceActionPhase.Enter,46,0,1,40,true);Send(h,SourceActionPhase.Sample,46,120,1,40,true);
                    Send(h,SourceActionPhase.Sample,46,120,1,40,true);
                    Check(e.Count==10&&e.Count(x=>x.Index==2)==4&&e.Count(x=>x.Index==0)==3&&e.Count(x=>x.Index==1)==3,"Loop skipped events or repeated paused boundary");
                    Check(e.Select(x=>x.AuthoredFrame).SequenceEqual(e.Select(x=>x.AuthoredFrame).OrderBy(x=>x)),"Loop catch-up order differs");
                });
                Case("rewind-and-generation-reuse-rejected",(h,e,r)=>{
                    Send(h,SourceActionPhase.Enter,46,0);Send(h,SourceActionPhase.Sample,46,10);
                    bool rewind=false,reuse=false;try{Send(h,SourceActionPhase.Sample,46,9);}catch(InvalidOperationException){rewind=true;}
                    Send(h,SourceActionPhase.Exit,46,10);try{Send(h,SourceActionPhase.Enter,46,0);}catch(InvalidOperationException){reuse=true;}
                    Check(rewind&&reuse,"Stale action input accepted");
                });
                // The fixtures above use real source event lists with deliberate
                // clocks, not evidence of the game's native callback scheduler.
                report["pass"]=true;report["nativeCallbackParityVerified"]=false;
                Debug.Log("REMIELLE_SOURCE_FRAME_EVENTS_PASS "+cases.Count);
            }
            catch(Exception ex){report["error"]=ex.ToString();Debug.LogException(ex);}
            finally{File.WriteAllText(QualifiedIntegrationAudit.Output+"/source-frame-events-verification.json",report.ToString());}
            if(!(bool)report["pass"])EditorApplication.Exit(1);
        }
    }
}
