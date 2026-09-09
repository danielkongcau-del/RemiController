using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using Remielle.ControllerRuntime;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class NativeScalarCodecAudit
{
    const string Folder = "E:/ZZZ/local-only/RemielleControllerImplementation/20260906/motion-sampling";
    const string WriteRoot = "E:/ZZZ/ZCode/90_Builds/ControllerImplementation/20260906/motion-sampling/"; // D1-c 写根（读根保留 A 类）

    public static void Run()
    {
        var result = new JObject { ["schema"] = "remielle-scalar-codec-unity-v1", ["pass"] = false };
        GameObject instance = null;
        try
        {
            var vectors = JObject.Parse(File.ReadAllText(Folder + "/scalar-vectors.json"));
            Require((bool)vectors["pass"], "Independent scalar comparison did not pass");
            foreach (string part in new[] { "bank", "binary", "library", "runtimeSource", "independentReader" })
                using (var stream = File.OpenRead((string)vectors[part]["path"]))
                using (var sha = SHA256.Create())
                    Require(NativeMotionArchive.Hex(sha.ComputeHash(stream)) == (string)vectors[part]["sha256"], "Scalar input hash: " + part);
            string directory = Path.Combine(Application.streamingAssetsPath, "RemielleControllerMotions");
            var bank = new NativeMotionBank(directory);
            var profile = (JObject)JObject.Parse(File.ReadAllText(Path.Combine(directory, "binding-profiles.json")))["profiles"]
                .Single(x => (string)x["name"] == "Origin");
            instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/V3/Remielle_V3_Animated.prefab"));
            var bridge = instance.GetComponent<RemielleNativeAnimation>();
            bridge.autoplay = false; bridge.enabled = false;
            bridge.nativeAnimation.Stop(); bridge.nativeAnimation.enabled = false;
            Transform root = bridge.nativeAnimation.transform;
            var nodes = profile["nodes"].ToDictionary(x => (uint)x["pathHash"], x =>
                (string)x["path"] == "" ? root : root.Find((string)x["path"]));
            int motions = 0, queries = 0, negativeWeights = 0, defaultRestores = 0, invalidRejected = 0;
            long values = 0, morphWrites = 0, rootScalarValuesRetained = 0;
            using (var binary = new BinaryReader(File.OpenRead(Folder + "/scalar-vectors.bin")))
                foreach (var row in vectors["rows"])
                {
                    string id = (string)row["assetID"];
                    var archive = bank.Load(id);
                    var binding = new NativeMotionPoseBinding(archive, root, profile);
                    Require(binding.UnboundMorphTracks.Count == 0, "Unbound original morph");
                    var codec = new NativeMotionCodec(directory, bank.Record(id));
                    var output = new float[codec.ScalarCount];
                    var bytes = new byte[output.Length*4];
                    var morphs = new List<(int track, SkinnedMeshRenderer renderer, int shape)>();
                    for (int track = 0; track < archive.ScalarCount; track++)
                    {
                        var b = archive.ScalarBinding(track);
                        if ((string)b["typeID"] != "SkinnedMeshRenderer") continue;
                        var renderer = nodes[(uint)b["path"]].GetComponent<SkinnedMeshRenderer>();
                        int shape = Enumerable.Range(0, renderer.sharedMesh.blendShapeCount).Single(i =>
                            unchecked((uint)Animator.StringToHash(renderer.sharedMesh.GetBlendShapeName(i))) == (uint)b["attribute"]);
                        morphs.Add((track, renderer, shape));
                    }
                    try
                    {
                        binding.ResetDefaults();
                        foreach (var m in bridge.morphs)
                            for (int i = 0; i < m.source.sharedMesh.blendShapeCount; i++)
                            {
                                Require(m.source.GetBlendShapeWeight(i) == 0, "Previous action left a stale expression");
                                defaultRestores++;
                            }
                        Vector3 initialPosition = root.localPosition, initialScale = root.localScale;
                        Quaternion initialRotation = root.localRotation;
                        int count = ((JArray)row["times"]).Count;
                        for (int i = count-1; i >= 0; i--)
                        {
                            codec.SampleScalars((float)row["times"][i], output);
                            Buffer.BlockCopy(output, 0, bytes, 0, bytes.Length);
                            binary.BaseStream.Position = (long)row["offset"] + (long)i*bytes.Length;
                            Require(bytes.SequenceEqual(binary.ReadBytes(bytes.Length)), "Scalar output differs: " + id);
                            binding.ApplyScalarSamples(output);
                            foreach (var m in morphs)
                            {
                                Require(m.renderer.GetBlendShapeWeight(m.shape) == output[m.track], "Continuous morph sample changed");
                                if (output[m.track] < 0) negativeWeights++;
                                morphWrites++;
                            }
                            Require(root.localPosition.Equals(initialPosition) && root.localRotation.Equals(initialRotation) &&
                                root.localScale.Equals(initialScale), "Scalar application duplicated authored root motion");
                            if (i == count-1 || i == count/2 || i == 0)
                            {
                                bridge.ApplyPose();
                                foreach (var link in bridge.morphs)
                                    for (int j = 0; j < link.source.sharedMesh.blendShapeCount; j++)
                                        Require(link.target.GetBlendShapeWeight(j) == link.source.GetBlendShapeWeight(j), "Assembled morph bridge differs");
                            }
                            values += output.Length; rootScalarValuesRetained += binding.AnimatorScalarTracks.Count; queries++;
                        }
                        foreach (float time in new[] { -1f, float.NaN, float.PositiveInfinity, codec.Duration+1 })
                        {
                            bool failed = false;
                            try { codec.SampleScalars(time, output); } catch (ArgumentOutOfRangeException) { failed = true; }
                            Require(failed, "Invalid scalar time accepted"); invalidRejected++;
                        }
                    }
                    finally { codec.Dispose(); }
                    bool disposed = false;
                    try { codec.SampleScalars(0, output); } catch (ObjectDisposedException) { disposed = true; }
                    Require(disposed, "Disposed scalar context accepted a sample");
                    codec.Dispose();
                    motions++;
                    if (motions%25 == 0) Debug.Log("SCALAR_CODEC_UNITY_CHECKED " + motions);
                }
            result["pass"] = true; result["motions"] = motions; result["queries"] = queries;
            result["scalarFloat32ValuesBitwiseEqual"] = values; result["morphWeightsAppliedAndReadBack"] = morphWrites;
            result["negativeMorphWeightsPreserved"] = negativeWeights; result["defaultMorphRestoresChecked"] = defaultRestores;
            result["animatorScalarValuesRetainedWithoutDoubleMotion"] = rootScalarValuesRetained;
            result["invalidTimesRejected"] = invalidRejected; result["disposedScalarContextsRejected"] = motions;
            result["directGameDLLComparison"] = false;
            Debug.Log("SCALAR_CODEC_UNITY_OK " + motions + " motions " + values + " float32 values");
        }
        catch (Exception e) { result["error"] = e.ToString(); Debug.LogException(e); throw; }
        finally
        {
            if (instance) Object.DestroyImmediate(instance);
            File.WriteAllText(WriteRoot + "/unity-scalar-verification.json", result.ToString());
        }
    }

    static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
}
