using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Remielle.ControllerRuntime
{
    // Explicit source-frame application to an ORIGINAL-coordinate driver rig.
    // This is not a state clock, a mixer, or a retargeting policy. Unknown paths
    // remain in the archive and are exposed to the caller before application.
    public sealed class NativeMotionPoseBinding
    {
        sealed class Node
        {
            public Transform Target;
            public Vector3 Position, Scale;
            public Quaternion Rotation;
        }
        readonly NativeMotionArchive motion;
        readonly Dictionary<uint, Node> nodes = new Dictionary<uint, Node>();
        readonly Transform[] targets;
        readonly List<(int track, SkinnedMeshRenderer renderer, int shape)> morphs = new List<(int, SkinnedMeshRenderer, int)>();
        readonly List<SkinnedMeshRenderer> morphRenderers = new List<SkinnedMeshRenderer>();
        public IReadOnlyList<uint> UnboundTransformHashes { get; }
        public IReadOnlyList<int> UnboundMorphTracks { get; }
        public IReadOnlyList<int> AnimatorScalarTracks { get; }
        public int BoundTransforms => targets.Count(x => x != null);
        public int BoundMorphs => morphs.Count;

        public NativeMotionPoseBinding(NativeMotionArchive archive, Transform driverRoot, JObject profile)
        {
            motion = archive ?? throw new ArgumentNullException(nameof(archive));
            foreach (var item in profile["nodes"])
            {
                string path = (string)item["path"];
                uint hash = (uint)item["pathHash"];
                if (unchecked((uint)Animator.StringToHash(path)) != hash) throw new InvalidDataException("Avatar path hash mismatch");
                Transform t = path.Length == 0 ? driverRoot : driverRoot.Find(path);
                if (!t) throw new InvalidDataException("Driver is missing original Avatar path: " + path);
                var q = item["rotation"];
                nodes.Add(hash, new Node { Target = t, Position = Vec(item["position"]), Scale = Vec(item["scale"]),
                    Rotation = new Quaternion((float)q[0], (float)q[1], (float)q[2], (float)q[3]) });
                var smr = t.GetComponent<SkinnedMeshRenderer>();
                if (smr && smr.sharedMesh && smr.sharedMesh.blendShapeCount > 0) morphRenderers.Add(smr);
            }
            targets = new Transform[motion.TransformCount];
            var unbound = new List<uint>();
            for (int i = 0; i < targets.Length; i++)
            {
                uint hash = motion.TransformHash(i);
                if (nodes.TryGetValue(hash, out var node)) targets[i] = node.Target;
                else unbound.Add(hash);
            }
            UnboundTransformHashes = unbound.AsReadOnly();
            var unboundMorphs = new List<int>();
            var animatorScalars = new List<int>();
            for (int i = 0; i < motion.ScalarCount; i++)
            {
                var b = motion.ScalarBinding(i);
                if ((string)b["typeID"] == "Animator")
                {
                    // Preserve these channels for the future root/motion stage.
                    // Applying them here would duplicate authored root motion.
                    animatorScalars.Add(i);
                    continue;
                }
                if ((string)b["typeID"] != "SkinnedMeshRenderer") throw new InvalidDataException("Unknown scalar target type");
                uint path = (uint)b["path"], attribute = (uint)b["attribute"];
                SkinnedMeshRenderer renderer = nodes.TryGetValue(path, out var node) ? node.Target.GetComponent<SkinnedMeshRenderer>() : null;
                int found = -1;
                if (renderer && renderer.sharedMesh)
                    for (int j = 0; j < renderer.sharedMesh.blendShapeCount; j++)
                        if (unchecked((uint)Animator.StringToHash(renderer.sharedMesh.GetBlendShapeName(j))) == attribute)
                        {
                            if (found != -1) throw new InvalidDataException("Ambiguous morph hash");
                            found = j;
                        }
                if (found == -1) unboundMorphs.Add(i);
                else morphs.Add((i, renderer, found));
            }
            UnboundMorphTracks = unboundMorphs.AsReadOnly();
            AnimatorScalarTracks = animatorScalars.AsReadOnly();
        }

        public void ResetDefaults()
        {
            foreach (var node in nodes.Values)
            {
                node.Target.localPosition = node.Position;
                node.Target.localRotation = node.Rotation;
                node.Target.localScale = node.Scale;
            }
            foreach (var renderer in morphRenderers)
                for (int i = 0; i < renderer.sharedMesh.blendShapeCount; i++) renderer.SetBlendShapeWeight(i, 0);
        }

        public void ApplyFrame(int frame, bool allowUnboundChannels = false)
        {
            if ((uint)frame >= motion.FrameCount) throw new ArgumentOutOfRangeException(nameof(frame));
            if (!allowUnboundChannels && (UnboundTransformHashes.Count != 0 || UnboundMorphTracks.Count != 0))
                throw new InvalidOperationException("Unbound source channels require an explicit binding decision");
            for (int i = 0; i < targets.Length; i++)
            {
                if (!targets[i]) continue;
                motion.ReadTransform(frame, i, out var p, out var q, out var s);
                targets[i].localPosition = p;
                targets[i].localRotation = q;
                targets[i].localScale = s;
            }
            foreach (var m in morphs) m.renderer.SetBlendShapeWeight(m.shape, motion.ReadScalar(frame, m.track));
        }

        // Transform output from the full-tier codec. Scalar output is applied
        // separately with ApplyScalarSamples, using the same selected time.
        public void ApplyTransformSamples(float[] samples, bool allowUnboundChannels = false)
        {
            if (samples == null || samples.Length != checked(targets.Length * 10)) throw new ArgumentException("Sample dimensions");
            if (!allowUnboundChannels && UnboundTransformHashes.Count != 0)
                throw new InvalidOperationException("Unbound source transforms require an explicit binding decision");
            foreach (float value in samples)
                if (float.IsNaN(value) || float.IsInfinity(value)) throw new InvalidDataException("Nonfinite transform sample");
            for (int i = 0; i < targets.Length; i++)
            {
                if (!targets[i]) continue;
                int p = i*10;
                targets[i].localRotation = new Quaternion(samples[p], samples[p+1], samples[p+2], samples[p+3]);
                targets[i].localPosition = new Vector3(samples[p+4], samples[p+5], samples[p+6]);
                targets[i].localScale = new Vector3(samples[p+7], samples[p+8], samples[p+9]);
            }
        }

        public void ApplyScalarSamples(float[] samples, bool allowUnboundChannels = false)
        {
            if (samples == null || samples.Length != motion.ScalarCount) throw new ArgumentException("Scalar dimensions");
            if (!allowUnboundChannels && UnboundMorphTracks.Count != 0)
                throw new InvalidOperationException("Unbound source morphs require an explicit binding decision");
            foreach (float value in samples)
                if (float.IsNaN(value) || float.IsInfinity(value)) throw new InvalidDataException("Nonfinite scalar sample");
            foreach (var m in morphs) m.renderer.SetBlendShapeWeight(m.shape, samples[m.track]);
            // AnimatorScalarTracks remain available to the controller/motor;
            // they must not be added again over the authored root Transform.
        }

        static Vector3 Vec(JToken v) => new Vector3((float)v[0], (float)v[1], (float)v[2]);
    }
}
