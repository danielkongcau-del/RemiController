using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Remielle.ControllerRuntime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using nickmaltbie.OpenKCC.Character;
using nickmaltbie.OpenKCC.Utils.ColliderCast;
using Object=UnityEngine.Object;

namespace Remielle.Controller.Editor
{
    public static class SourceWeaponEmissionAudit
    {
        static bool presentationReview;
        public static void RunPresentationReview(){presentationReview=true;Run();}
        public static void Run()
        {
            var report=new JObject{["pass"]=false};var cases=new JArray();report["cases"]=cases;
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                var source=new NativeControllerSource(File.ReadAllText("Assets/ControllerRuntime/Data/source-controller-pack.json"));
                var settings=new NativeControllerTimeSettings(File.ReadAllText("Assets/ControllerRuntime/Data/source-time-settings.json"));
                string directory=Path.Combine(Application.streamingAssetsPath,"RemielleControllerMotions");
                var avatars=new NativeControllerAvatarBindings(File.ReadAllText(Path.Combine(directory,"controller-avatar-bindings.json")),File.ReadAllText(Path.Combine(directory,"binding-profiles.json")));
                SourceTrailBuild.Prepare();var texture=SourceWeaponMaterialBuild.Prepare();string pack=File.ReadAllText(SourceWeaponMaterialBuild.Pack);
                var data=JObject.Parse(pack);var rawCurves=data["configurations"][(string)data["emissionKey"]]["curveGroup"]["curveDict"];
                double maxCurveError=0;
                foreach(var channel in new[]{"r","g","b","a"})
                {
                    var raw=rawCurves["_SecondaryEmissionColor:value:"+channel];var curve=SourceWeaponEmission.ReadCurve(raw);var keys=raw[0][0];
                    for(int i=0;i<keys.Count()-1;i++)
                    {
                        var a=keys[i];var b=keys[i+1];if((int)a["m_WeightedMode"]!=0||(int)b["m_WeightedMode"]!=0)throw new Exception("Golden Hermite oracle only covers these unweighted source curves");
                        double dt=(double)b["m_Time"]-(double)a["m_Time"];
                        foreach(double t in new[]{0.0,.25,.5,.75,1.0})
                        {
                            double t2=t*t,t3=t2*t;
                            double expected=(2*t3-3*t2+1)*(double)a["m_Value"]+(t3-2*t2+t)*dt*(double)a["m_OutTangent"]+
                                (-2*t3+3*t2)*(double)b["m_Value"]+(t3-t2)*dt*(double)b["m_InTangent"];
                            maxCurveError=Math.Max(maxCurveError,Math.Abs(expected-curve.Evaluate((float)((double)a["m_Time"]+dt*t))));
                        }
                    }
                }
                if(maxCurveError>1e-5)throw new Exception("Source material curve differs from independent Hermite evaluation");
                report["maximumCurveError"]=maxCurveError;
                int bestPixels=0;byte[] bestImage=null;
                foreach(int hz in new[]{30,60,120})
                {
                    var actor=new GameObject("EmissionAuditActor");actor.AddComponent<CapsuleCollider>();actor.AddComponent<CapsuleColliderCast>();
                    var engine=actor.AddComponent<KCCMovementEngine>();actor.GetComponent<Rigidbody>().isKinematic=true;engine.Awake();var motor=actor.AddComponent<RemielleAuthoredMotor>();
                    var visual=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/V3/Remielle_V3_Animated.prefab"));visual.transform.SetParent(actor.transform,false);
                    var driver=visual.GetComponentInChildren<RemielleNativeAnimation>();var bindings=SourceVisibilityBuild.Prepare(visual);
                    var targets=bindings.Where(b=>b.renderer.name=="SMR_Remielle_Weapon_02_R"||b.renderer.name=="SMR_Remielle_Weapon_05_R").Select(b=>b.renderer).ToArray();
                    if(targets.Length!=2||targets.Any(r=>r.sharedMaterials.Length!=1))throw new Exception("GPU comparison expects the two qualified single-slot weapons");
                    var baseline=targets.Select(r=>r.sharedMaterials).ToArray();
                    var sentinel=new MaterialPropertyBlock();sentinel.SetFloat("_EmissionAuditSentinel",.375f);
                    targets[0].SetPropertyBlock(sentinel,0);
                    var camera=new GameObject("EmissionAuditCamera").AddComponent<Camera>();camera.fieldOfView=40;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.03f,.03f,.05f);
                    var light=new GameObject("EmissionAuditLight").AddComponent<Light>();light.type=LightType.Directional;light.transform.rotation=Quaternion.Euler(30,-20,0);RenderSettings.ambientLight=Color.gray;
                    using(var emission=new SourceWeaponEmission(pack,source,bindings,texture))
                    using(var session=new SourceActionSession(source,"Avatar_Female_Size02_RemielleOrigin_Controller",directory,settings,avatars,driver,visual.transform,motor))
                    {
                        session.ObserveActions(emission.Observe);float delta=1f/hz;session.Parameters.SetTrigger(session.Parameters.Hash("Trigger_PressAttackA"));
                        bool sawEnd=false;int ticks=0;
                        while(ticks++<hz*10)
                        {
                            session.Tick(delta);emission.Advance(delta);sawEnd|=session.CurrentState==40;
                            if(hz==60&&emission.CurrentColor.z>.25f)
                            {
                                var bounds=targets[0].bounds;bounds.Encapsulate(targets[1].bounds);
                                camera.transform.position=bounds.center+Vector3.forward*Math.Max(1.2f,bounds.extents.magnitude*3);camera.transform.LookAt(bounds.center);
                                var on=Render(camera,visual);var blocks=targets.Select(r=>{var b=new MaterialPropertyBlock();r.GetPropertyBlock(b,0);r.SetPropertyBlock(null,0);return b;}).ToArray();
                                var off=Render(camera,visual);for(int i=0;i<targets.Length;i++)targets[i].SetPropertyBlock(blocks[i],0);
                                int pixels=on.GetPixels32().Zip(off.GetPixels32(),(a,b)=>Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b)).Count(d=>d>3);
                                if(pixels>bestPixels){bestPixels=pixels;bestImage=on.EncodeToPNG();if(presentationReview)ReviewFace(camera,visual,session,driver,on);}
                                UnityEngine.Object.DestroyImmediate(on);UnityEngine.Object.DestroyImmediate(off);
                            }
                            if(sawEnd&&session.CurrentState==32&&!session.IsBlending&&!emission.IsPulsing)break;
                        }
                        if(ticks>=hz*10||emission.Activations!=1||emission.Releases!=1||emission.Pulses!=2||emission.ActiveOwners!=0||emission.IsPulsing||emission.PeakColor<.25f)
                            throw new Exception("Emission lifecycle failed at "+hz+" Hz");
                        cases.Add(new JObject{["hz"]=hz,["ticks"]=ticks,["activations"]=emission.Activations,["releases"]=emission.Releases,["pulses"]=emission.Pulses,["peakColor"]=emission.PeakColor,["ownersAtEnd"]=emission.ActiveOwners});
                        emission.Observe(new SourceActionNotice(SourceActionPhase.Enter,10000,46,0,130,false,"audit"));emission.Advance(delta);emission.Advance(delta);
                        var before=emission.CurrentColor;float age=emission.Age;emission.Advance(0);
                        if(emission.CurrentColor!=before||emission.Age!=age)throw new Exception("Pause advanced material pulse");
                        emission.Observe(new SourceActionNotice(SourceActionPhase.Leave,10000,46,2,130,false,"interrupted"));
                        emission.Observe(new SourceActionNotice(SourceActionPhase.Sample,10000,46,3,130,false,"outgoing-blend"));
                        if(emission.ActiveOwners!=0)throw new Exception("Outgoing source reactivated emission modifier");
                        emission.Observe(new SourceActionNotice(SourceActionPhase.Exit,10000,46,3,130,false,"interrupted"));
                        for(int i=0;i<hz;i++)emission.Advance(delta);
                        emission.Observe(new SourceActionNotice(SourceActionPhase.Enter,10001,46,0,130,false,"reentry"));emission.Advance(delta);emission.Advance(delta);
                        // Dispose while an actual property block is active.
                    }
                    var restored=new MaterialPropertyBlock();targets[0].GetPropertyBlock(restored,0);
                    if(restored.GetFloat("_EmissionAuditSentinel")!=.375f||restored.HasProperty(Shader.PropertyToID("_SecondaryEmission")))throw new Exception("Existing per-slot block was not restored");
                    targets[1].GetPropertyBlock(restored,0);if(!restored.isEmpty)throw new Exception("Empty per-slot baseline not restored");
                    for(int i=0;i<targets.Length;i++)if(!targets[i].sharedMaterials.SequenceEqual(baseline[i]))throw new Exception("Shared materials changed");
                    UnityEngine.Object.DestroyImmediate(camera.gameObject);UnityEngine.Object.DestroyImmediate(light.gameObject);UnityEngine.Object.DestroyImmediate(actor);
                }
                if(bestPixels<10)throw new Exception("No measurable emission GPU change");
                File.WriteAllBytes(QualifiedIntegrationAudit.Output+(presentationReview?"/presentation-issues/source-weapon-emission-reproduced.png":"/source-weapon-emission-preview.png"),bestImage);
                report["pass"]=true;report["gpuChangedPixels"]=bestPixels;report["nativeMaterialSchedulerParity"]=false;report["ditherOutlineImplemented"]=false;
                Debug.Log("REMIELLE_SOURCE_WEAPON_EMISSION_PASS "+bestPixels);
            }
            catch(Exception ex){report["error"]=ex.ToString();Debug.LogException(ex);}
            finally{File.WriteAllText(QualifiedIntegrationAudit.Output+(presentationReview?"/presentation-issues/emission-reproduction.json":"/source-weapon-emission-verification.json"),report.ToString());}
            if(!(bool)report["pass"])EditorApplication.Exit(1);
        }
        static void ReviewFace(Camera camera,GameObject visual,SourceActionSession session,RemielleNativeAnimation driver,Texture2D original)
        {
            string folder=QualifiedIntegrationAudit.Output+"/presentation-issues/";Directory.CreateDirectory(folder);
            var all=visual.GetComponentsInChildren<SkinnedMeshRenderer>(true);var brow=all.Single(r=>r.name=="SMR_Remielle_Eyebrow"&&r.enabled);
            var face=all.Single(r=>r.name=="SMR_Remielle_Face"&&r.enabled);var pos=camera.transform.position;var rot=camera.transform.rotation;
            void Save(string name){var t=Render(camera,visual);File.WriteAllBytes(folder+name,t.EncodeToPNG());Object.DestroyImmediate(t);}
            brow.enabled=false;var off=Render(camera,visual);brow.enabled=true;
            int changed=original.GetPixels32().Zip(off.GetPixels32(),(a,b)=>a.Equals(b)?0:1).Sum();Object.DestroyImmediate(off);
            camera.transform.position=face.bounds.center+Vector3.forward*.55f;camera.transform.LookAt(face.bounds.center);Save("emission-face.png");
            var hairs=all.Where(r=>r.enabled&&r.name.Contains("Hair")).ToArray();foreach(var r in hairs)r.enabled=false;Save("emission-face-no-hair.png");foreach(var r in hairs)r.enabled=true;
            camera.transform.SetPositionAndRotation(pos,rot);
            File.WriteAllText(folder+"emission-pose.json",new JObject{["state"]=session.CurrentState,["frame"]=session.ActionFrames,["browPixelDifference"]=changed,
                ["morphs"]=new JArray(driver.morphs.SelectMany(m=>Enumerable.Range(0,m.target.sharedMesh.blendShapeCount).Where(i=>Mathf.Abs(m.target.GetBlendShapeWeight(i))>.001f).Select(i=>new JObject{["renderer"]=m.target.name,["shape"]=m.target.sharedMesh.GetBlendShapeName(i),["weight"]=m.target.GetBlendShapeWeight(i)})))}.ToString());
        }
        static Texture2D Render(Camera camera,GameObject visual)
        {
            using var snapshot=new RemiellePoseSnapshot(visual);
            var target=RenderTexture.GetTemporary(1280,720,24,RenderTextureFormat.ARGB32);var old=camera.targetTexture;var active=RenderTexture.active;
            try{camera.targetTexture=target;camera.Render();RenderTexture.active=target;var image=new Texture2D(1280,720,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();return image;}
            finally{camera.targetTexture=old;RenderTexture.active=active;RenderTexture.ReleaseTemporary(target);}
        }
    }
}
