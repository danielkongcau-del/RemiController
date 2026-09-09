using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Remielle.ControllerRuntime
{
    public readonly struct NativeMotionInterval
    {
        public readonly float Start, Stop;
        public NativeMotionInterval(float start, float stop)
        {
            if (float.IsNaN(start) || float.IsInfinity(start) || float.IsNaN(stop) || float.IsInfinity(stop))
                throw new ArgumentOutOfRangeException(nameof(start));
            Start = start; Stop = stop;
        }
    }

    // Source-qualified clip intervals without loading or altering sample arrays.
    public sealed class NativeMotionIntervals
    {
        readonly Dictionary<string, NativeMotionInterval> values = new Dictionary<string, NativeMotionInterval>(StringComparer.Ordinal);
        public NativeMotionIntervals(string json, NativeMotionBank bank)
        {
            var root = JObject.Parse(json);
            if ((string)root["schema"] != "remielle-native-motion-intervals-v1")
                throw new InvalidDataException("Unknown motion interval schema");
            foreach (var item in root["motions"])
            {
                string id = (string)item["assetID"];
                var record = bank.Record(id);
                var interval = new NativeMotionInterval(Float(item["startBits"]), Float(item["stopBits"]));
                if ((string)item["motionSha256"] != (string)record["sha256"] || interval.Start != 0f ||
                    BitConverter.SingleToInt32Bits(interval.Stop) != BitConverter.SingleToInt32Bits((float)record["summary"]["duration"]))
                    throw new InvalidDataException("Motion interval does not match the qualified archive: " + id);
                values.Add(id, interval);
            }
            if (!new HashSet<string>(bank.AssetIDs).SetEquals(values.Keys))
                throw new InvalidDataException("Motion interval identities do not cover this bank");
        }
        public NativeMotionInterval Get(string assetID) => values[assetID];
        static float Float(JToken bits) => BitConverter.Int32BitsToSingle(unchecked((int)Convert.ToUInt32((string)bits, 16)));
    }

    public readonly struct NativeBlendLeaf
    {
        public readonly uint ClipIndex;
        // InputIndex counts all authored leaves, including currently zero-weight
        // leaves. Active results are compacted, without merging duplicate clips.
        public readonly int InputIndex;
        public readonly float Weight, SpeedScale, CycleOffset;
        public readonly bool Mirror;
        public NativeBlendLeaf(uint clip, int input, float weight, float speed, float cycle, bool mirror)
        { ClipIndex = clip; InputIndex = input; Weight = weight; SpeedScale = speed; CycleOffset = cycle; Mirror = mirror; }
    }

    public sealed class NativeBlendTreeResult
    {
        public float Length { get; }
        public IReadOnlyList<float> NodeWeights { get; }
        public IReadOnlyList<NativeBlendLeaf> Leaves { get; }
        internal NativeBlendTreeResult(float length, float[] weights, List<NativeBlendLeaf> leaves)
        { Length = length; NodeWeights = Array.AsReadOnly(weights); Leaves = leaves.AsReadOnly(); }
    }

    // Original cf1fc0 and its 1D cf3840 path: weights, clip input records and
    // effective tree length. Playable wiring and pose blending are separate.
    public sealed class NativeBlendTree
    {
        sealed class Node
        {
            public uint Clip, Parameter;
            public int[] Children;
            public float[] Thresholds;
            public float DurationScale, CycleOffset;
            public bool Mirror;
            public NativeMotionInterval Interval;
        }
        readonly Node[] nodes;

        public NativeBlendTree(JObject tree, IReadOnlyDictionary<uint, NativeMotionInterval> intervals)
        {
            if (tree == null || intervals == null) throw new ArgumentNullException();
            var source = (JArray)tree["nodes"];
            nodes = new Node[source.Count];
            var parents = new int[nodes.Length];
            for (int i = 0; i < nodes.Length; i++)
            {
                var n = source[i];
                var node = new Node { Clip = (uint)n["clipIndex"], Parameter = (uint)n["blendParameter"],
                    Children = n["childIndices"].Values<int>().ToArray(), Thresholds = n["thresholds"].Values<float>().ToArray(),
                    DurationScale = (float)n["duration"], CycleOffset = (float)n["cycleOffset"], Mirror = (bool)n["mirror"] };
                if (node.Clip != uint.MaxValue)
                {
                    if (node.Children.Length != 0) throw new InvalidDataException("Clip node has children");
                    node.Interval = intervals[node.Clip];
                }
                else if (node.Children.Length != 0)
                {
                    if ((int)n["blendType"] != 0) throw new NotSupportedException("Unqualified blend type");
                    if (node.Thresholds.Length != node.Children.Length) throw new InvalidDataException("1D threshold count");
                    for (int k = 0; k < node.Children.Length; k++)
                    {
                        int child = node.Children[k]; float threshold = node.Thresholds[k];
                        if (child <= i || child >= nodes.Length || ++parents[child] != 1)
                            throw new InvalidDataException("Source blend nodes are not in forward tree order");
                        if (float.IsNaN(threshold) || float.IsInfinity(threshold) || (k > 0 && threshold < node.Thresholds[k - 1]))
                            throw new InvalidDataException("Unordered or nonfinite source thresholds");
                    }
                }
                if (float.IsNaN(node.DurationScale) || float.IsInfinity(node.DurationScale) ||
                    float.IsNaN(node.CycleOffset) || float.IsInfinity(node.CycleOffset))
                    throw new InvalidDataException("Nonfinite leaf timing");
                nodes[i] = node;
            }
            for (int i = 1; i < nodes.Length; i++)
                if (parents[i] != 1) throw new InvalidDataException("Disconnected source blend node");
        }

        public static NativeBlendTree ForLayer(NativeControllerSource source, NativeMotionBank bank,
            NativeMotionIntervals intervals, string controller, int layer, int state)
        {
            var c = source.GetController(controller); var l = c["layers"][layer];
            var s = c["machines"][(int)l["smIdx"]]["states"][state];
            int ti = (int)s["blendTreeIndices"][(int)l["smms"]];
            if (ti < 0) return null;
            var tree = (JObject)s["trees"][ti]; var clips = new Dictionary<uint, NativeMotionInterval>();
            for (int i = 0; i < ((JArray)tree["nodes"]).Count; i++)
            {
                uint clip = (uint)tree["nodes"][i]["clipIndex"];
                if (clip == uint.MaxValue) continue;
                var binding = source.ResolveLeaf(controller, layer, state, i);
                if (binding == null) throw new InvalidDataException("Unqualified null motion in source tree");
                var chosen = binding["candidates"][(int)binding["selectedCandidateIndex"]];
                string expected = (string)chosen["sourceScope"] + "/" + (string)chosen["sourceBlock"] + "/" +
                    (string)chosen["cab"] + "/" + (string)chosen["pathID"];
                string id = bank.ResolveSlot(controller, checked((int)clip));
                if (!StringComparer.Ordinal.Equals(id, expected)) throw new InvalidDataException("Controller/bank source identity differs");
                clips[clip] = intervals.Get(id);
            }
            return new NativeBlendTree(tree, clips);
        }

        public NativeBlendTreeResult Evaluate(NativeParameterBank parameters)
        {
            if (parameters == null) throw new ArgumentNullException(nameof(parameters));
            // Reject unqualified missing/wrong-type blend parameters before any
            // output. The native missing-parameter scratch-buffer path is not
            // inferred to be a valid zero-weight tree.
            foreach (var n in nodes)
                if (n.Clip == uint.MaxValue && n.Children.Length != 0 &&
                    (!parameters.TryGetKind(n.Parameter, out int kind) || kind != 1))
                    throw new InvalidDataException("Missing or non-float blend parameter");
            var weights = new float[nodes.Length]; var leaves = new List<NativeBlendLeaf>();
            if (weights.Length != 0) weights[0] = 1f;
            float length = 0f, totalWeight = 0f; int input = 0;
            for (int i = 0; i < nodes.Length; i++)
            {
                Node n = nodes[i]; float weight = weights[i];
                if (n.Clip != uint.MaxValue)
                {
                    if (weight > 0f)
                    {
                        float duration = (float)(n.Interval.Stop - n.Interval.Start);
                        duration = (float)(duration * n.DurationScale);
                        float contribution = (float)(Math.Abs(duration) * weight);
                        length = (float)(contribution + length);
                        totalWeight = (float)(totalWeight + weight);
                        leaves.Add(new NativeBlendLeaf(n.Clip, input, weight,
                            n.DurationScale == 0f ? 1f : 1f / n.DurationScale, n.CycleOffset, n.Mirror));
                    }
                    input++;
                }
                else if (n.Children.Length != 0)
                {
                    float value = parameters.GetFloat(n.Parameter);
                    // SSE maxss/minss choose the second operand for unordered
                    // or equal values, including NaN and signed-zero inputs.
                    value = value > n.Thresholds[0] ? value : n.Thresholds[0];
                    float last = n.Thresholds[n.Thresholds.Length - 1];
                    value = value < last ? value : last;
                    for (int child = 0; child < n.Children.Length; child++)
                    {
                        float threshold = n.Thresholds[child], childWeight;
                        if (value >= threshold)
                        {
                            if (child == n.Children.Length - 1) childWeight = 1f;
                            else childWeight = Triangle(value, threshold, n.Thresholds[child + 1], value > n.Thresholds[child + 1]);
                        }
                        else
                        {
                            if (child == 0) childWeight = 1f;
                            else childWeight = Triangle(value, threshold, n.Thresholds[child - 1], n.Thresholds[child - 1] > value);
                        }
                        weights[n.Children[child]] = (float)(childWeight * weight);
                    }
                }
            }
            if (totalWeight == 0f) length = 1f;
            else if (totalWeight < 1f) length /= totalWeight;
            return new NativeBlendTreeResult(length, weights, leaves);
        }

        static float Triangle(float value, float threshold, float neighbor, bool outside)
        {
            if (outside) return 0f;
            float denominator = (float)(threshold - neighbor);
            if (denominator == 0f) return 1f;
            float numerator = (float)(value - neighbor);
            return numerator / denominator;
        }
    }
}
