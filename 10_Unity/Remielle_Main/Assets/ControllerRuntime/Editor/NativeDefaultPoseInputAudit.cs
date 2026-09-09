using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using Remielle.ControllerRuntime;
using UnityEngine;

public static class NativeDefaultPoseInputAudit
{
    const string Out="E:/ZZZ/local-only/RemielleControllerImplementation/20260906/";
    static void Require(bool ok,string why){if(!ok)throw new Exception(why);}
    static float Float(JToken t)=>BitConverter.Int32BitsToSingle(unchecked((int)Convert.ToUInt32((string)t,16)));
    static double Double(JToken t)=>BitConverter.Int64BitsToDouble(unchecked((long)Convert.ToUInt64((string)t,16)));
    static string Bits(float v)=>unchecked((uint)BitConverter.SingleToInt32Bits(v)).ToString("x8");
    static string Bits(double v)=>unchecked((ulong)BitConverter.DoubleToInt64Bits(v)).ToString("x16");
    static byte[] Bytes(JToken t){string s=(string)t;return Enumerable.Range(0,s.Length/2).Select(i=>Convert.ToByte(s.Substring(i*2,2),16)).ToArray();}
    static string Hash(string path){using(var s=File.OpenRead(path))using(var h=SHA256.Create())return NativeMotionArchive.Hex(h.ComputeHash(s));}
    static void Equal(JToken actual,JToken expected,string where)
    {Require(JToken.DeepEquals(actual,expected),where+" differs; actual "+actual.ToString(Newtonsoft.Json.Formatting.None)+" expected "+expected.ToString(Newtonsoft.Json.Formatting.None));}
    static NativePoseData Pose(JToken rows)=>new NativePoseData(rows[0].Select(Float).ToArray(),rows[1].Select(Float).ToArray(),
        rows[2].Select(Float).ToArray(),rows[3].Select(Float).ToArray(),rows[4].Select(x=>Convert.ToUInt32((string)x,16)).ToArray());
    static NativePoseStream Stream(JToken item)=>new NativePoseStream(Pose(item["pose"]),item["mask"].Select(r=>r.Select(x=>(byte)x).ToArray()).ToArray(),(byte)item["streamFlag"]);
    static NativePoseStream Defaults(JToken rows)=>new NativePoseStream(Pose(rows),Pose(rows).ChannelCounts().Select(n=>new byte[n]).ToArray());
    static NativeMixerInputWeight Weight(JToken w)=>new NativeMixerInputWeight(Float(w["inputBits"]),Float(w["startBits"]),Double(w["timestampBits"]),Double(w["startTimeBits"]),Float(w["rateBits"]));
    static JObject WeightSnapshot(NativeMixerInputWeight w)=>new JObject{["inputBits"]=Bits(w.Input),["startBits"]=Bits(w.Start),["timestampBits"]=Bits(w.Timestamp),["startTimeBits"]=Bits(w.StartTime),["rateBits"]=Bits(w.Rate)};
    static JObject PolicySnapshot(NativeDefaultPoseInput node,bool enabled)=>new JObject{["enabled"]=enabled,["pending"]=node.MustReadPreviousPose,["weight"]=WeightSnapshot(node.Weight)};
    static JObject Snapshot(NativePoseStream stream,byte[] root)
    {
        var pose=stream.Pose();var values=new JArray();for(int g=0;g<4;g++)values.Add(new JArray(pose.FloatChannel(g).Select(Bits)));
        values.Add(new JArray(pose.DiscreteChannel().Select(v=>v.ToString("x8"))));
        return new JObject{["pose"]=values,["mask"]=new JArray(stream.Mask().Select(r=>new JArray(r.Select(v=>(int)v)))),
            ["root"]=NativeMotionArchive.Hex(root),["streamFlag"]=stream.StreamFlag};
    }
    public static JObject Run(string path,NativeControllerSource source)
    {
        var data=JObject.Parse(File.ReadAllText(path));var e=data["evidence"];
        foreach(string k in new[]{"source","sourcePack","generator","oracle","stateClipVectors"})Require(Hash((string)e[k]["path"])==(string)e[k]["sha256"],"Default pose source changed: "+k);
        foreach(var r in e["dependencies"])Require(Hash((string)r["path"])==(string)r["sha256"],"Default pose dependency changed");
        var bindingsData=JObject.Parse(File.ReadAllText((string)e["stateClipVectors"]["path"]));var bankRef=bindingsData["evidence"]["bank"];
        Require(Hash((string)bankRef["path"])==(string)bankRef["sha256"],"Default pose motion bank changed");
        var bank=new NativeMotionBank(Path.GetDirectoryName((string)bankRef["path"]));
        int steps=0,reads=0,captures=0,sourceSteps=0;
        for(int repeat=0;repeat<2;repeat++)
        {
            foreach(var g in data["groups"])
            {
                var node=new NativeDefaultPoseInput(Stream(g["initial"]),Bytes(g["initial"]["root"]),Weight(g["weight"]));
                Require(!node.MustReadPreviousPose&&!node.ReadDefaultPose&&!node.ApplyFootIK,"Original default constructor flags");
                foreach(var s in g["steps"])
                {
                    bool enabled=node.ApplyStatePolicy((bool)s["positive"],(bool)s["writeDefaults"],Double(s["timestampBits"]));
                    Equal(PolicySnapshot(node,enabled),s["expectedPolicy"],"Default input policy");
                    node.ReadDefaultPose=(bool)s["readDefaultPose"];node.ApplyFootIK=(bool)s["applyFootIK"];
                    node.NodeFlag100=1;
                    var selected=s["selected"].Select(x=>(int)x).ToArray();bool prepare=(bool)s["prepareTransforms"];
                    if((bool)s["readPrevious"])
                    {
                        if(node.ReadPreviousPose(Stream(s["previous"]),Bytes(s["previous"]["root"]),prepare,selected[0],selected[1],selected[2]))captures++;
                        reads++;
                    }
                    var output=Stream(s["output"]);output.ReferenceFlag0=(byte)s["flags"][0];output.ReferenceFlag1=(byte)s["flags"][1];
                    var defaults=Defaults(s["defaults"]);var ov=s["overrideDefaults"].Type==JTokenType.Null?null:Defaults(s["overrideDefaults"]);
                    node.Gather(defaults,ov,output,prepare,selected[0],selected[1],selected[2]);
                    var expected=s["expected"];
                    Equal(Snapshot(node.CachedPose,node.CachedRootState),expected["cache"],"Previous pose cache");
                    Equal(Snapshot(output,Bytes(s["output"]["root"])),expected["output"],"Default ordinary pose");
                    Equal(new JArray(output.ReferenceFlag0,output.ReferenceFlag1),expected["flags"],"Default pose reference flags");
                    Require(node.MustReadPreviousPose==(bool)expected["pending"],"Previous read lifecycle differs");steps++;
                    Require(node.NodeFlag100==(byte)expected["node100"],"Default leaf graph flag clearing differs");
                }
            }
            for(int gi=0;gi<data["sourceGroups"].Count();gi++)
            {
                var group=bindingsData["groups"][gi];var bindings=new NativeStateClipBindings((int)group["capacity"]);
                var node=new NativeDefaultPoseInput(Stream(data["groups"][0]["initial"]),new byte[NativeDefaultPoseInput.RootStorageSize],default);
                for(int si=0;si<group["steps"].Count();si++)
                {
                    var row=group["steps"][si];var audit=data["sourceGroups"][gi]["steps"][si];var locator=row["source"];
                    Equal(locator,audit["source"],"Source default policy identity");
                    string name=(string)locator["controller"];var c=source.GetController(name);
                    var state=(JObject)c["machines"][(int)locator["machine"]]["states"][(int)locator["state"]];
                    var tree=(int)locator["tree"]<0?null:(JObject)state["trees"][(int)locator["tree"]];
                    bindings.ApplySource(state,tree,name,bank);
                    bool enabled=node.DispatchState(bindings,row["weights"].Select(Float).ToArray(),new NativeStateClipFields(state),Double(audit["timestampBits"]));
                    Equal(PolicySnapshot(node,enabled),audit["expected"],"Source Clip dispatch to default input");sourceSteps++;
                }
            }
        }
        return new JObject{["cases"]=data["groups"].Count(),["steps"]=steps/2,["requestedReads"]=reads/2,["actualCaptures"]=captures/2,
            ["sourceSteps"]=sourceSteps/2,["repeats"]=2,["stateDefaultPolicyConnected"]=true,["ordinaryPoseAndMaskBitwiseEqual"]=true,
            ["previousPoseAndRootStorageBitwiseEqual"]=true,["leafGraphFlagClearingVerified"]=true,["rootStorageSize"]=NativeDefaultPoseInput.RootStorageSize,["context82"]=false,["completeDefaultNodeResourceBindingQualified"]=false,
            ["rootPreparationAndApplicationQualified"]=false,["wholeFrameEvaluationQualified"]=false,["controllerPlayable"]=false,
            ["vectorsSha256"]=Hash(path),["runtimeSha256"]=Hash(Application.dataPath+"/ControllerRuntime/NativeDefaultPoseInput.cs"),
            ["auditSha256"]=Hash(Application.dataPath+"/ControllerRuntime/Editor/NativeDefaultPoseInputAudit.cs")};
    }
    public static void RunStandalone()
    {
        var result=new JObject{["pass"]=false};
        try
        {
            var source=new NativeControllerSource(File.ReadAllText(Application.dataPath+"/ControllerRuntime/Data/source-controller-pack.json"));
            result["defaultPoseInput"]=Run(Out+"native-default-pose-input-vectors.json",source);result["pass"]=true;
            Debug.Log("NATIVE_DEFAULT_POSE_INPUT_UNITY_OK");
        }
        catch(Exception e){result["error"]=e.ToString();throw;}
        finally{File.WriteAllText(Out+"unity-default-pose-input-verification.json",result.ToString());}
    }
}
