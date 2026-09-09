using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Unity.Cinemachine;
using Remielle.ControllerRuntime;

namespace Remielle.Controller
{
    // Integration fixture, not a gameplay input mapper or character controller.
    [DefaultExecutionOrder(220)]
    public sealed class RemielleAdapterFrameProbe : MonoBehaviour
    {
        public TextAsset sourcePack;
        public RemielleAuthoredMotor motor;
        public CinemachineBrain brain;
        public string reportDirectory;
        public bool completed;
        public bool passed;
        QualifiedStateRouter router;
        NativeParameterBank parameters;
        int frame, enteredAt;
        Vector3 start;
        void Start()
        {
            try
            {
                var source = new NativeControllerSource(sourcePack.text);
                const string name = "Avatar_Female_Size02_RemielleOrigin_AirCombat_Controller";
                parameters = source.CreateParameters(name);
                router = new QualifiedStateRouter(source, name, 0, 26);
                start = motor.transform.position;
            }
            catch(Exception ex) { Finish(false,ex.ToString()); }
        }
        void Update()
        {
            if(completed) return;
            try
            {
                frame++;
                // Explicit frame-counter fixture. This does not claim the
                // game's gameplay parameter writer advances once per render frame.
                parameters.SetInt(parameters.Hash("FrameCount"),frame);
                if(router.CurrentState==26)
                {
                    var decision=router.Preview(parameters,0,0,2,1,false,
                        new NativeTransitionTimingPolicy(true,false),callbackContextQualified:true);
                    if(decision.Accepted){router.CommitLifecycle(decision);enteredAt=frame;}
                }
                // Synthetic displacement verifies the motor seam independently
                // of animation extraction and source-space conversion.
                motor.ApplyAuthoredDelta(frame,new Vector3(.02f,0,0));
            }
            catch(Exception ex){Finish(false,ex.ToString());}
        }
        void LateUpdate()
        {
            if(completed) return;
            try
            {
                brain.ManualUpdate();
                if(frame<120) return;
                float moved=motor.transform.position.x-start.x;
                bool ok=enteredAt==90&&router.CurrentState==0&&router.EnterCount==2&&moved>.2f&&moved<1.1f;
                var camera=brain.GetComponent<Camera>();
                ok &= Vector3.Distance(camera.transform.position,motor.transform.position)>1;
                var previous=camera.targetTexture;
                var target=new RenderTexture(640,360,24);
                var old=RenderTexture.active;
                Texture2D image=null;
                try
                {
                    camera.targetTexture=target;camera.Render();RenderTexture.active=target;
                    image=new Texture2D(640,360,TextureFormat.RGB24,false);
                    image.ReadPixels(new Rect(0,0,640,360),0,0);image.Apply();
                    Directory.CreateDirectory(reportDirectory);
                    File.WriteAllBytes(Path.Combine(reportDirectory,"adapter-frame-probe.png"),image.EncodeToPNG());
                }
                finally
                {
                    camera.targetTexture=previous;RenderTexture.active=old;
                    target.Release();Destroy(target);if(image)Destroy(image);
                }
                Finish(ok,ok?null:"Frame, collision or camera assertion failed");
            }
            catch(Exception ex){Finish(false,ex.ToString());}
        }
        void Finish(bool ok,string error)
        {
            completed=true;passed=ok;
            Directory.CreateDirectory(reportDirectory);
            File.WriteAllText(Path.Combine(reportDirectory,"unity-frame-adapter-verification.json"),new JObject {
                ["pass"]=ok,["frames"]=frame,["enteredAtFixtureFrame"]=enteredAt,["error"]=error,
                ["motorX"]=motor?motor.transform.position.x:0,["actualPlayMode"]=true,
                ["syntheticParameterAndMotorInputs"]=true,["gameplayParityVerified"]=false,
                ["characterAnimationDriven"]=false }.ToString());
        }
        void OnGUI()
        {
            GUI.Label(new Rect(20,20,750,30),"Source-rule / OpenKCC / Cinemachine integration fixture (not playable character)");
            GUI.Label(new Rect(20,50,700,30),"Frame "+frame+"  |  source state "+(router==null?"initializing":router.CurrentState.ToString())+"  |  "+(completed?(passed?"PASS":"FAIL"):"running"));
        }
    }
}
