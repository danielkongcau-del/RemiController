using System;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Remielle.ControllerRuntime;
using Unity.Cinemachine;
using UnityEngine;

namespace Remielle.Controller
{
    [DefaultExecutionOrder(220)]
    public sealed class SourceMotionFrameProbe : MonoBehaviour
    {
        public RemielleNativeAnimation driver;
        public Transform visual;
        public RemielleAuthoredMotor motor;
        public CinemachineBrain brain;
        public TextAsset sourcePack;
        public string reportDirectory;
        public bool completed,passed;
        readonly List<SourceMotionSampler> clips=new List<SourceMotionSampler>();
        BlendedRootTransport transport;
        int frame;
        float maxMotorStep;
        readonly JArray captures=new JArray();
        void Start()
        {
            try
            {
                const string controller="Avatar_Female_Size02_RemielleOrigin_Controller";
                string directory=Path.Combine(Application.streamingAssetsPath,"RemielleControllerMotions");
                var source=new NativeControllerSource(sourcePack.text);
                var bank=new NativeMotionBank(directory);
                var bindings=new NativeControllerAvatarBindings(File.ReadAllText(Path.Combine(directory,"controller-avatar-bindings.json")),File.ReadAllText(Path.Combine(directory,"binding-profiles.json")));
                var profile=bindings.Resolve(source.GetController(controller));
                foreach(int slot in new[]{10,5,43,0})clips.Add(new SourceMotionSampler(directory,bank,controller,slot,driver.nativeAnimation.transform,profile));
                transport=new BlendedRootTransport(clips[0],driver,visual,motor,1);
            }
            catch(Exception ex){Finish(ex.ToString());}
        }
        void Update()
        {
            if(completed)return;
            try
            {
                frame++;
                // Fixed source time is intentional test input. These staged
                // blends are not gameplay transition selection or key handling.
                if(frame<=96)transport.Sample(frame/60d);
                else if(frame<=108)
                {
                    if(frame==97)transport.BeginBlend(clips[1],0);
                    double t=(frame-96)/60d;transport.Sample(1.6+t,t,(frame-96)/12f);
                    if(frame==108)transport.CompleteBlend();
                }
                else if(frame<=144)transport.Sample((frame-96)/60d);
                else if(frame<=156)
                {
                    if(frame==145)transport.BeginBlend(clips[2],0);
                    double t=(frame-144)/60d;transport.Sample(.8+t,t,(frame-144)/12f);
                    if(frame==156)transport.CompleteBlend();
                }
                else if(frame<=228)transport.Sample((frame-144)/60d);
                else if(frame<=240)
                {
                    if(frame==229)transport.BeginBlend(clips[3],0);
                    double t=(frame-228)/60d;transport.Sample(1.4+t,t,(frame-228)/12f);
                    if(frame==240)transport.CompleteBlend();
                }
                else transport.Sample((frame-228)/60d);
                maxMotorStep=Mathf.Max(maxMotorStep,motor.LastAppliedDelta.magnitude);
                if(maxMotorStep>1||!float.IsFinite(motor.transform.position.sqrMagnitude))throw new Exception("Motion discontinuity/nonfinite travel");
            }
            catch(Exception ex){Finish(ex.ToString());}
        }
        void LateUpdate()
        {
            if(completed)return;
            try
            {
                brain.ManualUpdate();
                if(frame==90||frame==135||frame==200||frame==300)Capture();
                if(frame>=360)Finish(null);
            }
            catch(Exception ex){Finish(ex.ToString());}
        }
        void Capture()
        {
            Directory.CreateDirectory(reportDirectory);
            Camera camera=brain.GetComponent<Camera>();
            var rt=RenderTexture.GetTemporary(1280,720,24,RenderTextureFormat.ARGB32);
            var texture=new Texture2D(1280,720,TextureFormat.RGB24,false);
            var oldTarget=camera.targetTexture;var oldActive=RenderTexture.active;
            try
            {
                camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
                texture.ReadPixels(new Rect(0,0,1280,720),0,0);texture.Apply();
                string name="source-motion-frame-"+frame+".png";
                File.WriteAllBytes(Path.Combine(reportDirectory,name),texture.EncodeToPNG());
                captures.Add(new JObject{["frame"]=frame,["image"]=name,["actorZ"]=motor.transform.position.z});
            }
            finally{camera.targetTexture=oldTarget;RenderTexture.active=oldActive;RenderTexture.ReleaseTemporary(rt);Destroy(texture);}
        }
        void Finish(string error)
        {
            passed=error==null;completed=true;
            Directory.CreateDirectory(reportDirectory);
            File.WriteAllText(Path.Combine(reportDirectory,"source-motion-frame-verification.json"),new JObject{
                ["pass"]=passed,["error"]=error,["frames"]=frame,["maxMotorStep"]=maxMotorStep,["captures"]=captures,
                ["actualPlayMode"]=true,["actualCharacter"]=true,["scriptedClipSchedule"]=true,
                ["gameplayTransitionsVerified"]=false,["inputIntegrated"]=false}.ToString());
            if(error!=null)Debug.LogError(error);
        }
        void OnDestroy(){transport?.Dispose();foreach(var clip in clips)clip.Dispose();clips.Clear();}
    }
}
