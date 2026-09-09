using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Remielle.ControllerRuntime
{
    public sealed class NativeControllerSource
    {
        readonly Dictionary<string, JObject> controllers;

        public NativeControllerSource(string json)
        {
            var root = JObject.Parse(json);
            if ((string)root["schema"] != "remielle-controller-source-pack-v1")
                throw new ArgumentException("Unsupported controller source schema");
            controllers = ((JArray)root["controllers"]).Cast<JObject>()
                .ToDictionary(x => (string)x["name"], StringComparer.Ordinal);
        }

        public IEnumerable<string> Names => controllers.Keys;
        public JObject GetController(string name) => (JObject)controllers[name].DeepClone();

        public NativeParameterBank CreateParameters(string name) =>
            new NativeParameterBank((JArray)controllers[name]["parameters"]);

        // Prepare once per source list. The layer scheduler chooses when to
        // search Current, Next or Any State; -1 denotes the original Any list.
        public NativeTransitionSearch CreateTransitionSearch(string name, int machine, int sourceState)
        {
            var definition = (JObject)controllers[name]["machines"][machine];
            var transitions = sourceState == -1 ? definition["anyTransitions"] :
                definition["states"][sourceState]["transitions"];
            return new NativeTransitionSearch(definition, (JArray)transitions);
        }

        public NativeSelectorResolution ResolveSelector(string name, int machine, uint destination,
            NativeParameterBank parameters, uint flags = 0, IEnumerable<uint> previouslyUsed = null) =>
            NativeSelectorResolver.Resolve((JObject)controllers[name]["machines"][machine], destination,
                parameters, flags, previouslyUsed);

        // Motion set belongs to the layer, and btidx maps it to a state tree.
        // The node's stored clip index selects a PPtr; state names never do.
        public JObject ResolveLeaf(string controller, int layer, int state, int nodeIndex = 0)
        {
            JObject c = controllers[controller];
            var l = c["layers"][layer];
            int machine = (int)l["smIdx"], motionSet = (int)l["smms"];
            var s = c["machines"][machine]["states"][state];
            int treeIndex = (int)s["blendTreeIndices"][motionSet];
            if (treeIndex < 0) return null;
            var node = s["trees"][treeIndex]["nodes"][nodeIndex];
            if (((JArray)node["childIndices"]).Count != 0)
                throw new InvalidOperationException("A blend tree requires evaluated child weights");
            var binding = (JObject)c["clips"][(int)node["clipIndex"]];
            if ((string)binding["resolution"] == "null") return null;
            string resolution = (string)binding["resolution"];
            int selected = (int?)binding["selectedCandidateIndex"] ?? -1;
            if ((resolution != "unique-in-recovered-inventory" && resolution != "unique-current-persistent-identity") ||
                selected < 0 || selected >= ((JArray)binding["candidates"]).Count)
                throw new InvalidOperationException("Unresolved source-qualified animation pointer: " + binding["pathID"]);
            return (JObject)binding.DeepClone();
        }
    }

    public sealed class NativeParameterBank
    {
        sealed class Parameter
        {
            public string Name;
            public int Kind;
            public float Float;
            public int Int;
            public bool Bool;
        }
        readonly Dictionary<uint, Parameter> values = new Dictionary<uint, Parameter>();
        readonly Dictionary<string, uint> names = new Dictionary<string, uint>(StringComparer.Ordinal);

        public NativeParameterBank(JArray definitions)
        {
            foreach (JObject d in definitions)
            {
                uint hash = (uint)d["hash"];
                var p = new Parameter { Name = (string)d["name"], Kind = (int)d["kind"] };
                switch (p.Kind)
                {
                    case 1: p.Float = (float)d["defaultValue"]; break;
                    case 3: p.Int = (int)d["defaultValue"]; break;
                    case 4: case 9: p.Bool = (bool)d["defaultValue"]; break;
                    default: throw new ArgumentException("Unsupported source parameter kind " + p.Kind);
                }
                values.Add(hash, p);
                names.Add(p.Name, hash);
            }
        }

        public uint Hash(string name) => names[name];
        public int Kind(uint hash) => values[hash].Kind;
        public bool TryGetKind(uint hash, out int kind)
        {
            bool found = values.TryGetValue(hash, out var parameter);
            kind = found ? parameter.Kind : 0;
            return found;
        }

        Parameter Require(uint hash, int kind)
        {
            var p = values[hash];
            if (p.Kind != kind) throw new InvalidOperationException("Parameter type mismatch: " + p.Name);
            return p;
        }

        public float GetFloat(uint hash) => Require(hash, 1).Float;
        public int GetInt(uint hash) => Require(hash, 3).Int;
        public bool GetBool(uint hash) => Require(hash, 4).Bool;
        public bool GetTrigger(uint hash) => Require(hash, 9).Bool;
        public void SetFloat(uint hash, float value) => Require(hash, 1).Float = value;
        public void SetInt(uint hash, int value) => Require(hash, 3).Int = value;
        public void SetBool(uint hash, bool value) => Require(hash, 4).Bool = value;
        public void SetTrigger(uint hash) => Require(hash, 9).Bool = true;
        public void ResetTrigger(uint hash) => Require(hash, 9).Bool = false;

        // Transition scheduling must explicitly commit/reset triggers. Reading a
        // condition must never consume one during a rejected candidate search.
    }
}
