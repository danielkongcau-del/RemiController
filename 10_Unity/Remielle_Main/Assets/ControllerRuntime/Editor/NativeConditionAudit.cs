using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using Remielle.ControllerRuntime;

public static class NativeConditionAudit
{
    static void Require(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }

    static float FloatBits(string hex) => BitConverter.ToSingle(BitConverter.GetBytes(Convert.ToUInt32(hex, 16)), 0);
    static float FloatBits(uint bits) => BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);

    public static JObject Run(string vectorPath, NativeControllerSource source)
    {
        var data = JObject.Parse(File.ReadAllText(vectorPath));
        using (var sha = SHA256.Create())
        using (var stream = File.OpenRead((string)data["sourcePack"]["path"]))
            Require(BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant() ==
                (string)data["sourcePack"]["sha256"], "Condition vectors refer to a different source pack");
        var banks = source.Names.ToDictionary(name => name, name => source.CreateParameters(name));
        var controllers = source.Names.ToDictionary(name => name, name => source.GetController(name));
        var fixtures = new Dictionary<(int, uint), NativeParameterBank>();
        var authored = new HashSet<string>();
        int count = 0, authoredCases = 0;
        foreach (var row in data["vectors"])
        {
            int kind = (int)row["kind"];
            uint parameter = (uint)row["parameter"];
            uint storedParameter = (bool)row["present"] ? parameter : parameter ^ 0x01010101u;
            var condition = new NativeCondition((uint)row["mode"], parameter, FloatBits((string)row["thresholdBits"]));
            NativeParameterBank bank;
            if (row["source"] is JObject original)
            {
                string name = (string)original["controller"];
                bank = banks[name];
                var machine = controllers[name]["machines"][(int)original["machine"]];
                JToken transitions;
                switch ((string)original["scope"])
                {
                    case "state": transitions = machine["states"][(int)original["index"]]["transitions"]; break;
                    case "any": transitions = machine["anyTransitions"]; break;
                    case "selector": transitions = machine["selectors"][(int)original["index"]]["transitions"]; break;
                    default: throw new Exception("Unknown condition scope");
                }
                var transition = transitions[(int)original["transition"]];
                var fields = (JArray)(transition["conds"] ?? transition["conditions"])[(int)original["condition"]];
                Require(JToken.DeepEquals(fields, original["original"]), "Authored condition differs from raw source pack");
                var parsed = NativeCondition.FromSource(fields);
                Require(parsed.Mode == condition.Mode && parsed.Parameter == condition.Parameter && parsed.Threshold == condition.Threshold,
                    "Original condition decoding differs");
                condition = parsed;
                authored.Add(original.ToString(Newtonsoft.Json.Formatting.None));
                authoredCases++;
            }
            else
            {
                var key = (kind, storedParameter);
                if (!fixtures.TryGetValue(key, out bank))
                {
                    bank = new NativeParameterBank(new JArray(new JObject {
                        ["name"] = "fixture", ["hash"] = storedParameter, ["kind"] = kind,
                        ["defaultValue"] = kind == 4 || kind == 9 ? (JToken)false : (JToken)0
                    }));
                    fixtures.Add(key, bank);
                }
            }
            switch (kind)
            {
                case 1: bank.SetFloat(storedParameter, FloatBits((uint)row["value"])); break;
                case 3: bank.SetInt(storedParameter, (int)row["value"]); break;
                case 4: bank.SetBool(storedParameter, (bool)row["value"]); break;
                case 9:
                    if ((bool)row["value"]) bank.SetTrigger(storedParameter); else bank.ResetTrigger(storedParameter);
                    break;
                default: throw new Exception("Uncovered source parameter kind");
            }
            for (int repeat = 0; repeat < 2; repeat++)
                Require(condition.Evaluate(bank) == (bool)row["expected"], "Native condition differs at vector " + count);
            if (kind == 9) Require(bank.GetTrigger(storedParameter) == (bool)row["value"], "Condition consumed a trigger");
            count++;
        }
        Require(count == (int)data["fixtureCases"] + (int)data["authoredCases"], "Condition vector count");
        Require(authored.Count == (int)data["authoredConditions"] && authoredCases == (int)data["authoredCases"], "Authored condition coverage");
        return new JObject {
            ["nativeConditionCases"] = count, ["conditionEvaluations"] = count * 2,
            ["authoredConditions"] = authored.Count, ["authoredConditionCases"] = authoredCases,
            ["mode9"] = "GreaterOrEqual", ["mode10"] = "LessOrEqual",
            ["triggerReadsPreserved"] = true
        };
    }
}
