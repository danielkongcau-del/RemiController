using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class RemielleSourceMipGpuAudit
{
    const string Review="E:/ZZZ/local-only/RemielleRenderingReview/20260905/";
    const string Output="E:/ZZZ/local-only/RemielleDataAcquisition/20260904/source-completion-plan/";
    public static void Run()
    {
        if(SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Direct3D11)throw new Exception("D3D11 required");
        RemielleNativeTextureImportAudit.Run();
        var shader=AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Editor/RemielleSourceMipGpuAudit.compute");
        if(!shader)throw new Exception("Missing mip audit compute shader");
        var staging=JObject.Parse(File.ReadAllText(Review+"native-material-texture-assets.json"));
        var sources=JObject.Parse(File.ReadAllText(Output+"source-completion-verification.json"));
        var selected=new HashSet<string>();
        foreach(JObject r in (JArray)sources["resources"])if((string)r["status"]=="exact-source-chain-recovered")selected.Add((string)r["payloadSha256"]);
        var result=new JArray();int sampleCount=0,mipCount=0;
        Directory.CreateDirectory(Output+"unity-mip-raw");
        foreach(JObject item in (JArray)staging["textures"])
        {
            string digest=(string)item["payloadSha256"];
            if(!selected.Contains(digest))continue;
            string asset=(string)item["asset"];
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(asset);
            if(!texture)throw new Exception("Missing texture "+asset);
            byte[] dds=File.ReadAllBytes((string)item["absoluteRawAsset"]);
            int header=dds[84]=='D'&&dds[85]=='X'&&dds[86]=='1'&&dds[87]=='0'?148:128;
            byte[] cpu=texture.GetRawTextureData();
            if(cpu.Length!=dds.Length-header)throw new Exception("Raw chain length mismatch "+asset);
            for(int i=0;i<cpu.Length;i++)if(cpu[i]!=dds[header+i])throw new Exception("Raw chain byte mismatch "+asset);
            var mips=new JArray();
            using var rawReadback=new BinaryWriter(File.Create(Output+"unity-mip-raw/"+digest+".f32"));
            for(int mip=0;mip<texture.mipmapCount;mip++)
            {
                int w=Math.Max(1,texture.width>>mip),h=Math.Max(1,texture.height>>mip);
                var coordinates=new List<Vector2Int>();
                for(int y=0;y<Math.Min(17,h);y++)for(int x=0;x<Math.Min(17,w);x++)
                    coordinates.Add(new Vector2Int(x*(w-1)/Math.Max(1,Math.Min(17,w)-1),y*(h-1)/Math.Max(1,Math.Min(17,h)-1)));
                var values=new Vector4[coordinates.Count];
                using(var positions=new ComputeBuffer(coordinates.Count,8))
                using(var samples=new ComputeBuffer(coordinates.Count,16))
                {
                    positions.SetData(coordinates);shader.SetTexture(0,"Source",texture);
                    shader.SetBuffer(0,"Coordinates",positions);shader.SetBuffer(0,"Samples",samples);
                    shader.SetInt("Mip",mip);shader.SetInt("Count",coordinates.Count);
                    shader.Dispatch(0,(coordinates.Count+63)/64,1,1);samples.GetData(values);
                }
                var entries=new JArray();
                foreach(var value in values){rawReadback.Write(value.x);rawReadback.Write(value.y);rawReadback.Write(value.z);rawReadback.Write(value.w);}
                for(int i=0;i<values.Length;i++)entries.Add(new JObject{["x"]=coordinates[i].x,["y"]=coordinates[i].y,
                    ["rgba"]=new JArray(values[i].x,values[i].y,values[i].z,values[i].w)});
                mips.Add(new JObject{["mip"]=mip,["width"]=w,["height"]=h,["samples"]=entries});
                sampleCount+=values.Length;mipCount++;
            }
            result.Add(new JObject{["payloadSha256"]=digest,["asset"]=asset,["graphicsFormat"]=texture.graphicsFormat.ToString(),
                ["rawBytesEqual"]=true,["mips"]=mips});
        }
        File.WriteAllText(Output+"unity-source-mip-readback.json",new JObject{["schema"]="remielle-source-mip-gpu-readback-v1",
            ["graphicsDevice"]=SystemInfo.graphicsDeviceName,["graphicsApi"]=SystemInfo.graphicsDeviceType.ToString(),
            ["textures"]=result,["mipCount"]=mipCount,["sampleCount"]=sampleCount,["boundary"]="Sampler-independent Texture.Load readback; CPU reference comparison is performed separately."}.ToString());
        Debug.Log("REMIELLE_SOURCE_MIP_READBACK_COMPLETE textures="+result.Count+" mips="+mipCount+" samples="+sampleCount);
    }
}
