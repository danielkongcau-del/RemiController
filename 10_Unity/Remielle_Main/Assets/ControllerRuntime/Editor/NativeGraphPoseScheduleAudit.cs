using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using Remielle.ControllerRuntime;
using UnityEditor;
using UnityEngine;

public static class NativeGraphPoseScheduleAudit
{
    const string Out="E:/ZZZ/ZCode/90_Builds/ControllerImplementation/20260906/";
    static void Require(bool ok,string why){if(!ok)throw new Exception(why);}
    static float Float(JToken t)=>BitConverter.Int32BitsToSingle(unchecked((int)Convert.ToUInt32((string)t,16)));
    static double Double(JToken t)=>BitConverter.Int64BitsToDouble(unchecked((long)Convert.ToUInt64((string)t,16)));
    static string Bits(float v)=>unchecked((uint)BitConverter.SingleToInt32Bits(v)).ToString("x8");
    static byte[] Bytes(JToken t){string s=(string)t;return Enumerable.Range(0,s.Length/2).Select(i=>Convert.ToByte(s.Substring(i*2,2),16)).ToArray();}
    static string Hash(string path){using(var s=File.OpenRead(path))using(var h=SHA256.Create())return NativeMotionArchive.Hex(h.ComputeHash(s));}
    static void Equal(JToken a,JToken b,string where){Require(JToken.DeepEquals(a,b),where+" actual="+a.ToString(Newtonsoft.Json.Formatting.None)+" expected="+b.ToString(Newtonsoft.Json.Formatting.None));}
    static NativeMixerInputWeight Weight(JToken w)=>new NativeMixerInputWeight(Float(w["inputBits"]),Float(w["startBits"]),Double(w["timestampBits"]),Double(w["startTimeBits"]),Float(w["rateBits"]));
    static NativePoseBindingLayout Layout(JToken counts)
    {
        var bindings=new List<JObject>();Require((int)counts[4]==0,"Current controller layouts contain no discrete bindings");
        for(int g=0;g<4;g++)for(int i=0;i<(int)counts[g];i++)bindings.Add(new JObject{
            ["typeID"]=g<3?"Transform":"SkinnedMeshRenderer",["path"]=i+1,["attribute"]=g+1,["customType"]=0,
            ["isPPtrCurve"]=0,["isIntCurve"]=0,["script"]=new JObject{["m_FileID"]=0,["m_PathID"]=0}});
        var layout=new NativePoseBindingLayout(bindings);Equal(new JArray(layout.Counts()),counts,"Layout counts");return layout;
    }
    static NativePoseStream Stream(JToken row,NativePoseBindingLayout layout)
    {
        var p=row["pose"];return new NativePoseStream(new NativePoseData(p[0].Select(Float).ToArray(),p[1].Select(Float).ToArray(),
            p[2].Select(Float).ToArray(),p[3].Select(Float).ToArray(),p[4].Select(x=>Convert.ToUInt32((string)x,16)).ToArray()),
            row["mask"].Select(r=>r.Select(x=>(byte)x).ToArray()).ToArray(),(byte)row["streamFlag"],bindingLayout:layout);
    }
    static JObject StreamSnapshot(NativePoseStream stream,byte[] root)
    {
        var p=stream.Pose();var rows=new JArray();for(int i=0;i<4;i++)rows.Add(new JArray(p.FloatChannel(i).Select(Bits)));
        rows.Add(new JArray(p.DiscreteChannel().Select(v=>v.ToString("x8"))));
        return new JObject{["pose"]=rows,["mask"]=new JArray(stream.Mask().Select(r=>new JArray(r.Select(v=>(int)v)))),
            ["root"]=NativeMotionArchive.Hex(root),["streamFlag"]=stream.StreamFlag};
    }
    static JObject Snapshot(NativeStateGraphBindings graph,NativeLayerTransitionState state)=>new JObject{
        ["topology"]=NativeGraphLifecycleAudit.Topology(graph),["flag80"]=state.GraphFlag80,["flag82"]=state.InterruptionActive,["eventCode"]=graph.TransitionContext.EventCode,
        ["poses"]=new JArray(graph.MotionSets.SelectMany(m=>m.Nodes).Where(n=>n.Pose!=null).Select(n=>new JObject{
            ["name"]=n.Name,["pending"]=n.Pose.MustReadPreviousPose,["cache"]=StreamSnapshot(n.Pose.CachedPose,n.Pose.CachedRootState)}))};
    public static JObject Run(string path,NativeControllerSource source)
    {
        var data=JObject.Parse(File.ReadAllText(path));var e=data["evidence"];
        foreach(string k in new[]{"source","sourcePack","bank","generator","oracle"})Require(Hash((string)e[k]["path"])==(string)e[k]["sha256"],"Graph pose schedule source changed: "+k);
        foreach(var r in e["dependencies"])Require(Hash((string)r["path"])==(string)r["sha256"],"Graph pose schedule dependency changed");
        var bank=new NativeMotionBank(Path.GetDirectoryName((string)e["bank"]["path"]));
        int sourceSteps=0,fixtureSteps=0,schedulingCalls=0,readPasses=0,captures=0,nodeVisits=0,resourceChecks=0;
        for(int repeat=0;repeat<2;repeat++)foreach(bool fixture in new[]{false,true})foreach(var group in data[fixture?"fixtures":"groups"])
        {
            NativeStateGraphBindings graph;
            if(fixture)graph=new NativeStateGraphBindings((JObject)group["machine"],group["capacities"].Values<int>().ToArray(),i=>(string)group["table"][i.ToString()]);
            else{var s=group["source"];graph=NativeStateGraphBindings.ForSource(source,bank,(string)s["controller"],(int)s["machine"]);}
            var nodes=graph.MotionSets.SelectMany(m=>m.Nodes).ToDictionary(n=>n.Name);
            var poses=nodes.Values.Where(n=>n.Pose!=null).ToArray();var layout=Layout(group["initialPose"]["counts"]);
            foreach(var n in poses)n.Pose.PrepareResources(layout);
            var resources=poses.Select(n=>n.Pose.PreparedResources).ToArray();
            var f=group["flags"];graph.GraphContext.FlagsA0=(byte)f["graphFlags"];
            foreach(var m in graph.MotionSets){m.Flag16c=(byte)f["flag16c"];m.CacheNode.Pose.ApplyFootIK=(int)f["footIK"]!=0;}
            foreach(var row in group["initial"]["topology"]["nodes"])
            {
                var n=nodes[(string)row["name"]];n.Flags38=(uint)row["nodeFlags"];n.Flag100=(byte)row["flag100"];n.Flag102=(byte)row["flag102"];n.Flag103=(byte)row["flag103"];
                for(int i=0;i<n.Inputs.Count;i++)n.Inputs[i].Weight=Weight(row["inputs"][i]["weight"]);
            }
            var runtime=new NativeLayerTransitionState{InterruptionActive=true};graph.TransitionContext.EventCode=0x55;
            Equal(Snapshot(graph,runtime),group["initial"],"Initial graph pose resources");
            int stepIndex=0;
            foreach(var s in group["steps"])
            {
                string op=(string)s["op"];JObject actual;
                switch(op)
                {
                    case "bind":runtime.CurrentState=(uint)s["current"];runtime.NextState=(uint)s["next"];graph.Bind(runtime,(bool)s["side"]);break;
                    case "weights":foreach(var r in s["rows"]){var n=nodes[(string)r["node"]];for(int i=0;i<n.Inputs.Count;i++)n.Inputs[i].Weight=Weight(r["weights"][i]);}break;
                    case "requestDefaults":foreach(var m in graph.MotionSets)foreach(var n in m.Nodes)if(n.Pose!=null&&!ReferenceEquals(n,m.CacheNode))n.Pose.RequestPreviousPose();break;
                    case "flag80":runtime.GraphFlag80=(byte)s["value"];break;
                    case "schedule":
                        graph.TransitionContext.RequestCachedCurrent=(bool)s["requested"];graph.TransitionContext.FootIK=(bool)s["footIK"];
                        Require(NativeGraphPoseSchedule.ScheduleInterruption(graph,runtime,Double(s["timestampBits"]))==(bool)s["requested"],"Decision gate return");schedulingCalls++;break;
                    case "read":
                        var previous=Stream(s["previous"],layout);var root=Bytes(s["previous"]["root"]);var before=StreamSnapshot(previous,root);
                        var visits=new List<string>();int readCount=0;var selected=s["selected"].Values<int>().ToArray();
                        foreach(var m in graph.MotionSets)readCount+=NativeGraphPoseSchedule.ReadPreviousPose(m.Root,previous,root,(bool)s["prepareTransforms"],selected[0],selected[1],selected[2],n=>visits.Add(n.Name));
                        actual=new JObject{["snapshot"]=Snapshot(graph,runtime),["visits"]=new JArray(visits),["captures"]=readCount,["previousPreserved"]=JToken.DeepEquals(before,StreamSnapshot(previous,root))};
                        Equal(actual,s["expected"],"Read "+stepIndex);readPasses++;captures+=readCount;nodeVisits+=visits.Count;break;
                    case "finish":runtime.EndTransitionMarker=(bool)s["marker"];graph.ConsumeTransitionCompletion(runtime,Double(s["timestampBits"]));break;
                    default:throw new Exception("Unknown graph pose schedule operation "+op);
                }
                if(op!="read")Equal(Snapshot(graph,runtime),s["expected"],op+" "+stepIndex);
                for(int i=0;i<poses.Length;i++){Require(ReferenceEquals(resources[i],poses[i].Pose.PreparedResources),"Graph pose resources were replaced");resourceChecks++;}
                if(fixture)fixtureSteps++;else sourceSteps++;stepIndex++;
            }
        }
        foreach(var pair in new[]{("sourceSteps",sourceSteps),("fixtureSteps",fixtureSteps),("schedulingCalls",schedulingCalls),("readPasses",readPasses),("captures",captures),("nodeVisits",nodeVisits)})
            Require(pair.Item2==2*(int)e[pair.Item1],"Graph pose schedule coverage "+pair.Item1);
        return new JObject{["sourceMachines"]=(int)e["sourceMachines"],["sourceStates"]=(int)e["sourceStates"],["sourceSteps"]=sourceSteps/2,["fixtureGroups"]=(int)e["fixtureGroups"],["fixtureSteps"]=fixtureSteps/2,
            ["schedulingCalls"]=schedulingCalls/2,["readPasses"]=readPasses/2,["captures"]=captures/2,["nodeVisits"]=nodeVisits/2,["resourceIdentityChecks"]=resourceChecks/2,["repeats"]=2,
            ["originalDecisionGateAndReentryEqual"]=true,["nonzeroAndUnorderedWeightsQualified"]=true,["recursivePreviousPoseCallbacksEqual"]=true,["zeroWeightInputsVisited"]=true,
            ["cacheDataMasksRootAndFlagsEqual"]=true,["pendingReadClearedAndRenewed"]=true,["sourceRebindingAndCompletionConnected"]=true,["resourceIdentityPreserved"]=true,
            ["upstreamInterruptionDecisionQualified"]=false,["previousWholeFrameProducerQualified"]=false,["wholeFrameEvaluationQualified"]=false,["controllerPlayable"]=false,
            ["vectorsSha256"]=Hash(path),["runtimeSha256"]=Hash("Assets/ControllerRuntime/NativeGraphPoseSchedule.cs"),["graphLifecycleSha256"]=Hash("Assets/ControllerRuntime/NativeGraphLifecycle.cs"),
            ["stateGraphSha256"]=Hash("Assets/ControllerRuntime/NativeStateGraphBindings.cs"),["defaultPoseSha256"]=Hash("Assets/ControllerRuntime/NativeDefaultPoseInput.cs"),
            ["topologyAuditSha256"]=Hash("Assets/ControllerRuntime/Editor/NativeGraphLifecycleAudit.cs"),["auditSha256"]=Hash("Assets/ControllerRuntime/Editor/NativeGraphPoseScheduleAudit.cs")};
    }
    public static void RunStandalone()
    {
        var report=new JObject{["pass"]=false};
        try{report["graphPoseSchedule"]=Run(Out+"native-graph-pose-schedule-vectors.json",new NativeControllerSource(File.ReadAllText("Assets/ControllerRuntime/Data/source-controller-pack.json")));report["pass"]=true;Debug.Log("NATIVE_GRAPH_POSE_SCHEDULE_OK "+report.ToString(Newtonsoft.Json.Formatting.None));}
        catch(Exception ex){report["error"]=ex.ToString();Debug.LogException(ex);}
        finally{File.WriteAllText(Out+"unity-graph-pose-schedule-verification.json",report.ToString());}
        if(!(bool)report["pass"])EditorApplication.Exit(1);
    }
}
