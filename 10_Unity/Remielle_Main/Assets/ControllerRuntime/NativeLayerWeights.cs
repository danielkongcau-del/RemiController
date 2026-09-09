using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Remielle.ControllerRuntime
{
    public readonly struct NativeLayerInputWeight
    {
        public readonly float Input, Start, Metadata;
        public readonly double Timestamp;
        public readonly bool Additive;
        public NativeLayerInputWeight(float input, float start, double timestamp, float metadata, bool additive)
        { Input = input; Start = start; Timestamp = timestamp; Metadata = metadata; Additive = additive; }
    }

    // Layer timing weights and pose-input weights have different native rules.
    // Runtime layer weights are supplied explicitly; initialization and gameplay
    // producers are not inferred from the asset's DefaultWeight field.
    public sealed class NativeLayerWeights
    {
        readonly int[] machines, sets, setCounts, modes;
        readonly bool[] affectsTiming, maskBlendShape;
        readonly float[] runtimeWeights;
        readonly NativeLayerInputWeight[] inputs;
        public string Controller { get; }
        public int LayerCount => machines.Length;
        public int MachineCount => setCounts.Length;

        public NativeLayerWeights(NativeControllerSource source, string controller, IReadOnlyList<float> initialRuntimeWeights,
            IReadOnlyList<NativeLayerInputWeight> initialInputs)
        {
            if (source == null || initialRuntimeWeights == null || initialInputs == null) throw new ArgumentNullException();
            Controller = controller; var c = source.GetController(controller); var layers = (JArray)c["layers"];
            if (initialRuntimeWeights.Count != layers.Count || initialInputs.Count != layers.Count)
                throw new ArgumentException("Layer weight dimensions differ");
            machines = new int[layers.Count]; sets = new int[layers.Count]; modes = new int[layers.Count];
            affectsTiming = new bool[layers.Count]; maskBlendShape = new bool[layers.Count];
            setCounts = new int[((JArray)c["machines"]).Count];
            for (int i = 0; i < layers.Count; i++)
            {
                var layer = layers[i]; machines[i] = (int)layer["smIdx"]; sets[i] = (int)layer["smms"];
                modes[i] = (int)layer["mode"]; affectsTiming[i] = (int)layer["sync"] != 0;
                if (layer["maskBlendShape"] == null) throw new InvalidDataException("Unqualified layer boolean layout");
                maskBlendShape[i] = (bool)layer["maskBlendShape"];
                if (machines[i] < 0 || machines[i] >= MachineCount || sets[i] < 0 || modes[i] < 0 || modes[i] > 1)
                    throw new InvalidDataException("Invalid source layer mapping or mode");
                setCounts[machines[i]] = Math.Max(setCounts[machines[i]], sets[i] + 1);
            }
            for (int mi = 0; mi < MachineCount; mi++)
            {
                var indices = Enumerable.Range(0, LayerCount).Where(i => machines[i] == mi).Select(i => sets[i]).OrderBy(i => i).ToArray();
                if (!indices.SequenceEqual(Enumerable.Range(0, setCounts[mi]))) throw new InvalidDataException("Nonunique or missing motion-set layer");
                foreach (var s in c["machines"][mi]["states"])
                    if (((JArray)s["blendTreeIndices"]).Count != setCounts[mi]) throw new InvalidDataException("Layer/state motion-set count differs");
            }
            runtimeWeights = initialRuntimeWeights.ToArray(); inputs = initialInputs.ToArray();
        }

        // Original valid-index store at cd0df3. This adapter rejects invalid
        // indices rather than reproducing engine logging and object validation.
        public void SetWeight(int layer, float value) { CheckLayer(layer); runtimeWeights[layer] = value; }
        public float GetWeight(int layer) { CheckLayer(layer); return runtimeWeights[layer]; }
        public bool MaskBlendShape(int layer) { CheckLayer(layer); return maskBlendShape[layer]; }
        public NativeLayerInputWeight GetInput(int layer) { CheckLayer(layer); return inputs[layer]; }

        // cd1f60..cd1fe3 in the original per-machine tick caller. A set's first
        // layer contributes one to timing even if its runtime weight is zero.
        public float[] TimingWeights(int machine)
        {
            if (machine < 0 || machine >= MachineCount) throw new ArgumentOutOfRangeException(nameof(machine));
            var result = new float[setCounts[machine]];
            for (int i = 0; i < LayerCount; i++)
                if (machines[i] == machine)
                    result[sets[i]] = sets[i] == 0 ? 1f : (affectsTiming[i] ? runtimeWeights[i] : 0f);
            return result;
        }

        // cd0c90 and the actual LayerMixer weight setter cd91d0. State output
        // weights are a separate input from TimingWeights; the full state tick
        // must produce them before this method can be used for pose mixing.
        public void ApplyPoseWeights(IReadOnlyList<float[]> stateOutputWeights, double timestamp)
        {
            if (stateOutputWeights == null || stateOutputWeights.Count != MachineCount)
                throw new ArgumentException("State output weight dimensions differ");
            for (int mi = 0; mi < MachineCount; mi++)
                if (stateOutputWeights[mi] == null || stateOutputWeights[mi].Length != setCounts[mi])
                    throw new ArgumentException("State motion-set output dimensions differ");
            for (int i = 0; i < LayerCount; i++)
            {
                float input = i == 0 ? 1f : (float)(runtimeWeights[i] * stateOutputWeights[machines[i]][sets[i]]);
                float metadata = i == 0 ? 1f : runtimeWeights[i];
                var old = inputs[i];
                // b0a7c0 rejects negative values, retains prior input/time, and
                // accepts unordered values. The later metadata write still runs.
                bool accepted = !(input < 0f);
                inputs[i] = new NativeLayerInputWeight(accepted ? input : old.Input, accepted ? input : old.Start,
                    accepted ? timestamp : old.Timestamp, metadata, modes[i] == 1);
            }
        }
        void CheckLayer(int layer)
        { if (layer < 0 || layer >= LayerCount) throw new ArgumentOutOfRangeException(nameof(layer)); }
    }
}
