using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using Remielle.ControllerRuntime;
using UnityEditor;
using UnityEngine;

public static class NativeStateClockAudit
{
    const string Out = "E:/ZZZ/local-only/RemielleControllerImplementation/20260906/";
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    static float Float(JToken value) => BitConverter.Int32BitsToSingle(unchecked((int)Convert.ToUInt32((string)value, 16)));
    static string Bits(float value) => unchecked((uint)BitConverter.SingleToInt32Bits(value)).ToString("x8");
    static string Hash(string path)
    {
        using (var sha = SHA256.Create()) using (var input = File.OpenRead(path))
            return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
    }

    // Only data movement uses reflection: arithmetic stays in the production
    // module and expected values come from independent original instructions.
    internal static T Read<T>(JToken values) where T : new()
    {
        var result = new T();
        foreach (var p in ((JObject)values).Properties())
        {
            string key = p.Name.EndsWith("Bits", StringComparison.Ordinal) ? p.Name.Substring(0, p.Name.Length - 4) : p.Name;
            string name = char.ToUpperInvariant(key[0]) + key.Substring(1);
            var field = typeof(T).GetField(name, BindingFlags.Instance | BindingFlags.Public);
            Require(field != null, "Unknown original runtime field " + name);
            object value = field.FieldType == typeof(float) ? (object)Float(p.Value) :
                field.FieldType == typeof(uint) ? (object)(uint)p.Value :
                field.FieldType == typeof(int) ? (object)(int)p.Value : (object)(bool)p.Value;
            field.SetValue(result, value);
        }
        return result;
    }
    static JObject Write<T>(T value)
    {
        var result = new JObject();
        foreach (var field in typeof(T).GetFields(BindingFlags.Instance | BindingFlags.Public))
        {
            string key = char.ToLowerInvariant(field.Name[0]) + field.Name.Substring(1);
            object item = field.GetValue(value);
            if (field.FieldType == typeof(float)) result[key + "Bits"] = Bits((float)item);
            else result[key] = JToken.FromObject(item);
        }
        return result;
    }
    internal static JObject Serialize(NativeLayerTransitionState state, NativeTransitionStartCommand command, NativeStateClockResult actual) => new JObject {
        ["state"] = Write(state), ["command"] = Write(command), ["previousNormalizedBits"] = Bits(actual.PreviousNormalized),
        ["normalizedBits"] = Bits(actual.Normalized), ["frameCountBits"] = Bits(actual.FrameCount),
        ["effectiveDurationBits"] = Bits(actual.EffectiveDuration), ["effectiveSpeedBits"] = Bits(actual.EffectiveSpeed),
        ["correctedFrameCount"] = actual.CorrectedFrameCount, ["correctionDiagnostic"] = actual.CorrectionDiagnostic
    };
    static JObject Parameters(NativeParameterBank bank, JArray definitions, JObject write = null, JObject floatBits = null)
    {
        var result = new JObject();
        foreach (var p in definitions)
        {
            uint hash = (uint)p["hash"]; string key = hash.ToString();
            if (write != null) switch ((int)p["kind"])
            {
                case 1: bank.SetFloat(hash, Float(floatBits[key])); break;
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
        foreach (string key in new[] { "sourcePack", "motionBank" })
            Require(Hash((string)evidence[key]["path"]) == (string)evidence[key]["sha256"], "Clock input identity changed: " + key);
        var bankData = JObject.Parse(File.ReadAllText((string)evidence["motionBank"]["path"]));
        var motions = bankData["motions"].ToDictionary(m => (string)m["assetID"]);
        var controllers = source.Names.ToDictionary(n => n, n => source.GetController(n));
        var states = new HashSet<string>(); var motionInputs = new HashSet<string>();
        var sequences = new Dictionary<string, NativeLayerTransitionState>();
        int clocks = 0, corrections = 0, diagnostics = 0, sequenceSteps = 0;
        foreach (var group in data["groups"])
        {
            var sourceFields = (JObject)group["stateSource"]; var definitions = (JArray)group["parameters"];
            if (group["source"] != null)
            {
                var id = group["source"]; var c = controllers[(string)id["controller"]];
                var s = c["machines"][(int)id["machine"]]["states"][(int)id["state"]];
                Require(Bits((float)s["speed"]) == Bits((float)sourceFields["speed"]) && (uint)s["sp"] == (uint)sourceFields["sp"], "Source state speed differs");
                Require(JToken.DeepEquals(definitions, c["parameters"]), "Source parameter definitions differ");
                Require(states.Add(id.ToString(Newtonsoft.Json.Formatting.None)), "Source state repeated");
                foreach (var row in group["cases"]) Require((bool)row["loop"] == ((int)s["loop"] != 0), "Source loop flag differs");
            }
            var parameters = new NativeParameterBank(definitions); var clock = new NativeStateClock(sourceFields);
            foreach (var row in group["cases"])
            {
                NativeLayerTransitionState state;
                if (row["sequence"] != null && (int)row["sequenceStep"] > 0)
                {
                    state = sequences[(string)row["sequence"]];
                    Require(JToken.DeepEquals(Write(state), row["initialState"]), "Prior live clock result differs before sequence step");
                }
                else state = Read<NativeLayerTransitionState>(row["initialState"]);
                if (row["motionAssetID"] != null)
                {
                    string id = (string)row["motionAssetID"]; motionInputs.Add(id);
                    Require(Bits((float)motions[id]["summary"]["duration"]) == (string)row["lengthBits"], "Clock clip-length fixture differs from source motion");
                }
                var command = Read<NativeTransitionStartCommand>(row["initialCommand"]);
                var before = Parameters(parameters, definitions, (JObject)row["values"], (JObject)row["valueBits"]);
                var actual = clock.Advance(state, command, parameters, Float(row["lengthBits"]), Float(row["deltaBits"]),
                    Float(row["animatorSpeedBits"]), new NativeStateClockOptions((bool)row["current"], (bool)row["loop"],
                    (bool)row["carryFromNext"], (bool)row["overrideEnabled"], Float(row["overrideBits"]),
                    (bool)row["overrideIsSeconds"], (bool)row["subsystemFlag"], (bool)row["timeManagerFlag"]));
                var serialized = Serialize(state, command, actual);
                Require(JToken.DeepEquals(serialized, row["expected"]), "Native clock differs at " + clocks + " actual=" +
                    serialized.ToString(Newtonsoft.Json.Formatting.None) + " expected=" + row["expected"].ToString(Newtonsoft.Json.Formatting.None));
                Require(JToken.DeepEquals(before, Parameters(parameters, definitions)), "Clock changed parameters");
                if (row["sequence"] != null) { sequences[(string)row["sequence"]] = state; sequenceSteps++; }
                if (actual.CorrectedFrameCount) corrections++;
                if (actual.CorrectionDiagnostic) diagnostics++;
                clocks++;
            }
        }
        int progressCases = 0, completed = 0;
        foreach (var row in data["progressCases"])
        {
            var state = Read<NativeLayerTransitionState>(row["initialState"]);
            var result = NativeTransitionProgress.Advance(state, Float(row["deltaBits"]), Float(row["carryBits"]),
                (bool)row["suppressDelta"], (bool)row["priorFixed"]);
            var intermediate = Write(state);
            bool finished = NativeTransitionProgress.TryComplete(state, out uint eventCode);
            var serialized = new JObject {
                ["progressState"] = intermediate, ["previousWeightBits"] = Bits(result.PreviousWeight),
                ["weightBits"] = Bits(result.Weight), ["inverseDurationBits"] = Bits(result.InverseDuration),
                ["state"] = Write(state), ["completed"] = finished, ["eventCode"] = eventCode
            };
            Require(JToken.DeepEquals(serialized, row["expected"]), "Native transition progress/completion differs at " + progressCases +
                " actual=" + serialized.ToString(Newtonsoft.Json.Formatting.None) + " expected=" + row["expected"].ToString(Newtonsoft.Json.Formatting.None));
            if (finished) completed++;
            progressCases++;
        }
        Require(clocks == (int)evidence["clockCases"] && states.Count == 253 && motionInputs.Count == 335, "Source clock coverage differs");
        Require(progressCases == (int)evidence["progressCases"] && completed == (int)evidence["completed"], "Completion coverage differs");
        Require(corrections == (int)evidence["frameCorrections"] && diagnostics == (int)evidence["diagnosticDispatchBypasses"], "Correction coverage differs");
        Require(sequences.Count == 12 && sequenceSteps == 2880, "Continuous clock sequence coverage differs");
        return new JObject {
            ["clockCases"] = clocks, ["authoredStates"] = states.Count, ["motionLengthInputs"] = motionInputs.Count,
            ["continuousSequences"] = sequences.Count, ["continuousSteps"] = sequenceSteps,
            ["progressCases"] = progressCases, ["completed"] = completed, ["frameCorrections"] = corrections,
            ["diagnosticDispatchBypasses"] = diagnostics, ["float32OutputsBitwiseEqual"] = true,
            ["parametersPreserved"] = true, ["blendedMotionLengthQualified"] = false,
            ["playableApplicationQualified"] = false, ["productionCorrectionFlagsQualified"] = false,
            ["fullTickOrderingVerified"] = false, ["controllerPlayable"] = false,
            ["vectorsSha256"] = Hash(path), ["runtimeSha256"] = Hash("Assets/ControllerRuntime/NativeStateClock.cs"),
            ["stateRuntimeSha256"] = Hash("Assets/ControllerRuntime/NativeTransitionSearch.cs"),
            ["auditSha256"] = Hash("Assets/ControllerRuntime/Editor/NativeStateClockAudit.cs")
        };
    }

    public static void RunBatch()
    {
        var report = new JObject { ["pass"] = false };
        try
        {
            report["clock"] = Run(Out + "native-clock-vectors.json", new NativeControllerSource(
                File.ReadAllText("Assets/ControllerRuntime/Data/source-controller-pack.json")));
            report["pass"] = true; Debug.Log("NATIVE_STATE_CLOCK_AUDIT_OK " + report.ToString(Newtonsoft.Json.Formatting.None));
        }
        catch (Exception ex) { report["error"] = ex.ToString(); Debug.LogException(ex); }
        finally { File.WriteAllText(Out + "unity-clock-verification.json", report.ToString()); }
        if (!(bool)report["pass"]) EditorApplication.Exit(1);
    }
}
