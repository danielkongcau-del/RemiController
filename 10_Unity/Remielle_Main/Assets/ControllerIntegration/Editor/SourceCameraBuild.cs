using System;
using System.IO;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Remielle.Controller.Editor
{
    public static class SourceCameraBuild
    {
        public const string Pack="Assets/ControllerIntegration/Data/source-action-camera.json";
        public static TextAsset Prepare(string path=Pack)
        {
            var p=JObject.Parse(File.ReadAllText(path));
            foreach(var e in p["evidence"])
            {
                using var stream=File.OpenRead((string)e["path"]);using var h=SHA256.Create();
                if(BitConverter.ToString(h.ComputeHash(stream)).Replace("-","").ToLowerInvariant()!=(string)e["sha256"])
                    throw new Exception("Camera source changed: "+e["path"]);
            }
            AssetDatabase.ImportAsset(path);return AssetDatabase.LoadAssetAtPath<TextAsset>(path);
        }
    }
}
