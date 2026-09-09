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
 public static class SourceSpecialFlashBuild
 {
  const string Output="E:/ZZZ/local-only/RemielleControllerDependencies/implementation/skill-fx-sources/";
  public static void Run()
  {
   var report=new JObject{["pass"]=false};var cases=new JArray();report["cases"]=cases;
   try
   {
    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
    AssetDatabase.Refresh();
    var source=new NativeControllerSource(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/ControllerRuntime/Data/source-controller-pack.json").text);
    var model=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/V3/Remielle_V3_Animated.prefab"));
    var driver=model.GetComponentInChildren<RemielleNativeAnimation>();
    var prefabs=new[]{"05","06"}.Select(n=>AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ControllerIntegration/Effects/SpecialFlash/Eff_RemielleOrigin_Attack_Special_"+n+"_Flash.prefab")).ToArray();
    foreach(int hz in new[]{30,60,120})
    {
     var events=new SourceFrameEvents(File.ReadAllText("Assets/ControllerIntegration/Data/source-special-effect-events.json"),source,"Avatar_Female_Size02_RemielleOrigin_Controller");
     using var fx=new SourceSkillParticles(File.ReadAllText("Assets/ControllerIntegration/Data/source-special-flash-runtime.json"),prefabs,events,driver);
     void Notice(SourceActionPhase phase,long id,double frame)=>events.Observe(new SourceActionNotice(phase,id,54,frame,62,false,"audit"));
     Notice(SourceActionPhase.Enter,1,0);Notice(SourceActionPhase.Sample,1,7);fx.Advance(1f/hz);
     if(fx.Activations!=0)throw new Exception("Flash fired before source frame 8");
     Notice(SourceActionPhase.Sample,1,8);fx.Advance(1f/hz);
     if(fx.Activations!=2||fx.ActiveCount!=2||fx.Prewarmed!=6)throw new Exception("Paired flash entry failed");
     Notice(SourceActionPhase.Sample,1,8);fx.Advance(1f/hz);
     if(fx.Activations!=2||fx.ParticleCount==0)throw new Exception("Duplicate event or missing burst");
     int count=fx.ParticleCount;double clock=fx.ActorSeconds;fx.Advance(0);
     if(fx.ParticleCount!=count||fx.ActorSeconds!=clock)throw new Exception("Actor pause advanced skill particles");
     var roots=fx.ActiveRoots.ToArray();var old=roots.Select(r=>r.transform.position).ToArray();
     model.transform.position+=Vector3.right;fx.Advance(0);
     for(int n=0;n<roots.Length;n++)if(Vector3.Distance(roots[n].transform.position,old[n]+Vector3.right)>1e-4f)throw new Exception("Attachment did not follow actor");
     for(int n=0;n<hz;n++)fx.Advance(1f/hz);
     if(fx.ActiveCount!=0||fx.Completed!=2||roots.Any(r=>r.GetComponentsInChildren<ParticleSystem>(true).Any(p=>p.particleCount>0)))throw new Exception("Cut times left residual particles");
     Notice(SourceActionPhase.Exit,1,62);
     Notice(SourceActionPhase.Enter,2,0);Notice(SourceActionPhase.Sample,2,8);fx.Advance(0);fx.Advance(1f/hz);
     if(fx.Created!=fx.Prewarmed||fx.ParticleCount==0)throw new Exception("Pooled reentry did not restart particles");
     Notice(SourceActionPhase.Leave,2,9);Notice(SourceActionPhase.Exit,2,9);
     if(fx.ActiveCount!=0||fx.Cancelled!=2)throw new Exception("Cancelled action left effects");
     Notice(SourceActionPhase.Enter,3,0);Notice(SourceActionPhase.Leave,3,7);Notice(SourceActionPhase.Exit,3,7);
     if(fx.Activations!=4)throw new Exception("Early cancellation synthesized future flashes");
     Notice(SourceActionPhase.Enter,4,0);Notice(SourceActionPhase.Sample,4,8);fx.Clear();
     if(fx.ActiveCount!=0||fx.ParticleCount!=0)throw new Exception("Disable clear failed");
     Notice(SourceActionPhase.Exit,4,8);
     cases.Add(new JObject{["hz"]=hz,["activations"]=fx.Activations,["completed"]=fx.Completed,["cancelled"]=fx.Cancelled,["created"]=fx.Created,["attachmentError"]=fx.MaximumAttachmentError});
    }
    Object.DestroyImmediate(model);
    if(Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length!=0)throw new Exception("Disposed pools retained particles");
    report["pass"]=true;
   }
   catch(Exception e){report["error"]=e.ToString();Debug.LogException(e);}
   File.WriteAllText(Output+"lifecycle-verification.json",report.ToString());
   if(!(bool)report["pass"])throw new Exception("Special flash lifecycle failed");
   SourceControllerBuild.BuildPlayer();
  }
 }
}
