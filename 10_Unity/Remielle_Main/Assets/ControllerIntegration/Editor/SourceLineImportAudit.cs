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
 public static class SourceLineImportAudit
 {
  const string Output="E:/ZZZ/ZCode/90_Builds/ControllerDependencies/implementation/skill-trail-sources/";
  public static void Run()
  {
   var report=new JObject{["pass"]=false,["rendered"]=false};var cases=new JArray();report["cases"]=cases;
   try
   {
    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
    foreach(var row in JObject.Parse(File.ReadAllText(Output+"line-renderer-data.json"))["rows"])
    {
     var source=(JObject)row["values"].DeepClone();source.Remove("m_GameObject");
     // Preserve qualified material pointers in the sidecar; defer binding until
     // prefab assembly. Other non-null references still fail Clean explicitly.
     foreach(JObject ptr in source["m_Materials"]){ptr["m_FileID"]=0;ptr["m_PathID"]=0;}
     var clean=(JObject)SourceParticleImportAudit.Clean(source);
     var go=new GameObject("Source line import audit");var line=go.AddComponent<LineRenderer>();
     UpgradeVersions(clean,JObject.Parse(EditorJsonUtility.ToJson(line))["LineRenderer"]);
     clean["m_ReceiveShadows"]=(int)clean["m_ReceiveShadows"]!=0;
     clean["m_DynamicOccludee"]=(int)clean["m_DynamicOccludee"]!=0;
     // Editor JSON omits runtime lightmap fields. Migrate through public APIs.
     line.lightmapIndex=(int)source["m_LightmapIndex"]==65535?-1:(int)source["m_LightmapIndex"];
     line.realtimeLightmapIndex=(int)source["m_LightmapIndexDynamic"]==65535?-1:(int)source["m_LightmapIndexDynamic"];
     line.lightmapScaleOffset=Vector(source["m_LightmapTilingOffset"]);line.realtimeLightmapScaleOffset=Vector(source["m_LightmapTilingOffsetDynamic"]);
     foreach(string key in new[]{"m_LightmapIndex","m_LightmapIndexDynamic","m_LightmapTilingOffset","m_LightmapTilingOffsetDynamic"})clean.Remove(key);
     EditorJsonUtility.FromJsonOverwrite(new JObject{["LineRenderer"]=clean}.ToString(),line);
     var actual=(JObject)JObject.Parse(EditorJsonUtility.ToJson(line))["LineRenderer"];
     var differences=new JArray();SourceParticleImportAudit.Compare(clean,actual,"",differences);
     if(line.positionCount!=source["m_Positions"].Count())throw new Exception("Line position count changed");
     if(line.useWorldSpace!=(bool)source["m_UseWorldSpace"]||line.loop!=(bool)source["m_Loop"])throw new Exception("Line coordinate semantics changed");
     cases.Add(new JObject{["identity"]=row["identity"].DeepClone(),["differences"]=differences,["positionCount"]=line.positionCount});
     File.WriteAllText(Output+"line-import-"+(string)row["identity"]["pathID"]+".json",actual.ToString());
     Object.DestroyImmediate(go);
    }
    report["pass"]=cases.Count==180&&cases.All(c=>!c["differences"].Any());
   }
   catch(Exception e){report["error"]=e.ToString();Debug.LogException(e);}
   File.WriteAllText(Output+"line-import-verification.json",report.ToString());
   if(!(bool)report["pass"])throw new Exception("Line import requires review");
  }
  static Vector4 Vector(JToken x)=>new Vector4((float)x["x"],(float)x["y"],(float)x["z"],(float)x["w"]);
  static void UpgradeVersions(JToken source,JToken current)
  {
   if(source is JObject obj&&current is JObject template)
   {
    if(template["serializedVersion"]!=null)obj["serializedVersion"]=template["serializedVersion"].DeepClone();
    foreach(var property in obj.Properties().ToArray())UpgradeVersions(property.Value,template[property.Name]);
   }
   else if(source is JArray array&&current is JArray samples&&samples.Count>0)
    foreach(var item in array)UpgradeVersions(item,samples[0]);
  }
 }
}
