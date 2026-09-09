using UnityEngine;

// Final battle UberPost. FX Bloom may be supplied by its own future render
// layer; camera RGB is not a substitute for the captured particle/G-buffer inputs.
[RequireComponent(typeof(Camera))]
public class RemielleCapturedCombatPost : MonoBehaviour
{
    public Shader shader;
    public TextAsset capturedData;
    public Texture2D lut;
    public Texture fxBloom;
    public bool available,chromaticAberration=true;
    Material material;
    Vector4[] pixel,pixel1;
    void Prepare()
    {
        if(material)return;
        material=new Material(shader){hideFlags=HideFlags.HideAndDontSave};
        var data=JsonUtility.FromJson<CapturedBloomData>(capturedData.text);var final=data.steps[data.steps.Length-1];
        pixel=RemielleReviewBloom.Vectors(final.pixel);pixel1=RemielleReviewBloom.Vectors(final.pixel1);
    }
    void OnRenderImage(RenderTexture source,RenderTexture destination)
    {
        if(!available){Graphics.Blit(source,destination);return;}
        Prepare();var previous=RenderTexture.active;bool srgb=GL.sRGBWrite;
        try{
            pixel[138]=new Vector4(source.width,source.height,1f/source.width,1f/source.height);
            // No particle layer is being rendered in this review. Disable its
            // contribution explicitly, while retaining native LUT/chromatic order.
            pixel1[21].x=fxBloom?1:0;
            material.SetVectorArray("P0",pixel);material.SetVectorArray("P1",pixel1);
            material.SetFloat("_DisableChromatic",chromaticAberration?0:1);
            material.SetTexture("_T0",source);material.SetTexture("_T1",Texture2D.grayTexture);
            material.SetTexture("_T2",lut);material.SetTexture("_T3",Texture2D.blackTexture);
            material.SetTexture("_T4",fxBloom?fxBloom:Texture2D.blackTexture);material.SetTexture("_T5",Texture2D.blackTexture);
            Graphics.SetRenderTarget(destination);GL.sRGBWrite=destination?destination.sRGB:QualitySettings.activeColorSpace==ColorSpace.Linear;
            if(!material.SetPass(6))throw new System.InvalidOperationException("Captured combat final pass unavailable");
            Graphics.DrawProceduralNow(MeshTopology.Triangles,6);
        }finally{RenderTexture.active=previous;GL.sRGBWrite=srgb;}
    }
    void OnDestroy(){if(material){if(Application.isPlaying)Destroy(material);else DestroyImmediate(material);}}
}
