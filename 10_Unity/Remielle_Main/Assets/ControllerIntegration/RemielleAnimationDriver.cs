// zcode 2026-09-07: C0 animation facade. Wraps RemielleNativeAnimation so the
// upcoming combat/state layers never touch the Legacy Animation component or
// the bone-basis driver directly. This is the single seam to swap the playback
// backend later (e.g. Playables) without touching gameplay code.
//
// Semantics locked by the C0 design review:
// - Legacy Animation Play/CrossFade resets a zero state speed back to 1, so a
//   hit-stop freeze (SpeedScale == 0) must be re-applied every frame by Tick().
// - state.normalizedTime is negative while a crossfade blends in; NormalizedTime
//   clamps that to 0 for callers.
// - CurrentClip is tracked here; Animation.clip is the "default animation"
//   concept and does not follow CrossFade calls.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remielle.Controller
{
    public interface IRemielleAnimationDriver
    {
        IReadOnlyList<string> AvailableClips { get; }
        string CurrentClip { get; }
        bool IsPlaying { get; }
        float NormalizedTime { get; }
        float SpeedScale { get; set; }
        bool HasClip(string clip);
        float ClipLength(string clip);
        void Play(string clip, float fade = 0.18f, float speed = 1f);
        bool TryPlay(string clip, float fade = 0.18f, float speed = 1f);
        void Tick();
    }

    public sealed class RemielleAnimationDriver : IRemielleAnimationDriver
    {
        readonly RemielleNativeAnimation native;
        readonly List<string> clips = new List<string>();
        string currentClip;

        public RemielleAnimationDriver(RemielleNativeAnimation native)
        {
            this.native = native ?? throw new ArgumentNullException(nameof(native));
            if (native.nativeAnimation == null)
                throw new ArgumentException("RemielleNativeAnimation.nativeAnimation is not assigned");
            foreach (AnimationState state in native.nativeAnimation)
                clips.Add(state.name);
            clips.Sort(StringComparer.Ordinal);
        }

        public IReadOnlyList<string> AvailableClips => clips;
        public string CurrentClip => currentClip;
        public bool IsPlaying => native.nativeAnimation.isPlaying;
        public float SpeedScale { get; set; } = 1f;

        public float NormalizedTime
        {
            get
            {
                var state = currentClip != null ? native.nativeAnimation[currentClip] : null;
                return state != null ? Mathf.Max(0f, state.normalizedTime) : 0f;
            }
        }

        public bool HasClip(string clip) => clip != null && native.nativeAnimation[clip] != null;

        public float ClipLength(string clip)
        {
            var state = native.nativeAnimation[clip];
            if (state == null) throw new ArgumentException("Unavailable native clip: " + clip);
            return state.length;
        }

        public void Play(string clip, float fade = 0.18f, float speed = 1f)
        {
            if (!HasClip(clip)) throw new ArgumentException("Unavailable native clip: " + clip);
            SpeedScale = speed;
            native.Play(clip, fade);
            currentClip = clip;
            ApplySpeed();
        }

        public bool TryPlay(string clip, float fade = 0.18f, float speed = 1f)
        {
            if (!HasClip(clip)) return false;
            Play(clip, fade, speed);
            return true;
        }

        public void Tick()
        {
            ApplySpeed();
        }

        void ApplySpeed()
        {
            var state = currentClip != null ? native.nativeAnimation[currentClip] : null;
            if (state != null) state.speed = SpeedScale;
        }
    }
}
