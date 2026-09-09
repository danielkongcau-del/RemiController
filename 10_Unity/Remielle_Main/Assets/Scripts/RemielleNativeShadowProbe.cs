using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

// Review-only dynamic implementation of the captured character shadow layout:
// a four-slice main-light map and a tight per-object map, both R16 at 2048².
[DefaultExecutionOrder(450)]
public class RemielleNativeShadowProbe : MonoBehaviour
{
    public Shader shadowShader;
    public Transform model;
    public Light keyLight;
    public Camera reviewCamera;
    public int resolution=2048;
    public int draws;
    public long frames;
    public string status;
    public RenderTexture CascadeShadow=>cascadeShadow;
    public RenderTexture ObjectShadow=>objectShadow;

    readonly Matrix4x4[] cascadeWorldToShadow=new Matrix4x4[4];
    readonly Vector4[] cascadeSpheres=new Vector4[4];
    readonly Dictionary<Material,Material> adapters=new Dictionary<Material,Material>();
    readonly List<Material> slots=new List<Material>();
    readonly Vector3[] nearCorners=new Vector3[4],farCorners=new Vector3[4],frustumPoints=new Vector3[8];
    SkinnedMeshRenderer[] renderers;
    CommandBuffer commands;
    RenderTexture cascadeShadow,objectShadow;
    Matrix4x4 objectWorldToShadow;

    static readonly float[] CascadeFractions={0.067f,0.2f,0.5f,1f};
    static Matrix4x4 BiasMatrix=>new Matrix4x4(new Vector4(.5f,0,0,0),new Vector4(0,.5f,0,0),new Vector4(0,0,1,0),new Vector4(.5f,.5f,0,1));

    void Prepare()
    {
        if(commands!=null)return;
        if(!shadowShader||!shadowShader.isSupported||!model||!keyLight||!reviewCamera)throw new InvalidOperationException("Native shadow probe references missing");
        renderers=model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        commands=new CommandBuffer{name="Remielle dynamic native-layout shadows"};
    }

    RenderTexture ShadowTarget(string name,int layers)
    {
        var descriptor=new RenderTextureDescriptor(resolution,resolution,GraphicsFormat.R16_UNorm,GraphicsFormat.D32_SFloat)
        {msaaSamples=1,useMipMap=false,autoGenerateMips=false,dimension=layers>1?TextureDimension.Tex2DArray:TextureDimension.Tex2D,volumeDepth=layers};
        var target=new RenderTexture(descriptor){name=name,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp,hideFlags=HideFlags.HideAndDontSave};
        if(!target.Create())throw new InvalidOperationException("Could not create "+name);return target;
    }

    void EnsureTargets()
    {
        if(cascadeShadow&&cascadeShadow.width==resolution)return;
        Release(ref cascadeShadow);Release(ref objectShadow);
        cascadeShadow=ShadowTarget("Remielle native four-cascade shadow",4);
        objectShadow=ShadowTarget("Remielle native per-object shadow",1);
    }

    static void Release(ref RenderTexture target)
    {if(!target)return;target.Release();if(Application.isPlaying)Destroy(target);else DestroyImmediate(target);target=null;}

    Bounds ModelBounds()
    {
        bool first=true;Bounds bounds=default;
        foreach(var renderer in renderers)if(renderer&&renderer.enabled&&renderer.gameObject.activeInHierarchy)
        {if(first){bounds=renderer.bounds;first=false;}else bounds.Encapsulate(renderer.bounds);}
        if(first)throw new InvalidOperationException("No native shadow casters");return bounds;
    }

    Matrix4x4 LightMatrix(Vector3 center,float radius)
    {
        Vector3 forward=keyLight.transform.forward.normalized;
        Vector3 up=Mathf.Abs(Vector3.Dot(forward,Vector3.up))>.95f?Vector3.forward:Vector3.up;
        float extent=Mathf.Max(.25f,radius*1.03f);
        Vector3 position=center-forward*(extent+5f);
        // Unity camera space looks down -Z. A Transform inverse alone leaves
        // points along transform.forward on +Z and clips the complete caster.
        Matrix4x4 view=Matrix4x4.Scale(new Vector3(1,1,-1))*Matrix4x4.TRS(position,Quaternion.LookRotation(forward,up),Vector3.one).inverse;
        Matrix4x4 projection=Matrix4x4.Ortho(-extent,extent,-extent,extent,.01f,extent*2+10f);
        return GL.GetGPUProjectionMatrix(projection,true)*view;
    }

    void UpdateMatrices(Bounds bounds)
    {
        float previous=reviewCamera.nearClipPlane;
        for(int layer=0;layer<4;layer++)
        {
            float distance=Mathf.Lerp(reviewCamera.nearClipPlane,reviewCamera.farClipPlane,CascadeFractions[layer]);
            reviewCamera.CalculateFrustumCorners(new Rect(0,0,1,1),previous,Camera.MonoOrStereoscopicEye.Mono,nearCorners);
            reviewCamera.CalculateFrustumCorners(new Rect(0,0,1,1),distance,Camera.MonoOrStereoscopicEye.Mono,farCorners);
            Vector3 center=Vector3.zero;
            for(int i=0;i<4;i++){frustumPoints[i]=reviewCamera.transform.TransformPoint(nearCorners[i]);frustumPoints[i+4]=reviewCamera.transform.TransformPoint(farCorners[i]);center+=frustumPoints[i]+frustumPoints[i+4];}
            center/=8;float radius=0;for(int i=0;i<8;i++)radius=Mathf.Max(radius,Vector3.Distance(center,frustumPoints[i]));
            radius=Mathf.Ceil(radius*resolution/16f)*16f/resolution;
            cascadeSpheres[layer]=new Vector4(center.x,center.y,center.z,radius*radius);
            cascadeWorldToShadow[layer]=BiasMatrix*LightMatrix(center,radius);previous=distance;
        }
        float objectRadius=Mathf.Max(.25f,bounds.extents.magnitude*1.08f);
        objectWorldToShadow=BiasMatrix*LightMatrix(bounds.center,objectRadius);
    }

    Material Adapter(Material source)
    {
        if(!adapters.TryGetValue(source,out var adapter))
        {adapter=new Material(shadowShader){name=source.name+" (native shadow depth)",hideFlags=HideFlags.HideAndDontSave};adapters.Add(source,adapter);}
        adapter.CopyPropertiesFromMaterial(source);adapter.SetInt("_ReviewShadowZTest",(int)(SystemInfo.usesReversedZBuffer?CompareFunction.GreaterEqual:CompareFunction.LessEqual));return adapter;
    }

    void DrawCasters()
    {
        foreach(var renderer in renderers)
        {
            if(!renderer||!renderer.enabled||!renderer.gameObject.activeInHierarchy||!renderer.sharedMesh)continue;
            renderer.GetSharedMaterials(slots);
            if(slots.Count!=renderer.sharedMesh.subMeshCount)throw new InvalidOperationException("Native shadow material/submesh mismatch: "+renderer.name);
            for(int submesh=0;submesh<slots.Count;submesh++)
            {
                var source=slots[submesh];if(!source)throw new InvalidOperationException("Missing native shadow material: "+renderer.name);
                if(source.HasProperty("_MaterialType")&&source.GetFloat("_MaterialType")==3)continue;
                commands.DrawRenderer(renderer,Adapter(source),submesh,0);draws++;
            }
        }
    }

    public void RenderNow()
    {
        Prepare();EnsureTargets();UpdateMatrices(ModelBounds());commands.Clear();draws=0;
        float far=SystemInfo.usesReversedZBuffer?0:1;
        for(int layer=0;layer<4;layer++)
        {
            commands.SetRenderTarget(cascadeShadow,0,CubemapFace.Unknown,layer);
            commands.ClearRenderTarget(RTClearFlags.All,new Color(far,0,0,1),far,0);
            commands.SetGlobalMatrix("_ReviewShadowVP",BiasMatrix.inverse*cascadeWorldToShadow[layer]);
            DrawCasters();
        }
        commands.SetRenderTarget(objectShadow);
        commands.ClearRenderTarget(RTClearFlags.All,new Color(far,0,0,1),far,0);
        commands.SetGlobalMatrix("_ReviewShadowVP",BiasMatrix.inverse*objectWorldToShadow);
        DrawCasters();Graphics.ExecuteCommandBuffer(commands);frames++;
        status="native shadows 4+1 x "+resolution+", draws "+draws;
    }

    public void ApplyTo(Material material)
    {
        material.SetTexture("_ReviewCascadeShadow",cascadeShadow);material.SetTexture("_ReviewObjectShadow",objectShadow);
        material.SetMatrixArray("_ReviewCascadeWorldToShadow",cascadeWorldToShadow);material.SetMatrix("_ReviewObjectWorldToShadow",objectWorldToShadow);
        material.SetVector("_ReviewCascadeSphere0",cascadeSpheres[0]);material.SetVector("_ReviewCascadeSphere1",cascadeSpheres[1]);material.SetVector("_ReviewCascadeSphere2",cascadeSpheres[2]);material.SetVector("_ReviewCascadeSphere3",cascadeSpheres[3]);
        material.SetVector("_ReviewCascadeRadiusSquared",new Vector4(cascadeSpheres[0].w,cascadeSpheres[1].w,cascadeSpheres[2].w,cascadeSpheres[3].w));
        material.SetVector("_ReviewShadowTexelSize",new Vector4(1f/resolution,1f/resolution,resolution,resolution));
        material.SetFloat("_ReviewReversedZ",SystemInfo.usesReversedZBuffer?1:0);material.SetFloat("_ReviewNativeShadowEnabled",1);
    }

    void OnDisable(){Release(ref cascadeShadow);Release(ref objectShadow);}
    void OnDestroy()
    {
        Release(ref cascadeShadow);Release(ref objectShadow);commands?.Release();
        foreach(var material in adapters.Values)if(material){if(Application.isPlaying)Destroy(material);else DestroyImmediate(material);}adapters.Clear();
    }
}
