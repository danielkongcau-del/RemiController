using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

// Native GPU resources are kept as linear binary textures. No PNG/8-bit
// intermediate, vertical flip, automatic mip generation or color conversion.
public static class RuntimeMaterialResources
{
    const string Source = "E:/ZZZ/local-only/RemielleDataAcquisition/20260904";
    const string Target = "Assets/SourceAssets/Runtime";
    public static void Build()
    {
        Directory.CreateDirectory(Target);
        var rows = (JArray)JObject.Parse(File.ReadAllText(Source+"/lut-payloads.json"))["rows"];
        var report = new JArray();
        foreach (var row in rows.GroupBy(x=>(string)x["payloadSha256"]).Select(x=>x.First()))
        {
            bool post=(string)row["role"]=="UberPost-LUT";
            string name=post?"RuntimePostBattle":((string)row["frame"]).EndsWith("144810")?"RuntimeCharacterMenu":"RuntimeCharacterBattle";
            byte[] raw=File.ReadAllBytes(Path.Combine(Source,(string)row["rawPayload"]));
            if(Hash(raw)!=(string)row["payloadSha256"])throw new InvalidDataException("LUT source hash: "+name);
            var format=post?TextureFormat.RGBA32:TextureFormat.RGBAHalf;
            string path=Target+"/"+name+".asset";
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if(texture==null)
            {
                texture=new Texture2D((int)row["width"],(int)row["height"],format,false,true){name=name};
                AssetDatabase.CreateAsset(texture,path);
            }
            if(texture.format!=format||texture.width!=(int)row["width"]||texture.height!=(int)row["height"])throw new InvalidDataException("LUT asset format: "+name);
            texture.LoadRawTextureData(raw);texture.wrapMode=TextureWrapMode.Clamp;texture.filterMode=FilterMode.Bilinear;texture.anisoLevel=0;texture.Apply(false,false);
            if(Hash(texture.GetRawTextureData())!=Hash(raw))throw new InvalidDataException("LUT bits changed: "+name);
            EditorUtility.SetDirty(texture);
            report.Add(new JObject{["asset"]=path,["sha256"]=Hash(raw),["format"]=format.ToString(),["parameters"]=row["parameters"].DeepClone(),["nativeBitsPreserved"]=true});
        }
        AssetDatabase.SaveAssets();
        File.WriteAllText(Target+"/resource_import_report.json",new JObject{["pass"]=true,["textures"]=report}.ToString());
    }
    public static void Apply(Material material)
    {
        if(!material.HasProperty("_EnableLUT"))return;
        if(material.GetFloat("_MaterialType")==3){material.SetFloat("_EnableLUT",0);EditorUtility.SetDirty(material);return;}
        var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(Target+"/RuntimeCharacterMenu.asset");
        if(texture==null)throw new InvalidDataException("Native character LUT missing");
        material.SetTexture("_Lut2DTex",texture);material.SetVector("_Lut2DTexParam",new Vector4(1f/1024,1f/32,31,1));material.SetFloat("_EnableLUT",1);
        EditorUtility.SetDirty(material);
    }
    static string Hash(byte[] bytes){using var hash=SHA256.Create();return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
}
