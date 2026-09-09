using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Remielle.ControllerRuntime;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class NativeMotionBankAudit
{
    const string Out = "E:/ZZZ/local-only/RemielleControllerImplementation/20260906";

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }

    public static void RunAll()
    {
        Run();
        NativeMotionCodecAudit.Run();
        NativeScalarCodecAudit.Run();
        NativeControllerAvatarAudit.Run();
    }

    public static void Run()
    {
        var result = new JObject { ["schema"] = "remielle-motion-bank-unity-v1", ["pass"] = false };
        GameObject instance = null, funnelRoot = null;
        try
        {
            string directory = Path.Combine(Application.streamingAssetsPath, "RemielleControllerMotions");
            var index = JObject.Parse(File.ReadAllText(Path.Combine(directory, "index.json")));
            var profiles = ((JArray)JObject.Parse(File.ReadAllText(Path.Combine(directory, "binding-profiles.json")))["profiles"])
                .Cast<JObject>().ToDictionary(x => (string)x["name"]);
            var bank = new NativeMotionBank(directory);
            instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/V3/Remielle_V3_Animated.prefab"));
            var bridge = instance.GetComponent<RemielleNativeAnimation>();
            bridge.autoplay = false; bridge.enabled = false;
            bridge.nativeAnimation.Stop(); bridge.nativeAnimation.enabled = false;
            var originRoot = bridge.nativeAnimation.transform;
            funnelRoot = MakeRig(profiles["Funnel"]);
            var funnelAssets = new HashSet<string>(index["slots"].Where(x => ((string)x["controller"]).Contains("Funnel"))
                .Select(x => (string)x["assetID"]));
            int motions = 0, digestChecks = 0, frameApplications = 0, transformChecks = 0, morphChecks = 0;
            int partialOrigin = 0, strictRejections = 0, sourcePathHashes = 0;
            long floats = 0;
            var rows = new JArray();
            foreach (var profile in profiles.Values)
                foreach (var node in profile["nodes"])
                {
                    Require(unchecked((uint)Animator.StringToHash((string)node["path"])) == (uint)node["pathHash"], "Avatar StringToHash differs");
                    sourcePathHashes++;
                }
            foreach (string id in bank.AssetIDs)
            {
                var archive = bank.Load(id);
                var record = bank.Record(id);
                Require(archive.Name == (string)record["summary"]["name"], "Archive name differs");
                foreach (var array in ((JObject)record["arrays"]).Properties())
                {
                    Require(archive.ArraySha256(array.Name) == (string)array.Value["sha256"], id + " array differs: " + array.Name);
                    floats += (long)array.Value["bytes"] / 4;
                    digestChecks++;
                }
                var contexts = new List<(string name, Transform root)> { ("Origin", originRoot) };
                if (funnelAssets.Contains(id)) contexts.Add(("Funnel", funnelRoot.transform));
                foreach (var context in contexts)
                {
                    var binding = new NativeMotionPoseBinding(archive, context.root, profiles[context.name]);
                    var expected = record["bindingCoverage"][context.name];
                    Require(binding.BoundTransforms == (int)expected["matched"], "Bound track count differs");
                    Require(binding.UnboundTransformHashes.SequenceEqual(expected["unboundHashes"].Select(x => (uint)x)), "Unbound paths were lost");
                    if (context.name == "Origin")
                    {
                        Require(binding.UnboundMorphTracks.Count == 0, "Origin morph channel did not bind");
                        if (binding.UnboundTransformHashes.Count > 0) partialOrigin++;
                    }
                    bool partial = binding.UnboundTransformHashes.Count != 0 || binding.UnboundMorphTracks.Count != 0;
                    if (partial)
                    {
                        bool rejected = false;
                        try { binding.ApplyFrame(0); } catch (InvalidOperationException) { rejected = true; }
                        Require(rejected, "Partial pose silently accepted by strict API");
                        strictRejections++;
                    }
                    var nodes = profiles[context.name]["nodes"].ToDictionary(n => (uint)n["pathHash"], n =>
                        (string)n["path"] == "" ? context.root : context.root.Find((string)n["path"]));
                    // The same persistent rig receives every next clip. Resetting
                    // defaults before applying a source frame prevents stale pose
                    // channels; the archive also retains every out-of-range guard.
                    foreach (int frame in new[] { 0, archive.FrameCount / 2, archive.FrameCount - 1 }.Distinct())
                    {
                        binding.ResetDefaults();
                        binding.ApplyFrame(frame, allowUnboundChannels: true);
                        for (int track = 0; track < archive.TransformCount; track++)
                        {
                            if (!nodes.TryGetValue(archive.TransformHash(track), out var t)) continue;
                            archive.ReadTransform(frame, track, out var p, out var q, out var s);
                            Require(t.localPosition.Equals(p) && t.localScale.Equals(s), "Source TRS value changed");
                            Require(Mathf.Abs(Quaternion.Dot(t.localRotation.normalized, q.normalized)) > .999999f, "Source rotation changed");
                            transformChecks++;
                        }
                        if (context.name == "Origin")
                        {
                            for (int track = 0; track < archive.ScalarCount; track++)
                            {
                                var b = archive.ScalarBinding(track);
                                if ((string)b["typeID"] != "SkinnedMeshRenderer") continue;
                                var renderer = nodes[(uint)b["path"]].GetComponent<SkinnedMeshRenderer>();
                                int shape = Enumerable.Range(0, renderer.sharedMesh.blendShapeCount).Single(i =>
                                    unchecked((uint)Animator.StringToHash(renderer.sharedMesh.GetBlendShapeName(i))) == (uint)b["attribute"]);
                                Require(renderer.GetBlendShapeWeight(shape) == archive.ReadScalar(frame, track), "Raw morph sample changed");
                                morphChecks++;
                            }
                            bridge.ApplyPose();
                            foreach (var link in bridge.bones)
                                Require(IsFinite(link.target.localPosition) && IsFinite(link.target.localScale), "Nonfinite assembled pose");
                        }
                        frameApplications++;
                    }
                    rows.Add(new JObject { ["assetID"] = id, ["profile"] = context.name,
                        ["boundTransforms"] = binding.BoundTransforms, ["unboundTransforms"] = binding.UnboundTransformHashes.Count,
                        ["boundMorphs"] = binding.BoundMorphs, ["unboundMorphs"] = binding.UnboundMorphTracks.Count,
                        ["animatorScalarsRetained"] = binding.AnimatorScalarTracks.Count });
                }
                motions++;
                if (motions % 50 == 0) Debug.Log("MOTION_BANK_UNITY_CHECKED " + motions);
            }
            int slots = 0;
            foreach (var s in index["slots"])
            {
                Require(bank.ResolveSlot((string)s["controller"], (int)s["clipIndex"]) == (string)s["assetID"], "Motion slot differs");
                slots++;
            }
            result["pass"] = true; result["uniqueMotions"] = motions; result["slots"] = slots;
            result["float32ValuesChecked"] = floats; result["arrayDigestChecks"] = digestChecks;
            result["sourceAvatarPathHashes"] = sourcePathHashes;
            result["frameApplications"] = frameApplications; result["transformApplicationsChecked"] = transformChecks;
            result["morphApplicationsChecked"] = morphChecks; result["originMotionsWithUnboundTransformPaths"] = partialOrigin;
            result["partialBindingsRejectedByDefault"] = strictRejections; result["bindings"] = rows;
            result["interpolationVerified"] = false; result["stateMixingVerified"] = false;
            result["completeNativeBindingPolicyVerified"] = false; result["controllerPlayable"] = false;
            Debug.Log("MOTION_BANK_UNITY_OK " + motions + " motions " + floats + " float32 values");
        }
        catch (Exception e)
        {
            result["error"] = e.ToString();
            Debug.LogException(e);
            throw;
        }
        finally
        {
            if (instance) Object.DestroyImmediate(instance);
            if (funnelRoot) Object.DestroyImmediate(funnelRoot);
            File.WriteAllText(Out + "/unity-motion-bank-verification.json", result.ToString());
        }
    }

    static bool IsFinite(Vector3 value) => !(float.IsNaN(value.x) || float.IsInfinity(value.x) ||
        float.IsNaN(value.y) || float.IsInfinity(value.y) || float.IsNaN(value.z) || float.IsInfinity(value.z));

    static GameObject MakeRig(JObject profile)
    {
        var nodes = new Dictionary<string, Transform>();
        foreach (var item in profile["nodes"])
        {
            string path = (string)item["path"];
            int split = path.LastIndexOf('/');
            var node = new GameObject(path.Length == 0 ? "FunnelSourceDriverAudit" : path.Substring(split + 1)).transform;
            if (path.Length > 0) node.SetParent(nodes[split < 0 ? "" : path.Substring(0, split)], false);
            nodes.Add(path, node);
        }
        return nodes[""].gameObject;
    }
}
