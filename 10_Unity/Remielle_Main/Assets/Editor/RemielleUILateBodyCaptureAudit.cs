using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

public static class RemielleUILateBodyCaptureAudit
{
    public static void Run()
    {
        string root=RemielleUILateBodyBuild.Root;Directory.CreateDirectory(root+"unity");
        var m=JObject.Parse(File.ReadAllText(root+"forward-inputs.json"));RemielleUILateBodyBuild.CheckRefs(m);var rows=new JArray();
        foreach(var row in m["cases"])
        {
            string id=(string)row["id"],profile=(string)row["profile"];const int w=2448,h=1368;
            var p=AssetDatabase.LoadAssetAtPath<RemielleNativeUILateBodyProfile>(RemielleUILateBodyBuild.Assets+profile+".asset");
            var colorInput=new Texture2D(w,h,TextureFormat.RGBAHalf,false,true);colorInput.LoadRawTextureData(RemielleUINativePostBuild.ReadRef(row["initial"]["color"]["payload"]));colorInput.Apply(false,false);
            var originalDepth=RemielleUINativePostBuild.ReadRef(row["initial"]["depth"]["payload"]);var depthBytes=new byte[w*h*4];for(int i=0;i<w*h;i++)Buffer.BlockCopy(originalDepth,i*8,depthBytes,i*4,4);
            var depthInput=new Texture2D(w,h,TextureFormat.RFloat,false,true);depthInput.LoadRawTextureData(depthBytes);depthInput.Apply(false,false);
            var color=new RenderTexture(new RenderTextureDescriptor(w,h,GraphicsFormat.R16G16B16A16_SFloat,GraphicsFormat.None));color.Create();
            var depth=new RenderTexture(new RenderTextureDescriptor(w,h){graphicsFormat=GraphicsFormat.None,depthStencilFormat=GraphicsFormat.D32_SFloat_S8_UInt,stencilFormat=GraphicsFormat.R8_UInt,msaaSamples=1});depth.Create();
            var depthRead=new RenderTexture(new RenderTextureDescriptor(w,h,GraphicsFormat.R32G32B32A32_SFloat,GraphicsFormat.None));depthRead.Create();
            var init=new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/RenderingReview/Shader/GeneratedNativeUILateBody/NativeUILateDepthInit.shader"));init.SetTexture("_InitialDepth",depthInput);
            var reader=new Material(p.geometryProfile.depthReaderShader);reader.SetTexture("_SeqDepth",depth,RenderTextureSubElement.Depth);reader.SetTexture("_SeqStencil",depth,RenderTextureSubElement.Stencil);
            var cmd=new CommandBuffer();bool srgb=GL.sRGBWrite;
            try
            {
                GL.sRGBWrite=false;cmd.SetRenderTarget(new RenderTargetIdentifier(color),new RenderTargetIdentifier(depth));cmd.ClearRenderTarget(RTClearFlags.All,Color.clear,1,0);cmd.SetViewport(new Rect(0,0,w,h));
                cmd.DrawProcedural(Matrix4x4.identity,init,0,MeshTopology.Triangles,3);Graphics.ExecuteCommandBuffer(cmd);Graphics.CopyTexture(colorInput,color);
                if(!RemielleUINativePostAudit.Read(color,w*h*8).SequenceEqual(RemielleUINativePostBuild.ReadRef(row["initial"]["color"]["payload"])))throw new Exception("Captured HDR initialization changed bits");
                byte[] ReadDepth(){cmd.Clear();cmd.SetRenderTarget(depthRead);cmd.SetViewport(new Rect(0,0,w,h));cmd.DrawProcedural(Matrix4x4.identity,reader,0,MeshTopology.Triangles,3);Graphics.ExecuteCommandBuffer(cmd);return RemielleUINativePostAudit.Read(depthRead,w*h*16);}
                var before=ReadDepth();for(int i=0;i<w*h;i++)for(int k=0;k<4;k++)if(before[i*16+k]!=depthBytes[i*4+k])throw new Exception("Captured depth upload changed bits");
                using(var late=new RemielleNativeUILateBody(p))late.Draw(color,depth);
                var after=ReadDepth();if(!before.SequenceEqual(after))throw new Exception("Late forward draw changed depth/stencil");
                string output=root+"unity/"+id+".raw";File.WriteAllBytes(output,RemielleUINativePostAudit.Read(color,w*h*8));
                rows.Add(new JObject{["id"]=id,["output"]=RemielleUINativePostBuild.Ref(output),["profile"]=RemielleUINativePostBuild.Ref(RemielleUILateBodyBuild.Assets+profile+".asset"),["initialColorExact"]=true,["initialDepthExact"]=true,["depthStencilPreserved"]=true,["initialStencil"]=0,["stencilNotConsumed"]=true});
                foreach(var message in ShaderUtil.GetShaderMessages(p.shader))if(message.severity.ToString()=="Error")throw new Exception(message.message);
            }
            finally
            {
                GL.sRGBWrite=srgb;cmd.Release();RenderTexture.active=null;foreach(var rt in new[]{color,depth,depthRead}){rt.Release();Object.DestroyImmediate(rt);}
                foreach(var o in new Object[]{colorInput,depthInput,init,reader})Object.DestroyImmediate(o);
            }
        }
        var files=new JArray();foreach(string name in new[]{"Editor/RemielleUILateBodyBuild.cs","Editor/RemielleUILateBodyCaptureAudit.cs","RenderingReview/Runtime/RemielleNativeUILateBody.cs","RenderingReview/Runtime/RemielleNativeUILateBodyProfile.cs"})files.Add(RemielleUINativePostBuild.Ref("Assets/"+name));
        File.WriteAllText(root+"unity.json",new JObject{["schema"]="remielle-ui-late-body-unity-capture-v1",["cases"]=rows,["inputs"]=RemielleUINativePostBuild.Ref(root+"forward-inputs.json"),["shader"]=RemielleUINativePostBuild.Ref(root+"unity-shader.json"),["implementation"]=files}.ToString());
        Debug.Log("REMIELLE_UI_LATE_BODY_UNITY_CAPTURE_READBACK 2");
    }
}
