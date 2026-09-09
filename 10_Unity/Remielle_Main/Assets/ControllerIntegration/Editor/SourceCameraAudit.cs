using System;
using System.IO;
using Newtonsoft.Json.Linq;
using Remielle.ControllerRuntime;
using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Remielle.Controller.Editor
{
    public static class SourceCameraAudit
    {
        public static void Run()
        {
            var report=new JObject{["pass"]=false};var cases=new JArray();report["cases"]=cases;
            void Check(bool pass,string message){if(!pass)throw new Exception(message);}
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                string pack=SourceCameraBuild.Prepare().text;
                var curve=SourceLayoutCamera.ReadCurve(JObject.Parse(pack)["curves"]["Camera_Default_Curve_04"]["curve"]);
                double maxError=0;
                for(int i=0;i<=1000;i++){double t=i/1000.0;maxError=Math.Max(maxError,Math.Abs(curve.Evaluate((float)t)-t*t*(3-2*t)));}
                Check(maxError<1e-6,"Authored unweighted zero-tangent Hermite curve differs");report["curveIndependentHermiteMaxError"]=maxError;
                foreach(int hz in new[]{30,60,120})
                {
                    var target=new GameObject("Target").transform;
                    var camera=new GameObject("LayoutCamera").AddComponent<CinemachineCamera>();camera.Follow=camera.LookAt=target;camera.Lens.FieldOfView=40;
                    var follow=camera.gameObject.AddComponent<CinemachineFollow>();follow.FollowOffset=new Vector3(0,1,6);
                    follow.TrackerSettings.BindingMode=BindingMode.WorldSpace;follow.TrackerSettings.PositionDamping=Vector3.zero;
                    var composer=camera.gameObject.AddComponent<CinemachineRotationComposer>();composer.Damping=Vector2.zero;
                    var source=new NativeControllerSource(File.ReadAllText("Assets/ControllerRuntime/Data/source-controller-pack.json"));
                    var events=new SourceFrameEvents(File.ReadAllText("Assets/ControllerIntegration/Data/source-action-events.json"),source,"Avatar_Female_Size02_RemielleOrigin_Controller");
                    using(var host=new SourceLayoutCamera(pack,events,camera))
                    {
                        void Send(SourceActionPhase phase,long id,double frame)=>events.Observe(new SourceActionNotice(phase,id,25,frame,100,false,"camera-audit"));
                        void Step(int count){for(int i=0;i<count;i++)host.Advance(1f/hz);}
                        Send(SourceActionPhase.Enter,1,0);Send(SourceActionPhase.Sample,1,7);
                        Check(host.AtBaseline&&host.Activations==0,"Camera entered before authored frame 8");
                        Send(SourceActionPhase.Sample,1,8);host.Advance(0);
                        Check(host.HasOwner&&host.FieldOfView==40,"Camera snapped on enter");
                        Step(hz/2);Check(host.FieldOfView==65&&!host.IsTransitioning,"Camera did not reach source FOV at 0.5 seconds");
                        var worldAim=target.TransformVector(composer.TargetOffset);var offset=follow.FollowOffset;
                        Check(Math.Abs((offset-worldAim).magnitude-3)<1e-5,"Source orbit radius not preserved");
                        target.rotation=Quaternion.Euler(0,135,0);host.Advance(0);
                        Check(Vector3.Distance(target.TransformVector(composer.TargetOffset),worldAim)<1e-6&&follow.FollowOffset==offset,"Dash orbit rotated with movement direction");
                        Send(SourceActionPhase.Sample,1,38);Check(host.FieldOfView==65&&!host.HasOwner,"Natural exit snapped the camera");
                        Step(hz/2);Check(host.AtBaseline,"Natural exit failed to restore baseline");
                        Send(SourceActionPhase.Exit,1,39);Check(host.Releases==1,"Scope release repeated camera exit");
                        Send(SourceActionPhase.Enter,2,0);Send(SourceActionPhase.Sample,2,8);Step(hz/5);
                        float before=host.FieldOfView;Send(SourceActionPhase.Leave,2,12);host.Advance(0);
                        Check(host.FieldOfView==before&&host.ForcedReleases==1,"Early exit jumped or lost its owner");
                        Step(hz/10);float exiting=host.FieldOfView;
                        Send(SourceActionPhase.Enter,3,0);Send(SourceActionPhase.Sample,3,8);host.Advance(0);
                        Check(host.FieldOfView==exiting,"Reentry snapped an outgoing blend");
                        Send(SourceActionPhase.Exit,2,20);Check(host.HasOwner,"Stale scope cancelled the new camera owner");
                        float paused=host.FieldOfView;host.Advance(0);Check(host.FieldOfView==paused,"Zero delta advanced the blend");
                        Send(SourceActionPhase.Exit,3,12);Step(hz/2);Check(host.AtBaseline,"Reentry cancellation leaked camera values");
                        Send(SourceActionPhase.Enter,4,0);Send(SourceActionPhase.Exit,4,2);
                        Check(host.Activations==3&&host.Releases==3&&host.AtBaseline,"Exit before camera entry activated a future event");
                        Send(SourceActionPhase.Enter,5,0);Send(SourceActionPhase.Sample,5,8);Step(hz/5);host.Dispose();
                        Check(host.AtBaseline&&camera.Lens.FieldOfView==40&&follow.FollowOffset==new Vector3(0,1,6)&&composer.TargetOffset==Vector3.zero,"Disposal failed to restore exact baseline");
                        Send(SourceActionPhase.Sample,5,38);Check(host.Activations==4&&host.Releases==4,"Disposed consumer remained subscribed");
                        cases.Add(new JObject{["hz"]=hz,["activations"]=host.Activations,["releases"]=host.Releases,["forcedReleases"]=host.ForcedReleases,["pass"]=true});
                    }
                    UnityEngine.Object.DestroyImmediate(camera.gameObject);UnityEngine.Object.DestroyImmediate(target.gameObject);
                }
                report["pass"]=true;report["nativeCameraComposeParity"]=false;Debug.Log("REMIELLE_SOURCE_CAMERA_PASS");
            }
            catch(Exception ex){report["error"]=ex.ToString();Debug.LogException(ex);}
            finally{File.WriteAllText(QualifiedIntegrationAudit.Output+"/source-camera-verification.json",report.ToString());}
            if(!(bool)report["pass"])EditorApplication.Exit(1);
        }
    }
}
