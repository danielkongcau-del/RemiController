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
    public static class SourceTrailAudit
    {
        const string Main="Avatar_Female_Size02_RemielleOrigin_Controller";
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
                var template=SourceTrailBuild.Prepare();string pack=File.ReadAllText(SourceTrailBuild.Pack);
                int bestPixels=0;byte[] bestImage=null;
                foreach(int hz in new[]{30,60,120})
                {
                    var actor=new GameObject("TrailAuditActor");actor.AddComponent<CapsuleCollider>();actor.AddComponent<CapsuleColliderCast>();
                    var engine=actor.AddComponent<KCCMovementEngine>();actor.GetComponent<Rigidbody>().isKinematic=true;engine.Awake();
                    var motor=actor.AddComponent<RemielleAuthoredMotor>();
                    var visual=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/V3/Remielle_V3_Animated.prefab"));visual.transform.SetParent(actor.transform,false);
                    var driver=visual.GetComponentInChildren<RemielleNativeAnimation>();
                    var camera=new GameObject("TrailAuditCamera").AddComponent<Camera>();camera.fieldOfView=40;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.03f,.03f,.05f);
                    var light=new GameObject("TrailAuditLight").AddComponent<Light>();light.type=LightType.Directional;light.transform.rotation=Quaternion.Euler(30,-20,0);RenderSettings.ambientLight=Color.gray;
                    using(var trail=new SourceWeaponTrail(pack,source,driver,template))
                    using(var session=new SourceActionSession(source,Main,directory,settings,avatars,driver,visual.transform,motor))
                    {
                        session.ObserveActions(trail.Observe);var parameters=session.Parameters;float delta=1f/hz;
                        void Step(){session.Tick(delta);trail.Advance(delta);}
                        parameters.SetTrigger(parameters.Hash("Trigger_PressAttackA"));
                        int frames=0,peak=0;bool sawWindow=false,sawEnd=false;int captures=0;
                        while(frames++<hz*10)
                        {
                            Step();sawWindow|=trail.ActiveOwners>0;peak=Math.Max(peak,trail.Vertices);sawEnd|=session.CurrentState==40;
                            if(trail.Mesh.vertices.Any(p=>!float.IsFinite(p.x)||!float.IsFinite(p.y)||!float.IsFinite(p.z)))throw new Exception("Non-finite trail mesh");
                            if(hz==60&&trail.Vertices>0&&session.CurrentState==46&&captures<4&&session.ActionFrames>=13+captures*12)
                            {
                                camera.transform.position=actor.transform.position+new Vector3(0,1.5f,5);camera.transform.LookAt(actor.transform.position+Vector3.up*1.2f);
                                var on=Render(camera);trail.Renderer.enabled=false;var off=Render(camera);trail.Renderer.enabled=true;
                                int changed=on.GetPixels32().Zip(off.GetPixels32(),(a,b)=>Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b)).Count(d=>d>3);
                                if(changed>bestPixels){bestPixels=changed;bestImage=on.EncodeToPNG();}
                                UnityEngine.Object.DestroyImmediate(on);UnityEngine.Object.DestroyImmediate(off);captures++;
                            }
                            if(sawEnd&&session.CurrentState==32&&!session.IsBlending&&trail.Vertices==0)break;
                        }
                        if(!sawWindow||!sawEnd||peak==0||trail.ActiveOwners!=0||trail.Vertices!=0||frames>=hz*10||trail.Activations!=1||trail.Releases!=1)throw new Exception("Attack trail lifecycle failed at "+hz+" Hz");
                        cases.Add(new JObject{["hz"]=hz,["ticks"]=frames,["peakVertices"]=peak,["activations"]=trail.Activations,["releases"]=trail.Releases,["remainingOwners"]=trail.ActiveOwners,["remainingVertices"]=trail.Vertices});
                        // A leaving source is still sampled during blends. It
                        // must not regain its modifier because its frame fits.
                        trail.Observe(new SourceActionNotice(SourceActionPhase.Enter,10000,46,6,130,false,"audit"));trail.Advance(delta);
                        trail.Observe(new SourceActionNotice(SourceActionPhase.Leave,10000,46,7,130,false,"interrupted"));
                        trail.Observe(new SourceActionNotice(SourceActionPhase.Sample,10000,46,8,130,false,"outgoing-blend"));
                        if(trail.ActiveOwners!=0)throw new Exception("Leaving blend source reactivated trail");
                        trail.Observe(new SourceActionNotice(SourceActionPhase.Exit,10000,46,8,130,false,"interrupted"));
                        for(int i=0;i<hz;i++)trail.Advance(delta);
                        if(trail.Vertices!=0)throw new Exception("Interrupted trail did not expire");
                        trail.Observe(new SourceActionNotice(SourceActionPhase.Enter,10001,46,6,130,false,"reentry"));trail.Advance(delta);
                        int vertices=trail.Vertices;var before=trail.StartPoint.position;trail.Advance(0);
                        if(trail.Vertices!=vertices||trail.StartPoint.position!=before)throw new Exception("Pause changed trail");
                        trail.Observe(new SourceActionNotice(SourceActionPhase.Exit,10001,46,6,130,false,"disposed"));
                    }
                    if(GameObject.Find("Remielle_Source_Common24_Trail"))throw new Exception("Trail object leaked after dispose");
                    UnityEngine.Object.DestroyImmediate(camera.gameObject);UnityEngine.Object.DestroyImmediate(light.gameObject);UnityEngine.Object.DestroyImmediate(visual);UnityEngine.Object.DestroyImmediate(actor);
                }
                if(bestPixels<10)throw new Exception("Trail geometry produced no measurable visible GPU effect");
                File.WriteAllBytes(QualifiedIntegrationAudit.Output+"/source-trail-preview.png",bestImage);
                report["pass"]=true;report["gpuChangedPixels"]=bestPixels;report["renderSize"]=new JArray(1280,720);
                report["sourceAssetsUsed"]=true;report["nativeTrailAlgorithmParity"]=false;report["nativeShaderParity"]=false;
                Debug.Log("REMIELLE_SOURCE_TRAIL_PASS "+bestPixels);
            }
            catch(Exception ex){report["error"]=ex.ToString();Debug.LogException(ex);}
            finally{File.WriteAllText(QualifiedIntegrationAudit.Output+"/source-trail-verification.json",report.ToString());}
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
