using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
public static class NativeAnimationReview
{
    const string Out="E:/ZZZ/ZCode/90_Builds/RuntimeRepair/20260904";
    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var root=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/V3/Remielle_V3_Animated.prefab"));
        var driver=root.GetComponent<RemielleNativeAnimation>();var profile=root.GetComponent<RemielleRuntimeProfile>();
        var light=new GameObject("ReviewLight").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1;light.color=Color.white;light.transform.rotation=Quaternion.Euler(25,160,0);light.shadows=LightShadows.None;
        RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;RenderSettings.ambientLight=new Color(.25f,.25f,.25f);
        var cam=new GameObject("ReviewCamera").AddComponent<Camera>();cam.tag="MainCamera";cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.07f,.07f,.09f);cam.nearClipPlane=.01f;cam.farClipPlane=100;cam.fieldOfView=32;
        cam.aspect=960f/1200;
        var post=cam.gameObject.AddComponent<RemiellePostLut>();
        post.lutShader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/NativeRuntimeLut.shader");
        if(post.lutShader==null)throw new InvalidOperationException("Native post LUT shader missing");
        var controls=cam.gameObject.AddComponent<RemielleReviewControls>();controls.model=driver;controls.profile=profile;controls.post=post;controls.battlePost=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/SourceAssets/Runtime/RuntimePostBattle.asset");
        controls.follow=driver.bones.First(x=>x.target.name=="Bip001 Pelvis").target;
        var report=new JArray();
        foreach(string clip in new[]{"Idle_Loop","MC_Idle_Loop","Walk_Loop","Run_Loop_02","Run_Transform_02"})
        {
            foreach(float fraction in new[]{0f,.5f})
            {
                float time=driver.nativeAnimation[clip].length*fraction;driver.Sample(clip,time);
                var bounds=Bounds(root);Frame(cam,bounds);
                string path=Out+"/animation-"+clip+"-"+(fraction==0?"start":"mid")+".png";
                using(new RemiellePoseSnapshot(root)) Shot(cam,path);
                report.Add(new JObject{["clip"]=clip,["time"]=time,["render"]=path,["boundsCenter"]=new JArray(bounds.center.x,bounds.center.y,bounds.center.z),["boundsSize"]=new JArray(bounds.size.x,bounds.size.y,bounds.size.z)});
            }
        }
        driver.Sample("Idle_Loop",0);Frame(cam,Bounds(root));
        Directory.CreateDirectory("Assets/V3");EditorSceneManager.SaveScene(SceneManager.GetActiveScene(),"Assets/V3/Remielle_AnimationReview.unity");
        File.WriteAllText(Out+"/animation-render-review.json",new JObject{["captureMethod"]="CPU baked native poses; avoids same-frame GPU skin cache",["renders"]=report}.ToString());Debug.Log("NATIVE_ANIMATION_REVIEW_RENDERED");
    }
    static Bounds Bounds(GameObject root)
    {
        var bounds=new Bounds();bool first=true;
        foreach(var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if(!renderer.enabled || !renderer.gameObject.activeInHierarchy)continue;
            // Frame the character; retain auxiliary/pooled meshes in the model.
            string name=renderer.sharedMesh.name;
            if(!name.Contains("Body")&&!name.Contains("Face")&&!name.Contains("Hair")&&!name.Contains("Wings"))continue;
            var mesh=new Mesh();renderer.BakeMesh(mesh);
            foreach(var vertex in mesh.vertices){var point=renderer.transform.TransformPoint(vertex);if(first){bounds=new Bounds(point,Vector3.zero);first=false;}else bounds.Encapsulate(point);}
            Object.DestroyImmediate(mesh);
        }
        return bounds;
    }
    static void Frame(Camera camera,Bounds bounds)
    {
        float distance=Mathf.Max(bounds.extents.y,bounds.extents.x/(960f/1200))/Mathf.Tan(camera.fieldOfView*Mathf.Deg2Rad/2)*1.3f+bounds.extents.z;
        camera.transform.position=bounds.center+Vector3.forward*distance;camera.transform.LookAt(bounds.center);
    }
    static void Shot(Camera cam,string path)
    {
        var rt=new RenderTexture(960,1200,24);cam.targetTexture=rt;cam.Render();var previous=RenderTexture.active;RenderTexture.active=rt;
        var texture=new Texture2D(960,1200,TextureFormat.RGBA32,false);texture.ReadPixels(new Rect(0,0,960,1200),0,0);texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());RenderTexture.active=previous;cam.targetTexture=null;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(texture);
    }
}
