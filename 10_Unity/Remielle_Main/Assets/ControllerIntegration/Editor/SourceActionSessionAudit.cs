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
    public static class SourceActionSessionAudit
    {
        const string Main="Avatar_Female_Size02_RemielleOrigin_Controller";
        public static void Run()
        {
            var report=new JObject{["pass"]=false};var cases=new JArray();report["cases"]=cases;
            try
            {
                string directory=Path.Combine(Application.streamingAssetsPath,"RemielleControllerMotions");
                var source=new NativeControllerSource(File.ReadAllText("Assets/ControllerRuntime/Data/source-controller-pack.json"));
                var settings=new NativeControllerTimeSettings(File.ReadAllText("Assets/ControllerRuntime/Data/source-time-settings.json"));
                var avatars=new NativeControllerAvatarBindings(File.ReadAllText(Path.Combine(directory,"controller-avatar-bindings.json")),File.ReadAllText(Path.Combine(directory,"binding-profiles.json")));
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                foreach(string scenario in new[]{"early-release","late-release","walk-loop-stop","moving-evade","stationary-evade","attack-return","pause","cross-list-priority","blend-release","blend-evade","repeated-interruption",
                    "dash-early-stop","dash-loop-stop","dash-evade-loop","dash-self-evade","dash-attack-return"})
                {
                    var actor=new GameObject("SourceActionActor");actor.AddComponent<CapsuleCollider>();actor.AddComponent<CapsuleColliderCast>();
                    var engine=actor.AddComponent<KCCMovementEngine>();actor.GetComponent<Rigidbody>().isKinematic=true;engine.Awake();
                    var motor=actor.AddComponent<RemielleAuthoredMotor>();
                    var visual=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/V3/Remielle_V3_Animated.prefab"));visual.transform.SetParent(actor.transform,false);
                    var driver=visual.GetComponentInChildren<RemielleNativeAnimation>();
                    var events=new SourceFrameEvents(File.ReadAllText("Assets/ControllerIntegration/Data/source-action-events.json"),source,Main);
                    var emitted=new System.Collections.Generic.List<SourceFrameEvents.Emission>();
                    var released=new System.Collections.Generic.List<long>();
                    events.Emitted+=emitted.Add;events.Released+=(id,reason)=>released.Add(id);
                    using(var session=new SourceActionSession(source,Main,directory,settings,avatars,driver,visual.transform,motor))
                    {
                        session.ObserveActions(events.Observe);
                        var p=session.Parameters;
                        void Move(bool value)=>p.SetBool(p.Hash("Bool_IsMoving"),value);
                        void Press(string name)=>p.SetTrigger(p.Hash(name));
                        void Hold(bool value)=>p.SetBool(p.Hash("Bool_HoldEvade"),value);
                        void Step(int count=1){for(int i=0;i<count;i++)session.Tick(1f/60f);}
                        void ExpectTarget(int state){if((session.NextState??session.CurrentState)!=state)throw new Exception(scenario+" target differs: "+session.CurrentState+" -> "+session.NextState);}
                        void Until(int state,int limit=650)
                        {int i=0;while((session.CurrentState!=state||session.IsBlending)&&i++<limit)Step();if(i>=limit)throw new Exception(scenario+" never reached "+state);}
                        int warmed=0;
                        if(scenario=="dash-loop-stop")
                        {
                            var positions=driver.bones.Select(x=>x.target.position).ToArray();
                            SourcePreviewMotions.Prewarm(session);warmed=session.PreparedSamplerCount;
                            float error=driver.bones.Select((b,i)=>Vector3.Distance(positions[i],b.target.position)).Max();
                            if(error>.00005f||actor.transform.position!=Vector3.zero)throw new Exception("Prewarm altered live pose or moved actor");
                        }
                        if(scenario.StartsWith("dash-"))
                        {
                            Move(true);Press("Trigger_PressEvade");Step();ExpectTarget(8);Hold(true);Until(25);
                            if(scenario=="dash-early-stop")
                            {Hold(false);Move(false);Step();ExpectTarget(27);Until(27);Until(32);}
                            else
                            {
                                Until(26);Hold(false);Step(20);ExpectTarget(26);
                                if(scenario=="dash-loop-stop")
                                {Move(false);Step();ExpectTarget(27);Until(27);Until(32);}
                                else if(scenario=="dash-attack-return")
                                {Press("Trigger_PressAttackA");Move(false);Step();ExpectTarget(10);Until(10);Until(17);Until(32);}
                                else
                                {
                                    Press("Trigger_PressEvade");Step();ExpectTarget(29);Until(29);
                                    if(scenario=="dash-self-evade")
                                    {
                                        while(session.ActionFrames<23)Step();Press("Trigger_PressEvade");Step();ExpectTarget(29);
                                        if(session.Trace.Count(t=>(string)t["event"]=="transition-start"&&(int)t["targetState"]==29)!=2)throw new Exception("Repeated dash evade was not accepted");
                                        Until(29);
                                    }
                                    Until(30);Move(false);Step();ExpectTarget(27);Until(27);Until(32);
                                }
                            }
                            if(warmed!=0&&session.PreparedSamplerCount!=warmed)throw new Exception("Warm trajectory allocated a new sampler");
                        }
                        else if(scenario=="pause")
                        {Press("Trigger_PressAttackA");session.Tick(0);if(session.CurrentState!=32||!p.GetTrigger(p.Hash("Trigger_PressAttackA")))throw new Exception("Pause consumed input");}
                        else if(scenario=="cross-list-priority")
                        {Press("Trigger_Die");Press("Trigger_PressEvade");Step();ExpectTarget(1);if(!p.GetTrigger(p.Hash("Trigger_PressEvade")))throw new Exception("Unselected request consumed");}
                        else if(scenario.StartsWith("blend-")||scenario=="repeated-interruption")
                        {
                            Move(true);Step();ExpectTarget(12);
                            if(!session.IsBlending)throw new Exception("No blend to interrupt");
                            if(scenario=="blend-evade")
                            {Press("Trigger_PressEvade");Step();ExpectTarget(8);}
                            else
                            {
                                Move(false);Step();ExpectTarget(14);
                                if(scenario=="repeated-interruption")
                                {Press("Trigger_PressEvade");Step();ExpectTarget(9);Until(9);Until(32);}
                                else {Until(14);Until(32);}
                            }
                            int required=scenario=="repeated-interruption"?2:1;
                            if(session.Trace.Count(t=>(bool?)t["interrupted"]==true)<required)throw new Exception("Input waited for blend completion");
                        }
                        else if(scenario=="stationary-evade")
                        {Press("Trigger_PressEvade");Step();ExpectTarget(9);Until(9);Until(32);}
                        else if(scenario=="attack-return")
                        {Press("Trigger_PressAttackA");Step();ExpectTarget(46);Until(46);Until(40);Until(32);}
                        else
                        {
                            Move(true);Step();ExpectTarget(12);Until(12);
                            if(scenario=="early-release"){Move(false);Step();ExpectTarget(14);Until(14);Until(32);}
                            else if(scenario=="late-release"){Step(20);Move(false);Step();ExpectTarget(11);Until(11);Until(32);}
                            else if(scenario=="walk-loop-stop"){Until(13);Step(80);Move(false);Step();ExpectTarget(11);Until(11);Until(32);}
                            else if(scenario=="moving-evade"){Press("Trigger_PressEvade");Step();ExpectTarget(8);Until(8);Until(31);}
                        }
                        cases.Add(new JObject{["name"]=scenario,["currentState"]=session.CurrentState,["nextState"]=session.NextState.HasValue?(JToken)session.NextState.Value:JValue.CreateNull(),
                            ["trace"]=new JArray(session.Trace),["actorZ"]=actor.transform.position.z,["prewarmedSamplers"]=warmed});
                    }
                    if(events.ActiveScopes!=0||released.Count!=released.Distinct().Count())throw new Exception("Action event scopes leaked or released twice: "+scenario);
                    if(emitted.GroupBy(e=>(e.Generation,e.Index,e.Cycle)).Any(g=>g.Count()!=1))throw new Exception("Action event duplicated: "+scenario);
                    if(scenario=="attack-return"&&!emitted.Where(e=>e.State==46).Select(e=>e.Index).SequenceEqual(new[]{2,0,1}))throw new Exception("Actual attack clock missed original frames");
                    var result=(JObject)cases.Last;result["actionEvents"]=emitted.Count;result["releasedActions"]=released.Count;result["scopesAfterDispose"]=events.ActiveScopes;
                    UnityEngine.Object.DestroyImmediate(visual);UnityEngine.Object.DestroyImmediate(actor);
                }
                report["pass"]=true;report["scope"]="Original source conditions/selectors/clocks drive actual character playback; single-layer parameter commands, not keyboard or full interruption parity";
                Debug.Log("REMIELLE_SOURCE_ACTION_SESSION_PASS "+cases.Count);
            }
            catch(Exception ex){report["error"]=ex.ToString();Debug.LogException(ex);}
            finally{File.WriteAllText(QualifiedIntegrationAudit.Output+"/source-action-session-verification.json",report.ToString());}
            if(!(bool)report["pass"])EditorApplication.Exit(1);
        }
    }
}
