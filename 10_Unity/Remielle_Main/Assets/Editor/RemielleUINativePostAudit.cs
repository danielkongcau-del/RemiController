using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

public static class RemielleUINativePostAudit
{
    public static byte[] Read(Texture t,int bytes)
    {
        var data=new NativeArray<byte>(bytes,Allocator.Persistent,NativeArrayOptions.UninitializedMemory);
        try{var r=AsyncGPUReadback.RequestIntoNativeArray(ref data,t,0);r.WaitForCompletion();if(r.hasError)throw new Exception("Native UI post readback failed");return data.ToArray();}finally{data.Dispose();}
    }
    public static void Run()
    {
        string folder=RemielleUINativePostBuild.WriteRoot;Directory.CreateDirectory(folder+"unity");var manifest=JObject.Parse(File.ReadAllText(folder+"manifest.json"));var rows=new JArray();
        foreach(var row in manifest["cases"])
        {
            string id=(string)row["id"];int w=(int)row["width"],h=(int)row["height"];
            var profile=AssetDatabase.LoadAssetAtPath<RemielleNativeUIPostProfile>(RemielleUINativePostBuild.Assets+id+".asset");var material=new Material(profile.finalShader);
            var source=RemielleUINativePostBuild.Texture(row["inputs"].Single(i=>(int)i["slot"]==0));var bloomRow=row["inputs"].Single(i=>(int)i["slot"]==3);
            var expanded=RemielleUINativePostBuild.Texture(bloomRow);expanded.filterMode=FilterMode.Point;
            var bloom=new RenderTexture(new RenderTextureDescriptor(expanded.width,expanded.height,GraphicsFormat.B10G11R11_UFloatPack32,GraphicsFormat.None)){filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};bloom.Create();
            var target=new RenderTexture(new RenderTextureDescriptor(w,h,GraphicsFormat.R8G8B8A8_SRGB,GraphicsFormat.None));target.Create();var cmd=new CommandBuffer();
            try
            {
                // R11 and RGBA32F have different filtered sampling precision on
                // this GPU even when every texel value is identical. Restore
                // the packed render target, then verify every upload bit.
                Graphics.Blit(expanded,bloom);
                if(!Read(bloom,bloom.width*bloom.height*4).SequenceEqual(RemielleUINativePostBuild.ReadRef(bloomRow["raw"])))throw new Exception("Packed Bloom reconstruction differs from source");
                RemielleNativeUIPost.DrawFinal(material,profile,source,bloom,target,profile.pixel0,profile.pixel1,cmd);
                string path=folder+"unity/"+id+"-o0.raw";File.WriteAllBytes(path,Read(target,w*h*4));
                var bloomSource=RemielleUINativePostBuild.Texture(row["bloomSource"]);bloomSource.filterMode=FilterMode.Point;
                var bloomHdr=new RenderTexture(new RenderTextureDescriptor(bloomSource.width,bloomSource.height,GraphicsFormat.R16G16B16A16_SFloat,GraphicsFormat.None));bloomHdr.Create();
                string chainPath=folder+"unity/"+id+"-bloom.raw";
                try
                {
                    Graphics.Blit(bloomSource,bloomHdr);
                    if(!Read(bloomHdr,w*h*8).SequenceEqual(RemielleUINativePostBuild.ReadRef(row["bloomSource"]["raw"])))throw new Exception("Bloom source upload mismatch");
                    using var pipeline=new RemielleNativeUIPost(profile);pipeline.Render(bloomHdr);File.WriteAllBytes(chainPath,Read(pipeline.Bloom,pipeline.Bloom.width*pipeline.Bloom.height*4));
                }
                finally {if(RenderTexture.active==bloomHdr)RenderTexture.active=null;bloomHdr.Release();Object.DestroyImmediate(bloomHdr);Object.DestroyImmediate(bloomSource);}
                rows.Add(new JObject{["id"]=id,["output"]=RemielleUINativePostBuild.Ref(path),["bloomOutput"]=RemielleUINativePostBuild.Ref(chainPath),["profile"]=RemielleUINativePostBuild.Ref(RemielleUINativePostBuild.Assets+id+".asset")});
                foreach(var m in ShaderUtil.GetShaderMessages(profile.finalShader))if(m.severity.ToString()=="Error")throw new Exception(m.message);
            }
            finally{cmd.Release();if(RenderTexture.active==target||RenderTexture.active==bloom)RenderTexture.active=null;target.Release();bloom.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(source);Object.DestroyImmediate(bloom);Object.DestroyImmediate(expanded);Object.DestroyImmediate(material);}
        }
        var files=new JArray();foreach(string path in new[]{"Assets/Editor/RemielleUINativePostAudit.cs","Assets/RenderingReview/Runtime/RemielleNativeUIPost.cs","Assets/RenderingReview/Shader/GeneratedNativeUIReplay/NativeUIFinalPost.shader"})files.Add(RemielleUINativePostBuild.Ref(path));
        File.WriteAllText(folder+"unity.json",new JObject{["schema"]="remielle-native-ui-post-unity-readback-v1",["cases"]=rows,["manifest"]=RemielleUINativePostBuild.Ref(folder+"manifest.json"),["implementationFiles"]=files}.ToString());
        Debug.Log("REMIELLE_NATIVE_UI_POST_CAPTURE_READBACK "+rows.Count);
    }
}
