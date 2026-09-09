using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using Remielle.ControllerRuntime;
using UnityEditor;
using UnityEngine;

public static class NativeMotionCodecAudit
{
    const string Folder = "E:/ZZZ/local-only/RemielleControllerImplementation/20260906/motion-sampling";

    public static void ConfigurePlugin()
    {
        const string path = "Assets/Plugins/x86_64/RemielleAclCodec.dll";
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        var plugin = AssetImporter.GetAtPath(path) as PluginImporter;
        Require(plugin != null, "Native codec did not import as a plugin");
        plugin.SetCompatibleWithAnyPlatform(false);
        plugin.SetCompatibleWithEditor(true);
        plugin.SetEditorData("CPU", "x86_64");
        plugin.SetEditorData("OS", "Windows");
        plugin.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows64, true);
        plugin.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows, false);
        plugin.SetCompatibleWithPlatform(BuildTarget.StandaloneLinux64, false);
        plugin.SetCompatibleWithPlatform(BuildTarget.StandaloneOSX, false);
        plugin.SetPlatformData(BuildTarget.StandaloneWindows64, "CPU", "x86_64");
        plugin.SaveAndReimport();
        Debug.Log("ACL_PLUGIN_CONFIGURED editor=" + plugin.GetCompatibleWithEditor() + " os=" + plugin.GetEditorData("OS"));
    }

    public static void Run()
    {
        var result = new JObject { ["schema"] = "remielle-motion-codec-unity-v1", ["pass"] = false };
        GameObject instance = null;
        try
        {
            const string pluginPath = "Assets/Plugins/x86_64/RemielleAclCodec.dll";
            var plugin = AssetImporter.GetAtPath(pluginPath) as PluginImporter;
            Require(plugin != null, "Native codec did not import as a plugin");
            Debug.Log("ACL_PLUGIN editor=" + plugin.GetCompatibleWithEditor() + " any=" + plugin.GetCompatibleWithAnyPlatform() +
                " os=" + plugin.GetEditorData("OS") + " cpu=" + plugin.GetEditorData("CPU"));
            Require(plugin.GetCompatibleWithEditor() && plugin.GetEditorData("OS") == "Windows" &&
                plugin.GetCompatibleWithPlatform(BuildTarget.StandaloneWindows64), "Native codec platform configuration differs");
            var vectors = JObject.Parse(File.ReadAllText(Folder + "/vectors.json"));
            foreach (string part in new[] { "bank", "vectors" })
            {
                using (var file = File.OpenRead((string)vectors[part]["path"]))
                using (var sha = SHA256.Create())
                    Require(NativeMotionArchive.Hex(sha.ComputeHash(file)) == (string)vectors[part]["sha256"], "Vector input hash");
            }
            string directory = Path.Combine(Application.streamingAssetsPath, "RemielleControllerMotions");
            var bank = new NativeMotionBank(directory);
            var profile = (JObject)JObject.Parse(File.ReadAllText(Path.Combine(directory, "binding-profiles.json")))["profiles"]
                .Single(x => (string)x["name"] == "Origin");
            instance = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/V3/Remielle_V3_Animated.prefab"));
            var bridge = instance.GetComponent<RemielleNativeAnimation>();
            bridge.autoplay = false; bridge.enabled = false;
            bridge.nativeAnimation.Stop(); bridge.nativeAnimation.enabled = false;
            var root = bridge.nativeAnimation.transform;
            var nodes = profile["nodes"].ToDictionary(n => (uint)n["pathHash"], n =>
                (string)n["path"] == "" ? root : root.Find((string)n["path"]));
            int motions = 0, samples = 0, rejected = 0, disposed = 0;
            int applied = 0;
            long values = 0;
            using (var binary = new BinaryReader(File.OpenRead(Folder + "/vectors.bin")))
                foreach (var row in vectors["rows"])
                {
                    var archive = bank.Load((string)row["assetID"]);
                    var binding = new NativeMotionPoseBinding(archive, root, profile);
                    var codec = new NativeMotionCodec(directory, bank.Record((string)row["assetID"]));
                    var output = new float[(int)row["transformCount"]*10];
                    var bytes = new byte[output.Length*4];
                    try
                    {
                        int count = ((JArray)row["times"]).Count;
                        foreach (int i in Enumerable.Range(0, count).Reverse().Concat(new[] { 0 }))
                        {
                            codec.SampleTransforms((float)row["times"][i], output);
                            Buffer.BlockCopy(output, 0, bytes, 0, bytes.Length);
                            binary.BaseStream.Position = (long)row["offset"] + (long)i*bytes.Length;
                            byte[] expected = binary.ReadBytes(bytes.Length);
                            Require(bytes.SequenceEqual(expected), "Unity runtime codec differs: " + row["assetID"]);
                            binding.ResetDefaults();
                            binding.ApplyTransformSamples(output, allowUnboundChannels: true);
                            for (int track = 0; track < archive.TransformCount; track++)
                            {
                                if (!nodes.TryGetValue(archive.TransformHash(track), out var node)) continue;
                                int o = track*10;
                                Require(node.localPosition.Equals(new Vector3(output[o+4], output[o+5], output[o+6])) &&
                                    node.localScale.Equals(new Vector3(output[o+7], output[o+8], output[o+9])), "Continuous source TRS application changed");
                                Require(Mathf.Abs(Quaternion.Dot(node.localRotation.normalized,
                                    new Quaternion(output[o], output[o+1], output[o+2], output[o+3]).normalized)) > .999999f,
                                    "Continuous source rotation changed");
                                applied++;
                            }
                            bridge.ApplyPose();
                            values += output.Length; samples++;
                        }
                        foreach (float time in new[] { -1f, float.NaN, float.PositiveInfinity })
                        {
                            bool failed = false;
                            try { codec.SampleTransforms(time, output); } catch (ArgumentOutOfRangeException) { failed = true; }
                            Require(failed, "Invalid time accepted"); rejected++;
                        }
                    }
                    finally { codec.Dispose(); }
                    bool afterDispose = false;
                    try { codec.SampleTransforms(0, output); } catch (ObjectDisposedException) { afterDispose = true; }
                    Require(afterDispose, "Disposed decoder accepted a sample");
                    codec.Dispose(); disposed++;
                    motions++;
                }
            result["pass"] = true; result["motions"] = motions; result["samples"] = samples;
            result["transformFloat32ValuesBitwiseEqual"] = values; result["invalidTimesRejected"] = rejected;
            result["continuousDriverTransformApplicationsChecked"] = applied;
            result["disposedHandlesRejectedAndDoubleDisposeSafe"] = disposed;
            result["referenceKind"] = vectors["referenceKind"].DeepClone();
            result["directGameDLLComparison"] = false;
            result["continuousMorphScalarSamplingVerified"] = false;
            Debug.Log("MOTION_CODEC_UNITY_OK " + motions + " motions " + values + " float32 values");
        }
        catch (Exception e) { result["error"] = e.ToString(); Debug.LogException(e); throw; }
        finally
        {
            if (instance) UnityEngine.Object.DestroyImmediate(instance);
            File.WriteAllText(Folder + "/unity-codec-verification.json", result.ToString());
        }
    }

    static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
}
