using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace Remielle.Controller.Editor
{
 public static class SourceHitVisualBuild
 {
  const string Output="E:/ZZZ/ZCode/90_Builds/ControllerDependencies/implementation/";
  const string AssetsRoot="Assets/ControllerIntegration/Effects/HitParticles/";
  static readonly List<ParticleSystemVertexStream> Streams=new(){ParticleSystemVertexStream.Position,ParticleSystemVertexStream.Color,
   ParticleSystemVertexStream.UV,ParticleSystemVertexStream.Custom1XYZW,ParticleSystemVertexStream.Custom2XYZW};
  const float ReviewExposure=1;
  static string Hash(string path){using var h=SHA256.Create();return BitConverter.ToString(h.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();}
  static Vector3 V(JToken a)=>a is JArray?new Vector3((float)a[0],(float)a[1],(float)a[2]):new Vector3((float)a["x"],(float)a["y"],(float)a["z"]);
  static Vector2 V2(JToken a)=>new Vector2((float)a["X"],(float)a["Y"]);
  public static void Run()
  { RunFor("Assets/ControllerIntegration/Data/source-hit-visuals.json",AssetsRoot,Output,"hit-visuals",false); }
  public static void RunSpecialFlash()
  { RunFor("Assets/ControllerIntegration/Data/source-special-flash-visuals.json","Assets/ControllerIntegration/Effects/SpecialFlash/",Output+"skill-fx-sources/","visuals",true); }
  public static void RunSpecialBurst()
  { RunFor("Assets/ControllerIntegration/Data/source-special-burst-visuals.json","Assets/ControllerIntegration/Effects/SpecialBurst/",Output+"skill-burst-sources/","visuals",true,3f); }
  public static void RunSpecialSmoke()
  { RunFor("Assets/ControllerIntegration/Data/source-special-smoke-visuals.json","Assets/ControllerIntegration/Effects/SpecialSmoke/",Output+"skill-smoke-sources/","visuals",true,15f); }
  public static void RunSpecialTrail()
  { RunFor("Assets/ControllerIntegration/Data/source-special-trail-visuals.json","Assets/ControllerIntegration/Effects/SpecialTrail/",Output+"skill-trail-sources/","visuals",true,30f); }
  public static void RunSpecialTrailSide()
  { RunFor("Assets/ControllerIntegration/Data/source-special-trail-visuals.json","Assets/ControllerIntegration/Effects/SpecialTrail/",Output+"skill-trail-sources/","side-visuals",true,30f); }
  static void RunFor(string packPath,string assetsRoot,string output,string reviewFolder,bool special,float lastSample=.8f)
  {
   var report=new JObject{["pass"]=false,["nativeShaderParity"]=false,["playerUpdated"]=false};
   var rows=new JArray();report["effects"]=rows;
   try
   {
    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
    var pack=JObject.Parse(File.ReadAllText(packPath));
    foreach(var e in pack["evidence"])if(Hash((string)e["path"])!=(string)e["sha256"])throw new Exception("Source evidence changed");
    Directory.CreateDirectory(assetsRoot);Directory.CreateDirectory(output+reviewFolder);AssetDatabase.Refresh();
    foreach(var t in pack["textures"])
    {
     string path=(string)t["path"];if(Hash(path)!=(string)t["pngSha256"])throw new Exception("Texture changed: "+path);
     var imp=(TextureImporter)AssetImporter.GetAtPath(path);imp.textureType=TextureImporterType.Default;
     imp.sRGBTexture=(bool)t["sRGB"];imp.textureCompression=TextureImporterCompression.Uncompressed;
     imp.alphaIsTransparency=false;imp.mipmapEnabled=(int)t["mipCount"]>1;imp.maxTextureSize=8192;
     imp.filterMode=(FilterMode)(int)t["filterMode"];imp.wrapMode=(TextureWrapMode)(int)t["wrapMode"];imp.anisoLevel=(int)t["aniso"];
     imp.SaveAndReimport();
    }
    var shader=Shader.Find("Remielle/Source Hit Particle Adapter");if(shader==null||ShaderUtil.ShaderHasError(shader))throw new Exception("Hit adapter shader invalid");
    var materials=new Dictionary<string,Material>();
    foreach(var m in pack["materials"])
    {
     if(Hash((string)m["sourceJson"])!=(string)m["sha256"])throw new Exception("Material source changed");
     string path=assetsRoot+((string)m["identityKey"]).Replace('/','_')+".mat";
     bool refracts=m["textures"].Any(t=>(string)t["property"]=="_DTTex");
     var selectedShader=refracts?Shader.Find("Remielle/Source Particle Distortion Adapter"):shader;
     if(!selectedShader||ShaderUtil.ShaderHasError(selectedShader))throw new Exception("Particle adapter shader failed");
     var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
     if(mat==null){mat=new Material(selectedShader);AssetDatabase.CreateAsset(mat,path);}else{var clean=new Material(selectedShader);mat.CopyPropertiesFromMaterial(clean);Object.DestroyImmediate(clean);mat.shader=selectedShader;}
     mat.name=(string)m["name"];
     foreach(var p in ((JObject)m["properties"]["m_Floats"]).Properties())if(mat.HasProperty(p.Name))mat.SetFloat(p.Name,(float)p.Value);
     SourceParticleAlphaBuild.Apply(mat,m);
     foreach(var p in ((JObject)m["properties"]["m_Colors"]).Properties())if(mat.HasProperty(p.Name))
      mat.SetVector(p.Name,new Vector4((float)p.Value["r"],(float)p.Value["g"],(float)p.Value["b"],(float)p.Value["a"]));
     foreach(var t in m["textures"])
     {
      string prop=(string)t["property"];
      if(prop=="_BaseMap"||prop=="_Main")prop="_MainTex";
      if(prop=="_Mask")prop="_MaskTex";
      if(prop=="_Distor")prop="_DistortionTex";
      if(!mat.HasProperty(prop))continue;
      var tex=AssetDatabase.LoadAssetAtPath<Texture2D>((string)t["unityPath"]);if(tex==null)throw new Exception("Missing source texture");
      mat.SetTexture(prop,tex);mat.SetTextureScale(prop,V2(t["scale"]));mat.SetTextureOffset(prop,V2(t["offset"]));
     }
     if(m["textures"].Any(t=>(string)t["property"]=="_BaseMap"))
     {
      var color=m["properties"]["m_Colors"]["_BaseColor"];
      mat.SetColor("_MultiplyParticleColor",new Color((float)color["r"],(float)color["g"],(float)color["b"],(float)color["a"]));
      // Adapter output is premultiplied, including straight-alpha source variants.
      mat.SetFloat("_SrcFactor",1);mat.SetFloat("_DstFactor",10);
     }
     if(m["textures"].Any(t=>(string)t["property"]=="_Main"))
     {
      // This source shader family uses its own selectors. Its generic fields
      // are serialized defaults: reading red instead of alpha produces a white quad.
      var f=m["properties"]["m_Floats"];
      foreach(var alias in new[]{("_MainAlphaswitch","_AlphaChannelMapping"),("_MainColorswitch","_ColorChannelMapping"),
       ("_ASE_MaskTex","_UseMask"),("_MaskAlphaswitch","_MaskChannelMapping")})
      {
       if(f[alias.Item1]==null)throw new Exception("Missing source shader family selector: "+alias.Item1);
       mat.SetFloat(alias.Item2,(float)f[alias.Item1]);
      }
     }
     EditorUtility.SetDirty(mat);materials.Add((string)m["identityKey"],mat);
    }
    var camera=new GameObject("Hit visual GPU audit").AddComponent<Camera>();camera.enabled=false;
    camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.orthographic=true;camera.orthographicSize=9;
    camera.allowHDR=true;camera.nearClipPlane=.01f;camera.farClipPlane=100;
    foreach(var effect in pack["effects"])
    {
     var map=new Dictionary<long,GameObject>();GameObject root=null;
     foreach(var node in effect["nodes"])map.Add((long)node["transformID"],new GameObject((string)node["name"]));
     foreach(var node in effect["nodes"])
     {
      var go=map[(long)node["transformID"]];long parent=(long)node["parentID"];
      if(parent!=0)go.transform.SetParent(map[parent].transform,false);else root=go;
      go.transform.localPosition=V(node["position"]);var q=node["rotation"];
      go.transform.localRotation=new Quaternion((float)q[0],(float)q[1],(float)q[2],(float)q[3]);go.transform.localScale=V(node["scale"]);
      if(node["lightSource"] is JObject lightSource)
      {
       var light=go.AddComponent<Light>();var v=lightSource["values"];var c=v["m_Color"];
       light.type=(LightType)(int)v["m_Type"];light.color=new Color((float)c["r"],(float)c["g"],(float)c["b"],(float)c["a"]);
       light.intensity=(float)v["m_Intensity"];light.range=(float)v["m_Range"];light.enabled=(int)v["m_Enabled"]!=0;
       light.shadows=(LightShadows)(int)v["m_Shadows"]["m_Type"];light.cullingMask=unchecked((int)(uint)v["m_CullingMask"]["m_Bits"]);
       light.renderMode=(LightRenderMode)(int)v["m_RenderMode"];
       light.colorTemperature=(float)v["m_ColorTemperature"];light.useColorTemperature=(bool)v["m_UseColorTemperature"];
      }
      if(node["line"] is JObject lineData)
      {
       var line=go.AddComponent<LineRenderer>();var lineJson=(JObject)lineData.DeepClone();lineJson.Remove("m_GameObject");
       EditorJsonUtility.FromJsonOverwrite(new JObject{["LineRenderer"]=lineJson}.ToString(),line);
       line.sharedMaterials=node["lineMaterialKeys"].Select(k=>materials[(string)k]).ToArray();
       foreach(var material in line.sharedMaterials)if(material.HasProperty("_AdapterCustomColor"))material.SetFloat("_AdapterCustomColor",0);
      }
      if(node["particle"].Type==JTokenType.Null)continue;
      var ps=go.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
      var data=(JObject)node["particle"].DeepClone();data.Remove("m_GameObject");
      EditorJsonUtility.FromJsonOverwrite(new JObject{["ParticleSystem"]=data}.ToString(),ps);
      var main=ps.main;main.playOnAwake=false; // Pool/host explicitly starts this one-shot.
      if(node["shapeMesh"] is JObject shapeSource){var shape=ps.shape;shape.mesh=BuildMesh(shapeSource,assetsRoot);}
      var r=go.GetComponent<ParticleSystemRenderer>();var rendererData=(JObject)node["renderer"].DeepClone();rendererData["m_RenderMode"]=ResolvedRenderMode(node);ApplyRenderer(r,rendererData);
      r.sharedMaterials=new[]{node["materialKey"].Type==JTokenType.Null?null:materials[(string)node["materialKey"]]};r.SetActiveVertexStreams(Streams);
      if(node["meshes"] is JArray meshes&&meshes.Count>0)r.SetMeshes(meshes.Select(m=>BuildMesh(m,assetsRoot)).ToArray());
      else if(node["mesh"].Type!=JTokenType.Null)r.mesh=BuildMesh(node["mesh"],assetsRoot);
     }
     if(root==null)throw new Exception("No root");
     foreach(var node in effect["nodes"].Where(n=>n["lightLink"] is JObject))
     {
      var identity=node["lightLink"]["lightSource"]["identity"];
      var lightNode=effect["nodes"].Single(n=>n["lightSource"] is JObject && JToken.DeepEquals(n["lightSource"]["identity"],identity));
      var light=map[(long)lightNode["transformID"]].GetComponent<Light>();
      var module=map[(long)node["transformID"]].GetComponent<ParticleSystem>().lights;module.light=light;
     }
     foreach(var node in effect["nodes"])map[(long)node["transformID"]].SetActive((bool?)node["activeSelf"]??true);
     var systems=root.GetComponentsInChildren<ParticleSystem>(true);
     camera.transform.position=root.transform.position+new Vector3(0,0,-12);camera.transform.LookAt(root.transform.position);
     if(reviewFolder=="side-visuals")
     {
      var points=root.GetComponentsInChildren<LineRenderer>().Where(l=>l.enabled).SelectMany(l=>Enumerable.Range(0,l.positionCount).Select(i=>l.useWorldSpace?l.GetPosition(i):l.transform.TransformPoint(l.GetPosition(i)))).ToArray();
      if(points.Length==0)throw new Exception("No active source line endpoints for side review");
      var bounds=new Bounds(points[0],Vector3.zero);foreach(var point in points)bounds.Encapsulate(point);
      camera.orthographicSize=Mathf.Max(2,bounds.extents.magnitude*1.2f);
      camera.transform.position=bounds.center+new Vector3(1,.3f,-.25f).normalized*camera.orthographicSize*3;
      camera.transform.LookAt(bounds.center);
     }
     var row=new JObject{["name"]=effect["name"], ["systems"]=systems.Length};var samples=new JArray();row["samples"]=samples;rows.Add(row);
     row["renderers"]=new JArray(systems.Select(ps=>{var r=ps.GetComponent<ParticleSystemRenderer>();return new JObject{
      ["node"]=AnimationUtility.CalculateTransformPath(ps.transform,root.transform),["renderMode"]=(int)r.renderMode,
      ["meshVertices"]=r.mesh?r.mesh.vertexCount:0,["meshTriangles"]=r.mesh?r.mesh.triangles.Length/3:0};}));
     foreach(float t in new[]{.02f,.05f,.08f,.12f,.2f,.4f,.8f,lastSample}.Distinct())
     {
      foreach(var node in effect["nodes"].Where(n=>n["line"] is JObject))
      {
       var line=map[(long)node["transformID"]].GetComponent<LineRenderer>();var sim=node["simulator"];
       if(!(sim is JObject))throw new Exception("Source line simulator missing");
       line.enabled=(bool)node["lineSource"]["values"]["m_Enabled"] && (int)sim["hideNode"]==0 && t>=(float)sim["startDelay"] && t<(float)sim["startDelay"]+(float)sim["duration"];
      }
      foreach(var ps in systems){ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);ps.useAutoRandomSeed=false;ps.randomSeed=14591;
       var node=effect["nodes"].Single(n=>map[(long)n["transformID"]]==ps.gameObject);
       if(ps.gameObject.activeInHierarchy&&(!special||(int)node["simulator"]["isEmptyNode"]==0&&t<(float)node["simulator"]["startDelay"]+(float)node["simulator"]["duration"]))ps.Simulate(t,false,true,false);
       SetParticleTime(ps,t);}
      int streamChecks=0;foreach(var ps in systems)if(ps.particleCount>0)streamChecks+=CheckStreams(ps,camera);
      var image=Capture(camera);int pixels=VisiblePixels(image);
      File.WriteAllBytes(output+reviewFolder+"/"+root.name+"-"+t.ToString("F2",System.Globalization.CultureInfo.InvariantCulture)+".png",image.EncodeToPNG());Object.DestroyImmediate(image);
      samples.Add(new JObject{["time"]=t,["particles"]=systems.Sum(p=>p.particleCount),["visiblePixels"]=pixels,["streamVerticesChecked"]=streamChecks});
     }
     if(!samples.Any(s=>(int)s["visiblePixels"]>50)||(int)samples.Last["visiblePixels"]!=0)throw new Exception("Particle GPU visibility/expiry failed");
     foreach(var ps in systems){ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);ps.Simulate(.02f,false,true,false);ps.GetComponent<Renderer>().enabled=false;}
     int nodeIndex=0;
     foreach(var ps in systems.Where(p=>p.particleCount>0))
     {
      var renderer=ps.GetComponent<Renderer>();renderer.enabled=true;var block=new MaterialPropertyBlock();block.SetFloat("_EffectTime",.02f);renderer.SetPropertyBlock(block);
      var isolated=Capture(camera);File.WriteAllBytes(output+reviewFolder+"/"+root.name+"-node-"+(nodeIndex++)+".png",isolated.EncodeToPNG());Object.DestroyImmediate(isolated);renderer.enabled=false;
     }
     foreach(var node in effect["nodes"].Where(n=>n["particle"].Type!=JTokenType.Null))map[(long)node["transformID"]].GetComponent<Renderer>().enabled=(bool)node["renderer"]["m_Enabled"];
     // A same-time A/B forces only custom dissolve, leaving particle/UV/color data unchanged.
     foreach(var ps in systems){ps.Simulate(.05f,false,true,false);SetParticleTime(ps,.05f);var b=new MaterialPropertyBlock();ps.GetComponent<Renderer>().GetPropertyBlock(b);b.SetFloat("_AuditCustom",0);ps.GetComponent<Renderer>().SetPropertyBlock(b);}
     var before=Capture(camera);
     foreach(var ps in systems){var b=new MaterialPropertyBlock();ps.GetComponent<Renderer>().GetPropertyBlock(b);b.SetFloat("_AuditCustom",1);ps.GetComponent<Renderer>().SetPropertyBlock(b);}
     var after=Capture(camera);var x=before.GetPixels32();var y=after.GetPixels32();int changed=Enumerable.Range(0,x.Length).Count(i=>!x[i].Equals(y[i]));
     row["customDissolveChangedPixels"]=changed;Object.DestroyImmediate(before);Object.DestroyImmediate(after);
     if(!special&&changed<20)throw new Exception("Custom dissolve not visible on GPU");
     row["customDissolveApplicable"]=systems.Any(p=>{var m=p.GetComponent<Renderer>().sharedMaterial;return m&&m.HasProperty("_UseDissolveTex")&&m.GetFloat("_UseDissolveTex")>0&&m.GetFloat("_CustomData1Z")>0;});
     row["customDissolveRequiredGate"]=!special;
     foreach(var ps in systems){ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);ps.GetComponent<Renderer>().SetPropertyBlock(null);ps.useAutoRandomSeed=true;}
     foreach(var node in effect["nodes"].Where(n=>n["line"] is JObject))map[(long)node["transformID"]].GetComponent<LineRenderer>().enabled=(bool)node["lineSource"]["values"]["m_Enabled"];
     string prefab=assetsRoot+root.name+".prefab";var saved=PrefabUtility.SaveAsPrefabAsset(root,prefab);row["prefab"]=prefab;
     if(!saved||saved.GetComponentsInChildren<ParticleSystem>(true).Length!=systems.Length)throw new Exception("Saved particle hierarchy differs");
     foreach(var node in effect["nodes"])
     {
      var original=map[(long)node["transformID"]].transform;string path=AnimationUtility.CalculateTransformPath(original,root.transform);
      var t=SavedTransform(original,root.transform,saved.transform);if(!t)throw new Exception("Saved effect node missing");
      if(t.gameObject.activeSelf!=((bool?)node["activeSelf"]??true))throw new Exception("Saved source activation flag changed");
      if((t.localPosition-original.localPosition).sqrMagnitude>1e-10f||(t.localScale-original.localScale).sqrMagnitude>1e-10f||Quaternion.Angle(t.localRotation,original.localRotation)>.001f)
       throw new Exception("Saved source transform changed: "+path+" position="+(t.localPosition-original.localPosition).sqrMagnitude+" scale="+(t.localScale-original.localScale).sqrMagnitude+" angle="+Quaternion.Angle(t.localRotation,original.localRotation)+" rotations="+t.localRotation.ToString("R")+" / "+original.localRotation.ToString("R"));
      if(node["lightSource"] is JObject ls)
      {var light=t.GetComponent<Light>();if(!light||light.enabled!=((int)ls["values"]["m_Enabled"]!=0)||light.intensity!=(float)ls["values"]["m_Intensity"])throw new Exception("Saved light template changed");}
      if(node["line"] is JObject)
      {
       var line=t.GetComponent<LineRenderer>();var originalLine=original.GetComponent<LineRenderer>();
       if(!line||line.positionCount!=originalLine.positionCount||line.widthMultiplier!=originalLine.widthMultiplier||!line.sharedMaterials.SequenceEqual(originalLine.sharedMaterials))throw new Exception("Saved line differs");
       for(int i=0;i<line.positionCount;i++)if(line.GetPosition(i)!=originalLine.GetPosition(i))throw new Exception("Saved line endpoint changed");
      }
      if(node["particle"].Type==JTokenType.Null)continue;
      if(node["shapeMesh"] is JObject && t.GetComponent<ParticleSystem>().shape.mesh!=original.GetComponent<ParticleSystem>().shape.mesh)throw new Exception("Saved emission mesh changed");
      if(node["lightLink"] is JObject)
      {
       var sourceLight=original.GetComponent<ParticleSystem>().lights.light;
       if(t.GetComponent<ParticleSystem>().lights.light!=SavedTransform(sourceLight.transform,root.transform,saved.transform).GetComponent<Light>())throw new Exception("Saved particle light link changed");
      }
      var r=t.GetComponent<ParticleSystemRenderer>();if((int)r.renderMode!=ResolvedRenderMode(node))throw new Exception("Saved render mode changed");
      if(node["meshes"] is JArray expectedMeshes&&expectedMeshes.Count>0&&r.meshCount!=expectedMeshes.Count)throw new Exception("Saved mesh selection slots differ");
      var expected=node["materialKey"].Type==JTokenType.Null?null:materials[(string)node["materialKey"]];
      if(r.sharedMaterials.Length!=1||r.sharedMaterial!=expected)throw new Exception("Saved material reference changed");
      if(r.renderMode==ParticleSystemRenderMode.Mesh && (!r.mesh||r.mesh.vertexCount!=(int)node["mesh"]["data"]["m_VertexCount"]))throw new Exception("Saved mesh missing");
     }
     row["savedHierarchyChecked"]=true;Object.DestroyImmediate(root);
    }
    // Remove only this builder's superseded pathID-only materials after both
    // prefabs reference fully qualified replacements.
    foreach(var m in pack["materials"])
    {
     string old=assetsRoot+(string)m["pathID"]+".mat";
     if(AssetDatabase.LoadAssetAtPath<Material>(old))AssetDatabase.DeleteAsset(old);
    }
    Object.DestroyImmediate(camera.gameObject);AssetDatabase.SaveAssets();
    report["textures"]=pack["textures"].Count();report["materials"]=materials.Count;report["pass"]=true;
    report["reviewDisplayExposure"]=ReviewExposure;report["reviewDisplayCurve"]="Reinhard per channel then linear-to-sRGB; original material HDR values unchanged";
    report["visualQualityAccepted"]=false;
    var rendererRows=rows.SelectMany(e=>e["renderers"]);
    report["rendererModes"]=new JObject{["billboard"]=rendererRows.Count(r=>(int)r["renderMode"]==0),["stretch"]=rendererRows.Count(r=>(int)r["renderMode"]==1),["mesh"]=rendererRows.Count(r=>(int)r["renderMode"]==4)};
    report["meshVertices"]=rendererRows.Sum(r=>(int)r["meshVertices"]);report["meshTriangles"]=rendererRows.Sum(r=>(int)r["meshTriangles"]);
   }
   catch(Exception e){report["error"]=e.ToString();Debug.LogException(e);}
   File.WriteAllText(output+"hit-visual-build-verification.json",report.ToString());
   if(!(bool)report["pass"])throw new Exception("Hit visual assembly needs review");
  }
  static Transform SavedTransform(Transform source,Transform sourceRoot,Transform savedRoot)
  {
   var indices=new Stack<int>();
   for(var t=source;t!=sourceRoot;t=t.parent){if(!t)throw new Exception("Node outside source root");indices.Push(t.GetSiblingIndex());}
   var result=savedRoot;while(indices.Count>0)result=result.GetChild(indices.Pop());
   return result;
  }
  static int ResolvedRenderMode(JToken node)
  {
   int mode=(int)node["renderer"]["m_RenderMode"];
   if(mode!=6)return mode;
   // Recovered game type 7742 lists Billboard, Stretch, HorizontalBillboard,
   // VerticalBillboard, Mesh, Text, None. Treat 6 as None in this adapter.
   // Declaration order is evidence for the shift, not recovered constants;
   // retain that qualification in render-mode-review.json. Materials may be
   // retained on non-rendering particle nodes and do not imply Billboard.
   return (int)ParticleSystemRenderMode.None;
  }
  static void ApplyRenderer(ParticleSystemRenderer r,JToken a)
  {
   r.enabled=(bool)a["m_Enabled"];r.renderMode=(ParticleSystemRenderMode)(int)a["m_RenderMode"];r.sortMode=(ParticleSystemSortMode)(int)a["m_SortMode"];
   r.minParticleSize=(float)a["m_MinParticleSize"];r.maxParticleSize=(float)a["m_MaxParticleSize"];
   r.cameraVelocityScale=(float)a["m_CameraVelocityScale"];r.velocityScale=(float)a["m_VelocityScale"];r.lengthScale=(float)a["m_LengthScale"];
   r.sortingFudge=(float)a["m_SortingFudge"];r.normalDirection=(float)a["m_NormalDirection"];r.shadowBias=(float)a["m_ShadowBias"];
   r.alignment=(ParticleSystemRenderSpace)(int)a["m_RenderAlignment"];r.pivot=V(a["m_Pivot"]);r.flip=V(a["m_Flip"]);
   r.allowRoll=(bool)a["m_AllowRoll"];r.applyActiveColorSpace=(bool)a["m_ApplyActiveColorSpace"];r.enableGPUInstancing=(bool)a["m_EnableGPUInstancing"];
   r.sortingOrder=(int)a["m_SortingOrder"];r.shadowCastingMode=(ShadowCastingMode)(int)a["m_CastShadows"];r.receiveShadows=(bool)a["m_ReceiveShadows"];
   r.lightProbeUsage=(LightProbeUsage)(int)a["m_LightProbeUsage"];r.reflectionProbeUsage=(ReflectionProbeUsage)(int)a["m_ReflectionProbeUsage"];
   r.sortingLayerID=(int)a["m_SortingLayerID"];r.renderingLayerMask=(uint)a["m_RenderingLayerMask"];r.rendererPriority=(int)a["m_RendererPriority"];
   r.motionVectorGenerationMode=(MotionVectorGenerationMode)(int)a["m_MotionVectors"];r.allowOcclusionWhenDynamic=(bool)a["m_DynamicOccludee"];
   r.maskInteraction=(SpriteMaskInteraction)(int)a["m_MaskInteraction"];
  }
  static Mesh BuildMesh(JToken source,string assetsRoot)
  {
   if(Hash((string)source["sourceJson"])!=(string)source["sha256"])throw new Exception("Particle mesh source changed");
   var d=source["data"];int count=(int)d["m_VertexCount"];
   if(d["m_SubMeshes"].Count()!=1||(string)d["m_SubMeshes"][0]["topology"]!="Triangles")throw new Exception("Unexpected particle mesh topology");
   var vertices=new Vector3[count];var normals=new Vector3[count];var uv=new Vector2[count];
   for(int i=0;i<count;i++)
   {
    vertices[i]=new Vector3((float)d["m_Vertices"][i*3],(float)d["m_Vertices"][i*3+1],(float)d["m_Vertices"][i*3+2]);
    normals[i]=new Vector3((float)d["m_Normals"][i*3],(float)d["m_Normals"][i*3+1],(float)d["m_Normals"][i*3+2]);
    uv[i]=new Vector2((float)d["m_UV0"][i*2],(float)d["m_UV0"][i*2+1]);
   }
   string path=assetsRoot+(string)source["sourceBlock"]+"_"+(string)source["cab"]+"_"+(string)source["pathID"]+".asset";
   var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(!mesh){mesh=new Mesh();AssetDatabase.CreateAsset(mesh,path);}else mesh.Clear();
   mesh.name=(string)d["m_Name"];mesh.vertices=vertices;mesh.normals=normals;mesh.uv=uv;mesh.triangles=d["m_Indices"].Select(x=>(int)x).ToArray();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
   return mesh;
  }
  static int CheckStreams(ParticleSystem ps,Camera camera)
  {
   if(ps.GetComponent<ParticleSystemRenderer>().renderMode==ParticleSystemRenderMode.None)return 0;
   var c1=new List<Vector4>();var c2=new List<Vector4>();ps.GetCustomParticleData(c1,ParticleSystemCustomData.Custom1);ps.GetCustomParticleData(c2,ParticleSystemCustomData.Custom2);
   var mesh=new Mesh();ps.GetComponent<ParticleSystemRenderer>().BakeMesh(mesh,camera,ParticleSystemBakeMeshOptions.Default);
   var a=new List<Vector4>();var b=new List<Vector4>();var c=new List<Vector4>();mesh.GetUVs(0,a);mesh.GetUVs(1,b);mesh.GetUVs(2,c);
   if(a.Count==0||a.Count!=b.Count||a.Count!=c.Count)throw new Exception("Missing baked custom streams");
   for(int i=0;i<a.Count;i++)
   {
    var one=new Vector4(a[i].z,a[i].w,b[i].x,b[i].y);var two=new Vector4(b[i].z,b[i].w,c[i].x,c[i].y);
    if(!Enumerable.Range(0,ps.particleCount).Any(k=>StreamEqual(one,k<c1.Count?c1[k]:Vector4.zero)&&StreamEqual(two,k<c2.Count?c2[k]:Vector4.zero)))throw new Exception("Custom stream packing differs from shader inputs: "+ps.name+" vertex "+i+" custom1="+one+" custom2="+two+" counts="+c1.Count+"/"+c2.Count+" time="+ps.time+" closestCPU="+(c2.Count>0?c2.OrderBy(v=>(v-two).sqrMagnitude).First().ToString("R"):"none")+" baked="+two.ToString("R"));
   }
   int n=a.Count;Object.DestroyImmediate(mesh);return n;
  }
  static bool StreamEqual(Vector4 a,Vector4 b)
  {
   // Source HDR custom colors reach hundreds. Float bake/readback error scales
   // with magnitude; retain a per-component check, including near-zero fields.
   for(int i=0;i<4;i++)if(!float.IsFinite(a[i])||!float.IsFinite(b[i])||Mathf.Abs(a[i]-b[i])>4e-6f*Mathf.Max(1,Mathf.Abs(b[i])))return false;
   return true;
  }
  static void SetParticleTime(ParticleSystem ps,float t)
  {
   var block=new MaterialPropertyBlock();block.SetFloat("_EffectTime",t);
   block.SetFloat("_AdapterCustomColor",ps.customData.enabled&&ps.customData.GetMode(ParticleSystemCustomData.Custom2)!=ParticleSystemCustomDataMode.Disabled?1:0);
   ps.GetComponent<Renderer>().SetPropertyBlock(block);
  }
  static Texture2D Capture(Camera camera)
  {
   var old=RenderTexture.active;var rt=RenderTexture.GetTemporary(768,768,24,RenderTextureFormat.ARGBHalf);camera.targetTexture=rt;
   camera.Render();RenderTexture.active=rt;var hdr=new Texture2D(768,768,TextureFormat.RGBAFloat,false,true);hdr.ReadPixels(new Rect(0,0,768,768),0,0);hdr.Apply();
   var values=hdr.GetPixels();for(int i=0;i<values.Length;i++)
   {
    var v=values[i]*ReviewExposure;
    if(float.IsNaN(v.r)||float.IsNaN(v.g)||float.IsNaN(v.b)||float.IsInfinity(v.r)||float.IsInfinity(v.g)||float.IsInfinity(v.b))throw new Exception("Non-finite HDR particle pixel");
    values[i]=new Color(Mathf.LinearToGammaSpace(v.r/(1+v.r)),Mathf.LinearToGammaSpace(v.g/(1+v.g)),Mathf.LinearToGammaSpace(v.b/(1+v.b)),1);
   }
   var result=new Texture2D(768,768,TextureFormat.RGBA32,false,false);result.SetPixels(values);result.Apply();Object.DestroyImmediate(hdr);
   camera.targetTexture=null;RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);return result;
  }
  static int VisiblePixels(Texture2D image)=>image.GetPixels32().Count(c=>c.r>3||c.g>3||c.b>3);
 }
}
