using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Remielle.Controller.Editor
{
    public static class SourceWeaponSurfaceAudit
    {
        public static void Run()
        {
            var report=new JObject{["pass"]=false};
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                SourceTrailBuild.Prepare();SourceWeaponMaterialBuild.Prepare();var texture=SourceWeaponMaterialBuild.PrepareSurface();
                var shader=AssetDatabase.LoadAssetAtPath<Shader>(SourceWeaponMaterialBuild.SurfaceShader);
                var visual=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/V3/Remielle_V3_Animated.prefab"));
                var bindings=SourceVisibilityBuild.Prepare(visual);var pack=JObject.Parse(File.ReadAllText(SourceWeaponMaterialBuild.Pack));
                var ids=pack["ditherTargets"].Select(t=>SourceRendererVisibility.Key((string)t["sourceBlock"],(string)t["cab"],(string)t["rendererPathID"])).ToArray();
                var targets=bindings.Where(b=>ids.Contains(b.Identity)).Select(b=>b.renderer).ToArray();
                foreach(var r in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))r.enabled=targets.Contains(r);
                if(targets.Any(r=>r.sharedMaterials.Length!=1))throw new Exception("Expected single-slot weapon surfaces");
                var materials=targets.Select(r=>r.sharedMaterial).ToArray();
                var bounds=targets[0].bounds;foreach(var r in targets.Skip(1))bounds.Encapsulate(r.bounds);
                var camera=new GameObject("SurfaceAuditCamera").AddComponent<Camera>();camera.fieldOfView=35;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.02f,.02f,.03f);
                camera.transform.position=bounds.center+Vector3.forward*Math.Max(1.2f,bounds.extents.magnitude*3.4f);camera.transform.LookAt(bounds.center);
                var light=new GameObject("SurfaceAuditLight").AddComponent<Light>();light.type=LightType.Directional;light.transform.rotation=Quaternion.Euler(30,-20,0);RenderSettings.ambientLight=Color.gray;
                var original=Render(camera);var cases=new JArray();
                foreach(int hz in new[]{30,60,120})
                {
                    using(var surface=new SourceWeaponSurface(pack.ToString(),bindings,shader,texture))
                    {
                        if(surface.Visibility!=0||surface.TargetCount!=3)throw new Exception("Authored hidden default was not initialized");
                        if(hz==60)
                        {
                            var hidden=Render(camera);
                            void Override(float alpha,Vector4 color)
                            {
                                foreach(var r in targets){var b=new MaterialPropertyBlock();r.GetPropertyBlock(b,0);b.SetFloat("_SourceWeaponVisibility",alpha);b.SetVector("_SourceWeaponOutlineColor",color);r.SetPropertyBlock(b,0);}
                            }
                            Override(1,Vector4.zero);var neutral=Render(camera);
                            int changed=Diff(original,neutral);if(changed!=0)throw new Exception("Neutral derived shader changed the accepted rendering: "+changed);
                            Override(.5f,Vector4.zero);var half=Render(camera);
                            int fullPixels=Diff(neutral,hidden),halfPixels=Diff(half,hidden);
                            double ratio=(double)halfPixels/fullPixels;
                            if(fullPixels<100||ratio<.40||ratio>.60)throw new Exception("Dither coverage is not approximately half: "+ratio);
                            Override(1,new Vector4(1.4980392f,1.4980392f,1.4980392f,1));var outline=Render(camera);
                            int outlinePixels=Diff(neutral,outline);if(outlinePixels==0)throw new Exception("Outline pulse did not affect the GPU output");
                            File.WriteAllBytes(QualifiedIntegrationAudit.Output+"/source-weapon-surface-neutral.png",neutral.EncodeToPNG());
                            File.WriteAllBytes(QualifiedIntegrationAudit.Output+"/source-weapon-surface-half.png",half.EncodeToPNG());
                            File.WriteAllBytes(QualifiedIntegrationAudit.Output+"/source-weapon-surface-outline.png",outline.EncodeToPNG());
                            report["neutralChangedPixels"]=changed;report["visiblePixels"]=fullPixels;report["halfVisiblePixels"]=halfPixels;report["halfCoverageRatio"]=ratio;report["outlineChangedPixels"]=outlinePixels;
                            foreach(var t in new[]{hidden,neutral,half,outline})UnityEngine.Object.DestroyImmediate(t);
                        }
                        surface.SetActive(true);surface.Advance(1f/hz);float begin=surface.Visibility;
                        for(int i=0;i<hz;i++)surface.Advance(1f/hz);
                        if(begin!=0||Math.Abs(surface.Visibility-1)>1e-6||surface.IsTransitioning||surface.OutlineColor!=Vector4.zero)throw new Exception("Entered effect failed to hold visible endpoint");
                        surface.SetActive(false);surface.Advance(1f/hz);float closing=surface.Visibility;
                        for(int i=0;i<hz;i++)surface.Advance(1f/hz);
                        if(closing!=1||Math.Abs(surface.Visibility)>1e-6||surface.IsTransitioning)throw new Exception("Leaving effect did not stay hidden");
                        surface.SetActive(true);surface.Advance(1f/hz);surface.Advance(1f/hz);float paused=surface.Visibility;var color=surface.OutlineColor;
                        surface.Advance(0);if(surface.Visibility!=paused||surface.OutlineColor!=color)throw new Exception("Pause advanced weapon surface");
                        // Ending in the middle of a transition exercises cleanup.
                        cases.Add(new JObject{["hz"]=hz,["enteredBegin"]=begin,["leavingBegin"]=closing,["pausedVisibility"]=paused,["pass"]=true});
                    }
                    for(int i=0;i<targets.Length;i++)
                    {
                        var b=new MaterialPropertyBlock();targets[i].GetPropertyBlock(b,0);
                        if(targets[i].sharedMaterial!=materials[i]||!b.isEmpty)throw new Exception("Surface disposal did not restore model materials and blocks");
                    }
                }
                UnityEngine.Object.DestroyImmediate(original);
                report["cases"]=cases;report["pass"]=true;report["nativeStartupPolicyVerified"]=false;report["nativeOutlineParity"]=false;
                Debug.Log("REMIELLE_SOURCE_WEAPON_SURFACE_PASS");
            }
            catch(Exception e){report["error"]=e.ToString();Debug.LogException(e);}
            finally{File.WriteAllText(QualifiedIntegrationAudit.Output+"/source-weapon-surface-verification.json",report.ToString());}
            if(!(bool)report["pass"])EditorApplication.Exit(1);
        }
        static int Diff(Texture2D a,Texture2D b)=>a.GetPixels32().Zip(b.GetPixels32(),(x,y)=>Math.Abs(x.r-y.r)+Math.Abs(x.g-y.g)+Math.Abs(x.b-y.b)).Count(d=>d>3);
        static Texture2D Render(Camera camera)
        {
            var target=RenderTexture.GetTemporary(1280,720,24,RenderTextureFormat.ARGB32);var old=camera.targetTexture;var active=RenderTexture.active;
            try{camera.targetTexture=target;camera.Render();RenderTexture.active=target;var image=new Texture2D(1280,720,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();return image;}
            finally{camera.targetTexture=old;RenderTexture.active=active;RenderTexture.ReleaseTemporary(target);}
        }
    }
}
