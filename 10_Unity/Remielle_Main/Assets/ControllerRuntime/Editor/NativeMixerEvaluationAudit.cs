using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using Remielle.ControllerRuntime;
using UnityEditor;
using UnityEngine;

public static class NativeMixerEvaluationAudit
{
    const string Out="E:/ZZZ/local-only/RemielleControllerImplementation/20260906/";
    static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
    static float Float(JToken b)=>BitConverter.Int32BitsToSingle(unchecked((int)Convert.ToUInt32((string)b,16)));
    static double Double(JToken b)=>BitConverter.Int64BitsToDouble(unchecked((long)Convert.ToUInt64((string)b,16)));
    static string Bits(float v)=>unchecked((uint)BitConverter.SingleToInt32Bits(v)).ToString("x8");
    static string Hash(string path)
    {using(var sha=SHA256.Create())using(var f=File.OpenRead(path))return BitConverter.ToString(sha.ComputeHash(f)).Replace("-","").ToLowerInvariant();}
    static NativeMixerInputWeight Weight(JToken r)=>new NativeMixerInputWeight(Float(r["inputBits"]),Float(r["startBits"]),Double(r["timestampBits"]),Double(r["startTimeBits"]),Float(r["rateBits"]));
    static NativeMixerLink Link(JToken r)=>new NativeMixerLink((int)r["node"],(int)r["port"]);
    static NativeMixerNode[] Nodes(JToken r)=>r.Select(n=>new NativeMixerNode((uint)n["type"],Double(n["delayBits"]),n["links"].Select(Link).ToArray())).ToArray();
    static NativeStateInputWeights[] Sets(JToken r)=>r.Select(s=>new NativeStateInputWeights(
        s["currentBits"].Type==JTokenType.Null?null:s["currentBits"].Select(Float).ToArray(),
        s["nextBits"].Type==JTokenType.Null?null:s["nextBits"].Select(Float).ToArray(),(bool)s["mixerFlag16c"])).ToArray();
    static JObject Write(NativeMixerEvaluationResult r)=>new JObject{
        ["interpolate"]=r.Interpolate,["extrapolate"]=r.Extrapolate,["dispatch"]=r.Dispatch.ToString().ToLowerInvariant(),
        ["contributions"]=new JArray(r.Contributions.Select(c=>new JObject{["node"]=c.Node,["weightBits"]=Bits(c.Weight)}))};
    static void Equal(NativeMixerEvaluationResult actual,JToken expectations,string where)
    {
        foreach(string wrapper in new[]{"0xcc5fc0","0xcc7de0"})
        {
            var expected=(JObject)expectations[wrapper].DeepClone();uint rva=(uint)expected["callbackRva"];expected.Remove("callbackRva");
            uint callback=actual.Dispatch==NativeMixerDispatch.Empty?(wrapper=="0xcc5fc0"?0xcc61b0u:0xcc7eb0u):
                actual.Dispatch==NativeMixerDispatch.Single?(wrapper=="0xcc5fc0"?0xc8bc40u:0xc8bc38u):(wrapper=="0xcc5fc0"?0xcc62a0u:0xcc8030u);
            Require(callback==rva,where+" original wrapper dispatch differs");
            var serialized=Write(actual);
            Require(JToken.DeepEquals(serialized,expected),where+" actual="+serialized.ToString(Newtonsoft.Json.Formatting.None)+" expected="+expected.ToString(Newtonsoft.Json.Formatting.None));
        }
    }
    public static JObject Run(string path)
    {
        var data=JObject.Parse(File.ReadAllText(path));var evidence=data["evidence"];
        foreach(string key in new[]{"source","sourcePack","stateOutputVectors"})
            Require(Hash((string)evidence[key]["path"])==(string)evidence[key]["sha256"],"Mixer evaluation source changed: "+key);
        int fixtures=0,jointSteps=0,jointEvaluations=0;
        for(int repeat=0;repeat<2;repeat++)
        {
            foreach(var row in data["cases"])
            {
                var weights=row["weights"].Select(Weight).ToArray();
                var actual=NativeMixerEvaluation.Evaluate(weights,row["links"].Select(Link).ToArray(),Nodes(row["nodes"]),Float(row["offsetBits"]),Double(row["timestampBits"]),Float(row["deltaBits"]));
                Equal(actual,row["expected"],"Mixer evaluation "+fixtures);fixtures++;
                Require(weights.SequenceEqual(row["weights"].Select(Weight)),"Weight history mutated during evaluation");
            }
            var nodes=Nodes(data["jointNodes"]);var links=new[]{new NativeMixerLink(0,0),new NativeMixerLink(2,0)};
            foreach(var group in data["jointGroups"])
            {
                var state=new NativeStateOutputWeights(group["sets"].Select(s=>s["initial"].Select(Weight).ToArray()).ToArray());
                var sets=Sets(group["sets"]);bool flag=(bool)group["runtimeFlag80"];state.BeginOutputs(sets,flag);
                foreach(var row in group["cases"])
                {
                    var input=row["input"];
                    state.ApplyTransition(sets,flag,new NativeTransitionProgressResult(Float(input["previousBits"]),Float(input["weightBits"]),Float(input["rateBits"])),Double(input["timestampBits"]),Float(input["deltaBits"]));
                    foreach(var eval in row["evaluations"])
                    {
                        int si=(int)eval["set"];var weights=new[]{state.GetInput(si,0),state.GetInput(si,1)};
                        var actual=NativeMixerEvaluation.Evaluate(weights,links,nodes,Float(eval["offsetBits"]),Double(eval["timestampBits"]),Float(eval["deltaBits"]));
                        Equal(actual,eval["expected"],"State output to actual mixer inputs "+jointEvaluations);jointEvaluations++;
                    }
                    jointSteps++;
                }
            }
        }
        Require(fixtures==2*(int)evidence["fixtureCases"]&&jointSteps==2*(int)evidence["jointSteps"]&&jointEvaluations==2*(int)evidence["jointEvaluations"],"Mixer evaluation coverage differs");
        return new JObject{
            ["fixtureCases"]=fixtures/2,["jointGroups"]=((JArray)data["jointGroups"]).Count,["jointSteps"]=jointSteps/2,["jointEvaluations"]=jointEvaluations/2,["wrappers"]=2,["repeats"]=2,
            ["timedHistoryEvaluationQualified"]=true,["stateOutputToMixerEvaluationComposed"]=true,["weightsAndInputSelectionBitwiseEqual"]=true,["originalWrapperDispatchVerified"]=true,
            ["graphAndEvaluationInputProducersQualified"]=false,["poseCallbacksExecuted"]=false,["poseMixingVerified"]=false,["controllerPlayable"]=false,
            ["vectorsSha256"]=Hash(path),["runtimeSha256"]=Hash("Assets/ControllerRuntime/NativeMixerEvaluation.cs"),["auditSha256"]=Hash("Assets/ControllerRuntime/Editor/NativeMixerEvaluationAudit.cs"),
            ["stateOutputRuntimeSha256"]=Hash("Assets/ControllerRuntime/NativeStateOutputWeights.cs")};
    }
    public static void RunBatch()
    {
        var result=new JObject{["pass"]=false};
        try{result["mixerEvaluation"]=Run(Out+"native-mixer-evaluation-vectors.json");result["pass"]=true;Debug.Log("NATIVE_MIXER_EVALUATION_AUDIT_OK "+result.ToString(Newtonsoft.Json.Formatting.None));}
        catch(Exception ex){result["error"]=ex.ToString();Debug.LogException(ex);}
        finally{File.WriteAllText(Out+"unity-mixer-evaluation-verification.json",result.ToString());}
        if(!(bool)result["pass"])EditorApplication.Exit(1);
    }
}
