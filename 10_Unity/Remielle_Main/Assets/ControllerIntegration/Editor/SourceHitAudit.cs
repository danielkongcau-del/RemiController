using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Remielle.ControllerRuntime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Remielle.Controller.Editor
{
    public static class SourceHitAudit
    {
        public static void Run()
        {
            var report=new JObject{["pass"]=false};var cases=new JArray();report["cases"]=cases;
            void Check(bool ok,string error){if(!ok)throw new Exception(error);}
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                string pack=SourceCameraBuild.Prepare("Assets/ControllerIntegration/Data/source-hit-query.json").text;
                var source=new NativeControllerSource(File.ReadAllText("Assets/ControllerRuntime/Data/source-controller-pack.json"));
                var rows=JObject.Parse(pack)["attacks"];
                var fan=(JObject)rows.First(r=>(int)r["state"]==46&&(int)r["entry"]==0);
                var box=(JObject)rows.First(r=>(int)r["state"]==54&&(int)r["entry"]==0);
                Check(SourceHitQuery.Intersects(fan,new Vector3(4.8f,2,0),.45f),"Cylinder rim contact lost");
                Check(!SourceHitQuery.Intersects(fan,new Vector3(4.9f,2.4f,0),.45f),"Cylinder corner broadphase false positive");
                Check(!SourceHitQuery.Intersects(fan,new Vector3(4,1,4),.45f),"Square broadphase became a circular hit");
                Check(SourceHitQuery.Intersects(box,new Vector3(0,1.5f,39.4f),.45f),"Forward box endpoint lost");
                Check(!SourceHitQuery.Intersects(box,new Vector3(0,1.5f,39.6f),.45f),"Forward box exceeded endpoint");
                foreach(int hz in new[]{30,60,120})
                {
                    var actor=new GameObject("HitAuditActor");
                    var capsule=actor.AddComponent<CapsuleCollider>();capsule.center=new Vector3(0,.9f,0);capsule.height=1.8f;capsule.radius=.3f;
                    var targets=Enumerable.Range(0,40).Select(i=>SourceTrainingTarget.Create("Target"+i,new Vector3(0,1,2))).ToArray();
                    // Multiple hurtboxes on one owner must not multiply hits.
                    var extra=new GameObject("SecondHurtbox");extra.layer=SourceHitQuery.TargetLayer;extra.transform.SetParent(targets[0].transform,false);
                    extra.AddComponent<SphereCollider>().isTrigger=true;
                    var outside=SourceTrainingTarget.Create("Outside",new Vector3(4,1,4));
                    var events=new SourceFrameEvents(File.ReadAllText("Assets/ControllerIntegration/Data/source-action-events.json"),source,"Avatar_Female_Size02_RemielleOrigin_Controller");
                    using(var hits=new SourceHitQuery(pack,events,actor.transform))
                    {
                        void Send(SourceActionPhase phase,long id,double frame)=>events.Observe(new SourceActionNotice(phase,id,46,frame,90,false,"hit-audit"));
                        Send(SourceActionPhase.Enter,1,0);Send(SourceActionPhase.Leave,1,12);Send(SourceActionPhase.Exit,1,12);
                        Check(hits.Queries==0,"Cancelled first attack emitted future hits");
                        Send(SourceActionPhase.Enter,2,0);
                        for(int i=1;i<=hz;i++)
                        {
                            double frame=i*60.0/hz;Send(SourceActionPhase.Sample,2,frame);
                            int expected=frame<13?0:frame<23?40:80;
                            Check(hits.Hits==expected,"Cross-frame hit count differs at "+hz+" Hz / "+frame);
                            Send(SourceActionPhase.Sample,2,frame);Check(hits.Hits==expected,"Repeated sample duplicated hits");
                        }
                        Check(outside.Hits==0&&targets.All(t=>t.Hits==2),"Owner deduplication or narrowphase failed");
                        Check(hits.BufferCapacity>41,"Truncated overlap buffer");
                        Send(SourceActionPhase.Leave,2,60);Send(SourceActionPhase.Exit,2,60);
                        foreach(var target in targets)target.vulnerable=false;
                        Send(SourceActionPhase.Enter,3,0);Send(SourceActionPhase.Sample,3,24);
                        Check(hits.Hits==80&&hits.Misses==2,"Invulnerable target or miss handling failed");
                        Send(SourceActionPhase.Leave,3,24);Send(SourceActionPhase.Exit,3,24);
                        targets[0].vulnerable=true;
                        var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.transform.position=new Vector3(0,1,1);wall.transform.localScale=new Vector3(2,2,.2f);
                        Send(SourceActionPhase.Enter,4,0);Send(SourceActionPhase.Sample,4,24);
                        Check(hits.Hits==80&&hits.WallRejected>0,"Occluding wall allowed a hit");
                        UnityEngine.Object.DestroyImmediate(wall);
                        Send(SourceActionPhase.Leave,4,24);Send(SourceActionPhase.Exit,4,24);
                        hits.Dispose();Send(SourceActionPhase.Enter,5,0);Send(SourceActionPhase.Sample,5,24);
                        Check(hits.Hits==80,"Disposed hit owner remained subscribed");
                        cases.Add(new JObject{["hz"]=hz,["hits"]=hits.Hits,["queries"]=hits.Queries,["misses"]=hits.Misses,["capacity"]=hits.BufferCapacity,["wallRejected"]=hits.WallRejected,["pass"]=true});
                    }
                    var boxEvents=new SourceFrameEvents(File.ReadAllText("Assets/ControllerIntegration/Data/source-action-events.json"),source,"Avatar_Female_Size02_RemielleOrigin_Controller");
                    using(var hits=new SourceHitQuery(pack,boxEvents,actor.transform))
                    {
                        boxEvents.Observe(new SourceActionNotice(SourceActionPhase.Enter,1,54,0,90,false,"self-filter"));
                        boxEvents.Observe(new SourceActionNotice(SourceActionPhase.Sample,1,54,26,90,false,"self-filter"));
                        Check(hits.Hits==4&&hits.WallRejected==0,"Rear-origin box was occluded by the attacker's own capsule");
                        ((JObject)cases.Last)["rearOriginBoxHits"]=hits.Hits;
                    }
                    foreach(var target in targets)UnityEngine.Object.DestroyImmediate(target.gameObject);
                    UnityEngine.Object.DestroyImmediate(outside.gameObject);UnityEngine.Object.DestroyImmediate(actor);
                }
                report["pass"]=true;report["sourceAttackEntries"]=rows.Count();report["nativeCoordinateParity"]=false;report["damageAndEnergyResolved"]=false;
                Debug.Log("REMIELLE_SOURCE_HIT_PASS");
            }
            catch(Exception ex){report["error"]=ex.ToString();Debug.LogException(ex);}
            finally{File.WriteAllText(QualifiedIntegrationAudit.Output+"/source-hit-verification.json",report.ToString());}
            if(!(bool)report["pass"])EditorApplication.Exit(1);
        }
    }
}
