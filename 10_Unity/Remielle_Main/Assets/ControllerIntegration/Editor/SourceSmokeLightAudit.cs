using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;
namespace Remielle.Controller.Editor
{
 public static class SourceSmokeLightAudit
 {
  const string Output="E:/ZZZ/ZCode/90_Builds/ControllerDependencies/implementation/skill-smoke-sources/";
  static JObject report;
  static System.Collections.IEnumerator steps;
  public static void Run()
  {
   report=new JObject{["pass"]=false,["crossFrame"]=true};steps=Execute();EditorApplication.update+=Tick;
  }
  static void Tick()
  {
   try{if(steps.MoveNext())return;}
   catch(Exception e){report["error"]=e.ToString();Debug.LogException(e);}
   EditorApplication.update-=Tick;
   File.WriteAllText(Output+"light-gpu-verification.json",report.ToString());
   EditorApplication.Exit((bool)report["pass"]?0:1);
  }
  static System.Collections.IEnumerator Execute()
  {
    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
    RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=Color.black;RenderSettings.ambientIntensity=0;
    var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ControllerIntegration/Effects/SpecialSmoke/Eff_RemielleOrigin_Attack_Special_15_Smoke.prefab");
    var root=Object.Instantiate(prefab);
    var ps=root.GetComponentsInChildren<ParticleSystem>(true).Single(p=>p.lights.enabled);
    foreach(var other in root.GetComponentsInChildren<ParticleSystem>(true))other.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
    foreach(var r in root.GetComponentsInChildren<Renderer>(true))r.enabled=r==ps.GetComponent<ParticleSystemRenderer>();
    if(!ps.lights.light||ps.lights.light.enabled)throw new Exception("Source disabled light template not retained");
    ps.useAutoRandomSeed=false;ps.randomSeed=14591;ps.Simulate(.1f,false,true,false);
    var particles=new ParticleSystem.Particle[ps.particleCount];ps.GetParticles(particles);
    if(particles.Length==0)throw new Exception("Light emitter has no particles");
    var center=ps.main.simulationSpace==ParticleSystemSimulationSpace.World?particles[0].position:ps.transform.TransformPoint(particles[0].position);
    var receiver=GameObject.CreatePrimitive(PrimitiveType.Cube);receiver.transform.position=center+Vector3.forward*2;
    receiver.transform.localScale=new Vector3(10,10,.1f);
    var mat=new Material(Shader.Find("Standard"));mat.color=Color.white;mat.SetFloat("_Glossiness",0);receiver.GetComponent<Renderer>().sharedMaterial=mat;
    var camera=new GameObject("Particle light audit").AddComponent<Camera>();camera.enabled=false;camera.transform.position=center-Vector3.forward*6;
    camera.transform.LookAt(center);camera.orthographic=true;camera.orthographicSize=5;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.renderingPath=RenderingPath.Forward;
    for(int frame=0;frame<4;frame++)yield return null;
    var on=Capture(camera);var module=ps.lights;module.enabled=false;
    for(int frame=0;frame<4;frame++)yield return null;
    var off=Capture(camera);
    int changed=on.GetPixels32().Zip(off.GetPixels32(),(a,b)=>Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b)>6).Count(x=>x);
    File.WriteAllBytes(Output+"light-on.png",on.EncodeToPNG());File.WriteAllBytes(Output+"light-off.png",off.EncodeToPNG());
    report["changedPixels"]=changed;report["particles"]=particles.Length;report["templateEnabled"]=ps.lights.light.enabled;
    report["pass"]=changed>100;
    Object.DestroyImmediate(on);Object.DestroyImmediate(off);Object.DestroyImmediate(root);Object.DestroyImmediate(receiver);Object.DestroyImmediate(mat);
   }
  static Texture2D Capture(Camera camera)
  {
   var previous=RenderTexture.active;var rt=RenderTexture.GetTemporary(512,512,24,RenderTextureFormat.ARGB32);
   camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
   var image=new Texture2D(512,512,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,512,512),0,0);image.Apply();
   camera.targetTexture=null;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);return image;
  }
 }
}
