using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

public static class RemielleUILiveDepthHierarchyAudit
{
    const string Out="E:/ZZZ/local-only/RemielleRenderingReview/20260905/ui-live-binding/live-depth-hierarchy/";
    static JObject Ref(string p)=>RemielleUINativePostBuild.Ref(p);
    static int Targets()=>Resources.FindObjectsOfTypeAll<RenderTexture>().Count(t=>(t.hideFlags&HideFlags.HideAndDontSave)==HideFlags.HideAndDontSave);
    static JObject Write(string name,byte[] data){string p=Out+name;File.WriteAllBytes(p,data);return Ref(p);}
    public static void Run()
    {
        Directory.CreateDirectory(Out);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        var root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/V3/Remielle_V3_Animated.prefab"));var driver=root.GetComponent<RemielleNativeAnimation>();
        var go=new GameObject("Native depth hierarchy input audit");var camera=go.AddComponent<Camera>();camera.enabled=false;
        var cases=new JArray();var lifecycle=new JArray();
        try
        {
            foreach(string profileName in new[]{"display","store"})
            {
                int before=Targets();
                {
                    var profile=AssetDatabase.LoadAssetAtPath<RemielleNativeUIProfile>(RemielleUILiveProfileBuild.Assets+profileName+".asset");
                    using var geometry=new RemielleNativeUIRenderer(profile,driver,camera);
                    using var hierarchy=new RemielleNativeUIDepthHierarchy(AssetDatabase.LoadAssetAtPath<Shader>(RemielleUIDepthHierarchyAudit.ShaderPath));
                    var reader=new Material(profile.depthReaderShader);var cmd=new CommandBuffer();
                    try
                    {
                        for(int i=0;i<3;i++)
                        {
                            int w=new[]{960,1280,768}[i],h=new[]{540,720,432}[i];float yaw=new[]{0f,65f,180f}[i];string sample=new[]{"__rest","Idle_Loop","Walk_Start"}[i];
                            if(i==0){driver.ResetSourcePose();driver.ApplyPose();}else driver.Sample(sample,driver.nativeAnimation[sample].length*.37f);
                            camera.aspect=(float)w/h;camera.fieldOfView=38;camera.nearClipPlane=new[]{.1f,.3f,.2f}[i];camera.farClipPlane=new[]{50f,75f,100f}[i];RemielleUILiveRasterAudit.FrameModel(geometry,camera,yaw);
                            geometry.Prepare(w,h,new Vector2(.25f,-.125f));geometry.Render();string id=profileName+"-"+i;
                            var depthBytes=RemielleUINativePostAudit.Read(geometry.DepthRead,w*h*16);var packedDepth=new byte[w*h*4];for(int p=0;p<w*h;p++)Buffer.BlockCopy(depthBytes,p*16,packedDepth,p*4,4);
                            var normalBytes=RemielleUINativePostAudit.Read(geometry.Targets[3],w*h*4);
                            hierarchy.Render(geometry,camera);int ow=hierarchy.Width,oh=hierarchy.Height;
                            var output=new JArray();foreach(var pair in new[]{(hierarchy.LinearDepth,"linear"),(hierarchy.NormalAndSample,"normal"),(hierarchy.Range,"range")})output.Add(Write(id+"-"+pair.Item2+".raw",RemielleUINativePostAudit.Read(pair.Item1,ow*oh*4)));
                            var mip=AsyncGPUReadback.Request(hierarchy.Range,1);mip.WaitForCompletion();if(mip.hasError)throw new Exception("Live hierarchy mip failed");var mipRef=Write(id+"-mip1.raw",mip.GetData<byte>().ToArray());
                            var read=new RenderTexture(new RenderTextureDescriptor(ow,oh,GraphicsFormat.R32G32B32A32_SFloat,GraphicsFormat.None));read.Create();JObject depthRef;
                            try{reader.SetTexture("_SeqDepth",hierarchy.Depth,RenderTextureSubElement.Depth);reader.SetTexture("_SeqStencil",hierarchy.Depth,RenderTextureSubElement.Stencil);cmd.Clear();cmd.SetRenderTarget(read);cmd.SetViewport(new Rect(0,0,ow,oh));cmd.DrawProcedural(Matrix4x4.identity,reader,0,MeshTopology.Triangles,3);Graphics.ExecuteCommandBuffer(cmd);depthRef=Write(id+"-depth-stencil.rgba32f",RemielleUINativePostAudit.Read(read,ow*oh*16));}
                            finally{RenderTexture.active=null;read.Release();Object.DestroyImmediate(read);}
                            if(!normalBytes.SequenceEqual(RemielleUINativePostAudit.Read(geometry.Targets[3],w*h*4)))throw new Exception("Hierarchy modified input normal target");
                            cases.Add(new JObject{["id"]=id,["profile"]=profileName,["sample"]=sample,["yaw"]=yaw,["width"]=w,["height"]=h,["near"]=camera.nearClipPlane,["far"]=camera.farClipPlane,["depthInput"]=Write(id+"-source-depth.raw",packedDepth),["normalInput"]=Write(id+"-source-normal.raw",normalBytes),["outputs"]=output,["rangeMip1"]=mipRef,["depthStencil"]=depthRef,["sourceNormalPreserved"]=true});
                        }
                    }
                    finally{cmd.Release();Object.DestroyImmediate(reader);}
                }
                lifecycle.Add(new JObject{["profile"]=profileName,["before"]=before,["after"]=Targets()});
            }
            var files=new JArray();foreach(string p in new[]{"Assets/Editor/RemielleUILiveDepthHierarchyAudit.cs","Assets/RenderingReview/Runtime/RemielleNativeUIDepthHierarchy.cs","Assets/RenderingReview/Runtime/RemielleNativeUIRenderer.cs","Assets/RenderingReview/Runtime/RemielleNativeUIMeshBinding.cs","Assets/RenderingReview/Runtime/RemielleNativeUIConstants.cs",RemielleUIDepthHierarchyAudit.ShaderPath,"Assets/V3/Remielle_V3_Animated.prefab"})files.Add(Ref(p));
            File.WriteAllText(Out+"unity.json",new JObject{["schema"]="remielle-ui-live-depth-hierarchy-v1",["cases"]=cases,["lifecycle"]=lifecycle,["implementation"]=files,["boundary"]="Six current skin/camera/jitter/resolution/near-far inputs. The producer is independent of the main HDR chain; auxiliary consumers and the visible Player remain separate."}.ToString());Debug.Log("REMIELLE_UI_LIVE_DEPTH_HIERARCHY 6");
        }
        finally{Object.DestroyImmediate(go);Object.DestroyImmediate(root);}
    }
}
