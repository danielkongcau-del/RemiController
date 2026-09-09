using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using Remielle.ControllerRuntime;
using UnityEditor;
using UnityEngine;

public static class NativePoseMathAudit
{
    const string Out="E:/ZZZ/ZCode/90_Builds/ControllerImplementation/20260906/";
    static void Require(bool ok,string why){if(!ok)throw new Exception(why);}
    static float Float(JToken x)=>BitConverter.Int32BitsToSingle(unchecked((int)Convert.ToUInt32((string)x,16)));
    static double Double(JToken x)=>BitConverter.Int64BitsToDouble(unchecked((long)Convert.ToUInt64((string)x,16)));
    static string Bits(float x)=>unchecked((uint)BitConverter.SingleToInt32Bits(x)).ToString("x8");
    static string Hash(string path)
    {using(var s=SHA256.Create())using(var f=File.OpenRead(path))return BitConverter.ToString(s.ComputeHash(f)).Replace("-","").ToLowerInvariant();}
    static byte[][] Mask(JToken r)=>r.Select(a=>a.Select(v=>(byte)v).ToArray()).ToArray();
    static float[][] Weights(JToken r)=>r.Select(a=>a.Select(Float).ToArray()).ToArray();
    static NativePoseData Pose(JToken r)=>r==null||r.Type==JTokenType.Null?null:new NativePoseData(r[0].Select(Float).ToArray(),r[1].Select(Float).ToArray(),r[2].Select(Float).ToArray(),r[3].Select(Float).ToArray(),r[4].Select(v=>Convert.ToUInt32((string)v,16)).ToArray());
    static JArray Write(NativePoseData p)=>new JArray(Enumerable.Range(0,4).Select(g=>(JToken)new JArray(p.FloatChannel(g).Select(Bits))).Append(new JArray(p.DiscreteChannel().Select(v=>v.ToString("x8")))));
    static JObject Write(NativePoseAccumulator a)=>new JObject{["pose"]=Write(a.Pose()),["mask"]=new JArray(a.Mask().Select(r=>new JArray(r.Select(v=>(int)v)))),["weights"]=new JArray(a.Weights().Select(r=>new JArray(r.Select(Bits))))};
    static void Equal(JToken a,JToken e,string where)
    {Require(JToken.DeepEquals(a,e),where+" actual="+a.ToString(Newtonsoft.Json.Formatting.None)+" expected="+e.ToString(Newtonsoft.Json.Formatting.None));}
    static NativeMixerInputWeight Weight(JToken r)=>new NativeMixerInputWeight(Float(r["inputBits"]),Float(r["startBits"]),Double(r["timestampBits"]),Double(r["startTimeBits"]),Float(r["rateBits"]));
    static NativeMixerLink Link(JToken r)=>new NativeMixerLink((int)r["node"],(int)r["port"]);

    public static JObject Run(string path)
    {
        var data=JObject.Parse(File.ReadAllText(path));var evidence=data["evidence"];
        foreach(string key in new[]{"source","sourcePack","oracle","mixerVectors","nativeSource","nativeBuild","nativeDll","nativeBuildProof"})
            Require(Hash((string)evidence[key]["path"])==(string)evidence[key]["sha256"],"Pose math input identity changed: "+key);
        const string dll="Assets/Plugins/x86_64/RemiellePoseMath.dll";
        Require(Hash(dll)==(string)evidence["nativeDll"]["sha256"]&&NativePoseMath.Abi()==1,"Unqualified runtime pose normalizer");
        var mixer=JObject.Parse(File.ReadAllText((string)evidence["mixerVectors"]["path"]));
        int norm=0,quats=0,groups=0,accumulations=0,joint=0;
        for(int repeat=0;repeat<2;repeat++)
        {
            foreach(var row in data["normalization"])
            {
                var values=row["inputBits"].Select(Float).ToArray();var mask=row["mask"].Select(v=>(byte)v).ToArray();
                NativePoseMath.Normalize(values,mask);Equal(new JArray(values.Select(Bits)),row["expected"],"Host SSE quaternion normalization "+norm);
                norm++;quats+=mask.Length;
            }
            foreach(var group in data["groups"])
            {
                var state=new NativePoseAccumulator(Pose(group["initial"]),Mask(group["initialMask"]),Weights(group["initialWeights"]));
                foreach(var row in group["steps"])
                {
                    state.Accumulate(Pose(row["source"]),Mask(row["mask"]),Float(row["weightBits"]));
                    Equal(Write(state),row["expected"],"Original masked pose accumulation "+accumulations);accumulations++;
                }
                state.Finish(Pose(group["defaults"]));Equal(Write(state),group["expected"],"Original default contribution and finalization "+groups);groups++;
            }
            var fixture=data["joint"];var poses=fixture["poses"].Select(Pose).ToArray();var masks=fixture["masks"].Select(Mask).ToArray();var defaults=Pose(fixture["defaults"]);
            foreach(var row in fixture["cases"])
            {
                var m=mixer["cases"][(int)row["mixerCase"]];
                var nodes=m["nodes"].Select(n=>new NativeMixerNode((uint)n["type"],Double(n["delayBits"]),n["links"].Select(Link).ToArray())).ToArray();
                var selection=NativeMixerEvaluation.Evaluate(m["weights"].Select(Weight).ToArray(),m["links"].Select(Link).ToArray(),nodes,Float(m["offsetBits"]),Double(m["timestampBits"]),Float(m["deltaBits"]));
                Require(selection.Dispatch==NativeMixerDispatch.Blend,"Pose math joint case did not select the blend path");
                Equal(new JArray(selection.Contributions.Select(c=>new JObject{["node"]=c.Node,["weightBits"]=Bits(c.Weight)})),m["expected"]["0xcc5fc0"]["contributions"],"Joint selected pose inputs differ");
                var counts=defaults.ChannelCounts();var weightCounts=counts.Take(3).Concat(new[]{counts[4],counts[3]});
                var state=new NativePoseAccumulator(defaults,counts.Select(n=>new byte[n]).ToArray(),weightCounts.Select(n=>new float[n]).ToArray());
                foreach(var c in selection.Contributions)state.Accumulate(poses[c.Node],masks[c.Node],c.Weight);
                state.Finish(defaults);Equal(Write(state),row["expected"],"Mixer selection to masked pose result "+joint);joint++;
            }
        }
        Require(norm==2*(int)evidence["normalizationCases"]&&quats==2*(int)evidence["normalizationQuaternions"]&&groups==2*(int)evidence["poseGroups"]&&accumulations==2*(int)evidence["poseAccumulations"]&&joint==2*(int)evidence["jointMixerCases"],"Pose math coverage differs");
        return new JObject{
            ["normalizationCases"]=norm/2,["normalizationQuaternions"]=quats/2,["poseGroups"]=groups/2,["poseAccumulations"]=accumulations/2,["jointMixerCases"]=joint/2,["repeats"]=2,
            ["hostOriginalPoseMathBitwiseEqual"]=true,["maskedPoseAndDefaultContributionQualified"]=true,["mixerSelectionToPoseMathComposed"]=true,
            ["hostEmulatorQuaternionValueDifferences"]=evidence["hostEmulatorQuaternionValueDifferences"].DeepClone(),
            ["sourcePoseBindingAndMaskProducersQualified"]=false,["completePoseCallbacksVerified"]=false,["layerAndRootMotionMixingVerified"]=false,["controllerPlayable"]=false,
            ["vectorsSha256"]=Hash(path),["runtimeSha256"]=Hash("Assets/ControllerRuntime/NativePoseMath.cs"),["auditSha256"]=Hash("Assets/ControllerRuntime/Editor/NativePoseMathAudit.cs"),
            ["runtimeDllSha256"]=Hash(dll),["runtimeDllMetaSha256"]=Hash(dll+".meta"),["mixerRuntimeSha256"]=Hash("Assets/ControllerRuntime/NativeMixerEvaluation.cs")};
    }
    public static void RunBatch()
    {
        var result=new JObject{["pass"]=false};
        try{result["poseMath"]=Run(Out+"native-pose-math-vectors.json");result["pass"]=true;Debug.Log("NATIVE_POSE_MATH_AUDIT_OK "+result.ToString(Newtonsoft.Json.Formatting.None));}
        catch(Exception ex){result["error"]=ex.ToString();Debug.LogException(ex);}
        finally{File.WriteAllText(Out+"unity-pose-math-verification.json",result.ToString());}
        if(!(bool)result["pass"])EditorApplication.Exit(1);
    }
}
