using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Remielle.ControllerRuntime
{
    // Reads the shipped recovery archives directly. No curve fitting, frame
    // reduction, quaternion sign changes, or retiming takes place at this layer.
    public sealed class NativeMotionArchive
    {
        readonly float[] transforms, scalars, times;
        readonly uint[] transformHashes;
        readonly JObject metadata;
        readonly JArray scalarBindings;
        public int FrameCount => times.Length;
        public int TransformCount => transformHashes.Length;
        public int ScalarCount { get; }
        public float Duration { get; }
        public float SampleRate { get; }
        public bool Loop { get; }
        public string Name => (string)metadata["name"];
        public string SourceCab => (string)metadata["sourceCab"];
        public string SourcePathID => ((long)metadata["sourcePathID"]).ToString(CultureInfo.InvariantCulture);
        public JObject Metadata => (JObject)metadata.DeepClone();
        public uint TransformHash(int track) => transformHashes[track];
        public JObject ScalarBinding(int track) => (JObject)scalarBindings[track].DeepClone();
        public float Time(int frame) => times[frame];

        NativeMotionArchive(Npy rotations, Npy scalar, Npy time, Npy meta)
        {
            if (!meta.Descriptor.StartsWith("<U", StringComparison.Ordinal) || meta.Shape.Length != 0)
                throw new InvalidDataException("Metadata must be a scalar little-endian NumPy Unicode value");
            int codePoints = int.Parse(meta.Descriptor.Substring(2), CultureInfo.InvariantCulture);
            if (meta.Data.Length != checked(codePoints * 4)) throw new InvalidDataException("Metadata size");
            metadata = JObject.Parse(new UTF32Encoding(false, false, true).GetString(meta.Data));
            int n = (int)metadata["transformCount"], frames = (int)metadata["frameCount"];
            ScalarCount = (int)metadata["scalarCount"];
            if (n < 0 || frames <= 0 || ScalarCount < 0) throw new InvalidDataException("Negative/empty shape");
            transforms = rotations.Floats(frames, checked(n * 10));
            scalars = scalar.Floats(frames, ScalarCount);
            times = time.Floats(frames);
            Duration = (float)metadata["duration"];
            SampleRate = (float)metadata["sampleRate"];
            Loop = (bool)metadata["loop"];
            if (!(SampleRate > 0) || !(Duration >= 0) || float.IsInfinity(Duration) ||
                !(bool)metadata["allQualityTiersLoaded"] || !(bool)metadata["allScalarFramesPresent"])
                throw new InvalidDataException("Incomplete animation recovery");
            if (times[0] != 0 || times[frames - 1] < Duration) throw new InvalidDataException("Time extent");
            for (int i = 1; i < frames; i++)
                if (!(times[i] > times[i - 1])) throw new InvalidDataException("Unordered sample times");
            var bindings = (JArray)metadata["bindings"];
            if (bindings.Count != checked(n * 3 + ScalarCount)) throw new InvalidDataException("Binding dimensions");
            transformHashes = new uint[n];
            var seen = new HashSet<uint>();
            int[] attributes = { 2, 1, 3 };
            for (int i = 0; i < n; i++)
            {
                uint hash = (uint)bindings[i * 3]["path"];
                if (!seen.Add(hash)) throw new InvalidDataException("Duplicate transform binding");
                transformHashes[i] = hash;
                for (int j = 0; j < 3; j++)
                {
                    var b = bindings[i * 3 + j];
                    if ((string)b["typeID"] != "Transform" || (uint)b["path"] != hash || (int)b["attribute"] != attributes[j])
                        throw new InvalidDataException("Original transform binding order differs");
                }
            }
            scalarBindings = new JArray(bindings.Skip(n * 3).Select(x => x.DeepClone()));
            foreach (var b in bindings)
                if ((int)b["isPPtrCurve"] != 0 || (int)b["isIntCurve"] != 0)
                    throw new InvalidDataException("Unsupported non-float channel");
        }

        public static NativeMotionArchive Load(string path, string expectedSha256)
        {
            if (!BitConverter.IsLittleEndian) throw new PlatformNotSupportedException("Little-endian motion bank");
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                using (var hash = SHA256.Create())
                    if (!StringComparer.OrdinalIgnoreCase.Equals(Hex(hash.ComputeHash(file)), expectedSha256))
                        throw new InvalidDataException("Motion archive checksum mismatch: " + path);
                file.Position = 0;
                using (var zip = new ZipArchive(file, ZipArchiveMode.Read))
                {
                    string[] required = { "transforms.npy", "scalars.npy", "times.npy", "metadata.npy" };
                    if (zip.Entries.Count != 4 || required.Any(name => zip.Entries.Count(x => x.FullName == name) != 1))
                        throw new InvalidDataException("Unexpected NPZ entries");
                    return new NativeMotionArchive(Npy.Read(zip.GetEntry(required[0])), Npy.Read(zip.GetEntry(required[1])),
                        Npy.Read(zip.GetEntry(required[2])), Npy.Read(zip.GetEntry(required[3])));
                }
            }
        }

        public void ReadTransform(int frame, int track, out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            if ((uint)frame >= FrameCount || (uint)track >= TransformCount) throw new ArgumentOutOfRangeException();
            int p = checked((frame * TransformCount + track) * 10);
            rotation = new Quaternion(transforms[p], transforms[p+1], transforms[p+2], transforms[p+3]);
            position = new Vector3(transforms[p+4], transforms[p+5], transforms[p+6]);
            scale = new Vector3(transforms[p+7], transforms[p+8], transforms[p+9]);
        }

        public float ReadScalar(int frame, int track)
        {
            if ((uint)frame >= FrameCount || (uint)track >= ScalarCount) throw new ArgumentOutOfRangeException();
            return scalars[checked(frame * ScalarCount + track)];
        }

        // Independent Python expectations cover every byte, including source
        // guard frames and signed zero, not only selected transform endpoints.
        public string ArraySha256(string name)
        {
            float[] values;
            switch (name)
            {
                case "transforms": values = transforms; break;
                case "scalars": values = scalars; break;
                case "times": values = times; break;
                default: throw new ArgumentException("Unknown array: " + name);
            }
            var bytes = new byte[checked(values.Length * 4)];
            Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
            using (var hash = SHA256.Create()) return Hex(hash.ComputeHash(bytes));
        }

        public static string Hex(byte[] value) => BitConverter.ToString(value).Replace("-", "").ToLowerInvariant();

        sealed class Npy
        {
            public string Descriptor;
            public int[] Shape;
            public byte[] Data;

            public float[] Floats(params int[] shape)
            {
                if (Descriptor != "<f4" || !Shape.SequenceEqual(shape)) throw new InvalidDataException("NPY float32 shape/dtype");
                int count = 1;
                foreach (int n in shape) count = checked(count * n);
                if (Data.Length != checked(count * 4)) throw new InvalidDataException("NPY payload size");
                var result = new float[count];
                Buffer.BlockCopy(Data, 0, result, 0, Data.Length);
                foreach (float value in result)
                    if (float.IsNaN(value) || float.IsInfinity(value)) throw new InvalidDataException("Nonfinite source sample");
                return result;
            }

            public static Npy Read(ZipArchiveEntry entry)
            {
                using (var stream = entry.Open())
                using (var reader = new BinaryReader(stream, Encoding.UTF8))
                {
                    byte[] magic = Exact(reader, 6);
                    if (!magic.SequenceEqual(new byte[] { 0x93, 78, 85, 77, 80, 89 })) throw new InvalidDataException("NPY magic");
                    int major = reader.ReadByte(), minor = reader.ReadByte();
                    if ((major != 1 && major != 2) || minor != 0) throw new InvalidDataException("Unsupported NPY version");
                    int headerLength = major == 1 ? reader.ReadUInt16() : checked((int)reader.ReadUInt32());
                    if (headerLength <= 0 || headerLength > 65536) throw new InvalidDataException("NPY header length");
                    string header = Encoding.ASCII.GetString(Exact(reader, headerLength));
                    var descriptor = Regex.Match(header, @"'descr'\s*:\s*'([^']+)'");
                    var shape = Regex.Match(header, @"'shape'\s*:\s*\(([0-9,\s]*)\)");
                    if (!descriptor.Success || !shape.Success || !Regex.IsMatch(header, @"'fortran_order'\s*:\s*False"))
                        throw new InvalidDataException("Unsupported NPY header");
                    string dtype = descriptor.Groups[1].Value;
                    if (dtype != "<f4" && !Regex.IsMatch(dtype, @"^<U[0-9]+$"))
                        throw new InvalidDataException("Only float32 and Unicode metadata are supported");
                    int[] dimensions = shape.Groups[1].Value.Split(',').Where(s => s.Trim().Length > 0)
                        .Select(s => int.Parse(s.Trim(), CultureInfo.InvariantCulture)).ToArray();
                    long bytes = entry.Length - (major == 1 ? 10 : 12) - headerLength;
                    if (bytes < 0 || bytes > 512L * 1024 * 1024) throw new InvalidDataException("NPY payload limit");
                    var data = Exact(reader, checked((int)bytes));
                    if (stream.ReadByte() != -1) throw new InvalidDataException("Trailing NPY data");
                    return new Npy { Descriptor = dtype, Shape = dimensions, Data = data };
                }
            }

            static byte[] Exact(BinaryReader reader, int count)
            {
                byte[] data = reader.ReadBytes(count);
                if (data.Length != count) throw new EndOfStreamException("Truncated NPY");
                return data;
            }
        }
    }

    public sealed class NativeMotionBank
    {
        readonly string root;
        readonly Dictionary<string, JObject> records;
        readonly Dictionary<(string, int), string> slots;
        public IEnumerable<string> AssetIDs => records.Keys;

        public NativeMotionBank(string directory)
        {
            root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var index = JObject.Parse(File.ReadAllText(Path.Combine(root, "index.json")));
            if ((string)index["schema"] != "remielle-controller-motion-bank-v1") throw new InvalidDataException("Motion bank schema");
            records = ((JArray)index["motions"]).Cast<JObject>().ToDictionary(x => (string)x["assetID"], StringComparer.Ordinal);
            slots = ((JArray)index["slots"]).ToDictionary(x => ((string)x["controller"], (int)x["clipIndex"]), x => (string)x["assetID"]);
            if (slots.Values.Any(x => !records.ContainsKey(x))) throw new InvalidDataException("Missing motion bank slot");
        }

        public string ResolveSlot(string controller, int clipIndex) => slots[(controller, clipIndex)];
        public JObject Record(string assetID) => (JObject)records[assetID].DeepClone();

        public NativeMotionArchive Load(string assetID)
        {
            JObject record = records[assetID];
            string path = Path.GetFullPath(Path.Combine(root, (string)record["relativePath"]));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Motion path escapes bank");
            var motion = NativeMotionArchive.Load(path, (string)record["sha256"]);
            if (motion.SourceCab != (string)record["source"]["cab"] || motion.SourcePathID != (string)record["source"]["pathID"])
                throw new InvalidDataException("Motion source identity mismatch");
            return motion;
        }
    }
}
