using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

// Uses captured geometry in memory. Never regenerates or saves model assets.
public static class RemielleNativeMaterialGpuAudit
{
    const string Root="E:/ZZZ/local-only/RemielleRenderingReview/20260905/";
    const string Acq="E:/ZZZ/local-only/RemielleDataAcquisition/20260904/";
    const string ShaderPath="Assets/RenderingReview/Shader/GeneratedNative/CapturedNativeMaterialBodies.shader";
    static readonly int[] Counts={209,29,27,41,170};
    [DllImport("RemielleUnityStateProbe")]static extern IntPtr GetRemielleStateObserver();
    [StructLayout(LayoutKind.Sequential)]struct Entity {public Vector4 a,b,c,d,e,f,g,h;}
    static string Sha(string p){using(var s=File.OpenRead(p))using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(s)).Replace("-","").ToLowerInvariant();}
    static T[] ReadStructs<T>(byte[] bytes) where T:struct
    {
        int size=Marshal.SizeOf<T>();if(bytes.Length%size!=0)throw new Exception("Invalid structured bytes");
        var values=new T[bytes.Length/size];var pin=GCHandle.Alloc(values,GCHandleType.Pinned);
        try{Marshal.Copy(bytes,0,pin.AddrOfPinnedObject(),bytes.Length);}finally{pin.Free();}return values;
    }
    static Vector4 Decode(byte[] raw,int at,int format)
    {
        var v=new Vector4(0,0,0,1);
        if(format==28)return new Vector4(raw[at]/255f,raw[at+1]/255f,raw[at+2]/255f,raw[at+3]/255f);
        if(format==34){v.x=Mathf.HalfToFloat(BitConverter.ToUInt16(raw,at));v.y=Mathf.HalfToFloat(BitConverter.ToUInt16(raw,at+2));return v;}
        int count=format==2?4:format==6?3:format==16?2:0;if(count==0)throw new Exception("Unknown vertex format: "+format);
        for(int i=0;i<count;i++)v[i]=BitConverter.ToSingle(raw,at+i*4);return v;
    }
    static Mesh BuildMesh(JObject row,int first,int count)
    {
        int vertices=(int)row["vertices"];var streams=new Dictionary<int,byte[]>();var strides=new Dictionary<int,int>();
        foreach(var vb in row["vertexBuffers"]){int slot=(int)vb["slot"];streams[slot]=File.ReadAllBytes((string)vb["path"]);strides[slot]=(int)vb["stride"];}
        var mesh=new Mesh{name="CapturedFixture_"+row["draw"],indexFormat=IndexFormat.UInt32,hideFlags=HideFlags.HideAndDontSave};
        foreach(JArray e in row["inputLayout"])
        {
            string semantic=(string)e[0];int semanticIndex=(int)e[1],format=(int)e[2],slot=(int)e[3],offset=(int)e[4];
            var values=new List<Vector4>(vertices);for(int v=0;v<vertices;v++)values.Add(Decode(streams[slot],v*strides[slot]+offset,format));
            if(semantic=="POSITION")mesh.SetVertices(values.Select(v=>(Vector3)v).ToList());
            else if(semantic=="NORMAL")mesh.SetNormals(values.Select(v=>(Vector3)v).ToList());
            else if(semantic=="TANGENT")mesh.SetTangents(values);
            else if(semantic=="COLOR")mesh.SetColors(values.Select(v=>new Color(v.x,v.y,v.z,v.w)).ToList());
            else if(semantic=="TEXCOORD")mesh.SetUVs(semanticIndex,values);
            else throw new Exception("Unexpected semantic");
        }
        var raw=File.ReadAllBytes((string)row["indexBuffer"]);var indices=new int[count];for(int i=0;i<count;i++)indices[i]=BitConverter.ToUInt16(raw,(first+i)*2);
        mesh.SetIndices(indices,MeshTopology.Triangles,0,true);mesh.UploadMeshData(false);return mesh;
    }
    static Texture LoadDepth(JObject row)
    {
        int w=(int)row["width"],h=(int)row["height"],layers=(int)row["layers"];var bytes=File.ReadAllBytes((string)row["path"]);
        int header=bytes[84]=='D'&&bytes[85]=='X'&&bytes[86]=='1'&&bytes[87]=='0'?148:128;
        if(bytes.Length!=header+w*h*layers*2)throw new Exception("Depth fixture size mismatch");
        if(layers==1){var t=new Texture2D(w,h,TextureFormat.R16,false,true){hideFlags=HideFlags.HideAndDontSave};var raw=new byte[w*h*2];Buffer.BlockCopy(bytes,header,raw,0,raw.Length);t.LoadRawTextureData(raw);t.Apply(false,false);return t;}
        var array=new Texture2DArray(w,h,layers,TextureFormat.R16,1,true){hideFlags=HideFlags.HideAndDontSave};
        for(int i=0;i<layers;i++)array.SetPixelData(bytes,0,i,header+i*w*h*2);array.Apply(false,false);return array;
    }
    public static void Run(){RunFixture(false);}
    public static void RunWindingNegativeControl(){RunFixture(true);}
    static void RunFixture(bool wrongWinding)
    {
        if(SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Direct3D11)throw new Exception("D3D11 required");
        var fixture=JObject.Parse(File.ReadAllText(Root+"native-pixel-replay/unity-reference/manifest.json"));
        var vertex=JObject.Parse(File.ReadAllText(Root+"native-vertex-replay/manifest.json"));
        var bindings=JObject.Parse(File.ReadAllText(Root+"native-material-binding-manifest.json"));
        var assets=JObject.Parse(File.ReadAllText(Root+"native-material-texture-assets.json"));
        var compile=JObject.Parse(File.ReadAllText(Root+"native-material-compile-source.json"));
        var declared=((JArray)compile["pixelShaders"]).ToDictionary(x=>(string)x["hash"],x=>(JObject)x["capturedConstantBufferFloat4Counts"]);
        var vm=((JArray)vertex["draws"]).ToDictionary(x=>(int)x["draw"],x=>(JObject)x);
        var bm=((JArray)bindings["draws"]).ToDictionary(x=>(int)x["draw"],x=>(JObject)x);
        var textures=new Dictionary<string,Texture>();var ownedTextures=new List<Texture>();
        foreach(var row in assets["textures"]){var t=AssetDatabase.LoadAssetAtPath<Texture>((string)row["asset"]);if(!t)throw new Exception("Missing staged texture");textures[(string)row["absoluteRawAsset"]]=t;}
        int width=(int)fixture["width"],height=(int)fixture["height"];
        var targets=new RenderTexture[4];var ids=new RenderTargetIdentifier[4];RenderTexture depth=null;
        var shader=AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);if(!shader||!shader.isSupported)throw new Exception("Native shader unsupported");
        var result=new JArray();string output=Root+"native-pixel-replay/"+(wrongWinding?"unity-winding-negative":"unity-gpu");Directory.CreateDirectory(output);
        try
        {
            for(int i=0;i<4;i++)
            {
                var d=new RenderTextureDescriptor(width,height){graphicsFormat=GraphicsFormat.R32G32B32A32_SFloat,depthStencilFormat=GraphicsFormat.None,msaaSamples=1,mipCount=1};
                targets[i]=new RenderTexture(d){hideFlags=HideFlags.HideAndDontSave};if(!targets[i].Create())throw new Exception("MRT creation failed");ids[i]=new RenderTargetIdentifier(targets[i]);
            }
            var dep=new RenderTextureDescriptor(width,height){graphicsFormat=GraphicsFormat.None,depthStencilFormat=GraphicsFormat.D32_SFloat,msaaSamples=1,mipCount=1};
            depth=new RenderTexture(dep){hideFlags=HideFlags.HideAndDontSave};if(!depth.Create())throw new Exception("Depth creation failed");
            foreach(JObject test in fixture["cases"])
            {
                if((int)test["samplerFixture"]!=3)continue;
                int draw=(int)test["draw"];var v=vm[draw];var b=bm[draw];var header=(string)v["indexHeader"];
                if(wrongWinding && draw!=367 && draw!=375 && draw!=384)continue;
                int first=int.Parse(System.Text.RegularExpressions.Regex.Match(header,@"first index: (\d+)").Groups[1].Value);
                int count=int.Parse(System.Text.RegularExpressions.Regex.Match(header,@"index count: (\d+)").Groups[1].Value);
                var mesh=BuildMesh(v,first,count);var mat=new Material(shader){hideFlags=HideFlags.HideAndDontSave};var cb=new GraphicsBuffer[5];GraphicsBuffer scene=null;var cmd=new CommandBuffer{name="Remielle native material fixture"};
                try
                {
                    if(draw==374)mat.EnableKeyword("CAPTURED_S3_HANDLE_B");
                    for(int i=0;i<5;i++)
                    {
                        var raw=File.ReadAllBytes(Acq+(string)b["constantBuffers"]["cb"+i]);var values=ReadStructs<Vector4>(raw);
                        int required=(int)declared[(string)test["ps"]]["cb"+i];if(values.Length<required)throw new Exception("Captured constant buffer is shorter than shader declaration");
                        // The unified allocation is wider than some original PS declarations.
                        // Only its unreferenced tail is padded; every consumed byte is original.
                        var upload=new Vector4[Counts[i]];Array.Copy(values,upload,Math.Min(values.Length,upload.Length));
                        cb[i]=new GraphicsBuffer(GraphicsBuffer.Target.Constant,Counts[i],16);cb[i].SetData(upload);mat.SetConstantBuffer("CapturedCb"+i,cb[i],0,Counts[i]*16);
                    }
                    var entity=ReadStructs<Entity>(File.ReadAllBytes((string)v["sceneBuffer"]));scene=new GraphicsBuffer(GraphicsBuffer.Target.Structured,entity.Length,128);scene.SetData(entity);mat.SetBuffer("_CapturedT1",scene);
                    foreach(JObject tex in test["resources"])
                    {
                        string path=(string)tex["path"];if(!textures.TryGetValue(path,out var t)){if((int)tex["format"]!=56)throw new Exception("Unknown fixture texture");t=LoadDepth(tex);textures.Add(path,t);ownedTextures.Add(t);}
                        mat.SetTexture("_Captured"+((string)tex["slot"]).ToUpperInvariant(),t);
                    }
                    int pass=mat.FindPass("PS_"+(string)test["ps"]);if(pass<0)throw new Exception("PS pass missing");
                    cmd.SetRenderTarget(ids,new RenderTargetIdentifier(depth));cmd.SetViewport(new Rect(0,0,width,height));cmd.ClearRenderTarget(true,true,new Color(-65504,-65504,-65504,-65504),1);
                    // Captured IB winding uses native D3D11 clockwise front faces.
                    // Unity defaults to counterclockwise; Cull Off does not fix SV_IsFrontFace.
                    cmd.SetInvertCulling(!wrongWinding);cmd.SetViewProjectionMatrices(Matrix4x4.identity,Matrix4x4.identity);cmd.DrawMesh(mesh,Matrix4x4.identity,mat,0,pass);
                    cmd.IssuePluginEvent(GetRemielleStateObserver(),wrongWinding?-draw:draw);cmd.SetInvertCulling(false);Graphics.ExecuteCommandBuffer(cmd);
                    var pixels=new List<uint>();float[] outputValues=null;
                    for(int target=0;target<4;target++)
                    {
                        var request=AsyncGPUReadback.Request(targets[target],0);request.WaitForCompletion();if(request.hasError)throw new Exception("GPU readback failed");var values=request.GetData<float>();
                        if(values.Length!=width*height*4)throw new Exception("GPU readback size mismatch");
                        if(target==0){for(int p=0;p<width*height;p++)if(values[p*4]!=-65504)pixels.Add((uint)p);outputValues=new float[pixels.Count*16];}
                        for(int p=0;p<pixels.Count;p++)for(int channel=0;channel<4;channel++)outputValues[p*16+target*4+channel]=values[(int)pixels[p]*4+channel];
                    }
                    string stem=output+"/"+(string)test["id"];var bytes=new byte[outputValues.Length*4];Buffer.BlockCopy(outputValues,0,bytes,0,bytes.Length);File.WriteAllBytes(stem+".f32",bytes);
                    var pi=pixels.ToArray();bytes=new byte[pi.Length*4];Buffer.BlockCopy(pi,0,bytes,0,bytes.Length);File.WriteAllBytes(stem+".u32",bytes);
                    string state=output+"/"+draw+"-state.json";
                    result.Add(new JObject{{"draw",draw},{"id",(string)test["id"]},{"pixels",pixels.Count},{"output",stem+".f32"},{"outputSha256",Sha(stem+".f32")},{"indices",stem+".u32"},{"indicesSha256",Sha(stem+".u32")},{"state",state},{"stateSha256",Sha(state)}});
                    Debug.Log("UNITY_NATIVE_MATERIAL_READBACK draw="+draw+" pixels="+pixels.Count);
                }
                finally{cmd.Release();scene?.Dispose();foreach(var x in cb)x?.Dispose();UnityEngine.Object.DestroyImmediate(mat);UnityEngine.Object.DestroyImmediate(mesh);}
            }
            var plugin=(PluginImporter)AssetImporter.GetAtPath("Assets/Plugins/Editor/x86_64/RemielleUnityStateProbe.dll");
            var files=new JArray();foreach(string file in new[]{"Assets/Editor/RemielleNativeMaterialGpuAudit.cs","Assets/Plugins/Editor/x86_64/RemielleUnityStateProbe.dll",ShaderPath})files.Add(new JObject{{"path",Path.GetFullPath(file)},{"sha256",Sha(file)}});
            foreach(var row in compile["vertexShaders"].Concat(compile["pixelShaders"])){string file=(string)row["generatedInclude"];files.Add(new JObject{{"path",file},{"sha256",Sha(file)}});}
            foreach(var row in assets["textures"]){string file=(string)row["asset"];files.Add(new JObject{{"path",Path.GetFullPath(file)},{"sha256",Sha(file)}});}
            File.WriteAllText(Root+(wrongWinding?"unity-native-material-winding-negative.json":"unity-native-material-readback.json"),new JObject{
                ["schema"]="remielle-unity-native-material-readback-v1",["device"]=SystemInfo.graphicsDeviceName,["api"]=SystemInfo.graphicsDeviceType.ToString(),["reversedZ"]=SystemInfo.usesReversedZBuffer,["uvStartsAtTop"]=SystemInfo.graphicsUVStartsAtTop,
                ["shaderSha256"]=Sha(ShaderPath),["fixtureManifestSha256"]=Sha(Root+"native-pixel-replay/unity-reference/manifest.json"),["draws"]=result,
                ["windingNegativeControl"]=wrongWinding,["implementationFiles"]=files,["probeEditorCompatible"]=plugin.GetCompatibleWithEditor(),["probeAnyPlatform"]=plugin.GetCompatibleWithAnyPlatform(),["probeStandaloneWindows64"]=plugin.GetCompatibleWithPlatform(BuildTarget.StandaloneWindows64),
                ["boundary"]="Actual Unity VS/PS, staged textures and captured constant/vertex buffers. Uses measured Unity sampler mode 3 and synthetic main-shadow fixture; numerical verification is separate. Game sampler descriptors remain unknown."
            }.ToString());
        }
        finally
        {
            foreach(var t in targets)if(t){t.Release();UnityEngine.Object.DestroyImmediate(t);}if(depth){depth.Release();UnityEngine.Object.DestroyImmediate(depth);}
            foreach(var t in ownedTextures)UnityEngine.Object.DestroyImmediate(t);
        }
    }
}
