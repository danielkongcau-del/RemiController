using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Unity.Cinemachine;
using UnityEngine;

namespace Remielle.Controller
{
    // Authored events/configuration into Cinemachine's existing impulse pool
    // and listener. The signal/distance/stacking policy is a preview adapter.
    public sealed class SourceCameraShake : IDisposable
    {
        public const int Channel=1<<24;
        public sealed class Signal : ISignalSource6D
        {
            public float SignalDuration { get; }
            readonly AnimationCurve decay;
            readonly float frequency,amplitude;
            readonly Vector3 direction;
            public bool Cancelled;
            public Signal(JToken config,AnimationCurve decay)
            {
                this.decay=decay;SignalDuration=(float)config["ShakeTotalTime"];
                frequency=(float)config["Frequency"];amplitude=(float)config["RadiusLength"]*.5f;
                float angle=(float)config["AngleVertical"]*Mathf.Deg2Rad;
                direction=new Vector3(Mathf.Cos(angle),Mathf.Sin(angle),0);
            }
            public void GetSignal(float timeSinceSignalStart,out Vector3 pos,out Quaternion rot)
            {
                rot=Quaternion.identity;pos=Vector3.zero;
                if(Cancelled||timeSinceSignalStart<0||timeSinceSignalStart>=SignalDuration)return;
                pos=direction*(amplitude*Mathf.Cos(2*Mathf.PI*frequency*timeSinceSignalStart)*decay.Evaluate(timeSinceSignalStart/SignalDuration));
            }
        }
        sealed class Active
        {
            public long Generation;
            public float Started;
            public Signal Signal;
            public CinemachineImpulseManager.ImpulseEvent Event;
        }
        readonly Dictionary<string,JToken> configs;
        readonly Dictionary<string,AnimationCurve> curves;
        readonly List<Active> active=new List<Active>();
        readonly SourceFrameEvents events;
        readonly Transform origin;
        bool disposed;
        public int Activations { get; private set; }
        public int Completed { get; private set; }
        public int Cancelled { get; private set; }
        public int UnsupportedEvents { get; private set; }
        public int ActiveCount=>active.Count;
        public SourceCameraShake(string json,SourceFrameEvents events,Transform origin)
        {
            var pack=JObject.Parse(json);if((string)pack["schema"]!="remielle-action-shake-v1")throw new ArgumentException("Wrong shake pack");
            if(CinemachineImpulseManager.Instance.IgnoreTimeScale)throw new ArgumentException("Source shake adapter requires scaled Cinemachine impulse time");
            configs=((JObject)pack["configs"]).Properties().ToDictionary(p=>p.Name,p=>p.Value);
            curves=((JObject)pack["curves"]).Properties().ToDictionary(p=>p.Name,p=>ReadCurve(p.Value));
            foreach(var c in configs.Values)
            {
                foreach(string f in new[]{"ShakeType","NoiseRatio","DistanceToPlane","RollAmplitude","PitchAmplitude","YawAmplitude","FadeInDuration","FadeOutDuration","PlayPriority","DataPriority","PlayStackingType"})
                    if((float)c[f]!=0)throw new ArgumentException("Unsupported source shake field "+f);
                if((bool)c["IngoreTimeScale"]||(bool)c["RealtimeVibration"]||(int)c["DissipationMode"]!=5||
                    (float)c["ShakeTotalTime"]<=0||(float)c["RadiusLength"]<0||!curves.ContainsKey((string)c["CurveKey"]))throw new ArgumentException("Unsupported source shake policy");
            }
            this.events=events??throw new ArgumentNullException(nameof(events));this.origin=origin?origin:throw new ArgumentNullException(nameof(origin));
            events.Emitted+=OnEvent;events.Released+=OnRelease;
        }
        public static AnimationCurve ReadCurve(JToken data)=>new AnimationCurve(data["keys"].Select(k=>new Keyframe(
            (float)k["time"],(float)k["value"],(float)k["inTangent"],(float)k["outTangent"],(float)k["inWeight"],(float)k["outWeight"])
            {weightedMode=(WeightedMode)(int)k["weightedMode"]}).ToArray()){preWrapMode=WrapMode.ClampForever,postWrapMode=WrapMode.ClampForever};
        void OnEvent(SourceFrameEvents.Emission e)
        {
            if(e.Type!="AnimatorEventCameraShakeEntry")return;
            if(!configs.TryGetValue((string)e.Fields["CameraShakeKey"],out var c)){UnsupportedEvents++;return;}
            Advance();
            var signal=new Signal(c,curves[(string)c["CurveKey"]]);var manager=CinemachineImpulseManager.Instance;
            var impulse=manager.NewImpulseEvent();impulse.SignalSource=signal;impulse.Position=origin.position;
            impulse.Channel=Channel;impulse.Radius=9999999;impulse.DissipationDistance=0;impulse.PropagationSpeed=float.MaxValue;
            impulse.DirectionMode=CinemachineImpulseManager.ImpulseEvent.DirectionModes.Fixed;
            impulse.Envelope=new CinemachineImpulseManager.EnvelopeDefinition{SustainTime=signal.SignalDuration};
            manager.AddImpulseEvent(impulse);
            active.Add(new Active{Generation=e.Generation,Signal=signal,Event=impulse,Started=impulse.StartTime});Activations++;
        }
        void OnRelease(long generation,string reason)
        {
            Advance();
            for(int i=active.Count-1;i>=0;i--)if(active[i].Generation==generation){Cancel(active[i]);active.RemoveAt(i);}
        }
        void Cancel(Active a)
        {
            a.Signal.Cancelled=true;
            // The manager pools events. Never cancel a reused event belonging
            // to another source after a listener has already recycled ours.
            if(ReferenceEquals(a.Event.SignalSource,a.Signal))
            {
                a.Event.Cancel(CinemachineImpulseManager.Instance.CurrentTime,true);
                // A zero-duration envelope is treated as non-expiring by CM.
                // Mute immediately and allow collection after a positive tick.
                if(a.Event.Envelope.Duration<=0)a.Event.Envelope.SustainTime=.0001f;
            }
            Cancelled++;
        }
        public void Advance()
        {
            if(disposed)throw new ObjectDisposedException(nameof(SourceCameraShake));
            float time=CinemachineImpulseManager.Instance.CurrentTime;
            for(int i=active.Count-1;i>=0;i--)
                if(time-active[i].Started>=active[i].Signal.SignalDuration||!ReferenceEquals(active[i].Event.SignalSource,active[i].Signal))
                {active.RemoveAt(i);Completed++;}
        }
        public void Dispose()
        {
            if(disposed)return;events.Emitted-=OnEvent;events.Released-=OnRelease;
            foreach(var a in active)Cancel(a);active.Clear();disposed=true;
        }
    }
}
