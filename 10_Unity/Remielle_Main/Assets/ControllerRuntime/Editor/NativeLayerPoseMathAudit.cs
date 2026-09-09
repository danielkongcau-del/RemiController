using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using Remielle.ControllerRuntime;
using UnityEditor;
using UnityEngine;

public static class NativeLayerPoseMathAudit
{
    const string Out="E:/ZZZ/local-only/RemielleControllerImplementation/20260906/";
    static void Require(bool ok,string why){if(!ok)throw new Exception(why);}
    public static float Float(JToken v)=>BitConverter.Int32BitsToSingle(unchecked((int)Convert.ToUInt32((string)v,16)));
    public static string Bits(float v)=>unchecked((uint)BitConverter.SingleToInt32Bits(v)).ToString("x8");
    public static string Hash(string path)
    {using(var s=SHA256.Create())using(var f=File.OpenRead(path))return BitConverter.ToString(s.ComputeHash(f)).Replace("-","").ToLowerInvariant();}
    public static byte[][] Mask(JToken value)=>value.Select(r=>r.Select(v=>(byte)v).ToArray()).ToArray();
    public static NativePoseData Pose(JToken v)=>v==null||v.Type==JTokenType.Null?null:new NativePoseData(
        v[0].Select(Float).ToArray(),v[1].Select(Float).ToArray(),v[2].Select(Float).ToArray(),v[3].Select(Float).ToArray(),v[4].Select(x=>Convert.ToUInt32((string)x,16)).ToArray());
    public static JArray WritePose(NativePoseData p)=>new JArray(Enumerable.Range(0,4)
        .Select(g=>(JToken)new JArray(p.FloatChannel(g).Select(Bits))).Append(new JArray(p.DiscreteChannel().Select(v=>v.ToString("x8")))));
    public static JObject Write(NativePoseStream stream)=>new JObject{
        ["pose"]=WritePose(stream.Pose()),["mask"]=new JArray(stream.Mask().Select(r=>new JArray(r.Select(v=>(int)v))))};
    public static void Equal(JToken actual,JToken expected,string where)
    {Require(JToken.DeepEquals(actual,expected),where+" actual="+actual.ToString(Newtonsoft.Json.Formatting.None)+" expected="+expected.ToString(Newtonsoft.Json.Formatting.None));}

    public static JObject Run(string path)
    {
        var data=JObject.Parse(File.ReadAllText(path));var e=data["evidence"];
        foreach(string key in new[]{"source","sourcePack","generator","oracle","nativeSource","nativeBuild","nativeDll","nativeBuildProof"})
            Require(Hash((string)e[key]["path"])==(string)e[key]["sha256"],"Layer pose input changed: "+key);
        foreach(var r in e["dependencies"])Require(Hash((string)r["path"])==(string)r["sha256"],"Layer pose dependency changed");
        const string dll="Assets/Plugins/x86_64/RemielleLayerPose.dll";
        Require(Hash(dll)==(string)e["nativeDll"]["sha256"]&&NativeLayerPoseMath.Abi()==1,"Unqualified layer pose library");
        int groups=0,cases=0,quaternionValues=0;
        for(int repeat=0;repeat<2;repeat++)foreach(var group in data["groups"])
        {
            var output=new NativePoseStream(Pose(group["initial"]),Mask(group["initialMask"]),17,23,29);
            foreach(var row in group["steps"])
            {
                var source=new NativePoseStream(Pose(row["source"]),Mask(row["mask"]),31,37,41);var defaults=Pose(row["defaults"]);
                NativeLayerPoseMath.Compose(defaults,source,Float(row["weightBits"]),(bool)row["additive"],output);
                Equal(Write(output),row["expected"],"Original layer math step "+cases);
                Equal(Write(source),new JObject{["pose"]=row["source"].DeepClone(),["mask"]=row["mask"].DeepClone()},"Layer source preserved");
                Equal(WritePose(defaults),row["defaults"],"Layer defaults preserved");
                Require(output.StreamFlag==17&&output.ReferenceFlag0==23&&output.ReferenceFlag1==29&&
                    source.StreamFlag==31&&source.ReferenceFlag0==37&&source.ReferenceFlag1==41,"Layer math changed stream flags");
                cases++;quaternionValues+=output.Pose().ChannelCounts()[1]*4;
            }
            groups++;
        }
        Require(groups==2*(int)e["groups"]&&cases==2*(int)e["cases"]&&quaternionValues==2*(int)e["quaternionValues"],"Layer math coverage differs");
        return new JObject{["groups"]=groups/2,["cases"]=cases/2,["quaternionValues"]=quaternionValues/2,["repeats"]=2,
            ["hostOriginalLayerMathBitwiseEqual"]=true,["overrideAndAdditiveQualified"]=true,["discreteThresholdAndMaskClearingQualified"]=true,
            ["sourceDefaultsAndFlagsPreserved"]=true,["hostEmulatorQuaternionValueDifferences"]=e["hostEmulatorQuaternionValueDifferences"].DeepClone(),
            ["aggregatePoseCallbackQualified"]=false,["rootAndAdditionalPoseQualified"]=false,["controllerPlayable"]=false,
            ["vectorsSha256"]=Hash(path),["runtimeSha256"]=Hash("Assets/ControllerRuntime/NativeLayerPoseMath.cs"),
            ["auditSha256"]=Hash("Assets/ControllerRuntime/Editor/NativeLayerPoseMathAudit.cs"),["runtimeDllSha256"]=Hash(dll),["runtimeDllMetaSha256"]=Hash(dll+".meta")};
    }
    public static void RunBatch()
    {
        var result=new JObject{["pass"]=false};
        try{result["layerPoseMath"]=Run(Out+"native-layer-pose-math-vectors.json");result["pass"]=true;Debug.Log("NATIVE_LAYER_POSE_MATH_AUDIT_OK "+result.ToString(Newtonsoft.Json.Formatting.None));}
        catch(Exception ex){result["error"]=ex.ToString();Debug.LogException(ex);}
        finally{File.WriteAllText(Out+"unity-layer-pose-math-verification.json",result.ToString());}
        if(!(bool)result["pass"])EditorApplication.Exit(1);
    }
}
