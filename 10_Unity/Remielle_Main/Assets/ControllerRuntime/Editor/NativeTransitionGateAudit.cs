using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using Remielle.ControllerRuntime;
using UnityEditor;
using UnityEngine;

public static class NativeTransitionGateAudit
{
    const string Out = "E:/ZZZ/local-only/RemielleControllerImplementation/20260906/";
    const string Pack = "Assets/ControllerRuntime/Data/source-controller-pack.json";

    static void Require(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }

    static string Hash(string path)
    {
        using (var sha = SHA256.Create())
        using (var input = File.OpenRead(path))
            return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
    }

    static float Float(string bits) => BitConverter.ToSingle(BitConverter.GetBytes(Convert.ToUInt32(bits, 16)), 0);
    static string Bits(float value) => BitConverter.ToUInt32(BitConverter.GetBytes(value), 0).ToString("x8");

    static void SetParameters(NativeParameterBank bank, JArray definitions, JObject values)
    {
        foreach (var definition in definitions)
        {
            uint hash = (uint)definition["hash"];
            var value = values[hash.ToString()];
            switch ((int)definition["kind"])
            {
                case 1: bank.SetFloat(hash, (float)value); break;
                case 3: bank.SetInt(hash, (int)value); break;
                case 4: bank.SetBool(hash, (bool)value); break;
                case 9: if ((bool)value) bank.SetTrigger(hash); else bank.ResetTrigger(hash); break;
            }
        }
    }

    static void CheckParameters(NativeParameterBank bank, JArray definitions, JObject values)
    {
        foreach (var definition in definitions)
        {
            uint hash = (uint)definition["hash"];
            var value = values[hash.ToString()];
            switch ((int)definition["kind"])
            {
                case 1: Require(Bits(bank.GetFloat(hash)) == Bits((float)value), "Gate changed float"); break;
                case 3: Require(bank.GetInt(hash) == (int)value, "Gate changed int"); break;
                case 4: Require(bank.GetBool(hash) == (bool)value, "Gate changed bool"); break;
                case 9: Require(bank.GetTrigger(hash) == (bool)value, "Gate consumed trigger"); break;
            }
        }
    }

    public static JObject Run(string path, NativeControllerSource source)
    {
        var data = JObject.Parse(File.ReadAllText(path));
        var evidence = data["evidence"];
        Require(Hash((string)evidence["sourcePack"]["path"]) == (string)evidence["sourcePack"]["sha256"],
            "Transition source pack differs from native vectors");
        var controllers = source.Names.ToDictionary(name => name, name => source.GetController(name));
        const string settingsPath = "Assets/ControllerRuntime/Data/source-time-settings.json";
        var settingsData = JObject.Parse(File.ReadAllText(settingsPath));
        var settings = new NativeControllerTimeSettings(settingsData.ToString());
        var originalSettings = JObject.Parse(File.ReadAllText(Out + "transition-settings/verification.json"));
        Require((bool)originalSettings["pass"] && Hash(settingsPath) == (string)originalSettings["runtimeData"]["sha256"],
            "Original TimeManager verification differs");
        var settingsValues = new[] { settings.FixedTimestep, settings.MaximumAllowedTimestep,
            settings.TimeScale, settings.MaximumParticleTimestep };
        for (int i = 0; i < settingsValues.Length; i++)
            Require(BitConverter.ToString(BitConverter.GetBytes(settingsValues[i])).Replace("-", "").ToLowerInvariant() ==
                (string)originalSettings["fields"][i]["hex"], "TimeManager float differs from original reader");
        Require(settings.EvaluateAgainWhenAnimatorEndTransition == (bool)originalSettings["fields"][4]["value"],
            "End-transition comparison setting differs");
        for (int marker = 0; marker < 2; marker++)
        {
            var policy = settings.TransitionPolicy(marker != 0);
            Require(policy.ComparisonFix && policy.IncludePreviousBoundary == (marker != 0), "TimeManager policy differs");
        }
        int corruptSettings = 0;
        foreach (string field in new[] { "cab", "pathID", "classID", "sha256", "objectSha256", "raw" })
        {
            var damaged = (JObject)settingsData.DeepClone();
            if (field == "raw") damaged["objectBytesHex"] = new string('0', 40);
            else damaged["source"][field] = field == "classID" ? JToken.FromObject(6) : JToken.FromObject("wrong-source");
            bool rejected = false;
            try { new NativeControllerTimeSettings(damaged.ToString()); }
            catch (ArgumentException) { rejected = true; }
            Require(rejected, "Unqualified TimeManager accepted: " + field);
            corruptSettings++;
        }
        int count = 0, timed = 0, conditions = 0, reverse = 0, rejectedOvershoot = 0, invalid = 0;
        var authored = new HashSet<string>();
        foreach (JObject group in data["groups"])
        {
            var transition = (JObject)group["transition"];
            var definitions = (JArray)group["parameters"];
            if (group["source"] != null)
            {
                var origin = group["source"];
                var controller = controllers[(string)origin["controller"]];
                var machine = controller["machines"][(int)origin["machine"]];
                int state = (int)origin["state"];
                var transitions = state < 0 ? machine["anyTransitions"] : machine["states"][state]["transitions"];
                Require(JToken.DeepEquals(transitions[(int)origin["transition"]], transition), "Authored transition changed");
                Require(JToken.DeepEquals(controller["parameters"], definitions), "Authored parameters changed");
                string id = origin.ToString(Newtonsoft.Json.Formatting.None);
                Require(authored.Add(id), "Duplicate authored transition");
                if ((bool)transition["hexit"]) timed++;
            }
            var bank = new NativeParameterBank(definitions);
            var gate = NativeTransitionGate.FromSource(transition);
            foreach (JObject row in group["cases"])
            {
                SetParameters(bank, definitions, (JObject)row["values"]);
                float previous = Float((string)row["previousBits"]), current = Float((string)row["currentBits"]);
                float direction = Float((string)row["directionBits"]);
                bool loop = (bool)row["loop"];
                var policy = new NativeTransitionTimingPolicy((bool)row["comparisonFix"], (bool)row["includePreviousBoundary"]);
                var expected = row["expected"];
                for (int repeat = 0; repeat < 2; repeat++)
                {
                    var actual = gate.Evaluate(bank, previous, current, direction, loop, policy);
                    Require(actual.Eligible == (bool)expected["eligible"] &&
                        Bits(actual.Overshoot) == (string)expected["overshootBits"] &&
                        actual.ConditionsChecked == (int)expected["conditionsChecked"],
                        "Native transition differs at case " + count + " repeat " + repeat +
                        ": actual=" + actual.Eligible + "/" + Bits(actual.Overshoot) + "/" + actual.ConditionsChecked +
                        " row=" + row.ToString(Newtonsoft.Json.Formatting.None));
                    CheckParameters(bank, definitions, (JObject)row["values"]);
                }
                if (direction < 0f) reverse++;
                if (!(bool)expected["eligible"] && Float((string)expected["overshootBits"]) != 0f) rejectedOvershoot++;
                conditions += (int)expected["conditionsChecked"];
                count++;
            }
        }
        Require(authored.Count == 1318 && authored.Count == (int)evidence["authoredTransitions"], "Source transition coverage differs");
        Require(timed == (int)evidence["authoredTimedTransitions"], "Timed transition coverage differs");
        Require(count == (int)evidence["cases"] && conditions == (int)evidence["conditionCalls"], "Native case count differs");
        var empty = new NativeParameterBank(new JArray());
        var fixture = NativeTransitionGate.FromSource(new JObject {
            ["conds"] = new JArray(), ["hexit"] = true, ["useFrameCount"] = false,
            ["exit"] = .5f, ["frameCount"] = 0, ["totalFramesSrc"] = 0
        });
        foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        for (int field = 0; field < 3; field++)
        {
            bool rejected = false;
            try { fixture.Evaluate(empty, field == 0 ? bad : 0f, field == 1 ? bad : 1f,
                field == 2 ? bad : 1f, false, new NativeTransitionTimingPolicy(false, false)); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            Require(rejected, "Nonfinite clock input accepted");
            invalid++;
        }
        return new JObject {
            ["nativeTransitionCases"] = count, ["repeatedEvaluations"] = count * 2,
            ["authoredTransitions"] = authored.Count, ["authoredTimedTransitions"] = timed,
            ["nativeConditionCalls"] = conditions, ["reverseClockCases"] = reverse,
            ["rejectedCandidatesRetainingOvershoot"] = rejectedOvershoot,
            ["invalidClockInputsRejected"] = invalid,
            ["parameterValuesPreserved"] = true, ["overshootBitwiseEqual"] = true,
            ["bothComparisonPoliciesTested"] = true, ["productionPolicyQualified"] = false,
            ["serializedTimeManagerQualified"] = true, ["originalTimeManagerFields"] = 5,
            ["corruptTimeManagerSourcesRejected"] = corruptSettings,
            ["stateEndTransitionMarkerLifecycleVerified"] = false,
            ["stateClockAdvancementVerified"] = false, ["transitionCommitVerified"] = false,
            ["vectorsSha256"] = Hash(path), ["runtimeSha256"] = Hash("Assets/ControllerRuntime/NativeTransitionGate.cs"),
            ["timeSettingsSha256"] = Hash(settingsPath),
            ["timeSettingsRuntimeSha256"] = Hash("Assets/ControllerRuntime/NativeControllerTimeSettings.cs"),
            ["auditSha256"] = Hash("Assets/ControllerRuntime/Editor/NativeTransitionGateAudit.cs")
        };
    }

    public static void RunBatch()
    {
        var result = new JObject { ["pass"] = false };
        try
        {
            result["transitionWindows"] = Run(Out + "native-transition-window-vectors.json",
                new NativeControllerSource(File.ReadAllText(Pack)));
            result["pass"] = true;
            Debug.Log("NATIVE_TRANSITION_WINDOW_AUDIT_OK " + result.ToString(Newtonsoft.Json.Formatting.None));
        }
        catch (Exception ex) { result["error"] = ex.ToString(); Debug.LogException(ex); }
        finally { File.WriteAllText(Out + "unity-transition-window-verification.json", result.ToString()); }
        if (!(bool)result["pass"]) EditorApplication.Exit(1);
    }
}
