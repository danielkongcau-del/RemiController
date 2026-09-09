using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using Remielle.ControllerRuntime;
using UnityEditor;
using UnityEngine;

public static class NativeStateOutputWeightsAudit
{
    const string Out = "E:/ZZZ/ZCode/90_Builds/ControllerImplementation/20260906/";
    static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
    static float Float(JToken bits) => BitConverter.Int32BitsToSingle(unchecked((int)Convert.ToUInt32((string)bits,16)));
    static double Double(JToken bits) => BitConverter.Int64BitsToDouble(unchecked((long)Convert.ToUInt64((string)bits,16)));
    static string Bits(float v) => unchecked((uint)BitConverter.SingleToInt32Bits(v)).ToString("x8");
    static string Bits(double v) => unchecked((ulong)BitConverter.DoubleToInt64Bits(v)).ToString("x16");
    static string Hash(string path)
    { using(var sha=SHA256.Create()) using(var stream=File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant(); }
    static void Equal(JToken actual, JToken expected, string where)
    { Require(JToken.DeepEquals(actual,expected),where+" actual="+actual.ToString(Newtonsoft.Json.Formatting.None)+" expected="+expected.ToString(Newtonsoft.Json.Formatting.None)); }
    static NativeMixerInputWeight Read(JToken r) => new NativeMixerInputWeight(Float(r["inputBits"]),Float(r["startBits"]),Double(r["timestampBits"]),Double(r["startTimeBits"]),Float(r["rateBits"]));
    static JObject Write(NativeMixerInputWeight r) => new JObject {
        ["inputBits"]=Bits(r.Input),["startBits"]=Bits(r.Start),["timestampBits"]=Bits(r.Timestamp),["startTimeBits"]=Bits(r.StartTime),["rateBits"]=Bits(r.Rate) };
    static NativeStateInputWeights[] Sets(JToken sets) => sets.Select(s=>new NativeStateInputWeights(
        s["currentBits"].Type==JTokenType.Null?null:s["currentBits"].Select(Float).ToArray(),
        s["nextBits"].Type==JTokenType.Null?null:s["nextBits"].Select(Float).ToArray(),(bool)s["mixerFlag16c"])).ToArray();
    static NativeStateOutputWeights Create(JToken sets) => new NativeStateOutputWeights(sets.Select(s=>s["initial"].Select(Read).ToArray()).ToArray());
    static JObject Write(NativeStateOutputWeights r) => new JObject {
        ["outputWeightBits"]=new JArray(r.OutputWeights().Select(Bits)),
        ["inputs"]=new JArray(Enumerable.Range(0,r.MotionSetCount).Select(i=>new JArray(Enumerable.Range(0,2).Select(j=>Write(r.GetInput(i,j)))))) };

    public static JObject Run(string path, NativeControllerSource source)
    {
        var data=JObject.Parse(File.ReadAllText(path));var evidence=data["evidence"];
        foreach(string key in new[]{"sourcePack","clockVectors","layerVectors","source"})
            Require(Hash((string)evidence[key]["path"])==(string)evidence[key]["sha256"],"State output source changed: "+key);
        int fixtureSteps=0,setterSteps=0,progressCases=0,sourceQueries=0,empty=0;
        for(int repeat=0;repeat<2;repeat++)
        {
            foreach(var group in data["fixtures"])
            {
                var state=Create(group["sets"]);var sets=Sets(group["sets"]);bool flag=(bool)group["runtimeFlag80"];
                state.BeginOutputs(sets,flag);Equal(Write(state),group["beginExpected"],"Initial state output");
                foreach(var row in group["cases"])
                {
                    state.ApplyTransition(sets,flag,new NativeTransitionProgressResult(Float(row["previousBits"]),Float(row["weightBits"]),Float(row["rateBits"])),Double(row["timestampBits"]),Float(row["deltaBits"]));
                    Equal(Write(state),row["expected"],"Transition state output "+fixtureSteps);fixtureSteps++;
                }
            }
            foreach(var row in data["emptyMachines"])
            {
                var state=Create(row["sets"]);state.BeginOutputs(Sets(row["sets"]),true);state.ClearOutputs();
                Equal(Write(state),row["expected"],"Empty machine output");empty++;
            }
            var inputs=data["setterInitial"][0]["initial"].Select(Read).ToArray();
            foreach(var row in data["setterCases"])
            {
                NativeTimedMixerWeights.Set(inputs,(int)row["index"],Float(row["inputBits"]),Float(row["startBits"]),Float(row["rateBits"]),Double(row["timestampBits"]),Float(row["deltaBits"]));
                Equal(new JArray(inputs.Select(Write)),row["expected"],"Timed input setter "+setterSteps);setterSteps++;
            }
            foreach(var row in data["progressionCases"])
            {
                var input=row["clockInput"];var layer=NativeStateClockAudit.Read<NativeLayerTransitionState>(input["initialState"]);
                var progress=NativeTransitionProgress.Advance(layer,Float(input["deltaBits"]),Float(input["carryBits"]),(bool)input["suppressDelta"],(bool)input["priorFixed"]);
                var expected=input["expected"];
                Require(Bits(progress.PreviousWeight)==(string)expected["previousWeightBits"] && Bits(progress.Weight)==(string)expected["weightBits"] && Bits(progress.InverseDuration)==(string)expected["inverseDurationBits"],"Composed progress differs");
                var state=Create(data["progressionSets"]);var sets=Sets(data["progressionSets"]);
                state.BeginOutputs(sets,false);Equal(Write(state),row["beginExpected"],"Composed progress begin");
                state.ApplyTransition(sets,false,progress,4.1,.125f);Equal(Write(state),row["expected"],"Progress to output weights "+progressCases);progressCases++;
            }
        }
        var sourceIDs=new HashSet<string>();
        var layers=JObject.Parse(File.ReadAllText((string)evidence["layerVectors"]["path"]));
        var bank=new NativeMotionBank("Assets/StreamingAssets/RemielleControllerMotions");
        var intervals=new NativeMotionIntervals(File.ReadAllText("Assets/ControllerRuntime/Data/source-motion-time-ranges.json"),bank);
        foreach(var group in data["sourceGroups"])
        {
            var id=group["source"];string name=(string)id["controller"];int mi=(int)id["machine"],si=(int)id["state"];
            Require(sourceIDs.Add(name+"/"+mi+"/"+si),"Repeated source state");
            var c=source.GetController(name);var parameters=source.CreateParameters(name);
            var motions=new NativeStateMotions(source,bank,intervals,name,mi,si);
            var sourceLayer=layers["groups"].Single(g=>(string)g["controller"]==name);
            foreach(var row in group["cases"])
            {
                foreach(var p in ((JObject)row["values"]).Properties())parameters.SetFloat(uint.Parse(p.Name),Float(p.Value));
                var lw=new NativeLayerWeights(source,name,c["layers"].Select(l=>(float)l["weight"]).ToArray(),
                    sourceLayer["initial"].Select(i=>new NativeLayerInputWeight(Float(i["inputBits"]),Float(i["startBits"]),Double(i["timestampBits"]),0f,false)).ToArray());
                var trees=motions.Evaluate(parameters,lw).MotionSets;
                Equal(new JArray(trees.Select(t=>t==null?(JToken)JValue.CreateNull():NativeBlendTreeAudit.Serialize(t))),row["treeResults"],"Source tree input differs");
                var inputSets=new List<NativeStateInputWeights>();
                for(int ti=0;ti<trees.Count;ti++)
                {
                    var t=trees[ti];float[] weights=null;
                    if(t!=null)
                    {
                        int count=t.Leaves.Count==0?0:t.Leaves.Max(l=>l.InputIndex)+1;
                        weights=new float[count+1];foreach(var leaf in t.Leaves)weights[leaf.InputIndex]=leaf.Weight;
                    }
                    Equal(weights==null?(JToken)JValue.CreateNull():new JArray(weights.Select(Bits)),row["sets"][ti]["currentBits"],"Source graph fixture differs");
                    inputSets.Add(new NativeStateInputWeights(weights,weights,false));
                }
                var state=Create(row["sets"]);state.BeginOutputs(inputSets,false);Equal(Write(state),row["beginExpected"],"Source state output differs");
                var outputs=row["stateOutputWeightBits"].Select(r=>r.Select(Float).ToArray()).ToArray();outputs[mi]=state.OutputWeights();
                lw.ApplyPoseWeights(outputs,4.1);
                var actual=new JArray(Enumerable.Range(0,lw.LayerCount).Select(i=>{
                    var v=lw.GetInput(i);return new JObject{["inputBits"]=Bits(v.Input),["startBits"]=Bits(v.Start),["timestampBits"]=Bits(v.Timestamp),["metadataBits"]=Bits(v.Metadata),["additive"]=v.Additive};}));
                Equal(actual,row["layerExpected"],"Source output to layer weights differs");sourceQueries++;
            }
        }
        var expectedIDs=source.Names.SelectMany(n=>source.GetController(n)["machines"].SelectMany(m=>m["states"].Select(s=>n+"/"+(int)m["index"]+"/"+(int)s["state"])));
        Require(sourceIDs.SetEquals(expectedIDs)&&sourceIDs.Count==253,"Source state coverage differs");
        Require(fixtureSteps==2*(int)evidence["fixtureSteps"]&&setterSteps==2*(int)evidence["setterSteps"]&&progressCases==2*(int)evidence["progressCases"]&&sourceQueries==(int)evidence["sourceQueries"]&&empty==2*(int)evidence["emptyMachineCases"],"State output coverage differs");
        return new JObject {
            ["fixtureGroups"]=((JArray)data["fixtures"]).Count,["fixtureSteps"]=fixtureSteps/2,["setterSteps"]=setterSteps/2,["progressCases"]=progressCases/2,["emptyMachineCases"]=empty/2,["repeats"]=2,
            ["sourceStates"]=sourceIDs.Count,["sourceQueries"]=sourceQueries,["stateOutputAndTimedHistoryBitwiseEqual"]=true,["progressToOutputWeightsComposed"]=true,["sourceTreeToOutputToLayerComposed"]=true,
            ["graphAndRuntimeFlagProducersQualified"]=false,["timedHistoryEvaluationQualified"]=false,["fullTickOrderingVerified"]=false,["poseMixingVerified"]=false,["controllerPlayable"]=false,
            ["vectorsSha256"]=Hash(path),["runtimeSha256"]=Hash("Assets/ControllerRuntime/NativeStateOutputWeights.cs"),["auditSha256"]=Hash("Assets/ControllerRuntime/Editor/NativeStateOutputWeightsAudit.cs"),
            ["layerRuntimeSha256"]=Hash("Assets/ControllerRuntime/NativeLayerWeights.cs"),["clockRuntimeSha256"]=Hash("Assets/ControllerRuntime/NativeStateClock.cs"),["stateMotionsRuntimeSha256"]=Hash("Assets/ControllerRuntime/NativeStateMotions.cs"),["blendRuntimeSha256"]=Hash("Assets/ControllerRuntime/NativeBlendTree.cs") };
    }
    public static void RunBatch()
    {
        var report=new JObject{["pass"]=false};
        try {
            report["stateOutputWeights"]=Run(Out+"native-state-output-vectors.json",new NativeControllerSource(File.ReadAllText("Assets/ControllerRuntime/Data/source-controller-pack.json")));
            report["pass"]=true;Debug.Log("NATIVE_STATE_OUTPUT_WEIGHTS_AUDIT_OK "+report.ToString(Newtonsoft.Json.Formatting.None));
        }
        catch(Exception ex){report["error"]=ex.ToString();Debug.LogException(ex);}
        finally{File.WriteAllText(Out+"unity-state-output-verification.json",report.ToString());}
        if(!(bool)report["pass"])EditorApplication.Exit(1);
    }
}
