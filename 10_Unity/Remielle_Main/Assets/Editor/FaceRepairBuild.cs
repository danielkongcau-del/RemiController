using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

public static class FaceRepairBuild
{
    const string Out="E:/ZZZ/local-only/RemielleModelReadiness/20260904/face";
    public static void Run()
    {
        RemielleImportSettings.ApplyAnimationSettings();
        var root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/V2b/Remielle_V2b_Materials.prefab"));
        root.name="Remielle_V2b";
        var lighting=root.GetComponent<RemielleFaceLighting>()??root.AddComponent<RemielleFaceLighting>();
        lighting.Calibrate(root.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Bip001 Head"),
            root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s=>s.name=="SMR_Remielle_Face"||s.name=="SMR_Remielle_Eyebrow").Cast<Renderer>().ToArray());
        PrefabUtility.SaveAsPrefabAsset(root,"Assets/V2b/Remielle_V2b_Materials.prefab");Object.DestroyImmediate(root);
        NativeAnimationBuild.RebuildRig();
        NativeAnimationReview.Run();
        Verify();
    }
    public static void Verify() => VerifyTo(Out);
    public static void VerifyTo(string output)
    {
        Directory.CreateDirectory(output);
        var errors=new List<string>();
        Application.LogCallback handler=(text,stack,type)=>{if(type==LogType.Error||type==LogType.Exception)errors.Add(text);};
        Application.logMessageReceived+=handler;
        try
        {
            if(!PlayerSettings.legacyClampBlendShapeWeights)throw new Exception("Native expression clamp is disabled");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
            var root=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/V3/Remielle_V3_Animated.prefab"));
            var bridge=root.GetComponent<RemielleNativeAnimation>();var lighting=root.GetComponent<RemielleFaceLighting>();
            if(lighting==null)throw new Exception("Missing animated face lighting");
            var skins=new JArray();
            foreach(var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if(renderer.bones.Length!=renderer.sharedMesh.bindposes.Length||renderer.bones.Any(b=>b==null))throw new Exception("Invalid skin: "+renderer.name);
                Selection.activeGameObject=renderer.gameObject;
                UnityEditorInternal.InternalEditorUtility.CalculateSelectionBounds(false,false,false);
                skins.Add(new JObject{["name"]=renderer.name,["bones"]=renderer.bones.Length,["bindposes"]=renderer.sharedMesh.bindposes.Length,["enabled"]=renderer.enabled});
            }
            Selection.activeGameObject=root;
            UnityEditorInternal.InternalEditorUtility.CalculateSelectionBounds(false,false,false);
            var poses=new JArray();float maxSkinError=0,maxAxisError=0;int negativeWeights=0;
            var headBind=lighting.head.rotation;
            foreach(AnimationState state in bridge.nativeAnimation)
            {
                foreach(float fraction in new[]{0f,.25f,.5f,.75f,1f})
                {
                    bridge.Sample(state.name,state.length*fraction);
                    float poseSkinError=0;
                    foreach(var link in bridge.morphs)
                    {
                        poseSkinError=Mathf.Max(poseSkinError,VerifySkin(link.target));
                        for(int i=0;i<link.target.sharedMesh.blendShapeCount;i++)if(link.target.GetBlendShapeWeight(i)<0)negativeWeights++;
                    }
                    var expected=(lighting.head.rotation*Quaternion.Inverse(headBind))*Vector3.forward;
                    foreach(var renderer in lighting.renderers)
                    {
                        var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block);
                        if(block.GetFloat("_HeadDirectionsWorldSpace")!=1)throw new Exception("Face shader directions are not explicitly world-space");
                        var actual=((Vector3)block.GetVector("_headForwardVector")).normalized;
                        maxAxisError=Mathf.Max(maxAxisError,(actual-expected).magnitude);
                    }
                    maxSkinError=Mathf.Max(maxSkinError,poseSkinError);
                    poses.Add(new JObject{["clip"]=state.name,["time"]=state.length*fraction,["skinVertexMaxError"]=poseSkinError});
                }
            }
            // Independent float32 world-space sums include long animated bone
            // chains. 0.05 mm permits their rounding, well below the millimetre
            // expression distortion that the native clamp corrects.
            if(maxSkinError>0.00005f||maxAxisError>0.00001f)throw new Exception($"Face verification failed: skin={maxSkinError}, head={maxAxisError}");
            var light=new GameObject("FaceCheckLight").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1;light.shadows=LightShadows.None;light.transform.rotation=Quaternion.Euler(25,160,0);
            RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;RenderSettings.ambientLight=new Color(.25f,.25f,.25f);
            var cam=new GameObject("FaceCheckCamera").AddComponent<Camera>();cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.07f,.07f,.09f);cam.fieldOfView=35;cam.nearClipPlane=.001f;cam.farClipPlane=100;
            var face=bridge.morphs.Single(m=>m.target.name=="SMR_Remielle_Face").target;
            foreach(string clip in new[]{"Idle_Loop","MC_Idle_Loop"})foreach(float fraction in new[]{0f,.5f})
            {
                bridge.Sample(clip,bridge.nativeAnimation[clip].length*fraction);
                FaceShot(root,face,cam,output+"/after-"+clip+"-"+(fraction==0?"start":"mid")+".png");
            }
            bridge.Sample("Idle_Loop",0);
            foreach(float yaw in new[]{-60f,0f,60f})
            {
                light.transform.rotation=Quaternion.Euler(25,180+yaw,0);
                FaceShot(root,face,cam,output+"/after-light-"+yaw+".png");
            }
            Selection.activeGameObject=null;
            if(errors.Count!=0)throw new Exception(string.Join("\n",errors));
            File.WriteAllText(output+"/verification.json",new JObject{["pass"]=true,["utc"]=DateTime.UtcNow.ToString("O"),["legacyClampBlendShapeWeights"]=true,["renderers"]=skins,["poses"]=poses,["rawNegativeWeightsPreserved"]=negativeWeights,["clampedSkinVertexMaxError"]=maxSkinError,["headDirectionMaxError"]=maxAxisError,["selectionBoundsErrors"]=new JArray(errors),["captureMethod"]="CPU baked snapshots of actual native skin poses"}.ToString());
            Object.DestroyImmediate(root);Debug.Log("FACE_REPAIR_VERIFIED");
        }
        finally { Application.logMessageReceived-=handler; }
    }
    static float VerifySkin(SkinnedMeshRenderer renderer)
    {
        var mesh=renderer.sharedMesh;var expected=mesh.vertices;var delta=new Vector3[mesh.vertexCount];
        for(int i=0;i<mesh.blendShapeCount;i++)
        {
            if(mesh.GetBlendShapeFrameCount(i)!=1||mesh.GetBlendShapeFrameWeight(i,0)!=100)throw new Exception("Unexpected native expression range");
            mesh.GetBlendShapeFrameVertices(i,0,delta,null,null);
            float weight=Mathf.Clamp(renderer.GetBlendShapeWeight(i),0,100)/100;
            for(int j=0;j<expected.Length;j++)expected[j]+=delta[j]*weight;
        }
        var skin=mesh.boneWeights;var matrices=renderer.bones.Select((bone,i)=>bone.localToWorldMatrix*mesh.bindposes[i]).ToArray();
        var baked=new Mesh();renderer.BakeMesh(baked);var actual=baked.vertices;float error=0;
        for(int j=0;j<expected.Length;j++)
        {
            var b=skin[j];var v=expected[j];
            var p=matrices[b.boneIndex0].MultiplyPoint3x4(v)*b.weight0+matrices[b.boneIndex1].MultiplyPoint3x4(v)*b.weight1+matrices[b.boneIndex2].MultiplyPoint3x4(v)*b.weight2+matrices[b.boneIndex3].MultiplyPoint3x4(v)*b.weight3;
            var a=renderer.transform.TransformPoint(actual[j]);
            if(float.IsNaN(a.x)||float.IsNaN(a.y)||float.IsNaN(a.z))throw new Exception("Nonfinite expression vertex");
            error=Mathf.Max(error,(a-p).magnitude);
        }
        Object.DestroyImmediate(baked);return error;
    }
    static void FaceShot(GameObject root,SkinnedMeshRenderer face,Camera cam,string path)
    {
        var mesh=new Mesh();face.BakeMesh(mesh);var vertices=mesh.vertices.Select(v=>face.transform.TransformPoint(v)).ToArray();
        var bounds=new Bounds(vertices[0],Vector3.zero);foreach(var v in vertices)bounds.Encapsulate(v);Object.DestroyImmediate(mesh);
        cam.transform.position=bounds.center+Vector3.forward*.62f;cam.transform.rotation=Quaternion.LookRotation(Vector3.back,Vector3.up);
        using(new RemiellePoseSnapshot(root))
        {
            var rt=new RenderTexture(720,1024,24);cam.targetTexture=rt;cam.Render();var previous=RenderTexture.active;RenderTexture.active=rt;
            var texture=new Texture2D(720,1024,TextureFormat.RGBA32,false);texture.ReadPixels(new Rect(0,0,720,1024),0,0);texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());RenderTexture.active=previous;cam.targetTexture=null;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(texture);
        }
    }
}
