using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Remielle.Controller.Editor
{
 public static class SourceHitParticleAudit
 {
  const string Output="E:/ZZZ/local-only/RemielleControllerDependencies/implementation/";
  public static void Run()
  {
   var report=new JObject{["pass"]=false};var cases=new JArray();report["cases"]=cases;
   try
   {
    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
    var pack=SourceCameraBuild.Prepare("Assets/ControllerIntegration/Data/source-hit-particles-runtime.json");
    var prefabs=new[]{"Hit_Slash_Large_Remielle","Hit_Smash_Large_Remielle"}.Select(n=>AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ControllerIntegration/Effects/HitParticles/"+n+".prefab")).ToArray();
    foreach(int hz in new[]{30,60,120})foreach(string key in new[]{"Hit_Slash_Large","Hit_Smash_Large"})
    {
     using var fx=new SourceHitParticles(pack.text,prefabs,null,.25f);
     var receipt=new SourceHitQuery.Receipt{HurtboxCenter=new Vector3(2,1,4),AttackerRotation=Quaternion.Euler(0,70,0),
      Config=JObject.Parse("{\"AttackEffect\":{\"AttackEffects\":[{\"EffectName\":\""+key+"\",\"XRotOffset\":-20,\"YRotOffset\":80,\"ZRotOffset\":0}]}}")};
     fx.Emit(receipt);var first=fx.Snapshot().Single();
     if(first.Root.transform.position!=receipt.HurtboxCenter||Math.Abs(first.Age-.02f)>1e-6||first.Particles==0||fx.Created!=fx.Prewarmed||fx.Prewarmed<2||first.Root.transform.localScale!=Vector3.one*.25f)throw new Exception("Start offset, source emission, anchor or prewarm differs");
     fx.Advance(0);if(fx.Snapshot().Single().Age!=first.Age)throw new Exception("World pause advanced particles");
     receipt.HurtboxCenter+=Vector3.right;fx.Emit(receipt);
     if(first.Root.transform.position!=new Vector3(2,1,4)||fx.ActiveCount!=2)throw new Exception("Independent hit positions differ");
     for(int n=0;n<hz;n++)fx.Advance(1f/hz);
     if(fx.Completed!=2||fx.ActiveCount!=0||first.Root.activeSelf||first.Root.GetComponentsInChildren<ParticleSystem>(true).Any(p=>p.particleCount!=0))throw new Exception("Effect expiration left particles");
     fx.Emit(receipt);if(fx.Reused==0||fx.Created!=fx.Prewarmed||fx.Snapshot().Single().Particles!=first.Particles)throw new Exception("Pool reuse has stale or missing burst");
     fx.SetRenderingEnabled(false);if(fx.Snapshot().Single().Root.GetComponentsInChildren<Renderer>().Any(r=>r.enabled))throw new Exception("GPU switch did not disable owned renderers");fx.SetRenderingEnabled(true);
     fx.Clear();if(fx.Cancelled!=1||fx.ActiveCount!=0)throw new Exception("Disable/clear did not release particles");
     // More simultaneous hits than the idle pool limit must not drop effects.
     for(int i=0;i<40;i++)fx.Emit(receipt);
     if(fx.ActiveCount!=40)throw new Exception("Concurrent hits dropped");fx.Advance(1);
     if(fx.ActiveCount!=0||fx.Completed!=42)throw new Exception("Large frame failed to expire effects");
     cases.Add(new JObject{["hz"]=hz,["effect"]=key,["activations"]=fx.Activations,["completed"]=fx.Completed,["cancelled"]=fx.Cancelled,["created"]=fx.Created,["reused"]=fx.Reused});
    }
    if(UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length!=0)throw new Exception("Disposed pool retained scene objects");
    report["pass"]=true;
   }
   catch(Exception ex){report["error"]=ex.ToString();Debug.LogException(ex);}
   File.WriteAllText(Output+"hit-particle-lifecycle-verification.json",report.ToString());
   if(!(bool)report["pass"])throw new Exception("Hit particle lifecycle audit failed");
  }
 }
}
