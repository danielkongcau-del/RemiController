using UnityEngine;
[RequireComponent(typeof(Camera))]
public class RemiellePostLut : MonoBehaviour
{
    public Texture2D lut;
    // Serialized by the scene builder so player shader stripping retains it.
    public Shader lutShader;
    Material material;
    void OnRenderImage(RenderTexture source,RenderTexture destination)
    {
        if(lut==null){Graphics.Blit(source,destination);return;}
        if(material==null)
        {
            if(lutShader==null)lutShader=Shader.Find("Hidden/Remielle/NativeRuntimeLut");
            material=new Material(lutShader);
        }
        material.SetTexture("_Lut2DTex",lut);material.SetFloat("_Post",1);
        material.SetVector("_Lut2DTexParam",new Vector4(1f/4096,1f/64,63,1));
        Graphics.Blit(source,destination,material);
    }
    void OnDestroy(){if(material!=null)Destroy(material);}
}
