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

namespace Remielle.Controller.Editor
{
    public static class SourceWeaponFamiliesAudit
    {
        public static void Run()
        {
            var report=new JObject{["pass"]=false};var cases=new JArray();report["cases"]=cases;
            try
            {
                var source=new NativeControllerSource(File.ReadAllText("Assets/ControllerRuntime/Data/source-controller-pack.json"));
                var settings=new NativeControllerTimeSettings(File.ReadAllText("Assets/ControllerRuntime/Data/source-time-settings.json"));
                string directory=Path.Combine(Application.streamingAssetsPath,"RemielleControllerMotions");
                var avatars=new NativeControllerAvatarBindings(File.ReadAllText(Path.Combine(directory,"controller-avatar-bindings.json")),File.ReadAllText(Path.Combine(directory,"binding-profiles.json")));
                var material=SourceTrailBuild.Prepare();var emissionTexture=SourceWeaponMaterialBuild.Prepare();var outlineTexture=SourceWeaponMaterialBuild.PrepareSurface();SourceWeaponMaterialBuild.VerifyFamilies();
                var shader=AssetDatabase.LoadAssetAtPath<Shader>(SourceWeaponMaterialBuild.SurfaceShader);
                foreach(string side in new[]{"L","B"})foreach(int hz in new[]{30,60,120})
                {
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                    var actor=new GameObject("FamilyAuditActor");actor.AddComponent<CapsuleCollider>();actor.AddComponent<CapsuleColliderCast>();
                    var engine=actor.AddComponent<KCCMovementEngine>();actor.GetComponent<Rigidbody>().isKinematic=true;engine.Awake();var motor=actor.AddComponent<RemielleAuthoredMotor>();
                    var visual=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/V3/Remielle_V3_Animated.prefab"));visual.transform.SetParent(actor.transform,false);
                    var driver=visual.GetComponentInChildren<RemielleNativeAnimation>();var bindings=SourceVisibilityBuild.Prepare(visual);
                    string pack=File.ReadAllText("Assets/ControllerIntegration/Data/source-weapon-material-"+side+".json");
                    string trailPack=File.ReadAllText("Assets/ControllerIntegration/Data/source-weapon-trail-"+side+".json");
                    var camera=new GameObject("FamilyAuditCamera").AddComponent<Camera>();camera.fieldOfView=40;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.025f,.025f,.04f);
                    var light=new GameObject("FamilyAuditLight").AddComponent<Light>();light.type=LightType.Directional;light.transform.rotation=Quaternion.Euler(30,-20,0);RenderSettings.ambientLight=Color.gray;
                    int bestPixels=0,frames=0;byte[] bestImage=null;float peakVisibility=0;
                    using(var window=new SourceModifierWindow(pack,source))
                    using(var surface=new SourceWeaponSurface(pack,bindings,shader,outlineTexture))
                    using(var emission=side=="L"?new SourceWeaponEmission(pack,source,bindings,emissionTexture):null)
                    using(var trail=new SourceWeaponTrail(trailPack,source,driver,material))
                    using(var session=new SourceActionSession(source,"Avatar_Female_Size02_RemielleOrigin_Controller",directory,settings,avatars,driver,visual.transform,motor,side=="L"?42:52))
                    {
                        window.Changed+=surface.SetActive;
                        session.ObserveActions(n=>{window.Observe(n);emission?.Observe(n);trail.Observe(n);});
                        while(frames++<hz*12)
                        {
                            session.Tick(1f/hz);emission?.Advance(1f/hz);surface.Advance(1f/hz);trail.Advance(1f/hz);peakVisibility=Math.Max(peakVisibility,surface.Visibility);
                            if(trail.Mesh.vertices.Any(p=>!float.IsFinite(p.x)||!float.IsFinite(p.y)||!float.IsFinite(p.z)))throw new Exception("Nonfinite family trail geometry");
                            if(hz==60&&trail.Vertices>0&&frames%12==0&&session.CurrentState==(side=="L"?42:52))
                            {
                                camera.transform.position=actor.transform.position+new Vector3(0,1.5f,5);camera.transform.LookAt(actor.transform.position+Vector3.up*1.2f);
                                var on=Render(camera);trail.Renderer.enabled=false;var off=Render(camera);trail.Renderer.enabled=true;
                                int changed=on.GetPixels32().Zip(off.GetPixels32(),(a,b)=>Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b)).Count(d=>d>3);
                                if(changed>bestPixels){bestPixels=changed;bestImage=on.EncodeToPNG();}
                                UnityEngine.Object.DestroyImmediate(on);UnityEngine.Object.DestroyImmediate(off);
                            }
                            if(session.CurrentState==32&&!session.IsBlending&&window.ActiveOwners==0&&!surface.IsTransitioning&&trail.Vertices==0)break;
                        }
                        if(frames>=hz*12||trail.PeakVertices==0||peakVisibility<.999f||surface.Visibility!=0||window.ActiveOwners!=0||trail.ActiveOwners!=0||trail.Vertices!=0||window.Activations==0||window.Activations!=window.Releases)
                            throw new Exception("Family lifecycle failed "+side+" at "+hz+" Hz");
                        if(hz==60)
                        {
                            if(bestPixels<10)throw new Exception("Family trail not visible on GPU: "+side);
                            File.WriteAllBytes(QualifiedIntegrationAudit.Output+"/source-weapon-family-"+side+".png",bestImage);
                        }
                        cases.Add(new JObject{["family"]=side,["hz"]=hz,["ticks"]=frames,["activations"]=window.Activations,["releases"]=window.Releases,
                            ["trailActivations"]=trail.Activations,["trailReleases"]=trail.Releases,["peakVertices"]=trail.PeakVertices,["peakVisibility"]=peakVisibility,["finalVisibility"]=surface.Visibility,
                            ["emissionPulses"]=emission?.Pulses,["gpuChangedPixels"]=hz==60?bestPixels:(int?)null,["sourceStateSeed"]=side=="L"?42:52});
                        // Re-entry followed by an early interruption must not
                        // regain its window from outgoing blend samples.
                        int state=side=="L"?42:52;
                        foreach(var phase in new[]{SourceActionPhase.Enter,SourceActionPhase.Leave,SourceActionPhase.Sample,SourceActionPhase.Exit})
                        {
                            var n=new SourceActionNotice(phase,9000,state,8,160,false,"interrupt-audit");window.Observe(n);emission?.Observe(n);trail.Observe(n);
                        }
                        for(int i=0;i<hz;i++){emission?.Advance(1f/hz);surface.Advance(1f/hz);trail.Advance(1f/hz);}
                        if(window.ActiveOwners!=0||surface.Visibility!=0||trail.ActiveOwners!=0||trail.Vertices!=0)throw new Exception("Interrupted family leaked");
                    }
                }
                report["pass"]=true;report["nativeParticlePluginsImplemented"]=false;report["bigWeaponGameplayEntryWired"]=false;Debug.Log("REMIELLE_WEAPON_FAMILIES_PASS");
            }
            catch(Exception e){report["error"]=e.ToString();Debug.LogException(e);}
            finally{File.WriteAllText(QualifiedIntegrationAudit.Output+"/source-weapon-families-verification.json",report.ToString());}
            if(!(bool)report["pass"])EditorApplication.Exit(1);
        }
        static Texture2D Render(Camera camera)
        {
            var target=RenderTexture.GetTemporary(1280,720,24,RenderTextureFormat.ARGB32);var old=camera.targetTexture;var active=RenderTexture.active;
            try{camera.targetTexture=target;camera.Render();RenderTexture.active=target;var image=new Texture2D(1280,720,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();return image;}
            finally{camera.targetTexture=old;RenderTexture.active=active;RenderTexture.ReleaseTemporary(target);}
        }
    }
}
