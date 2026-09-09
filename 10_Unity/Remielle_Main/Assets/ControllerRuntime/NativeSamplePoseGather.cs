using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Remielle.ControllerRuntime
{
    // Independent channel transport layout. It retains every supplied source
    // binding, including paths outside the current Avatar. Avatar filtering and
    // retargeting must be qualified separately before applying this to a rig.
    public sealed class NativePoseBindingLayout
    {
        readonly string[][] keys;
        readonly Dictionary<string, int>[] indices;
        readonly Dictionary<string, int> specialIndices;
        readonly string[] specialKeys;
        public NativePoseBindingLayout(IEnumerable<JObject> sourceBindings) : this(sourceBindings, null) { }
        internal NativePoseBindingLayout(IEnumerable<JObject> sourceBindings, IEnumerable<JObject> specialBindings)
        {
            if (sourceBindings == null) throw new ArgumentNullException(nameof(sourceBindings));
            var groups = Enumerable.Range(0, 5).Select(_ => new List<string>()).ToArray();
            foreach (var binding in sourceBindings) groups[Group(binding)].Add(Key(binding));
            keys = groups.Select(x => specialBindings == null ? x.Distinct(StringComparer.Ordinal).OrderBy(k=>k,StringComparer.Ordinal).ToArray() : x.Distinct(StringComparer.Ordinal).ToArray()).ToArray();
            indices = keys.Select(x => x.Select((k, i) => (k, i)).ToDictionary(p => p.k, p => p.i, StringComparer.Ordinal)).ToArray();
            specialKeys = specialBindings == null ? Array.Empty<string>() : specialBindings.Select(b=>{
                if (Group(b)!=3 || !NativeSourceBindingRules.Special(NativeCurveBinding.FromSource(b)))
                    throw new ArgumentException("Only qualified special bindings can leave the ordinary pose table");
                if (indices[3].ContainsKey(Key(b))) throw new ArgumentException("Special and ordinary layouts overlap");
                return Key(b);
            }).ToArray();
            specialIndices = specialKeys.Select((k,i)=>(k,i)).ToDictionary(p=>p.k,p=>p.i,StringComparer.Ordinal);
        }
        public int[] Counts() => keys.Select(x => x.Length).ToArray();
        public string[][] Keys() => keys.Select(x => (string[])x.Clone()).ToArray();
        public string[] SpecialKeys() => (string[])specialKeys.Clone();
        public NativePoseSampleMap Map(NativeMotionArchive archive)
        {
            if (archive == null) throw new ArgumentNullException(nameof(archive));
            var bindings = archive.Metadata["bindings"].Cast<JObject>().ToArray();
            var offsets = keys.Select(x => Enumerable.Repeat(-1, x.Length).ToArray()).ToArray();
            var specialOffsets = Enumerable.Repeat(-1, specialKeys.Length).ToArray();
            int sourceOffset = 0;
            foreach (var binding in bindings)
            {
                int group = Group(binding); string key = Key(binding);
                if (specialIndices.TryGetValue(key, out int specialTarget))
                {
                    if (specialOffsets[specialTarget]!=-1) throw new InvalidDataException("Duplicate special source binding: " + key);
                    specialOffsets[specialTarget]=sourceOffset;sourceOffset=checked(sourceOffset+1);continue;
                }
                if (!indices[group].TryGetValue(key, out int target))
                    throw new InvalidDataException("Source binding is absent from the declared transport layout: " + key);
                if (offsets[group][target] != -1) throw new InvalidDataException("Duplicate source binding: " + key);
                offsets[group][target] = sourceOffset;
                sourceOffset = checked(sourceOffset + (group == 1 ? 4 : group < 3 ? 3 : 1));
            }
            if (sourceOffset != checked(archive.TransformCount * 10 + archive.ScalarCount))
                throw new InvalidDataException("Source binding width differs from recovered sample layout");
            return new NativePoseSampleMap(this, offsets, sourceOffset, specialOffsets);
        }
        static int Group(JObject binding)
        {
            if ((int)binding["isPPtrCurve"] != 0 || (int)binding["isIntCurve"] != 0)
                throw new NotSupportedException("This source gather path requires continuous float bindings");
            var script = binding["script"];
            if ((int)script["m_FileID"] != 0 || (long)script["m_PathID"] != 0)
                throw new NotSupportedException("Script binding requires a qualified external identity");
            if ((string)binding["typeID"] != "Transform") return 3;
            switch ((uint)binding["attribute"])
            {
                case 1: return 0;
                case 2: return 1;
                case 3: return 2;
                default: throw new NotSupportedException("Euler/other Transform bindings need a separate source path");
            }
        }
        // Script/PPtr/int inputs are checked above. Keep component class and
        // custom attribute type, not just the path or the property hash.
        static string Key(JObject b) => (string)b["typeID"] + ":" + ((uint)b["path"]).ToString("x8", CultureInfo.InvariantCulture) + ":" +
            ((uint)b["attribute"]).ToString("x8", CultureInfo.InvariantCulture) + ":" + ((uint)b["customType"]).ToString("x8", CultureInfo.InvariantCulture);
    }

    public sealed class NativePoseSampleMap
    {
        readonly int[][] offsets;
        readonly int[] specialOffsets;
        public NativePoseBindingLayout Layout { get; }
        public int SourceFloatCount { get; }
        internal NativePoseSampleMap(NativePoseBindingLayout layout, int[][] sourceOffsets, int sourceFloatCount, int[] specialSourceOffsets)
        { Layout = layout; offsets = sourceOffsets; SourceFloatCount = sourceFloatCount; specialOffsets = specialSourceOffsets; }
        public int[][] Offsets() => offsets.Select(x => (int[])x.Clone()).ToArray();
        public int[] SpecialOffsets() => (int[])specialOffsets.Clone();
        public float?[] ReadSpecial(float[] source)
        {
            if (source==null || source.Length!=SourceFloatCount) throw new ArgumentException("Source sample dimensions differ");
            return specialOffsets.Select(i=>i<0 ? (float?)null : source[i]).ToArray();
        }
        public void Gather(float[] source, NativePoseStream defaults, NativePoseStream output,
            bool markDefaultsActive, byte[][] writeMask = null)
        {
            Check(source, defaults, output);
            NativeSamplePoseGather.Gather(source, offsets, defaults.Data, output, markDefaultsActive, writeMask);
        }
        internal void Check(float[] source, NativePoseStream defaults, NativePoseStream output)
        {
            if (source == null || source.Length != SourceFloatCount) throw new ArgumentException("Source sample dimensions differ");
            if (defaults == null || output == null || !ReferenceEquals(defaults.BindingLayout, Layout) || !ReferenceEquals(output.BindingLayout, Layout))
                throw new ArgumentException("Sample map, defaults and output must share a declared binding layout");
        }
        internal int[][] SourceOffsets => offsets;
    }

    // Direct complete-tier sampling followed by the qualified original gather.
    // Time conversion, default/mask policy and subsequent root corrections are
    // supplied by the caller; this does not create an Animator/Avatar policy.
    public sealed class NativeSampledPoseSource : IDisposable
    {
        readonly NativeMotionCodec codec;
        readonly float[] transforms, scalars, samples;
        bool disposed;
        bool sampled;
        public NativePoseSampleMap Map { get; }
        public string AssetID { get; }
        public NativeSampledPoseSource(string bankDirectory, string assetID, NativePoseBindingLayout layout)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            var bank = new NativeMotionBank(bankDirectory);
            var archive = bank.Load(assetID);
            Map = layout.Map(archive); AssetID = assetID;
            transforms = new float[checked(archive.TransformCount * 10)];
            scalars = new float[archive.ScalarCount]; samples = new float[Map.SourceFloatCount];
            codec = new NativeMotionCodec(bankDirectory, bank.Record(assetID));
        }
        public void Sample(float time, NativePoseStream defaults, NativePoseStream output,
            bool markDefaultsActive, byte[][] writeMask = null)
        {
            ReadSamples(time);
            Map.Gather(samples, defaults, output, markDefaultsActive, writeMask);
            sampled=true;
        }
        // The original prepass and ordinary pass reuse one decoded sample.
        // Root/loop/additive corrections have separate scheduling boundaries.
        public void SampleForPreparation(float time, NativeClipPoseContext context, NativePoseStream output)
        {
            ReadSamples(time);
            NativeClipPoseChannels.Prepare(Map, samples, context, output);
            sampled=true;
        }
        public void GatherCurrent(NativeClipPoseContext context, NativePoseStream output)
        {
            if (disposed) throw new ObjectDisposedException(nameof(NativeSampledPoseSource));
            if (!sampled) throw new InvalidOperationException("No completed source sample is available");
            NativeClipPoseChannels.Gather(Map, samples, context, output);
        }
        void ReadSamples(float time)
        {
            if (disposed) throw new ObjectDisposedException(nameof(NativeSampledPoseSource));
            sampled=false;
            codec.SampleTransforms(time, transforms);
            if (scalars.Length != 0) codec.SampleScalars(time, scalars);
            Array.Copy(transforms, samples, transforms.Length);
            Array.Copy(scalars, 0, samples, transforms.Length, scalars.Length);
        }
        public float?[] SpecialSamples()
        {
            if (disposed) throw new ObjectDisposedException(nameof(NativeSampledPoseSource));
            if (!sampled) throw new InvalidOperationException("No completed source sample is available");
            return Map.ReadSpecial(samples);
        }
        public void Dispose() { if (!disposed) { codec.Dispose(); disposed = true; } }
    }

    // Original cedf90 float/quaternion gather path. Every current controller
    // source uses this binding representation; Euler and discrete paths remain
    // separate. Indices address scalar offsets in the sampled flat buffer.
    public static class NativeSamplePoseGather
    {
        public static void Gather(float[] source, int[][] offsets, NativePoseData defaults,
            NativePoseStream output, bool markDefaultsActive, byte[][] writeMask = null)
        {
            Validate(source, offsets, defaults, output, writeMask);
            var counts = output.Data.Counts;
            for (int g = 0; g < 4; g++) for (int i = 0; i < counts[g]; i++)
            {
                if (writeMask != null && writeMask[g][i] == 0) continue;
                WriteChannel(source, offsets, defaults, output, markDefaultsActive, g, i);
            }
        }
        internal static void Validate(float[] source, int[][] offsets, NativePoseData defaults,
            NativePoseStream output, byte[][] writeMask)
        {
            if (source == null || output == null || offsets == null || offsets.Length != 5) throw new ArgumentNullException();
            output.CheckPose(defaults);
            if (writeMask != null) output.CheckMask(writeMask);
            var counts = output.Data.Counts;
            if (counts[4] != 0) throw new NotSupportedException("Discrete sample gather is not part of this source path");
            for (int g = 0; g < 5; g++)
            {
                if (offsets[g] == null || offsets[g].Length != counts[g]) throw new ArgumentException("Gather map dimensions differ");
                int width = g == 1 ? 4 : g < 3 ? 3 : 1;
                foreach (int offset in offsets[g])
                    if (offset < -1 || offset > short.MaxValue || (offset >= 0 && offset > source.Length - width))
                        throw new ArgumentException("Invalid original signed sample offset");
            }
        }
        internal static void WriteChannel(float[] source, int[][] offsets, NativePoseData defaults,
            NativePoseStream output, bool markDefaultsActive, int g, int i)
        {
                int offset = offsets[g][i], target = i * (g < 3 ? 4 : 1);
                if (offset == -1)
                {
                    Array.Copy(defaults.Floats[g], target, output.Data.Floats[g], target, g < 3 ? 4 : 1);
                    output.Active[g][i] = markDefaultsActive ? (byte)1 : (byte)0;
                    return;
                }
                if (g == 1) NormalizeQuaternion(source, offset, output.Data.Floats[g], target);
                else
                {
                    Array.Copy(source, offset, output.Data.Floats[g], target, g < 3 ? 3 : 1);
                    if (g < 3) output.Data.Floats[g][target + 3] = 0f;
                }
                output.Active[g][i] = 1;
        }
        static void NormalizeQuaternion(float[] source, int offset, float[] destination, int target)
        {
            float x = source[offset], y = source[offset + 1], z = source[offset + 2], w = source[offset + 3];
            float p0 = (float)(x * x), p1 = (float)(y * y), p2 = (float)(z * z), p3 = (float)(w * w);
            float a = (float)(p0 + p1), b = (float)(p1 + p2), c = (float)(p2 + p3), d = (float)(p3 + p0);
            Lane(x, (float)(c + a), 0f, destination, target);
            Lane(y, (float)(d + b), 0f, destination, target + 1);
            Lane(z, (float)(a + c), 0f, destination, target + 2);
            Lane(w, (float)(b + d), 1f, destination, target + 3);
        }
        static void Lane(float value, float squaredLength, float identity, float[] output, int index)
        {
            // cedf90 uses precise sqrt/divide and a 1e-30 guard, distinct from
            // the mixer finalization's approximate reciprocal-square-root path.
            float epsilon = BitConverter.Int32BitsToSingle(0x0da24260);
            output[index] = squaredLength > epsilon ? (float)(value / (float)Math.Sqrt(squaredLength)) : identity;
        }
    }
}
