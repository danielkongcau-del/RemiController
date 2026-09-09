using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

public static class RemielleUILiveLateBodyAudit
{
    const string Out="E:/ZZZ/ZCode/90_Builds/RenderingReview/20260905/ui-live-binding/live-late-body/";
    static JObject Ref(string p)=>RemielleUINativePostBuild.Ref(p);
    static string Q(string p)=>"\""+p.Replace('\\','/')+"\"";
    static byte[] Bytes<T>(T[] values)where T:struct
    {
        var bytes=new byte[values.Length*Marshal.SizeOf<T>()];var pin=GCHandle.Alloc(values,GCHandleType.Pinned);
        try{Marshal.Copy(pin.AddrOfPinnedObject(),bytes,0,bytes.Length);}finally{pin.Free();}return bytes;
    }
    static void ExportMesh(Mesh mesh,string stem)
    {
        var p=mesh.vertices;var n=mesh.normals;var t=mesh.tangents;var c=mesh.colors;var values=new Vector4[p.Length*9];
        for(int v=0;v<p.Length;v++){values[v*9]=new Vector4(p[v].x,p[v].y,p[v].z,1);values[v*9+1]=new Vector4(n[v].x,n[v].y,n[v].z,1);values[v*9+2]=t[v];values[v*9+3]=c[v];}
        for(int channel=0;channel<5;channel++){var uv=new List<Vector4>();mesh.GetUVs(channel,uv);if(uv.Count!=p.Length)throw new Exception("Late live UV source missing");for(int v=0;v<p.Length;v++)values[v*9+4+channel]=uv[v];}
        File.WriteAllBytes(stem+"-vb.buf",Bytes(values));File.WriteAllBytes(stem+"-ib.buf",Bytes(mesh.GetIndices(0).Select(i=>checked((ushort)i)).ToArray()));
    }
    static int PrivateTargets()=>Resources.FindObjectsOfTypeAll<RenderTexture>().Count(t=>(t.hideFlags&HideFlags.HideAndDontSave)==HideFlags.HideAndDontSave);
    public static RemielleNativeUILighting CreateLighting(string name)
    {
        var source=JObject.Parse(File.ReadAllText("E:/ZZZ/local-only/RemielleRenderingReview/20260905/captured-deferred-character-ui.json"));
        var row=source["cases"].Single(c=>(string)c["key"]==name);var lutSource=row["inputs"].Single(i=>(int)i["slot"]==5);
        var lut=AssetDatabase.LoadAssetAtPath<Texture>(RemielleUILiveProfileBuild.Assets+"Textures/"+lutSource["sourceSha256"]+".asset");
        return new RemielleNativeUILighting(AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/CapturedDeferredCharacter523.shader"),lut,RemielleReviewBloom.Vectors(row["pixelConstants"].Select(v=>(float)v).ToArray(),185));
    }
    public static void Run()
    {
        Directory.CreateDirectory(Out+"inputs");Directory.CreateDirectory(Out+"unity");
        var manifest=JObject.Parse(File.ReadAllText(RemielleUILateBodyBuild.Root+"isolated-inputs.json"));var shaders=manifest["programs"].ToDictionary(s=>(string)s["stage"]);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);var model=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/V3/Remielle_V3_Animated.prefab"));var driver=model.GetComponent<RemielleNativeAnimation>();
        var cameraObject=new GameObject("Native late Body all-action audit");var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;
        var lines=new StringBuilder();var initial=new StringBuilder();var cases=new JArray();var lifecycle=new JArray();const int w=960,h=540;
        try
        {
            foreach(string profileName in new[]{"display","store"})
            {
                int before=PrivateTargets();
                {
                    var profile=AssetDatabase.LoadAssetAtPath<RemielleNativeUIProfile>(RemielleUILiveProfileBuild.Assets+profileName+".asset");
                    var lateProfile=AssetDatabase.LoadAssetAtPath<RemielleNativeUILateBodyProfile>(RemielleUILateBodyBuild.Assets+profileName+".asset");
                    using var geometry=new RemielleNativeUIRenderer(profile,driver,camera);using var lighting=CreateLighting(profileName);using var late=new RemielleNativeUILateBody(lateProfile);
                    var source=manifest["cases"].Single(c=>(string)c["profile"]==profileName);
                    driver.ResetSourcePose();driver.ApplyPose();var clips=driver.nativeAnimation.Cast<AnimationState>().ToArray();if(clips.Length!=15)throw new Exception("Expected all 15 native main actions");
                    var readMaterial=new Material(profile.depthReaderShader);var readTarget=new RenderTexture(new RenderTextureDescriptor(w,h,GraphicsFormat.R32G32B32A32_SFloat,GraphicsFormat.None)){hideFlags=HideFlags.HideAndDontSave};readTarget.Create();var cmd=new CommandBuffer();
                    try
                    {
                        for(int i=0;i<clips.Length;i++)
                        {
                            var clip=clips[i];float time=clip.length*.37f,yaw=new[]{0f,65f,180f}[i%3];driver.Sample(clip.name,time);
                            camera.aspect=(float)w/h;camera.fieldOfView=38;camera.nearClipPlane=.1f;camera.farClipPlane=50;RemielleUILiveRasterAudit.FrameModel(geometry,camera,yaw);
                            var jitter=new Vector2((i%4-.5f)*.125f,(i%3-.5f)*.1875f);geometry.Prepare(w,h,jitter);late.Prepare(geometry,driver);
                            geometry.Render();lighting.Render(geometry,camera);
                            string id=profileName+"-"+i.ToString("D2"),stem=Out+"inputs/"+id;ExportMesh(late.Mesh,stem);
                            string hdr=stem+"-initial.raw",depth=stem+"-depth.raw";File.WriteAllBytes(hdr,RemielleUINativePostAudit.Read(lighting.Hdr,w*h*8));
                            var ds=RemielleUINativePostAudit.Read(geometry.DepthRead,w*h*16);var depthRaw=new byte[w*h*8];
                            for(int pixel=0;pixel<w*h;pixel++){Buffer.BlockCopy(ds,pixel*16,depthRaw,pixel*8,4);BitConverter.TryWriteBytes(depthRaw.AsSpan(pixel*8+4,4),(uint)BitConverter.ToSingle(ds,pixel*16+4));}File.WriteAllBytes(depth,depthRaw);
                            var aux=RemielleUINativePostAudit.Read(lighting.Auxiliary,w*h*4);
                            lines.AppendLine(id+" 1014 0 "+Q((string)shaders["vs"]["artifacts"]["dxbc"]["path"])+" "+Q((string)shaders["vs"]["translation"]["path"])+" "+Q((string)shaders["ps"]["artifacts"]["dxbc"]["path"])+" "+Q((string)shaders["ps"]["translation"]["path"]));
                            lines.AppendLine("9\nPOSITION 0 6 0 0\nNORMAL 0 6 0 16\nTANGENT 0 2 0 32\nCOLOR 0 2 0 48\nTEXCOORD 0 2 0 64\nTEXCOORD 1 2 0 80\nTEXCOORD 2 2 0 96\nTEXCOORD 3 2 0 112\nTEXCOORD 4 6 0 128\n1\n0 144 "+Q(stem+"-vb.buf")+"\n"+Q(stem+"-ib.buf"));
                            lines.AppendLine(late.Constants.Length.ToString());var constants=new JArray();
                            foreach(var cb in late.Constants){string path=stem+"-"+cb.source.stage+cb.source.slot+".buf";File.WriteAllBytes(path,cb.bytes);lines.AppendLine((cb.source.stage=="vs"?0:1)+" "+cb.source.slot+" "+Q(path));constants.Add(new JObject{["stage"]=cb.source.stage,["slot"]=cb.source.slot,["data"]=Ref(path)});}
                            lines.AppendLine(source["resources"].Count().ToString());foreach(var r in source["resources"])lines.AppendLine(((string)r["stage"]=="vs"?0:1)+" "+((string)r["slot"]).Substring(1)+" "+r["kind"]+" "+r["width"]+" "+r["height"]+" "+r["mips"]+" "+r["layers"]+" "+r["format"]+" "+Q((string)r["path"]));
                            lines.AppendLine(source["samplers"].Count().ToString());foreach(var s in source["samplers"])lines.AppendLine("1 "+((string)s["slot"]).Substring(1)+" "+s["group"]);
                            initial.AppendLine(id+" "+Q(hdr)+" "+Q(depth)+" 3 2 6 1 2");
                            late.Draw(lighting.Hdr,geometry.Depth);
                            if(!aux.SequenceEqual(RemielleUINativePostAudit.Read(lighting.Auxiliary,w*h*4)))throw new Exception("Late draw modified temporal auxiliary");
                            readMaterial.SetTexture("_SeqDepth",geometry.Depth,RenderTextureSubElement.Depth);readMaterial.SetTexture("_SeqStencil",geometry.Depth,RenderTextureSubElement.Stencil);
                            cmd.Clear();cmd.SetRenderTarget(readTarget);cmd.SetViewport(new Rect(0,0,w,h));cmd.DrawProcedural(Matrix4x4.identity,readMaterial,0,MeshTopology.Triangles,3);Graphics.ExecuteCommandBuffer(cmd);
                            if(!ds.SequenceEqual(RemielleUINativePostAudit.Read(readTarget,w*h*16)))throw new Exception("Late draw modified live depth/stencil");
                            string output=Out+"unity/"+id+".raw";File.WriteAllBytes(output,RemielleUINativePostAudit.Read(lighting.Hdr,w*h*8));
                            cases.Add(new JObject{["id"]=id,["profile"]=profileName,["sample"]=clip.name,["time"]=time,["yaw"]=yaw,["jitterPixels"]=new JArray(jitter.x,jitter.y),["vertex"]=Ref(stem+"-vb.buf"),["index"]=Ref(stem+"-ib.buf"),["constants"]=constants,["initialHdr"]=Ref(hdr),["depth"]=Ref(depth),["output"]=Ref(output),["depthStencilPreserved"]=true,["temporalAuxiliaryPreserved"]=true});
                            Debug.Log("REMIELLE_UI_LIVE_LATE_BODY "+id+" "+clip.name);
                        }
                    }
                    finally{cmd.Release();RenderTexture.active=null;readTarget.Release();Object.DestroyImmediate(readTarget);Object.DestroyImmediate(readMaterial);}
                }
                int after=PrivateTargets();lifecycle.Add(new JObject{["profile"]=profileName,["before"]=before,["after"]=after});
            }
            File.WriteAllText(Out+"inputs.txt",cases.Count+" "+w+" "+h+"\n"+lines);File.WriteAllText(Out+"initial.txt",initial.ToString());
            var implementation=new JArray();foreach(string p in new[]{"Assets/Editor/RemielleUILiveLateBodyAudit.cs","Assets/RenderingReview/Runtime/RemielleNativeUILateBody.cs","Assets/RenderingReview/Runtime/RemielleNativeUIRenderer.cs","Assets/RenderingReview/Runtime/RemielleNativeUIConstants.cs","Assets/RenderingReview/Runtime/RemielleNativeUIMeshBinding.cs","Assets/RenderingReview/Runtime/RemielleNativeUILighting.cs","Assets/V3/Remielle_V3_Animated.prefab"})implementation.Add(Ref(p));
            File.WriteAllText(Out+"unity.json",new JObject{["schema"]="remielle-ui-live-late-body-readback-v1",["width"]=w,["height"]=h,["cases"]=cases,["lifecycle"]=lifecycle,["inputs"]=Ref(Out+"inputs.txt"),["initial"]=Ref(Out+"initial.txt"),["source"]=Ref(RemielleUILateBodyBuild.Root+"isolated-inputs.json"),["implementation"]=implementation,["boundary"]="One pose per each of 15 native actions and each profile, with current skin, camera and jitter. Continuous Player acceptance and dynamic light producers remain separate."}.ToString());
        }
        finally{Object.DestroyImmediate(cameraObject);Object.DestroyImmediate(model);}
    }
}
