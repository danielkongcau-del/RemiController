using System;
using System.IO;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Remielle.Controller.Editor
{
    public static class SourceWeaponMaterialBuild
    {
        public const string Pack="Assets/ControllerIntegration/Data/source-weapon-material.json";
        public const string SurfaceShader="Assets/ControllerIntegration/Effects/SourceWeaponHoyoToon.shader";
        public static void VerifyFamilies()
        {
            foreach(string side in new[]{"L","B"})foreach(string kind in new[]{"material","trail"})
            {
                var p=JObject.Parse(File.ReadAllText("Assets/ControllerIntegration/Data/source-weapon-"+kind+"-"+side+".json"));
                foreach(var e in p["evidence"])
                {
                    using var stream=File.OpenRead((string)e["path"]);using var h=SHA256.Create();
                    if(BitConverter.ToString(h.ComputeHash(stream)).Replace("-","").ToLowerInvariant()!=(string)e["sha256"])throw new Exception("Weapon family source changed: "+e["path"]);
                }
            }
        }
        static void VerifyTexture(JToken row)
        {
            using var stream=File.OpenRead((string)row["asset"]);using var h=SHA256.Create();
            if(BitConverter.ToString(h.ComputeHash(stream)).Replace("-","").ToLowerInvariant()!=(string)row["sha256"])throw new Exception("Imported weapon texture differs from its retained source: "+row["asset"]);
        }
        public static Texture2D PrepareSurface()
        {
            var provenance=JObject.Parse(File.ReadAllText("Assets/ControllerIntegration/Effects/source-weapon-shader-provenance.json"));
            foreach(var e in System.Linq.Enumerable.Concat(provenance["evidence"],provenance["outputs"]))
            {
                using var stream=File.OpenRead((string)e["path"]);using var h=SHA256.Create();
                if(BitConverter.ToString(h.ComputeHash(stream)).Replace("-","").ToLowerInvariant()!=(string)e["sha256"])throw new Exception("Weapon shader input/output changed: "+e["path"]);
            }
            var shader=AssetDatabase.LoadAssetAtPath<Shader>(SurfaceShader);if(!shader||ShaderUtil.ShaderHasError(shader))throw new Exception("Weapon surface shader unavailable");
            var p=JObject.Parse(File.ReadAllText(Pack));string path=(string)p["outlineTexture"]["asset"];
            VerifyTexture(p["outlineTexture"]);
            AssetDatabase.ImportAsset(path);var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType=TextureImporterType.Default;importer.sRGBTexture=true;importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled=false;importer.wrapMode=TextureWrapMode.Repeat;importer.filterMode=FilterMode.Bilinear;importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        public static Texture2D Prepare()
        {
            var p=JObject.Parse(File.ReadAllText(Pack));
            foreach(var e in p["evidence"])
            {
                using var stream=File.OpenRead((string)e["path"]);using var h=SHA256.Create();
                if(BitConverter.ToString(h.ComputeHash(stream)).Replace("-","").ToLowerInvariant()!=(string)e["sha256"])throw new Exception("Weapon material source changed: "+e["path"]);
            }
            string path=(string)p["emissionTexture"]["asset"];
            VerifyTexture(p["emissionTexture"]);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            if(!importer||!importer.sRGBTexture||importer.textureCompression!=TextureImporterCompression.Uncompressed)throw new Exception("Prepare the verified source trail texture first");
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
