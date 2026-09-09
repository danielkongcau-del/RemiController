using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;

// Diagnostic only: separates the currently reconstructed forward material terms
// before combat Bloom/UberPost. Source materials and source animation are untouched.
public static class RemielleBrdfIsolationAudit
{
    const string Out=RemielleRenderReviewBuild.Out+"/brdf-isolation";
    const int Width=1280,Height=720;
    static readonly string[] Terms={"_SpecIntensity","_MatCap","_RimGlow","_Emission","_EnableLUT"};
    sealed class Saved
    {
        public Material material;
        public readonly Dictionary<string,float> values=new Dictionary<string,float>();
    }
    sealed class Variant
    {
        public string key,label;
        public Action<List<Saved>> apply;
        public Variant(string k,string l,Action<List<Saved>> a){key=k;label=l;apply=a;}
    }

    public static void Run()
    {
        Directory.CreateDirectory(Out);
        RemielleRenderReviewBuild.BuildScene();
        var review=UnityEngine.Object.FindFirstObjectByType<RemielleLightingReview>();
        if(!review)throw new Exception("Lighting review scene was not built");
        review.showUI=false;review.capturedEnvironment=true;review.nativeEnvironment=true;review.selfShadows=false;
        review.ApplyProfile(2);review.SetCameraPreset(0);
        review.model.Sample("Idle_Loop",Mathf.Min(.42f,review.model.nativeAnimation["Idle_Loop"].length*.25f));
        var camera=review.reviewCamera;
        review.bloom.enabled=false;review.highQualityBloom.enabled=false;review.combatPostProcess.enabled=false;
        var renderers=review.model.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .Where(r=>r.enabled&&r.gameObject.activeInHierarchy&&r.sharedMesh).ToArray();
        var materials=renderers.SelectMany(r=>r.sharedMaterials).Where(m=>m).Distinct().ToArray();
        var saved=materials.Select(m=>Save(m)).ToList();
        var mask=RenderMask(camera,renderers,AssetDatabase.LoadAssetAtPath<Shader>("Assets/RenderingReview/Shader/ReviewBloomMask.shader"));
        var maskPixels=ReadMask(mask);
        var variants=new[]{
            new Variant("baseline","All reconstructed terms",s=>{}),
            new Variant("no-specular","Specular disabled",s=>Set(s,"_SpecIntensity",0)),
            new Variant("no-matcap","MatCap disabled",s=>Set(s,"_MatCap",0)),
            new Variant("no-rim","Rim disabled",s=>Set(s,"_RimGlow",0)),
            new Variant("no-emission","Primary emission disabled",s=>Set(s,"_Emission",0)),
            new Variant("no-character-lut","Character LUT disabled",s=>Set(s,"_EnableLUT",0)),
            new Variant("no-specular-matcap","Specular and MatCap disabled",s=>{Set(s,"_SpecIntensity",0);Set(s,"_MatCap",0);}),
            new Variant("diffuse-shadow-only","Specular, MatCap, rim and emission disabled",s=>{Set(s,"_SpecIntensity",0);Set(s,"_MatCap",0);Set(s,"_RimGlow",0);Set(s,"_Emission",0);})
        };
        var rows=new JArray();bool pass=true;
        try{
            foreach(var variant in variants)
            {
                Restore(saved);variant.apply(saved);
                Texture2D pixels=Capture(camera);
                try{
                    var row=Measure(variant,pixels,maskPixels);
                    rows.Add(row);pass&=(bool)row["pass"];
                    File.WriteAllBytes(Out+"/"+variant.key+".rgba32f",pixels.GetRawTextureData());
                    SaveDisplay(pixels,Out+"/"+variant.key+".png");
                    Debug.Log("BRDF isolation "+variant.key+": p99="+row["lumaP99"]+" over.5="+row["fractionLumaAbove050"]);
                }finally{UnityEngine.Object.DestroyImmediate(pixels);}
            }
        }finally{
            Restore(saved);camera.targetTexture=null;mask.Release();UnityEngine.Object.DestroyImmediate(mask);
        }
        var materialRows=new JArray();
        foreach(var entry in saved)
        {
            var values=new JObject();foreach(var pair in entry.values)values[pair.Key]=pair.Value;
            materialRows.Add(new JObject{{"name",entry.material.name},{"values",values}});
        }
        File.WriteAllText(Out+"/brdf-isolation.json",new JObject{
            ["pass"]=pass,["utc"]=DateTime.UtcNow.ToString("O"),["device"]=SystemInfo.graphicsDeviceName,
            ["width"]=Width,["height"]=Height,["profile"]="combat",["clip"]="Idle_Loop",["clipTime"]=.42,
            ["characterMaskPixels"]=maskPixels.Count(v=>v),["materials"]=materialRows,["variants"]=rows,
            ["scope"]="Current reconstructed forward character output before HighQualityBloom and final UberPost. Variants isolate shader terms; they do not claim pose-aligned native equivalence."
        }.ToString());
        if(!pass)throw new Exception("BRDF isolation capture failed structural checks");
        Debug.Log("REMIELLE_BRDF_ISOLATION_CAPTURED");
    }

    static Saved Save(Material material)
    {
        var result=new Saved{material=material};
        foreach(string property in Terms)if(material.HasProperty(property))result.values[property]=material.GetFloat(property);
        return result;
    }
    static void Restore(List<Saved> saved)
    {foreach(var entry in saved)foreach(var pair in entry.values)entry.material.SetFloat(pair.Key,pair.Value);}
    static void Set(List<Saved> saved,string property,float value)
    {foreach(var entry in saved)if(entry.values.ContainsKey(property))entry.material.SetFloat(property,value);}

    static RenderTexture RenderMask(Camera camera,SkinnedMeshRenderer[] renderers,Shader shader)
    {
        if(!shader||!shader.isSupported||ShaderUtil.ShaderHasError(shader))throw new Exception("Review Bloom mask shader unavailable");
        var target=new RenderTexture(Width,Height,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear){filterMode=FilterMode.Point};target.Create();
        var command=new CommandBuffer{name="BRDF isolation character mask"};var copies=new Dictionary<Material,Material>();var slots=new List<Material>();
        try{
            command.SetRenderTarget(target);command.ClearRenderTarget(true,true,Color.clear);
            command.SetGlobalMatrix("_ReviewMaskVP",GL.GetGPUProjectionMatrix(camera.projectionMatrix,true)*camera.worldToCameraMatrix);
            foreach(var renderer in renderers)
            {
                renderer.GetSharedMaterials(slots);
                if(slots.Count!=renderer.sharedMesh.subMeshCount)throw new Exception("Material/submesh mismatch: "+renderer.name);
                for(int i=0;i<slots.Count;i++)
                {
                    var source=slots[i];if(!source)throw new Exception("Missing material: "+renderer.name);
                    if(source.GetFloat("_MaterialType")==3)continue;
                    if(!copies.TryGetValue(source,out var copy)){copy=new Material(shader);copy.CopyPropertiesFromMaterial(source);copies.Add(source,copy);}
                    int pass=copy.FindPass("Review Bloom Mask");if(pass<0)throw new Exception("Review Bloom mask pass missing");
                    command.DrawRenderer(renderer,copy,i,pass);
                }
            }
            Graphics.ExecuteCommandBuffer(command);return target;
        }catch{target.Release();UnityEngine.Object.DestroyImmediate(target);throw;}
        finally{command.Release();foreach(var copy in copies.Values)UnityEngine.Object.DestroyImmediate(copy);}
    }
    static bool[] ReadMask(RenderTexture mask)
    {
        var old=RenderTexture.active;var image=new Texture2D(Width,Height,TextureFormat.RGBA32,false,true);
        try{RenderTexture.active=mask;image.ReadPixels(new Rect(0,0,Width,Height),0,0);image.Apply();return image.GetPixels32().Select(c=>c.r>127).ToArray();}
        finally{RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(image);}
    }
    static Texture2D Capture(Camera camera)
    {
        var oldTarget=camera.targetTexture;var oldActive=RenderTexture.active;bool oldEnabled=camera.enabled;
        var target=new RenderTexture(Width,Height,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear){antiAliasing=1};target.Create();
        var image=new Texture2D(Width,Height,TextureFormat.RGBAFloat,false,true);
        try{camera.enabled=false;camera.targetTexture=target;camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,Width,Height),0,0);image.Apply();return image;}
        catch{UnityEngine.Object.DestroyImmediate(image);throw;}
        finally{camera.targetTexture=oldTarget;camera.enabled=oldEnabled;RenderTexture.active=oldActive;target.Release();UnityEngine.Object.DestroyImmediate(target);}
    }
    static JObject Measure(Variant variant,Texture2D image,bool[] mask)
    {
        var pixels=image.GetPixels();var luma=new List<float>();var peak=new List<float>();double sr=0,sg=0,sb=0,sl=0;int a45=0,a50=0,nonfinite=0;
        for(int i=0;i<pixels.Length;i++)if(mask[i])
        {
            var c=pixels[i];float y=.2126f*c.r+.7152f*c.g+.0722f*c.b,m=Mathf.Max(c.r,Mathf.Max(c.g,c.b));
            if(!float.IsFinite(y)||!float.IsFinite(m)){nonfinite++;continue;}
            luma.Add(y);peak.Add(m);sr+=c.r;sg+=c.g;sb+=c.b;sl+=y;if(y>.45f)a45++;if(y>.5f)a50++;
        }
        luma.Sort();peak.Sort();int count=luma.Count;bool pass=count>1000&&nonfinite==0;
        return new JObject{{"key",variant.key},{"label",variant.label},{"pass",pass},{"pixels",count},{"nonfinite",nonfinite},
            {"meanRgb",new JArray(sr/count,sg/count,sb/count)},{"meanLuma",sl/count},{"lumaP50",Q(luma,.5f)},{"lumaP90",Q(luma,.9f)},
            {"lumaP95",Q(luma,.95f)},{"lumaP99",Q(luma,.99f)},{"lumaP999",Q(luma,.999f)},{"maxLuma",Q(luma,1)},
            {"maxChannelP99",Q(peak,.99f)},{"maxChannelP999",Q(peak,.999f)},{"maxChannel",Q(peak,1)},
            {"fractionLumaAbove045",(double)a45/count},{"fractionLumaAbove050",(double)a50/count}};
    }
    static float Q(List<float> values,float q)
    {if(values.Count==0)return float.NaN;return values[Mathf.Clamp(Mathf.RoundToInt((values.Count-1)*q),0,values.Count-1)];}
    static void SaveDisplay(Texture2D source,string path)
    {
        var input=source.GetPixels();var output=new Color32[input.Length];
        for(int i=0;i<input.Length;i++)
        {
            Color c=input[i];float r=Mathf.Pow(Mathf.Max(0,c.r)/(1+Mathf.Max(0,c.r)),1/2.2f),g=Mathf.Pow(Mathf.Max(0,c.g)/(1+Mathf.Max(0,c.g)),1/2.2f),b=Mathf.Pow(Mathf.Max(0,c.b)/(1+Mathf.Max(0,c.b)),1/2.2f);
            output[i]=new Color(r,g,b,1);
        }
        var image=new Texture2D(Width,Height,TextureFormat.RGBA32,false);try{image.SetPixels32(output);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());}finally{UnityEngine.Object.DestroyImmediate(image);}
    }
}
