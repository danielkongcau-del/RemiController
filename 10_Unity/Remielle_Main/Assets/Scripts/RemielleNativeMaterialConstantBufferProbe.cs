using System;
using UnityEngine;

// Verifies the Unity-side binding mechanism for the captured native cb0-cb4
// layouts. It does not render the captured material body into the visible path.
public class RemielleNativeMaterialConstantBufferProbe : MonoBehaviour
{
    public Shader shader;
    public Shader readbackShader;
    public TextAsset[] capturedBuffers;
    public int boundBuffers,activatedPasses,gpuReadbacks,queryVisibleBuffers;
    static readonly int[] Float4Counts={209,29,27,41,170};

    public void Verify()
    {
        if(!SystemInfo.supportsSetConstantBuffer)throw new InvalidOperationException("Constant buffer override is unsupported");
        if(!shader||!shader.isSupported||!readbackShader||!readbackShader.isSupported||capturedBuffers==null||capturedBuffers.Length!=5)throw new InvalidOperationException("Captured native constant-buffer probe is incomplete");
        var material=new Material(shader){hideFlags=HideFlags.HideAndDontSave};
        var readbackMaterial=new Material(readbackShader){hideFlags=HideFlags.HideAndDontSave};
        var buffers=new GraphicsBuffer[5];
        var expected=new Vector4[5];
        var target=new RenderTexture(1,1,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear){hideFlags=HideFlags.HideAndDontSave};
        var readback=new Texture2D(1,1,TextureFormat.RGBAFloat,false,true){hideFlags=HideFlags.HideAndDontSave};
        var previous=RenderTexture.active;
        try
        {
            boundBuffers=0;activatedPasses=0;gpuReadbacks=0;queryVisibleBuffers=0;
            target.Create();
            for(int index=0;index<5;index++)
            {
                string name="CapturedCb"+index;
                if(material.HasConstantBuffer(name))queryVisibleBuffers++;
                int count=Float4Counts[index];
                var scalar=new float[count*4];
                byte[] source=capturedBuffers[index].bytes;
                if(source.Length<count*16)throw new InvalidOperationException(name+" captured bytes are shorter than the unified layout");
                Buffer.BlockCopy(source,0,scalar,0,count*16);
                expected[index]=new Vector4(scalar[0],scalar[1],scalar[2],scalar[3]);
                var values=new Vector4[count];
                for(int i=0;i<count;i++)values[i]=new Vector4(scalar[i*4],scalar[i*4+1],scalar[i*4+2],scalar[i*4+3]);
                buffers[index]=new GraphicsBuffer(GraphicsBuffer.Target.Constant,count,16);
                buffers[index].SetData(values);
                material.SetConstantBuffer(name,buffers[index],0,count*16);
                readbackMaterial.SetConstantBuffer(name,buffers[index],0,count*16);
                boundBuffers++;
            }
            for(int pass=0;pass<material.passCount;pass++)if(material.SetPass(pass))activatedPasses++;
            for(int pass=0;pass<5;pass++)
            {
                Graphics.Blit(Texture2D.whiteTexture,target,readbackMaterial,pass);
                RenderTexture.active=target;readback.ReadPixels(new Rect(0,0,1,1),0,0,false);readback.Apply(false,false);
                Vector4 actual=readback.GetPixel(0,0);
                if((actual-expected[pass]).sqrMagnitude>1e-8f)throw new InvalidOperationException("CapturedCb"+pass+" GPU readback mismatch: expected="+expected[pass]+" actual="+actual);
                gpuReadbacks++;
            }
            if(boundBuffers!=5||activatedPasses!=6||gpuReadbacks!=5)throw new InvalidOperationException("Captured native binding smoke test failed: buffers="+boundBuffers+" passes="+activatedPasses+" readbacks="+gpuReadbacks);
        }
        finally
        {
            RenderTexture.active=previous;
            target.Release();
            foreach(var buffer in buffers)buffer?.Dispose();
            if(Application.isPlaying){Destroy(readback);Destroy(target);Destroy(readbackMaterial);Destroy(material);}
            else{DestroyImmediate(readback);DestroyImmediate(target);DestroyImmediate(readbackMaterial);DestroyImmediate(material);}
        }
    }
}
