using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Remielle.ControllerRuntime;

namespace Remielle.Controller
{
    // Finite Unique modifier window for families with no emission consumer.
    // It uses the same inclusive host boundaries and leaving-generation rule.
    public sealed class SourceModifierWindow : IDisposable
    {
        readonly JObject[] windows;
        readonly HashSet<long> owners=new HashSet<long>(),leaving=new HashSet<long>();
        bool disposed;
        public event Action<bool> Changed;
        public int ActiveOwners=>owners.Count;
        public int Activations { get; private set; }
        public int Releases { get; private set; }
        public SourceModifierWindow(string json,NativeControllerSource source)
        {
            var p=JObject.Parse(json);var ctl=source.GetController("Avatar_Female_Size02_RemielleOrigin_Controller");
            if(!JToken.DeepEquals(p["controllerIdentity"],ctl["identity"])||(string)p["controllerRawSha256"]!=(string)ctl["raw"]["sha256"])throw new ArgumentException("Modifier controller identity mismatch");
            windows=p["windows"].Cast<JObject>().ToArray();
            foreach(var w in windows)if((string)ctl["machines"][0]["states"][(int)w["state"]]["name"]!=(string)w["AnimatorStateName"])throw new ArgumentException("Modifier state identity mismatch");
        }
        public void Observe(SourceActionNotice n)
        {
            if(disposed)throw new ObjectDisposedException(nameof(SourceModifierWindow));
            if(n.Phase==SourceActionPhase.Leave)leaving.Add(n.Generation);
            if(n.Phase==SourceActionPhase.Exit)leaving.Remove(n.Generation);
            if(n.Phase==SourceActionPhase.Sample&&leaving.Contains(n.Generation))return;
            bool active=n.Phase!=SourceActionPhase.Leave&&n.Phase!=SourceActionPhase.Exit&&windows.Any(w=>(int)w["state"]==n.State&&
                n.Frame>=((bool)w["MaxFrameCountLow"]?n.LengthFrames:(int)w["FrameCountLow"])&&n.Frame<=((bool)w["MaxFrameCountHigh"]?n.LengthFrames:(int)w["FrameCountHigh"]));
            if(active){if(owners.Add(n.Generation)&&owners.Count==1){Activations++;Changed?.Invoke(true);}}
            else if(owners.Remove(n.Generation)&&owners.Count==0){Releases++;if(n.Reason!="owner-disposed")Changed?.Invoke(false);}
        }
        public void Dispose(){disposed=true;owners.Clear();leaving.Clear();Changed=null;}
    }
}
