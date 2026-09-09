using System;
using System.IO;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Remielle.Controller.Editor
{
    public static class SourceTrailBuild
    {
        public const string Pack="Assets/ControllerIntegration/Data/source-weapon-trail.json";
        public const string MaterialPath="Assets/ControllerIntegration/Effects/Source_Common24_Trail.mat";
        public static Material Prepare()
        {
            var pack=JObject.Parse(File.ReadAllText(Pack));
            foreach(var e in pack["evidence"])
            {
                using var stream=File.OpenRead((string)e["path"]);using var hash=SHA256.Create();
                if(BitConverter.ToString(hash.ComputeHash(stream)).Replace("-","").ToLowerInvariant()!=(string)e["sha256"])throw new Exception("Trail source changed: "+e["path"]);
            }
            var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/ControllerIntegration/Effects/SourceTrailPreview.shader");
            if(!shader||ShaderUtil.ShaderHasError(shader))throw new Exception("Trail preview shader unavailable");
            var material=new Material(shader);var original=pack["material"]["m_SavedProperties"];
            foreach(var pair in (JObject)pack["textures"])
            {
                var row=pair.Value;string path=(string)row["asset"];AssetDatabase.ImportAsset(path);
                var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.textureType=TextureImporterType.Default;
                importer.sRGBTexture=pair.Key=="_MainTex";importer.textureCompression=TextureImporterCompression.Uncompressed;importer.mipmapEnabled=false;
                importer.wrapMode=pair.Key=="_Mask"?TextureWrapMode.Clamp:TextureWrapMode.Repeat;importer.filterMode=FilterMode.Bilinear;importer.SaveAndReimport();
                material.SetTexture(pair.Key,AssetDatabase.LoadAssetAtPath<Texture2D>(path));
                material.SetTextureScale(pair.Key,new Vector2((float)row["scale"]["X"],(float)row["scale"]["Y"]));
                material.SetTextureOffset(pair.Key,new Vector2((float)row["offset"]["X"],(float)row["offset"]["Y"]));
            }
            var tint=original["m_Colors"]["_TintColor"];material.SetColor("_TintColor",new Color((float)tint["r"],(float)tint["g"],(float)tint["b"],(float)tint["a"]));
            material.SetVector("_Scroll",new Vector4((float)original["m_Floats"]["_USpeed"],(float)original["m_Floats"]["_VSpeed"],0,0));
            var old=AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if(old){EditorUtility.CopySerialized(material,old);UnityEngine.Object.DestroyImmediate(material);EditorUtility.SetDirty(old);material=old;}
            else AssetDatabase.CreateAsset(material,MaterialPath);
            AssetDatabase.SaveAssets();return material;
        }
    }
}
