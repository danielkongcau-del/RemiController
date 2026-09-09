using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Unity.Cinemachine;
using UnityEngine;

namespace Remielle.Controller
{
    // Per-hit unscaled time, on the existing Cinemachine impulse listener.
    // Never change the singleton's clock and accidentally retime action shakes.
    public sealed class SourceHitFeedback : IDisposable
    {
        sealed class UnscaledSignal : ISignalSource6D
        {
            public readonly SourceCameraShake.Signal Wave;
            public readonly double Start;
            readonly Func<double> now;
            public float SignalDuration=>Wave.SignalDuration;
            public UnscaledSignal(JToken config,AnimationCurve decay,Func<double> clock)
            {Wave=new SourceCameraShake.Signal(config,decay);now=clock;Start=now();}
            public void GetSignal(float unused,out Vector3 position,out Quaternion rotation)
                =>Wave.GetSignal((float)(now()-Start),out position,out rotation);
        }
        sealed class Active
        {
            public UnscaledSignal Signal;
            public CinemachineImpulseManager.ImpulseEvent Event;
        }
        readonly Dictionary<string,JToken> configs;
        readonly Dictionary<string,AnimationCurve> curves;
        readonly List<Active> active=new List<Active>();
        readonly SourceHitQuery hits;
        readonly Transform origin;
        readonly Func<double> now;
        bool disposed;
        public int Activations { get; private set; }
        public int Completed { get; private set; }
        public int Cancelled { get; private set; }
        public int MissesSkipped { get; private set; }
        public int UnsupportedKeys { get; private set; }
        public int ActiveCount=>active.Count;
        public SourceHitFeedback(string json,SourceHitQuery hits,Transform origin,Func<double> unscaledClock=null)
        {
            var p=JObject.Parse(json);if((string)p["schema"]!="remielle-hit-feedback-v1")throw new ArgumentException("Wrong hit feedback pack");
            configs=((JObject)p["configs"]).Properties().ToDictionary(x=>x.Name,x=>x.Value);
            curves=((JObject)p["curves"]).Properties().ToDictionary(x=>x.Name,x=>SourceCameraShake.ReadCurve(x.Value));
            foreach(var c in configs.Values)
            {
                foreach(string f in new[]{"ShakeType","NoiseRatio","DistanceToPlane","RollAmplitude","PitchAmplitude","YawAmplitude","FadeInDuration","FadeOutDuration","PlayPriority","DataPriority","PlayStackingType"})
                    if((float)c[f]!=0)throw new NotSupportedException("Unsupported hit shake field "+f);
                if(!(bool)c["IngoreTimeScale"]||(bool)c["RealtimeVibration"]||(int)c["DissipationMode"]!=5||
                    (float)c["ShakeTotalTime"]<=0||(float)c["RadiusLength"]<0||!curves.ContainsKey((string)c["CurveKey"]))throw new NotSupportedException("Unsupported hit shake time policy");
            }
            this.hits=hits??throw new ArgumentNullException(nameof(hits));this.origin=origin?origin:throw new ArgumentNullException(nameof(origin));
            now=unscaledClock??(()=>Time.unscaledTimeAsDouble);hits.Resolved+=OnResult;
        }
        void OnResult(SourceHitQuery.Result result)
        {
            var rule=result.Config["CameraShake"];if(rule==null||rule.Type==JTokenType.Null)return;
            if(result.TargetCount==0&&!(bool)rule["ShakeOnNotHit"]){MissesSkipped++;return;}
            string key=(string)rule["shakeConfigKey"];
            if(!configs.TryGetValue(key,out var c)){UnsupportedKeys++;return;}
            Advance();var signal=new UnscaledSignal(c,curves[(string)c["CurveKey"]],now);
            var manager=CinemachineImpulseManager.Instance;var impulse=manager.NewImpulseEvent();
            impulse.SignalSource=signal;impulse.Position=origin.position;impulse.Channel=SourceCameraShake.Channel;
            impulse.Radius=9999999;impulse.DissipationDistance=0;impulse.PropagationSpeed=float.MaxValue;
            impulse.DirectionMode=CinemachineImpulseManager.ImpulseEvent.DirectionModes.Fixed;
            // This envelope only keeps CM's pooled event alive. Wave and
            // lifecycle use the original unscaled 0.6 s clock, even at scale 0.
            impulse.Envelope=new CinemachineImpulseManager.EnvelopeDefinition{SustainTime=float.MaxValue*.25f};
            manager.AddImpulseEvent(impulse);
            // Nonzero listener distance adds a tiny propagation delay. Keep
            // the envelope active even for a hit created during a scaled pause.
            impulse.StartTime=manager.CurrentTime-1;
            active.Add(new Active{Signal=signal,Event=impulse});Activations++;
        }
        static void End(Active a)
        {
            a.Signal.Wave.Cancelled=true;
            if(!ReferenceEquals(a.Event.SignalSource,a.Signal))return;
            // Positive envelope already in the past lets CM recycle this
            // owned event even when its scaled clock is completely paused.
            a.Event.Envelope=new CinemachineImpulseManager.EnvelopeDefinition{SustainTime=.001f};
            a.Event.StartTime=CinemachineImpulseManager.Instance.CurrentTime-1;
        }
        public void Advance()
        {
            if(disposed)throw new ObjectDisposedException(nameof(SourceHitFeedback));
            for(int i=active.Count-1;i>=0;i--)
                if(now()-active[i].Signal.Start>=active[i].Signal.SignalDuration||!ReferenceEquals(active[i].Event.SignalSource,active[i].Signal))
                {End(active[i]);active.RemoveAt(i);Completed++;}
        }
        public void Dispose()
        {
            if(disposed)return;hits.Resolved-=OnResult;
            foreach(var a in active){End(a);Cancelled++;}active.Clear();disposed=true;
        }
    }
}
