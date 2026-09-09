using System;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Compare the port to captured native draw outputs, not to a copy of its equations.
public static class RemielleBloomAudit
{
    const string Out=RemielleRenderReviewBuild.Out;
    static Texture2D Load(JToken item,bool native=false)
    {
        Texture2D t;
        if(native&&!(bool)item["packed"]){
            t=new Texture2D((int)item["width"],(int)item["height"],TextureFormat.RGBAHalf,false,true);
            var file=File.ReadAllBytes((string)item["source"]);int offset=128;var pixels=new byte[file.Length-offset];Buffer.BlockCopy(file,offset,pixels,0,pixels.Length);t.LoadRawTextureData(pixels);
        }else{t=new Texture2D((int)item["width"],(int)item["height"],TextureFormat.RGBAFloat,false,true);t.LoadRawTextureData(File.ReadAllBytes((string)item["path"]));}
        t.Apply();t.filterMode=FilterMode.Bilinear;t.wrapMode=TextureWrapMode.Clamp;return t;
    }
    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var data=JsonUtility.FromJson<CapturedBloomData>(File.ReadAllText(Out+"/captured-bloom.json"));
        var fixtures=JObject.Parse(File.ReadAllText(Out+"/bloom-fixtures.json"));var rows=new JArray();bool pass=true;
        var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/CapturedMenuBloom.shader");
        if(shader==null||!shader.isSupported||ShaderUtil.ShaderHasError(shader))throw new Exception("Native Bloom shader unavailable");
        var material=new Material(shader);var active=RenderTexture.active;bool srgb=GL.sRGBWrite;
        try{foreach(var row in fixtures["fixtures"])
        {
            int i=(int)row["step"];var step=data.steps[i];var source=Load(row["source"],true);var expected=Load(row["expected"]);
            var second=row["second"]?.Type==JTokenType.Object?Load(row["second"],true):null;
            var target=new RenderTexture(step.width,step.height,0,RenderTextureFormat.RGB111110Float,RenderTextureReadWrite.Linear);
            var readback=new RenderTexture(step.width,step.height,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            var pixels=new Texture2D(step.width,step.height,TextureFormat.RGBAFloat,false,true);target.Create();
            try{
                Graphics.SetRenderTarget(target);GL.Clear(false,true,Color.clear);
                RemielleReviewBloom.Draw(material,step.passIndex,source,second,target,RemielleReviewBloom.Vectors(step.vertex),RemielleReviewBloom.Vectors(step.pixel),RemielleReviewBloom.Vectors(step.pixel1));
                // GPU expands packed floats before CPU readback, preserving subnormals.
                Graphics.Blit(target,readback);RenderTexture.active=readback;pixels.ReadPixels(new Rect(0,0,step.width,step.height),0,0);pixels.Apply();
                var actual=pixels.GetPixels();var truth=expected.GetPixels();
                File.WriteAllBytes(Out+"/bloom-fixtures/actual-"+step.call+".rgba32f",pixels.GetRawTextureData());
                int left=0,top=0,right=step.width,bottom=step.height;
                if(i<12){var v=RemielleReviewBloom.Vectors(step.vertex)[171];left=Mathf.RoundToInt((v.z-v.x+1)*.5f*step.width);right=Mathf.RoundToInt((v.z+v.x+1)*.5f*step.width);top=Mathf.RoundToInt((1-v.w-v.y)*.5f*step.height);bottom=Mathf.RoundToInt((1-v.w+v.y)*.5f*step.height);}
                double total=0;float max=0,maxUnits=0;int values=0,nonzero=0;
                for(int y=top;y<bottom;y++)for(int x=left;x<right;x++)for(int c=0;c<3;c++)
                {
                    float e=truth[y*step.width+x][c],a=actual[y*step.width+x][c];
                    float error=Mathf.Abs(e-a);float quantum=Mathf.Pow(2,Mathf.Max(-14,Mathf.Floor(Mathf.Log(Mathf.Max(e,1e-10f),2)))-(c==2?5:6));
                    max=Mathf.Max(max,error);maxUnits=Mathf.Max(maxUnits,error/quantum);total+=error;values++;if(e>.01f)nonzero++;
                }
                bool ok=nonzero>20&&maxUnits<=2.1f;
                rows.Add(new JObject{["call"]=step.call,["pass"]=ok,["values"]=values,["nonzeroValues"]=nonzero,["meanAbsoluteError"]=total/values,["maxAbsoluteError"]=max,["maxNativeFormatUnits"]=maxUnits,["readbackFlipY"]=false});pass&=ok;
            }finally{RenderTexture.active=active;target.Release();readback.Release();UnityEngine.Object.DestroyImmediate(readback);UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(source);UnityEngine.Object.DestroyImmediate(second);UnityEngine.Object.DestroyImmediate(expected);UnityEngine.Object.DestroyImmediate(pixels);}
        }}finally{RenderTexture.active=active;GL.sRGBWrite=srgb;UnityEngine.Object.DestroyImmediate(material);}
        File.WriteAllText(Out+"/bloom-gpu-verification.json",new JObject{["pass"]=pass,["device"]=SystemInfo.graphicsDeviceName,["tolerance"]="At most 2.1 representable R11/G11/B10 output steps per channel, all written pixels",["draws"]=rows}.ToString());
        if(!pass)throw new Exception("Captured Bloom GPU replay differs from native outputs");
        Debug.Log("REMIELLE_NATIVE_BLOOM_GPU_VERIFIED");
    }
}
