using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Remielle.Controller
{
    public sealed class SourceHitStop : IDisposable
    {
        readonly Dictionary<(int,int),JObject> attacks=new Dictionary<(int,int),JObject>();
        readonly SourceHitQuery hits;
        double remaining;
        bool disposed;
        public double RemainingSeconds=>remaining;
        public double ConsumedSeconds { get; private set; }
        public int Requests { get; private set; }
        public int ZeroFrameHits { get; private set; }
        public int MissesSkipped { get; private set; }
        public int Completed { get; private set; }
        public SourceHitStop(string json,SourceHitQuery hits)
        {
            var p=JObject.Parse(json);if((string)p["schema"]!="remielle-hitstop-v1")throw new ArgumentException("Wrong hitstop pack");
            foreach(JObject row in p["attacks"])
            {
                if((int)row["attackerFrames"]<0)throw new ArgumentException("Negative hitstop");
                attacks.Add(((int)row["state"],(int)row["entry"]),row);
            }
            this.hits=hits??throw new ArgumentNullException(nameof(hits));hits.Resolved+=OnResult;
        }
        void OnResult(SourceHitQuery.Result result)
        {
            if(result.TargetCount==0){MissesSkipped++;return;}
            if(!attacks.TryGetValue((result.State,result.Entry),out var row)||(string)row["key"]!=result.Key)
                throw new InvalidOperationException("Unqualified hitstop event");
            int frames=(int)row["attackerFrames"];
            if(frames==0){ZeroFrameHits++;return;}
            remaining=Math.Max(remaining,frames/60.0);Requests++;
        }
        public float Consume(float delta)
        {
            if(disposed)throw new ObjectDisposedException(nameof(SourceHitStop));
            if(!float.IsFinite(delta)||delta<0)throw new ArgumentOutOfRangeException(nameof(delta));
            if(delta==0||remaining==0)return delta;
            double used=Math.Min(delta,remaining);remaining-=used;ConsumedSeconds+=used;
            if(remaining<1e-9){remaining=0;Completed++;}
            double rest=delta-used;return rest<1e-8?0:(float)rest;
        }
        public void Dispose(){if(disposed)return;hits.Resolved-=OnResult;remaining=0;disposed=true;}
    }
}
