using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using Remielle.ControllerRuntime;
using UnityEditor;
using UnityEngine;

public static class NativeControllerPoseHistoryAudit
{
    const string Out="E:/ZZZ/local-only/RemielleControllerImplementation/20260906/";
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
        for(int g=0;g<4;g++)for(int i=0;i<(int)counts[g];i++)bindings.Add(new JObject{["typeID"]=g<3?"Transform":"SkinnedMeshRenderer",
            ["path"]=i+1,["attribute"]=g+1,["customType"]=0,["isPPtrCurve"]=0,["isIntCurve"]=0,["script"]=new JObject{["m_FileID"]=0,["m_PathID"]=0}});
        var layout=new NativePoseBindingLayout(bindings);Equal(new JArray(layout.Counts()),counts,"Layer layout counts");return layout;
    }
    static NativePoseStream Stream(JToken row,NativePoseBindingLayout layout)
    {
        var p=row["pose"];return new NativePoseStream(new NativePoseData(p[0].Select(Float).ToArray(),p[1].Select(Float).ToArray(),p[2].Select(Float).ToArray(),
            p[3].Select(Float).ToArray(),p[4].Select(x=>Convert.ToUInt32((string)x,16)).ToArray()),row["mask"].Select(r=>r.Select(x=>(byte)x).ToArray()).ToArray(),(byte)row["streamFlag"],bindingLayout:layout);
    }
    static JObject PoseSnapshot(NativePoseStream stream,byte[] root)
    {
        var p=stream.Pose();var rows=new JArray();for(int i=0;i<4;i++)rows.Add(new JArray(p.FloatChannel(i).Select(Bits)));
        rows.Add(new JArray(p.DiscreteChannel().Select(v=>v.ToString("x8"))));return new JObject{["pose"]=rows,["mask"]=new JArray(stream.Mask().Select(r=>new JArray(r.Select(v=>(int)v)))),
            ["root"]=NativeMotionArchive.Hex(root),["streamFlag"]=stream.StreamFlag};
    }
    static JObject Snapshot(NativeControllerPoseHistory history,NativeMotionSetGraph[] layers)=>new JObject{
        ["ownerFlag101"]=history.OwnerFlag101,
        ["nodes"]=new JArray(layers.SelectMany((m,li)=>m.Nodes.Select(n=>new JObject{["name"]=li+"|"+n.Name,["flag100"]=n.Flag100}))),
        ["poses"]=new JArray(layers.SelectMany((m,li)=>m.Nodes.Where(n=>n.Pose!=null).Select(n=>new JObject{
            ["name"]=li+"|"+n.Name,["pending"]=n.Pose.MustReadPreviousPose,["cache"]=PoseSnapshot(n.Pose.CachedPose,n.Pose.CachedRootState)})))};
    public static JObject Run(string path,NativeControllerSource source)
    {
        var data=JObject.Parse(File.ReadAllText(path));var e=data["evidence"];
        foreach(string k in new[]{"source","sourcePack","bank","generator","oracle","layerVectors"})Require(Hash((string)e[k]["path"])==(string)e[k]["sha256"],"Controller pose history source changed: "+k);
        foreach(var r in e["dependencies"])Require(Hash((string)r["path"])==(string)r["sha256"],"Controller pose history dependency changed");
        var bank=new NativeMotionBank(Path.GetDirectoryName((string)e["bank"]["path"]));int sourceSteps=0,fixtureSteps=0,readPasses=0,captures=0,nodeVisits=0,identityChecks=0,sourceLayers=0,sourceMachines=0;
        for(int repeat=0;repeat<2;repeat++)foreach(bool fixture in new[]{false,true})foreach(var group in data[fixture?"fixtures":"groups"])
        {
            NativeStateGraphBindings[] machines;NativeMotionSetGraph[] layers;NativeControllerPoseHistory history;NativeControllerGraphBindings controller=null;
            if(fixture)
            {
                machines=group["capacities"].Select(c=>new NativeStateGraphBindings(new JObject{["motionSetCount"]=1,["states"]=new JArray()},new[]{(int)c},_=>null)).ToArray();
                layers=machines.Select(m=>m.MotionSets[0]).ToArray();history=new NativeControllerPoseHistory(layers.Select(l=>new NativeControllerPoseInput(l.Root,null,null)).ToArray());
            }
            else
            {
                controller=new NativeControllerGraphBindings(source,bank,(string)group["source"]);machines=controller.Machines.ToArray();layers=controller.Layers.Select(l=>l.MotionGraph).ToArray();
                Equal(new JArray(controller.Layers.Select(l=>new JArray(l.MachineIndex,l.MotionSetIndex))),group["mapping"],"Source controller layer order");
                foreach(var l in controller.Layers)Require(ReferenceEquals(l.MotionGraph,machines[l.MachineIndex].MotionSets[l.MotionSetIndex]),"Synchronized layer copied a motion graph");
                history=controller.CreatePoseHistory(new NativePoseStream[layers.Length],new byte[layers.Length][]);sourceLayers+=layers.Length;sourceMachines+=machines.Length;
            }
            var layout=Layout(group["initialPose"]["counts"]);var nodes=layers.SelectMany(l=>l.Nodes).ToArray();var poses=nodes.Where(n=>n.Pose!=null).ToArray();
            var states=machines.Select(_=>new NativeLayerTransitionState()).ToArray();var flags=group["flags"];var weights=group["weights"];
            foreach(var m in machines)m.GraphContext.FlagsA0=(byte)flags["graphFlags"];
            foreach(var layer in layers)
            {
                layer.Flag16c=(byte)flags["flag16c"];layer.CacheNode.Pose.ApplyFootIK=(int)flags["footIK"]!=0;
                for(int i=0;i<3;i++)layer.Root.Inputs[i].Weight=Weight(weights[i]);
                foreach(var pair in new[]{(layer.FirstMixer,0),(layer.SecondMixer,1)})for(int i=0;i<pair.Item1.Inputs.Count;i++)pair.Item1.Inputs[i].Weight=Weight(weights[(i+pair.Item2)%3]);
            }
            foreach(var n in nodes){n.Flags38=(uint)flags["nodeFlags"];n.Flag100=(byte)flags["flag100"];n.Flag102=(byte)flags["flag102"];n.Flag103=(byte)flags["flag103"];if(n.Pose!=null)n.Pose.PrepareResources(layout);}
            var resources=poses.Select(n=>n.Pose.PreparedResources).ToArray();history.OwnerInputCount=1;history.OwnerFlag101=0xa5;
            Equal(Snapshot(history,layers),group["initial"],"Initial controller history");int step=0;
            foreach(var s in group["steps"])
            {
                string op=(string)s["op"];
                switch(op)
                {
                    case "request":foreach(var n in poses)n.Pose.RequestPreviousPose();break;
                    case "owner":
                        history.ControllerStateAvailable=(bool)s["available"];history.OwnerInputCount=(ulong)s["inputCount"];history.OwnerFlag101=(byte)s["flag"];
                        var holes=new HashSet<int>(s["holes"].Values<int>());for(int i=0;i<layers.Length;i++)history.Inputs[i].Node=holes.Contains(i)?null:layers[i].Root;break;
                    case "publish":
                        for(int i=0;i<layers.Length;i++){var row=s["frames"][i];history.Inputs[i].PreviousPose=row.Type==JTokenType.Null?null:Stream(row,layout);history.Inputs[i].PreviousRoot=row.Type==JTokenType.Null?null:Bytes(row["root"]);}break;
                    case "interrupt":
                        for(int i=0;i<machines.Length;i++){machines[i].TransitionContext.RequestCachedCurrent=true;machines[i].TransitionContext.FootIK=(bool)s["footIK"];NativeGraphPoseSchedule.ScheduleInterruption(machines[i],states[i],Double(s["timestampBits"]));}break;
                    case "finish":for(int i=0;i<machines.Length;i++){states[i].EndTransitionMarker=true;machines[i].ConsumeTransitionCompletion(states[i],Double(s["timestampBits"]));}break;
                    case "read":
                        Func<JArray> previous=()=>new JArray(history.Inputs.Select(i=>i.PreviousPose==null?(JToken)JValue.CreateNull():PoseSnapshot(i.PreviousPose,i.PreviousRoot)));
                        var before=previous();var visits=new List<string>();var selected=s["selected"].Values<int>().ToArray();
                        int copied=history.ReadPreviousPoses((bool)s["prepareTransforms"],selected[0],selected[1],selected[2],(li,n)=>visits.Add(li+"|"+n.Name));
                        Equal(new JObject{["snapshot"]=Snapshot(history,layers),["visits"]=new JArray(visits),["captures"]=copied,["previousPreserved"]=JToken.DeepEquals(before,previous())},s["expected"],"Controller read "+step);
                        readPasses++;captures+=copied;nodeVisits+=visits.Count;break;
                    default:throw new Exception("Unknown controller pose operation "+op);
                }
                if(op!="read")Equal(Snapshot(history,layers),s["expected"],op+" "+step);
                for(int i=0;i<poses.Length;i++){Require(ReferenceEquals(resources[i],poses[i].Pose.PreparedResources),"Layer cache allocation replaced");identityChecks++;}
                if(fixture)fixtureSteps++;else sourceSteps++;step++;
            }
        }
        foreach(var pair in new[]{("sourceSteps",sourceSteps),("fixtureSteps",fixtureSteps),("readPasses",readPasses),("captures",captures),("nodeVisits",nodeVisits),("sourceLayers",sourceLayers),("sourceMachines",sourceMachines)})
            Require(pair.Item2==2*(int)e[pair.Item1],"Controller pose history coverage "+pair.Item1);
        return new JObject{["sourceControllers"]=(int)e["sourceControllers"],["sourceLayers"]=sourceLayers/2,["sourceMachines"]=sourceMachines/2,["emptySourceMachines"]=(int)e["emptySourceMachines"],
            ["sourceSteps"]=sourceSteps/2,["fixtureSteps"]=fixtureSteps/2,["readPasses"]=readPasses/2,["captures"]=captures/2,["nodeVisits"]=nodeVisits/2,["resourceIdentityChecks"]=identityChecks/2,["repeats"]=2,
            ["sourceLayerOrderAndSharedGraphIdentityEqual"]=true,["fullOwnerGateAndFlagLifecycleEqual"]=true,["distinctLayerPreviousPoseRoutingEqual"]=true,["nullLayersRetainArrayIndices"]=true,
            ["recursiveCallbacksAndCacheRewiringConnected"]=true,["previousInputsPreserved"]=true,["resourceIdentityPreserved"]=true,
            ["nativeOwnerGraphAllocationQualified"]=false,["ownerStateResourceProducerQualified"]=false,["aggregatePosePreparationQualified"]=false,["previousWholeFrameProducerQualified"]=false,["wholeFrameEvaluationQualified"]=false,["controllerPlayable"]=false,
            ["vectorsSha256"]=Hash(path),["runtimeSha256"]=Hash("Assets/ControllerRuntime/NativeControllerPoseHistory.cs"),["stateGraphSha256"]=Hash("Assets/ControllerRuntime/NativeStateGraphBindings.cs"),
            ["scheduleSha256"]=Hash("Assets/ControllerRuntime/NativeGraphPoseSchedule.cs"),["graphLifecycleSha256"]=Hash("Assets/ControllerRuntime/NativeGraphLifecycle.cs"),
            ["defaultPoseSha256"]=Hash("Assets/ControllerRuntime/NativeDefaultPoseInput.cs"),["auditSha256"]=Hash("Assets/ControllerRuntime/Editor/NativeControllerPoseHistoryAudit.cs")};
    }
    public static void RunStandalone()
    {
        var report=new JObject{["pass"]=false};
        try{report["controllerPoseHistory"]=Run(Out+"native-controller-pose-history-vectors.json",new NativeControllerSource(File.ReadAllText("Assets/ControllerRuntime/Data/source-controller-pack.json")));report["pass"]=true;Debug.Log("NATIVE_CONTROLLER_POSE_HISTORY_OK "+report.ToString(Newtonsoft.Json.Formatting.None));}
        catch(Exception ex){report["error"]=ex.ToString();Debug.LogException(ex);}
        finally{File.WriteAllText(Out+"unity-controller-pose-history-verification.json",report.ToString());}
        if(!(bool)report["pass"])EditorApplication.Exit(1);
    }
}
