using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

// Checks the currently saved model. Never regenerates meshes, clips or prefabs.
public static class ModelReadinessBuild
{
    const string Out="E:/ZZZ/ZCode/90_Builds/ModelReadiness/20260904";
    public static void Run()
    {
        Directory.CreateDirectory(Out);
        FullProjectAudit.RunReadOnly(Out);
        FinishBuild();
    }
    public static void FinishBuild()
    {
        InspectModel();
        FullPlayerBuild.BuildTo(Out+"/Player");
        Debug.Log("MODEL_READINESS_BUILD_VERIFIED");
    }
    public static void InspectModel()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        var root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/V3/Remielle_V3_Animated.prefab"));
        var driver=root.GetComponent<RemielleNativeAnimation>();
        var errors=new List<string>();var rows=new JArray();int references=0;
        foreach(var component in root.GetComponentsInChildren<Component>(true))
        {
            if(component==null){errors.Add("Missing script");continue;}
            using var so=new SerializedObject(component);var property=so.GetIterator();
            while(property.Next(true))if(property.propertyType==SerializedPropertyType.ObjectReference)
            {
                references++;
                if(property.objectReferenceValue==null&&property.objectReferenceInstanceIDValue!=0)
                    errors.Add(component.name+" broken reference "+property.propertyPath);
            }
        }
        var renderers=root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach(var r in renderers)
        {
            var mesh=r.sharedMesh;
            if(mesh==null){errors.Add(r.name+" missing mesh");continue;}
            var vertices=mesh.vertices;var normals=mesh.normals;var tangents=mesh.tangents;
            if(vertices.Any(v=>!Finite(v))||normals.Any(v=>!Finite(v))||tangents.Any(v=>!Finite(v)||!float.IsFinite(v.w)))errors.Add(r.name+" nonfinite vertex channel");
            if(mesh.bindposes.Any(m=>Enumerable.Range(0,16).Any(i=>!float.IsFinite(m[i]))))errors.Add(r.name+" nonfinite bindpose");
            float weightError=0;
            foreach(var w in mesh.boneWeights)
            {
                var ws=new[]{w.weight0,w.weight1,w.weight2,w.weight3};
                var ids=new[]{w.boneIndex0,w.boneIndex1,w.boneIndex2,w.boneIndex3};
                if(ws.Any(v=>!float.IsFinite(v)||v<0))errors.Add(r.name+" invalid weight");
                weightError=Mathf.Max(weightError,Mathf.Abs(ws.Sum()-1));
                for(int i=0;i<4;i++)if(ws[i]>0&&(ids[i]<0||ids[i]>=r.bones.Length))errors.Add(r.name+" invalid joint index");
            }
            if(weightError>1e-5f)errors.Add(r.name+" unnormalized weights");
            if(r.enabled&&r.sharedMaterials.Length!=mesh.subMeshCount)errors.Add(r.name+" material slots != submeshes");
            if(r.enabled&&r.sharedMaterials.Any(m=>m==null))errors.Add(r.name+" null material");
            rows.Add(new JObject{["name"]=r.name,["enabled"]=r.enabled,["vertices"]=mesh.vertexCount,["submeshes"]=mesh.subMeshCount,["updateWhenOffscreen"]=r.updateWhenOffscreen,["weightSumMaxError"]=weightError});
        }
        int checkedVertices=0;float maxOutsideBounds=0;var poses=new JArray();
        foreach(AnimationState state in driver.nativeAnimation)foreach(float f in new[]{0f,.25f,.5f,.75f,1f})
        {
            driver.Sample(state.name,state.length*f);float poseOutside=0;
            foreach(var r in renderers.Where(r=>r.enabled))
            {
                var baked=new Mesh();r.BakeMesh(baked);var bounds=r.bounds;
                foreach(var vertex in baked.vertices)
                {
                    var world=r.transform.TransformPoint(vertex);checkedVertices++;
                    if(!Finite(world))throw new Exception("Nonfinite deformed vertex: "+r.name);
                    poseOutside=Mathf.Max(poseOutside,Vector3.Distance(world,bounds.ClosestPoint(world)));
                }
                Object.DestroyImmediate(baked);
            }
            maxOutsideBounds=Mathf.Max(maxOutsideBounds,poseOutside);
            poses.Add(new JObject{["clip"]=state.name,["fraction"]=f,["maxDistanceOutsideRendererBounds"]=poseOutside});
        }
        // Engine-computed renderer bounds are refreshed during render updates,
        // not each explicit same-frame Sample(). The player probe must gate
        // culling coverage after real frames and rendering, without enlarging bounds.
        File.WriteAllText(Out+"/model-integrity.json",new JObject{["pass"]=errors.Count==0,["utc"]=DateTime.UtcNow.ToString("O"),["serializedReferencesChecked"]=references,["renderers"]=rows,["deformedVerticesChecked"]=checkedVertices,["poses"]=poses,["boundsStatus"]="Same-frame diagnostics only; requires player bounds gate",["errors"]=new JArray(errors)}.ToString());
        Object.DestroyImmediate(root);
        if(errors.Count>0)throw new Exception(string.Join("\n",errors));
        EditorSceneManager.OpenScene("Assets/V3/Remielle_AnimationReview.unity");
        Debug.Log("MODEL_INTEGRITY_VERIFIED");
    }
    static bool Finite(Vector3 v)=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z);
}
