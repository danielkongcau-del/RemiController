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
    public static class SourceMotionPlaybackAudit
    {
        const string Main="Avatar_Female_Size02_RemielleOrigin_Controller";
        public static void Run()
        {
            string output=QualifiedIntegrationAudit.Output+"/source-motion-playback-verification.json";
            var report=new JObject{["pass"]=false};
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                string directory=Path.Combine(Application.streamingAssetsPath,"RemielleControllerMotions");
                var source=new NativeControllerSource(File.ReadAllText("Assets/ControllerRuntime/Data/source-controller-pack.json"));
                var controller=source.GetController(Main);
                var bank=new NativeMotionBank(directory);
                var bindings=new NativeControllerAvatarBindings(File.ReadAllText(Path.Combine(directory,"controller-avatar-bindings.json")),File.ReadAllText(Path.Combine(directory,"binding-profiles.json")));
                var profile=bindings.Resolve(controller);
                var actor=new GameObject("SourceMotionActor");
                actor.AddComponent<CapsuleCollider>();actor.AddComponent<CapsuleColliderCast>();
                var kcc=actor.AddComponent<KCCMovementEngine>();actor.GetComponent<Rigidbody>().isKinematic=true;kcc.Awake();
                var motor=actor.AddComponent<RemielleAuthoredMotor>();
                var visual=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/V3/Remielle_V3_Animated.prefab"));
                visual.transform.SetParent(actor.transform,false);
                var driver=visual.GetComponentInChildren<RemielleNativeAnimation>();
                var coverage=new JArray();report["sourceClips"]=coverage;
                int index=0;
                foreach(var state in controller["machines"][0]["states"])
                {
                    int id=index++;string name=(string)state["name"];
                    if(!(name.Contains("Walk")||name.Contains("Run")||name.Contains("Evade")||name=="Attack_Normal_01"||name=="Attack_Normal_01_End"||name=="Idle"))continue;
                    int tree=(int)state["blendTreeIndices"][0];
                    foreach(var node in state["trees"][tree]["nodes"])
                    {
                        if(((JArray)node["childIndices"]).Count!=0)continue;
                        int slot=(int)node["clipIndex"];
                        using(var sampler=new SourceMotionSampler(directory,bank,Main,slot,driver.nativeAnimation.transform,profile))
                        {
                            foreach(double t in new[]{0d,sampler.Duration*.5,(double)sampler.Duration})
                            {
                                sampler.Sample(t);driver.ApplyPose();
                                foreach(var bone in driver.bones)
                                    if(!float.IsFinite(bone.target.position.sqrMagnitude))throw new Exception("Nonfinite source pose "+name);
                            }
                            coverage.Add(new JObject{["state"]=id,["name"]=name,["slot"]=slot,["assetID"]=sampler.AssetID,["duration"]=sampler.Duration,["loop"]=sampler.Loop});
                        }
                    }
                }
                var rates=new JArray();report["loopTransport"]=rates;
                long sequence=1;float maxStep=0;
                foreach(int fps in new[]{30,60,144})
                {
                    actor.transform.position=Vector3.zero;Physics.SyncTransforms();
                    using(var sampler=new SourceMotionSampler(directory,bank,Main,10,driver.nativeAnimation.transform,profile))
                    using(var transport=new LoopingRootTransport(sampler,driver,visual.transform,motor,sequence))
                    {
                        if(!sampler.Loop)throw new Exception("Walk source is not looped");
                        int samples=fps*3;
                        double total=sampler.Duration*5d;
                        for(int i=1;i<=samples;i++)
                        {
                            var before=actor.transform.position;
                            transport.Sample(total*i/samples);
                            maxStep=Mathf.Max(maxStep,Vector3.Distance(before,actor.transform.position));
                        }
                        Vector3 expected=sampler.CycleDisplacement*5;
                        // The accepted rig maps original forward +Z to world +Z.
                        if(Vector3.Distance(actor.transform.position,expected)>.0001f)throw new Exception("Loop travel lost/doubled at "+fps);
                        if(sampler.CompletedCycles!=5||sampler.ClipTime!=0)throw new Exception("Loop boundary not exact");
                        // A single update may cross several full cycles.
                        transport.Sample(sampler.Duration*8d);
                        if(Vector3.Distance(actor.transform.position,sampler.CycleDisplacement*8)>.0001f)throw new Exception("Multi-cycle delta failed");
                        rates.Add(new JObject{["fpsLabel"]=fps,["samples"]=samples,["completedCycles"]=sampler.CompletedCycles,["actorZ"]=actor.transform.position.z});
                        sequence+=samples+1;
                    }
                }
                report["maxSingleStepBeforeMultiCycleJump"]=maxStep;
                var blends=new JArray();report["blendedTransport"]=blends;
                foreach(var pair in new[]{(10,43),(43,37),(37,0),(10,5),(5,28)})
                foreach(int rate in new[]{30,60,144})
                {
                    actor.transform.position=Vector3.zero;Physics.SyncTransforms();
                    using(var from=new SourceMotionSampler(directory,bank,Main,pair.Item1,driver.nativeAnimation.transform,profile))
                    using(var to=new SourceMotionSampler(directory,bank,Main,pair.Item2,driver.nativeAnimation.transform,profile))
                    using(var transport=new BlendedRootTransport(from,driver,visual.transform,motor,sequence,from.Loop?from.Duration*50d:from.Duration*.4))
                    {
                        double start=from.Loop?from.Duration*50d:from.Duration*.4;
                        var before=driver.bones.Select(x=>x.target.position).ToArray();
                        transport.BeginBlend(to,0);
                        for(int j=0;j<before.Length;j++)if(Vector3.Distance(before[j],driver.bones[j].target.position)>.0001f)throw new Exception("BeginBlend changed weight-zero pose");
                        int steps=(int)Math.Ceiling(rate*.2);float maxDelta=0;
                        for(int i=1;i<=steps;i++)
                        {
                            double elapsed=.2*((double)i/steps);
                            transport.Sample(start+elapsed,elapsed,(float)i/steps);
                            maxDelta=Mathf.Max(maxDelta,motor.LastAppliedDelta.magnitude);
                        }
                        if(maxDelta>1f)throw new Exception("Unrelated motion origins caused a blend launch");
                        var atEnd=driver.bones.Select(x=>x.target.position).ToArray();
                        transport.CompleteBlend();transport.Sample(.2);
                        float maxHandoff=0;
                        for(int j=0;j<atEnd.Length;j++)maxHandoff=Mathf.Max(maxHandoff,Vector3.Distance(atEnd[j],driver.bones[j].target.position));
                        if(maxHandoff>.0001f||motor.LastAppliedDelta.magnitude>.0001f)throw new Exception("Blend completion doubled travel or jumped pose");
                        // Independent unblended destination evaluation at the same
                        // time must match every target, including default channels.
                        to.Sample(.2);driver.ApplyPose();float maxEndpoint=0;
                        for(int j=0;j<atEnd.Length;j++)maxEndpoint=Mathf.Max(maxEndpoint,Vector3.Distance(atEnd[j],driver.bones[j].target.position));
                        if(maxEndpoint>.0001f)throw new Exception("Full destination weight differs from direct sampling");
                        blends.Add(new JObject{["fromSlot"]=pair.Item1,["toSlot"]=pair.Item2,["rate"]=rate,["steps"]=steps,["maxMotorStep"]=maxDelta,["handoffError"]=maxHandoff,["endpointError"]=maxEndpoint});
                        sequence+=steps+1;
                    }
                }
                report["scope"]="Full-tier runtime ACL source slots, actual original Avatar binding and looping root transport; editor sampling, no gameplay-input parity claim";
                report["pass"]=true;
                UnityEngine.Object.DestroyImmediate(visual);UnityEngine.Object.DestroyImmediate(actor);
                Debug.Log("REMIELLE_SOURCE_MOTION_PLAYBACK_PASS "+coverage.Count);
            }
            catch(Exception ex){report["error"]=ex.ToString();Debug.LogException(ex);}
            finally{File.WriteAllText(output,report.ToString());}
            if(!(bool)report["pass"])EditorApplication.Exit(1);
        }
    }
}
