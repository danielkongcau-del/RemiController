using System;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class RemielleHighQualityBloomAudit
{
    const string Out=RemielleRenderReviewBuild.Out;
    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        AssetDatabase.Refresh();
        var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/CapturedHighQualityBloom.shader");
        if(!shader||!shader.isSupported||ShaderUtil.ShaderHasError(shader))throw new Exception("HQ Bloom shader unavailable");
        var data=JsonUtility.FromJson<CapturedBloomData>(File.ReadAllText(Out+"/captured-hq-bloom.json"));
        var fixtures=JObject.Parse(File.ReadAllText(Out+"/hq-bloom-fixtures.json"));
        var material=new Material(shader);var previous=RenderTexture.active;bool srgb=GL.sRGBWrite,pass=true;var results=new JArray();
        try{foreach(var row in fixtures["fixtures"]){
            var step=data.steps[(int)row["step"]];
            var inputs=new List<Texture2D>();
            var rt=new RenderTexture(step.width,step.height,0,RenderTextureFormat.RGB111110Float,RenderTextureReadWrite.Linear);
            var readback=new RenderTexture(step.width,step.height,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            var pixels=new Texture2D(step.width,step.height,TextureFormat.RGBAFloat,false,true);
            var expected=new Texture2D(step.width,step.height,TextureFormat.RGBAFloat,false,true);
            expected.LoadRawTextureData(File.ReadAllBytes((string)row["expected"]["path"]));expected.Apply();
            var mask=row["writtenMask"]==null?null:File.ReadAllBytes((string)row["writtenMask"]);
            try{
                foreach(var input in row["inputs"]){var t=RemielleCombatPostAudit.Load(input);inputs.Add(t);material.SetTexture("_T"+(int)input["slot"],t);}
                material.SetVectorArray("P0",RemielleReviewBloom.Vectors(step.pixel,264));
                rt.Create();Graphics.SetRenderTarget(rt);GL.sRGBWrite=false;GL.Clear(false,true,Color.clear);
                if(!material.SetPass(step.passIndex))throw new Exception("HQ Bloom pass unavailable");
                Graphics.DrawProceduralNow(MeshTopology.Triangles,3);
                Graphics.Blit(rt,readback);RenderTexture.active=readback;
                pixels.ReadPixels(new Rect(0,0,step.width,step.height),0,0);pixels.Apply();
                var actual=pixels.GetPixels();var truth=expected.GetPixels();
                float max=0,units=0;double sum=0;int count=0,nonzero=0,outliers=0;
                for(int i=0;i<actual.Length;i++){
                    if(mask!=null&&mask[i]==0)continue;
                    for(int c=0;c<3;c++){
                        float a=actual[i][c],e=truth[i][c];
                        if(!float.IsFinite(a))throw new Exception("Nonfinite HQ Bloom output");
                        float error=Mathf.Abs(e-a),quantum=Mathf.Pow(2,Mathf.Max(-14,Mathf.Floor(Mathf.Log(Mathf.Max(e,1e-10f),2)))-(c==2?5:6));
                        max=Mathf.Max(max,error);units=Mathf.Max(units,error/quantum);sum+=error;count++;
                        if(e>.01f)nonzero++;if(error/quantum>2.1f)outliers++;
                    }
                }
                bool ok=count>0&&outliers==0&&(step.call!=536||nonzero>20);pass&=ok;
                results.Add(new JObject{["call"]=step.call,["pass"]=ok,["values"]=count,["nonzeroValues"]=nonzero,["maxAbsoluteError"]=max,["meanAbsoluteError"]=sum/count,["maxNativeFormatUnits"]=units,["outliers"]=outliers,["nativeStencilRegion"]=mask!=null});
                File.WriteAllBytes(Out+"/hq-bloom-fixtures/actual-"+step.call+".raw",pixels.GetRawTextureData());
                Debug.Log("HQ Bloom replay "+step.call+": "+ok+" / units="+units+" / outliers="+outliers);
            }finally{
                RenderTexture.active=previous;foreach(var t in inputs)UnityEngine.Object.DestroyImmediate(t);
                rt.Release();readback.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(readback);
                UnityEngine.Object.DestroyImmediate(pixels);UnityEngine.Object.DestroyImmediate(expected);
            }
        }}finally{RenderTexture.active=previous;GL.sRGBWrite=srgb;UnityEngine.Object.DestroyImmediate(material);}
        File.WriteAllText(Out+"/hq-bloom-gpu-verification.json",new JObject{["pass"]=pass,["utc"]=DateTime.UtcNow.ToString("O"),["device"]=SystemInfo.graphicsDeviceName,["draws"]=results,["scope"]="23 captured HQ Bloom draws. Every written RGB value, restricted to original depth-stencil regions for 556-558. Original scene and character shading are inputs, not reproduced here."}.ToString());
        if(!pass)throw new Exception("HighQualityBloom native replay mismatch");
        Debug.Log("REMIELLE_HQ_BLOOM_GPU_VERIFIED");
    }
}
