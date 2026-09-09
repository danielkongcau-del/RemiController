using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

public static class NativeAnimationBuild
{
    const string Out="E:/ZZZ/ZCode/90_Builds/RuntimeRepair/20260904";
    const string Clips="Assets/V3/Animations";
    public static void Run()
    {
        Build(true);
    }
    public static void RebuildRig()
    {
        Build(false);
    }
    static void Build(bool rebuildClips)
    {
        RemielleImportSettings.ApplyAnimationSettings();
        Directory.CreateDirectory(Clips);
        var input=JObject.Parse(File.ReadAllText("Assets/SourceAssets/AnimationInputs/animation_inputs.json"));
        // Validate every declared input before clearing any existing clip curves.
        foreach(var row in input["clips"])
            foreach(var pair in new[]{("packed","packedSha256"),("source","sourceSha256"),("defaultSource","defaultSha256")})
                CheckHash((string)row[pair.Item1],(string)row[pair.Item2]);
        var clips=new Dictionary<string,AnimationClip>();
        foreach(var row in input["clips"])
        {
            string name=(string)row["name"],asset=Clips+"/"+name+".anim";
            var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(asset);
            if(!rebuildClips)
            {
                if(clip==null||Mathf.Abs(clip.length-(float)row["duration"])>0.00001f)
                    throw new InvalidDataException("Missing or changed native clip: "+name);
                clips.Add(name,clip);continue;
            }
            if(clip==null){clip=new AnimationClip{name=name,legacy=true};AssetDatabase.CreateAsset(clip,asset);}
            clip.ClearCurves();clip.legacy=true;clip.frameRate=(float)row["sampleRate"];clip.wrapMode=(bool)row["loop"]?WrapMode.Loop:WrapMode.ClampForever;
            var packed=File.ReadAllBytes((string)row["packed"]);int headerLength=BitConverter.ToInt32(packed,4),start=8+headerLength;
            if(System.Text.Encoding.ASCII.GetString(packed,0,4)!="RANI")throw new InvalidDataException("packed animation signature");
            var header=JObject.Parse(System.Text.Encoding.UTF8.GetString(packed,8,headerLength));
            var bindings=new List<EditorCurveBinding>();var curves=new List<AnimationCurve>();
            foreach(var track in header["tracks"])
            {
                int width=(int)track["width"],count=(int)track["keyCount"],offset=start+(int)track["offset"];
                float[,] rotations=null;
                if((string)track["property"]=="m_LocalRotation")
                {
                    rotations=new float[count,4];
                    for(int i=0;i<count;i++)
                    {
                        int k=offset+i*(1+width*3)*4;
                        for(int c=0;c<4;c++)rotations[i,c]=BitConverter.ToSingle(packed,k+4*(1+c));
                        float dot=0;if(i>0)for(int c=0;c<4;c++)dot+=rotations[i-1,c]*rotations[i,c];
                        // q and -q encode exactly the same authored orientation.
                        // Align their signs BEFORE computing interpolation slopes.
                        if(i>0&&dot<0)for(int c=0;c<4;c++)rotations[i,c]=-rotations[i,c];
                    }
                }
                for(int c=0;c<width;++c)
                {
                    var keys=new List<Keyframe>();float duration=(float)row["duration"];
                    for(int i=0;i<count;++i)
                    {
                        int k=offset+i*(1+width*3)*4;
                        float time=BitConverter.ToSingle(packed,k);
                        // ACL includes a guard sample beyond the declared stop.
                        // Retain that sample in the packed archive, but do not
                        // lengthen the playable clip or its authored loop.
                        if(time>duration+0.000001f)continue;
                        float value=BitConverter.ToSingle(packed,k+4*(1+c));
                        float incoming=BitConverter.ToSingle(packed,k+4*(1+width+c)),outgoing=BitConverter.ToSingle(packed,k+4*(1+2*width+c));
                        if(rotations!=null)
                        {
                            value=rotations[i,c];incoming=outgoing=0;
                            if(i>0)incoming=(value-rotations[i-1,c])/(time-BitConverter.ToSingle(packed,k-(1+width*3)*4));
                            if(i+1<count)outgoing=(rotations[i+1,c]-value)/(BitConverter.ToSingle(packed,k+(1+width*3)*4)-time);
                        }
                        keys.Add(new Keyframe(Mathf.Min(time,duration),value,incoming,outgoing));
                    }
                    bool morph=(string)track["kind"]=="SkinnedMeshRenderer";
                    bindings.Add(EditorCurveBinding.FloatCurve((string)track["path"],morph?typeof(SkinnedMeshRenderer):typeof(Transform),(string)track["property"]+(morph?"":"."+"xyzw"[c])));curves.Add(new AnimationCurve(keys.ToArray()));
                }
            }
            AnimationUtility.SetEditorCurves(clip,bindings.ToArray(),curves.ToArray());
            clip.EnsureQuaternionContinuity();
            var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.startTime=0;settings.stopTime=(float)row["duration"];settings.loopTime=(bool)row["loop"];AnimationUtility.SetAnimationClipSettings(clip,settings);
            if(Mathf.Abs(clip.length-(float)row["duration"])>0.00001f)throw new InvalidDataException("Native clip duration changed: "+name);
            EditorUtility.SetDirty(clip);clips.Add(name,clip);Debug.Log("Native clip built: "+name);
        }
        AssetDatabase.SaveAssets();
        var root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/V2b/Remielle_V2b_Materials.prefab"));root.name="Remielle_V3_Animated";
        var targets=root.GetComponentsInChildren<Transform>(true).ToDictionary(t=>t.name);
        var audit=JObject.Parse(File.ReadAllText(Out+"/native-rig-bind-audit.json"));var nodes=audit["nodes"].ToDictionary(n=>(long)n["pathID"]);var sources=new Dictionary<long,Transform>();
        var container=new GameObject("NativePoseDriver");container.transform.SetParent(root.transform,false);
        // The assembled prefab retains a Y180 wrapper. Driver poses live in
        // the original upright world frame, so cancel that wrapper locally.
        container.transform.rotation=Quaternion.identity;
        Transform Make(long id)
        {
            if(sources.TryGetValue(id,out var existing))return existing;
            var row=nodes[id];var raw=JObject.Parse(File.ReadAllText((string)row["source"]));long pid=(long)row["parent"];
            var go=new GameObject((string)row["name"]);var parent=nodes.ContainsKey(pid)?Make(pid):container.transform;go.transform.SetParent(parent,false);
            go.transform.localPosition=Vec(raw["m_LocalPosition"]);go.transform.localScale=Vec(raw["m_LocalScale"]);
            var q=raw["m_LocalRotation"];go.transform.localRotation=new Quaternion((float)q["X"],(float)q["Y"],(float)q["Z"],(float)q["W"]);
            sources[id]=go.transform;return go.transform;
        }
        foreach(long id in nodes.Keys)Make(id);
        var sourceRest=sources.Values.Select(t=>(node:t,position:t.localPosition,rotation:t.localRotation,scale:t.localScale)).ToArray();
        var driverRoot=sources.Single(x=>!nodes.ContainsKey((long)nodes[x.Key]["parent"])).Value;
        var morphLinks=new List<RemielleNativeAnimation.MorphLink>();
        foreach(string name in new[]{"Remielle_Face","Remielle_Eyebrow"})
        {
            var target=targets["SMR_"+name].GetComponent<SkinnedMeshRenderer>();
            if(target==null||target.sharedMesh.blendShapeCount==0)throw new InvalidDataException("Missing native morph mesh: "+name);
            var driver=driverRoot.Find(name);
            if(driver==null)throw new InvalidDataException("Missing native morph path: "+name);
            var smr=driver.gameObject.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh=target.sharedMesh;smr.bones=target.bones;smr.rootBone=target.rootBone;smr.enabled=false;
            // Disabled renderers still participate in editor selection bounds.
            // A skinned mesh must retain one valid bone per bindpose.
            if(smr.bones.Length!=smr.sharedMesh.bindposes.Length||smr.bones.Any(b=>b==null))
                throw new InvalidDataException("Invalid expression driver skin: "+name);
            morphLinks.Add(new RemielleNativeAnimation.MorphLink{source=smr,target=target});
        }
        var animation=driverRoot.gameObject.AddComponent<Animation>();animation.playAutomatically=false;
        foreach(var pair in clips)animation.AddClip(pair.Value,pair.Key);
        var bridge=root.AddComponent<RemielleNativeAnimation>();bridge.nativeAnimation=animation;
        bridge.morphs=morphLinks.ToArray();
        var bases=sources.ToDictionary(x=>x.Key,x=>x.Value.worldToLocalMatrix*targets[x.Value.name].localToWorldMatrix);
        var links=new List<RemielleNativeAnimation.BoneLink>();
        foreach(var row in audit["nodes"].OrderBy(n=>Depth(sources[(long)n["pathID"]])))
        {
            long id=(long)row["pathID"],pid=(long)row["parent"];var target=targets[(string)row["name"]];
            // Boundary basis handles the assembled presentation/skeleton roots.
            Matrix4x4 parentBasis=nodes.ContainsKey(pid)?bases[pid]:container.transform.worldToLocalMatrix*target.parent.localToWorldMatrix;
            links.Add(new RemielleNativeAnimation.BoneLink{source=sources[id],target=target,basis=bases[id],parentBasisInverse=parentBasis.inverse,
                sourceRestPosition=sources[id].localPosition,sourceRestRotation=sources[id].localRotation,sourceRestScale=sources[id].localScale});
        }
        bridge.bones=links.ToArray();
        var profile=root.AddComponent<RemielleRuntimeProfile>();profile.characterMenu=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/SourceAssets/Runtime/RuntimeCharacterMenu.asset");profile.characterBattle=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/SourceAssets/Runtime/RuntimeCharacterBattle.asset");
        var rest=links.ToDictionary(x=>x.target,x=>x.target.localToWorldMatrix);bridge.ApplyPose();float restError=0;
        foreach(var link in links)restError=Mathf.Max(restError,Error(rest[link.target],link.target.localToWorldMatrix));
        if(restError>1e-5)throw new InvalidDataException("Native bridge rest error: "+restError);
        var results=new JArray();
        foreach(var pair in clips)
        {
            int missing=AnimationUtility.GetCurveBindings(pair.Value).Count(b=>!string.IsNullOrEmpty(b.path)&&driverRoot.Find(b.path)==null);
            if(missing!=0)throw new InvalidDataException("Unbound animation transforms: "+pair.Key);
            float poseError=0,morphError=0;var times=new[]{0f,pair.Value.length*0.25f,pair.Value.length*0.5f,pair.Value.length*0.75f,pair.Value.length};
            var morphCurves=AnimationUtility.GetCurveBindings(pair.Value).Where(b=>b.type==typeof(SkinnedMeshRenderer)).ToArray();
            if(morphCurves.Length!=44)throw new InvalidDataException("Expected all 44 facial tracks: "+pair.Key);
            foreach(float time in times)
            {
                bridge.Sample(pair.Key,time);
                foreach(var link in links)
                {
                    var expected=link.source.localToWorldMatrix*link.basis;
                    poseError=Mathf.Max(poseError,Error(expected,link.target.localToWorldMatrix));
                }
                foreach(var binding in morphCurves)
                {
                    var link=morphLinks.Single(m=>m.source.name==binding.path);
                    int index=link.target.sharedMesh.GetBlendShapeIndex(binding.propertyName.Substring("blendShape.".Length));
                    if(index<0)throw new InvalidDataException("Unbound native morph "+binding.propertyName);
                    float expected=AnimationUtility.GetEditorCurve(pair.Value,binding).Evaluate(time);
                    morphError=Mathf.Max(morphError,Mathf.Abs(expected-link.target.GetBlendShapeWeight(index)));
                }
            }
            results.Add(new JObject{["clip"]=pair.Key,["duration"]=pair.Value.length,["unboundPaths"]=missing,["sampledPoses"]=times.Length,["worldPoseMaxError"]=poseError,["blendShapeTracks"]=morphCurves.Length,["blendShapeMaxError"]=morphError,["allQualityTiersLoaded"]=true});
            if(float.IsNaN(morphError)||morphError>0.001f)throw new InvalidDataException("Native expression mismatch: "+results.ToString());
            if(float.IsNaN(poseError)||poseError>0.0001f)throw new InvalidDataException("Native animation pose mismatch: "+results.ToString());
        }
        foreach(var pose in sourceRest){pose.node.localPosition=pose.position;pose.node.localRotation=pose.rotation;pose.node.localScale=pose.scale;}
        foreach(var link in morphLinks)for(int i=0;i<link.source.sharedMesh.blendShapeCount;i++)link.source.SetBlendShapeWeight(i,0);
        bridge.ApplyPose();
        // Publish only a validated, restored rest pose, never the last audit pose.
        PrefabUtility.SaveAsPrefabAsset(root,"Assets/V3/Remielle_V3_Animated.prefab");
        File.WriteAllText(Out+"/native-animation-verification.json",new JObject{["pass"]=true,["restMaxError"]=restError,["bones"]=links.Count,["clips"]=results}.ToString());
        Object.DestroyImmediate(root);AssetDatabase.SaveAssets();Debug.Log("NATIVE_ANIMATION_VERIFIED");
    }

    static Vector3 Vec(JToken v)=>new Vector3((float)v["X"],(float)v["Y"],(float)v["Z"]);
    static void CheckHash(string path,string expected)
    {
        using var stream=File.OpenRead(path);using var sha=SHA256.Create();
        string actual=BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
        if(string.IsNullOrEmpty(expected)||actual!=expected)throw new InvalidDataException("Native animation input changed: "+path);
    }
    static int Depth(Transform t){int n=0;while(t.parent!=null){++n;t=t.parent;}return n;}
    static float Error(Matrix4x4 a,Matrix4x4 b){float e=0;for(int i=0;i<16;++i)e=Mathf.Max(e,Mathf.Abs(a[i]-b[i]));return e;}
}
