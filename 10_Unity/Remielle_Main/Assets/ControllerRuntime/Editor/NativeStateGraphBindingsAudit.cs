using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using Remielle.ControllerRuntime;
using UnityEditor;
using UnityEngine;

public static class NativeStateGraphBindingsAudit
{
    const string Out="E:/ZZZ/local-only/RemielleControllerImplementation/20260906/";
    static void Require(bool ok,string why){if(!ok)throw new Exception(why);}
    static float Float(JToken t)=>BitConverter.Int32BitsToSingle(unchecked((int)Convert.ToUInt32((string)t,16)));
    static string Bits(float v)=>unchecked((uint)BitConverter.SingleToInt32Bits(v)).ToString("x8");
    static string Hash(string path){using(var s=File.OpenRead(path))using(var h=SHA256.Create())return NativeMotionArchive.Hex(h.ComputeHash(s));}
    static void Equal(JToken a,JToken b,string why){Require(JToken.DeepEquals(a,b),why+": "+a.ToString(Newtonsoft.Json.Formatting.None));}
    static JObject Snapshot(NativeStateClipBindings set)=>new JObject{
        ["boundLeafCount"]=set.BoundLeafCount,["ikOnFeet"]=set.IKOnFeet,["writeDefaultValues"]=set.WriteDefaultValues,["stateSpeedBits"]=Bits(set.StateSpeed),
        ["slots"]=new JArray(set.Slots.Select(s=>new JObject{["assetID"]=s.AssetID==null?JValue.CreateNull():new JValue(s.AssetID),["nameID"]=s.NameID,["pathID"]=s.PathID,["fullPathID"]=s.FullPathID,["tagID"]=s.TagID,
            ["stateLoop"]=s.StateLoop,["dirty"]=s.BindingsDirty,["ready"]=s.ResourceReady,["ikOnFeet"]=s.IKOnFeet,["writeDefaultValues"]=s.WriteDefaultValues,["stateSpeedBits"]=Bits(s.StateSpeed),["samplingLoop"]=s.SamplingLoop}))};
    static JArray Snapshot(NativeStateGraphBindings graph)=>new JArray(Enumerable.Range(0,graph.MotionSetCount).Select(i=>new JArray(Snapshot(graph.Current[i]),Snapshot(graph.Next[i]))));
    static NativeLayerTransitionState State(JToken s)=>new NativeLayerTransitionState{
        CurrentState=(uint)s["currentState"],NextState=(uint)s["nextState"],TransitionIndex=(int)s["transitionIndex"],TransitionSourceState=(int)s["transitionSourceState"],
        TraversalFlags=(uint)s["traversalFlags"],SourceDuration=Float(s["sourceDurationBits"]),TransitionStartTime=Float(s["transitionStartTimeBits"]),
        TransitionElapsed=Float(s["transitionElapsedBits"]),TransitionDuration=Float(s["transitionDurationBits"]),DestinationOffset=Float(s["destinationOffsetBits"]),
        InTransition=(bool)s["inTransition"],NeedsDestinationStart=(bool)s["needsDestinationStart"],FixedDuration=(bool)s["fixedDuration"]};
    static JObject State(NativeLayerTransitionState s)=>new JObject{
        ["currentState"]=s.CurrentState,["nextState"]=s.NextState,["transitionIndex"]=s.TransitionIndex,["transitionSourceState"]=s.TransitionSourceState,["traversalFlags"]=s.TraversalFlags,
        ["sourceDurationBits"]=Bits(s.SourceDuration),["transitionStartTimeBits"]=Bits(s.TransitionStartTime),["transitionElapsedBits"]=Bits(s.TransitionElapsed),["transitionDurationBits"]=Bits(s.TransitionDuration),
        ["destinationOffsetBits"]=Bits(s.DestinationOffset),["inTransition"]=s.InTransition,["needsDestinationStart"]=s.NeedsDestinationStart,["fixedDuration"]=s.FixedDuration};
    static void Bind(NativeStateGraphBindings graph,NativeLayerTransitionState state,JToken row)
    {
        Require(state.CurrentState==(uint)row["currentState"] && state.NextState==(uint)row["nextState"],"Producer state differs from original selector input");
        var before=State(state);graph.Bind(state,(bool)row["currentSide"]);
        Equal(State(state),before,"Selector modified transition state");Equal(Snapshot(graph),row["expected"]["graphs"],"Original state graph binding differs");
    }
    public static JObject Run(string path,NativeControllerSource source)
    {
        var data=JObject.Parse(File.ReadAllText(path));var e=data["evidence"];
        foreach(string k in new[]{"source","sourcePack","bank","commitVectors","generator","oracle"})
            Require(Hash((string)e[k]["path"])==(string)e[k]["sha256"],"State graph source changed: "+k);
        foreach(var r in e["dependencies"])Require(Hash((string)r["path"])==(string)r["sha256"],"State graph dependency changed");
        var bank=new NativeMotionBank(Path.GetDirectoryName((string)e["bank"]["path"]));
        var commits=JObject.Parse(File.ReadAllText((string)e["commitVectors"]["path"]));
        int selections=0,fixtures=0,joint=0,accepted=0,jointSelections=0,invalid=0;
        var seen=new System.Collections.Generic.HashSet<string>();
        for(int repeat=0;repeat<2;repeat++)
        {
            foreach(var group in data["groups"])
            {
                var origin=group["source"];var graph=NativeStateGraphBindings.ForSource(source,bank,(string)origin["controller"],(int)origin["machine"]);
                Equal(new JArray(graph.Current.Select(s=>s.Slots.Count)),group["capacities"],"Source capacity differs");
                var state=new NativeLayerTransitionState();
                foreach(var r in group["steps"])
                {
                    state.CurrentState=(uint)r["currentState"];state.NextState=(uint)r["nextState"];Bind(graph,state,r);selections++;
                    foreach(var s in graph.Current.Concat(graph.Next).SelectMany(g=>g.Slots))if(s.AssetID!=null)seen.Add(s.AssetID);
                }
                var snapshot=Snapshot(graph);state.CurrentState=uint.MaxValue;
                try{graph.Bind(state,true);throw new Exception("Invalid selected state accepted");}catch(ArgumentOutOfRangeException){invalid++;}
                Equal(Snapshot(graph),snapshot,"Invalid state changed cached graph");
            }
            foreach(var group in data["fixtures"])
            {
                var graph=new NativeStateGraphBindings((JObject)group["machine"],group["capacities"].Values<int>().ToArray(),i=>(string)group["table"][i.ToString()]);
                foreach(var r in group["steps"])
                {
                    var state=new NativeLayerTransitionState{CurrentState=(uint)r["currentState"],NextState=(uint)r["nextState"]};
                    Bind(graph,state,r);fixtures++;
                }
            }
            foreach(var j in data["joint"])
            {
                var group=commits["groups"][(int)j["group"]];var row=group["cases"][(int)j["case"]];var origin=group["source"];
                string controller=(string)origin["controller"];int machine=(int)origin["machine"];
                var graph=NativeStateGraphBindings.ForSource(source,bank,controller,machine);var state=State(row["initialState"]);
                Bind(graph,state,j["before"]);jointSelections++;
                var c=source.GetController(controller);var parameters=new NativeParameterBank((JArray)c["parameters"]);
                foreach(var p in c["parameters"])
                {
                    uint hash=(uint)p["hash"];var value=row["values"][hash.ToString()];
                    switch((int)p["kind"])
                    {
                        case 1:parameters.SetFloat(hash,(float)value);break;
                        case 3:parameters.SetInt(hash,(int)value);break;
                        case 4:parameters.SetBool(hash,(bool)value);break;
                        case 9:if((bool)value)parameters.SetTrigger(hash);else parameters.ResetTrigger(hash);break;
                    }
                }
                var cmd=row["initialCommand"];var command=new NativeTransitionStartCommand{Command=(int)cmd["command"],Offset=Float(cmd["offsetBits"]),OvershootSeconds=Float(cmd["overshootSecondsBits"]),OffsetIsFrames=(bool)cmd["offsetIsFrames"]};
                var search=source.CreateTransitionSearch(controller,machine,(int)origin["state"]);
                var result=search.EvaluateAndCommit(state,command,parameters,Float(row["previousBits"]),Float(row["currentBits"]),Float(row["directionBits"]),
                    (bool)row["loop"],new NativeTransitionTimingPolicy((bool)row["comparisonFix"],(bool)row["includePreviousBoundary"]),(int)row["sourceState"],(uint)row["eventCode"],row["used"].Values<uint>());
                Require(result.Accepted==(bool)row["expected"]["accepted"],"Joint commit acceptance differs");
                Equal(State(state),row["expected"]["state"],"Joint commit state differs");
                foreach(var after in j["after"]){Bind(graph,state,after);jointSelections++;}
                joint++;if(result.Accepted)accepted++;
            }
        }
        Require(selections==2*(int)e["sourceSelections"] && fixtures==2*(int)e["fixtureSelections"] && seen.Count==(int)e["sourceMotions"],"Source selection coverage differs");
        Require(joint==2*(int)e["jointCases"] && accepted==2*(int)e["jointAccepted"] && jointSelections==2*(int)e["jointSelections"],"Commit producer coverage differs");
        return new JObject{["controllers"]=(int)e["controllers"],["machines"]=(int)e["machines"],["sourceStates"]=(int)e["sourceStates"],["sourceMotions"]=seen.Count,
            ["sourceSelections"]=selections/2,["sourceBindingCalls"]=(int)e["sourceBindingCalls"],["sourceNullTreeCalls"]=(int)e["sourceNullTreeCalls"],["fixtureSelections"]=fixtures/2,
            ["jointCases"]=joint/2,["jointAccepted"]=accepted/2,["jointSelections"]=jointSelections/2,["invalidSelectedStatesRejected"]=invalid/2,["repeats"]=2,
            ["currentAndNextGraphsBitwiseEqual"]=true,["motionSetMappingPreserved"]=true,["originalProviderExecuted"]=true,["nullTreeHistoryPreserved"]=true,
            ["transitionCommitProducerConnected"]=true,["selectorPreservesRuntimeState"]=true,["sourceIdentityPreserved"]=true,
            ["nativeGraphAllocationQualified"]=false,["parentDirtyPropagationQualified"]=false,["sharedResourceTableProducerQualified"]=false,["portExchangeQualified"]=false,
            ["wholeFrameEvaluationQualified"]=false,["controllerPlayable"]=false,
            ["vectorsSha256"]=Hash(path),["runtimeSha256"]=Hash("Assets/ControllerRuntime/NativeStateGraphBindings.cs"),
            ["bindingRuntimeSha256"]=Hash("Assets/ControllerRuntime/NativeStateClipBindings.cs"),["transitionRuntimeSha256"]=Hash("Assets/ControllerRuntime/NativeTransitionSearch.cs"),
            ["sourceApiSha256"]=Hash("Assets/ControllerRuntime/NativeControllerSource.cs"),["auditSha256"]=Hash("Assets/ControllerRuntime/Editor/NativeStateGraphBindingsAudit.cs")};
    }
    public static void RunStandalone()
    {
        var report=new JObject{["pass"]=false};
        try
        {
            report["stateGraphBindings"]=Run(Out+"native-state-graph-bindings-vectors.json",new NativeControllerSource(File.ReadAllText("Assets/ControllerRuntime/Data/source-controller-pack.json")));
            report["pass"]=true;Debug.Log("NATIVE_STATE_GRAPH_BINDINGS_OK "+report.ToString(Newtonsoft.Json.Formatting.None));
        }
        catch(Exception ex){report["error"]=ex.ToString();Debug.LogException(ex);}
        finally{File.WriteAllText(Out+"unity-state-graph-bindings-verification.json",report.ToString());}
        if(!(bool)report["pass"])EditorApplication.Exit(1);
    }
}
