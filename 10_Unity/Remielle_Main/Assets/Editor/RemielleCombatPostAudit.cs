using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class RemielleCombatPostAudit
{
    const string Out=RemielleRenderReviewBuild.Out;
    internal static Texture2D Load(JToken item)
    {
        int format=(int)item["dxgi"],w=(int)item["width"],h=(int)item["height"];
        var t=new Texture2D(w,h,format==10||format==26?TextureFormat.RGBAHalf:format==28||format==29?TextureFormat.RGBA32:TextureFormat.RGBAFloat,false,format!=29);
        if(format==10||format==28||format==29){var b=File.ReadAllBytes((string)item["source"]);int offset=(int)item["offset"];var pixels=new byte[b.Length-offset];Buffer.BlockCopy(b,offset,pixels,0,pixels.Length);t.LoadRawTextureData(pixels);}
        else if(format==26){var bytes=File.ReadAllBytes((string)item["path"]);var packed=new byte[bytes.Length/2];for(int i=0;i<bytes.Length/4;i++){ushort value=Mathf.FloatToHalf(BitConverter.ToSingle(bytes,i*4));packed[i*2]=(byte)value;packed[i*2+1]=(byte)(value>>8);}t.LoadRawTextureData(packed);}
        else t.LoadRawTextureData(File.ReadAllBytes((string)item["path"]));
        t.Apply();t.wrapMode=TextureWrapMode.Clamp;t.filterMode=FilterMode.Bilinear;return t;
    }
    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        AssetDatabase.Refresh();
        var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/CapturedCombatPost.shader");
        if(!shader||!shader.isSupported||ShaderUtil.ShaderHasError(shader))throw new Exception("Combat post shader unavailable");
        var data=JsonUtility.FromJson<CapturedBloomData>(File.ReadAllText(Out+"/captured-combat-post.json"));
        var fixtures=JObject.Parse(File.ReadAllText(Out+"/combat-post-fixtures.json"));
        var material=new Material(shader);var previous=RenderTexture.active;bool srgb=GL.sRGBWrite;var results=new JArray();bool pass=true;
        try{foreach(var row in fixtures["fixtures"]){
            int index=(int)row["step"];var step=data.steps[index];bool final=step.call==666;
            var inputs=new System.Collections.Generic.List<Texture2D>();
            var rt=new RenderTexture(step.width,step.height,0,final?RenderTextureFormat.ARGB32:RenderTextureFormat.RGB111110Float,final?RenderTextureReadWrite.sRGB:RenderTextureReadWrite.Linear);
            var readback=final?null:new RenderTexture(step.width,step.height,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            var pixels=new Texture2D(step.width,step.height,final?TextureFormat.RGBA32:TextureFormat.RGBAFloat,false,true);
            var expected=new Texture2D(step.width,step.height,TextureFormat.RGBAFloat,false,true);expected.LoadRawTextureData(File.ReadAllBytes((string)row["expected"]["path"]));expected.Apply();
            try{
                foreach(var input in row["inputs"]){var t=Load(input);inputs.Add(t);material.SetTexture("_T"+(int)input["slot"],t);}
                material.SetVectorArray("V0",RemielleReviewBloom.Vectors(step.vertex));material.SetVectorArray("P0",RemielleReviewBloom.Vectors(step.pixel));material.SetVectorArray("P1",RemielleReviewBloom.Vectors(step.pixel1));
                rt.Create();Graphics.SetRenderTarget(rt);GL.sRGBWrite=rt.sRGB;GL.Clear(false,true,Color.clear);
                if(!material.SetPass(step.passIndex))throw new Exception("Combat post pass unavailable");Graphics.DrawProceduralNow(MeshTopology.Triangles,6);
                if(!final)Graphics.Blit(rt,readback);RenderTexture.active=final?rt:readback;
                pixels.ReadPixels(new Rect(0,0,step.width,step.height),0,0);pixels.Apply();
                var a=pixels.GetPixels();var e=expected.GetPixels();int left=0,top=0,right=step.width,bottom=step.height;
                if(index<12){var v=RemielleReviewBloom.Vectors(step.vertex)[171];left=Mathf.RoundToInt((v.z-v.x+1)*.5f*step.width);right=Mathf.RoundToInt((v.z+v.x+1)*.5f*step.width);top=Mathf.RoundToInt((1-v.w-v.y)*.5f*step.height);bottom=Mathf.RoundToInt((1-v.w+v.y)*.5f*step.height);}
                float max=0,units=0;double sum=0;int count=0,nonzero=0,outliers=0;
                for(int y=top;y<bottom;y++)for(int x=left;x<right;x++)for(int c=0;c<3;c++){
                    // Native intermediate fullscreen passes invert projection Y;
                    // the final mesh blit writes the display-oriented DDS instead.
                    float truth=e[(final?step.height-1-y:y)*step.width+x][c],actual=a[y*step.width+x][c];
                    float error=Mathf.Abs(truth-actual);float quantum=final?1f/255:Mathf.Pow(2,Mathf.Max(-14,Mathf.Floor(Mathf.Log(Mathf.Max(truth,1e-10f),2)))-(c==2?5:6));
                    if(!float.IsFinite(actual))throw new Exception("Nonfinite combat post output");
                    max=Mathf.Max(max,error);units=Mathf.Max(units,error/quantum);sum+=error;count++;if(truth>.01f)nonzero++;if(error/quantum>2.1f)outliers++;
                }
                // Some coarse FX atlas regions are legitimately near black.
                // Compare every written value; only the source step needs a content gate.
                bool ok=count>0&&outliers==0&&(index!=0||nonzero>20);pass&=ok;
                results.Add(new JObject{["call"]=step.call,["pass"]=ok,["values"]=count,["nonzeroValues"]=nonzero,["maxAbsoluteError"]=max,["meanAbsoluteError"]=sum/count,["maxNativeFormatUnits"]=units,["outliers"]=outliers,["expectedFlipYForReadPixels"]=final});
                File.WriteAllBytes(Out+"/combat-post-fixtures/actual-"+step.call+".raw",pixels.GetRawTextureData());
                if(final)File.WriteAllBytes(Out+"/combat-post-native-replay.png",pixels.EncodeToPNG());
                Debug.Log("Combat post replay "+step.call+": "+ok+" / units="+units+" / outliers="+outliers);
            }finally{RenderTexture.active=previous;foreach(var t in inputs)UnityEngine.Object.DestroyImmediate(t);rt.Release();if(readback){readback.Release();UnityEngine.Object.DestroyImmediate(readback);}UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(pixels);UnityEngine.Object.DestroyImmediate(expected);}
        }}finally{RenderTexture.active=previous;GL.sRGBWrite=srgb;UnityEngine.Object.DestroyImmediate(material);}
        File.WriteAllText(Out+"/combat-post-gpu-verification.json",new JObject{["pass"]=pass,["utc"]=DateTime.UtcNow.ToString("O"),["device"]=SystemInfo.graphicsDeviceName,["draws"]=results,["scope"]="Captured FX Bloom and final UberPost, all written RGB pixels. Not full character shading or earlier HighQualityBloom."}.ToString());
        if(!pass)throw new Exception("Combat post native replay mismatch");
        Debug.Log("REMIELLE_COMBAT_POST_GPU_VERIFIED");
    }
}
