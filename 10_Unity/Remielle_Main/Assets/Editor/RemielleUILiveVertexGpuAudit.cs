using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class RemielleUILiveVertexGpuAudit
{
    const string Out = "E:/ZZZ/local-only/RemielleRenderingReview/20260905/ui-live-binding/";
    const string WriteRoot = "E:/ZZZ/ZCode/90_Builds/RenderingReview/20260905/ui-live-binding/"; // D1-c 写根（读根保留 A 类）
    const string Capture = "E:/ZZZ/local-only/RemielleRenderingReview/20260905/ui-full-sequence/";
    const string Prefab = "Assets/V3/Remielle_V3_Animated.prefab";
    [StructLayout(LayoutKind.Sequential)] struct Input { public Vector4 a,b,c,d,e,f,g,h,i; }
    [StructLayout(LayoutKind.Sequential)] struct Output { public Vector4 a,b,c,d,e,f,g,h,i,j; }
    [StructLayout(LayoutKind.Sequential)] struct Entity { public Vector4 a,b,c,d,e,f,g,h; }
    static JObject Load(string path) => JObject.Parse(File.ReadAllText(path));
    static string Sha(string path) { using var f=File.OpenRead(path); using var h=SHA256.Create(); return BitConverter.ToString(h.ComputeHash(f)).Replace("-", "").ToLowerInvariant(); }
    static JObject Ref(string path) => new JObject { ["path"]=Path.GetFullPath(path), ["sha256"]=Sha(path) };
    static JArray Matrix(Matrix4x4 m) => new JArray(Enumerable.Range(0,16).Select(i=>m[i]));
    static string Quote(string path) => "\"" + path.Replace('\\','/') + "\"";
    static byte[] Bytes<T>(T[] data) where T:struct
    {
        var result=new byte[data.Length*Marshal.SizeOf<T>()]; var pin=GCHandle.Alloc(data,GCHandleType.Pinned);
        try { Marshal.Copy(pin.AddrOfPinnedObject(),result,0,result.Length); } finally { pin.Free(); } return result;
    }
    static T[] Structs<T>(byte[] data) where T:struct
    {
        int size=Marshal.SizeOf<T>(); if(data.Length%size!=0)throw new Exception("Invalid native struct size");
        var result=new T[data.Length/size]; var pin=GCHandle.Alloc(result,GCHandleType.Pinned);
        try { Marshal.Copy(data,0,pin.AddrOfPinnedObject(),data.Length); } finally { pin.Free(); } return result;
    }
    static Vector4 Decode(byte[] b,int at,int format)
    {
        if(format==28)return new Vector4(b[at]/255f,b[at+1]/255f,b[at+2]/255f,b[at+3]/255f);
        if(format==34)return new Vector4(Mathf.HalfToFloat(BitConverter.ToUInt16(b,at)),Mathf.HalfToFloat(BitConverter.ToUInt16(b,at+2)),0,1);
        int count=format==2?4:format==6?3:format==16?2:0; if(count==0)throw new Exception("Unexpected live input format");
        var v=new Vector4(0,0,0,1);for(int i=0;i<count;i++)v[i]=BitConverter.ToSingle(b,at+i*4);return v;
    }
    static Input[] Inputs(JObject draw,RemielleNativeUIMeshBinding binding)
    {
        int count=binding.Positions.Length;var attributes=new Vector4[count*9];
        var streams=draw["vertexBuffers"].ToDictionary(v=>(int)v["slot"],v=>(raw:File.ReadAllBytes((string)v["path"]),stride:(int)v["stride"]));
        foreach(JArray e in draw["inputLayout"])
        {
            string semantic=(string)e[0]; int index=semantic=="POSITION"?0:semantic=="NORMAL"?1:semantic=="TANGENT"?2:semantic=="COLOR"?3:4+(int)e[1];
            var stream=streams[(int)e[3]];
            for(int i=0;i<count;i++)attributes[i*9+index]=Decode(stream.raw,i*stream.stride+(int)e[4],(int)e[2]);
        }
        for(int i=0;i<count;i++)
        {
            var p=binding.Positions[i];var n=binding.Normals[i];var pp=binding.PreviousPositions[i];
            attributes[i*9]=new Vector4(p.x,p.y,p.z,1);attributes[i*9+1]=new Vector4(n.x,n.y,n.z,1);
            attributes[i*9+2]=binding.Tangents[i];attributes[i*9+8]=new Vector4(pp.x,pp.y,pp.z,1);
        }
        return Structs<Input>(Bytes(attributes));
    }

    public static void Run() { Run(true); }
    public static void RunComputeDiagnostic() { Run(false); }
    public static void RunAll() { RemielleUILivePoseAudit.Run(); Run(true); }
    public static void RunVertexStage() { Run(true); }
    static void Run(bool vertexStage)
    {
        if(SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Direct3D11 || !SystemInfo.supportsComputeShaders)
            throw new Exception("D3D11 compute probe required");
        string outputFolder=vertexStage?"unity-live-vertex-stage":"unity-live-vertex";
        Directory.CreateDirectory(WriteRoot+"live-vertex-inputs");Directory.CreateDirectory(WriteRoot+outputFolder);
        var manifest=Load(Capture+"manifest.json");var sourceBindings=Load(Out+"mesh-bindings.json");var roots=Load(Out+"native-root-bindings.json");
        var contract=Load(Out+"uniform-contract.json")["shaders"].ToDictionary(s=>(string)s["hash"]);
        var shaderSources=manifest["shaders"].ToDictionary(s=>(string)s["hash"]);
        var draws=manifest["cases"].Cast<JObject>().Where(d=>(string)d["profile"]=="display" && new[]{0,1,2,3,4,6}.Contains((int)d["relative"])).ToArray();
        var bindingNames=sourceBindings["draws"].ToDictionary(d=>(string)d["id"],d=>(string)d["mesh"]);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        var root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab));
        var driver=root.GetComponent<RemielleNativeAnimation>();var smrs=root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var cameraObject=new GameObject("Native UI live vertex camera");var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;
        var pointTarget=new RenderTexture(256,256,0,RenderTextureFormat.ARGB32){hideFlags=HideFlags.HideAndDontSave};
        if(vertexStage&&!pointTarget.Create())throw new Exception("Could not create native vertex transport target");
        var bindings=new Dictionary<string,RemielleNativeUIMeshBinding>();
        var cases=new JArray();var lines=new StringBuilder();int frameIndex=0;
        var working=Matrix4x4.TRS(new Vector3(500,0,0),Quaternion.Euler(0,180,0),Vector3.one);
        var pelvis=driver.bones.Single(b=>b.source.name=="Bip001 Pelvis").source;
        var head=driver.bones.Single(b=>b.source.name=="Bip001 Head").source;
        RemielleNativeUIConstants.Frame previous=null;
        try
        {
            foreach(var r in roots["meshes"])
            {
                string name=(string)r["mesh"];var smr=smrs.Single(s=>s.name=="SMR_"+name&&s.enabled);
                var native=driver.bones.Single(b=>b.source.name==(string)r["rootName"]).source;
                bindings.Add(name,new RemielleNativeUIMeshBinding(smr,smr.sharedMesh,native));
            }
            driver.ResetSourcePose();driver.ApplyPose();
            var samples=driver.nativeAnimation.Cast<AnimationState>().Select(s=>(name:s.name,time:s.length*.37f)).ToList();samples.Insert(0,("__rest",0));
            foreach(var sample in samples)
            {
                if(sample.name!="__rest")driver.Sample(sample.name,sample.time);
                float yaw=new[]{0f,70f,-70f,180f}[frameIndex%4];var target=new Vector3(0,1.1f,0);
                camera.transform.position=target+Quaternion.Euler(0,yaw,0)*new Vector3(0,.15f,3.2f);
                camera.transform.rotation=Quaternion.LookRotation(target-camera.transform.position,Vector3.up);
                int width=frameIndex%2==0?960:640,height=frameIndex%2==0?540:480;
                camera.aspect=(float)width/height;camera.fieldOfView=30+8*(frameIndex%3);camera.nearClipPlane=.1f;camera.farClipPlane=50;
                var frame=new RemielleNativeUIConstants.Frame(camera,width,height,working,previous);
                foreach(var b in bindings.Values)b.Prepare();
                foreach(var draw in draws)
                {
                    string meshName=bindingNames[(string)draw["id"]],hash=(string)draw["vs"],id=sample.name+"-"+draw["relative"];
                    var binding=bindings[meshName];var input=Inputs(draw,binding);var cbFiles=new JArray();var graphics=new List<GraphicsBuffer>();
                    string stem=WriteRoot+"live-vertex-inputs/"+id,vb=stem+".buf";File.WriteAllBytes(vb,Bytes(input));
                    var shader=vertexStage?null:AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/RenderingReview/Shader/NativeUILiveVertexProbe/NativeUIVertex_"+hash+".compute");
                    var gfx=vertexStage?AssetDatabase.LoadAssetAtPath<Shader>("Assets/RenderingReview/Shader/NativeUILiveVertexProbe/NativeUIVertexStage_"+hash+".shader"):null;
                    if(vertexStage?!gfx||!gfx.isSupported:!shader)throw new Exception("Live vertex probe is missing or unsupported");
                    int kernel=vertexStage?0:shader.FindKernel("CSMain");
                    var material=vertexStage?new Material(gfx){hideFlags=HideFlags.HideAndDontSave}:null;
                    if(!vertexStage)foreach(var message in ShaderUtil.GetComputeShaderMessages(shader))
                        if(message.severity.ToString()=="Error")throw new Exception("Live vertex probe failed to compile: "+message.message);
                    try
                    {
                        var ib=new GraphicsBuffer(GraphicsBuffer.Target.Structured,input.Length,144);graphics.Add(ib);ib.SetData(input);
                        var ob=new GraphicsBuffer(GraphicsBuffer.Target.Structured,input.Length,160);graphics.Add(ob);
                        if(vertexStage)material.SetBuffer("_LiveInput",ib);
                        else {shader.SetBuffer(kernel,"_LiveInput",ib);shader.SetBuffer(kernel,"_LiveOutput",ob);shader.SetInt("_LiveVertexCount",input.Length);}
                        foreach(var cb in draw["constantBuffers"].Where(c=>(string)c["stage"]=="vs").OrderBy(c=>(int)c["slot"]))
                        {
                            int slot=(int)cb["slot"],count=(int)cb["declaredFloat4"];var bytes=File.ReadAllBytes((string)cb["path"]).Take(count*16).ToArray();
                            var cbContract=contract[hash]["constantBuffers"].Single(c=>(int)c["slot"]==slot);
                            var fields=cbContract["variables"].Select(f=>new RemielleNativeUIConstants.Field{name=(string)f["name"],offsetBytes=(int)f["offsetBytes"],storageBytes=(int)f["storageBytes"]}).ToArray();
                            int patched=RemielleNativeUIConstants.Patch(bytes,fields,frame,binding,pelvis,head);
                            string path=stem+"-cb"+slot+".buf";File.WriteAllBytes(path,bytes);
                            cbFiles.Add(new JObject{["slot"]=slot,["file"]=Ref(path),["patchedFields"]=patched,["source"]=Ref((string)cb["path"])});
                            var buffer=new GraphicsBuffer(GraphicsBuffer.Target.Constant,count,16);graphics.Add(buffer);buffer.SetData(Structs<Vector4>(bytes));
                            if(vertexStage)material.SetConstantBuffer("SeqVSCb"+slot,buffer,0,bytes.Length);
                            else shader.SetConstantBuffer("SeqVSCb"+slot,buffer,0,bytes.Length);
                        }
                        var resource=draw["resources"].Single(r=>(string)r["stage"]=="vs"&&(int)r["kind"]==0);
                        var entities=Structs<Entity>(File.ReadAllBytes((string)resource["path"]));
                        var scene=new GraphicsBuffer(GraphicsBuffer.Target.Structured,entities.Length,128);graphics.Add(scene);scene.SetData(entities);
                        if(vertexStage)
                        {
                            material.SetBuffer("_SeqVST0",scene);
                            var cmd=new CommandBuffer{name="Native UI actual vertex-stage probe"};
                            try {
                                cmd.SetRenderTarget(pointTarget);cmd.SetViewport(new Rect(0,0,256,256));
                                cmd.SetRandomWriteTarget(1,ob);cmd.DrawProcedural(Matrix4x4.identity,material,0,MeshTopology.Points,input.Length);
                                cmd.ClearRandomWriteTargets();Graphics.ExecuteCommandBuffer(cmd);
                            } finally {cmd.Release();}
                            foreach(var message in ShaderUtil.GetShaderMessages(gfx))
                                if(message.severity.ToString()=="Error")throw new Exception("Live vertex shader failed: "+message.message);
                        }
                        else
                        {
                            shader.SetBuffer(kernel,"_SeqVST0",scene);shader.Dispatch(kernel,(input.Length+63)/64,1,1);
                            foreach(var message in ShaderUtil.GetComputeShaderMessages(shader))
                                if(message.severity.ToString()=="Error")throw new Exception("Live vertex dispatch failed: "+message.message);
                        }
                        var output=new Output[input.Length];ob.GetData(output);
                        string result=WriteRoot+outputFolder+"/"+id+".f32";File.WriteAllBytes(result,Bytes(output));
                        cases.Add(new JObject{["id"]=id,["clip"]=sample.name,["time"]=sample.time,["mesh"]=meshName,["vs"]=hash,["vertices"]=input.Length,
                            ["width"]=width,["height"]=height,["cameraYaw"]=yaw,["fieldOfView"]=camera.fieldOfView,["input"]=Ref(vb),["constantBuffers"]=cbFiles,
                            ["entities"]=Ref((string)resource["path"]),["output"]=Ref(result),["objectToWorld"]=Matrix(working*binding.ObjectToWorld),
                            ["previousObjectToWorld"]=Matrix(frame.previousSceneToProfile*binding.PreviousObjectToWorld),["vp"]=Matrix(frame.vp),["previousVP"]=Matrix(frame.previousVP),
                            ["hasHistory"]=frame.hasHistory&&binding.HasHistory,["positionRoundTripMaxError"]=binding.PositionRoundTripMaxError});
                        lines.AppendLine(id+" "+input.Length+" "+Quote((string)shaderSources[hash]["dxbc"]["path"])+" "+Quote((string)shaderSources[hash]["translation"]["path"]));
                        lines.AppendLine("9");lines.AppendLine("POSITION 0 6 0 0\nNORMAL 0 6 0 16\nTANGENT 0 2 0 32\nCOLOR 0 2 0 48\nTEXCOORD 0 2 0 64\nTEXCOORD 1 2 0 80\nTEXCOORD 2 2 0 96\nTEXCOORD 3 2 0 112\nTEXCOORD 4 6 0 128");
                        lines.AppendLine("1\n0 144 "+Quote(vb));foreach(var cb in cbFiles)lines.AppendLine(Quote((string)cb["file"]["path"]));lines.AppendLine(Quote((string)resource["path"]));
                    }
                    finally { foreach(var buffer in graphics)buffer.Dispose();if(material)Object.DestroyImmediate(material); }
                }
                foreach(var b in bindings.Values)b.Commit();previous=frame;frameIndex++;
            }
            File.WriteAllText(WriteRoot+"live-vertex-inputs.txt",cases.Count+"\n"+lines,new UTF8Encoding(false));
            var files=new JArray{Ref(Prefab),Ref("Assets/Editor/RemielleUILiveVertexGpuAudit.cs"),Ref("Assets/RenderingReview/Runtime/RemielleNativeUIMeshBinding.cs"),Ref("Assets/RenderingReview/Runtime/RemielleNativeUIConstants.cs"),Ref(Out+"uniform-contract.json"),Ref(Out+"native-root-bindings.json"),Ref(Out+"mesh-bindings.json"),Ref(Out+"vertex-probe-shaders.json")};
            File.WriteAllText(WriteRoot+outputFolder+".json",new JObject{["schema"]="remielle-ui-live-vertex-gpu-v1",["executionStage"]=vertexStage?"vertex":"compute",["device"]=SystemInfo.graphicsDeviceName,["api"]=SystemInfo.graphicsDeviceType.ToString(),["cases"]=cases,["implementationFiles"]=files,["inputs"]=Ref(WriteRoot+"live-vertex-inputs.txt"),
                ["boundary"]=vertexStage?"Actual Unity vertex-stage execution with nointerpolation point/PS UAV transport of original defined outputs. Current bones, changing camera/resolution, previous-pose inputs. Full live character rasterization and lighting remain separate gates.":"Current saved bones and blendshapes, original root frames, changing camera/resolution and previous-pose inputs. Unity compute calls two original-source VS functions; independent DXBC vertex-stage comparison and live rasterized lighting remain separate gates."}.ToString());
            Debug.Log("REMIELLE_UI_LIVE_VERTEX_GPU_READBACK "+cases.Count);
        }
        finally { foreach(var binding in bindings.Values)binding.Dispose();pointTarget.Release();Object.DestroyImmediate(pointTarget);Object.DestroyImmediate(cameraObject);Object.DestroyImmediate(root); }
    }
}
