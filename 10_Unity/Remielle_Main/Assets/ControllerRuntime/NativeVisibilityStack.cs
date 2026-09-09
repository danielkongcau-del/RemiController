using System;
using System.Collections.Generic;

namespace Remielle.ControllerRuntime
{
    // Port of the verified mode-zero tagged container. This is the per-renderer
    // primary stack; global/component overrides are a separate, later stage.
    public sealed class NativeVisibilityStack
    {
        struct Slot { public bool Active, Value; }
        readonly List<Slot> slots = new List<Slot>();
        readonly Dictionary<string, int> tags = new Dictionary<string, int>(StringComparer.Ordinal);
        public bool Value { get; private set; }
        public int ActiveTagCount => tags.Count;
        public int WinningSlot { get; private set; }

        public NativeVisibilityStack(bool baseline)
        {
            slots.Add(new Slot { Active = true, Value = baseline });
            Value = baseline;
        }

        public void Push(string tag, bool visible)
        {
            if (string.IsNullOrEmpty(tag)) return;
            if (!tags.TryGetValue(tag, out int index))
            {
                index = 1;
                while (index < slots.Count && slots[index].Active) index++;
                if (index == slots.Count) slots.Add(default);
                tags.Add(tag, index);
            }
            slots[index] = new Slot { Active = true, Value = visible };
            Resolve();
        }

        public void Pop(string tag)
        {
            if (string.IsNullOrEmpty(tag) || !tags.TryGetValue(tag, out int index)) return;
            tags.Remove(tag);
            slots[index] = default;
            Resolve();
        }

        void Resolve()
        {
            int index = slots.Count - 1;
            while (!slots[index].Active) index--;
            WinningSlot = index;
            Value = slots[index].Value;
        }
    }

    public sealed class NativeRendererVisibility
    {
        readonly Dictionary<string, NativeVisibilityStack> renderers =
            new Dictionary<string, NativeVisibilityStack>(StringComparer.Ordinal);

        // Callers register qualified renderer identities and resolve source paths
        // before dispatch. Names alone must not merge two source renderers.
        public void Register(string identity, bool baseline)
        {
            if (string.IsNullOrEmpty(identity)) throw new ArgumentException(nameof(identity));
            renderers.Add(identity, new NativeVisibilityStack(baseline));
        }

        public bool GetVisible(string identity) => renderers[identity].Value;

        public void Push(string tag, bool visible, bool applyAll, IEnumerable<string> identities)
        {
            if (string.IsNullOrEmpty(tag)) return;
            if (applyAll)
            {
                foreach (var stack in renderers.Values) stack.Push(tag, visible);
                return;
            }
            if (identities == null) throw new ArgumentNullException(nameof(identities));
            // Validate the complete command before changing any target.
            var targets = new List<NativeVisibilityStack>();
            foreach (string id in identities) targets.Add(renderers[id]);
            foreach (var stack in targets) stack.Push(tag, visible);
        }

        public void Pop(string tag)
        {
            foreach (var stack in renderers.Values) stack.Pop(tag);
        }
    }
}
