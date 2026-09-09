using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

[Serializable] public class RemielleCapturedLight
{
    public string key,label,frame;
    public float[] lightDirection,lightColor,sceneMainLightColor,ambient,postTints,sourceCamera,bodyLightDirection,hairLightDirection;
    public float exposure,sourceFov,bloomIntensity,bloomThreshold;
    public int sourceWidth,sourceHeight;
    public bool battle,environmentLayoutExact,overrideLight;
}
[Serializable] public class RemielleCapturedLights { public RemielleCapturedLight[] profiles; public string[] tintOrder; }

// Scene-scoped review controls. Source prefab/material assets are never edited.
[DefaultExecutionOrder(250)]
public class RemielleLightingReview : MonoBehaviour
{
    public TextAsset capturedLighting;
    public RemielleNativeAnimation model;
    public RemielleRuntimeProfile runtimeProfile;
    public RemielleCapturedCombatPost combatPostProcess;
    public RemielleHighQualityBloom highQualityBloom;
    public RemielleNativeGBufferProbe nativeGBufferProbe;
    public RemielleNativeDeferredProbe nativeDeferredProbe;
    public RemielleReviewBloom bloom;
    public Camera reviewCamera;
    public Light keyLight;
    public Shader diagnosticShader;
    public GameObject floor;
    public int profileIndex;
    public bool capturedEnvironment=true,nativeEnvironment=true,selfShadows=false,showUI=true;
    public bool followBodyMotion;
    public float cameraYaw,cameraHeight=1.19f,cameraDistance=2.7f,lightYawOffset;
    public string status="";
    RemielleCapturedLights data;
    Material[] lightingMaterials;
    Vector2 scroll;
    Transform bodyAnchor;Vector3 bodyAnchorStart;
    int savedAA;float savedShadowDistance;ShadowQuality savedShadows;ShadowResolution savedResolution;
    AmbientMode savedAmbientMode;Color savedAmbient;bool ownsSettings;
    public RemielleCapturedLights Data => data ??= JsonUtility.FromJson<RemielleCapturedLights>(capturedLighting.text);
    public RemielleCapturedLight Current => Data.profiles[profileIndex];
    public static Vector3 V(float[] a)=>new Vector3(a[0],a[1],a[2]);
    public static Color GammaColor(float[] a)=>new Color(a[0],a[1],a[2],1).gamma;
    public static void SetMaterialEnvironment(Material m,RemielleCapturedLights data,int index,bool captured,bool shadows,bool nativeAmbient=true)
    {
        if(m==null||!m.HasProperty("_MaterialType")||m.GetFloat("_MaterialType")==3)return;
        var p=data.profiles[index];
        for(int i=0;i<data.tintOrder.Length;i++)
            m.SetVector(data.tintOrder[i],captured?new Vector4(p.postTints[i*4],p.postTints[i*4+1],p.postTints[i*4+2],p.postTints[i*4+3]):Vector4.one);
        m.SetFloat("_UseSelfShadow",shadows?1:0);
        if(m.HasProperty("_ReviewNativeEnvironment")){
            m.SetFloat("_ReviewNativeEnvironment",nativeAmbient?1:0);
            m.SetVector("_ReviewAmbient",captured?V(p.ambient):new Vector3(.05087609f,.05087609f,.05087609f));
        }
        if(m.HasProperty("_ReviewOverrideLight")){
            m.SetFloat("_ReviewOverrideLight",p.overrideLight?1:0);
            m.SetVector("_ReviewBodyLight",V(p.bodyLightDirection));m.SetVector("_ReviewHairLight",V(p.hairLightDirection));
        }
    }
    void Start()
    {
        bodyAnchor=model.bones.First(link=>link.target&&link.target.name=="Bip001 Pelvis").target;bodyAnchorStart=bodyAnchor.position;
        savedAA=QualitySettings.antiAliasing;savedShadowDistance=QualitySettings.shadowDistance;
        savedShadows=QualitySettings.shadows;savedResolution=QualitySettings.shadowResolution;
        savedAmbientMode=RenderSettings.ambientMode;savedAmbient=RenderSettings.ambientLight;ownsSettings=true;
        QualitySettings.antiAliasing=4;QualitySettings.shadowDistance=20;
        QualitySettings.shadows=ShadowQuality.All;QualitySettings.shadowResolution=ShadowResolution.VeryHigh;
        ApplyProfile(profileIndex);SetCameraPreset(0);
    }
    public void ApplyProfile(int index)
    {
        profileIndex=Mathf.Clamp(index,0,Data.profiles.Length-1);var p=Current;
        runtimeProfile.Apply(p.battle);
        lightingMaterials=model.GetComponentsInChildren<SkinnedMeshRenderer>(true).SelectMany(r=>r.sharedMaterials).Where(m=>m!=null).Distinct().ToArray();
        foreach(var material in lightingMaterials)
            SetMaterialEnvironment(material,Data,profileIndex,capturedEnvironment,selfShadows,nativeEnvironment);
        var ambient=capturedEnvironment?p.ambient:new[]{.05087609f,.05087609f,.05087609f};
        RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=GammaColor(ambient);
        keyLight.color=GammaColor(p.lightColor);keyLight.intensity=1;
        keyLight.shadows=selfShadows?LightShadows.Soft:LightShadows.None;
        keyLight.shadowBias=.03f;keyLight.shadowNormalBias=.15f;keyLight.shadowStrength=1;keyLight.shadowCustomResolution=4096;
        combatPostProcess.available=p.battle;
        highQualityBloom.available=p.battle;
        bloom.available=!p.battle;
        status=p.label;UpdateLight();
    }
    public void UpdateLight()
    {
        var rotation=Quaternion.Euler(0,lightYawOffset,0);
        Vector3 toward=rotation*V(Current.overrideLight?Current.bodyLightDirection:Current.lightDirection).normalized;
        keyLight.transform.rotation=Quaternion.LookRotation(-toward,Vector3.up);
        if(lightingMaterials!=null)foreach(var material in lightingMaterials)if(material.HasProperty("_ReviewOverrideLight")){
            material.SetVector("_ReviewBodyLight",rotation*V(Current.bodyLightDirection));
            material.SetVector("_ReviewHairLight",rotation*V(Current.hairLightDirection));
        }
    }
    public void SetCameraPreset(int preset)
    {
        cameraYaw=0;
        if(preset==0){cameraHeight=Current.battle?1.05f:Current.sourceCamera[1];cameraDistance=Current.battle?4.5f:Mathf.Abs(Current.sourceCamera[2]);reviewCamera.fieldOfView=Current.battle?32:Current.sourceFov;}
        if(preset==1){cameraHeight=.83f;cameraDistance=4.8f;reviewCamera.fieldOfView=24;}
        if(preset==2){cameraHeight=1.44f;cameraDistance=.92f;reviewCamera.fieldOfView=32;}
        PositionCamera();
    }
    public void PositionCamera()
    {
        var center=new Vector3(0,cameraHeight,0);
        if(followBodyMotion&&bodyAnchor)center+=bodyAnchor.position-bodyAnchorStart;
        reviewCamera.transform.position=center+Quaternion.Euler(0,cameraYaw,0)*(Vector3.forward*cameraDistance);
        reviewCamera.transform.LookAt(center);
    }
    void LateUpdate(){PositionCamera();UpdateLight();}
    void OnGUI()
    {
        if(!showUI)return;
        GUILayout.BeginArea(new Rect(12,12,245,Mathf.Max(200,Screen.height-24)),GUI.skin.box);
        scroll=GUILayout.BeginScrollView(scroll);
        GUILayout.Label("Remielle / Lighting review");
        for(int i=0;i<Data.profiles.Length;i++)if(GUILayout.Button((i==profileIndex?"> ":"")+Data.profiles[i].label)){ApplyProfile(i);SetCameraPreset(0);}
        bool env=GUILayout.Toggle(capturedEnvironment,"Captured environment tints");
        bool response=GUILayout.Toggle(nativeEnvironment,"Original ambient response");
        bool sh=GUILayout.Toggle(selfShadows,"Geometry / ground shadows");
        if(env!=capturedEnvironment||sh!=selfShadows||response!=nativeEnvironment){capturedEnvironment=env;selfShadows=sh;nativeEnvironment=response;ApplyProfile(profileIndex);}
        floor.SetActive(GUILayout.Toggle(floor.activeSelf,"Show ground"));
        if(bloom.available)bloom.amount=GUILayout.Toggle(bloom.amount>0,"Captured menu Bloom")?1:0;
        else {
            highQualityBloom.effectEnabled=GUILayout.Toggle(highQualityBloom.effectEnabled,"Captured combat HQ Bloom");
            combatPostProcess.chromaticAberration=GUILayout.Toggle(combatPostProcess.chromaticAberration,"Captured lens color separation");
        }
        if(nativeGBufferProbe){nativeGBufferProbe.liveUpdate=GUILayout.Toggle(nativeGBufferProbe.liveUpdate,"Live native MRT probe");GUILayout.Label(nativeGBufferProbe.status);}
        if(nativeDeferredProbe){nativeDeferredProbe.liveUpdate=nativeGBufferProbe&&nativeGBufferProbe.liveUpdate;GUILayout.Label(nativeDeferredProbe.status);}
        GUILayout.Label("Camera");GUILayout.BeginHorizontal();
        if(GUILayout.Button("Capture"))SetCameraPreset(0);if(GUILayout.Button("Full"))SetCameraPreset(1);if(GUILayout.Button("Face"))SetCameraPreset(2);
        GUILayout.EndHorizontal();
        followBodyMotion=GUILayout.Toggle(followBodyMotion,"Follow body movement");
        GUILayout.Label("Orbit");cameraYaw=GUILayout.HorizontalSlider(cameraYaw,-180,180);
        GUILayout.Label("Light rotation");lightYawOffset=GUILayout.HorizontalSlider(lightYawOffset,-180,180);
        if(GUILayout.Button("Reset light"))lightYawOffset=0;
        if(GUILayout.Button("Save 4K PNG"))StartCoroutine(Capture4K());
        if(GUILayout.Button(model.nativeAnimation.enabled?"Pause":"Play"))model.nativeAnimation.enabled=!model.nativeAnimation.enabled;
        foreach(AnimationState state in model.nativeAnimation)if(GUILayout.Button(state.name)){model.nativeAnimation.enabled=true;model.Play(state.name);}
        GUILayout.Label(status);GUILayout.EndScrollView();GUILayout.EndArea();
    }
    System.Collections.IEnumerator Capture4K()
    {
        yield return new WaitForEndOfFrame();
        string folder=System.IO.Path.Combine(Application.persistentDataPath,"LightingCaptures");System.IO.Directory.CreateDirectory(folder);
        string path=System.IO.Path.Combine(folder,Current.key+"-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".png");
        SaveCamera(reviewCamera,path,3840,2160,4);status="Saved: "+path;
    }
    public static void SaveCamera(Camera camera,string path,int width,int height,int samples)
    {
        var oldTarget=camera.targetTexture;var oldActive=RenderTexture.active;bool wasEnabled=camera.enabled;
        var rt=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB){antiAliasing=samples};
        Texture2D image=null;
        try{rt.Create();camera.enabled=false;camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
            image=new Texture2D(width,height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();
            System.IO.File.WriteAllBytes(path,image.EncodeToPNG());}
        finally{camera.targetTexture=oldTarget;camera.enabled=wasEnabled;RenderTexture.active=oldActive;rt.Release();Destroy(rt);if(image!=null)Destroy(image);}
    }
    void OnDestroy()
    {
        if(!ownsSettings)return;
        QualitySettings.antiAliasing=savedAA;QualitySettings.shadowDistance=savedShadowDistance;QualitySettings.shadows=savedShadows;QualitySettings.shadowResolution=savedResolution;
        RenderSettings.ambientMode=savedAmbientMode;RenderSettings.ambientLight=savedAmbient;
    }
}
