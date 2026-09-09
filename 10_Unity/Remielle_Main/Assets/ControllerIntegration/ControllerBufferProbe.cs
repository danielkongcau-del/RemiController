using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Remielle.Controller
{
    [DefaultExecutionOrder(-300)]
    public sealed class ControllerBufferProbe : MonoBehaviour
    {
        public RemielleSourceController controller;
        public string reportDirectory;
        public bool completed,passed;
        Keyboard keyboard;Mouse mouse;InputSettings originalSettings,testSettings;
        float oldCaptureDelta;
        int frame,phase,idleFrames,prepared;
        bool sentEarly,sentCombo,enteredNormal,enteredSecond,sawPending;
        void Start()
        {
            originalSettings=InputSystem.settings;testSettings=Instantiate(originalSettings);InputSystem.settings=testSettings;
            testSettings.updateMode=InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
            testSettings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            oldCaptureDelta=Time.captureDeltaTime;Time.captureDeltaTime=1f/60;
            keyboard=InputSystem.AddDevice<Keyboard>("BufferProbeKeyboard");mouse=InputSystem.AddDevice<Mouse>("BufferProbeMouse");
            controller.RestrictInputDevices(keyboard,mouse);prepared=controller.Session.PreparedSamplerCount;
            foreach(var target in FindObjectsByType<SourceTrainingTarget>(FindObjectsSortMode.None))target.gameObject.SetActive(false);
        }
        void Update()
        {
            if(completed)return;
            try
            {
                frame++;if(!controller.Ready)throw new Exception(controller.Error??"Controller not ready");
                if(frame>2200)throw new Exception("Buffer probe timed out in phase "+phase);
                var s=controller.Session;var b=controller.InputBuffer;bool attack=false;
                if(s.PreparedSamplerCount!=prepared)throw new Exception("Input buffering loaded an unprepared motion");
                if(phase==0)
                {
                    if(s.CurrentState==55&&s.ActionFrames>=20&&!sentEarly){attack=true;sentEarly=true;}
                    if(sentEarly&&(s.CurrentState==10||s.CurrentState==46||s.NextState==10||s.NextState==46))
                        throw new Exception("Expired early attack executed after the EX lockout");
                    if(sentEarly&&s.CurrentState==32&&!s.IsBlending&&++idleFrames>=30)
                    {
                        if(b.Expired!=1||b.PendingCount!=0)throw new Exception("Early attack did not expire cleanly");
                        phase=1;attack=true;
                    }
                }
                else
                {
                    enteredNormal|=s.CurrentState==46;
                    if(s.CurrentState==46&&!s.IsBlending&&s.ActionFrames>=20&&!sentCombo){attack=true;sentCombo=true;}
                    if(sentCombo&&s.CurrentState==46&&s.ActionFrames>21&&s.ActionFrames<26&&b.PendingCount==1)sawPending=true;
                    if(s.NextState==41||s.CurrentState==41)
                    {
                        var transition=s.Trace.Last(t=>(string)t["event"]=="transition-start"&&(int)t["targetState"]==41);
                        if((float)transition["sourceFrames"]<26||(float)transition["sourceFrames"]>28)throw new Exception("Buffered combo bypassed its authored frame-26 gate");
                        enteredSecond=true;
                    }
                    if(enteredSecond&&s.CurrentState==32&&!s.IsBlending){Finish(null);return;}
                }
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(frame==1?new[]{Key.E}:Array.Empty<Key>()));
                InputSystem.QueueStateEvent(mouse,new MouseState().WithButton(MouseButton.Left,attack));
            }
            catch(Exception ex){Finish(ex.ToString());}
        }
        void Finish(string error)
        {
            var b=controller.InputBuffer;var s=controller.Session;
            if(error==null&&(!enteredNormal||!enteredSecond||!sawPending||b.Expired!=1||b.Consumed!=3||b.PendingCount!=0))error="Buffer trajectory coverage/count differs";
            completed=true;passed=error==null;controller.readDevices=false;Directory.CreateDirectory(reportDirectory);
            File.WriteAllText(Path.Combine(reportDirectory,"controller-buffer-verification.json"),new JObject{
                ["pass"]=passed,["error"]=error,["frames"]=frame,["expired"]=b.Expired,["consumed"]=b.Consumed,["pending"]=b.PendingCount,
                ["durationSeconds"]=b.Duration,["sawPendingBeforeGate"]=sawPending,["enteredSecondAttack"]=enteredSecond,
                ["virtualInputSystemDevices"]=true,["directParameterInjection"]=false,["preparedSamplers"]=prepared,["finalSamplers"]=s.PreparedSamplerCount,
                ["trace"]=new JArray(s.Trace)}.ToString());
            if(error!=null)Debug.LogError(error);
        }
        void OnDestroy()
        {
            if(keyboard!=null)InputSystem.RemoveDevice(keyboard);if(mouse!=null)InputSystem.RemoveDevice(mouse);
            if(originalSettings)InputSystem.settings=originalSettings;if(testSettings)Destroy(testSettings);Time.captureDeltaTime=oldCaptureDelta;
        }
    }
}
