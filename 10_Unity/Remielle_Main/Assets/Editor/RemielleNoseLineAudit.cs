using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEditor;

// Verifies the captured StandardFace nose-line branch on the derived review
// materials.  The legacy HoyoToon approximation remains available only as an
// isolation control; source materials and the protected model prefab are never
// edited by this audit.
public static class RemielleNoseLineAudit
{
    const int Width=1280,Height=720;
    const string Property="_ReviewNativeNoseLine";
    static readonly float[] Yaws={-70,-55,-40,0,40,55,70};

    public static void Run()
    {
        string folder=RemielleRenderReviewBuild.Out+"/nose-line-isolation";
        Directory.CreateDirectory(folder);
        var review=UnityEngine.Object.FindFirstObjectByType<RemielleLightingReview>();
        if(!review)throw new Exception("Lighting review scene missing for nose-line audit");
        review.showUI=false;review.capturedEnvironment=true;review.nativeEnvironment=true;review.selfShadows=false;
        review.ApplyProfile(0);review.SetCameraPreset(2);
        review.model.Sample("Idle_Loop",Mathf.Min(.42f,review.model.nativeAnimation["Idle_Loop"].length*.25f));
        review.bloom.enabled=false;review.highQualityBloom.enabled=false;review.combatPostProcess.enabled=false;

        var faceMaterials=review.model.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .SelectMany(r=>r.sharedMaterials).Where(m=>m&&m.HasProperty("_MaterialType")&&Mathf.Approximately(m.GetFloat("_MaterialType"),1))
            .Distinct().ToArray();
        if(faceMaterials.Length<1||faceMaterials.Any(m=>!m.HasProperty(Property)))
            throw new Exception("Expected derived face-family materials with native nose-line control");
        var allMaterials=review.model.GetComponentsInChildren<SkinnedMeshRenderer>(true).SelectMany(r=>r.sharedMaterials).Where(m=>m).Distinct().ToArray();
        var smoothness=new JArray();
        foreach(var material in allMaterials)
        {
            var values=new JArray();
            for(int i=1;i<=5;i++)
            {
                string name=i==1?"_AlbedoSmoothness":"_AlbedoSmoothness"+i;
                if(material.HasProperty(name))values.Add(material.GetFloat(name));
            }
            if(values.Count!=5)throw new Exception("Missing material-ID smoothness slots on "+material.name);
            smoothness.Add(new JObject{{"material",material.name},{"values",values}});
        }

        var saved=faceMaterials.ToDictionary(m=>m,m=>m.GetFloat(Property));float savedYaw=review.cameraYaw;
        var rows=new JArray();int changedAngles=0,totalChanged=0;
        try
        {
            foreach(float yaw in Yaws)
            {
                review.cameraYaw=yaw;review.PositionCamera();
                foreach(var material in faceMaterials)material.SetFloat(Property,1);var native=Capture(review.reviewCamera);
                foreach(var material in faceMaterials)material.SetFloat(Property,0);var legacy=Capture(review.reviewCamera);
                try
                {
                    var row=Measure(yaw,native,legacy);rows.Add(row);
                    int changed=(int)row["changedPixels"];totalChanged+=changed;if(changed>0)changedAngles++;
                    if(yaw==-70||yaw==0)
                    {
                        SaveDisplay(native,folder+"/native-yaw"+yaw+".png");
                        SaveDisplay(legacy,folder+"/legacy-yaw"+yaw+".png");
                    }
                }
                finally{UnityEngine.Object.DestroyImmediate(native);UnityEngine.Object.DestroyImmediate(legacy);}
            }
        }
        finally
        {
            foreach(var pair in saved)pair.Key.SetFloat(Property,pair.Value);review.cameraYaw=savedYaw;review.PositionCamera();
        }
        bool pass=changedAngles>=1&&totalChanged>20;
        var report=new JObject{
            ["pass"]=pass,["utc"]=DateTime.UtcNow.ToString("O"),["device"]=SystemInfo.graphicsDeviceName,
            ["shader"]="Assets/RenderingReview/Shader/HoyoToonZenlessZoneZero.shader",
            ["nativeEvidence"]="miHoYo_Character_NapAvatarStandardFace_00__ps_013.hlsl lines 176-188",
            ["capturedThresholds"]=new JArray(new JObject{{"material","MAT_Remielle_Face"},{"horizontal",.85},{"lookDown",.5}},new JObject{{"material","MAT_Remielle_Eyebrow"},{"horizontal",.92},{"lookDown",.62}}),
            ["anglesTested"]=Yaws.Length,["anglesWithDifference"]=changedAngles,["totalChangedPixels"]=totalChanged,
            ["smoothnessSlots"]=smoothness,["comparisons"]=rows,
            ["scope"]="Derived review shader only. Native directional gate with the stable local HoyoToon alpha/color mask versus the previous ungated approximation; native intermediate complement color remains deferred with the downstream G-buffer/BRDF stages."
        };
        File.WriteAllText(folder+"/nose-line-isolation.json",report.ToString());
        if(!pass)throw new Exception("Native nose-line branch produced no measurable isolated response");
        Debug.Log("REMIELLE_NATIVE_NOSE_LINE_VERIFIED");
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

    static JObject Measure(float yaw,Texture2D native,Texture2D legacy)
    {
        var a=native.GetPixels();var b=legacy.GetPixels();int changed=0,minX=Width,minY=Height,maxX=-1,maxY=-1;double sum=0,max=0;
        for(int i=0;i<a.Length;i++)
        {
            double d=Math.Max(Math.Abs(a[i].r-b[i].r),Math.Max(Math.Abs(a[i].g-b[i].g),Math.Abs(a[i].b-b[i].b)));
            if(d<=1.0/4096.0)continue;
            changed++;sum+=d;max=Math.Max(max,d);int x=i%Width,y=i/Width;minX=Math.Min(minX,x);maxX=Math.Max(maxX,x);minY=Math.Min(minY,y);maxY=Math.Max(maxY,y);
        }
        return new JObject{{"yaw",yaw},{"changedPixels",changed},{"meanChangedMaxChannel",changed>0?sum/changed:0},{"maxChannelDifference",max},
            {"bounds",changed>0?new JArray(minX,minY,maxX,maxY):new JArray()},{"boundsAreaFraction",changed>0?(double)((maxX-minX+1)*(maxY-minY+1))/(Width*Height):0}};
    }

    static void SaveDisplay(Texture2D source,string path)
    {
        var input=source.GetPixels();var output=new Color32[input.Length];
        for(int i=0;i<input.Length;i++)output[i]=new Color(Mathf.LinearToGammaSpace(Mathf.Clamp01(input[i].r)),Mathf.LinearToGammaSpace(Mathf.Clamp01(input[i].g)),Mathf.LinearToGammaSpace(Mathf.Clamp01(input[i].b)),1);
        var image=new Texture2D(Width,Height,TextureFormat.RGBA32,false);
        try{image.SetPixels32(output);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());}
        finally{UnityEngine.Object.DestroyImmediate(image);}
    }
}
