using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using Remielle.ControllerRuntime;
using UnityEditor;
using UnityEngine;

public static class NativeLayerWeightsAudit
{
    const string Out = "E:/ZZZ/ZCode/90_Builds/ControllerImplementation/20260906/";
    static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
    static float Float(JToken bits) => BitConverter.Int32BitsToSingle(unchecked((int)Convert.ToUInt32((string)bits, 16)));
    static double Double(JToken bits) => BitConverter.Int64BitsToDouble(unchecked((long)Convert.ToUInt64((string)bits, 16)));
    static string Bits(float value) => unchecked((uint)BitConverter.SingleToInt32Bits(value)).ToString("x8");
    static string Bits(double value) => unchecked((ulong)BitConverter.DoubleToInt64Bits(value)).ToString("x16");
    static string Hash(string path)
    {
        using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path))
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }
    static NativeLayerInputWeight[] Initial(JToken g) => g["initial"].Select(v =>
        new NativeLayerInputWeight(Float(v["inputBits"]), Float(v["startBits"]), Double(v["timestampBits"]), 0f, false)).ToArray();
    static JObject Serialize(NativeLayerInputWeight value) => new JObject {
        ["inputBits"] = Bits(value.Input), ["startBits"] = Bits(value.Start), ["timestampBits"] = Bits(value.Timestamp),
        ["metadataBits"] = Bits(value.Metadata), ["additive"] = value.Additive };
    static void Equal(JToken actual, JToken expected, string where)
    {
        Require(JToken.DeepEquals(actual, expected), where + " actual=" + actual.ToString(Newtonsoft.Json.Formatting.None) +
            " expected=" + expected.ToString(Newtonsoft.Json.Formatting.None));
    }
    public static JObject Run(string path, NativeControllerSource source)
    {
        var vectors = JObject.Parse(File.ReadAllText(path)); var evidence = vectors["evidence"];
        foreach (string key in new[] { "sourcePack", "motionBank", "motionIntervals", "jointVectors", "blendVectors" })
            Require(Hash((string)evidence[key]["path"]) == (string)evidence[key]["sha256"], "Layer input identity changed: " + key);
        string packText = File.ReadAllText((string)evidence["sourcePack"]["path"]);
        int cases = 0, applications = 0, fieldLayers = 0, maskEntries = 0, jointCases = 0;
        var fieldIDs = new HashSet<string>();
        foreach (var row in vectors["fields"])
        {
            string name = (string)row["controller"]; int index = (int)row["layer"];
            Require(fieldIDs.Add(name + "/" + index), "Repeated source layer");
            var layer = source.GetController(name)["layers"][index]; var expected = row["expected"];
            Require(Hash((string)row["source"]["path"]) == (string)row["source"]["sha256"], "Layer raw source changed");
            Equal(layer["span"], row["span"], "Layer source byte span");
            foreach (string key in new[] { "smIdx", "smms", "binding", "mode", "ik", "sync", "maskBlendShape" })
                Equal(layer[key], expected[key], "Original layer field " + key);
            Require(Bits((float)layer["weight"]) == (string)expected["weight"], "Original layer default weight differs");
            Equal(new JArray(layer["bodyMask"].Select(t => Convert.ToUInt32((string)t,16).ToString("x8"))), expected["bodyMask"], "Body mask words differ");
            Equal(new JArray(layer["skel"].Select(t => new JArray((uint)t[0],Bits((float)t[1])))), expected["skel"], "Skeleton mask entries differ");
            maskEntries += ((JArray)layer["skel"]).Count; fieldLayers++;
        }
        Require(fieldIDs.SetEquals(source.Names.SelectMany(name => Enumerable.Range(0,((JArray)source.GetController(name)["layers"]).Count).Select(i => name+"/"+i))),
            "Full source layer coverage differs");
        foreach (var group in vectors["groups"])
        {
            string name = (string)group["controller"];
            foreach (var variant in group["variants"])
            {
                var root = JObject.Parse(packText);
                var c = root["controllers"].Single(t => (string)t["name"] == name);
                if ((string)variant["variant"] == "allSynced")
                    foreach (var layer in c["layers"]) layer["sync"] = 1;
                else Require((string)variant["variant"] == "source", "Unknown layer fixture variant");
                Equal(c["layers"], variant["layers"], "Layer fixture differs beyond declared sync change");
                var adapted = new NativeControllerSource(root.ToString(Newtonsoft.Json.Formatting.None));
                var weights = new NativeLayerWeights(adapted, name, new float[((JArray)c["layers"]).Count], Initial(group));
                foreach (var row in variant["cases"])
                {
                    for (int i = 0; i < weights.LayerCount; i++)
                    {
                        weights.SetWeight(i, Float(row["runtimeWeightBits"][i]));
                        Require(Bits(weights.GetWeight(i)) == (string)row["runtimeWeightBits"][i], "Layer setter changed float32 bits");
                        Require(weights.MaskBlendShape(i) == (bool)c["layers"][i]["maskBlendShape"], "Layer blend-shape mask flag differs");
                    }
                    var timing = new JArray(Enumerable.Range(0,weights.MachineCount).Select(mi => new JArray(weights.TimingWeights(mi).Select(Bits))));
                    Equal(timing,row["timingWeightBits"],"Native timing weights " + cases);
                    weights.ApplyPoseWeights(row["stateOutputWeightBits"].Select(t => t.Select(Float).ToArray()).ToArray(),Double(row["timestampBits"]));
                    Equal(new JArray(Enumerable.Range(0,weights.LayerCount).Select(i => Serialize(weights.GetInput(i)))),row["expected"],"Native pose weight application " + cases);
                    cases++; applications += weights.LayerCount;
                }
            }
        }
        var bank = new NativeMotionBank("Assets/StreamingAssets/RemielleControllerMotions");
        var intervals = new NativeMotionIntervals(File.ReadAllText("Assets/ControllerRuntime/Data/source-motion-time-ranges.json"),bank);
        var stateIDs = new HashSet<string>();
        foreach (var group in vectors["jointGroups"])
        {
            var id=group["source"]; string name=(string)id["controller"]; int mi=(int)id["machine"], si=(int)id["state"];
            Require(stateIDs.Add(name+"/"+mi+"/"+si),"Repeated joint state");
            var c=source.GetController(name); var stateSource=(JObject)c["machines"][mi]["states"][si];
            var initial=vectors["groups"].Single(g => (string)g["controller"]==name);
            var layerWeights=new NativeLayerWeights(source,name,c["layers"].Select(l => (float)l["weight"]).ToArray(),Initial(initial));
            var motions=new NativeStateMotions(source,bank,intervals,name,mi,si); var stateClock=new NativeStateClock(stateSource);
            var parameters=source.CreateParameters(name);
            foreach (var row in group["cases"])
            {
                foreach (var p in ((JObject)row["values"]).Properties()) parameters.SetFloat(uint.Parse(p.Name),Float(p.Value));
                Equal(new JArray(layerWeights.TimingWeights(mi).Select(Bits)),row["timingWeightBits"],"Joint timing weights");
                var motion=motions.Evaluate(parameters,layerWeights);
                Require(Bits(motion.Length)==(string)row["lengthBits"],"Layer timing composed length differs");
                Equal(new JArray(motion.MotionSets.Select(t => t==null ? (JToken)JValue.CreateNull() : NativeBlendTreeAudit.Serialize(t))),row["treeResults"],"Layer timing composed trees differ");
                var input=row["clockInput"];
                var layer=NativeStateClockAudit.Read<NativeLayerTransitionState>(input["initialState"]);
                var command=NativeStateClockAudit.Read<NativeTransitionStartCommand>(input["initialCommand"]);
                var clock=stateClock.Advance(layer,command,parameters,motion.Length,Float(input["deltaBits"]),Float(input["animatorSpeedBits"]),
                    new NativeStateClockOptions((bool)input["current"],(bool)input["loop"],(bool)input["carryFromNext"],
                        (bool)input["overrideEnabled"],Float(input["overrideBits"]),(bool)input["overrideIsSeconds"],
                        (bool)input["subsystemFlag"],(bool)input["timeManagerFlag"]));
                Equal(NativeStateClockAudit.Serialize(layer,command,clock),row["clockExpected"],"Layer timing composed clock differs");
                jointCases++;
            }
        }
        var expectedIDs=source.Names.SelectMany(name => source.GetController(name)["machines"].SelectMany(m => m["states"].Select(s =>
            name+"/"+(int)m["index"]+"/"+(int)s["state"])));
        Require(stateIDs.SetEquals(expectedIDs) && stateIDs.Count==253 && fieldLayers==16 && maskEntries==3780,"Source layer/state coverage differs");
        Require(cases==(int)evidence["layerCases"] && applications==(int)evidence["layerApplications"] && jointCases==(int)evidence["jointCases"],"Layer execution coverage differs");
        return new JObject {
            ["sourceLayers"]=fieldLayers,["skeletonMaskEntries"]=maskEntries,["layerCases"]=cases,["layerApplications"]=applications,
            ["sourceStates"]=stateIDs.Count,["jointCases"]=jointCases,["sourceTimingWeightRuleQualified"]=true,
            ["timingAndPoseWeightOutputsBitwiseEqual"]=true,["timingTreesLengthAndClockComposed"]=true,
            ["runtimeWeightInitializationQualified"]=false,["stateOutputWeightProductionQualified"]=false,
            ["layerMaskPoseMixingVerified"]=false,["controllerPlayable"]=false,
            ["vectorsSha256"]=Hash(path),["runtimeSha256"]=Hash("Assets/ControllerRuntime/NativeLayerWeights.cs"),
            ["auditSha256"]=Hash("Assets/ControllerRuntime/Editor/NativeLayerWeightsAudit.cs"),
            ["stateMotionsRuntimeSha256"]=Hash("Assets/ControllerRuntime/NativeStateMotions.cs"),
            ["blendRuntimeSha256"]=Hash("Assets/ControllerRuntime/NativeBlendTree.cs"),
            ["clockRuntimeSha256"]=Hash("Assets/ControllerRuntime/NativeStateClock.cs"),
            ["clockAuditSha256"]=Hash("Assets/ControllerRuntime/Editor/NativeStateClockAudit.cs"),
            ["blendAuditSha256"]=Hash("Assets/ControllerRuntime/Editor/NativeBlendTreeAudit.cs")
        };
    }
    public static void RunBatch()
    {
        var report=new JObject{["pass"]=false};
        try {
            var source=new NativeControllerSource(File.ReadAllText("Assets/ControllerRuntime/Data/source-controller-pack.json"));
            report["layerWeights"]=Run(Out+"native-layer-vectors.json",source);report["pass"]=true;
            Debug.Log("NATIVE_LAYER_WEIGHTS_AUDIT_OK "+report.ToString(Newtonsoft.Json.Formatting.None));
        }
        catch(Exception ex){report["error"]=ex.ToString();Debug.LogException(ex);}
        finally{File.WriteAllText(Out+"unity-layer-verification.json",report.ToString());}
        if(!(bool)report["pass"])EditorApplication.Exit(1);
    }
}
