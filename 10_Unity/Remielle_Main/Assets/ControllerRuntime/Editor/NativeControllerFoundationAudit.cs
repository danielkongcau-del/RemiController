using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Remielle.ControllerRuntime;

public static class NativeControllerFoundationAudit
{
    const string Out = "E:/ZZZ/local-only/RemielleControllerImplementation/20260906/";
    const string Prep = "E:/ZZZ/local-only/RemielleControllerPreparation/20260906/";
    const string Pack = "Assets/ControllerRuntime/Data/source-controller-pack.json";

    static void Require(bool success, string message)
    {
        if (!success) throw new Exception(message);
    }

    public static void Run()
    {
        var result = new JObject { ["schema"] = "remielle-controller-foundation-unity-v1", ["pass"] = false };
        try
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(Pack);
            Require(asset, "Controller source pack did not import");
            var source = new NativeControllerSource(asset.text);
            int parameters = 0, hashChecks = 0, resolvedLeaves = 0, unresolvedLeaves = 0, blendRoots = 0;
            foreach (string name in source.Names)
            {
                var c = source.GetController(name);
                var bank = source.CreateParameters(name);
                foreach (var p in c["parameters"])
                {
                    uint hash = (uint)p["hash"];
                    Require(bank.Hash((string)p["name"]) == hash, "Original parameter identity changed");
                    Require(unchecked((uint)Animator.StringToHash((string)p["name"])) == hash, "Unity parameter hash differs from source");
                    hashChecks++;
                    switch ((int)p["kind"])
                    {
                        case 1:
                            Require(bank.GetFloat(hash) == (float)p["defaultValue"], "Float default");
                            bank.SetFloat(hash, .25f); Require(bank.GetFloat(hash) == .25f, "Float update"); break;
                        case 3:
                            Require(bank.GetInt(hash) == (int)p["defaultValue"], "Int default");
                            bank.SetInt(hash, -2147483647); Require(bank.GetInt(hash) == -2147483647, "Int update"); break;
                        case 4:
                            Require(bank.GetBool(hash) == (bool)p["defaultValue"], "Bool default");
                            bank.SetBool(hash, !bank.GetBool(hash)); Require(bank.GetBool(hash) != (bool)p["defaultValue"], "Bool update"); break;
                        case 9:
                            Require(bank.GetTrigger(hash) == (bool)p["defaultValue"], "Trigger default");
                            bank.SetTrigger(hash); Require(bank.GetTrigger(hash) && bank.GetTrigger(hash), "Trigger reads consumed pending value");
                            bank.ResetTrigger(hash); Require(!bank.GetTrigger(hash), "Trigger reset"); break;
                    }
                    parameters++;
                }
                for (int li = 0; li < ((JArray)c["layers"]).Count; li++)
                {
                    var layer = c["layers"][li];
                    int mi = (int)layer["smIdx"], set = (int)layer["smms"];
                    var states = (JArray)c["machines"][mi]["states"];
                    for (int si = 0; si < states.Count; si++)
                    {
                        var s = states[si];
                        int tree = (int)s["blendTreeIndices"][set];
                        if (tree < 0) { Require(source.ResolveLeaf(name, li, si) == null, "Placeholder has a motion"); continue; }
                        var nodes = (JArray)s["trees"][tree]["nodes"];
                        for (int ni = 0; ni < nodes.Count; ni++)
                        {
                            if (((JArray)nodes[ni]["childIndices"]).Count != 0)
                            {
                                bool rejected = false;
                                try { source.ResolveLeaf(name, li, si, ni); } catch (InvalidOperationException) { rejected = true; }
                                Require(rejected, "Blend root was mistaken for a single clip"); blendRoots++; continue;
                            }
                            var binding = c["clips"][(int)nodes[ni]["clipIndex"]];
                            if ((string)binding["resolution"] == "unresolved")
                            {
                                bool rejected = false;
                                try { source.ResolveLeaf(name, li, si, ni); } catch (InvalidOperationException) { rejected = true; }
                                Require(rejected, "Ambiguous/missing source clip silently selected"); unresolvedLeaves++; continue;
                            }
                            var actual = source.ResolveLeaf(name, li, si, ni);
                            Require(JToken.DeepEquals(binding, actual), "Layer motion-set / PPtr mismatch"); resolvedLeaves++;
                        }
                    }
                }
            }

            // These expected PPtrs are independently retained in the native RANI
            // build input history, and distinguish Idle from movement clip slots.
            var main = source.GetController("Avatar_Female_Size02_RemielleOrigin_Controller");
            foreach (var expected in new[] {
                ("Idle", "3561100767826961103"), ("Walk_Start", "-1678135712426977003"),
                ("Walk_Loop", "-2893577299971434832"), ("RunLoop_01", "7376968608536389763"),
                ("Attack_Normal_01", "7100607445617743312") })
            {
                int index = ((JArray)main["machines"][0]["states"]).Single(s => (string)s["name"] == expected.Item1).Value<int>("state");
                Require((string)source.ResolveLeaf((string)main["name"], 0, index)["pathID"] == expected.Item2,
                    "Known original motion pointer changed: " + expected.Item1);
            }

            var vectors = JObject.Parse(File.ReadAllText(Out + "native-visibility-vectors.json"));
            int nativeSteps = 0;
            foreach (var sequence in vectors["sequences"])
            {
                var stack = new NativeVisibilityStack(true);
                foreach (var step in sequence)
                {
                    string tag = (string)step["tag"];
                    if ((bool)step["remove"]) stack.Pop(tag); else stack.Push(tag, (bool)step["value"]);
                    Require(stack.Value == (bool)step["expectedValue"], "Native visibility value differs at " + nativeSteps);
                    Require(stack.WinningSlot == (int)step["expectedWinningSlot"], "Native slot priority differs at " + nativeSteps);
                    Require(stack.ActiveTagCount == (int)step["expectedTagCount"], "Tag count differs at " + nativeSteps);
                    nativeSteps++;
                }
                stack.Push(null, false); stack.Push("", false); stack.Pop(null); stack.Pop("");
                var last = sequence.Last;
                Require(stack.Value == (bool)last["expectedValue"] && stack.ActiveTagCount == (int)last["expectedTagCount"], "Empty tag was not ignored");
            }

            int authoredObservations = 0;
            var traces = JObject.Parse(File.ReadAllText(Prep + "event-trace-verification.json"));
            foreach (var trace in traces["traces"])
            {
                var bank = new NativeRendererVisibility();
                foreach (var row in trace["observed"]) bank.Register((string)row["renderer"], true);
                int commandIndex = 0;
                foreach (var command in trace["commands"])
                {
                    if ((string)command["$type"] == "PopRenderVisibleAction") bank.Pop((string)command["Tag"]);
                    else bank.Push((string)command["Tag"], (bool)command["Visible"], (bool?)command["ApplyAllRenderers"] ?? false,
                        command["Paths"]?.Values<string>() ?? Enumerable.Empty<string>());
                    foreach (var row in trace["observed"])
                    {
                        Require(bank.GetVisible((string)row["renderer"]) == (bool)row["visibleAfterEachCommand"][commandIndex], "Authored visibility payload differs");
                        authoredObservations++;
                    }
                    commandIndex++;
                }
            }
            var strict = new NativeRendererVisibility(); strict.Register("source:A", true);
            bool unknownRejected = false;
            try { strict.Push("hide", false, false, new[] { "source:A", "missing" }); }
            catch (System.Collections.Generic.KeyNotFoundException) { unknownRejected = true; }
            Require(unknownRejected && strict.GetVisible("source:A"), "Partially applied unresolved renderer command");

            result["conditions"] = NativeConditionAudit.Run(Out + "native-condition-vectors.json", source);
            result["selectors"] = NativeSelectorAudit.Run(Out + "native-selector-vectors.json", source);
            result["transitionWindows"] = NativeTransitionGateAudit.Run(Out + "native-transition-window-vectors.json", source);
            File.WriteAllText(Out + "unity-transition-window-verification.json", new JObject {
                ["pass"] = true, ["transitionWindows"] = result["transitionWindows"].DeepClone()
            }.ToString());
            result["transitionCommit"] = NativeTransitionCommitAudit.Run(Out + "native-transition-commit-vectors.json", source);
            File.WriteAllText(Out + "unity-transition-commit-verification.json", new JObject {
                ["pass"] = true, ["transitionCommit"] = result["transitionCommit"].DeepClone()
            }.ToString());
            result["stateClock"] = NativeStateClockAudit.Run(Out + "native-clock-vectors.json", source);
            File.WriteAllText(Out + "unity-clock-verification.json", new JObject {
                ["pass"] = true, ["clock"] = result["stateClock"].DeepClone()
            }.ToString());
            result["blendTrees"] = NativeBlendTreeAudit.Run(Out + "native-blend-vectors.json", source);
            File.WriteAllText(Out + "unity-blend-verification.json", new JObject {
                ["pass"] = true, ["blendTrees"] = result["blendTrees"].DeepClone()
            }.ToString());
            result["stateMotions"] = NativeStateMotionsAudit.Run(Out + "native-state-motion-vectors.json", source);
            File.WriteAllText(Out + "unity-state-motion-verification.json", new JObject {
                ["pass"] = true, ["blendTrees"] = result["blendTrees"].DeepClone(), ["stateMotions"] = result["stateMotions"].DeepClone()
            }.ToString());
            result["clipTime"] = NativeClipTimeAudit.Run(Out + "native-clip-time-vectors.json", source);
            File.WriteAllText(Out + "unity-clip-time-verification.json", new JObject {
                ["pass"] = true, ["clipTime"] = result["clipTime"].DeepClone()
            }.ToString());
            result["layerWeights"] = NativeLayerWeightsAudit.Run(Out + "native-layer-vectors.json", source);
            File.WriteAllText(Out + "unity-layer-verification.json", new JObject {
                ["pass"] = true, ["layerWeights"] = result["layerWeights"].DeepClone()
            }.ToString());
            result["stateOutputWeights"] = NativeStateOutputWeightsAudit.Run(Out + "native-state-output-vectors.json", source);
            File.WriteAllText(Out + "unity-state-output-verification.json", new JObject {
                ["pass"] = true, ["stateOutputWeights"] = result["stateOutputWeights"].DeepClone()
            }.ToString());
            result["mixerEvaluation"] = NativeMixerEvaluationAudit.Run(Out + "native-mixer-evaluation-vectors.json");
            File.WriteAllText(Out + "unity-mixer-evaluation-verification.json", new JObject {
                ["pass"] = true, ["mixerEvaluation"] = result["mixerEvaluation"].DeepClone()
            }.ToString());
            result["poseMath"] = NativePoseMathAudit.Run(Out + "native-pose-math-vectors.json");
            File.WriteAllText(Out + "unity-pose-math-verification.json", new JObject {
                ["pass"] = true, ["poseMath"] = result["poseMath"].DeepClone()
            }.ToString());
            result["poseCallbacks"] = NativePoseCallbackAudit.Run(Out + "native-pose-callback-vectors.json");
            File.WriteAllText(Out + "unity-pose-callback-verification.json", new JObject {
                ["pass"] = true, ["poseCallbacks"] = result["poseCallbacks"].DeepClone()
            }.ToString());
            result["samplePose"] = NativeSamplePoseAudit.Run(Out + "native-sample-pose-vectors.json");
            File.WriteAllText(Out + "unity-sample-pose-verification.json", new JObject {
                ["pass"] = true, ["samplePose"] = result["samplePose"].DeepClone()
            }.ToString());
            result["sourceBindingRules"] = NativeSourceBindingAudit.Run(Out + "native-binding-rules-vectors.json");
            File.WriteAllText(Out + "unity-source-binding-verification.json", new JObject {
                ["pass"] = true, ["sourceBindingRules"] = result["sourceBindingRules"].DeepClone()
            }.ToString());
            result["clipPoseChannels"] = NativeClipPoseChannelsAudit.Run(Out + "native-clip-pose-channels-vectors.json");
            File.WriteAllText(Out + "unity-clip-pose-channels-verification.json", new JObject {
                ["pass"] = true, ["clipPoseChannels"] = result["clipPoseChannels"].DeepClone()
            }.ToString());
            result["stateClipBindings"] = NativeStateClipBindingsAudit.Run(Out + "native-state-clip-bindings-vectors.json", source);
            File.WriteAllText(Out + "unity-state-clip-bindings-verification.json", new JObject {
                ["pass"] = true, ["stateClipBindings"] = result["stateClipBindings"].DeepClone()
            }.ToString());
            result["defaultPoseInput"] = NativeDefaultPoseInputAudit.Run(Out + "native-default-pose-input-vectors.json", source);
            File.WriteAllText(Out + "unity-default-pose-input-verification.json", new JObject {
                ["pass"] = true, ["defaultPoseInput"] = result["defaultPoseInput"].DeepClone()
            }.ToString());
            result["poseResources"] = NativePoseResourcesAudit.Run(Out + "native-pose-resources-vectors.json");
            File.WriteAllText(Out + "unity-pose-resources-verification.json", new JObject {
                ["pass"] = true, ["poseResources"] = result["poseResources"].DeepClone()
            }.ToString());
            // zcode 2026-09-06, loop aggregate-pose-resources: register the cd3060
            // aggregate-node preparation audit so it joins this unified gate.
            result["aggregatePoseResources"] = NativeAggregatePoseResourcesAudit.Run(Out + "native-aggregate-pose-resources-vectors.json");
            File.WriteAllText(Out + "unity-aggregate-pose-resources-verification.json", new JObject {
                ["pass"] = true, ["aggregatePoseResources"] = result["aggregatePoseResources"].DeepClone()
            }.ToString());
            result["stateGraphBindings"] = NativeStateGraphBindingsAudit.Run(Out + "native-state-graph-bindings-vectors.json", source);
            File.WriteAllText(Out + "unity-state-graph-bindings-verification.json", new JObject {
                ["pass"] = true, ["stateGraphBindings"] = result["stateGraphBindings"].DeepClone()
            }.ToString());
            result["graphLifecycle"] = NativeGraphLifecycleAudit.Run(Out + "native-graph-lifecycle-vectors.json", source);
            File.WriteAllText(Out + "unity-graph-lifecycle-verification.json", new JObject {
                ["pass"] = true, ["graphLifecycle"] = result["graphLifecycle"].DeepClone()
            }.ToString());
            result["graphPoseSchedule"] = NativeGraphPoseScheduleAudit.Run(Out + "native-graph-pose-schedule-vectors.json", source);
            File.WriteAllText(Out + "unity-graph-pose-schedule-verification.json", new JObject {
                ["pass"] = true, ["graphPoseSchedule"] = result["graphPoseSchedule"].DeepClone()
            }.ToString());
            result["controllerPoseHistory"] = NativeControllerPoseHistoryAudit.Run(Out + "native-controller-pose-history-vectors.json", source);
            File.WriteAllText(Out + "unity-controller-pose-history-verification.json", new JObject {
                ["pass"] = true, ["controllerPoseHistory"] = result["controllerPoseHistory"].DeepClone()
            }.ToString());
            result["layerPoseMath"] = NativeLayerPoseMathAudit.Run(Out + "native-layer-pose-math-vectors.json");
            File.WriteAllText(Out + "unity-layer-pose-math-verification.json", new JObject {
                ["pass"] = true, ["layerPoseMath"] = result["layerPoseMath"].DeepClone()
            }.ToString());
            result["layerPoseMixer"] = NativeLayerPoseMixerAudit.Run(Out + "native-layer-pose-mixer-vectors.json", source);
            File.WriteAllText(Out + "unity-layer-pose-mixer-verification.json", new JObject {
                ["pass"] = true, ["layerPoseMixer"] = result["layerPoseMixer"].DeepClone()
            }.ToString());
            result["pass"] = true;
            result["controllers"] = source.Names.Count(); result["parameters"] = parameters;
            result["sourceParameterHashesVerifiedInUnity"] = hashChecks;
            result["resolvedLayerLeaves"] = resolvedLeaves; result["unresolvedLeavesRejected"] = unresolvedLeaves;
            result["blendRootsRequireEvaluation"] = blendRoots;
            result["nativeVisibilitySteps"] = nativeSteps;
            result["authoredRendererObservations"] = authoredObservations;
            result["sceneOrPrefabSaved"] = false;
            result["controllerPlaybackImplemented"] = false;
            result["boundary"] = "Source layer fields and masks, parameters, conditions, selectors, time windows, serialized TimeManager, same-list transition-start commitment, original layer timing weights feeding trees/length/clock, state-output loops and timed mixer-input history with explicit graph fixtures, time-history evaluation and typed input selection, masked pose accumulation/default-finalization math with original host SSE normalization, complete cc5fc0 pose-channel callbacks with context+82=false and explicit child producers, source tree/output/layer composition, leaf time dispatch/history and native sample-time mapping, transition progress and completion-copy blocks, and renderer visibility. No claim of runtime layer-weight initialization/producers, state graph/flag or evaluation-input producers, graph allocation, production loop/evaluation policies, additional pose context+82 or other pose callbacks/source binding/layer and root-motion mixing, full tick ordering, production clock-correction flags, global interruption ordering, observer callbacks, trigger reset scheduling, gameplay decisions or VFX rendering.";
            Debug.Log("CONTROLLER_FOUNDATION_OK " + result.ToString(Newtonsoft.Json.Formatting.None));
        }
        catch (Exception error) { result["error"] = error.ToString(); throw; }
        finally { File.WriteAllText(Out + "unity-foundation-verification.json", result.ToString()); }
    }
}
