using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

// Read-only source-prefab audit. Snapshot meshes and ID materials live only in
// an unsaved scene; neither the model nor authored animation curves are saved.
public static class RemielleAccessoryAttributionAudit
{
    const string Out="E:/ZZZ/local-only/RemielleRenderingReview/20260905/ui-live-binding/accessories/attribution/";
    const string WriteRoot = "E:/ZZZ/ZCode/90_Builds/RenderingReview/20260905/ui-live-binding/accessories/attribution/"; // D1-c 写根（读根保留 A 类）
    const string Prefab="Assets/V3/Remielle_V3_Animated.prefab";
    const int W=720,H=1024;
    static JArray V(Vector3 v)=>new JArray(v.x,v.y,v.z);
    static JObject Ref(string p)=>RemielleUINativePostBuild.Ref(p);
    static Vector3[] Points(MeshRenderer r)=>r.GetComponent<MeshFilter>().sharedMesh.vertices.Select(v=>r.transform.TransformPoint(v)).ToArray();
    static Bounds BoundsOf(Vector3[] p){var b=new Bounds(p[0],Vector3.zero);foreach(var v in p)b.Encapsulate(v);return b;}
    static void Frame(Camera c,Vector3[] p)
    {
        var b=BoundsOf(p);c.orthographic=true;c.aspect=(float)W/H;c.orthographicSize=Mathf.Max(b.size.y/2,b.size.x/(2*c.aspect))*1.08f;
        c.nearClipPlane=.001f;c.farClipPlane=100;c.transform.position=b.center+Vector3.forward*5;c.transform.LookAt(b.center);
    }
    static JObject Capture(Camera c,string name,bool linear)
    {
        var old=RenderTexture.active;var rt=new RenderTexture(W,H,24,RenderTextureFormat.ARGB32,linear?RenderTextureReadWrite.Linear:RenderTextureReadWrite.sRGB){antiAliasing=1};
        var t=new Texture2D(W,H,TextureFormat.RGBA32,false,linear);
        try
        {
            rt.Create();c.targetTexture=rt;c.Render();RenderTexture.active=rt;t.ReadPixels(new Rect(0,0,W,H),0,0);t.Apply();
            File.WriteAllBytes(WriteRoot+name+".png",t.EncodeToPNG());
            var pixels=t.GetPixels32();var counts=new int[28];
            if(linear)
            {
                File.WriteAllBytes(WriteRoot+name+".rgba8",t.GetRawTextureData<byte>().ToArray());
                foreach(var p in pixels){if(p.g!=0||p.b!=0||p.r>27)throw new Exception("Unexpected ID color");counts[p.r]++;}
            }
            return new JObject{["name"]=name,["png"]=Ref(Out+name+".png"),["countsById"]=new JArray(counts),["linearId"]=linear,["width"]=W,["height"]=H};
        }
        finally{c.targetTexture=null;RenderTexture.active=old;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(t);}
    }
    static JArray BoneChain(Transform bone,RemielleNativeAnimation driver)
    {
        var rows=new JArray();for(var t=bone;t!=null&&t!=driver.transform;t=t.parent)
        {
            var link=driver.bones.SingleOrDefault(b=>b.target==t);
            rows.Add(new JObject{["targetName"]=t.name,["targetScale"]=V(t.localScale),["targetPosition"]=V(t.localPosition),
                ["sourcePath"]=link.source?AnimationUtility.CalculateTransformPath(link.source,driver.nativeAnimation.transform):null,
                ["sourceScale"]=link.source?V(link.source.localScale):null,["sourceRestScale"]=link.source?V(link.sourceRestScale):null});
        }
        return rows;
    }
    public static void Run()
    {
        Directory.CreateDirectory(WriteRoot);if(SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Direct3D11)throw new Exception("D3D11 required");
        var before=Ref(Path.GetFullPath(Prefab));EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab));var driver=root.GetComponent<RemielleNativeAnimation>();
        driver.autoplay=false;driver.nativeAnimation.Stop();var skins=root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s=>s.enabled&&s.gameObject.activeInHierarchy).OrderBy(s=>s.name,StringComparer.Ordinal).ToArray();
        if(skins.Length!=27)throw new Exception("Expected 27 source renderers");
        var cam=new GameObject("Accessory attribution camera").AddComponent<Camera>();cam.enabled=false;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=Color.black;cam.allowHDR=false;cam.allowMSAA=false;
        var sun=new GameObject("Diagnostic key light").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1;sun.transform.rotation=Quaternion.Euler(25,160,0);
        RenderSettings.skybox=null;RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=Color.gray;
        var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Editor/RemielleAccessoryId.shader");if(!shader||!shader.isSupported)throw new Exception("ID shader unsupported");
        var idMaterials=skins.Select((s,i)=>{var m=new Material(shader);m.SetVector("_IdColor",new Vector4((i+1)/255f,0,0,1));return m;}).ToArray();
        var poses=new JArray();var curveClips=new JArray();
        try
        {
            foreach(var pose in new[]{"source-default","Idle_Loop-0"})
            {
                driver.ResetSourcePose();driver.ApplyPose();if(pose!="source-default")driver.Sample("Idle_Loop",0);
                var row=new JObject{["pose"]=pose};var meshRows=new JArray();var images=new JArray();
                using(var snapshot=new RemiellePoseSnapshot(root))
                {
                    var copies=skins.Select(s=>s.transform.Find(s.name+"_PoseSnapshot").GetComponent<MeshRenderer>()).ToArray();
                    var originals=copies.Select(c=>c.sharedMaterials).ToArray();Frame(cam,copies.SelectMany(Points).ToArray());
                    row["camera"]=new JObject{["position"]=V(cam.transform.position),["rotation"]=V(cam.transform.eulerAngles),["orthographicSize"]=cam.orthographicSize};
                    for(int i=0;i<skins.Length;i++)
                    {
                        var s=skins[i];var points=Points(copies[i]);var b=BoundsOf(points);var screen=points.Select(p=>cam.WorldToViewportPoint(p)).ToArray();var sb=BoundsOf(screen);
                        var mesh=copies[i].GetComponent<MeshFilter>().sharedMesh;double area=0;var tris=mesh.triangles;
                        for(int k=0;k<tris.Length;k+=3)area+=Vector3.Cross(points[tris[k+1]]-points[tris[k]],points[tris[k+2]]-points[tris[k]]).magnitude*.5;
                        meshRows.Add(new JObject{["id"]=i+1,["name"]=s.name,["meshAsset"]=AssetDatabase.GetAssetPath(s.sharedMesh),["vertexCount"]=points.Length,["boneCount"]=s.bones.Length,
                            ["worldMin"]=V(b.min),["worldMax"]=V(b.max),["viewportMin"]=V(sb.min),["viewportMax"]=V(sb.max),["worldTriangleArea"]=area,["rootBoneChain"]=BoneChain(s.rootBone,driver),
                            ["skinBones"]=new JArray(s.bones.Select((bone,index)=>new JObject{["index"]=index,["name"]=bone.name,["chain"]=BoneChain(bone,driver)}))});
                        copies[i].sharedMaterials=Enumerable.Repeat(idMaterials[i],mesh.subMeshCount).ToArray();copies[i].SetPropertyBlock(null);
                    }
                    images.Add(Capture(cam,pose+"-ids",true));
                    if(pose=="Idle_Loop-0")
                    {
                        for(int i=0;i<copies.Length;i++)
                        {
                            for(int j=0;j<copies.Length;j++)copies[j].enabled=j==i;
                            var image=Capture(cam,$"isolated-{i+1:00}",true);
                            var counts=image["countsById"].Select(v=>(int)v).ToArray();
                            if(counts.Where((v,k)=>k!=0&&k!=i+1).Any(v=>v!=0))throw new Exception("Isolated ID mismatch");
                            images.Add(image);
                        }
                    }
                    for(int i=0;i<copies.Length;i++){copies[i].enabled=true;copies[i].sharedMaterials=originals[i];}
                    images.Add(Capture(cam,pose+"-material-preview",false));
                }
                row["meshes"]=meshRows;row["images"]=images;poses.Add(row);
            }
            var relevantPaths=new HashSet<string>();foreach(var s in skins)foreach(var bone in s.bones)foreach(JObject b in BoneChain(bone,driver))if(b["sourcePath"]?.Type==JTokenType.String)relevantPaths.Add((string)b["sourcePath"]);
            foreach(AnimationState state in driver.nativeAnimation)
            {
                var clip=state.clip;var rows=new JArray();
                foreach(var binding in AnimationUtility.GetCurveBindings(clip).Where(b=>b.type==typeof(Transform)&&b.propertyName.StartsWith("m_LocalScale.")&&relevantPaths.Contains(b.path)))
                {
                    var c=AnimationUtility.GetEditorCurve(clip,binding);
                    rows.Add(new JObject{["path"]=binding.path,["property"]=binding.propertyName,["keys"]=new JArray(c.keys.Select(k=>new JObject{["time"]=k.time,["value"]=k.value,["inTangent"]=k.inTangent,["outTangent"]=k.outTangent,["inWeight"]=k.inWeight,["outWeight"]=k.outWeight,["weightedMode"]=(int)k.weightedMode}))});
                }
                curveClips.Add(new JObject{["name"]=state.name,["length"]=clip.length,["clip"]=Ref(Path.GetFullPath(AssetDatabase.GetAssetPath(clip))),["scaleCurves"]=rows});
            }
            if(curveClips.Count!=15)throw new Exception("Expected 15 existing clips");
            var after=Ref(Path.GetFullPath(Prefab));if((string)before["sha256"]!=(string)after["sha256"])throw new Exception("Source prefab changed");
            File.WriteAllText(WriteRoot+"unity-attribution.json",new JObject{["pass"]=true,["schema"]="remielle-accessory-attribution-v1",["utc"]=DateTime.UtcNow.ToString("O"),["device"]=SystemInfo.graphicsDeviceName,
                ["prefabBefore"]=before,["prefabAfter"]=after,["sourceModified"]=false,["poses"]=poses,["scaleCurves"]=curveClips,["imagePurpose"]="ID attribution and illustrative material preview, not a native lighting comparison"}.ToString());
            Debug.Log("REMIELLE_ACCESSORY_ATTRIBUTION_OK");
        }
        finally{foreach(var m in idMaterials)Object.DestroyImmediate(m);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
    }
}
