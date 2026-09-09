using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using nickmaltbie.OpenKCC.Character;
using nickmaltbie.OpenKCC.Utils.ColliderCast;

namespace Remielle.Controller.Editor
{
    public static class AuthoredRootAudit
    {
        public static void VerifyTransport()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/V3/Remielle_V3_Animated.prefab");
            var checks=new JArray();float maxError=0;
            foreach(bool collision in new[]{false,true})
            {
                var actor=new GameObject("RootTransportActor");
                actor.AddComponent<CapsuleCollider>();actor.AddComponent<CapsuleColliderCast>();
                var engine=actor.AddComponent<KCCMovementEngine>();actor.GetComponent<Rigidbody>().isKinematic=true;engine.Awake();
                var motor=actor.AddComponent<RemielleAuthoredMotor>();
                var visual=(GameObject)PrefabUtility.InstantiatePrefab(prefab);visual.transform.SetParent(actor.transform,false);
                var reference=(GameObject)PrefabUtility.InstantiatePrefab(prefab);
                var a=visual.GetComponentInChildren<RemielleNativeAnimation>();
                var b=reference.GetComponentInChildren<RemielleNativeAnimation>();
                GameObject wall=null;
                if(collision){wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.transform.position=new Vector3(0,0,1.5f);wall.transform.localScale=new Vector3(10,10,.2f);}
                Physics.SyncTransforms();
                using(var transport=new SingleClipRootTransport(a,visual.transform,motor,"Walk_Loop",1))
                {
                    for(int i=1;i<=29;i++)
                    {
                        float t=i/50f; b.Sample("Walk_Loop",t);transport.Sample(t);
                        Vector3 blocked=transport.AuthoredDisplacement-actor.transform.position;
                        for(int j=0;j<a.bones.Length;j++)
                        {
                            float error=Vector3.Distance(a.bones[j].target.position,b.bones[j].target.position-blocked);
                            maxError=Mathf.Max(maxError,error);
                            if(error>.0001f)throw new Exception("Double root application or relative bone drift: "+a.bones[j].target.name+" "+error);
                        }
                    }
                    if(collision && !(actor.transform.position.z>.2f && actor.transform.position.z<1.1f))throw new Exception("Root transport collision failed");
                    if(!collision && Vector3.Distance(actor.transform.position,transport.AuthoredDisplacement)>.0001f)throw new Exception("Authored travel mismatch");
                    checks.Add(new JObject{["collision"]=collision,["samples"]=29,["actorZ"]=actor.transform.position.z,["authoredZ"]=transport.AuthoredDisplacement.z,["bonesPerSample"]=a.bones.Length});
                    bool rejected=false;try{transport.Sample(.1f);}catch(ArgumentOutOfRangeException){rejected=true;}
                    if(!rejected)throw new Exception("Backward time was accepted");
                }
                UnityEngine.Object.DestroyImmediate(visual);UnityEngine.Object.DestroyImmediate(reference);UnityEngine.Object.DestroyImmediate(actor);
                if(wall)UnityEngine.Object.DestroyImmediate(wall);
            }
            File.WriteAllText(QualifiedIntegrationAudit.Output+"/authored-root-transport-verification.json",new JObject{
                ["pass"]=true,["checks"]=checks,["maxBoneWorldPositionError"]=maxError,
                ["scope"]="Original Walk_Loop, unchanged bone mapping, actual KCC; editor samples, not live input or GPU audit",
                ["loopsAndBlendsQualified"]=false,["sourceAssetsRewritten"]=false}.ToString());
            Debug.Log("REMIELLE_ROOT_TRANSPORT_PASS");
        }
        public static void Run()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/V3/Remielle_V3_Animated.prefab"));
            try
            {
                var driver = go.GetComponentInChildren<RemielleNativeAnimation>();
                var root = driver.bones.Single(x => x.source.name == "Root");
                var pelvis = driver.bones.Single(x => x.source.name == "Bip001");
                var rows = new JArray();
                foreach (string clip in new[] { "Walk_Loop", "Run_Loop_01", "Run_Loop_02" })
                {
                    if (driver.nativeAnimation[clip] == null) continue;
                    driver.Sample(clip, 0);
                    Vector3 origin = root.source.localPosition;
                    Vector3 pelvisOrigin = pelvis.target.position;
                    var mapping = root.target.parent.localToWorldMatrix * root.parentBasisInverse;
                    float duration = driver.nativeAnimation[clip].length;
                    for (int i = 1; i <= 4; i++)
                    {
                        float time = duration * i / 5f;
                        driver.Sample(clip, time);
                        Vector3 rootDelta = mapping.MultiplyVector(root.source.localPosition - origin);
                        Vector3 pelvisDelta = pelvis.target.position - pelvisOrigin;
                        rows.Add(new JObject { ["clip"] = clip, ["time"] = time,
                            ["rootWorldDelta"] = Vec(rootDelta), ["pelvisWorldDelta"] = Vec(pelvisDelta),
                            ["pelvisResidual"] = Vec(pelvisDelta-rootDelta) });
                    }
                }
                File.WriteAllText(QualifiedIntegrationAudit.Output+"/authored-root-observations.json", new JObject {
                    ["rootSourcePath"] = AnimationUtility.CalculateTransformPath(root.source,driver.nativeAnimation.transform),
                    ["rootTargetPath"] = AnimationUtility.CalculateTransformPath(root.target,go.transform),
                    ["pelvisSourcePath"] = AnimationUtility.CalculateTransformPath(pelvis.source,driver.nativeAnimation.transform),
                    ["samples"] = rows, ["scope"] = "Read-only cloned prefab pose observations; no controller root policy inferred" }.ToString());
                Debug.Log("REMIELLE_ROOT_OBSERVATIONS_SAVED");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
        static JArray Vec(Vector3 v) => new JArray(v.x,v.y,v.z);
    }
}
