using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Remielle.ControllerRuntime
{
    public static class NativeMotionSetLength
    {
        // cff4c2..cff6a8, repeated for source-present motion sets in order.
        // Weight construction by the layer scheduler is not inferred here.
        public static float Combine(IReadOnlyList<float?> lengths, IReadOnlyList<float> weights)
        {
            if (lengths == null || weights == null) throw new ArgumentNullException();
            if (lengths.Count != weights.Count) throw new ArgumentException("Motion-set weight count differs");
            float total = 0f;
            for (int i = 0; i < lengths.Count; i++)
            {
                if (!lengths[i].HasValue) continue;
                float remaining = 1f;
                for (int later = lengths.Count - 1; later > i; later--)
                {
                    if (!lengths[later].HasValue) continue;
                    float covered = (float)(remaining * weights[later]);
                    remaining = (float)(remaining - covered);
                }
                float contribution = (float)(weights[i] * lengths[i].Value);
                contribution = (float)(contribution * remaining);
                total = (float)(total + contribution);
            }
            return total;
        }
    }

    public sealed class NativeStateMotionResult
    {
        public float Length { get; }
        public IReadOnlyList<NativeBlendTreeResult> MotionSets { get; }
        internal NativeStateMotionResult(float length, NativeBlendTreeResult[] sets)
        { Length = length; MotionSets = Array.AsReadOnly(sets); }
    }

    // Source state and per-layer motion-set mapping are kept intact. This does
    // not assign layer weights or apply the resulting poses to a skeleton.
    public sealed class NativeStateMotions
    {
        readonly NativeBlendTree[] sets;
        readonly string controllerName;
        readonly int machineIndex;
        public NativeStateMotions(NativeControllerSource source, NativeMotionBank bank, NativeMotionIntervals intervals,
            string controller, int machine, int state)
        {
            controllerName = controller; machineIndex = machine;
            var c = source.GetController(controller); var s = c["machines"][machine]["states"][state];
            sets = new NativeBlendTree[((JArray)s["blendTreeIndices"]).Count];
            for (int i = 0; i < sets.Length; i++)
            {
                if ((int)s["blendTreeIndices"][i] < 0) continue;
                int set = i;
                int[] layers = Enumerable.Range(0, ((JArray)c["layers"]).Count).Where(li =>
                    (int)c["layers"][li]["smIdx"] == machine && (int)c["layers"][li]["smms"] == set).ToArray();
                if (layers.Length != 1) throw new InvalidDataException("Motion set does not have one qualified source layer");
                sets[i] = NativeBlendTree.ForLayer(source, bank, intervals, controller, layers[0], state);
            }
        }

        public NativeStateMotionResult Evaluate(NativeParameterBank parameters, IReadOnlyList<float> motionSetWeights)
        {
            if (motionSetWeights == null || motionSetWeights.Count != sets.Length)
                throw new ArgumentException("Motion-set weight count differs");
            var results = new NativeBlendTreeResult[sets.Length]; var lengths = new float?[sets.Length];
            for (int i = 0; i < sets.Length; i++)
            {
                if (sets[i] == null) continue;
                results[i] = sets[i].Evaluate(parameters); lengths[i] = results[i].Length;
            }
            return new NativeStateMotionResult(NativeMotionSetLength.Combine(lengths, motionSetWeights), results);
        }

        public NativeStateMotionResult Evaluate(NativeParameterBank parameters, NativeLayerWeights layerWeights)
        {
            if (layerWeights == null || !StringComparer.Ordinal.Equals(layerWeights.Controller, controllerName))
                throw new ArgumentException("Layer weights belong to a different controller");
            return Evaluate(parameters, layerWeights.TimingWeights(machineIndex));
        }
    }
}
