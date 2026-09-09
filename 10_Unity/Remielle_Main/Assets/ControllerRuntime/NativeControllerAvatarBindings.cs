using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Remielle.ControllerRuntime
{
    // Selects the original Avatar profile via qualified controller identity.
    // It does not turn outside-Avatar curves into a permissive fallback policy.
    public sealed class NativeControllerAvatarBindings
    {
        readonly Dictionary<string, JObject> entries, profiles;

        public NativeControllerAvatarBindings(string bindingJson, string profileJson)
        {
            var bindings = JObject.Parse(bindingJson);
            var profileRoot = JObject.Parse(profileJson);
            if ((string)bindings["schema"] != "remielle-controller-avatar-bindings-v1" ||
                (string)profileRoot["schema"] != "remielle-motion-binding-profiles-v1")
                throw new InvalidDataException("Controller Avatar binding schema");
            entries = bindings["controllers"].Cast<JObject>().ToDictionary(x => (string)x["name"], StringComparer.Ordinal);
            profiles = profileRoot["profiles"].Cast<JObject>().ToDictionary(x => (string)x["name"], StringComparer.Ordinal);
        }

        public JObject Resolve(JObject controller)
        {
            var entry = entries[(string)controller["name"]];
            var actual = controller["identity"];
            var expected = entry["controllerIdentity"];
            foreach (string field in new[] { "sourceBlock", "cab", "pathID", "blockSha256AtAcquisition" })
                if ((string)actual[field] != (string)expected[field])
                    throw new InvalidDataException("Controller identity differs from the Avatar binding: " + field);
            var profile = profiles[(string)entry["profile"]["name"]];
            if ((string)profile["avatar"]["sha256"] != (string)entry["profile"]["avatar"]["sha256"])
                throw new InvalidDataException("Avatar source hash differs");
            return (JObject)profile.DeepClone();
        }
    }
}
