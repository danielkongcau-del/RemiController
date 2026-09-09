#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

[DefaultExecutionOrder(10000)]
public class RemielleLightingAudit : MonoBehaviour
{
    const string Out="E:/ZZZ/ZCode/90_Builds/RenderingReview/20260905";
    [Serializable] class Shot {public string name;public int width,height,msaa;}
    [Serializable] class LightCheck {public string profile;public Vector3 expectedDirection,actualDirection,expectedColor,actualColor;public float directionError,colorError;}
    [Serializable] class MaskCheck {public string name;public int width,height,draws,characterPixels,emissionPixels;public long positionChecksum;}
    [Serializable] class GBufferCheck {public string name;public int width,height,draws,shadowDraws,shadowResolution,cascadeSlices,motionRenderers,dynamicMotionPixels,characterPixels,shadowedPixels,litPixels,objectReceiverShadowedPixels,cascadeReceiverShadowedPixels,objectShadowWrittenPixels,auxiliaryPixels,auxiliaryRgbPixels,auxiliaryAlphaPixels,skinPixels,stencil128Pixels,stencil144Pixels,stencilCoveragePixels,stencilOverlap;public int[] cascadeShadowWrittenPixels=new int[4];public bool stencilReadbackYFlipped;public long motionHistoryFrames,shadowFrames,positionChecksum,auxiliaryRgbChecksum,auxiliaryAlphaChecksum;public float objectShadowMin=1,objectShadowMax,minMotionX=1,maxMotionX,minMotionY=1,maxMotionY,maxMotionCenterError,maxFlagValue,maxNormalLengthError,minDepth=1,maxDepth;public string primaryFormat,auxiliaryFormat,motionFormat,normalFormat,shadowDiagnosticFormat,cascadeShadowFormat,objectShadowFormat,depthStencilFormat,stencilViewFormat;}
    [Serializable] class DeferredCheck {public string name,hdrFormat,auxiliaryFormat;public int width,height,resolvedPixels,hdrPixels,outsideStencilPixels;public long colorChecksum;public float maxExpandedNormalError,maxExpandedMotionError;}
    [Serializable] class Report {public bool pass;public string utc,error,device;public bool supportsSetConstantBuffer;public int constantBufferOffsetAlignment,nativeMaterialConstantBuffers,nativeMaterialPasses,nativeMaterialGpuReadbacks,nativeMaterialQueryVisibleBuffers,frames,profileSwitches,materialCountBefore,materialCountAfter,edgePixels1x,edgePixels4x;public List<Shot> shots=new();public List<LightCheck> lights=new();public List<MaskCheck> masks=new();public List<GBufferCheck> gBuffers=new();public List<DeferredCheck> deferred=new();}
    readonly Report report=new();RemielleLightingReview review;RemielleNativeShadowProbe nativeShadow;RemielleNativeGBufferProbe nativeGBuffer;RemielleNativeDeferredProbe nativeDeferred;bool finished;int frame;double started;
    readonly string[] shots={"display-capture","display-neutral","display-face","display-back","display-ground","display-no-shadows","store-capture","combat-capture","combat-face","display-bloom","display-no-bloom","display-4k","display-legacy-ambient-face","combat-no-chromatic-face","combat-no-hq-bloom","combat-ground","combat-back","combat-no-runtime-emission"};
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)] static void Install()
    {if(Environment.GetCommandLineArgs().Contains("-remielleLightingAudit"))new GameObject("OptInLightingAudit").AddComponent<RemielleLightingAudit>();}
    void OnEnable(){Application.logMessageReceived+=OnLog;}
    void OnDestroy(){Application.logMessageReceived-=OnLog;}
    void OnLog(string message,string stack,LogType type){if(!finished&&(type==LogType.Exception||type==LogType.Error||type==LogType.Assert))Finish(false,message+"\n"+stack);}
    void Start()
    {
        try{
            review=FindFirstObjectByType<RemielleLightingReview>();nativeShadow=FindFirstObjectByType<RemielleNativeShadowProbe>();nativeGBuffer=FindFirstObjectByType<RemielleNativeGBufferProbe>();nativeDeferred=FindFirstObjectByType<RemielleNativeDeferredProbe>();review.showUI=false;Application.runInBackground=true;Time.captureDeltaTime=1f/60;started=Time.realtimeSinceStartupAsDouble;
            if(!nativeShadow||!nativeGBuffer||!nativeDeferred)throw new Exception("Live native shadow/G-buffer/Deferred probe missing");nativeGBuffer.liveUpdate=false;nativeDeferred.liveUpdate=false;
            Directory.CreateDirectory(Out);report.utc=DateTime.UtcNow.ToString("O");report.device=SystemInfo.graphicsDeviceName;report.supportsSetConstantBuffer=SystemInfo.supportsSetConstantBuffer;report.constantBufferOffsetAlignment=SystemInfo.constantBufferOffsetAlignment;
            if(!report.supportsSetConstantBuffer)throw new Exception("Active graphics device cannot bind native constant buffers");
            var nativeMaterialBindings=FindFirstObjectByType<RemielleNativeMaterialConstantBufferProbe>();if(!nativeMaterialBindings)throw new Exception("Captured native material binding probe missing");nativeMaterialBindings.Verify();report.nativeMaterialConstantBuffers=nativeMaterialBindings.boundBuffers;report.nativeMaterialPasses=nativeMaterialBindings.activatedPasses;report.nativeMaterialGpuReadbacks=nativeMaterialBindings.gpuReadbacks;report.nativeMaterialQueryVisibleBuffers=nativeMaterialBindings.queryVisibleBuffers;
            review.model.Play("Idle_Loop",0);review.model.nativeAnimation["Idle_Loop"].speed=0;
        }catch(Exception e){Finish(false,e.ToString());}
    }
    void LateUpdate()
    {
        if(finished||review==null)return;
        try{
            if(Time.realtimeSinceStartupAsDouble-started>300)throw new Exception("Lighting audit timed out");
            frame++;
            if(frame<=shots.Length*8)
            {
                int index=(frame-1)/8;
                if((frame-1)%8==0)Setup(index);
                if(frame%8==0){int width=index==11?3840:1920,height=index==11?2160:1080;RemielleLightingReview.SaveCamera(review.reviewCamera,Out+"/"+shots[index]+".png",width,height,4);report.shots.Add(new Shot{name=shots[index],width=width,height=height,msaa=4});if(index==7||index==15||index==16||index==17)ProbeMask(shots[index]);}
            }
            else if(frame==shots.Length*8+1)
            {
                for(int i=0;i<3;i++){review.ApplyProfile(i);report.lights.Add(ProbeLight());}
                ProbeAntialiasing();
                report.materialCountBefore=Resources.FindObjectsOfTypeAll<Material>().Length;
                for(int i=0;i<30;i++){review.ApplyProfile(i%3);report.profileSwitches++;}
                report.materialCountAfter=Resources.FindObjectsOfTypeAll<Material>().Length;
                if(report.materialCountAfter!=report.materialCountBefore)throw new Exception("Material count grew across profile switches");
                review.ApplyProfile(2);review.highQualityBloom.effectEnabled=true;review.followBodyMotion=true;review.SetCameraPreset(1);review.bloom.amount=0;review.model.Play("Idle_Loop",0);review.model.nativeAnimation["Idle_Loop"].speed=1;
                ProbeGBuffer("combat-static");
            }
            else
            {
                int motion=frame-(shots.Length*8+1);
                if(motion==90)review.model.Play("Walk_Loop");if(motion==180)review.model.Play("Run_Loop_02");
                foreach(var r in review.model.GetComponentsInChildren<SkinnedMeshRenderer>())if(r.enabled&&(!float.IsFinite(r.bounds.center.x)||!float.IsFinite(r.bounds.size.y)))throw new Exception("Nonfinite animated bounds");
                nativeGBuffer.RenderNow(640,360);
                if(motion%45==0){RemielleLightingReview.SaveCamera(review.reviewCamera,Out+"/motion-"+motion+".png",1280,720,4);ProbeMask("motion-"+motion);}
                if(motion==45||motion==135||motion==225)ProbeGBuffer("motion-"+motion,false);
                if(motion>=270)Finish(true,"");
            }
        }catch(Exception e){Finish(false,e.ToString());}
    }
    static Color[] ReadFloat(RenderTexture source)
    {
        var staging=new RenderTexture(source.width,source.height,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
        var texture=new Texture2D(source.width,source.height,TextureFormat.RGBAFloat,false,true);var previous=RenderTexture.active;
        try{staging.Create();Graphics.Blit(source,staging);RenderTexture.active=staging;texture.ReadPixels(new Rect(0,0,source.width,source.height),0,0);texture.Apply();return texture.GetPixels();}
        finally{RenderTexture.active=previous;staging.Release();Destroy(staging);Destroy(texture);}
    }
    static Color[] ReadFloatLayer(RenderTexture source,int layer)
    {
        var descriptor=new RenderTextureDescriptor(source.width,source.height,source.graphicsFormat,GraphicsFormat.None)
        {dimension=UnityEngine.Rendering.TextureDimension.Tex2D,volumeDepth=1,msaaSamples=1,useMipMap=false,autoGenerateMips=false};
        var staging=new RenderTexture(descriptor);
        try{staging.Create();Graphics.CopyTexture(source,layer,0,staging,0,0);return ReadFloat(staging);}
        finally{staging.Release();Destroy(staging);}
    }
    static void SavePreview(string path,Color[] source,int width,int height,int mode)
    {
        var texture=new Texture2D(width,height,TextureFormat.RGBA32,false,true);var output=new Color[source.Length];
        for(int i=0;i<source.Length;i++){
            Color c=source[i];
            if(mode==0){c.r=c.r/(1+Mathf.Max(0,c.r));c.g=c.g/(1+Mathf.Max(0,c.g));c.b=c.b/(1+Mathf.Max(0,c.b));c=c.gamma;c.a=1;}
            else if(mode==1)c=new Color(c.r*4,c.g*4,c.b*4,1);
            else if(mode==4){int stencil=Mathf.RoundToInt(c.g*255);c=stencil==144?new Color(1,.2f,.8f,1):stencil==128?new Color(c.r,c.r,c.r,1):Color.black;}
            else if(mode==5)c=new Color(c.r,c.r,c.r,1);
            else c.a=1;
            output[i]=c;
        }
        texture.SetPixels(output);texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());Destroy(texture);
    }
    void ProbeGBuffer(string name,bool renderNow=true)
    {
        if(renderNow)nativeGBuffer.RenderNow(640,360);
        var p=ReadFloat(nativeGBuffer.Primary);var a=ReadFloat(nativeGBuffer.Auxiliary);var m=ReadFloat(nativeGBuffer.MotionFlagsClass);var n=ReadFloat(nativeGBuffer.WorldNormal);var s=ReadFloat(nativeGBuffer.ShadowDiagnostic);var d=ReadFloat(nativeGBuffer.DepthStencilReadback);
        var check=new GBufferCheck{name=name,width=640,height=360,draws=nativeGBuffer.draws,shadowDraws=nativeShadow.draws,shadowResolution=nativeShadow.CascadeShadow.width,cascadeSlices=nativeShadow.CascadeShadow.volumeDepth,shadowFrames=nativeShadow.frames,motionRenderers=nativeGBuffer.motionRenderers,motionHistoryFrames=nativeGBuffer.motionHistoryFrames,
            primaryFormat=nativeGBuffer.Primary.graphicsFormat.ToString(),auxiliaryFormat=nativeGBuffer.Auxiliary.graphicsFormat.ToString(),motionFormat=nativeGBuffer.MotionFlagsClass.graphicsFormat.ToString(),normalFormat=nativeGBuffer.WorldNormal.graphicsFormat.ToString(),
            shadowDiagnosticFormat=nativeGBuffer.ShadowDiagnostic.graphicsFormat.ToString(),cascadeShadowFormat=nativeShadow.CascadeShadow.graphicsFormat.ToString(),objectShadowFormat=nativeShadow.ObjectShadow.graphicsFormat.ToString(),
            depthStencilFormat=nativeGBuffer.DepthStencil.depthStencilFormat.ToString(),stencilViewFormat=nativeGBuffer.DepthStencil.stencilFormat.ToString()};
        bool formats= nativeGBuffer.Primary.graphicsFormat==GraphicsFormat.R16G16B16A16_SFloat
            && nativeGBuffer.Auxiliary.graphicsFormat==GraphicsFormat.R8G8B8A8_SRGB
            && nativeGBuffer.MotionFlagsClass.graphicsFormat==GraphicsFormat.A2B10G10R10_UNormPack32
            && nativeGBuffer.WorldNormal.graphicsFormat==GraphicsFormat.A2B10G10R10_UNormPack32
            && nativeGBuffer.DepthStencil.depthStencilFormat==GraphicsFormat.D32_SFloat_S8_UInt
            && nativeGBuffer.DepthStencil.stencilFormat==GraphicsFormat.R8_UInt;
        var objectShadow=ReadFloat(nativeShadow.ObjectShadow);float clearDepth=SystemInfo.usesReversedZBuffer?0:1;
        foreach(var value in objectShadow){check.objectShadowMin=Mathf.Min(check.objectShadowMin,value.r);check.objectShadowMax=Mathf.Max(check.objectShadowMax,value.r);if(Mathf.Abs(value.r-clearDepth)>1f/65535f)check.objectShadowWrittenPixels++;}
        for(int layer=0;layer<4;layer++)foreach(var value in ReadFloatLayer(nativeShadow.CascadeShadow,layer))if(Mathf.Abs(value.r-clearDepth)>1f/65535f)check.cascadeShadowWrittenPixels[layer]++;
        int sameOverlap=0,flippedOverlap=0;
        for(int i=0;i<d.Length;i++){
            int stencil=Mathf.RoundToInt(d[i].g*255);if(stencil==128){check.stencil128Pixels++;check.stencilCoveragePixels++;}else if(stencil==144){check.stencil144Pixels++;check.stencilCoveragePixels++;}
            if(n[i].a>.5f){if(stencil==128||stencil==144)sameOverlap++;int x=i%check.width,y=i/check.width,opposite=(check.height-1-y)*check.width+x;int oppositeStencil=Mathf.RoundToInt(d[opposite].g*255);if(oppositeStencil==128||oppositeStencil==144)flippedOverlap++;}
        }
        check.stencilReadbackYFlipped=flippedOverlap>sameOverlap;check.stencilOverlap=Mathf.Max(sameOverlap,flippedOverlap);
        for(int i=0;i<n.Length;i++)if(n[i].a>.5f){
            check.characterPixels++;check.positionChecksum+=i;
            if(!float.IsFinite(p[i].r+p[i].g+p[i].b+a[i].r+a[i].g+a[i].b))throw new Exception("Nonfinite live native G-buffer value");
            float auxiliaryRgb=a[i].r+a[i].g+a[i].b;
            if(auxiliaryRgb+a[i].a>.00001f)check.auxiliaryPixels++;
            if(auxiliaryRgb>.00001f){check.auxiliaryRgbPixels++;check.auxiliaryRgbChecksum+=Mathf.RoundToInt(a[i].r*255)+Mathf.RoundToInt(a[i].g*255)+Mathf.RoundToInt(a[i].b*255);}
            if(a[i].a>.00001f){check.auxiliaryAlphaPixels++;check.auxiliaryAlphaChecksum+=Mathf.RoundToInt(a[i].a*255);}
            check.maxMotionCenterError=Mathf.Max(check.maxMotionCenterError,Mathf.Abs(m[i].r-.497999996f),Mathf.Abs(m[i].g-.497999996f));
            if(s[i].r<.99f)check.shadowedPixels++;if(s[i].r>.01f)check.litPixels++;
            if(s[i].g<.99f)check.objectReceiverShadowedPixels++;
            if(s[i].b<.99f)check.cascadeReceiverShadowedPixels++;
            check.minMotionX=Mathf.Min(check.minMotionX,m[i].r);check.maxMotionX=Mathf.Max(check.maxMotionX,m[i].r);check.minMotionY=Mathf.Min(check.minMotionY,m[i].g);check.maxMotionY=Mathf.Max(check.maxMotionY,m[i].g);
            if(Mathf.Abs(m[i].r-509f/1023f)>1.1f/1023f||Mathf.Abs(m[i].g-509f/1023f)>1.1f/1023f)check.dynamicMotionPixels++;
            check.maxFlagValue=Mathf.Max(check.maxFlagValue,Mathf.Abs(m[i].b));
            if(m[i].a>.15f){check.skinPixels++;if(Mathf.Abs(m[i].a-1f/3f)>.01f)throw new Exception("Invalid live native skin class code");}
            Vector3 normal=new Vector3(n[i].r*2-1,n[i].g*2-1,n[i].b*2-1);check.maxNormalLengthError=Mathf.Max(check.maxNormalLengthError,Mathf.Abs(normal.magnitude-1));
            int x=i%check.width,y=i/check.width,depthIndex=check.stencilReadbackYFlipped?(check.height-1-y)*check.width+x:i;
            int stencil=Mathf.RoundToInt(d[depthIndex].g*255);if(stencil!=128&&stencil!=144)throw new Exception("Unexpected live native stencil "+stencil+" / same="+sameOverlap+" / flipped="+flippedOverlap+" / coverage="+check.stencilCoveragePixels);
            check.minDepth=Mathf.Min(check.minDepth,d[depthIndex].r);check.maxDepth=Mathf.Max(check.maxDepth,d[depthIndex].r);
        }
        bool expectMotion=name.StartsWith("motion-");
        if(!formats||check.draws<25||check.shadowDraws!=check.draws*5||check.shadowResolution!=2048||check.cascadeSlices!=4||check.shadowDiagnosticFormat!="R8G8B8A8_UNorm"||check.cascadeShadowFormat!="R16_UNorm"||check.objectShadowFormat!="R16_UNorm"||check.objectShadowWrittenPixels<10||check.litPixels<10||check.characterPixels<1000||check.stencilCoveragePixels!=check.characterPixels||check.stencil128Pixels<1000||check.stencil144Pixels<10||check.skinPixels<100||check.auxiliaryPixels<10||check.auxiliaryRgbPixels<10||(!expectMotion&&check.maxMotionCenterError>.002f)||(expectMotion&&(check.motionRenderers<20||check.dynamicMotionPixels<100))||check.maxFlagValue>.002f||check.maxNormalLengthError>.01f||check.maxDepth-check.minDepth<.00001f)
            throw new Exception("Invalid live native G-buffer: "+JsonUtility.ToJson(check));
        report.gBuffers.Add(check);
        SavePreview(Out+"/native-live-"+name+"-primary.png",p,640,360,0);SavePreview(Out+"/native-live-"+name+"-auxiliary.png",a,640,360,1);SavePreview(Out+"/native-live-"+name+"-motion-class.png",m,640,360,2);SavePreview(Out+"/native-live-"+name+"-normal.png",n,640,360,3);SavePreview(Out+"/native-live-"+name+"-shadow.png",s,640,360,5);SavePreview(Out+"/native-live-"+name+"-depth-stencil.png",d,640,360,4);
        ProbeDeferred(name,n,m,check.characterPixels);
    }
    void ProbeDeferred(string name,Color[] sourceNormal,Color[] sourceMotion,int characterPixels)
    {
        nativeDeferred.RenderNow(640,360);
        var expandedNormal=ReadFloat(nativeDeferred.ExpandedNormal);var expandedMotion=ReadFloat(nativeDeferred.ExpandedMotionFlagsClass);var hdr=ReadFloat(nativeDeferred.Hdr);var auxiliary=ReadFloat(nativeDeferred.Auxiliary);
        var check=new DeferredCheck{name=name,width=640,height=360,hdrFormat=nativeDeferred.Hdr.graphicsFormat.ToString(),auxiliaryFormat=nativeDeferred.Auxiliary.graphicsFormat.ToString()};
        unchecked{
            long checksum=1469598103934665603L;
            for(int i=0;i<hdr.Length;i++){
                check.maxExpandedNormalError=Mathf.Max(check.maxExpandedNormalError,Mathf.Abs(sourceNormal[i].r-expandedNormal[i].r),Mathf.Abs(sourceNormal[i].g-expandedNormal[i].g),Mathf.Abs(sourceNormal[i].b-expandedNormal[i].b),Mathf.Abs(sourceNormal[i].a-expandedNormal[i].a));
                check.maxExpandedMotionError=Mathf.Max(check.maxExpandedMotionError,Mathf.Abs(sourceMotion[i].r-expandedMotion[i].r),Mathf.Abs(sourceMotion[i].g-expandedMotion[i].g),Mathf.Abs(sourceMotion[i].b-expandedMotion[i].b),Mathf.Abs(sourceMotion[i].a-expandedMotion[i].a));
                bool written=auxiliary[i].b>.5f;
                if(written){
                    check.resolvedPixels++;if(!float.IsFinite(hdr[i].r+hdr[i].g+hdr[i].b+auxiliary[i].r+auxiliary[i].g+auxiliary[i].b))throw new Exception("Nonfinite live draw 523 output");
                    if(hdr[i].r+hdr[i].g+hdr[i].b>.00001f)check.hdrPixels++;
                    int value=Mathf.RoundToInt(Mathf.Clamp01(hdr[i].r)*1023)^Mathf.RoundToInt(Mathf.Clamp01(hdr[i].g)*1023)<<10^Mathf.RoundToInt(Mathf.Clamp01(hdr[i].b)*1023)<<20;
                    checksum=(checksum^value)*1099511628211L;
                }else if(hdr[i].r+hdr[i].g+hdr[i].b>.00001f)check.outsideStencilPixels++;
            }
            check.colorChecksum=checksum;
        }
        if(check.hdrFormat!="B10G11R11_UFloatPack32"||check.auxiliaryFormat!="A2B10G10R10_UNormPack32"||check.resolvedPixels!=characterPixels||check.hdrPixels<characterPixels*.9f||check.outsideStencilPixels!=0||check.maxExpandedNormalError>.00001f||check.maxExpandedMotionError>.00001f)
            throw new Exception("Invalid live draw 523 resolve: "+JsonUtility.ToJson(check));
        report.deferred.Add(check);SavePreview(Out+"/native-live-"+name+"-resolved.png",hdr,640,360,0);SavePreview(Out+"/native-live-"+name+"-resolved-auxiliary.png",auxiliary,640,360,2);
    }
    void Setup(int i)
    {
        review.capturedEnvironment=i!=1;review.nativeEnvironment=i!=12;review.selfShadows=i==4;review.bloom.amount=i==9||i==11?1:0;review.floor.SetActive(i==4||i==5||i==15);
        review.highQualityBloom.effectEnabled=i!=14;
        review.combatPostProcess.chromaticAberration=i!=13;
        review.lightYawOffset=0;review.ApplyProfile(i==6?1:i==7||i==8||i>=13?2:0);review.SetCameraPreset(i==2||i==8||i==12||i==13?2:i==3||i==4||i==5||i==16?1:0);
        if(i==3||i==16)review.cameraYaw=180;
        if(i==17)foreach(var m in review.model.GetComponentsInChildren<SkinnedMeshRenderer>(true).SelectMany(r=>r.sharedMaterials).Where(m=>m).Distinct())
        {
            if(m.HasProperty("_SecondaryEmission"))m.SetFloat("_SecondaryEmission",0);
            if(m.HasProperty("_SpecialWeaponEmission"))m.SetFloat("_SpecialWeaponEmission",0);
        }
        review.PositionCamera();
    }
    void ProbeMask(string name)
    {
        var mask=review.highQualityBloom.Mask;if(!mask)throw new Exception("Live Bloom mask missing");
        var before=RenderTexture.active;var t=new Texture2D(mask.width,mask.height,TextureFormat.RGBA32,false,true);
        try{
            RenderTexture.active=mask;t.ReadPixels(new Rect(0,0,mask.width,mask.height),0,0);t.Apply();
            var values=t.GetPixels32();var check=new MaskCheck{name=name,width=mask.width,height=mask.height,draws=review.highQualityBloom.maskDraws};
            for(int i=0;i<values.Length;i++){if(values[i].r>127){check.characterPixels++;check.positionChecksum+=i;}if(values[i].g>0)check.emissionPixels++;}
            if(check.characterPixels<1000||check.characterPixels>values.Length*.8||check.draws<25)throw new Exception("Invalid live Bloom coverage: "+JsonUtility.ToJson(check));
            report.masks.Add(check);File.WriteAllBytes(Out+"/mask-"+name+".png",t.EncodeToPNG());
        }finally{RenderTexture.active=before;Destroy(t);}
    }
    LightCheck ProbeLight()
    {
        var quad=GameObject.CreatePrimitive(PrimitiveType.Quad);quad.layer=30;quad.transform.position=new Vector3(0,100,0);quad.transform.localScale=Vector3.one*2;
        var material=new Material(review.diagnosticShader);quad.GetComponent<Renderer>().sharedMaterial=material;
        var camera=new GameObject("LightProbeCamera").AddComponent<Camera>();camera.enabled=false;camera.renderingPath=RenderingPath.Forward;camera.cullingMask=1<<30;camera.orthographic=true;camera.orthographicSize=.5f;camera.transform.position=new Vector3(0,100,1);camera.transform.rotation=Quaternion.Euler(0,180,0);
        var rt=new RenderTexture(8,8,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);camera.targetTexture=rt;
        var image=new Texture2D(8,8,TextureFormat.RGBAFloat,false,true);var previous=RenderTexture.active;
        var result=new LightCheck{profile=review.Current.key,expectedDirection=-review.keyLight.transform.forward,expectedColor=RemielleLightingReview.V(review.Current.lightColor)};
        try{for(int i=0;i<2;i++){material.SetFloat("_Mode",i);camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,8,8),0,0);image.Apply();Color c=image.GetPixel(4,4);if(i==0)result.actualDirection=new Vector3(c.r,c.g,c.b);else result.actualColor=new Vector3(c.r,c.g,c.b);}
            result.directionError=Vector3.Distance(result.expectedDirection,result.actualDirection);result.colorError=Vector3.Distance(result.expectedColor,result.actualColor);
            if(result.directionError>.001f||result.colorError>.002f)throw new Exception("GPU light mismatch "+JsonUtility.ToJson(result));
        }finally{quad.SetActive(false);RenderTexture.active=previous;camera.targetTexture=null;rt.Release();Destroy(camera.gameObject);Destroy(quad);Destroy(material);Destroy(image);Destroy(rt);}
        return result;
    }
    void ProbeAntialiasing()
    {
        var quad=GameObject.CreatePrimitive(PrimitiveType.Quad);quad.layer=30;quad.transform.position=new Vector3(0,100,0);quad.transform.rotation=Quaternion.Euler(0,0,27);
        var material=new Material(review.diagnosticShader);material.SetFloat("_Mode",2);quad.GetComponent<Renderer>().sharedMaterial=material;
        var camera=new GameObject("AntialiasingProbe").AddComponent<Camera>();camera.enabled=false;camera.allowHDR=true;camera.allowMSAA=true;camera.renderingPath=RenderingPath.Forward;camera.cullingMask=1<<30;
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.orthographic=true;camera.orthographicSize=1;camera.transform.position=new Vector3(0,100,1);camera.transform.rotation=Quaternion.Euler(0,180,0);
        // Include the same image-effect resolve path, with Bloom disabled.
        camera.gameObject.AddComponent<RemielleReviewBloom>().amount=0;
        var previous=RenderTexture.active;int savedAA=QualitySettings.antiAliasing;
        try{foreach(int samples in new[]{1,4}){
            // HDR image-effect intermediates also consult the global quality
            // setting; a single-sample final target alone does not disable MSAA.
            QualitySettings.antiAliasing=samples==1?0:samples;
            var rt=new RenderTexture(32,32,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear){antiAliasing=samples};
            var pixels=new Texture2D(32,32,TextureFormat.RGBA32,false,true);
            try{rt.Create();camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;pixels.ReadPixels(new Rect(0,0,32,32),0,0);pixels.Apply();
                int edges=pixels.GetPixels32().Count(p=>p.r>0&&p.r<255);if(samples==1)report.edgePixels1x=edges;else report.edgePixels4x=edges;
            }finally{camera.targetTexture=null;RenderTexture.active=previous;rt.Release();Destroy(rt);Destroy(pixels);}
        }
        if(report.edgePixels1x!=0||report.edgePixels4x<20)throw new Exception("4x MSAA coverage was not observed at GPU output");
        }finally{QualitySettings.antiAliasing=savedAA;quad.SetActive(false);Destroy(camera.gameObject);Destroy(quad);Destroy(material);}
    }
    void Finish(bool passed,string error)
    {
        if(finished)return;
        if(passed&&(report.gBuffers.Count<4||report.gBuffers.Select(x=>x.positionChecksum).Distinct().Count()<2)){passed=false;error="Live native G-buffer animation coverage did not change";}
        if(passed&&(report.gBuffers.Sum(x=>x.shadowedPixels)<1000||report.gBuffers.Sum(x=>x.objectReceiverShadowedPixels)<1000)){passed=false;error="Dynamic native per-object shadow receiver did not exercise lit and shadowed regions";}
        if(passed&&report.gBuffers.Sum(x=>x.cascadeShadowWrittenPixels.Sum())<1000){passed=false;error="Dynamic native cascade shadow maps did not contain rendered casters";}
        if(passed&&(report.deferred.Count<4||report.deferred.Select(x=>x.colorChecksum).Distinct().Count()<2)){passed=false;error="Live draw 523 output did not track animation";}
        finished=true;report.pass=passed;report.error=error;report.frames=frame;
        File.WriteAllText(Out+"/player-verification.json",JsonUtility.ToJson(report,true));
        if(passed)Debug.Log("REMIELLE_LIGHTING_PLAYER_VERIFIED");else Debug.LogError(error);
        #if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(passed?0:1);
        #else
        Application.Quit(passed?0:1);
        #endif
    }
}
#endif
