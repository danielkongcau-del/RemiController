using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using Remielle.ControllerRuntime;
using UnityEditor;
using UnityEngine;

public static class NativePoseCallbackAudit
{
    const string Out = "E:/ZZZ/local-only/RemielleControllerImplementation/20260906/";
    static void Require(bool value, string why) { if (!value) throw new Exception(why); }
    static float Float(JToken x) => BitConverter.Int32BitsToSingle(unchecked((int)Convert.ToUInt32((string)x, 16)));
    static double Double(JToken x) => BitConverter.Int64BitsToDouble(unchecked((long)Convert.ToUInt64((string)x, 16)));
    static string Bits(float x) => unchecked((uint)BitConverter.SingleToInt32Bits(x)).ToString("x8");
    static string Hash(string path)
    { using (var s = SHA256.Create()) using (var f = File.OpenRead(path)) return BitConverter.ToString(s.ComputeHash(f)).Replace("-", "").ToLowerInvariant(); }
    static byte[][] Mask(JToken r) => r.Select(a => a.Select(v => (byte)v).ToArray()).ToArray();
    static float[][] Weights(JToken r) => r.Select(a => a.Select(Float).ToArray()).ToArray();
    static NativePoseData Pose(JToken r) => r == null || r.Type == JTokenType.Null ? null : new NativePoseData(
        r[0].Select(Float).ToArray(), r[1].Select(Float).ToArray(), r[2].Select(Float).ToArray(),
        r[3].Select(Float).ToArray(), r[4].Select(v => Convert.ToUInt32((string)v, 16)).ToArray());
    static NativePoseStream Stream(JToken r) => new NativePoseStream(Pose(r["pose"]), Mask(r["mask"]),
        (byte)r["streamFlag"], r["flags"] == null ? (byte)0 : (byte)r["flags"][0], r["flags"] == null ? (byte)0 : (byte)r["flags"][1]);
    static JArray Write(NativePoseData p) => new JArray(Enumerable.Range(0, 4).Select(g => (JToken)new JArray(p.FloatChannel(g).Select(Bits)))
        .Append(new JArray(p.DiscreteChannel().Select(v => v.ToString("x8")))));
    static JArray Write(byte[][] mask) => new JArray(mask.Select(row => new JArray(row.Select(v => (int)v))));
    static JObject Write(NativePoseStream s, bool referenceFlags)
    {
        var r = new JObject { ["pose"] = Write(s.Pose()), ["mask"] = Write(s.Mask()), ["streamFlag"] = s.StreamFlag };
        if (referenceFlags) r["flags"] = new JArray((int)s.ReferenceFlag0, (int)s.ReferenceFlag1);
        return r;
    }
    static void Equal(JToken a, JToken e, string where)
    { Require(JToken.DeepEquals(a, e), where + " actual=" + a.ToString(Newtonsoft.Json.Formatting.None) + " expected=" + e.ToString(Newtonsoft.Json.Formatting.None)); }
    static NativeMixerInputWeight Weight(JToken r) => new NativeMixerInputWeight(Float(r["inputBits"]), Float(r["startBits"]),
        Double(r["timestampBits"]), Double(r["startTimeBits"]), Float(r["rateBits"]));
    static NativeMixerLink Link(JToken r) => new NativeMixerLink((int)r["node"], (int)r["port"]);
    static NativePoseChannelMixer Mixer(JToken state) => new NativePoseChannelMixer(Stream(state["scratch"]), Mask(state["priorMask"]), Weights(state["weights"]));

    static (string dispatch, int calls) Evaluate(NativePoseChannelMixer mixer, NativePoseStream output,
        JToken fixture, JToken m, JToken expected, string label)
    {
        var nodes = m["nodes"].Select(n => new NativeMixerNode((uint)n["type"], Double(n["delayBits"]), n["links"].Select(Link).ToArray())).ToArray();
        var selection = NativeMixerEvaluation.Evaluate(m["weights"].Select(Weight).ToArray(), m["links"].Select(Link).ToArray(), nodes,
            Float(m["offsetBits"]), Double(m["timestampBits"]), Float(m["deltaBits"]));
        var calls = new JArray();
        mixer.Evaluate(selection, output, Pose(fixture["defaults"]), Pose(fixture["overrideDefaults"]),
            (bool)fixture["preserveScalarMask"], (bool)fixture["skipDefaults"], (node, stream) =>
            {
                var source = fixture["sources"][node];
                calls.Add(new JObject { ["node"] = node, ["target"] = ReferenceEquals(output, stream) ? "output" : "scratch" });
                stream.WriteMasked(Pose(source["pose"]), Mask(source["mask"]), (byte)source["streamFlag"], (byte)source["flags"][0], (byte)source["flags"][1]);
            });
        var state = new JObject {
            ["output"] = Write(output, true), ["scratch"] = Write(mixer.Scratch(), false),
            ["weights"] = new JArray(mixer.Weights().Select(row => new JArray(row.Select(Bits)))), ["priorMask"] = Write(mixer.PriorMask()) };
        Equal(state, expected["state"], label + " persistent stream state");
        Equal(calls, expected["childCalls"], label + " child dispatch order/receiver");
        Require(selection.Dispatch.ToString().ToLowerInvariant() == (string)expected["selection"]["dispatch"], label + " selection");
        return (selection.Dispatch.ToString().ToLowerInvariant(), calls.Count);
    }

    public static JObject Run(string path)
    {
        var data = JObject.Parse(File.ReadAllText(path)); var evidence = data["evidence"];
        foreach (var r in new[] { evidence["source"], evidence["sourcePack"], evidence["generator"], evidence["mixerVectors"], evidence["poseOracle"] }.Concat(evidence["dependencies"]))
            Require(Hash((string)r["path"]) == (string)r["sha256"], "Pose callback input identity changed: " + r["path"]);
        Require(!(bool)evidence["context82"] && (bool)evidence["hostFinalizationOriginal"], "Unexpected original pose context");
        var input = JObject.Parse(File.ReadAllText((string)evidence["mixerVectors"]["path"]));
        var counts = new Dictionary<string, int> { ["empty"] = 0, ["single"] = 0, ["blend"] = 0 };
        int cases = 0, childCalls = 0, groups = 0, steps = 0;
        for (int repeat = 0; repeat < 2; repeat++)
        {
            foreach (var row in data["cases"])
            {
                var s = row["initial"]; var actual = Evaluate(Mixer(s), Stream(s["output"]), row["fixture"],
                    input["cases"][(int)row["mixerCase"]], row["expected"], "Case " + cases);
                counts[actual.dispatch]++; childCalls += actual.calls; cases++;
            }
            foreach (var group in data["continuousGroups"])
            {
                var s = group["initial"]; var mixer = Mixer(s); var output = Stream(s["output"]);
                foreach (var row in group["steps"])
                {
                    Evaluate(mixer, output, row["fixture"], input["cases"][(int)row["mixerCase"]], row["expected"], "Continuous step " + steps);
                    steps++;
                }
                groups++;
            }
        }
        Require(cases == 2 * (int)evidence["cases"] && childCalls == 2 * (int)evidence["childCalls"] &&
            groups == 2 * (int)evidence["continuousGroups"] && steps == 2 * (int)evidence["continuousSteps"], "Pose callback coverage differs");
        foreach (var pair in counts) Require(pair.Value == 2 * (int)evidence["dispatchCounts"][pair.Key], "Pose dispatch coverage differs");
        return new JObject {
            ["cases"] = cases / 2, ["dispatchCounts"] = JObject.FromObject(counts.ToDictionary(x => x.Key, x => x.Value / 2)),
            ["childCalls"] = childCalls / 2, ["continuousGroups"] = groups / 2, ["continuousSteps"] = steps / 2, ["repeats"] = 2,
            ["originalPoseChannelCallbacksBitwiseEqual"] = true, ["completeCc5fc0PoseChannelPathVerified"] = true,
            ["additionalPoseContext82Verified"] = false, ["sourceBindingAndMaskProducersVerified"] = false,
            ["completePoseCallbacksVerified"] = false, ["controllerPlayable"] = false,
            ["vectorsSha256"] = Hash(path), ["runtimeSha256"] = Hash("Assets/ControllerRuntime/NativePoseChannelMixer.cs"),
            ["auditSha256"] = Hash("Assets/ControllerRuntime/Editor/NativePoseCallbackAudit.cs"),
            ["poseRuntimeSha256"] = Hash("Assets/ControllerRuntime/NativePoseMath.cs"),
            ["normalizerSha256"] = Hash("Assets/Plugins/x86_64/RemiellePoseMath.dll"),
            ["mixerRuntimeSha256"] = Hash("Assets/ControllerRuntime/NativeMixerEvaluation.cs") };
    }

    public static void RunBatch()
    {
        var result = new JObject { ["pass"] = false };
        try { result["poseCallbacks"] = Run(Out + "native-pose-callback-vectors.json"); result["pass"] = true; Debug.Log("NATIVE_POSE_CALLBACK_AUDIT_OK " + result.ToString(Newtonsoft.Json.Formatting.None)); }
        catch (Exception ex) { result["error"] = ex.ToString(); Debug.LogException(ex); }
        finally { File.WriteAllText(Out + "unity-pose-callback-verification.json", result.ToString()); }
        if (!(bool)result["pass"]) EditorApplication.Exit(1);
    }
}
