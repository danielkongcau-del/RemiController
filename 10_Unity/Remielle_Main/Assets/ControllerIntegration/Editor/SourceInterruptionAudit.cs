using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Remielle.ControllerRuntime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using nickmaltbie.OpenKCC.Character;
using nickmaltbie.OpenKCC.Utils.ColliderCast;

namespace Remielle.Controller.Editor
{
    public static class SourceInterruptionAudit
    {
        const string Main="Avatar_Female_Size02_RemielleOrigin_Controller";
        static void Check(bool value,string name){if(!value)throw new Exception(name);}
        public static void Run()
        {
            var report=new JObject{["pass"]=false};var poses=new JArray();
            try
            {
                var vectors=JObject.Parse(File.ReadAllText(QualifiedIntegrationAudit.Output+"/action-rule-evidence/interruption-queue-vectors.json"));
                int verified=0;
                foreach(var v in vectors["vectors"])
                {
                    var plan=SourceTransitionQueue.Plan((bool)v["active"],(int)v["intr"],(bool)v["ordered"],
                        (int)v["origin"],(int)v["edge"],(bool)v["priorInTransition"],(bool)v["interruptionActive"],
                        (int)v["counts"][0],(int)v["counts"][1],(int)v["counts"][2]);
                    var actual=new JArray(plan.Select(e=>new JObject{["kind"]=e.Kind,["count"]=e.Count}));
                    Check(JToken.DeepEquals(actual,v["expected"]),"Original planner differs: "+v);verified++;
                }
                report["nativeQueueCases"]=verified;report["nativeCodeSha256"]=vectors["codeSha256"];
                var source=new NativeControllerSource(File.ReadAllText("Assets/ControllerRuntime/Data/source-controller-pack.json"));
                string directory=Path.Combine(Application.streamingAssetsPath,"RemielleControllerMotions");
                var avatars=new NativeControllerAvatarBindings(File.ReadAllText(Path.Combine(directory,"controller-avatar-bindings.json")),File.ReadAllText(Path.Combine(directory,"binding-profiles.json")));
                var definition=source.GetController(Main);var profile=avatars.Resolve(definition);var bank=new NativeMotionBank(directory);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                foreach(int hz in new[]{30,60,120})
                {
                    var actor=new GameObject("InterruptionContinuityActor");actor.AddComponent<CapsuleCollider>();actor.AddComponent<CapsuleColliderCast>();
                    var engine=actor.AddComponent<KCCMovementEngine>();actor.GetComponent<Rigidbody>().isKinematic=true;engine.Awake();
                    var motor=actor.AddComponent<RemielleAuthoredMotor>();
                    var visual=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/V3/Remielle_V3_Animated.prefab"));visual.transform.SetParent(actor.transform,false);
                    var driver=visual.GetComponentInChildren<RemielleNativeAnimation>();
                    SourceMotionSampler Motion(int state)
                    {
                        var s=definition["machines"][0]["states"][state];int tree=(int)s["blendTreeIndices"][(int)definition["layers"][0]["smms"]];
                        return new SourceMotionSampler(directory,bank,Main,(int)s["trees"][tree]["nodes"][0]["clipIndex"],driver.nativeAnimation.transform,profile);
                    }
                    using(var a=Motion(13))using(var b=Motion(8))using(var c=Motion(46))using(var d=Motion(9))
                    using(var transport=new BlendedRootTransport(a,driver,visual.transform,motor,1,a.Duration*8))
                    {
                        double time=a.Duration*8,dt=1d/hz;
                        transport.BeginBlend(b,0);
                        for(int i=1;i<=3;i++){time+=dt;transport.Sample(time,i*dt,i/10f);}
                        float maxPosition=0,maxAngle=0,maxMorph=0,maxActor=0;
                        foreach(var destination in new[]{c,d})
                        {
                            var before=driver.bones.Select(x=>x.target.localToWorldMatrix).ToArray();
                            var morphs=driver.morphs.SelectMany(x=>Enumerable.Range(0,x.source.sharedMesh.blendShapeCount).Select(i=>(r:x.source,i))).ToArray();
                            var weights=morphs.Select(x=>x.r.GetBlendShapeWeight(x.i)).ToArray();
                            var actorPosition=actor.transform.position;
                            var snapshot=transport.CaptureInterruption();
                            // Model loading can reset the shared source rig. The
                            // already captured pose must survive that operation.
                            destination.Sample(.5);
                            transport.InterruptBlend(destination,0,snapshot);transport.Sample(time,0,0);
                            for(int i=0;i<before.Length;i++)
                            {
                                var after=driver.bones[i].target.localToWorldMatrix;
                                maxPosition=Mathf.Max(maxPosition,Vector3.Distance(before[i].GetColumn(3),after.GetColumn(3)));
                                maxAngle=Mathf.Max(maxAngle,Quaternion.Angle(before[i].rotation,after.rotation));
                            }
                            for(int i=0;i<morphs.Length;i++)maxMorph=Mathf.Max(maxMorph,Mathf.Abs(weights[i]-morphs[i].r.GetBlendShapeWeight(morphs[i].i)));
                            maxActor=Mathf.Max(maxActor,Vector3.Distance(actorPosition,actor.transform.position));
                            int samples=ReferenceEquals(destination,d)?10:3;
                            for(int i=1;i<=samples;i++){time+=dt;transport.Sample(time,i*dt,i/10f);}
                        }
                        Check(maxPosition<.00005f&&maxAngle<.1f&&maxMorph<.001f&&maxActor<.000001f,"Interruption introduced pose/root jump");
                        var endpoint=driver.bones.Select(x=>x.target.localToWorldMatrix).ToArray();var beforeComplete=actor.transform.position;
                        transport.CompleteBlend();transport.Sample(10*dt);
                        float completionError=driver.bones.Select((x,i)=>Vector3.Distance(endpoint[i].GetColumn(3),x.target.position)).Max();
                        Check(completionError<.00005f&&Vector3.Distance(beforeComplete,actor.transform.position)<.000001f,"Completion applied travel twice");
                        poses.Add(new JObject{["hz"]=hz,["bones"]=beforeComplete==actor.transform.position?driver.bones.Length:0,
                            ["interruptions"]=2,["maxPositionError"]=maxPosition,["maxAngleError"]=maxAngle,["maxMorphError"]=maxMorph,["maxActorJump"]=maxActor,["completionError"]=completionError});
                    }
                    UnityEngine.Object.DestroyImmediate(actor);
                }
                report["poseCases"]=poses;report["pass"]=true;
                report["scope"]="Original candidate-list order/count; actual rig frozen-pose interruption continuity. Not native graph velocity, cross-layer callbacks or input-expiry parity.";
                Debug.Log("REMIELLE_INTERRUPTION_PASS "+verified);
            }
            catch(Exception ex){report["error"]=ex.ToString();Debug.LogException(ex);}
            finally{File.WriteAllText(QualifiedIntegrationAudit.Output+"/interruption-verification.json",report.ToString());}
            if(!(bool)report["pass"]){EditorApplication.Exit(1);return;}
            SourceActionSessionAudit.Run();
            QualifiedIntegrationAudit.Run();
        }
    }
}
