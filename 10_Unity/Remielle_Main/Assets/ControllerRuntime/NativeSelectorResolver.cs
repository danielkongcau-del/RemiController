using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Remielle.ControllerRuntime
{
    public sealed class NativeSelectorResolution
    {
        public uint State;
        public uint TraversalFlags;
        public uint[] UsedTriggers;
        public int ConditionsChecked;
        public readonly List<int> Visited = new List<int>();
        public readonly List<int[]> Matched = new List<int[]>();
    }

    public static class NativeSelectorResolver
    {
        // Original resolver 0xcfea40 and used-trigger marker 0xd00e60.
        // This resolves a destination; it does not commit a state or reset a trigger.
        public static NativeSelectorResolution Resolve(JObject machine, uint destination,
            NativeParameterBank parameters, uint flags = 0, IEnumerable<uint> previouslyUsed = null)
        {
            var result = new NativeSelectorResolution { TraversalFlags = flags };
            var used = new HashSet<uint>(previouslyUsed ?? Enumerable.Empty<uint>());
            var selectors = (JArray)machine["selectors"];
            var visited = new HashSet<uint>();
            while (destination != uint.MaxValue && destination >= 30000)
            {
                uint index = destination - 30000;
                if (index >= selectors.Count || !visited.Add(index))
                    throw new InvalidOperationException("Invalid or cyclic native selector destination " + destination);
                var selector = selectors[(int)index];
                result.Visited.Add((int)index);
                result.TraversalFlags |= (int)selector["entryByte"] != 0 ? 2u : 4u;
                bool matched = false;
                var transitions = (JArray)selector["transitions"];
                for (int ti = 0; ti < transitions.Count; ti++)
                {
                    var transition = transitions[ti];
                    var conditions = ((JArray)transition["conditions"]).Cast<JArray>().Select(NativeCondition.FromSource).ToArray();
                    bool eligible = true;
                    foreach (var condition in conditions)
                    {
                        result.ConditionsChecked++;
                        if (!condition.Evaluate(parameters)) { eligible = false; break; }
                    }
                    if (!eligible) continue;
                    foreach (var condition in conditions)
                        if (condition.Mode == 1 && parameters.TryGetKind(condition.Parameter, out int kind) && kind == 9)
                            used.Add(condition.Parameter);
                    result.Matched.Add(new[] { (int)index, ti });
                    destination = (uint)transition["destRaw"];
                    matched = true;
                    break;
                }
                if (!matched) { destination = 0; break; }
            }
            result.State = destination == uint.MaxValue ? 0 : destination;
            result.UsedTriggers = used.OrderBy(value => value).ToArray();
            return result;
        }
    }
}
