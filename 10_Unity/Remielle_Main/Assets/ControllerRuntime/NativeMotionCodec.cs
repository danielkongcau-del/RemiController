using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
using Newtonsoft.Json.Linq;

namespace Remielle.ControllerRuntime
{
    // Windows x64, using our local ACL adapter rather than any original game
    // DLL. Owns a complete, persistent database; Dispose releases its buffers.
    public sealed class NativeMotionCodec : IDisposable
    {
        readonly object gate = new object();
        readonly CodecHandle handle;
        readonly CodecHandle scalarHandle;
        public int TransformCount { get; }
        public int ScalarCount { get; }
        public float Duration { get; }

        public NativeMotionCodec(string bankDirectory, JObject record)
        {
            string root = Path.GetFullPath(bankDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            byte[] Read(string field)
            {
                var part = record["codec"][field];
                string path = Path.GetFullPath(Path.Combine(root, (string)part["relativePath"]));
                if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Codec path escapes bank");
                byte[] bytes = File.ReadAllBytes(path);
                if (bytes.Length != (long)part["bytes"]) throw new InvalidDataException("Codec payload size");
                using (var hash = SHA256.Create())
                    if (NativeMotionArchive.Hex(hash.ComputeHash(bytes)) != (string)part["sha256"])
                        throw new InvalidDataException("Codec payload checksum: " + field);
                return bytes;
            }
            byte[] transforms = Read("transforms"), database = Read("database"), bulk = Read("bulk");
            IntPtr ptr = Create(transforms, (uint)transforms.Length, database, (uint)database.Length, bulk, (uint)bulk.Length, out int status);
            if (ptr == IntPtr.Zero || status != 0)
            {
                if (ptr != IntPtr.Zero) Destroy(ptr);
                throw new InvalidDataException("Full-tier ACL initialization failed: " + status);
            }
            handle = new CodecHandle(ptr);
            TransformCount = (int)record["summary"]["transformCount"];
            ScalarCount = (int)record["summary"]["scalarCount"];
            Duration = (float)record["summary"]["duration"];
            try
            {
                if (ScalarCount > 0)
                {
                    byte[] scalars = Read("scalars");
                    IntPtr scalarPtr = Create(scalars, (uint)scalars.Length, database, (uint)database.Length, bulk, (uint)bulk.Length, out status);
                    if (scalarPtr == IntPtr.Zero || status != 0)
                    {
                        if (scalarPtr != IntPtr.Zero) Destroy(scalarPtr);
                        throw new InvalidDataException("Full-tier scalar ACL initialization failed: " + status);
                    }
                    scalarHandle = new CodecHandle(scalarPtr);
                }
            }
            catch { handle.Dispose(); throw; }
        }

        // Looping, state speed and time offsets belong to the controller clock.
        // This API requires an explicitly chosen time within the authored clip.
        public void SampleTransforms(float time, float[] destination)
        {
            if (float.IsNaN(time) || float.IsInfinity(time) || time < 0 || time > Duration)
                throw new ArgumentOutOfRangeException(nameof(time));
            if (destination == null || destination.Length != checked(TransformCount * 10))
                throw new ArgumentException("Transform sample buffer dimensions", nameof(destination));
            lock (gate)
            {
                if (handle.IsClosed) throw new ObjectDisposedException(nameof(NativeMotionCodec));
                int status = Sample(handle, time, destination, (uint)destination.Length);
                if (status != 0) throw new InvalidDataException("ACL sample failed: " + status);
            }
        }

        // Same chosen clip time as transforms; source constant/default channels
        // bypass interpolation in the codec. Root/motion scalars are returned
        // unchanged for the motor stage, not applied as a second displacement.
        public void SampleScalars(float time, float[] destination)
        {
            if (float.IsNaN(time) || float.IsInfinity(time) || time < 0 || time > Duration)
                throw new ArgumentOutOfRangeException(nameof(time));
            if (destination == null || destination.Length != ScalarCount)
                throw new ArgumentException("Scalar sample buffer dimensions", nameof(destination));
            lock (gate)
            {
                if (handle.IsClosed) throw new ObjectDisposedException(nameof(NativeMotionCodec));
                if (ScalarCount == 0) return;
                int status = Sample(scalarHandle, time, destination, (uint)destination.Length);
                if (status != 0) throw new InvalidDataException("Scalar ACL sample failed: " + status);
            }
        }

        public void Dispose()
        {
            lock (gate) { scalarHandle?.Dispose(); handle.Dispose(); }
        }

        sealed class CodecHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            public CodecHandle(IntPtr value) : base(true) { SetHandle(value); }
            protected override bool ReleaseHandle() { Destroy(handle); return true; }
        }

        [DllImport("RemielleAclCodec", EntryPoint = "RemielleAclCreate", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr Create([In] byte[] transforms, uint transformBytes, [In] byte[] database, uint databaseBytes,
            [In] byte[] bulk, uint bulkBytes, out int status);
        [DllImport("RemielleAclCodec", EntryPoint = "RemielleAclSample", CallingConvention = CallingConvention.Cdecl)]
        static extern int Sample(CodecHandle handle, float time, [Out] float[] output, uint floats);
        [DllImport("RemielleAclCodec", EntryPoint = "RemielleAclDestroy", CallingConvention = CallingConvention.Cdecl)]
        static extern void Destroy(IntPtr handle);
    }
}
