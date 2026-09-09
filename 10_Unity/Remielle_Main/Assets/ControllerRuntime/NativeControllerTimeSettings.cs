using System;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;

namespace Remielle.ControllerRuntime
{
    // Qualified TimeManager class 5, pathID 8, from the original globalgamemanagers.
    // Does not read installed game files or change Unity's global Time settings.
    public sealed class NativeControllerTimeSettings
    {
        public float FixedTimestep { get; }
        public float MaximumAllowedTimestep { get; }
        public float TimeScale { get; }
        public float MaximumParticleTimestep { get; }
        public bool EvaluateAgainWhenAnimatorEndTransition { get; }

        public NativeControllerTimeSettings(string json)
        {
            var data = JObject.Parse(json);
            var source = data["source"];
            if ((string)data["schema"] != "remielle-controller-time-settings-v1" ||
                (string)source["cab"] != "globalgamemanagers" || (string)source["pathID"] != "8" ||
                (int)source["classID"] != 5 || (string)source["sha256"] !=
                    "f0d6acc5158082dcb5662ff0c3d5d45c292fd4c0bd52b028f78a64640c804405")
                throw new ArgumentException("Unqualified original TimeManager identity");
            string hex = (string)data["objectBytesHex"];
            if (hex == null || hex.Length != 40) throw new ArgumentException("Expected the original 20-byte TimeManager");
            byte[] raw = new byte[20];
            for (int i = 0; i < raw.Length; i++) raw[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            using (var sha = SHA256.Create())
            {
                string actual = BitConverter.ToString(sha.ComputeHash(raw)).Replace("-", "").ToLowerInvariant();
                if (actual != (string)source["objectSha256"] || actual !=
                    "258088d1aceea7f2776a8acaa5ff1bacfe9bcbcd12a722af38418c6817a064ac")
                    throw new ArgumentException("Original TimeManager bytes changed");
            }
            if (!BitConverter.IsLittleEndian) throw new PlatformNotSupportedException("Original TimeManager is little endian");
            FixedTimestep = BitConverter.ToSingle(raw, 0);
            MaximumAllowedTimestep = BitConverter.ToSingle(raw, 4);
            TimeScale = BitConverter.ToSingle(raw, 8);
            MaximumParticleTimestep = BitConverter.ToSingle(raw, 12);
            EvaluateAgainWhenAnimatorEndTransition = raw[16] != 0;
        }

        // The caller supplies the prior end-transition marker. Its scheduling
        // lifecycle belongs to the state executor, not to this settings object.
        public NativeTransitionTimingPolicy TransitionPolicy(bool priorEndTransitionMarker) =>
            new NativeTransitionTimingPolicy(EvaluateAgainWhenAnimatorEndTransition, priorEndTransitionMarker);
    }
}
