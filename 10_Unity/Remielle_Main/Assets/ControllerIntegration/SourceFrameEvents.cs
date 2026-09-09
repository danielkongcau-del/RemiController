using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Remielle.ControllerRuntime;

namespace Remielle.Controller
{
    // Project event host: authored frames/flags, monotonic per-entry clocks and
    // explicit cancellation. This does not claim native callback-order parity.
    public sealed class SourceFrameEvents
    {
        sealed class Entry
        {
            public int Index,Frame;
            public bool Max,ForceIn,ForceOut;
            public string Type;
            public JObject Fields;
        }
        sealed class Scope
        {
            public int State;
            public double Frame,Length;
            public bool Loop,Leaving;
            public long[] LastCycle;
        }
        public sealed class Emission
        {
            public long Generation { get; internal set; }
            public int State { get; internal set; }
            public int Index { get; internal set; }
            public long Cycle { get; internal set; }
            public double AuthoredFrame { get; internal set; }
            public string Type { get; internal set; }
            public string Reason { get; internal set; }
            public JObject Fields { get; internal set; }
        }
        readonly Dictionary<int,Entry[]> entries=new Dictionary<int,Entry[]>();
        readonly Dictionary<long,Scope> scopes=new Dictionary<long,Scope>();
        long highestGeneration;
        public int ActiveScopes=>scopes.Count;
        public event Action<Emission> Emitted;
        public event Action<long,string> Released;
        public SourceFrameEvents(string json,NativeControllerSource source,string controller)
        {
            var pack=JObject.Parse(json);var definition=source.GetController(controller);
            if((string)pack["schema"]!="remielle-main-action-events-v1"||(string)pack["controller"]!=controller||
                (int)pack["layer"]!=0||(string)pack["controllerRawSha256"]!=(string)definition["raw"]["sha256"]||
                !JToken.DeepEquals(pack["controllerIdentity"],definition["identity"]))throw new ArgumentException("Event source identity differs");
            foreach(var row in pack["states"])
            {
                int index=(int)row["state"];
                if((string)row["name"]!=(string)definition["machines"][0]["states"][index]["name"])throw new ArgumentException("Event state binding differs");
                entries.Add(index,row["entries"].Select(e=>new Entry{Index=(int)e["index"],Frame=(int)e["frame"],Max=(bool)e["maxFrame"],
                    ForceIn=(bool)e["forceIn"],ForceOut=(bool)e["forceOut"],Type=(string)e["type"],Fields=(JObject)e["fields"].DeepClone()}).ToArray());
            }
            if(entries.Count!=((JArray)definition["machines"][0]["states"]).Count)throw new ArgumentException("Incomplete event state set");
        }
        public void Observe(SourceActionNotice n)
        {
            if(!double.IsFinite(n.Frame)||n.Frame<0||!double.IsFinite(n.LengthFrames)||n.LengthFrames<=0)throw new ArgumentOutOfRangeException();
            if(n.Phase==SourceActionPhase.Enter)
            {
                if(n.Generation<=highestGeneration)throw new InvalidOperationException("Reused action generation");
                highestGeneration=n.Generation;
                var s=new Scope{State=n.State,Frame=n.Frame,Length=n.LengthFrames,Loop=n.Loop,LastCycle=entries[n.State].Select(_=>-1L).ToArray()};
                scopes.Add(n.Generation,s);
                long cycle=s.Loop?(long)Math.Floor(s.Frame/s.Length):0;
                var list=entries[s.State];
                foreach(var x in list.Select((e,i)=>(e,i)).OrderBy(x=>Due(x.e,s,cycle)).ThenBy(x=>x.e.Index))
                {
                    double due=Due(x.e,s,cycle);
                    // Do not move an authored future skill-start to frame zero.
                    // forceIn only catches entries skipped by an entry offset.
                    if(due==n.Frame||due<n.Frame&&x.e.ForceIn)Emit(n.Generation,s,x.i,cycle,"enter");
                }
                return;
            }
            if(!scopes.TryGetValue(n.Generation,out var scope)||scope.State!=n.State)throw new InvalidOperationException("Unknown action generation");
            if(n.Frame<scope.Frame||n.LengthFrames!=scope.Length||n.Loop!=scope.Loop)throw new InvalidOperationException("Action clock rewound or changed identity");
            if(n.Phase==SourceActionPhase.Sample&&!scope.Leaving)
            {
                var queue=new List<(double due,int index,long cycle)>();var list=entries[scope.State];
                for(int i=0;i<list.Length;i++)
                {
                    long first=scope.Loop?Math.Max(0,(long)Math.Floor(scope.Frame/scope.Length)-1):0;
                    long last=scope.Loop?(long)Math.Floor(n.Frame/scope.Length):0;
                    if(last-first>10000)throw new InvalidOperationException("Event catch-up exceeds supported update span");
                    for(long c=first;c<=last;c++)
                    {double due=Due(list[i],scope,c);if(due>scope.Frame&&due<=n.Frame&&c>scope.LastCycle[i])queue.Add((due,i,c));}
                }
                foreach(var x in queue.OrderBy(x=>x.due).ThenBy(x=>entries[scope.State][x.index].Index))Emit(n.Generation,scope,x.index,x.cycle,"frame");
            }
            if((n.Phase==SourceActionPhase.Leave||n.Phase==SourceActionPhase.Exit)&&!scope.Leaving)
            {
                long cycle=scope.Loop?(long)Math.Floor(n.Frame/scope.Length):0;
                var list=entries[scope.State];
                foreach(var x in list.Select((e,i)=>(e,i)).OrderBy(x=>Due(x.e,scope,cycle)).ThenBy(x=>x.e.Index))
                    if(x.e.ForceOut&&scope.LastCycle[x.i]<cycle)Emit(n.Generation,scope,x.i,cycle,"leave:"+n.Reason);
                scope.Leaving=true;Released?.Invoke(n.Generation,n.Reason);
            }
            scope.Frame=n.Frame;
            if(n.Phase==SourceActionPhase.Exit)scopes.Remove(n.Generation);
        }
        static double Due(Entry e,Scope s,long cycle)=>(e.Max?s.Length:e.Frame)+cycle*s.Length;
        void Emit(long generation,Scope scope,int index,long cycle,string reason)
        {
            if(scope.LastCycle[index]>=cycle)return;
            scope.LastCycle[index]=cycle;var e=entries[scope.State][index];
            Emitted?.Invoke(new Emission{Generation=generation,State=scope.State,Index=e.Index,Cycle=cycle,
                AuthoredFrame=Due(e,scope,cycle),Type=e.Type,Reason=reason,Fields=(JObject)e.Fields.DeepClone()});
        }
    }
}
