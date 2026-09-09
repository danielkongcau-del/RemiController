using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using Remielle.ControllerRuntime;
using UnityEditor;
using UnityEngine;

public static class NativeStateMotionsAudit
{
    const string Out = "E:/ZZZ/local-only/RemielleControllerImplementation/20260906/";
    static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
    static float Float(JToken bits) => BitConverter.Int32BitsToSingle(unchecked((int)Convert.ToUInt32((string)bits, 16)));
    static string Bits(float value) => unchecked((uint)BitConverter.SingleToInt32Bits(value)).ToString("x8");
    static string Hash(string path)
    {
        using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path))
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }
    public static JObject Run(string path, NativeControllerSource source)
    {
        var vectors = JObject.Parse(File.ReadAllText(path)); var evidence = vectors["evidence"];
        foreach (string key in new[] { "sourcePack", "motionBank", "motionIntervals", "blendVectors" })
            Require(Hash((string)evidence[key]["path"]) == (string)evidence[key]["sha256"], "State-motion input identity changed: " + key);
        var bank = new NativeMotionBank("Assets/StreamingAssets/RemielleControllerMotions");
        var intervals = new NativeMotionIntervals(File.ReadAllText("Assets/ControllerRuntime/Data/source-motion-time-ranges.json"), bank);
        var states = new HashSet<string>(); int cases = 0, emptySets = 0;
        foreach (var group in vectors["groups"])
        {
            var id = group["source"]; string name = (string)id["controller"];
            int mi = (int)id["machine"], si = (int)id["state"];
            Require(states.Add(id.ToString(Newtonsoft.Json.Formatting.None)), "Repeated source state");
            var stateSource = (JObject)source.GetController(name)["machines"][mi]["states"][si];
            var parameters = source.CreateParameters(name);
            var motions = new NativeStateMotions(source, bank, intervals, name, mi, si);
            var clock = new NativeStateClock(stateSource);
            foreach (var row in group["cases"])
            {
                foreach (var p in ((JObject)row["values"]).Properties()) parameters.SetFloat(uint.Parse(p.Name), Float(p.Value));
                var weights = row["weightBits"].Select(Float).ToArray();
                var actual = motions.Evaluate(parameters, weights);
                var trees = new JArray(actual.MotionSets.Select(t => t == null ? (JToken)JValue.CreateNull() : NativeBlendTreeAudit.Serialize(t)));
                Require(Bits(actual.Length) == (string)row["lengthBits"] && JToken.DeepEquals(trees, row["treeResults"]),
                    "Source tree/length composition differs at " + cases + " actualLength=" + Bits(actual.Length) + " expected=" + row["lengthBits"]);
                emptySets += actual.MotionSets.Count(t => t == null);
                var input = row["clockInput"];
                Require((bool)input["loop"] == ((int)stateSource["loop"] != 0), "Joint clock loop flag differs from source");
                Require(JToken.DeepEquals(input["valueBits"], row["values"]), "Joint clock parameter inputs differ");
                var layer = NativeStateClockAudit.Read<NativeLayerTransitionState>(input["initialState"]);
                var command = NativeStateClockAudit.Read<NativeTransitionStartCommand>(input["initialCommand"]);
                var result = clock.Advance(layer, command, parameters, actual.Length, Float(input["deltaBits"]),
                    Float(input["animatorSpeedBits"]), new NativeStateClockOptions((bool)input["current"], (bool)input["loop"],
                        (bool)input["carryFromNext"], (bool)input["overrideEnabled"], Float(input["overrideBits"]),
                        (bool)input["overrideIsSeconds"], (bool)input["subsystemFlag"], (bool)input["timeManagerFlag"]));
                var serialized = NativeStateClockAudit.Serialize(layer, command, result);
                Require(JToken.DeepEquals(serialized, row["clockExpected"]), "Joint native clock differs at " + cases +
                    " actual=" + serialized.ToString(Newtonsoft.Json.Formatting.None) + " expected=" + row["clockExpected"].ToString(Newtonsoft.Json.Formatting.None));
                cases++;
            }
        }
        int fixtures = 0;
        foreach (var row in vectors["aggregationFixtures"])
        {
            float?[] lengths = row["lengthBits"].Select(t => t.Type == JTokenType.Null ? (float?)null : Float(t)).ToArray();
            float[] weights = row["weightBits"].Select(Float).ToArray();
            Require(Bits(NativeMotionSetLength.Combine(lengths, weights)) == (string)row["expectedLengthBits"],
                "Native motion-set aggregation differs at fixture " + fixtures);
            fixtures++;
        }
        Require(states.Count == (int)evidence["sourceStates"] && states.Count == 253 && cases == (int)evidence["jointCases"], "Joint source coverage differs");
        Require(fixtures == (int)evidence["aggregationFixtures"], "Length aggregate fixture coverage differs");
        return new JObject {
            ["sourceStates"] = states.Count, ["jointCases"] = cases, ["aggregationFixtures"] = fixtures, ["emptySetObservations"] = emptySets,
            ["treesLengthAndClockFloat32BitwiseEqual"] = true, ["originalMotionSetMappingPreserved"] = true,
            ["productionMotionSetWeightsQualified"] = false, ["playableWiringQualified"] = false,
            ["fullTickOrderingVerified"] = false, ["controllerPlayable"] = false,
            ["vectorsSha256"] = Hash(path), ["runtimeSha256"] = Hash("Assets/ControllerRuntime/NativeStateMotions.cs"),
            ["blendRuntimeSha256"] = Hash("Assets/ControllerRuntime/NativeBlendTree.cs"),
            ["clockRuntimeSha256"] = Hash("Assets/ControllerRuntime/NativeStateClock.cs"),
            ["auditSha256"] = Hash("Assets/ControllerRuntime/Editor/NativeStateMotionsAudit.cs"),
            ["clockAuditSha256"] = Hash("Assets/ControllerRuntime/Editor/NativeStateClockAudit.cs"),
            ["blendAuditSha256"] = Hash("Assets/ControllerRuntime/Editor/NativeBlendTreeAudit.cs")
        };
    }
    public static void RunBatch()
    {
        var report = new JObject { ["pass"] = false };
        try
        {
            var source = new NativeControllerSource(File.ReadAllText("Assets/ControllerRuntime/Data/source-controller-pack.json"));
            report["blendTrees"] = NativeBlendTreeAudit.Run(Out + "native-blend-vectors.json", source);
            report["stateMotions"] = Run(Out + "native-state-motion-vectors.json", source);
            report["pass"] = true; Debug.Log("NATIVE_STATE_MOTIONS_AUDIT_OK " + report.ToString(Newtonsoft.Json.Formatting.None));
        }
        catch (Exception ex) { report["error"] = ex.ToString(); Debug.LogException(ex); }
        finally { File.WriteAllText(Out + "unity-state-motion-verification.json", report.ToString()); }
        if (!(bool)report["pass"]) EditorApplication.Exit(1);
    }
}
