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
using Object=UnityEngine.Object;
namespace Remielle.Controller.Editor
{
 public static class PresentationIssueAudit
 {
  const string Out="E:/ZZZ/local-only/RemielleControllerDependencies/implementation/presentation-issues/";
  static GameObject snapshotRoot;
  static JArray Vec(Vector3 p)=>new JArray(p.x,p.y,p.z);
  public static void Run()
  {
   Directory.CreateDirectory(Out);var report=new JObject{["pass"]=false};var rows=new JArray();report["poses"]=rows;
   try
   {
    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
    var source=new NativeControllerSource(File.ReadAllText("Assets/ControllerRuntime/Data/source-controller-pack.json"));
    var settings=new NativeControllerTimeSettings(File.ReadAllText("Assets/ControllerRuntime/Data/source-time-settings.json"));
    string dir=Path.Combine(Application.streamingAssetsPath,"RemielleControllerMotions");
    var avatars=new NativeControllerAvatarBindings(File.ReadAllText(Path.Combine(dir,"controller-avatar-bindings.json")),File.ReadAllText(Path.Combine(dir,"binding-profiles.json")));
    var light=new GameObject("IssueAuditLight").AddComponent<Light>();light.type=LightType.Directional;light.transform.rotation=Quaternion.Euler(30,-20,0);RenderSettings.ambientLight=Color.gray;
    var camera=new GameObject("IssueAuditCamera").AddComponent<Camera>();camera.enabled=false;camera.fieldOfView=40;camera.nearClipPlane=.01f;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.05f,.05f,.06f);
    foreach(int state in new[]{32,54})
    {
     var actor=new GameObject("IssueAuditActor");actor.AddComponent<CapsuleCollider>();actor.AddComponent<CapsuleColliderCast>();
     var engine=actor.AddComponent<KCCMovementEngine>();actor.GetComponent<Rigidbody>().isKinematic=true;engine.Awake();var motor=actor.AddComponent<RemielleAuthoredMotor>();
     var visual=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/V3/Remielle_V3_Animated.prefab"));visual.transform.SetParent(actor.transform,false);
     snapshotRoot=visual;var driver=visual.GetComponentInChildren<RemielleNativeAnimation>();driver.enabled=false;driver.nativeAnimation.enabled=false;
     using(var session=new SourceActionSession(source,"Avatar_Female_Size02_RemielleOrigin_Controller",dir,settings,avatars,driver,visual.transform,motor,state))
     {
      for(int i=0;i<(state==54?27:1);i++)session.Tick(1f/60f);
      var all=visual.GetComponentsInChildren<SkinnedMeshRenderer>(true);var enabled=all.Select(r=>r.enabled).ToArray();
      var brow=all.Single(r=>r.name=="SMR_Remielle_Eyebrow"&&r.enabled);var face=all.Single(r=>r.name=="SMR_Remielle_Face"&&r.enabled);
      string prefix=state==54?"special":"idle";var row=new JObject{["state"]=state,["frame"]=session.ActionFrames,["browEnabled"]=brow.enabled,["browVertices"]=brow.sharedMesh.vertexCount};rows.Add(row);
      camera.transform.position=face.bounds.center+Vector3.forward*.85f;camera.transform.LookAt(face.bounds.center);
      var a=Capture(camera,prefix+"-face.png");brow.enabled=false;var b=Capture(camera,prefix+"-no-brow.png");brow.enabled=true;
      row["browVisiblePixelDifference"]=a.Zip(b,(x,y)=>x.Equals(y)?0:1).Sum();
      foreach(var r in all.Where(r=>r.name.Contains("Hair")))r.enabled=false;
      Capture(camera,prefix+"-no-hair.png");
      foreach(var r in all)r.enabled=r==brow;Capture(camera,prefix+"-brow-only.png");
      var original=brow.sharedMaterials;var debug=new Material(Shader.Find("Unlit/Color"));debug.color=Color.magenta;brow.sharedMaterials=Enumerable.Repeat(debug,original.Length).ToArray();
      Capture(camera,prefix+"-brow-geometry.png");brow.sharedMaterials=original;Object.DestroyImmediate(debug);
      for(int i=0;i<all.Length;i++)all[i].enabled=enabled[i];
      var variants=original.Select(m=>new Material(m)).ToArray();foreach(var m in variants){m.SetFloat("_StencilCompA",8);m.SetFloat("_StencilCompB",8);m.SetFloat("_ZTesting",8);}
      brow.sharedMaterials=variants;Capture(camera,prefix+"-brow-no-tests.png");brow.sharedMaterials=original;foreach(var m in variants)Object.DestroyImmediate(m);
      camera.transform.position=actor.transform.position+new Vector3(0,1.4f,6);camera.transform.LookAt(actor.transform.position+Vector3.up);
      Capture(camera,prefix+"-model.png");foreach(var r in all.Where(r=>r.name.Contains("Cannon")))r.enabled=false;Capture(camera,prefix+"-no-cannons.png");
      for(int i=0;i<all.Length;i++)all[i].enabled=enabled[i];
      var floater=all.Single(r=>r.name=="SMR_Remielle_Floater_02");floater.enabled=false;Capture(camera,prefix+"-no-floater.png");floater.enabled=true;
      row["renderers"]=new JArray(all.Where(r=>r.name.Contains("Cannon")||r==floater||r==brow||r==face).Select(r=>new JObject{
       ["name"]=r.name,["enabled"]=r.enabled,["mesh"]=AssetDatabase.GetAssetPath(r.sharedMesh),["boundsCenter"]=Vec(r.bounds.center),["boundsSize"]=Vec(r.bounds.size),
       ["materials"]=new JArray(r.sharedMaterials.Select(m=>AssetDatabase.GetAssetPath(m))),
       ["bones"]=new JArray(r.bones.Select(t=>new JObject{["name"]=t.name,["position"]=Vec(t.position)}))}));
      row["sourceBones"]=new JArray(driver.bones.Where(bn=>bn.source.name.Contains("Can")||bn.source.name.Contains("Flo")).Select(bn=>new JObject{
       ["name"]=bn.source.name,["sourcePosition"]=Vec(bn.source.position),["targetPosition"]=Vec(bn.target.position),
       ["sourceScale"]=Vec(bn.source.localScale),["targetScale"]=Vec(bn.target.localScale)}));
     }
     Object.DestroyImmediate(actor);
    }
    report["pass"]=true;
   }
   catch(Exception e){report["error"]=e.ToString();Debug.LogException(e);}
   File.WriteAllText(Out+"audit.json",report.ToString());if(!(bool)report["pass"])throw new Exception("Presentation issue audit failed");
  }
  static Color32[] Capture(Camera camera,string name)
  {
   using var snapshot=new RemiellePoseSnapshot(snapshotRoot);
   var old=camera.targetTexture;var active=RenderTexture.active;var rt=RenderTexture.GetTemporary(1280,720,24);var tex=new Texture2D(1280,720,TextureFormat.RGB24,false);
   try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1280,720),0,0);tex.Apply();File.WriteAllBytes(Out+name,tex.EncodeToPNG());return tex.GetPixels32();}
   finally{camera.targetTexture=old;RenderTexture.active=active;RenderTexture.ReleaseTemporary(rt);Object.DestroyImmediate(tex);}
  }
 }
}
