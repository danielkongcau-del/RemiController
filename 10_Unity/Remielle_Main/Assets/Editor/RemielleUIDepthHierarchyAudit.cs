using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

public static class RemielleUIDepthHierarchyAudit
{
    public const string Root="E:/ZZZ/local-only/RemielleRenderingReview/20260905/ui-live-binding/depth-hierarchy/";
    public const string ShaderPath="Assets/RenderingReview/Shader/GeneratedNativeUIReplay/NativeUIDepthHierarchy.shader";
    static JObject Ref(string path)=>RemielleUINativePostBuild.Ref(path);
    static Texture Load(JToken row)
    {
        int w=(int)row["width"],h=(int)row["height"];var raw=RemielleUINativePostBuild.ReadRef(row["raw"]);
        if((int)row["format"]==41){var t=new Texture2D(w,h,TextureFormat.RFloat,false,true);t.LoadRawTextureData(raw);t.Apply(false,false);return t;}
        // Unity cannot create a sampled Texture2D with R10 on this backend.
        // Upload exact normalized values, then quantize once to native R10;
        // the packed GPU readback must equal every original source word.
        var values=new float[w*h*4];for(int i=0;i<w*h;i++){uint v=BitConverter.ToUInt32(raw,i*4);values[i*4]=(v&1023)/1023f;values[i*4+1]=((v>>10)&1023)/1023f;values[i*4+2]=((v>>20)&1023)/1023f;values[i*4+3]=(v>>30)/3f;}
        var upload=new byte[values.Length*4];Buffer.BlockCopy(values,0,upload,0,upload.Length);
        var source=new Texture2D(w,h,TextureFormat.RGBAFloat,false,true);source.LoadRawTextureData(upload);source.Apply(false,false);
        var target=new RenderTexture(new RenderTextureDescriptor(w,h,GraphicsFormat.A2B10G10R10_UNormPack32,GraphicsFormat.None));target.Create();
        bool srgb=GL.sRGBWrite;
        try{GL.sRGBWrite=false;Graphics.Blit(source,target);if(!raw.SequenceEqual(RemielleUINativePostAudit.Read(target,w*h*4)))throw new Exception("Native R10 upload changed source bits");}
        finally{GL.sRGBWrite=srgb;Object.DestroyImmediate(source);}
        return target;
    }
    static byte[] ReadMip(RenderTexture target,int mip)
    {
        var request=AsyncGPUReadback.Request(target,mip);request.WaitForCompletion();if(request.hasError)throw new Exception("Depth hierarchy mip readback failed");return request.GetData<byte>().ToArray();
    }
    public static void Run()
    {
        Directory.CreateDirectory(Root+"unity");var manifest=JObject.Parse(File.ReadAllText(Root+"manifest.json"));RemielleUILateBodyBuild.CheckRefs(manifest);
        var shader=AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);var rows=new JArray();
        var profile=AssetDatabase.LoadAssetAtPath<RemielleNativeUIProfile>(RemielleUILiveProfileBuild.Assets+"display.asset");
        foreach(var row in manifest["cases"])
        {
            string name=(string)row["id"];var depth=Load(row["inputs"][0]);var normal=Load(row["inputs"][1]);
            var bytes=RemielleUINativePostBuild.ReadRef(row["constants"]);var floats=new float[bytes.Length/4];Buffer.BlockCopy(bytes,0,floats,0,bytes.Length);
            var constants=RemielleReviewBloom.Vectors(floats,168);using var hierarchy=new RemielleNativeUIDepthHierarchy(shader);
            try
            {
                hierarchy.Render(depth,normal,constants);int w=hierarchy.Width,h=hierarchy.Height;
                var outputs=new JArray();var textures=new[]{hierarchy.LinearDepth,hierarchy.NormalAndSample,hierarchy.Range};
                for(int i=0;i<3;i++){string path=Root+"unity/"+name+"-o"+i+".raw";File.WriteAllBytes(path,RemielleUINativePostAudit.Read(textures[i],w*h*4));outputs.Add(Ref(path));}
                string mip=Root+"unity/"+name+"-range-mip1.raw";File.WriteAllBytes(mip,ReadMip(hierarchy.Range,1));
                var reader=new Material(profile.depthReaderShader);var rt=new RenderTexture(new RenderTextureDescriptor(w,h,GraphicsFormat.R32G32B32A32_SFloat,GraphicsFormat.None));rt.Create();var cmd=new CommandBuffer();
                string dep=Root+"unity/"+name+"-depth-stencil.rgba32f";
                try{reader.SetTexture("_SeqDepth",hierarchy.Depth,RenderTextureSubElement.Depth);reader.SetTexture("_SeqStencil",hierarchy.Depth,RenderTextureSubElement.Stencil);cmd.SetRenderTarget(rt);cmd.SetViewport(new Rect(0,0,w,h));cmd.DrawProcedural(Matrix4x4.identity,reader,0,MeshTopology.Triangles,3);Graphics.ExecuteCommandBuffer(cmd);File.WriteAllBytes(dep,RemielleUINativePostAudit.Read(rt,w*h*16));}
                finally{cmd.Release();RenderTexture.active=null;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(reader);}
                rows.Add(new JObject{["id"]=name,["width"]=w,["height"]=h,["outputs"]=outputs,["depthStencil"]=Ref(dep),["rangeMip1"]=Ref(mip)});
                foreach(var message in ShaderUtil.GetShaderMessages(shader))if(message.severity.ToString()=="Error")throw new Exception(message.message);
            }
            finally{RenderTexture.active=null;if(depth is RenderTexture a)a.Release();if(normal is RenderTexture b)b.Release();Object.DestroyImmediate(depth);Object.DestroyImmediate(normal);}
        }
        var files=new JArray();foreach(string path in new[]{ShaderPath,"Assets/RenderingReview/Runtime/RemielleNativeUIDepthHierarchy.cs","Assets/Editor/RemielleUIDepthHierarchyAudit.cs"})files.Add(Ref(path));
        File.WriteAllText(Root+"unity.json",new JObject{["schema"]="remielle-ui-depth-hierarchy-unity-capture-v1",["cases"]=rows,["source"]=Ref(Root+"manifest.json"),["implementation"]=files}.ToString());Debug.Log("REMIELLE_UI_DEPTH_HIERARCHY_CAPTURE_READBACK 2");
    }
}
