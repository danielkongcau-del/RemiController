using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Remielle.Controller.Editor
{
 public static class SourceParticleAlphaBuild
 {
  const string Policy="Assets/ControllerIntegration/Data/source-particle-alpha-policy.json";
  const string Output="E:/ZZZ/ZCode/90_Builds/ControllerDependencies/implementation/skill-native-particle-shader/";
  static string Hash(string path){using var h=SHA256.Create();return BitConverter.ToString(h.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();}
  public static void Apply(Material material,JToken source)
  {
   if(!material.HasProperty("_AdapterAlphaPower"))return;
   var policy=JObject.Parse(File.ReadAllText(Policy));
   var row=policy["materials"].SingleOrDefault(r=>(string)r["source"]==(string)source["source"] && (string)r["cab"]==(string)source["cab"] && (long)r["pathID"]==(long)source["pathID"]);
   if(row==null)return;
   if(Hash((string)source["sourceJson"])!=(string)row["materialJsonSha256"])throw new Exception("Alpha material source changed");
   material.SetFloat("_AdapterAlphaPower",(float)row["powerAlpha"]);
   bool nativeDissolve=(bool?)row["nativeDissolve"]??false;
   material.SetFloat("_AdapterNativeDissolve",nativeDissolve?1:0);
   if(nativeDissolve)
   {
    var f=source["properties"]["m_Floats"];
    if((float)f["_SoftEdgeUsingOldFunction"]!=1||(float)f["_CustomData1W"]!=0||(float)f["_DissolveChannel"]!=0||(float)f["_UseDissolveTex"]!=1)throw new Exception("Native dissolve source conditions changed");
    material.SetFloat("_SoftRange",(float)f["_SoftRange"]);
    material.SetFloat("_UsingAlphaAsDissolve",(float)f["_UsingAlphaAsDissolve"]);
   }
  }
  public static void Run()
  {
   var policy=JObject.Parse(File.ReadAllText(Policy));
   foreach(var e in policy["evidence"])if(Hash((string)e["path"])!=(string)e["sha256"]||!(bool)JObject.Parse(File.ReadAllText((string)e["path"]))["pass"])throw new Exception("Alpha GPU evidence changed");
   var pack=JObject.Parse(File.ReadAllText("Assets/ControllerIntegration/Data/source-special-trail-visuals.json"));var rows=new JArray();
   foreach(var r in policy["materials"])
   {
    var source=pack["materials"].Single(m=>(string)m["source"]==(string)r["source"]&&(string)m["cab"]==(string)r["cab"]&&(long)m["pathID"]==(long)r["pathID"]);
    string path="Assets/ControllerIntegration/Effects/SpecialTrail/"+((string)source["identityKey"]).Replace('/','_')+".mat";
    var material=AssetDatabase.LoadAssetAtPath<Material>(path);
    if(!material||!material.HasProperty("_AdapterAlphaPower")||ShaderUtil.ShaderHasError(material.shader))throw new Exception("Alpha material shader invalid: "+path);
    string backup=Output+"alpha-material-before/"+Path.GetFileName(path);Directory.CreateDirectory(Path.GetDirectoryName(backup));if(!File.Exists(backup))File.Copy(path,backup);
    Apply(material,source);EditorUtility.SetDirty(material);
    rows.Add(new JObject{["path"]=path,["power"]=material.GetFloat("_AdapterAlphaPower"),["nativeDissolve"]=material.GetFloat("_AdapterNativeDissolve"),["softRange"]=material.GetFloat("_SoftRange")});
   }
   AssetDatabase.SaveAssets();
   foreach(var r in rows)if(AssetDatabase.LoadAssetAtPath<Material>((string)r["path"]).GetFloat("_AdapterAlphaPower")!=(float)r["power"])throw new Exception("Alpha save verification failed");
   File.WriteAllText(Output+"dissolve-material-application.json",new JObject{["pass"]=true,["scope"]="Four alpha exponents and two old soft-edge coverage formulas; full visual acceptance pending",["materials"]=rows}.ToString());
   SourceControllerBuild.BuildPlayer();
  }
 }
}
