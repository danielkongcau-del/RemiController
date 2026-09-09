using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;
namespace Remielle.Controller.Editor
{
 public static class SourceTrailLineAudit
 {
  const string Output="E:/ZZZ/ZCode/90_Builds/ControllerDependencies/implementation/skill-trail-sources/";
  public static void Run()
  {
   var report=new JObject{["pass"]=false};var cases=new JArray();report["cases"]=cases;
   try
   {
    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
    foreach(var effect in JObject.Parse(File.ReadAllText("Assets/ControllerIntegration/Data/source-special-trail-visuals.json"))["effects"])
    {
     var nodes=effect["nodes"].ToArray();var map=nodes.ToDictionary(n=>(long)n["transformID"]);
     bool Active(JToken n)=>(bool)n["activeSelf"]&&((long)n["parentID"]==0||Active(map[(long)n["parentID"]]));
     int[] Indices(JToken n)=>(long)n["parentID"]==0?Array.Empty<int>():Indices(map[(long)n["parentID"]]).Append(Array.IndexOf(nodes.Where(x=>(long)x["parentID"]==(long)n["parentID"]).ToArray(),n)).ToArray();
     var rows=new JArray(nodes.Where(n=>n["line"] is JObject).Select(n=>new JObject{
      ["siblingPath"]=new JArray(Indices(n)),["emits"]=Active(n)&&(bool)n["lineSource"]["values"]["m_Enabled"]&&(int)n["simulator"]["hideNode"]==0,
      ["startTime"]=n["simulator"]["startDelay"],["cutTime"]=(float)n["simulator"]["startDelay"]+(float)n["simulator"]["duration"]}));
     var root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ControllerIntegration/Effects/SpecialTrail/"+(string)effect["name"]+".prefab"));
     var clock=new SourceEffectLines(root.transform,rows);var lines=root.GetComponentsInChildren<LineRenderer>(true);
     var samples=new JArray();
     foreach(float t in new[]{0f,.1f,.2f,.25f,1f})
     {
      clock.Advance(t);int expected=rows.Count(n=>(bool)n["emits"]&&t>=(float)n["startTime"]&&t<(float)n["cutTime"]);
      int actual=lines.Count(l=>l.enabled&&l.gameObject.activeInHierarchy);if(actual!=expected)throw new Exception("Line time window differs");
      clock.SetVisible(false);if(lines.Any(l=>l.enabled))throw new Exception("Hidden line retained rendering");
      clock.SetVisible(true);if(lines.Count(l=>l.enabled)!=expected)throw new Exception("Line visibility restore differs");
      samples.Add(new JObject{["time"]=t,["visibleLines"]=actual});
     }
     clock.Clear();if(lines.Any(l=>l.enabled))throw new Exception("Line clear failed");
     File.WriteAllText(Output+(string)effect["name"]+"-line-runtime.json",rows.ToString());
     cases.Add(new JObject{["effect"]=effect["name"],["samples"]=samples});Object.DestroyImmediate(root);
    }
    report["pass"]=true;
   }
   catch(Exception e){report["error"]=e.ToString();Debug.LogException(e);}
   File.WriteAllText(Output+"line-lifecycle-verification.json",report.ToString());
   if(!(bool)report["pass"])throw new Exception("Trail line lifecycle failed");
  }
 }
}
