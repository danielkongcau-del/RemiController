using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Remielle.ControllerRuntime
{
    // The state executor owns these values across ticks and transition completion.
    // Offsets and duration retain the original units until the clock consumes them.
    public sealed class NativeLayerTransitionState
    {
        public uint CurrentState, NextState, PreviousState, TraversalFlags;
        public int TransitionIndex, TransitionSourceState;
        public float SourceDuration, TransitionStartTime, TransitionElapsed, TransitionDuration, DestinationOffset;
        public bool InTransition, NeedsDestinationStart, FixedDuration;
        public float CurrentNormalizedTime, NextNormalizedTime, PreviousNormalizedTime;
        public float CurrentFrameCount, NextFrameCount, PreviousFrameCount;
        public float DestinationDuration, CachedDestinationLength, PreviousDuration;
        public float CurrentSpeedMultiplier, NextSpeedMultiplier, PreviousSpeedMultiplier;
        public bool InterruptionActive, EndTransitionMarker;
        // Raw graph-state byte +0x80. It is not InterruptionActive (+0x82).
        // Keep the legacy clock field serialization contract unchanged; this
        // newly qualified graph extension is checked by its lifecycle audit.
        public byte GraphFlag80 { get; set; }
        public NativeLayerTransitionState Copy() => (NativeLayerTransitionState)MemberwiseClone();
    }

    public sealed class NativeTransitionStartCommand
    {
        public int Command;
        public float Offset, OvershootSeconds;
        public bool OffsetIsFrames;
    }

    public sealed class NativeTransitionSearchResult
    {
        public bool Accepted, LastGateEligible;
        public float LastGateOvershoot;
        public uint EventCode;
        public uint[] UsedTriggers;
        public int ConditionsChecked;
        public readonly List<int> VisitedCandidates = new List<int>();
        public readonly List<int> VisitedSelectors = new List<int>();
        public readonly List<int[]> MatchedSelectorBranches = new List<int[]>();
    }

    public sealed class NativeTransitionSearch
    {
        sealed class Candidate
        {
            public NativeTransitionGate Gate;
            public uint Destination;
            public float Duration, Offset, AutoValue, AutoRatio, Exit;
            public int OffsetFrames;
            public bool CanTransitionToSelf, UseFrameCount, AutomaticOffset, HasExitTime, FixedDuration;
            public uint[] TriggerConditionHashes;

            public Candidate(JObject source)
            {
                Gate = NativeTransitionGate.FromSource(source);
                Destination = (uint)source["dest"];
                Duration = (float)source["dur"]; Offset = (float)source["off"];
                AutoValue = (float)source["autoTransitionOffsetValue"];
                AutoRatio = (float)source["autoTransitionOffsetRatio"];
                OffsetFrames = (int)source["transitionOffsetCount"];
                Exit = (float)source["exit"];
                CanTransitionToSelf = (bool)source["canTransitionToSelf"];
                UseFrameCount = (bool)source["useFrameCount"];
                AutomaticOffset = (bool)source["autoTransitionOffset"];
                HasExitTime = (bool)source["hexit"]; FixedDuration = (bool)source["hfix"];
                TriggerConditionHashes = source["conds"].Where(c => (uint)c[0] == 1).Select(c => (uint)c[1]).ToArray();
            }
        }

        readonly JObject selectorMachine;
        readonly Candidate[] candidates;

        public NativeTransitionSearch(JObject machine, JArray transitions)
        {
            if (machine == null || transitions == null) throw new ArgumentNullException();
            // Only selector definitions are needed here; do not clone every
            // state's motion trees for every prepared candidate list.
            selectorMachine = new JObject { ["selectors"] = machine["selectors"].DeepClone() };
            candidates = transitions.Cast<JObject>().Select(t => new Candidate(t)).ToArray();
        }

        // Original UnityPlayer b8a21ad9..., RVA 0xd009e0. This commits the
        // beginning of a transition. Completion, list ordering between Any /
        // Current / Next, layer mixing and trigger reset belong to the caller.
        // The native observer/callback context is absent here. Its validation
        // and selector-event behavior still require integration with the host.
        public NativeTransitionSearchResult EvaluateAndCommit(NativeLayerTransitionState state,
            NativeTransitionStartCommand command, NativeParameterBank parameters,
            float previousNormalized, float currentNormalized, float playbackDirection,
            bool sourceLoops, NativeTransitionTimingPolicy policy, int sourceState,
            uint eventCode, IEnumerable<uint> previouslyUsed = null, int? candidateCount = null)
        {
            if (state == null || command == null || parameters == null) throw new ArgumentNullException();
            int count = candidateCount ?? candidates.Length;
            if (count < 0 || count > candidates.Length) throw new ArgumentOutOfRangeException(nameof(candidateCount));
            var used = new HashSet<uint>(previouslyUsed ?? Enumerable.Empty<uint>());
            foreach (uint hash in used)
                if (!parameters.TryGetKind(hash, out int kind) || kind != 9)
                    throw new ArgumentException("Used-trigger mask contains an unqualified trigger");
            var result = new NativeTransitionSearchResult { EventCode = eventCode };
            for (int ti = 0; ti < count; ti++)
            {
                var candidate = candidates[ti];
                result.VisitedCandidates.Add(ti);
                var gate = candidate.Gate.Evaluate(parameters, previousNormalized, currentNormalized,
                    playbackDirection, sourceLoops, policy);
                result.LastGateEligible = gate.Eligible;
                result.LastGateOvershoot = gate.Overshoot;
                result.ConditionsChecked += gate.ConditionsChecked;
                if (!gate.Eligible) continue;
                // Native self rejection compares the unresolved direct target,
                // and only when the layer is not already in a transition.
                if (!candidate.CanTransitionToSelf && !state.InTransition && candidate.Destination == state.CurrentState)
                {
                    result.LastGateEligible = false;
                    continue;
                }
                foreach (uint hash in candidate.TriggerConditionHashes)
                    if (parameters.TryGetKind(hash, out int kind) && kind == 9) used.Add(hash);

                state.InTransition = true;
                state.TraversalFlags = 1;
                var route = NativeSelectorResolver.Resolve(selectorMachine, candidate.Destination,
                    parameters, state.TraversalFlags, used);
                state.NextState = route.State;
                state.TraversalFlags = route.TraversalFlags;
                used.UnionWith(route.UsedTriggers);
                result.VisitedSelectors.AddRange(route.Visited);
                result.MatchedSelectorBranches.AddRange(route.Matched);
                result.ConditionsChecked += route.ConditionsChecked;
                state.TransitionSourceState = sourceState;
                state.TransitionIndex = ti;
                state.TransitionDuration = candidate.Duration;

                bool offsetIsFrames = candidate.UseFrameCount;
                float offset = candidate.Offset;
                if (offsetIsFrames)
                {
                    offset = candidate.OffsetFrames;
                    if (!(offset > 0f)) offset = 0f;
                }
                if (candidate.AutomaticOffset)
                {
                    offsetIsFrames = false;
                    offset = currentNormalized - (float)TruncateInt32(currentNormalized);
                    offset += candidate.AutoValue;
                    if (offset < 0f)
                        offset += (float)unchecked(TruncateInt32(Math.Abs(offset)) + 1);
                    offset *= Math.Abs(candidate.AutoRatio);
                    if (!(offset > 0f)) offset = 0f;
                    offset -= (float)TruncateInt32(offset);
                }
                state.DestinationOffset = offset;
                state.TransitionElapsed = 0f;
                // The native record stores the raw exit field here even when
                // eligibility used the frame-count ratio. Preserve that order.
                state.TransitionStartTime = candidate.HasExitTime ? candidate.Exit : currentNormalized;
                state.NeedsDestinationStart = true;
                state.FixedDuration = candidate.FixedDuration;
                command.Command = 0;
                command.OffsetIsFrames = offsetIsFrames;
                command.Offset = offset;
                float sourceDuration = float.IsInfinity(state.SourceDuration) ? 0f : state.SourceDuration;
                command.OvershootSeconds = sourceDuration * gate.Overshoot;
                result.EventCode = 0x19;
                result.Accepted = true;
                break;
            }
            result.UsedTriggers = used.OrderBy(hash => hash).ToArray();
            return result;
        }

        static int TruncateInt32(float value)
        {
            // SSE cvttss2si returns the indefinite int32 for out-of-range input.
            // Do not substitute modff, floor or a saturating conversion here.
            return float.IsNaN(value) || value >= 2147483648f || value < -2147483648f
                ? int.MinValue : unchecked((int)value);
        }
    }
}
