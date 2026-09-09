using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using Remielle.ControllerRuntime;
using UnityEditor;
using UnityEngine;

public static class NativeClipTimeAudit
{
    const string Out = "E:/ZZZ/ZCode/90_Builds/ControllerImplementation/20260906/";
    static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
    static float Float(JToken bits) => BitConverter.Int32BitsToSingle(unchecked((int)Convert.ToUInt32((string)bits, 16)));
    static string Bits(float value) => unchecked((uint)BitConverter.SingleToInt32Bits(value)).ToString("x8");
    static string Bits(double value) => unchecked((ulong)BitConverter.DoubleToInt64Bits(value)).ToString("x16");
    static string Hash(string path)
    {
        using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path))
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }
    static NativeMotionInterval Interval(JToken r) => new NativeMotionInterval(Float(r["startBits"]), Float(r["stopBits"]));
    static JObject Serialize(NativeClipTimeResult r) => new JObject {
        ["secondsBits"] = Bits(r.Seconds), ["phaseBits"] = Bits(r.Phase), ["cycleCountBits"] = Bits(r.CycleCount) };
    static JObject Serialize(NativeLeafTiming t) => new JObject {
        ["speedBits"] = Bits(t.Speed), ["cycleBits"] = Bits(t.Cycle), ["mirror"] = t.Mirror };
    static JObject Observe(NativeClipPlayback p, NativeLeafTiming timing, JToken input)
    {
        float offset = Float(input["evaluationOffsetBits"]), extrapolation = Float(input["extrapolationSpeedBits"]);
        var query = p.GetSampleInput(offset, extrapolation);
        return new JObject {
            ["playback"] = new JObject {
                ["timeBits"] = Bits(p.Time), ["previousSetTimeBits"] = Bits(p.PreviousSetTime),
                ["previousSecondsBits"] = Bits(p.PreviousSeconds), ["overrideOldBits"] = Bits(p.OverrideOld),
                ["overrideNewBits"] = Bits(p.OverrideNew), ["repeated"] = p.RepeatedSetTime, ["hasSetTime"] = p.HasSetTime },
            ["normalizedBits"] = Bits(query.Normalized), ["previousNormalizedBits"] = Bits(query.PreviousNormalized),
            ["mapped"] = Serialize(p.GetSampleTime(timing, Float(input["clipCycleBits"]), (bool)input["loop"],
                offset, extrapolation, Float(input["rateBits"])))
        };
    }
    static void Equal(JToken actual, JToken expected, string where)
    {
        Require(JToken.DeepEquals(actual, expected), where + " actual=" + actual.ToString(Newtonsoft.Json.Formatting.None) +
            " expected=" + expected.ToString(Newtonsoft.Json.Formatting.None));
    }

    public static JObject Run(string path, NativeControllerSource source)
    {
        var vectors = JObject.Parse(File.ReadAllText(path)); var evidence = vectors["evidence"];
        foreach (string key in new[] { "sourcePack", "motionBank", "motionIntervals", "jointVectors" })
            Require(Hash((string)evidence[key]["path"]) == (string)evidence[key]["sha256"], "Clip timing input identity changed: " + key);
        var joint = JObject.Parse(File.ReadAllText((string)evidence["jointVectors"]["path"]));
        int pure = 0, states = 0, cases = 0, leafQueries = 0, fixtureSteps = 0;
        foreach (var row in vectors["pureCases"])
        {
            for (int repeat = 0; repeat < 2; repeat++)
                Equal(Serialize(NativeClipTime.Map(Float(row["normalizedBits"]), Interval(row), Float(row["cycleBits"]),
                    (bool)row["loop"], Float(row["speedBits"]), (bool)row["negative"], Float(row["rateBits"]))), row["expected"], "Clip map " + pure);
            pure++;
        }
        var bank = new NativeMotionBank("Assets/StreamingAssets/RemielleControllerMotions");
        var intervals = new NativeMotionIntervals(File.ReadAllText("Assets/ControllerRuntime/Data/source-motion-time-ranges.json"), bank);
        var settings = (JObject)vectors["sourceSettings"];
        Require(new HashSet<string>(bank.AssetIDs).SetEquals(settings.Properties().Select(p => p.Name)), "Sampling settings bank coverage differs");
        foreach (var setting in settings.Properties())
        {
            var reference = setting.Value["sourceJson"];
            Require(Hash((string)reference["path"]) == (string)reference["sha256"], "Sampling source JSON changed");
            var muscle = JObject.Parse(File.ReadAllText((string)reference["path"]))["m_MuscleClip"];
            Require(Bits((float)muscle["m_CycleOffset"]) == (string)setting.Value["clipCycleBits"] &&
                (bool)muscle["m_LoopTime"] == (bool)setting.Value["sourceLoop"], "Source sampling settings differ");
        }
        var seen = new HashSet<string>();
        foreach (var group in vectors["groups"])
        {
            var id = group["source"]; string name = (string)id["controller"];
            int mi = (int)id["machine"], si = (int)id["state"];
            var sourceState = (JObject)source.GetController(name)["machines"][mi]["states"][si];
            Require((uint)sourceState["mp"] == 0 && (uint)sourceState["cp"] == 0 && (uint)sourceState["extra"] == 0,
                "Dynamic state mirror/cycle/time requires qualified parameter binding");
            var original = joint["groups"].Single(g => JToken.DeepEquals(g["source"], id));
            Require(((JArray)group["cases"]).Count == ((JArray)original["cases"]).Count, "Joint clip case count differs");
            var parameters = source.CreateParameters(name);
            var motions = new NativeStateMotions(source, bank, intervals, name, mi, si);
            var stateClock = new NativeStateClock(sourceState);
            foreach (var row in group["cases"])
            {
                var j = original["cases"][(int)row["jointCase"]];
                foreach (var p in ((JObject)j["values"]).Properties()) parameters.SetFloat(uint.Parse(p.Name), Float(p.Value));
                var motionResult = motions.Evaluate(parameters, j["weightBits"].Select(Float).ToArray());
                var input = j["clockInput"];
                var layer = NativeStateClockAudit.Read<NativeLayerTransitionState>(input["initialState"]);
                var command = NativeStateClockAudit.Read<NativeTransitionStartCommand>(input["initialCommand"]);
                var clock = stateClock.Advance(layer, command, parameters, motionResult.Length,
                    Float(input["deltaBits"]), Float(input["animatorSpeedBits"]),
                    new NativeStateClockOptions((bool)input["current"], (bool)input["loop"], (bool)input["carryFromNext"],
                        (bool)input["overrideEnabled"], Float(input["overrideBits"]), (bool)input["overrideIsSeconds"],
                        (bool)input["subsystemFlag"], (bool)input["timeManagerFlag"]));
                Equal(NativeStateClockAudit.Serialize(layer, command, clock), j["clockExpected"], "Clip pipeline clock " + cases);
                Require(motionResult.MotionSets.Sum(t => t == null ? 0 : t.Leaves.Count) == ((JArray)row["clips"]).Count,
                    "Active leaf pipeline coverage differs");
                var leafIDs = new HashSet<string>();
                foreach (var clip in row["clips"])
                {
                    int set = (int)clip["set"], leafIndex = (int)clip["leaf"]["inputIndex"];
                    Require(leafIDs.Add(set + "/" + leafIndex), "Repeated active leaf");
                    var leaf = motionResult.MotionSets[set].Leaves.Single(l => l.InputIndex == leafIndex);
                    string assetID = bank.ResolveSlot(name, checked((int)leaf.ClipIndex));
                    Require(assetID == (string)clip["assetID"], "Leaf source identity differs"); seen.Add(assetID);
                    var interval = intervals.Get(assetID);
                    Require(Bits(interval.Start) == (string)clip["interval"]["startBits"] &&
                        Bits(interval.Stop) == (string)clip["interval"]["stopBits"], "Leaf interval differs");
                    var timing = NativeLeafTiming.Combine((float)sourceState["speed"], (float)sourceState["cycle"], (int)sourceState["mir"] != 0, leaf);
                    Equal(Serialize(timing), clip["timing"], "Source leaf timing " + leafQueries);
                    var ci = clip["input"];
                    Require(Bits(clock.Normalized) == (string)ci["sampleBits"] && Bits(clock.PreviousNormalized) == (string)ci["previousBits"],
                        "Clip clock composition input differs");
                    Require((bool)ci["loop"] == (bool)settings[assetID]["sourceLoop"] &&
                        (string)ci["clipCycleBits"] == (string)settings[assetID]["clipCycleBits"], "Source sampling defaults differ");
                    var playback = new NativeClipPlayback(interval);
                    for (int repeat = 0; repeat < 2; repeat++)
                    {
                        playback.ApplyClock(clock);
                        Equal(Observe(playback, timing, ci), clip["expected"][repeat], "Source clip pipeline " + leafQueries + "/" + repeat);
                    }
                    leafQueries++;
                }
                cases++;
            }
            states++;
        }
        foreach (var g in vectors["fixtures"])
        {
            var l = g["leaf"];
            var leaf = new NativeBlendLeaf((uint)l["clipIndex"], (int)l["inputIndex"], Float(l["weightBits"]),
                Float(l["speedScaleBits"]), Float(l["cycleOffsetBits"]), (bool)l["mirror"]);
            var timing = NativeLeafTiming.Combine(Float(g["stateSpeedBits"]), Float(g["stateCycleBits"]), (bool)g["stateMirror"], leaf);
            Equal(Serialize(timing), g["timing"], "Fixture leaf timing");
            var playback = new NativeClipPlayback(Interval(g["interval"]));
            foreach (var row in g["cases"])
            {
                var input = row["input"];
                playback.ApplyClock(Float(input["sampleBits"]), Float(input["previousBits"]), (bool)input["override"]);
                Equal(Observe(playback, timing, input), row["expected"], "Playback sequence " + fixtureSteps);
                fixtureSteps++;
            }
        }
        Require(pure == (int)evidence["pureCases"] && states == 253 && seen.Count == 335 &&
            cases == (int)evidence["jointCases"] && leafQueries == (int)evidence["activeLeafQueries"] &&
            fixtureSteps == (int)evidence["playbackFixtureSteps"], "Clip timing coverage differs");
        return new JObject {
            ["pureCases"] = pure, ["pureRepeats"] = 2, ["sourceStates"] = states, ["sourceMotions"] = seen.Count,
            ["jointCases"] = cases, ["activeLeafQueries"] = leafQueries, ["playbackFixtureSteps"] = fixtureSteps,
            ["clipTimeAndHistoryBitwiseEqual"] = true, ["treesClockAndClipTimeComposed"] = true,
            ["productionLoopAndEvaluationPolicyQualified"] = false, ["graphAllocationAndWeightsQualified"] = false,
            ["poseMixingVerified"] = false, ["controllerPlayable"] = false,
            ["vectorsSha256"] = Hash(path), ["runtimeSha256"] = Hash("Assets/ControllerRuntime/NativeClipTime.cs"),
            ["auditSha256"] = Hash("Assets/ControllerRuntime/Editor/NativeClipTimeAudit.cs"),
            ["stateMotionsRuntimeSha256"] = Hash("Assets/ControllerRuntime/NativeStateMotions.cs"),
            ["clockRuntimeSha256"] = Hash("Assets/ControllerRuntime/NativeStateClock.cs"),
            ["clockAuditSha256"] = Hash("Assets/ControllerRuntime/Editor/NativeStateClockAudit.cs"),
            ["blendRuntimeSha256"] = Hash("Assets/ControllerRuntime/NativeBlendTree.cs")
        };
    }
    public static void RunBatch()
    {
        var report = new JObject { ["pass"] = false };
        try
        {
            var source = new NativeControllerSource(File.ReadAllText("Assets/ControllerRuntime/Data/source-controller-pack.json"));
            report["clipTime"] = Run(Out + "native-clip-time-vectors.json", source);
            report["pass"] = true; Debug.Log("NATIVE_CLIP_TIME_AUDIT_OK " + report.ToString(Newtonsoft.Json.Formatting.None));
        }
        catch (Exception ex) { report["error"] = ex.ToString(); Debug.LogException(ex); }
        finally { File.WriteAllText(Out + "unity-clip-time-verification.json", report.ToString()); }
        if (!(bool)report["pass"]) EditorApplication.Exit(1);
    }
}
