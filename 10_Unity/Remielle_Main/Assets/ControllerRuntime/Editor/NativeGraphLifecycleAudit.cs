using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using Remielle.ControllerRuntime;
using UnityEditor;
using UnityEngine;

public static class NativeGraphLifecycleAudit
{
    const string Out="E:/ZZZ/ZCode/90_Builds/ControllerImplementation/20260906/";
    static void Require(bool ok,string why){if(!ok)throw new Exception(why);}
    static float Float(JToken t)=>BitConverter.Int32BitsToSingle(unchecked((int)Convert.ToUInt32((string)t,16)));
    static double Double(JToken t)=>BitConverter.Int64BitsToDouble(unchecked((long)Convert.ToUInt64((string)t,16)));
    static string Bits(float v)=>unchecked((uint)BitConverter.SingleToInt32Bits(v)).ToString("x8");
    static string Bits(double v)=>unchecked((ulong)BitConverter.DoubleToInt64Bits(v)).ToString("x16");
    static string Hash(string path){using(var s=File.OpenRead(path))using(var h=SHA256.Create())return NativeMotionArchive.Hex(h.ComputeHash(s));}
    static void Equal(JToken a,JToken b,string why){Require(JToken.DeepEquals(a,b),why+": "+a.ToString(Newtonsoft.Json.Formatting.None));}
    static JToken Nullable(string v)=>v==null?JValue.CreateNull():new JValue(v);
    static NativeMixerInputWeight Weight(JToken w)=>new NativeMixerInputWeight(Float(w["inputBits"]),Float(w["startBits"]),Double(w["timestampBits"]),Double(w["startTimeBits"]),Float(w["rateBits"]));
    static JObject Weight(NativeMixerInputWeight w)=>new JObject{["inputBits"]=Bits(w.Input),["startBits"]=Bits(w.Start),["timestampBits"]=Bits(w.Timestamp),["startTimeBits"]=Bits(w.StartTime),["rateBits"]=Bits(w.Rate)};
    static JObject Clip(NativeStateClipBindings s)=>new JObject{
        ["boundLeafCount"]=s.BoundLeafCount,["ikOnFeet"]=s.IKOnFeet,["writeDefaultValues"]=s.WriteDefaultValues,["stateSpeedBits"]=Bits(s.StateSpeed),
        ["slots"]=new JArray(s.Slots.Select(v=>new JObject{["assetID"]=Nullable(v.AssetID),["nameID"]=v.NameID,["pathID"]=v.PathID,["fullPathID"]=v.FullPathID,["tagID"]=v.TagID,
            ["stateLoop"]=v.StateLoop,["dirty"]=v.BindingsDirty,["ready"]=v.ResourceReady,["ikOnFeet"]=v.IKOnFeet,["writeDefaultValues"]=v.WriteDefaultValues,["stateSpeedBits"]=Bits(v.StateSpeed),["samplingLoop"]=v.SamplingLoop}))};
    public static JObject Topology(NativeStateGraphBindings graph)=>new JObject{
        ["graphFlags"]=graph.GraphContext.FlagsA0,
        ["nodes"]=new JArray(graph.MotionSets.SelectMany(m=>m.Nodes).Select(n=>new JObject{
            ["name"]=n.Name,["nodeFlags"]=n.Flags38,["flag100"]=n.Flag100,["flag102"]=n.Flag102,["flag103"]=n.Flag103,
            ["inputs"]=new JArray(n.Inputs.Select(i=>new JObject{["node"]=Nullable(i.Node?.Name),["outputPort"]=i.OutputPort,["weight"]=Weight(i.Weight)})),
            ["parents"]=new JArray(n.Outputs.Select(n=>Nullable(n?.Name)))})),
        ["cachePorts"]=new JArray(graph.MotionSets.Select(m=>m.CachePort)),["outerFlags"]=new JArray(graph.MotionSets.Select(m=>m.Flag16c)),
        ["cacheFootIK"]=new JArray(graph.MotionSets.Select(m=>m.CacheNode.Pose.ApplyFootIK?1:0)),
        ["bindings"]=new JArray(graph.MotionSets.Select(m=>new JArray(Clip(m.FirstMixer.Clips),Clip(m.SecondMixer.Clips))))};
    static NativeLayerTransitionState State(JToken token)
    {
        var state=new NativeLayerTransitionState();
        foreach(var field in typeof(NativeLayerTransitionState).GetFields())
        {
            string key=char.ToLowerInvariant(field.Name[0])+field.Name.Substring(1);var value=token[key+(field.FieldType==typeof(float)?"Bits":"")];
            if(value==null)continue;
            field.SetValue(state,field.FieldType==typeof(float)?(object)Float(value):value.ToObject(field.FieldType));
        }
        state.GraphFlag80=(byte?)token["graphFlag80"]??0;return state;
    }
    static JObject State(NativeLayerTransitionState state,JObject expected)
    {
        var result=new JObject();
        foreach(var item in expected.Properties())
        {
            bool floating=item.Name.EndsWith("Bits");string key=floating?item.Name.Substring(0,item.Name.Length-4):item.Name;
            string name=char.ToUpperInvariant(key[0])+key.Substring(1);var value=typeof(NativeLayerTransitionState).GetField(name).GetValue(state);
            result[item.Name]=floating?new JValue(Bits((float)value)):JToken.FromObject(value);
        }
        return result;
    }
    static string PoseDigest(NativeDefaultPoseInput node)
    {
        using(var bytes=new MemoryStream())using(var h=SHA256.Create())
        {
            byte[] root=node.CachedRootState;bytes.Write(root,0,root.Length);var s=node.CachedPose;var pose=s.Pose();
            for(int g=0;g<4;g++){var values=pose.FloatChannel(g);var raw=new byte[values.Length*4];Buffer.BlockCopy(values,0,raw,0,raw.Length);bytes.Write(raw,0,raw.Length);}
            foreach(var mask in s.Mask())bytes.Write(mask,0,mask.Length);
            return NativeMotionArchive.Hex(h.ComputeHash(bytes.ToArray()));
        }
    }
    static void FillCache(NativeDefaultPoseInput node,int seed)
    {
        var bindings=new List<JObject>();
        for(int a=1;a<=4;a++)bindings.Add(new JObject{["typeID"]=a<4?"Transform":"SkinnedMeshRenderer",["path"]=1,["attribute"]=a,["customType"]=0,
            ["isPPtrCurve"]=0,["isIntCurve"]=0,["script"]=new JObject{["m_FileID"]=0,["m_PathID"]=0}});
        var layout=new NativePoseBindingLayout(bindings);node.PrepareResources(layout);
        var p=new NativePoseData(new[]{1f+seed,2f,3f,0f},new[]{0f,0f,0f,1f},new[]{1f,1f,1f,1f},new[]{.25f+seed},Array.Empty<uint>());
        var source=new NativePoseStream(p,new[]{new byte[]{1},new byte[]{1},new byte[]{1},new byte[]{1},Array.Empty<byte>()},bindingLayout:layout);
        var root=Enumerable.Range(0,NativeDefaultPoseInput.RootStorageSize).Select(i=>(byte)(i*17+seed)).ToArray();
        node.ApplyStatePolicy(false,false,0d);node.ReadPreviousPose(source,root,false,-1,-1,-1);
    }
    public static JObject Run(string path,NativeControllerSource controllers)
    {
        var data=JObject.Parse(File.ReadAllText(path));var e=data["evidence"];
        foreach(string k in new[]{"source","sourcePack","bank","generator","oracle"})Require(Hash((string)e[k]["path"])==(string)e[k]["sha256"],"Graph lifecycle source changed: "+k);
        foreach(var r in e["dependencies"])Require(Hash((string)r["path"])==(string)r["sha256"],"Graph lifecycle dependency changed");
        var bank=new NativeMotionBank(Path.GetDirectoryName((string)e["bank"]["path"]));
        int sourceSteps=0,fixtureSteps=0,producers=0,captures=0,skips=0,cacheChecks=0,aliasChecks=0;
        for(int repeat=0;repeat<2;repeat++)foreach(bool fixture in new[]{false,true})foreach(var group in data[fixture?"fixtures":"groups"])
        {
            NativeStateGraphBindings graph;
            if(fixture)graph=new NativeStateGraphBindings((JObject)group["machine"],group["capacities"].Values<int>().ToArray(),i=>(string)group["table"][i.ToString()]);
            else{var s=group["source"];graph=NativeStateGraphBindings.ForSource(controllers,bank,(string)s["controller"],(int)s["machine"]);}
            var caches=graph.MotionSets.SelectMany(m=>m.Nodes).Where(n=>n.Pose!=null).ToArray();
            for(int i=0;i<caches.Length;i++)
            {
                FillCache(caches[i].Pose,i+1);var resource=caches[i].Pose.PreparedResources;
                caches[i].Pose.MarkResourcesDirty();Require(caches[i].Flag102==1,"Pose resource request did not mark graph node");
                caches[i].Pose.PrepareResources(null);Require(caches[i].Flag102==0 && ReferenceEquals(resource,caches[i].Pose.PreparedResources),"Pose ready state did not clear graph dirty or retain cache");
            }
            var resources=caches.Select(n=>n.Pose.PreparedResources).ToArray();var digests=caches.Select(n=>PoseDigest(n.Pose)).ToArray();
            var f=group["flags"];graph.GraphContext.FlagsA0=(byte)f["graphFlags"];
            foreach(var set in graph.MotionSets)
            {
                set.Flag16c=(byte)f["flag16c"];set.CacheNode.Pose.ApplyFootIK=(int)f["footIK"]!=0;
                foreach(var n in set.Nodes){n.Flags38=(uint)f["nodeFlags"];n.Flag100=(byte)f["flag100"];n.Flag102=(byte)f["flag102"];n.Flag103=(byte)f["flag103"];}
                for(int i=0;i<3;i++)set.Root.Inputs[i].Weight=Weight(group["weights"][i%group["weights"].Count()]);
                for(int side=0;side<2;side++)
                {
                    var mixer=side==0?set.FirstMixer:set.SecondMixer;
                    for(int i=0;i<mixer.Inputs.Count;i++)mixer.Inputs[i].Weight=Weight(group["weights"][(i+side)%group["weights"].Count()]);
                }
            }
            Equal(Topology(graph),group["initial"],"Initial connected graph differs");
            foreach(var row in group["steps"])
            {
                string op=(string)row["op"];JToken expected=row["expected"];
                switch(op)
                {
                    case "bind":graph.Bind(new NativeLayerTransitionState{CurrentState=(uint)row["currentState"],NextState=(uint)row["nextState"]},(bool)row["currentSide"]);break;
                    case "capture":graph.MotionSets[(int)row["motionSet"]].SelectCachedCurrentPose((bool)row["footIK"],(bool)row["flag16c"],Double(row["timestampBits"]));captures++;break;
                    case "prepare":
                        foreach(var set in graph.MotionSets)for(int side=0;side<2;side++)foreach(var clip in (side==0?set.FirstMixer:set.SecondMixer).Clips.Slots)
                            if(clip.AssetID!=null)clip.CompleteResourceBinding(side!=0,false,false);
                        break;
                    case "complete":
                    case "finish":
                        NativeLayerTransitionState state;
                        if(op=="complete")
                        {
                            state=State(row["initialState"]);Require(NativeTransitionProgress.TryComplete(state,out uint code),"Expected transition completion missing");
                            Equal(State(state,(JObject)row["producer"]["state"]),row["producer"]["state"],"Completion producer differs");
                            Require(state.GraphFlag80==(byte)row["producer"]["graphFlag80"] && code==(uint)row["producer"]["eventCode"],"Completion event or graph byte differs");producers++;
                        }
                        else state=new NativeLayerTransitionState{EndTransitionMarker=(bool)row["marker"],GraphFlag80=(byte)row["flag80"],InterruptionActive=(bool)row["flag82"]};
                        bool hadMarker=state.EndTransitionMarker;Require(graph.ConsumeTransitionCompletion(state,Double(row["timestampBits"]))==hadMarker,"Completion marker consumption differs");
                        Require(state.EndTransitionMarker==(bool)expected["marker"] && state.GraphFlag80==(byte)expected["flag80"] && state.InterruptionActive==(bool)expected["flag82"],"Distinct runtime graph flags differ");
                        if(!hadMarker)skips++;expected=expected["topology"];break;
                    default:throw new Exception("Unknown graph action "+op);
                }
                Equal(Topology(graph),expected,"Graph lifecycle "+op+" differs");
                for(int i=0;i<caches.Length;i++)
                {
                    Require(ReferenceEquals(resources[i],caches[i].Pose.PreparedResources) && PoseDigest(caches[i].Pose)==digests[i],"Graph handoff changed cached pose/root storage");cacheChecks++;
                    var input=caches[i].Outputs[0].Inputs.Single(x=>ReferenceEquals(x.Node,caches[i]));
                    Equal(Weight(caches[i].Pose.Weight),Weight(input.Weight),"Pose input weight disconnected after rewiring");aliasChecks++;
                }
                if(fixture)fixtureSteps++;else sourceSteps++;
            }
        }
        Require(sourceSteps==2*(int)e["sourceSteps"] && fixtureSteps==2*(int)e["fixtureSteps"] && producers==2*(int)e["completionProducerCases"] && captures==2*(int)e["captureCalls"] && skips==2*(int)e["skippedMarkers"],"Graph lifecycle coverage differs");
        return new JObject{["sourceMachines"]=(int)e["sourceMachines"],["sourceStates"]=(int)e["sourceStates"],["sourceSteps"]=sourceSteps/2,["fixtureSteps"]=fixtureSteps/2,
            ["completionProducerCases"]=producers/2,["captureCalls"]=captures/2,["skippedMarkers"]=skips/2,["cachePreservationChecks"]=cacheChecks/2,["poseWeightAliasChecks"]=aliasChecks/2,["repeats"]=2,
            ["originalConnectedGraphResultsEqual"]=true,["weightHistoryBitwiseEqual"]=true,["clipReleasePreservesMetadataAndReady"]=true,["connectedDirtyPropagationQualified"]=true,
            ["completionProducerAndConsumerConnected"]=true,["runtimeFlag80And82Distinct"]=true,["poseResourceIdentityAndContentPreserved"]=true,["poseResourceDirtySynchronizationQualified"]=true,["stateSelectionAfterPortExchangeQualified"]=true,
            ["engineGraphAllocationQualified"]=false,["growingAndInvalidConnectionPathsQualified"]=false,["cachedCurrentReentryQualified"]=false,["fullCacheCaptureSchedulingQualified"]=false,
            ["wholeFrameEvaluationQualified"]=false,["controllerPlayable"]=false,
            ["vectorsSha256"]=Hash(path),["runtimeSha256"]=Hash("Assets/ControllerRuntime/NativeGraphLifecycle.cs"),["stateGraphSha256"]=Hash("Assets/ControllerRuntime/NativeStateGraphBindings.cs"),
            ["clipBindingsSha256"]=Hash("Assets/ControllerRuntime/NativeStateClipBindings.cs"),["defaultPoseSha256"]=Hash("Assets/ControllerRuntime/NativeDefaultPoseInput.cs"),
            ["transitionStateSha256"]=Hash("Assets/ControllerRuntime/NativeTransitionSearch.cs"),["clockSha256"]=Hash("Assets/ControllerRuntime/NativeStateClock.cs"),["auditSha256"]=Hash("Assets/ControllerRuntime/Editor/NativeGraphLifecycleAudit.cs")};
    }
    public static void RunStandalone()
    {
        var report=new JObject{["pass"]=false};
        try{report["graphLifecycle"]=Run(Out+"native-graph-lifecycle-vectors.json",new NativeControllerSource(File.ReadAllText("Assets/ControllerRuntime/Data/source-controller-pack.json")));report["pass"]=true;Debug.Log("NATIVE_GRAPH_LIFECYCLE_OK "+report.ToString(Newtonsoft.Json.Formatting.None));}
        catch(Exception ex){report["error"]=ex.ToString();Debug.LogException(ex);}
        finally{File.WriteAllText(Out+"unity-graph-lifecycle-verification.json",report.ToString());}
        if(!(bool)report["pass"])EditorApplication.Exit(1);
    }
}
