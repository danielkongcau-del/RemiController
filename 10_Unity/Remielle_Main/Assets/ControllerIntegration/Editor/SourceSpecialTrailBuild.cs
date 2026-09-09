using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Remielle.ControllerRuntime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;
namespace Remielle.Controller.Editor
{
 public static class SourceSpecialTrailBuild
 {
  const string Output="E:/ZZZ/local-only/RemielleControllerDependencies/implementation/skill-trail-sources/";
  public static void Run()
  {
   var report=new JObject{["pass"]=false};var cases=new JArray();report["cases"]=cases;
   try
   {
    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);AssetDatabase.Refresh();
    var source=new NativeControllerSource(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/ControllerRuntime/Data/source-controller-pack.json").text);
    var model=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/V3/Remielle_V3_Animated.prefab"));
    var driver=model.GetComponentInChildren<RemielleNativeAnimation>();
    string pack=File.ReadAllText(Output+"runtime-candidate.json");
    var prefabs=JObject.Parse(pack)["effects"].Select(e=>AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ControllerIntegration/Effects/"+((string)e["kind"]=="trail"?"SpecialTrail/":(string)e["kind"]=="smoke"?"SpecialSmoke/":(string)e["kind"]=="burst"?"SpecialBurst/":"SpecialFlash/")+(string)e["prefabName"]+".prefab")).ToArray();
    foreach(int hz in new[]{30,60,120})
    {
     var events=new SourceFrameEvents(File.ReadAllText("Assets/ControllerIntegration/Data/source-special-effect-events.json"),source,"Avatar_Female_Size02_RemielleOrigin_Controller");
     using var fx=new SourceSkillParticles(pack,prefabs,events,driver,model.transform);
     void Notice(SourceActionPhase phase,long id,double frame)=>events.Observe(new SourceActionNotice(phase,id,54,frame,62,false,"audit"));
     Notice(SourceActionPhase.Enter,1,0);Notice(SourceActionPhase.Sample,1,15);fx.Advance(1f/hz);
     if(fx.BurstActivations!=0)throw new Exception("Burst before frame 18");
     if(fx.ActiveRoots.Any(r=>r.name.Contains("Smoke")))throw new Exception("Smoke before frame 16");
     Notice(SourceActionPhase.Sample,1,16);fx.Advance(0);
     if(fx.ActiveRoots.Count(r=>r.name.Contains("Smoke"))!=2)throw new Exception("Paired smoke missing at frame 16");
     var smokeLight=fx.ActiveRoots.SelectMany(r=>r.GetComponentsInChildren<ParticleSystem>(true)).Single(p=>p.lights.enabled);
     fx.SetKindRenderingEnabled("smoke",false);
     if(smokeLight.lights.enabled)throw new Exception("Hidden smoke retained particle lights");
     fx.SetKindRenderingEnabled("smoke",true);
     if(!smokeLight.lights.enabled)throw new Exception("Smoke light visibility was not restored");
     fx.SetRenderingEnabled(false);if(smokeLight.lights.enabled)throw new Exception("Global hide retained particle lights");
     fx.SetRenderingEnabled(true);if(!smokeLight.lights.enabled)throw new Exception("Global visibility did not restore lights");
     Notice(SourceActionPhase.Sample,1,18);fx.Advance(1f/hz);
     if(fx.BurstActivations!=2||fx.Prewarmed!=40)throw new Exception("Paired bursts failed");
     Notice(SourceActionPhase.Sample,1,18);fx.Advance(.1f);
     if(fx.BurstActivations!=2||fx.ParticleCount==0)throw new Exception("Duplicate burst or missing particles");
     if(fx.ActiveRoots.Any(r=>r.name.Contains("Trail")))throw new Exception("Trail emitted before frame 19");
     Notice(SourceActionPhase.Sample,1,19);fx.Advance(0);
     if(fx.ActiveRoots.Count(r=>r.name.Contains("Trail"))!=2)throw new Exception("Paired trails missing");
     var lines=fx.ActiveRoots.SelectMany(r=>r.GetComponentsInChildren<LineRenderer>(true)).ToArray();
     if(lines.Count(l=>l.enabled&&l.gameObject.activeInHierarchy)!=12)throw new Exception("Source line visibility differs at birth");
     fx.SetKindRenderingEnabled("trail",false);if(lines.Any(l=>l.enabled))throw new Exception("Hidden trails retained lines");
     fx.SetKindRenderingEnabled("trail",true);if(lines.Count(l=>l.enabled)!=12)throw new Exception("Trail line restore differs");
     Notice(SourceActionPhase.Leave,1,19);Notice(SourceActionPhase.Exit,1,19);
     if(fx.ActiveCount!=6||fx.BurstCancelled!=0)throw new Exception("First-frame-follow burst was cancelled with actor action");
     var roots=fx.ActiveRoots.ToArray();var positions=roots.Select(r=>r.transform.position).ToArray();
     int particles=fx.ParticleCount;double clock=fx.ActorSeconds;
     model.transform.position+=Vector3.right;fx.Advance(0);
     if(fx.ParticleCount!=particles||fx.ActorSeconds!=clock||roots.Where((r,i)=>r.transform.position!=positions[i]).Any())throw new Exception("Detached position or paused particles changed");
     for(int i=0;i<hz*3;i++)fx.Advance(1f/hz);
     if(fx.ActiveCount!=0||fx.BurstCompleted!=2||roots.Any(r=>r.GetComponentsInChildren<ParticleSystem>(true).Any(p=>p.particleCount>0)))throw new Exception("Burst expiry retained particles");
     // Authored held-E self entry can repeat at 36 frames. Verify overlapping
     // generations through four simultaneous bursts of each type.
     for(int cast=0;cast<6;cast++)
     {
      long id=cast+2;Notice(SourceActionPhase.Enter,id,0);Notice(SourceActionPhase.Sample,id,19);fx.Advance(0);
      Notice(SourceActionPhase.Leave,id,19);Notice(SourceActionPhase.Exit,id,19);
      for(int i=0;i<(int)Math.Round(hz*.6);i++)fx.Advance(1f/hz);
     }
     if(fx.Created!=fx.Prewarmed||fx.BurstActivations!=14||fx.Activations!=56)throw new Exception("Held special exhausted prewarm or lost a generation");
     fx.Clear();if(fx.ActiveCount!=0||fx.ParticleCount!=0)throw new Exception("Disable did not clear bursts");
     cases.Add(new JObject{["hz"]=hz,["burstActivations"]=fx.BurstActivations,["burstCompleted"]=fx.BurstCompleted,["burstCancelled"]=fx.BurstCancelled,["created"]=fx.Created});
    }
    Object.DestroyImmediate(model);if(Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length!=0)throw new Exception("Disposed burst pools retained objects");
    report["pass"]=true;
   }
   catch(Exception e){report["error"]=e.ToString();Debug.LogException(e);}
   File.WriteAllText(Output+"lifecycle-verification.json",report.ToString());
   if(!(bool)report["pass"])throw new Exception("Trail lifecycle failed");
   // Candidate verification only; publish and rebuild Player after lighting QA.
  }
 }
}
