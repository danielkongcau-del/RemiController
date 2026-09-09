using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using Remielle.ControllerRuntime;
using UnityEditor;
using UnityEngine;

public static class NativeTransitionCommitAudit
{
    const string Out = "E:/ZZZ/local-only/RemielleControllerImplementation/20260906/";
    static string Hash(string path)
    {
        using (var sha = SHA256.Create())
        using (var input = File.OpenRead(path))
            return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
    }
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    static float Float(JToken token) => BitConverter.ToSingle(BitConverter.GetBytes(Convert.ToUInt32((string)token, 16)), 0);
    static string Bits(float value) => BitConverter.ToUInt32(BitConverter.GetBytes(value), 0).ToString("x8");

    static NativeLayerTransitionState State(JToken s) => new NativeLayerTransitionState {
        CurrentState = (uint)s["currentState"], NextState = (uint)s["nextState"],
        TransitionIndex = (int)s["transitionIndex"], TransitionSourceState = (int)s["transitionSourceState"],
        TraversalFlags = (uint)s["traversalFlags"], SourceDuration = Float(s["sourceDurationBits"]),
        TransitionStartTime = Float(s["transitionStartTimeBits"]), TransitionElapsed = Float(s["transitionElapsedBits"]),
        TransitionDuration = Float(s["transitionDurationBits"]), DestinationOffset = Float(s["destinationOffsetBits"]),
        InTransition = (bool)s["inTransition"], NeedsDestinationStart = (bool)s["needsDestinationStart"], FixedDuration = (bool)s["fixedDuration"]
    };
    static JObject State(NativeLayerTransitionState s) => new JObject {
        ["currentState"] = s.CurrentState, ["nextState"] = s.NextState,
        ["transitionIndex"] = s.TransitionIndex, ["transitionSourceState"] = s.TransitionSourceState,
        ["traversalFlags"] = s.TraversalFlags, ["sourceDurationBits"] = Bits(s.SourceDuration),
        ["transitionStartTimeBits"] = Bits(s.TransitionStartTime), ["transitionElapsedBits"] = Bits(s.TransitionElapsed),
        ["transitionDurationBits"] = Bits(s.TransitionDuration), ["destinationOffsetBits"] = Bits(s.DestinationOffset),
        ["inTransition"] = s.InTransition, ["needsDestinationStart"] = s.NeedsDestinationStart, ["fixedDuration"] = s.FixedDuration
    };
    static NativeTransitionStartCommand Command(JToken c) => new NativeTransitionStartCommand {
        Command = (int)c["command"], Offset = Float(c["offsetBits"]),
        OvershootSeconds = Float(c["overshootSecondsBits"]), OffsetIsFrames = (bool)c["offsetIsFrames"]
    };
    static JObject Command(NativeTransitionStartCommand c) => new JObject {
        ["command"] = c.Command, ["offsetBits"] = Bits(c.Offset),
        ["overshootSecondsBits"] = Bits(c.OvershootSeconds), ["offsetIsFrames"] = c.OffsetIsFrames
    };
    static JObject Parameters(NativeParameterBank bank, JArray definitions, JObject write = null)
    {
        var result = new JObject();
        foreach (var p in definitions)
        {
            uint hash = (uint)p["hash"]; string key = hash.ToString();
            if (write != null)
                switch ((int)p["kind"])
                {
                    case 1: bank.SetFloat(hash, (float)write[key]); break;
                    case 3: bank.SetInt(hash, (int)write[key]); break;
                    case 4: bank.SetBool(hash, (bool)write[key]); break;
                    case 9: if ((bool)write[key]) bank.SetTrigger(hash); else bank.ResetTrigger(hash); break;
                }
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

    public static JObject Run(string path, NativeControllerSource source)
    {
        var data = JObject.Parse(File.ReadAllText(path)); var evidence = data["evidence"];
        Require(Hash((string)evidence["sourcePack"]["path"]) == (string)evidence["sourcePack"]["sha256"], "Commit vectors use another source pack");
        var controllers = source.Names.ToDictionary(name => name, name => source.GetController(name));
        int count = 0, accepted = 0, multi = 0, routes = 0, marked = 0, conditions = 0, fullGroups = 0;
        var authored = new HashSet<string>();
        foreach (var group in data["groups"])
        {
            JObject machine; JArray definitions, transitions;
            NativeTransitionSearch search;
            var origin = group["source"];
            if (origin != null)
            {
                var c = controllers[(string)origin["controller"]]; machine = (JObject)c["machines"][(int)origin["machine"]];
                definitions = (JArray)c["parameters"];
                int state = (int)origin["state"];
                transitions = (JArray)(state < 0 ? machine["anyTransitions"] : machine["states"][state]["transitions"]);
                if (origin["isolatedTransition"] != null)
                {
                    int ti = (int)origin["isolatedTransition"];
                    Require(authored.Add(origin.ToString(Newtonsoft.Json.Formatting.None)), "Duplicate isolated source record");
                    transitions = new JArray(transitions[ti].DeepClone());
                    search = new NativeTransitionSearch(machine, transitions);
                }
                else
                {
                    fullGroups++;
                    search = source.CreateTransitionSearch((string)origin["controller"], (int)origin["machine"], state);
                }
            }
            else
            {
                machine = (JObject)group["fixture"]["machine"]; definitions = (JArray)group["fixture"]["parameters"];
                transitions = (JArray)group["fixture"]["candidates"]; search = new NativeTransitionSearch(machine, transitions);
            }
            var bank = new NativeParameterBank(definitions);
            foreach (var row in group["cases"])
            {
                var before = Parameters(bank, definitions, (JObject)row["values"]);
                for (int repeat = 0; repeat < 2; repeat++)
                {
                    var state = State(row["initialState"]); var command = Command(row["initialCommand"]);
                    var actual = search.EvaluateAndCommit(state, command, bank,
                        Float(row["previousBits"]), Float(row["currentBits"]), Float(row["directionBits"]),
                        (bool)row["loop"], new NativeTransitionTimingPolicy((bool)row["comparisonFix"], (bool)row["includePreviousBoundary"]),
                        (int)row["sourceState"], (uint)row["eventCode"], row["used"].Values<uint>());
                    var serialized = new JObject {
                        ["accepted"] = actual.Accepted, ["state"] = State(state), ["command"] = Command(command),
                        ["eventCode"] = actual.EventCode, ["lastGateEligible"] = actual.LastGateEligible,
                        ["lastGateOvershootBits"] = Bits(actual.LastGateOvershoot), ["used"] = JArray.FromObject(actual.UsedTriggers),
                        ["visitedCandidates"] = JArray.FromObject(actual.VisitedCandidates),
                        ["visitedSelectors"] = JArray.FromObject(actual.VisitedSelectors),
                        ["matchedSelectorBranches"] = JArray.FromObject(actual.MatchedSelectorBranches),
                        ["conditionCalls"] = actual.ConditionsChecked
                    };
                    Require(JToken.DeepEquals(serialized, row["expected"]), "Native transition commit differs at case " + count +
                        " repeat " + repeat + ": actual=" + serialized.ToString(Newtonsoft.Json.Formatting.None) +
                        " expected=" + row["expected"].ToString(Newtonsoft.Json.Formatting.None));
                    Require(JToken.DeepEquals(before, Parameters(bank, definitions)), "Commit consumed a trigger or changed parameters");
                    if (repeat == 0)
                    {
                        if (actual.Accepted) accepted++;
                        if (actual.VisitedCandidates.Count > 1) multi++;
                        if (actual.VisitedSelectors.Count > 0) routes++;
                        if (actual.UsedTriggers.Except(row["used"].Values<uint>()).Any()) marked++;
                        conditions += actual.ConditionsChecked;
                    }
                }
                count++;
            }
        }
        Require(count == (int)evidence["cases"] && authored.Count == 1318 && fullGroups == (int)evidence["fullSourceGroups"], "Source candidate coverage differs");
        Require(accepted == (int)evidence["accepted"] && multi == (int)evidence["multipleCandidates"] &&
            routes == (int)evidence["selectorRoutes"] && marked == (int)evidence["triggerMarksAdded"] &&
            conditions == (int)evidence["totalConditions"], "Original joint execution counts differ");
        return new JObject {
            ["cases"] = count, ["repeatedEvaluations"] = count * 2, ["authoredTransitions"] = authored.Count,
            ["fullSourceGroups"] = fullGroups, ["accepted"] = accepted, ["multipleCandidates"] = multi,
            ["selectorRoutes"] = routes, ["triggerMarksAdded"] = marked, ["conditionCalls"] = conditions,
            ["stateAndCommandFloat32BitwiseEqual"] = true, ["parameterValuesPreserved"] = true,
            ["observerPointerNull"] = true, ["globalInterruptionOrderingVerified"] = false,
            ["transitionCompletionVerified"] = false, ["controllerPlayable"] = false,
            ["vectorsSha256"] = Hash(path),
            ["runtimeSha256"] = Hash("Assets/ControllerRuntime/NativeTransitionSearch.cs"),
            ["sourceApiSha256"] = Hash("Assets/ControllerRuntime/NativeControllerSource.cs"),
            ["selectorRuntimeSha256"] = Hash("Assets/ControllerRuntime/NativeSelectorResolver.cs"),
            ["auditSha256"] = Hash("Assets/ControllerRuntime/Editor/NativeTransitionCommitAudit.cs")
        };
    }

    public static void RunBatch()
    {
        var report = new JObject { ["pass"] = false };
        try
        {
            report["transitionCommit"] = Run(Out + "native-transition-commit-vectors.json",
                new NativeControllerSource(File.ReadAllText("Assets/ControllerRuntime/Data/source-controller-pack.json")));
            report["pass"] = true;
            Debug.Log("NATIVE_TRANSITION_COMMIT_AUDIT_OK " + report.ToString(Newtonsoft.Json.Formatting.None));
        }
        catch (Exception ex) { report["error"] = ex.ToString(); Debug.LogException(ex); }
        finally { File.WriteAllText(Out + "unity-transition-commit-verification.json", report.ToString()); }
        if (!(bool)report["pass"]) EditorApplication.Exit(1);
    }
}
