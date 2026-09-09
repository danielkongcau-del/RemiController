using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Remielle.ControllerRuntime;
using UnityEditor;
using UnityEngine;
using static NativeLayerPoseMathAudit;

public static class NativeLayerPoseMixerAudit
{
    const string Out="E:/ZZZ/ZCode/90_Builds/ControllerImplementation/20260906/";
    static void Require(bool ok,string why){if(!ok)throw new Exception(why);}
    static double Double(JToken t)=>BitConverter.Int64BitsToDouble(unchecked((long)Convert.ToUInt64((string)t,16)));
    static byte[] Bytes(JToken t){string s=(string)t;return Enumerable.Range(0,s.Length/2).Select(i=>Convert.ToByte(s.Substring(i*2,2),16)).ToArray();}
    static string Hex(byte[] b)=>BitConverter.ToString(b).Replace("-","").ToLowerInvariant();
    static NativeMixerLink Link(JToken v)=>new NativeMixerLink((int)v["node"],(int)v["port"]);
    static NativePoseStream Stream(JToken r,NativePoseBindingLayout layout)=>new NativePoseStream(Pose(r["pose"]),Mask(r["mask"]),
        (byte)r["streamFlag"],r["flags"]==null?(byte)0:(byte)r["flags"][0],r["flags"]==null?(byte)0:(byte)r["flags"][1],layout);
    static JObject Snapshot(NativePoseStream s,bool flags)
    {
        var result=Write(s);result["streamFlag"]=s.StreamFlag;
        if(flags)result["flags"]=new JArray(s.ReferenceFlag0,s.ReferenceFlag1);return result;
    }
    static NativePoseBindingLayout Layout(JToken counts)
    {
        Require((int)counts[4]==0,"Source joint fixture has no discrete binding");var bindings=new List<JObject>();
        for(int g=0;g<4;g++)for(int i=0;i<(int)counts[g];i++)bindings.Add(new JObject{
            ["typeID"]=g<3?"Transform":"SkinnedMeshRenderer",["path"]=i+1,["attribute"]=g+1,["customType"]=0,["isPPtrCurve"]=0,["isIntCurve"]=0,
            ["script"]=new JObject{["m_FileID"]=0,["m_PathID"]=0}});
        var layout=new NativePoseBindingLayout(bindings);Equal(new JArray(layout.Counts()),counts,"Source joint layout");return layout;
    }

    public static JObject Run(string path,NativeControllerSource source)
    {
        var data=JObject.Parse(File.ReadAllText(path));var e=data["evidence"];
        foreach(string key in new[]{"source","sourcePack","generator","oracle","mathOracle","mathVectors","layerVectors","bank"})
            Require(Hash((string)e[key]["path"])==(string)e[key]["sha256"],"Layer pose callback input changed: "+key);
        foreach(var r in e["dependencies"])Require(Hash((string)r["path"])==(string)r["sha256"],"Layer pose callback dependency changed");
        var bank=new NativeMotionBank(Path.GetDirectoryName((string)e["bank"]["path"]));
        int sourceSteps=0,fixtureSteps=0,childCalls=0,sourceLayers=0,historyCaptures=0,resourceChecks=0;
        for(int repeat=0;repeat<2;repeat++)foreach(bool synthetic in new[]{true,false})foreach(var group in data[synthetic?"fixtures":"sourceGroups"])
        {
            var first=group["steps"][0]["fixture"];int count=((JArray)group["initial"]["history"]).Count;
            var layout=synthetic?null:Layout(first["counts"]);var output=Stream(group["initial"]["output"],layout);
            var previous=group["initial"]["history"].Select(r=>Stream(r,layout)).ToArray();
            var mixer=new NativeLayerPoseMixer(previous,Pose(group["initial"]["scratch"]),
                first["layerMasks"].Select(m=>m.Type==JTokenType.Null?null:Mask(m)).ToArray(),first["bodyMasks"].Select(Bytes).ToArray());
            NativeControllerGraphBindings graphs=null;NativeLayerWeights weights=null;NativeControllerPoseHistory owner=null;
            NativeGraphNode[][] poseNodes=null;NativePoseResources[][] prepared=null;
            if(!synthetic)
            {
                string name=(string)group["controller"];graphs=new NativeControllerGraphBindings(source,bank,name);
                Equal(new JArray(graphs.Layers.Select(l=>new JArray(l.MachineIndex,l.MotionSetIndex))),group["layerMapping"],"Layer graph mapping");
                weights=new NativeLayerWeights(source,name,new float[count],group["initialLayerInputs"].Select(w=>new NativeLayerInputWeight(
                    Float(w["inputBits"]),Float(w["startBits"]),Double(w["timestampBits"]),0f,false)).ToArray());
                var c=source.GetController(name);
                for(int i=0;i<count;i++)
                {
                    var body=c["layers"][i]["bodyMask"].SelectMany(x=>BitConverter.GetBytes(Convert.ToUInt32((string)x,16))).ToArray();
                    Require(Hex(body)==(string)first["bodyMasks"][i],"Source layer body mask differs");
                }
                owner=graphs.CreatePoseHistory(mixer.History,Enumerable.Range(0,count).Select(_=>new byte[NativeDefaultPoseInput.RootStorageSize]).ToArray());
                owner.ControllerStateAvailable=true;owner.OwnerInputCount=1;
                poseNodes=graphs.Layers.Select(l=>l.MotionGraph.Nodes.Where(n=>n.Pose!=null).ToArray()).ToArray();
                foreach(var n in poseNodes.SelectMany(n=>n))n.Pose.PrepareResources(layout);
                prepared=poseNodes.Select(nodes=>nodes.Select(n=>n.Pose.PreparedResources).ToArray()).ToArray();
                sourceLayers+=count;
            }
            foreach(var row in group["steps"])
            {
                var f=row["fixture"];var g=row["graph"];
                Equal(f["layerMasks"],first["layerMasks"],"Prepared masks changed in fixture");Equal(f["bodyMasks"],first["bodyMasks"],"Prepared body masks changed in fixture");
                NativeLayerInputWeight[] inputs;
                if(synthetic)inputs=g["weights"].Select((w,i)=>new NativeLayerInputWeight(Float(w["inputBits"]),Float(w["startBits"]),Double(w["timestampBits"]),0f,(bool)g["additive"][i])).ToArray();
                else
                {
                    var p=row["layerProducer"];for(int i=0;i<count;i++)weights.SetWeight(i,Float(p["runtimeWeightBits"][i]));
                    weights.ApplyPoseWeights(p["stateOutputWeightBits"].Select(r=>r.Select(Float).ToArray()).ToArray(),Double(p["timestampBits"]));
                    inputs=Enumerable.Range(0,count).Select(weights.GetInput).ToArray();
                    Equal(new JArray(inputs.Select(w=>new JObject{["inputBits"]=Bits(w.Input),["startBits"]=Bits(w.Start),
                        ["timestampBits"]=unchecked((ulong)BitConverter.DoubleToInt64Bits(w.Timestamp)).ToString("x16"),["metadataBits"]=Bits(w.Metadata),["additive"]=w.Additive})),
                        p["expected"],"Original source layer weight producer");
                }
                var nodes=g["nodes"].Select(n=>new NativeMixerNode((uint)n["type"],Double(n["delayBits"]),n["links"].Select(Link).ToArray())).ToArray();
                var query=new NativeLayerPoseQuery(Bytes(f["queryPayload"]),new byte[12],Pose(f["overrideDefaults"]),null);
                var calls=new JArray();var defaults=Pose(f["defaults"]);
                int called=mixer.Evaluate(inputs,g["links"].Select(Link).ToArray(),nodes,defaults,query,output,(layer,node,q,target)=>{
                    calls.Add(new JObject{["node"]=node,["layer"]=layer,["target"]=ReferenceEquals(target,output)?"output":"history",
                        ["queryPayload"]=Hex(q.Payload()),["bodyMask"]=Hex(q.BodyMask()),["evaluationDefaults"]=WritePose(q.EvaluationDefaults),
                        ["beforeFlags"]=new JArray(target.ReferenceFlag0,target.ReferenceFlag1)});
                    Require(ReferenceEquals(q.OverrideDefaults,query.OverrideDefaults),"Query override reference replaced");
                    var s=f["sources"][node];target.WriteMasked((bool)s["useEvaluationDefaults"]?q.EvaluationDefaults:Pose(s["pose"]),
                        Mask(s["mask"]),(byte)s["streamFlag"],(byte)s["flags"][0],(byte)s["flags"][1]);
                });
                Equal(new JObject{["state"]=new JObject{["output"]=Snapshot(output,true),["history"]=new JArray(mixer.History.Select(h=>Snapshot(h,false))),
                    ["scratch"]=WritePose(mixer.Scratch())},["childCalls"]=calls},row["expected"],"Original aggregate ordinary pose "+(sourceSteps+fixtureSteps));
                Require(called==calls.Count,"Layer callback count differs");childCalls+=called;
                Require(Hex(query.Payload())==(string)f["queryPayload"],"Caller query mutated");Equal(WritePose(defaults),f["defaults"],"Source defaults mutated");
                if(query.OverrideDefaults!=null)Equal(WritePose(query.OverrideDefaults),f["overrideDefaults"],"Override defaults mutated");
                if(synthetic)fixtureSteps++;
                else
                {
                    // The aggregate's real retained streams become the next
                    // previous-pose inputs, including skipped layer histories.
                    foreach(var n in poseNodes.SelectMany(n=>n))n.Pose.RequestPreviousPose();
                    owner.OwnerFlag101=0xa5;int captured=owner.ReadPreviousPoses(false,-1,-1,-1);
                    Require(captured==poseNodes.Sum(n=>n.Length)&&owner.OwnerFlag101==0,"Produced history was not fully consumed");historyCaptures+=captured;
                    for(int i=0;i<count;i++)
                    {
                        Require(ReferenceEquals(owner.Inputs[i].PreviousPose,mixer.History[i])&&ReferenceEquals(previous[i],mixer.History[i]),"Layer history identity replaced");
                        for(int j=0;j<poseNodes[i].Length;j++)
                        {
                            var n=poseNodes[i][j];Equal(Write(n.Pose.CachedPose),Write(mixer.History[i]),"Aggregate history to original graph cache");
                            Require(!n.Pose.MustReadPreviousPose&&ReferenceEquals(prepared[i][j],n.Pose.PreparedResources),"Graph cache request or storage identity differs");resourceChecks++;
                        }
                    }
                    sourceSteps++;
                }
            }
        }
        foreach(var pair in new[]{("fixtureSteps",fixtureSteps),("sourceSteps",sourceSteps),("sourceLayers",sourceLayers),("childCalls",childCalls)})
            Require(pair.Item2==2*(int)e[pair.Item1],"Aggregate coverage differs: "+pair.Item1);
        return new JObject{["fixtureGroups"]=(int)e["fixtureGroups"],["fixtureSteps"]=fixtureSteps/2,["sourceControllers"]=(int)e["sourceControllers"],
            ["sourceLayers"]=sourceLayers/2,["sourceSteps"]=sourceSteps/2,["childCalls"]=childCalls/2,["historyCaptures"]=historyCaptures/2,["resourceIdentityChecks"]=resourceChecks/2,["repeats"]=2,
            ["fullOrdinaryLayerCallbackEqual"]=true,["singleAndMultipleHistoryRulesEqual"]=true,["queryDefaultsAndForwardedPortsEqual"]=true,
            ["sourceLayerWeightsConnected"]=true,["producedOrdinaryHistoriesConnectedToGraphRead"]=true,["context82"]=false,
            ["aggregateResourcePreparationQualified"]=false,["originalChildGraphPoseProducerQualified"]=false,["rootAndAdditionalPoseQualified"]=false,
            ["previousWholeFrameProducerQualified"]=false,["wholeFrameEvaluationQualified"]=false,["controllerPlayable"]=false,
            ["vectorsSha256"]=Hash(path),["runtimeSha256"]=Hash("Assets/ControllerRuntime/NativeLayerPoseMixer.cs"),["mathSha256"]=Hash("Assets/ControllerRuntime/NativeLayerPoseMath.cs"),
            ["weightsSha256"]=Hash("Assets/ControllerRuntime/NativeLayerWeights.cs"),["historySha256"]=Hash("Assets/ControllerRuntime/NativeControllerPoseHistory.cs"),
            ["scheduleSha256"]=Hash("Assets/ControllerRuntime/NativeGraphPoseSchedule.cs"),["defaultPoseSha256"]=Hash("Assets/ControllerRuntime/NativeDefaultPoseInput.cs"),
            ["auditSha256"]=Hash("Assets/ControllerRuntime/Editor/NativeLayerPoseMixerAudit.cs"),["mathAuditSha256"]=Hash("Assets/ControllerRuntime/Editor/NativeLayerPoseMathAudit.cs")};
    }
    public static void RunBatch()
    {
        var result=new JObject{["pass"]=false};
        try{result["layerPoseMixer"]=Run(Out+"native-layer-pose-mixer-vectors.json",new NativeControllerSource(File.ReadAllText("Assets/ControllerRuntime/Data/source-controller-pack.json")));result["pass"]=true;Debug.Log("NATIVE_LAYER_POSE_MIXER_AUDIT_OK "+result.ToString(Newtonsoft.Json.Formatting.None));}
        catch(Exception ex){result["error"]=ex.ToString();Debug.LogException(ex);}
        finally{File.WriteAllText(Out+"unity-layer-pose-mixer-verification.json",result.ToString());}
        if(!(bool)result["pass"])EditorApplication.Exit(1);
    }
}
