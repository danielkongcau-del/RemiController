// zcode 2026-09-06, loop aggregate-pose-resources: consumes
// native-aggregate-pose-resources-vectors.json and compares the managed
// NativeAggregatePoseResources preparation against the original execution
// snapshot twice per case. Pointer fields are compared through derived
// properties only (non-null/distinct/length); booleans, counters, call
// counts and allocation records are compared exactly (size/alignment/outer).
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using Remielle.ControllerRuntime;
using UnityEngine;

public static class NativeAggregatePoseResourcesAudit
{
    const string Out = "E:/ZZZ/ZCode/90_Builds/ControllerImplementation/20260906/";
    const string Project = "E:/ZZZ/ZCode/10_Unity/Remielle_Main/Assets/";

    static void Require(bool ok, string why) { if (!ok) throw new Exception(why); }
    static string Hash(string path) { using (var s = File.OpenRead(path)) using (var h = SHA256.Create()) return NativeMotionArchive.Hex(h.ComputeHash(s)); }

    static void EqualCalls(JObject expected, NativeAggregatePoseResources resources, string where)
    {
        var actual = new JObject();
        foreach (var pair in resources.Calls.OrderBy(k => k.Key)) actual[pair.Key] = pair.Value;
        var want = new JObject();
        foreach (var pair in expected.Properties().OrderBy(p => p.Name)) want[pair.Name] = pair.Value;
        Require(JToken.DeepEquals(actual, want),
            where + " call counts differ; actual " + actual.ToString(Newtonsoft.Json.Formatting.None)
            + " expected " + want.ToString(Newtonsoft.Json.Formatting.None));
    }

    static void EqualAllocations(JArray expected, NativeAggregatePoseResources resources, string where)
    {
        // The oracle records {size, alignment, outer}; the managed allocator
        // fixture is never the outer arena owner, so outer is always false.
        var actual = new JArray(resources.Allocations.Select(p => new JObject {
            ["size"] = (int)(p >> 32), ["alignment"] = (int)(p & 0xffffffff), ["outer"] = false }));
        Require(JToken.DeepEquals(actual, expected),
            where + " allocations differ; actual " + actual.ToString(Newtonsoft.Json.Formatting.None)
            + " expected " + expected.ToString(Newtonsoft.Json.Formatting.None));
    }

    public static JObject Run(string path)
    {
        var data = JObject.Parse(File.ReadAllText(path));
        var e = (JObject)data["evidence"];
        foreach (string k in new[] { "source", "sourcePack", "generator", "oracle" })
            Require(Hash((string)e[k]["path"]) == (string)e[k]["sha256"], "Aggregate source changed: " + k);
        foreach (var r in e["dependencies"])
            Require(Hash((string)r["path"]) == (string)r["sha256"], "Aggregate dependency changed");
        int cases = 0; const int repeats = 2;
        for (int repeat = 0; repeat < repeats; repeat++)
        {
            foreach (var row in data["cases"])
            {
                string kind = (string)row["kind"];
                int count = (int)row["count"];
                if (kind == "ready-reentry")
                {
                    // The original fixture starts with the gate set, containers
                    // pre-allocated and the work arrays absent; a managed node
                    // reaches the gate through a full preparation, so only the
                    // re-entry itself and the preserved state are compared.
                    var gate = NativeAggregatePoseResources.Preset(count);
                    gate.Prepare(count, new object());
                    gate.Calls.Clear();
                    gate.Allocations.Clear();
                    gate.MarkResourcesDirty();
                    gate.Prepare(count, new object());
                    var expectedGate = (JObject)row["expected"];
                    Require(gate.Ready == (bool)expectedGate["ready"], "gate readiness differs from original");
                    Require(gate.Dirty == (bool)expectedGate["dirty"], "gate dirty differs from original");
                    EqualCalls((JObject)expectedGate["calls"], gate, kind);
                    Require(gate.Calls.Count == 1, "gate re-entry must only enter the original function");
                    Require(gate.SlotTable.Length == count, "gate re-entry must keep prepared slots");
                    Require(gate.Marks.Length == count && gate.Marks.All(m => m == 1), "gate re-entry must keep marks");
                    EqualAllocations((JArray)expectedGate["allocations"], gate, kind);
                    cases++; continue;
                }
                // Drift cases start from an empty node (first build) or from
                // the preset container count (shrink); all other kinds start
                // matched to the demand count.
                int initial = (int?)row["presetCount"] ?? (kind == "drift-first-build" ? 0 : count);
                var resources = NativeAggregatePoseResources.Preset(initial);
                resources.Prepare(count, new object());
                var expected = (JObject)row["expected"];
                Require(resources.Ready == (bool)expected["ready"], "ready differs");
                Require(resources.Dirty == (bool)expected["dirty"], "dirty differs");
                Require(resources.SlotTable.Length == (int)expected["slotCount"], "slot count differs");
                Require(resources.Marks.Length == count, "mark length differs");
                Require(resources.Marks.SequenceEqual(((JArray)expected["flags"]).Select(f => (byte)f)), "marks differ from original default-allow bytes");
                Require(resources.Derived != null, "derived set missing");
                Require(resources.SlotTable.All(t => t != null && t.RegionsFilled && t.RegionFillCalls == 2), "layer table factory result differs");
                Require(resources.SlotTable.Distinct().Count() == resources.SlotTable.Length, "slot tables are not distinct per layer");
                var container1 = (JObject)expected["container1"];
                Require(resources.LayerCount == (int)container1["count"], "container demand count differs");
                Require(resources.ContainerCapacity == (int)container1["capacity"], "container capacity differs");
                // The metadata container count follows the source count while
                // its capacity only grows (shrink keeps the larger capacity).
                var container2 = (JObject)expected["container2"];
                Require(resources.MetadataCount == count, "metadata count differs");
                Require(resources.MetadataCapacity == (int)container2["capacity"], "metadata capacity differs");
                EqualCalls((JObject)expected["calls"], resources, kind + " count=" + count);
                EqualAllocations((JArray)expected["allocations"], resources, kind + " count=" + count);
                cases++;
            }
        }
        return new JObject { ["pass"] = true, ["cases"] = cases, ["repeats"] = repeats,
            ["vectorsSha256"] = Hash(path),
            ["runtimeSha256"] = Hash(Project + "ControllerRuntime/NativeAggregatePoseResources.cs"),
            ["auditSha256"] = Hash(Project + "ControllerRuntime/Editor/NativeAggregatePoseResourcesAudit.cs") };
    }

    public static void RunStandalone()
    {
        var result = new JObject { ["schema"] = "remielle-aggregate-pose-resources-unity-v1", ["pass"] = false };
        try
        {
            result["aggregatePoseResources"] = Run(Out + "native-aggregate-pose-resources-vectors.json");
            result["pass"] = true;
            Debug.Log("NATIVE_AGGREGATE_POSE_RESOURCES_UNITY_OK " + result.ToString(Newtonsoft.Json.Formatting.None));
        }
        catch (Exception error) { result["error"] = error.ToString(); throw; }
        finally { File.WriteAllText(Out + "unity-aggregate-pose-resources-verification.json", result.ToString()); }
    }
}
