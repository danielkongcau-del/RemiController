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

// Captured UI fixtures only; every mesh, material and extra texture is in memory.
public static class RemielleNativeUIMaterialGpuAudit
{
    const string Root="E:/ZZZ/local-only/RemielleRenderingReview/20260905/ui-native-material/";
    const string WriteRoot = "E:/ZZZ/ZCode/90_Builds/RenderingReview/20260905/ui-native-material/"; // D1-c 写根（读根保留 A 类）
    const string ShaderPath="Assets/RenderingReview/Shader/GeneratedNativeUI/CapturedNativeUIMaterial.shader";
    static readonly int[] Counts={204,29,41,119};
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
    static Mesh BuildMesh(JObject row)
    {
        int vertices=(int)row["vertices"],first=(int)row["firstIndex"],count=(int)row["indexCount"];
        var streams=new Dictionary<int,byte[]>();var strides=new Dictionary<int,int>();
        foreach(var vb in row["vertexBuffers"]){int slot=(int)vb["slot"];streams[slot]=File.ReadAllBytes((string)vb["path"]);strides[slot]=(int)vb["stride"];}
        var mesh=new Mesh{name="UIFixture_"+row["id"],indexFormat=IndexFormat.UInt32,hideFlags=HideFlags.HideAndDontSave};
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
    static Texture LoadExtra(JObject row)
    {
        int w=(int)row["width"],h=(int)row["height"],format=(int)row["format"];
        int mips=(int)row["mips"];if(mips<1||(int)row["layers"]!=1)throw new Exception("Unsupported extra texture layout");
        if(format!=28&&format!=29&&format!=56)throw new Exception("Unsupported extra texture format");
        var bytes=File.ReadAllBytes((string)row["path"]);int header=bytes[84]=='D'&&bytes[85]=='X'&&bytes[86]=='1'&&bytes[87]=='0'?148:128;
        int expected=0;for(int mip=0;mip<mips;mip++)expected+=Math.Max(1,w>>mip)*Math.Max(1,h>>mip)*(format==56?2:4);
        if(bytes.Length-header!=expected)throw new Exception("Extra texture size mismatch");
        var t=new Texture2D(w,h,format==56?TextureFormat.R16:TextureFormat.RGBA32,mips,format!=29){hideFlags=HideFlags.HideAndDontSave};
        var raw=new byte[bytes.Length-header];Buffer.BlockCopy(bytes,header,raw,0,raw.Length);t.LoadRawTextureData(raw);t.Apply(false,false);return t;
    }
    public static void Run()
    {
        if(SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Direct3D11)throw new Exception("D3D11 required");
        var m=JObject.Parse(File.ReadAllText(Root+"manifest.json"));var generated=JObject.Parse(File.ReadAllText(Root+"unity-shader.json"));
        var textures=new Dictionary<string,Texture>();var ownedTextures=new List<Texture>();
        int width=(int)m["width"],height=(int)m["height"];
        var targets=new RenderTexture[4];var ids=new RenderTargetIdentifier[4];RenderTexture depth=null;
        var shader=AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);if(!shader||!shader.isSupported)throw new Exception("UI native shader unsupported");
        var results=new JArray();var messages=new JArray();string output=WriteRoot+"unity-gpu";Directory.CreateDirectory(output);
        try
        {
            foreach(JObject tex in m["textures"])
            {
                string asset=(string)tex["asset"];Texture t;
                if(asset!=null)t=AssetDatabase.LoadAssetAtPath<Texture>(asset);
                else {t=LoadExtra(tex);ownedTextures.Add(t);}
                if(!t)throw new Exception("Missing texture");textures.Add((string)tex["path"],t);
            }
            for(int i=0;i<4;i++)
            {
                var d=new RenderTextureDescriptor(width,height){graphicsFormat=GraphicsFormat.R32G32B32A32_SFloat,depthStencilFormat=GraphicsFormat.None,msaaSamples=1,mipCount=1};
                targets[i]=new RenderTexture(d){hideFlags=HideFlags.HideAndDontSave};if(!targets[i].Create())throw new Exception("MRT creation failed");ids[i]=new RenderTargetIdentifier(targets[i]);
            }
            var dep=new RenderTextureDescriptor(width,height){graphicsFormat=GraphicsFormat.None,depthStencilFormat=GraphicsFormat.D32_SFloat,msaaSamples=1,mipCount=1};
            depth=new RenderTexture(dep){hideFlags=HideFlags.HideAndDontSave};if(!depth.Create())throw new Exception("Depth creation failed");
            foreach(JObject row in m["cases"])
            {
                var mesh=BuildMesh(row);var mat=new Material(shader){hideFlags=HideFlags.HideAndDontSave};var cb=new GraphicsBuffer[4];GraphicsBuffer scene=null;var cmd=new CommandBuffer{name="Remielle UI native fixture"};
                try
                {
                    if((int)row["samplers"]["s2"]["fixtureGroup"]==1)mat.EnableKeyword("UI_S2_FIXTURE_B");
                    for(int i=0;i<4;i++)
                    {
                        var values=ReadStructs<Vector4>(File.ReadAllBytes((string)row["constantBuffers"][i]["path"]));
                        int required=(int)generated["pixelDeclarations"][(string)row["ps"]][i.ToString()];
                        if(values.Length<required)throw new Exception("Captured CB shorter than declaration");
                        // Only padding outside each original declaration may be synthesized.
                        var upload=new Vector4[Counts[i]];Array.Copy(values,upload,Math.Min(values.Length,upload.Length));
                        cb[i]=new GraphicsBuffer(GraphicsBuffer.Target.Constant,Counts[i],16);cb[i].SetData(upload);mat.SetConstantBuffer("UiCb"+i,cb[i],0,Counts[i]*16);
                    }
                    var entities=ReadStructs<Entity>(File.ReadAllBytes((string)row["sceneBuffer"]));scene=new GraphicsBuffer(GraphicsBuffer.Target.Structured,entities.Length,128);scene.SetData(entities);mat.SetBuffer("_UiT1",scene);
                    foreach(var tex in row["resources"])mat.SetTexture("_Ui"+((string)tex["slot"]).ToUpperInvariant(),textures[(string)tex["path"]]);
                    int pass=mat.FindPass("PS_"+(string)row["ps"]);if(pass<0)throw new Exception("Missing PS pass");
                    cmd.SetRenderTarget(ids,new RenderTargetIdentifier(depth));cmd.SetViewport(new Rect(0,0,width,height));cmd.ClearRenderTarget(true,true,new Color(-65504,-65504,-65504,-65504),1);
                    cmd.SetInvertCulling(true);cmd.SetViewProjectionMatrices(Matrix4x4.identity,Matrix4x4.identity);cmd.DrawMesh(mesh,Matrix4x4.identity,mat,0,pass);cmd.SetInvertCulling(false);Graphics.ExecuteCommandBuffer(cmd);
                    var pixels=new List<uint>();float[] outputValues=null;
                    for(int target=0;target<4;target++)
                    {
                        var request=AsyncGPUReadback.Request(targets[target],0);request.WaitForCompletion();if(request.hasError)throw new Exception("GPU readback failed");var values=request.GetData<float>();
                        if(values.Length!=width*height*4)throw new Exception("GPU readback size mismatch");
                        if(target==0){for(int p=0;p<width*height;p++)if(values[p*4]!=-65504)pixels.Add((uint)p);outputValues=new float[pixels.Count*16];}
                        for(int p=0;p<pixels.Count;p++)for(int ch=0;ch<4;ch++)outputValues[p*16+target*4+ch]=values[(int)pixels[p]*4+ch];
                    }
                    string stem=output+"/"+(string)row["id"];var bytes=new byte[outputValues.Length*4];Buffer.BlockCopy(outputValues,0,bytes,0,bytes.Length);File.WriteAllBytes(stem+".f32",bytes);
                    var pi=pixels.ToArray();bytes=new byte[pi.Length*4];Buffer.BlockCopy(pi,0,bytes,0,bytes.Length);File.WriteAllBytes(stem+".u32",bytes);
                    results.Add(new JObject{{"id",(string)row["id"]},{"pixels",pixels.Count},{"output",stem+".f32"},{"outputSha256",Sha(stem+".f32")},{"indices",stem+".u32"},{"indicesSha256",Sha(stem+".u32")}});
                    Debug.Log("UNITY_NATIVE_UI_READBACK "+row["id"]+" pixels="+pixels.Count);
                }
                finally{cmd.Release();scene?.Dispose();foreach(var x in cb)x?.Dispose();UnityEngine.Object.DestroyImmediate(mat);UnityEngine.Object.DestroyImmediate(mesh);}
            }
            foreach(var message in ShaderUtil.GetShaderMessages(shader)){messages.Add(new JObject{{"severity",message.severity.ToString()},{"message",message.message},{"line",message.line}});if(message.severity.ToString()=="Error")throw new Exception(message.message);}
            var files=new JArray(generated["files"].Select(x=>x.DeepClone()));
            foreach(string p in new[]{"Assets/Editor/RemielleNativeUIMaterialGpuAudit.cs"})files.Add(new JObject{{"path",Path.GetFullPath(p)},{"sha256",Sha(p)}});
            foreach(var t in m["textures"]){string asset=(string)t["asset"];if(asset!=null)files.Add(new JObject{{"path",Path.GetFullPath(asset)},{"sha256",Sha(asset)}});}
            File.WriteAllText(WriteRoot+"unity-readback.json",new JObject{
                ["schema"]="remielle-ui-native-unity-readback-v1",["device"]=SystemInfo.graphicsDeviceName,["api"]=SystemInfo.graphicsDeviceType.ToString(),["reversedZ"]=SystemInfo.usesReversedZBuffer,
                ["manifestSha256"]=Sha(Root+"manifest.json"),["draws"]=results,["implementationFiles"]=files,["shaderMessages"]=messages,
                ["boundary"]="Captured display/store base draws only; shared explicit sampler fixtures, one mip0-only red texture. No full UI sequence or dynamic model binding is claimed."
            }.ToString());
        }
        finally{foreach(var t in targets)if(t){t.Release();UnityEngine.Object.DestroyImmediate(t);}if(depth){depth.Release();UnityEngine.Object.DestroyImmediate(depth);}foreach(var t in ownedTextures)UnityEngine.Object.DestroyImmediate(t);}
    }
}
