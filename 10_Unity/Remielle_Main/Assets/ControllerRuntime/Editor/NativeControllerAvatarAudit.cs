using System;
using System.IO;
using Newtonsoft.Json.Linq;
using Remielle.ControllerRuntime;
using UnityEngine;

public static class NativeControllerAvatarAudit
{
    const string Out = "E:/ZZZ/local-only/RemielleControllerImplementation/20260906/motion-bindings";

    public static void Run()
    {
        var result = new JObject { ["schema"] = "remielle-controller-avatar-unity-v1", ["pass"] = false };
        try
        {
            string bank = Path.Combine(Application.streamingAssetsPath, "RemielleControllerMotions");
            var bindings = new NativeControllerAvatarBindings(File.ReadAllText(Path.Combine(bank, "controller-avatar-bindings.json")),
                File.ReadAllText(Path.Combine(bank, "binding-profiles.json")));
            var source = new NativeControllerSource(File.ReadAllText(Path.Combine(Application.dataPath, "ControllerRuntime/Data/source-controller-pack.json")));
            var expected = JObject.Parse(File.ReadAllText(Out + "/controller-avatar-verification.json"))["controllerProfiles"];
            int controllers = 0, rejected = 0;
            foreach (string name in source.Names)
            {
                var controller = source.GetController(name);
                var profile = bindings.Resolve(controller);
                if ((string)profile["name"] != (string)expected[name]) throw new InvalidDataException("Wrong original Avatar profile");
                foreach (string field in new[] { "sourceBlock", "cab", "pathID", "blockSha256AtAcquisition" })
                {
                    var changed = (JObject)controller.DeepClone();
                    changed["identity"][field] = "different-source-identity";
                    bool failed = false;
                    try { bindings.Resolve(changed); } catch (InvalidDataException) { failed = true; }
                    if (!failed) throw new InvalidDataException("Mismatched controller identity accepted");
                    rejected++;
                }
                controllers++;
            }
            result["pass"] = true; result["controllers"] = controllers; result["mismatchedIdentitiesRejected"] = rejected;
            result["completeNativeBindingPolicyVerified"] = false;
            Debug.Log("CONTROLLER_AVATARS_UNITY_OK " + controllers);
        }
        catch (Exception e) { result["error"] = e.ToString(); Debug.LogException(e); throw; }
        finally { File.WriteAllText(Out + "/unity-controller-avatar-verification.json", result.ToString()); }
    }
}
