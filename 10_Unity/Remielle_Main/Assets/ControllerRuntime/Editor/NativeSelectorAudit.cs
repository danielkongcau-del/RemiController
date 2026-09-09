using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using Remielle.ControllerRuntime;

public static class NativeSelectorAudit
{
    static void Require(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }

    public static JObject Run(string path, NativeControllerSource source)
    {
        var data = JObject.Parse(File.ReadAllText(path));
        using (var sha = SHA256.Create())
        using (var stream = File.OpenRead((string)data["evidence"]["sourcePack"]["path"]))
            Require(BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant() ==
                (string)data["evidence"]["sourcePack"]["sha256"], "Selector source pack differs from native vectors");
        var banks = source.Names.ToDictionary(name => name, name => source.CreateParameters(name));
        var definitions = source.Names.ToDictionary(name => name, name => (JArray)source.GetController(name)["parameters"]);
        var visited = new HashSet<string>();
        var branches = new HashSet<string>();
        int count = 0, nested = 0, marked = 0;
        foreach (var row in data["cases"])
        {
            string name = (string)row["controller"];
            var bank = banks[name];
            foreach (var p in definitions[name])
            {
                uint hash = (uint)p["hash"];
                var value = row["values"][hash.ToString()];
                switch ((int)p["kind"])
                {
                    case 1: bank.SetFloat(hash, (float)value); break;
                    case 3: bank.SetInt(hash, (int)value); break;
                    case 4: bank.SetBool(hash, (bool)value); break;
                    case 9: if ((bool)value) bank.SetTrigger(hash); else bank.ResetTrigger(hash); break;
                }
            }
            var priorUsed = row["used"].Values<uint>().ToArray();
            var actual = source.ResolveSelector(name, (int)row["machine"], (uint)row["entry"], bank, (uint)row["flags"], priorUsed);
            var expected = row["expected"];
            Require(actual.State == (uint)expected["state"] && actual.TraversalFlags == (uint)expected["flags"], "Native selector result differs at " + count);
            Require(actual.UsedTriggers.SequenceEqual(expected["used"].Values<uint>()), "Native used-trigger marks differ at " + count);
            Require(actual.Visited.SequenceEqual(expected["visited"].Values<int>()), "Native selector traversal order differs at " + count);
            Require(JToken.DeepEquals(JArray.FromObject(actual.Matched), expected["matched"]), "Native first-match branch order differs at " + count);
            foreach (var p in definitions[name])
            {
                uint hash = (uint)p["hash"];
                var value = row["values"][hash.ToString()];
                switch ((int)p["kind"])
                {
                    case 1: Require(bank.GetFloat(hash) == (float)value, "Routing changed float"); break;
                    case 3: Require(bank.GetInt(hash) == (int)value, "Routing changed int"); break;
                    case 4: Require(bank.GetBool(hash) == (bool)value, "Routing changed bool"); break;
                    case 9: Require(bank.GetTrigger(hash) == (bool)value, "Routing consumed trigger"); break;
                }
            }
            foreach (int index in actual.Visited) visited.Add(name + ":" + row["machine"] + ":" + index);
            foreach (var branch in actual.Matched) branches.Add(name + ":" + row["machine"] + ":" + branch[0] + ":" + branch[1]);
            if (actual.Visited.Count > 1) nested++;
            if (actual.UsedTriggers.Except(priorUsed).Any()) marked++;
            count++;
        }
        Require(count == (int)data["evidence"]["cases"], "Selector case count differs");
        Require(visited.Count == (int)data["evidence"]["selectors"], "A source selector was not visited");
        Require(branches.Count == (int)data["evidence"]["reachableBranchesMatched"], "Reachable selector branch coverage differs");
        return new JObject {
            ["nativeSelectorCases"] = count, ["sourceSelectorsVisited"] = visited.Count,
            ["nestedRoutes"] = nested, ["routesAddingTriggerMarks"] = marked,
            ["reachableBranchesMatched"] = branches.Count,
            ["sourceBranchesShadowedByEarlierRules"] = ((JArray)data["evidence"]["shadowedBranches"]).Count,
            ["parameterValuesPreserved"] = true,
            ["stateCommitOrTriggerResetImplemented"] = false
        };
    }
}
