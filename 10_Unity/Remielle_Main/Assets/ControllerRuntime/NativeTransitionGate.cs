using System;
using Newtonsoft.Json.Linq;

namespace Remielle.ControllerRuntime
{
    // Source TimeManager qualifies the first input; the state executor must
    // supply the prior end-transition marker for the caller's seventh argument.
    public readonly struct NativeTransitionTimingPolicy
    {
        public readonly bool ComparisonFix;
        public readonly bool IncludePreviousBoundary;

        public NativeTransitionTimingPolicy(bool comparisonFix, bool includePreviousBoundary)
        {
            ComparisonFix = comparisonFix;
            IncludePreviousBoundary = includePreviousBoundary;
        }
    }

    public readonly struct NativeTransitionEligibility
    {
        public readonly bool Eligible;
        // Signed normalized time beyond the window; retained even if a later
        // parameter condition fails. This is not a clip time or blend weight.
        public readonly float Overshoot;
        public readonly int ConditionsChecked;

        public NativeTransitionEligibility(bool eligible, float overshoot, int conditionsChecked)
        {
            Eligible = eligible; Overshoot = overshoot; ConditionsChecked = conditionsChecked;
        }
    }

    public sealed class NativeTransitionGate
    {
        // UnityPlayer b8a21ad9..., RVA 0xd00540; native constant bits 0x3727c5ac.
        const float BoundaryTolerance = 0.00001f;
        readonly NativeCondition[] conditions;
        public bool HasExitTime { get; }
        public bool UseFrameCount { get; }
        public float ExitTime { get; }
        public int FrameCount { get; }
        public int TotalFramesSource { get; }

        NativeTransitionGate(JObject source)
        {
            HasExitTime = (bool)source["hexit"];
            UseFrameCount = (bool)source["useFrameCount"];
            ExitTime = (float)source["exit"];
            FrameCount = (int)source["frameCount"];
            TotalFramesSource = (int)source["totalFramesSrc"];
            RequireFinite(ExitTime, "exit");
            var fields = (JArray)source["conds"];
            conditions = new NativeCondition[fields.Count];
            for (int i = 0; i < conditions.Length; i++)
                conditions[i] = NativeCondition.FromSource((JArray)fields[i]);
        }

        public static NativeTransitionGate FromSource(JObject source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            return new NativeTransitionGate(source);
        }

        // Keeps float32 int conversion/division and the zero-denominator rule.
        public float NormalizedExitTime
        {
            get
            {
                if (!UseFrameCount) return ExitTime;
                if (TotalFramesSource == 0) return 1f;
                float ratio = (float)FrameCount / (float)TotalFramesSource;
                return ratio > 0f ? ratio : 0f;
            }
        }

        // This reads an already advanced clock window. It does not advance a
        // clock, mutate parameters, resolve a destination or commit a transition.
        public NativeTransitionEligibility Evaluate(NativeParameterBank parameters,
            float previousNormalized, float currentNormalized, float playbackDirection,
            bool sourceLoops, NativeTransitionTimingPolicy policy)
        {
            if (parameters == null) throw new ArgumentNullException(nameof(parameters));
            RequireFinite(previousNormalized, nameof(previousNormalized));
            RequireFinite(currentNormalized, nameof(currentNormalized));
            RequireFinite(playbackDirection, nameof(playbackDirection));
            bool eligible = conditions.Length > 0;
            float overshoot = 0f;
            if (HasExitTime)
                eligible = CrossesExit(previousNormalized, currentNormalized, playbackDirection,
                    sourceLoops, policy, out overshoot);
            int checkedCount = 0;
            if (eligible)
            {
                foreach (var condition in conditions)
                {
                    checkedCount++;
                    if (!condition.Evaluate(parameters))
                    {
                        eligible = false;
                        break;
                    }
                }
            }
            return new NativeTransitionEligibility(eligible, overshoot, checkedCount);
        }

        bool CrossesExit(float previous, float current, float direction, bool loops,
            NativeTransitionTimingPolicy policy, out float overshoot)
        {
            overshoot = 0f;
            float exit = NormalizedExitTime;
            bool forward = direction >= 0f;
            if (exit > 1f)
            {
                // The comparison fix includes the old endpoint here regardless
                // of the separate caller mode used by the cyclic branch.
                bool hit = forward
                    ? (policy.ComparisonFix ? exit >= previous : exit > previous) && current >= exit
                    : (policy.ComparisonFix ? previous >= exit : previous > exit) && exit >= current;
                if (hit) overshoot = Snap(current - exit, policy.ComparisonFix);
                return hit;
            }

            // Original modff truncates toward zero, including negative clocks.
            // Inspect both the previous and current cycle; do not modulo/clamp
            // the clocks separately or manufacture a new positive phase.
            float previousCycle = (float)Math.Truncate((double)previous);
            float currentCycle = (float)Math.Truncate((double)current);
            float relativePrevious = previous - previousCycle;
            float relativeCurrent = current - previousCycle;
            bool crossed = CrossesCyclic(relativePrevious, relativeCurrent, exit, forward, policy);
            if (!crossed && previousCycle != currentCycle)
            {
                relativePrevious = previous - currentCycle;
                relativeCurrent = current - currentCycle;
                crossed = CrossesCyclic(relativePrevious, relativeCurrent, exit, forward, policy);
            }
            if (crossed)
            {
                overshoot = Snap(relativeCurrent - exit, policy.ComparisonFix);
                return true;
            }
            // Completed non-looping states may still satisfy exit=1 after the
            // crossing frame; the original forces overshoot to positive zero.
            return exit == 1f && previous >= 1f && current >= 1f && !loops &&
                (forward ? current >= exit : exit >= current);
        }

        static bool CrossesCyclic(float previous, float current, float exit, bool forward,
            NativeTransitionTimingPolicy policy)
        {
            bool oldSide = forward ? exit > previous : previous > exit;
            bool newSide = forward ? current >= exit : exit >= current;
            if (policy.ComparisonFix)
            {
                if (policy.IncludePreviousBoundary)
                    oldSide |= Math.Abs(exit - previous) <= BoundaryTolerance;
                newSide = (forward ? current > exit : exit > current) ||
                    Math.Abs(exit - current) <= BoundaryTolerance;
            }
            return oldSide && newSide;
        }

        static float Snap(float value, bool fix)
        {
            return fix && Math.Abs(value) <= BoundaryTolerance ? 0f : value;
        }

        static void RequireFinite(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentOutOfRangeException(name, "Native clock input must be finite");
        }
    }
}
