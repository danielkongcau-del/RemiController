using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using Remielle.ControllerRuntime;
using UnityEditor;
using UnityEngine;

public static class NativeBlendTreeAudit
{
    const string Out = "E:/ZZZ/ZCode/90_Builds/ControllerImplementation/20260906/";
    const string Bank = "Assets/StreamingAssets/RemielleControllerMotions";
    const string Intervals = "Assets/ControllerRuntime/Data/source-motion-time-ranges.json";
    static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
    static float Float(JToken bits) => BitConverter.Int32BitsToSingle(unchecked((int)Convert.ToUInt32((string)bits, 16)));
    static string Bits(float value) => unchecked((uint)BitConverter.SingleToInt32Bits(value)).ToString("x8");
    static string Hash(string path)
    {
        using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path))
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }
    static JObject Values(NativeParameterBank bank, JArray definitions)
    {
        var result = new JObject();
        foreach (var p in definitions)
        {
            uint hash = (uint)p["hash"]; string key = hash.ToString();
            switch ((int)p["kind"])
            {
                case 1: result[key] = Bits(bank.GetFloat(hash)); break;
                case 3: result[key] = bank.GetInt(hash); break;
                case 4: result[key] = bank.GetBool(hash); break;
                case 9: result[key] = bank.GetTrigger(hash); break;
            }
        }
        return result;
    }
    internal static JObject Serialize(NativeBlendTreeResult result) => new JObject {
        ["lengthBits"] = Bits(result.Length), ["nodeWeightBits"] = new JArray(result.NodeWeights.Select(Bits)),
        ["leaves"] = new JArray(result.Leaves.Select(l => new JObject {
            ["clipIndex"] = l.ClipIndex, ["inputIndex"] = l.InputIndex, ["weightBits"] = Bits(l.Weight),
            ["speedScaleBits"] = Bits(l.SpeedScale), ["cycleOffsetBits"] = Bits(l.CycleOffset), ["mirror"] = l.Mirror
        }))
    };

    public static JObject Run(string path, NativeControllerSource source)
    {
        var vectors = JObject.Parse(File.ReadAllText(path)); var evidence = vectors["evidence"];
        foreach (string key in new[] { "sourcePack", "motionBank", "motionIntervals" })
            Require(Hash((string)evidence[key]["path"]) == (string)evidence[key]["sha256"], "Blend input identity changed: " + key);
        var bank = new NativeMotionBank(Bank); var intervals = new NativeMotionIntervals(File.ReadAllText(Intervals), bank);
        var controllers = source.Names.ToDictionary(n => n, n => source.GetController(n));
        var identities = new HashSet<string>(); int trees = 0, nodes = 0, leaves = 0, roots = 0, cases = 0, activeInputs = 0;
        int missingParametersRejected = 0;
        foreach (var group in vectors["groups"])
        {
            var tree = (JObject)group["tree"]; var definitions = (JArray)group["parameters"];
            var inputIntervals = ((JObject)group["intervals"]).Properties().ToDictionary(p => uint.Parse(p.Name),
                p => new NativeMotionInterval(Float(p.Value["startBits"]), Float(p.Value["stopBits"])));
            NativeBlendTree evaluator;
            if (group["source"] != null)
            {
                var id = group["source"]; string name = (string)id["controller"]; var c = controllers[name];
                int machine = (int)id["machine"], stateIndex = (int)id["state"], ti = (int)id["tree"];
                var state = c["machines"][machine]["states"][stateIndex];
                Require(JToken.DeepEquals(tree, state["trees"][ti]), "Original blend tree differs");
                Require(JToken.DeepEquals(definitions, c["parameters"]), "Original blend parameter definitions differ");
                Require(identities.Add(id.ToString(Newtonsoft.Json.Formatting.None)), "Repeated source tree");
                int layer = Enumerable.Range(0, ((JArray)c["layers"]).Count).First(i =>
                    (int)c["layers"][i]["smIdx"] == machine && (int)state["blendTreeIndices"][(int)c["layers"][i]["smms"]] == ti);
                evaluator = NativeBlendTree.ForLayer(source, bank, intervals, name, layer, stateIndex);
                Require(evaluator != null, "Source tree resolved as a placeholder");
                foreach (var pair in ((JObject)group["intervals"]).Properties())
                {
                    string assetID = bank.ResolveSlot(name, int.Parse(pair.Name));
                    var interval = intervals.Get(assetID);
                    Require(assetID == (string)pair.Value["assetID"] && Bits(interval.Start) == (string)pair.Value["startBits"] &&
                        Bits(interval.Stop) == (string)pair.Value["stopBits"], "Source-qualified clip interval differs");
                }
                trees++; nodes += ((JArray)tree["nodes"]).Count;
                leaves += tree["nodes"].Count(n => (uint)n["clipIndex"] != uint.MaxValue);
                roots += tree["nodes"].Count(n => ((JArray)n["childIndices"]).Count != 0);
            }
            else evaluator = new NativeBlendTree(tree, inputIntervals);
            var parameters = new NativeParameterBank(definitions);
            foreach (var row in group["cases"])
            {
                foreach (var p in ((JObject)row["values"]).Properties()) parameters.SetFloat(uint.Parse(p.Name), Float(p.Value));
                var before = Values(parameters, definitions);
                for (int repeat = 0; repeat < 2; repeat++)
                {
                    var actual = evaluator.Evaluate(parameters); var serialized = Serialize(actual);
                    Require(JToken.DeepEquals(serialized, row["expected"]), "Native blend differs at " + cases +
                        " actual=" + serialized.ToString(Newtonsoft.Json.Formatting.None) + " expected=" + row["expected"].ToString(Newtonsoft.Json.Formatting.None));
                    Require(JToken.DeepEquals(before, Values(parameters, definitions)), "Blend evaluation changed parameters");
                    if (repeat == 0) activeInputs += actual.Leaves.Count;
                }
                cases++;
            }
            if (tree["nodes"].Any(n => ((JArray)n["childIndices"]).Count != 0))
            {
                bool rejected = false;
                try { evaluator.Evaluate(new NativeParameterBank(new JArray())); } catch (InvalidDataException) { rejected = true; }
                Require(rejected, "Missing blend parameter silently accepted"); missingParametersRejected++;
            }
        }
        int placeholders = 0;
        foreach (var pair in controllers)
        {
            var c = pair.Value;
            for (int li = 0; li < ((JArray)c["layers"]).Count; li++)
            {
                var layer = c["layers"][li]; var states = c["machines"][(int)layer["smIdx"]]["states"];
                for (int si = 0; si < ((JArray)states).Count; si++)
                    if ((int)states[si]["blendTreeIndices"][(int)layer["smms"]] < 0)
                    { Require(NativeBlendTree.ForLayer(source, bank, intervals, pair.Key, li, si) == null, "Placeholder became a motion"); placeholders++; }
            }
        }
        Require(trees == (int)evidence["authoredTrees"] && nodes == 464 && leaves == 456 && roots == 8, "Authored blend coverage differs");
        Require(cases == (int)evidence["cases"], "Blend vector coverage differs");
        return new JObject {
            ["authoredTrees"] = trees, ["authoredNodes"] = nodes, ["authoredLeaves"] = leaves, ["blendRoots"] = roots,
            ["cases"] = cases, ["repeatedEvaluations"] = cases * 2, ["activeLeafInputs"] = activeInputs,
            ["missingParameterGroupsRejected"] = missingParametersRejected, ["placeholdersRemainEmpty"] = placeholders,
            ["motionIntervalsVerified"] = bank.AssetIDs.Count(), ["float32OutputsBitwiseEqual"] = true, ["parametersPreserved"] = true,
            ["layerSetLengthAggregationQualified"] = false, ["playableWiringQualified"] = false,
            ["poseBlendingQualified"] = false, ["controllerPlayable"] = false,
            ["vectorsSha256"] = Hash(path), ["intervalsSha256"] = Hash(Intervals),
            ["runtimeSha256"] = Hash("Assets/ControllerRuntime/NativeBlendTree.cs"),
            ["auditSha256"] = Hash("Assets/ControllerRuntime/Editor/NativeBlendTreeAudit.cs")
        };
    }

    public static void RunBatch()
    {
        var report = new JObject { ["pass"] = false };
        try
        {
            report["blendTrees"] = Run(Out + "native-blend-vectors.json", new NativeControllerSource(
                File.ReadAllText("Assets/ControllerRuntime/Data/source-controller-pack.json")));
            report["pass"] = true; Debug.Log("NATIVE_BLEND_TREE_AUDIT_OK " + report.ToString(Newtonsoft.Json.Formatting.None));
        }
        catch (Exception ex) { report["error"] = ex.ToString(); Debug.LogException(ex); }
        finally { File.WriteAllText(Out + "unity-blend-verification.json", report.ToString()); }
        if (!(bool)report["pass"]) EditorApplication.Exit(1);
    }
}
