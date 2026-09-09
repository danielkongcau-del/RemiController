using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
public static class RuntimeLutAudit
{
    public static void Run()
    {
        RuntimeMaterialResources.Build();
        VerifyTo("E:/ZZZ/local-only/RemielleRuntimeRepair/20260904/lut-gpu-audit.json");
    }
    public static void VerifyTo(string output)
    {
        var tests=JObject.Parse(File.ReadAllText("Assets/SourceAssets/Runtime/lut_gpu_test_vectors.json"))["tests"];
        var rows=new JArray();
        var mat=new Material(Shader.Find("Hidden/Remielle/NativeRuntimeLut"));bool passed=true;
        foreach(var test in tests)
        {
            int count=((JArray)test["inputs"]).Count;
            var input=new Texture2D(count,1,TextureFormat.RGBAFloat,false,true){filterMode=FilterMode.Point};
            var pixels=new Color[count];for(int i=0;i<count;++i){var p=test["inputs"][i];pixels[i]=new Color((float)p[0],(float)p[1],(float)p[2],1);}
            input.SetPixels(pixels);input.Apply(false,false);
            var lut=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/SourceAssets/Runtime/"+test["texture"]+".asset");var a=test["parameters"];
            mat.SetTexture("_Lut2DTex",lut);mat.SetVector("_Lut2DTexParam",new Vector4((float)a[0],(float)a[1],(float)a[2],(float)a[3]));mat.SetFloat("_Post",(bool)test["post"]?1:0);
            var rt=new RenderTexture(count,1,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            bool srgb=GL.sRGBWrite;GL.sRGBWrite=false;Graphics.Blit(input,rt,mat);GL.sRGBWrite=srgb;
            var read=new Texture2D(count,1,TextureFormat.RGBAFloat,false,true);var previous=RenderTexture.active;RenderTexture.active=rt;read.ReadPixels(new Rect(0,0,count,1),0,0);read.Apply();RenderTexture.active=previous;
            var got=read.GetPixels();float max=0;
            for(int i=0;i<count;++i)for(int c=0;c<3;++c)max=Mathf.Max(max,Mathf.Abs(got[i][c]-(float)test["expected"][i][c]));
            var actual=new JArray();foreach(var color in got)actual.Add(new JArray(color.r,color.g,color.b));
            rows.Add(new JObject{["texture"]=(string)test["texture"],["samples"]=count,["gpuMaxError"]=max,["tolerance"]=(float)test["tolerance"],["actual"]=actual});
            UnityEngine.Object.DestroyImmediate(input);UnityEngine.Object.DestroyImmediate(read);rt.Release();UnityEngine.Object.DestroyImmediate(rt);
            if(max>(float)test["tolerance"])passed=false;
        }
        UnityEngine.Object.DestroyImmediate(mat);
        File.WriteAllText(output,new JObject{["pass"]=passed,["records"]=rows}.ToString());
        if(!passed)throw new InvalidOperationException("LUT GPU comparison requires review; see lut-gpu-audit.json");
        Debug.Log("NATIVE_LUT_GPU_VERIFIED "+rows.ToString());
    }
}
