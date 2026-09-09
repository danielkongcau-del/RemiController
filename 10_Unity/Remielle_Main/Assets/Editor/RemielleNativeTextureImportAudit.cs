using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class RemielleNativeTextureImportAudit
{
    const string Source="E:/ZZZ/local-only/RemielleRenderingReview/20260905/native-material-texture-assets.json";
    const string Output="E:/ZZZ/ZCode/90_Builds/RenderingReview/20260905/native-material-texture-import.json";

    public static void Run()
    {
        var source=JObject.Parse(File.ReadAllText(Source));
        if((string)source["schema"]!="remielle-native-material-texture-assets-v3"||(bool)source["pass"]!=true)throw new Exception("Native texture asset manifest is invalid");
        var rows=new JArray();int passed=0;
        foreach(JObject item in (JArray)source["textures"])
        {
            string path=(string)item["asset"];
            string rawPath=(string)item["rawAsset"];
            var desc=(JObject)item["descriptor"];
            bool expectedSrgb=(bool)item["expectedSrgb"];
            int stagedMips=(int)item["stagedMipLevels"],stagedLayers=(int)item["stagedArrayLayers"];
            int capturedMips=(int)item["capturedMipLevels"],capturedLayers=(int)item["capturedArrayLayers"];
            BuildTextureAsset(rawPath,path,(int)desc["width"],(int)desc["height"],stagedMips,(int)desc["array"],stagedLayers,(string)desc["format"],!expectedSrgb);
            var texture=AssetDatabase.LoadAssetAtPath<Texture>(path);
            if(!texture)throw new Exception("Captured DDS texture asset is null: "+path);
            int expectedWidth=(int)desc["width"],expectedHeight=(int)desc["height"],sourceMips=(int)desc["mips"],expectedMips=stagedMips,expectedArray=(int)desc["array"];
            bool dimensionPass=texture.dimension==(expectedArray>1?TextureDimension.Tex2DArray:TextureDimension.Tex2D);
            bool shapePass=expectedArray>1?texture is Texture2DArray:texture is Texture2D;
            bool arrayPass=expectedArray>1&&texture is Texture2DArray?((Texture2DArray)texture).depth==expectedArray:expectedArray==1;
            int actualMips=texture.mipmapCount;
            string graphicsFormat=texture.graphicsFormat.ToString();
            bool srgbPass=graphicsFormat.Contains("SRGB")==expectedSrgb;
            bool formatPass=FormatMatches((string)desc["format"],graphicsFormat);
            bool rowPass=texture.width==expectedWidth&&texture.height==expectedHeight&&actualMips==expectedMips&&dimensionPass&&shapePass&&arrayPass&&srgbPass&&formatPass;
            var row=new JObject{
                ["asset"]=path,["sourceFormat"]=(string)desc["format"],["sourceArray"]=expectedArray,["sourceMips"]=sourceMips,["capturedArrayLayers"]=capturedLayers,["capturedMips"]=capturedMips,["capturedSubresourceCoverage"]=(string)item["capturedSubresourceCoverage"],["stagedArrayLayers"]=stagedLayers,["stagedMips"]=expectedMips,["stagedSubresourceCoverage"]=(string)item["stagedSubresourceCoverage"],
                ["type"]=texture.GetType().Name,["dimension"]=texture.dimension.ToString(),["width"]=texture.width,["height"]=texture.height,["mips"]=actualMips,
                ["arrayLayers"]=texture is Texture2DArray?((Texture2DArray)texture).depth:1,["graphicsFormat"]=graphicsFormat,["expectedSrgb"]=expectedSrgb,["pass"]=rowPass
            };
            rows.Add(row);if(rowPass)passed++;else throw new Exception("Captured DDS import shape mismatch: "+row);
        }
        AssetDatabase.SaveAssets();
        var report=new JObject{
            ["schema"]="remielle-native-material-texture-import-v3",["pass"]=passed==36,["textures"]=rows,
            ["summary"]=new JObject{["checked"]=rows.Count,["passed"]=passed,["texture2D"]=Count(rows,"Tex2D"),["texture2DArray"]=Count(rows,"Tex2DArray"),["capturedBytes"]=(long)source["summary"]["capturedBytes"],["stagedBytes"]=(long)source["summary"]["stagedBytes"],["completeResources"]=(int)source["summary"]["stagedCompleteSourceResources"],["mip0OnlyResources"]=(int)source["summary"]["stagedMip0OnlyResources"]},
            ["boundary"]="All staged mip0 bytes match the capture. Complete source streams are joined by exact bytes and preserve source identities. Remaining mip0-only resources are counted in summary; TextureSettings do not establish live sampler descriptors."
        };
        File.WriteAllText(Output,report.ToString());
        if(!(bool)report["pass"])throw new Exception("Native texture import audit failed");
        Debug.Log("REMIELLE_NATIVE_TEXTURE_IMPORT_PASS "+passed);
    }

    static void BuildTextureAsset(string rawPath,string assetPath,int width,int height,int mips,int layers,int capturedLayers,string sourceFormat,bool linear)
    {
        byte[] dds=File.ReadAllBytes(rawPath);
        if(dds.Length<148||dds[0]!='D'||dds[1]!='D'||dds[2]!='S'||dds[3]!=' ')throw new Exception("Invalid captured DDS bytes: "+rawPath);
        int header=dds[84]=='D'&&dds[85]=='X'&&dds[86]=='1'&&dds[87]=='0'?148:128;
        var raw=new byte[dds.Length-header];Buffer.BlockCopy(dds,header,raw,0,raw.Length);
        TextureFormat format=TextureFormatFor(sourceFormat);
        int expectedBytes=0;
        for(int layer=0;layer<capturedLayers;layer++)for(int mip=0;mip<mips;mip++)expectedBytes+=MipBytes(format,Math.Max(1,width>>mip),Math.Max(1,height>>mip));
        if(raw.Length!=expectedBytes)throw new Exception("Captured DDS payload size mismatch: "+rawPath+" expected="+expectedBytes+" actual="+raw.Length);
        if(layers==1)
        {
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);bool created=!texture;
            bool mipChain=mips>1;
            if(created)texture=new Texture2D(width,height,format,mipChain,linear){name=Path.GetFileNameWithoutExtension(assetPath)};
            else if(!texture.Reinitialize(width,height,format,mipChain))throw new Exception("Could not reinitialize captured texture: "+assetPath);
            if(texture.mipmapCount!=mips)throw new Exception("Constructed texture mip count mismatch: "+assetPath);
            texture.LoadRawTextureData(raw);texture.Apply(false,false);
            if(created)AssetDatabase.CreateAsset(texture,assetPath);else EditorUtility.SetDirty(texture);
        }
        else
        {
            var texture=AssetDatabase.LoadAssetAtPath<Texture2DArray>(assetPath);bool created=!texture;
            if(!created&&(texture.width!=width||texture.height!=height||texture.depth!=layers||texture.format!=format||texture.mipmapCount!=mips))
            {
                AssetDatabase.DeleteAsset(assetPath);texture=null;created=true;
            }
            // The bool mipChain overload always allocates the full chain (9 levels at 256 px).
            // The captured runtime descriptor is an exact five-level array, so use the explicit
            // mip-count overload and preserve that layout instead of inventing four tail levels.
            if(created)texture=new Texture2DArray(width,height,layers,format,mips,linear){name=Path.GetFileNameWithoutExtension(assetPath)};
            int offset=0;
            for(int layer=0;layer<capturedLayers;layer++)for(int mip=0;mip<mips;mip++)
            {
                texture.SetPixelData(raw,mip,layer,offset);
                offset+=MipBytes(format,Math.Max(1,width>>mip),Math.Max(1,height>>mip));
            }
            texture.Apply(false,false);
            if(created)AssetDatabase.CreateAsset(texture,assetPath);else EditorUtility.SetDirty(texture);
        }
    }

    static TextureFormat TextureFormatFor(string source)
    {
        if(source.StartsWith("BC1_"))return TextureFormat.DXT1;
        if(source.StartsWith("BC6H_"))return TextureFormat.BC6H;
        if(source.StartsWith("BC7_"))return TextureFormat.BC7;
        if(source.StartsWith("R8G8B8A8_"))return TextureFormat.RGBA32;
        if(source=="R16G16B16A16_FLOAT")return TextureFormat.RGBAHalf;
        throw new Exception("Unsupported captured texture format: "+source);
    }

    static int MipBytes(TextureFormat format,int width,int height)
    {
        if(format==TextureFormat.DXT1)return Math.Max(1,(width+3)/4)*Math.Max(1,(height+3)/4)*8;
        if(format==TextureFormat.BC6H||format==TextureFormat.BC7)return Math.Max(1,(width+3)/4)*Math.Max(1,(height+3)/4)*16;
        if(format==TextureFormat.RGBA32)return width*height*4;
        if(format==TextureFormat.RGBAHalf)return width*height*8;
        throw new Exception("Unsupported mip format: "+format);
    }

    static bool FormatMatches(string source,string actual)
    {
        if(source.StartsWith("BC1_"))return actual.Contains("DXT1");
        if(source.StartsWith("BC6H_"))return actual.Contains("BC6H");
        if(source.StartsWith("BC7_"))return actual.Contains("BC7");
        if(source=="R8G8B8A8_UNORM")return actual=="R8G8B8A8_UNorm";
        if(source=="R8G8B8A8_UNORM_SRGB")return actual=="R8G8B8A8_SRGB";
        if(source=="R16G16B16A16_FLOAT")return actual=="R16G16B16A16_SFloat";
        return false;
    }

    static int Count(JArray rows,string dimension)
    {
        int count=0;foreach(JObject row in rows)if((string)row["dimension"]==dimension)count++;return count;
    }
}
