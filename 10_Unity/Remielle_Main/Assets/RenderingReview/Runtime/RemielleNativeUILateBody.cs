using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

public sealed class RemielleNativeUILateBody : IDisposable
{
    public readonly RemielleNativeUILateBodyProfile Profile;
    public readonly Mesh Mesh;
    public readonly Material Material;
    public readonly RemielleNativeUIRenderer.ConstantState[] Constants;
    public readonly List<RemielleNativeUIEntityBuffer> Entities=new();
    readonly CommandBuffer commands=new(){name="Remielle native UI late Body_1"};
    bool disposed;
    RemielleNativeUIConstants.Frame preparedFrame;

    public RemielleNativeUILateBody(RemielleNativeUILateBodyProfile profile)
    {
        if(!profile||!profile.shader||!profile.template||!profile.geometryProfile||profile.template.GetIndexCount(0)!=1014)
            throw new ArgumentException("Incomplete native late Body_1 profile");
        if(SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Direct3D11||!SystemInfo.usesReversedZBuffer)
            throw new NotSupportedException("Native late Body_1 requires D3D11 reversed depth");
        Profile=profile;Mesh=Object.Instantiate(profile.template);Mesh.hideFlags=HideFlags.HideAndDontSave;Mesh.MarkDynamic();
        Material=new Material(profile.shader){hideFlags=HideFlags.HideAndDontSave};
        Constants=new RemielleNativeUIRenderer.ConstantState[profile.constants.Length];
        try
        {
            for(int i=0;i<Constants.Length;i++)
            {
                var src=profile.constants[i];int count=src.template.Length/16;
                if(count<1||src.template.Length%16!=0)throw new InvalidOperationException("Late CB extent mismatch");
                var cb=new RemielleNativeUIRenderer.ConstantState{source=src,bytes=(byte[])src.template.Clone(),upload=new Vector4[count],buffer=new GraphicsBuffer(GraphicsBuffer.Target.Constant,count,16)};
                Constants[i]=cb;Upload(cb);Material.SetConstantBuffer("Late"+src.stage.ToUpperInvariant()+"Cb"+src.slot,cb.buffer,0,src.template.Length);
            }
            foreach(var resource in profile.resources)
            {
                if(resource.texture){Material.SetTexture(resource.binding,resource.texture);continue;}
                var entity=new RemielleNativeUIEntityBuffer(resource);Entities.Add(entity);Material.SetBuffer(resource.binding,entity.Buffer);
            }
        }
        catch{Dispose();throw;}
    }
    static void Upload(RemielleNativeUIRenderer.ConstantState cb)
    {
        var pin=GCHandle.Alloc(cb.upload,GCHandleType.Pinned);
        try{Marshal.Copy(cb.bytes,0,pin.AddrOfPinnedObject(),cb.bytes.Length);}finally{pin.Free();}
        cb.buffer.SetData(cb.upload);
    }

    // Invoke between geometry.Prepare and geometry.Render: both passes consume
    // the same baked pose before the geometry producer commits motion history.
    public void Prepare(RemielleNativeUIRenderer geometry,RemielleNativeAnimation model)
    {
        if(disposed||geometry.Profile!=Profile.geometryProfile||geometry.CurrentFrame==null||geometry.CurrentFrame==preparedFrame)
            throw new InvalidOperationException("Late Body_1 frame/profile mismatch");
        var frame=geometry.CurrentFrame;var binding=geometry.Meshes[Profile.bodyMeshIndex];binding.ApplyTo(Mesh);
        foreach(var entity in Entities){entity.Reset();entity.Upload();}
        var pelvis=model.bones.Single(b=>b.source.name=="Bip001 Pelvis").source;
        var head=model.bones.Single(b=>b.source.name=="Bip001 Head").source;
        foreach(var cb in Constants)
        {
            Buffer.BlockCopy(cb.source.template,0,cb.bytes,0,cb.bytes.Length);
            RemielleNativeUIConstants.Patch(cb.bytes,cb.source.fields,frame,binding,pelvis,head);
            foreach(var field in cb.source.fields)
            {
                if(field.name!="_HeadSphereNormalCenter"||field.offsetBytes+16>cb.bytes.Length)continue;
                int at=field.offsetBytes;var old=new Vector4(BitConverter.ToSingle(cb.source.template,at),BitConverter.ToSingle(cb.source.template,at+4),BitConverter.ToSingle(cb.source.template,at+8),BitConverter.ToSingle(cb.source.template,at+12));
                var local=Profile.geometryProfile.capturedHead.inverse.MultiplyPoint3x4(old);
                var current=(frame.sceneToProfile*head.localToWorldMatrix).MultiplyPoint3x4(local);
                for(int i=0;i<3;i++)BitConverter.TryWriteBytes(cb.bytes.AsSpan(at+i*4,4),current[i]);
            }
            Upload(cb);
        }
        preparedFrame=frame;
    }

    // The constructor also supports the immutable captured-input GPU audit.
    public void Draw(RenderTexture hdr,RenderTexture depth)
    {
        if(disposed||!hdr||!depth||hdr.width!=depth.width||hdr.height!=depth.height)
            throw new InvalidOperationException("Late Body_1 target mismatch");
        bool srgb=GL.sRGBWrite;
        try
        {
            GL.sRGBWrite=false;commands.Clear();commands.SetRenderTarget(new RenderTargetIdentifier(hdr),new RenderTargetIdentifier(depth));
            commands.SetViewport(new Rect(0,0,hdr.width,hdr.height));commands.SetInvertCulling(false);
            commands.SetViewProjectionMatrices(Matrix4x4.identity,Matrix4x4.identity);commands.DrawMesh(Mesh,Matrix4x4.identity,Material,0,0);
            Graphics.ExecuteCommandBuffer(commands);
        }
        finally{GL.sRGBWrite=srgb;}
    }
    static void Destroy(Object o){if(!o)return;if(Application.isPlaying)Object.Destroy(o);else Object.DestroyImmediate(o);}
    public void Dispose()
    {
        if(disposed)return;disposed=true;commands.Release();Destroy(Mesh);Destroy(Material);
        if(Constants!=null)foreach(var cb in Constants)cb?.buffer?.Dispose();foreach(var b in Entities)b.Dispose();
    }
}
