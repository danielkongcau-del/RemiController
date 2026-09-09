using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Native HQ kernels and scene/character composition. The live buffer describes
// the current forward character; it is not a replacement for a full native G-buffer.
[RequireComponent(typeof(Camera))]
public class RemielleHighQualityBloom : MonoBehaviour
{
    public Shader shader,maskShader;
    public TextAsset capturedData;
    public Texture2D sceneLut;
    public Transform model;
    public Renderer ground;
    public bool available,effectEnabled=true;
    public int maskDraws,lastWidth,lastHeight;
    public RenderTexture Mask => mask;
    Material material;
    CapturedBloomData data;
    Vector4[][] pixel;
    SkinnedMeshRenderer[] renderers;
    RenderTexture mask;
    CommandBuffer commands;
    readonly List<Material> slots=new List<Material>();
    readonly Dictionary<Material,Material> maskMaterials=new Dictionary<Material,Material>();

    void Prepare()
    {
        if(material)return;
        if(!shader||!maskShader||!capturedData||!sceneLut||!model)throw new InvalidOperationException("HQ Bloom source references missing");
        material=new Material(shader){hideFlags=HideFlags.HideAndDontSave};
        data=JsonUtility.FromJson<CapturedBloomData>(capturedData.text);
        pixel=new Vector4[data.steps.Length][];
        for(int i=0;i<pixel.Length;i++)pixel[i]=RemielleReviewBloom.Vectors(data.steps[i].pixel,264);
        renderers=model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        commands=new CommandBuffer{name="Remielle live Bloom material buffer"};
    }
    void ReleaseMask()
    {
        if(!mask)return;mask.Release();if(Application.isPlaying)Destroy(mask);else DestroyImmediate(mask);mask=null;
    }
    void RenderMask(int width,int height)
    {
        if(!mask||mask.width!=width||mask.height!=height){
            ReleaseMask();mask=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear){name="Remielle live Bloom mask",filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp,hideFlags=HideFlags.HideAndDontSave};mask.Create();
        }
        var camera=GetComponent<Camera>();commands.Clear();commands.SetRenderTarget(mask);commands.ClearRenderTarget(true,true,Color.clear);
        commands.SetGlobalMatrix("_ReviewMaskVP",GL.GetGPUProjectionMatrix(camera.projectionMatrix,true)*camera.worldToCameraMatrix);
        maskDraws=0;
        foreach(var renderer in renderers){
            if(!renderer||!renderer.enabled||!renderer.gameObject.activeInHierarchy||!renderer.sharedMesh)continue;
            renderer.GetSharedMaterials(slots);
            if(slots.Count!=renderer.sharedMesh.subMeshCount)throw new InvalidOperationException("Bloom buffer material/submesh count mismatch: "+renderer.name);
            for(int i=0;i<slots.Count;i++){
                var m=slots[i];if(!m)throw new InvalidOperationException("Missing live Bloom material");
                // Secondary and signature emission are encoded by the review
                // mask shader. Screen-image routing still has no recovered
                // Remielle runtime state and remains guarded.
                if(m.GetFloat("_ScreenImage")>.5f)throw new InvalidOperationException("This material needs its native screen-image buffer implementation: "+m.name);
                if(m.GetFloat("_MaterialType")==3)continue;
                if(!maskMaterials.TryGetValue(m,out var maskMaterial)){
                    maskMaterial=new Material(maskShader){hideFlags=HideFlags.HideAndDontSave,name=m.name+" (Bloom data)"};maskMaterials.Add(m,maskMaterial);
                }
                maskMaterial.CopyPropertiesFromMaterial(m);
                int pass=maskMaterial.FindPass("Review Bloom Mask");if(pass<0)throw new InvalidOperationException("Missing live Bloom mask pass: "+maskShader.name);
                commands.DrawRenderer(renderer,maskMaterial,i,pass);maskDraws++;
            }
        }
        if(ground&&ground.enabled&&ground.gameObject.activeInHierarchy)commands.DrawRenderer(ground,material,0,6);
        Graphics.ExecuteCommandBuffer(commands);lastWidth=width;lastHeight=height;
    }
    static RenderTexture Temp(int w,int h)
    {
        var r=RenderTexture.GetTemporary(w,h,0,RenderTextureFormat.RGB111110Float,RenderTextureReadWrite.Linear);
        r.filterMode=FilterMode.Bilinear;r.wrapMode=TextureWrapMode.Clamp;return r;
    }
    void Draw(int index,RenderTexture target)
    {
        material.SetVectorArray("P0",pixel[index]);Graphics.SetRenderTarget(target);GL.sRGBWrite=target?target.sRGB:QualitySettings.activeColorSpace==ColorSpace.Linear;
        if(!material.SetPass(data.steps[index].passIndex))throw new InvalidOperationException("HQ Bloom pass unavailable");
        Graphics.DrawProceduralNow(MeshTopology.Triangles,3);
    }
    void OnRenderImage(RenderTexture source,RenderTexture destination)
    {
        if(!available){ReleaseMask();Graphics.Blit(source,destination);return;}
        Prepare();var previous=RenderTexture.active;bool srgb=GL.sRGBWrite;
        var targets=new Dictionary<int,RenderTexture>();
        try{
            RenderMask(source.width,source.height);
            material.SetFloat("_PackedMask",1);material.SetTexture("_RoutingMask",mask);
            for(int i=0;effectEnabled&&i<20;i++){
                var step=data.steps[i];var target=Temp(step.width,step.height);targets.Add(step.call,target);
                if(i==0){material.SetTexture("_T0",source);material.SetTexture("_T1",mask);material.SetTexture("_T2",mask);}
                else for(int slot=0;slot<step.inputDraws.Length;slot++)material.SetTexture("_T"+slot,targets[step.inputDraws[slot]]);
                Draw(i,target);
            }
            // Scene/sky share a formula; the separate native sky pass is redundant
            // on this black studio background. Character RGB is already graded.
            Texture glow=effectEnabled?targets[555]:Texture2D.blackTexture;
            // Turning Bloom off must not also turn off the scene's color grading.
            material.SetTexture("_T0",sceneLut);material.SetTexture("_T1",source);material.SetTexture("_T2",glow);
            Draw(20,destination);
            material.SetTexture("_T1",mask);material.SetTexture("_T2",source);material.SetTexture("_T3",glow);
            Draw(21,destination);
        }finally{RenderTexture.active=previous;GL.sRGBWrite=srgb;foreach(var r in targets.Values)RenderTexture.ReleaseTemporary(r);}
    }
    void OnDisable(){ReleaseMask();}
    void OnDestroy(){ReleaseMask();commands?.Release();foreach(var m in maskMaterials.Values){if(Application.isPlaying)Destroy(m);else DestroyImmediate(m);}maskMaterials.Clear();if(material){if(Application.isPlaying)Destroy(material);else DestroyImmediate(material);}}
}
