using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// Ordered native UI geometry producer. Owns private skin meshes, CBs and MRTs;
// never replaces the formal model's shared meshes, materials or animation data.
public sealed class RemielleNativeUIRenderer : IDisposable
{
    public sealed class ConstantState
    {
        public RemielleNativeUIProfile.Constant source;
        public byte[] bytes;
        public GraphicsBuffer buffer;
        public Vector4[] upload;
    }
    public sealed class DrawState
    {
        public RemielleNativeUIProfile.Draw source;
        public Mesh mesh;
        public Material material;
        public int pass;
        public ConstantState[] constants;
        public readonly List<RemielleNativeUIEntityBuffer> entities=new();
    }
    public readonly RemielleNativeUIProfile Profile;
    public readonly DrawState[] Draws;
    public readonly RemielleNativeUIMeshBinding[] Meshes;
    public RenderTexture[] Targets { get; private set; }
    public RenderTexture Depth { get; private set; }
    public RenderTexture DepthRead { get; private set; }
    public RemielleNativeUIConstants.Frame CurrentFrame { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    readonly Camera camera;
    readonly Transform pelvis, head;
    readonly List<Texture> ownedTextures = new();
    readonly Dictionary<Texture,Texture> textureCopies = new();
    readonly Material depthReader;
    readonly CommandBuffer commands = new() { name = "Remielle native UI live geometry" };
    bool disposed, prepared;
    RemielleNativeUIConstants.Frame previous;

    public RemielleNativeUIRenderer(RemielleNativeUIProfile profile, RemielleNativeAnimation model, Camera camera)
    {
        if (!profile || !model || !camera || profile.draws.Length != 24 || profile.sourceMeshes.Length != 7)
            throw new ArgumentException("Incomplete native UI profile");
        if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D11 || !SystemInfo.usesReversedZBuffer)
            throw new NotSupportedException("Native UI rendering requires D3D11 reversed depth");
        Profile=profile;this.camera=camera;
        pelvis=model.bones.Single(b=>b.source.name=="Bip001 Pelvis").source;
        head=model.bones.Single(b=>b.source.name=="Bip001 Head").source;
        Meshes=new RemielleNativeUIMeshBinding[profile.sourceMeshes.Length];
        Draws=new DrawState[profile.draws.Length];
        depthReader=new Material(profile.depthReaderShader){hideFlags=HideFlags.HideAndDontSave};
        try
        {
            var renderers=model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for(int i=0;i<Meshes.Length;i++)
            {
                var source=profile.sourceMeshes[i];
                var renderer=renderers.Single(r=>r.name=="SMR_"+source.name && r.sharedMesh==source.importedMesh);
                var native=model.bones.Single(b=>b.source.name==source.rootName).source;
                Meshes[i]=new RemielleNativeUIMeshBinding(renderer,source.importedMesh,native);
            }
            for(int i=0;i<Draws.Length;i++)
            {
                var source=profile.draws[i]; if(source.relative!=i)throw new InvalidOperationException("Native draw ordering mismatch");
                var draw=new DrawState{source=source,mesh=Object.Instantiate(source.template),
                    material=new Material(profile.orderedShader){hideFlags=HideFlags.HideAndDontSave}};
                Draws[i]=draw;draw.mesh.hideFlags=HideFlags.HideAndDontSave;draw.mesh.MarkDynamic();
                draw.pass=draw.material.FindPass("DRAW_"+i);if(draw.pass<0)throw new InvalidOperationException("Native draw pass missing");
                if(source.samplerS2B)draw.material.EnableKeyword("UI_S2_FIXTURE_B");
                draw.constants=new ConstantState[source.constants.Length];
                for(int j=0;j<source.constants.Length;j++)
                {
                    var cb=source.constants[j];int count=cb.template.Length/16;
                    if(count<1||cb.template.Length%16!=0)throw new InvalidOperationException("Invalid native constant extent");
                    var state=new ConstantState{source=cb,bytes=(byte[])cb.template.Clone(),upload=new Vector4[count],
                        buffer=new GraphicsBuffer(GraphicsBuffer.Target.Constant,count,16)};
                    draw.constants[j]=state;
                    draw.material.SetConstantBuffer("Seq"+cb.stage.ToUpperInvariant()+"Cb"+cb.slot,state.buffer,0,cb.template.Length);
                }
                foreach(var resource in source.resources)
                {
                    if(resource.texture)
                    {
                        if(!textureCopies.TryGetValue(resource.texture,out var copy))
                        {
                            copy=Object.Instantiate(resource.texture);ownedTextures.Add(copy);textureCopies.Add(resource.texture,copy);
                            copy.hideFlags=HideFlags.HideAndDontSave;copy.filterMode=FilterMode.Trilinear;copy.wrapMode=TextureWrapMode.Repeat;copy.anisoLevel=8;
                        }
                        draw.material.SetTexture(resource.binding,copy);
                    }
                    else
                    {
                        var entity=new RemielleNativeUIEntityBuffer(resource);draw.entities.Add(entity);
                        draw.material.SetBuffer(resource.binding,entity.Buffer);
                    }
                }
            }
        }
        catch {Dispose();throw;}
    }

    public void Prepare(int width,int height,Vector2 jitterPixels=default)
    {
        if(disposed||prepared)throw new InvalidOperationException("Native UI frame lifecycle mismatch");
        if(width<1||height<1)throw new ArgumentException("Invalid native UI dimensions");
        EnsureTargets(width,height);
        CurrentFrame=new RemielleNativeUIConstants.Frame(camera,width,height,Profile.sceneToProfile,previous,jitterPixels);
        foreach(var mesh in Meshes)mesh.Prepare();
        foreach(var draw in Draws)
        {
            foreach(var entity in draw.entities){entity.Reset();entity.Upload();}
            var mesh=Meshes[draw.source.meshIndex];mesh.ApplyTo(draw.mesh);
            foreach(var cb in draw.constants)
            {
                Buffer.BlockCopy(cb.source.template,0,cb.bytes,0,cb.bytes.Length);
                RemielleNativeUIConstants.Patch(cb.bytes,cb.source.fields,CurrentFrame,mesh,pelvis,head);
                PatchAssemblyVertex(cb,mesh);
                PatchHeadSphere(cb);
                CopyStructs(cb.bytes,cb.upload);cb.buffer.SetData(cb.upload);
            }
        }
        prepared=true;
    }

    // Retains the captured head-local sphere offset and its authored radius.
    void PatchHeadSphere(ConstantState cb)
    {
        foreach(var field in cb.source.fields)
        {
            if(field.name!="_HeadSphereNormalCenter"||field.offsetBytes+16>cb.bytes.Length)continue;
            var old=ReadVector(cb.source.template,field.offsetBytes);
            var local=Profile.capturedHead.inverse.MultiplyPoint3x4(old);
            var current=(CurrentFrame.sceneToProfile*head.localToWorldMatrix).MultiplyPoint3x4(local);
            Put(cb.bytes,field.offsetBytes,new Vector4(current.x,current.y,current.z,old.w));
        }
    }

    // This VS has captured assembly but no original DXBC reflection. Offsets
    // follow its explicit cb0[30], cb0[102..105], cb0[169], cb1[0..3] reads.
    void PatchAssemblyVertex(ConstantState cb,RemielleNativeUIMeshBinding mesh)
    {
        if(cb.source.stage!="vs"||cb.source.shader!="bcddceab76ee10ce")return;
        if(cb.source.slot==0)
        {
            Put(cb.bytes,30*16,CurrentFrame.cameraPosition);
            for(int i=0;i<4;i++)Put(cb.bytes,(102+i)*16,CurrentFrame.vp.GetColumn(i));
            var clip=ReadVector(cb.bytes,169*16);clip.x=clip.y=0;Put(cb.bytes,169*16,clip);
        }
        if(cb.source.slot==1)
            for(int i=0;i<4;i++)Put(cb.bytes,i*16,(CurrentFrame.sceneToProfile*mesh.ObjectToWorld).GetColumn(i));
    }

    public void Render(Action<int> afterDraw=null)
    {
        if(disposed||!prepared)throw new InvalidOperationException("Prepare the native UI frame before rendering");
        var oldAniso=QualitySettings.anisotropicFiltering;
        try
        {
            QualitySettings.anisotropicFiltering=AnisotropicFiltering.Enable;
            for(int i=0;i<Draws.Length;i++)
            {
                var draw=Draws[i];commands.Clear();
                commands.SetRenderTarget(Targets.Select(t=>new RenderTargetIdentifier(t)).ToArray(),new RenderTargetIdentifier(Depth));
                commands.SetViewport(new Rect(0,0,Width,Height));
                if(i==0)commands.ClearRenderTarget(RTClearFlags.All,Color.clear,1,0);
                commands.SetInvertCulling(false);commands.SetViewProjectionMatrices(Matrix4x4.identity,Matrix4x4.identity);
                commands.DrawMesh(draw.mesh,Matrix4x4.identity,draw.material,0,draw.pass);
                Graphics.ExecuteCommandBuffer(commands);
                if(afterDraw!=null){ReadDepth();afterDraw(i);}
            }
            ReadDepth();
            foreach(var mesh in Meshes)mesh.Commit();previous=CurrentFrame;prepared=false;
        }
        finally {QualitySettings.anisotropicFiltering=oldAniso;}
    }
    void ReadDepth()
    {
        commands.Clear();commands.SetRenderTarget(DepthRead);commands.SetViewport(new Rect(0,0,Width,Height));
        commands.DrawProcedural(Matrix4x4.identity,depthReader,0,MeshTopology.Triangles,3);Graphics.ExecuteCommandBuffer(commands);
    }
    void EnsureTargets(int width,int height)
    {
        if(Width==width&&Height==height&&Depth)return;
        ReleaseTargets();Width=width;Height=height;
        Targets=new[]{Target(GraphicsFormat.R16G16B16A16_SFloat),Target(GraphicsFormat.R8G8B8A8_SRGB),
            Target(GraphicsFormat.A2B10G10R10_UNormPack32),Target(GraphicsFormat.A2B10G10R10_UNormPack32)};
        Depth=Target(GraphicsFormat.None,GraphicsFormat.D32_SFloat_S8_UInt);
        DepthRead=Target(GraphicsFormat.R32G32B32A32_SFloat);
        depthReader.SetTexture("_SeqDepth",Depth,RenderTextureSubElement.Depth);
        depthReader.SetTexture("_SeqStencil",Depth,RenderTextureSubElement.Stencil);
    }
    RenderTexture Target(GraphicsFormat color,GraphicsFormat depth=GraphicsFormat.None)
    {
        var d=new RenderTextureDescriptor(Width,Height){graphicsFormat=color,depthStencilFormat=depth,msaaSamples=1,mipCount=1};
        if(depth==GraphicsFormat.D32_SFloat_S8_UInt)d.stencilFormat=GraphicsFormat.R8_UInt;
        var t=new RenderTexture(d){hideFlags=HideFlags.HideAndDontSave};if(!t.Create()){Destroy(t);throw new InvalidOperationException("Native UI target creation failed");}return t;
    }
    static void CopyStructs<T>(byte[] bytes,T[] data) where T:struct
    {
        var pin=GCHandle.Alloc(data,GCHandleType.Pinned);try{Marshal.Copy(bytes,0,pin.AddrOfPinnedObject(),bytes.Length);}finally{pin.Free();}
    }
    static Vector4 ReadVector(byte[] b,int offset)=>new(BitConverter.ToSingle(b,offset),BitConverter.ToSingle(b,offset+4),BitConverter.ToSingle(b,offset+8),BitConverter.ToSingle(b,offset+12));
    static void Put(byte[] b,int offset,Vector4 v){for(int i=0;i<4;i++)BitConverter.TryWriteBytes(b.AsSpan(offset+i*4,4),v[i]);}
    static void Destroy(Object o){if(!o)return;if(Application.isPlaying)Object.Destroy(o);else Object.DestroyImmediate(o);}
    void ReleaseTargets(){if(Targets!=null)foreach(var t in Targets){if(t)t.Release();Destroy(t);}Targets=null;if(Depth)Depth.Release();Destroy(Depth);if(DepthRead)DepthRead.Release();Destroy(DepthRead);Depth=DepthRead=null;}
    public void Dispose()
    {
        if(disposed)return;disposed=true;commands.Release();ReleaseTargets();Destroy(depthReader);
        if(Meshes!=null)foreach(var mesh in Meshes)mesh?.Dispose();
        if(Draws!=null)foreach(var draw in Draws){if(draw==null)continue;Destroy(draw.mesh);Destroy(draw.material);if(draw.constants!=null)foreach(var cb in draw.constants)cb?.buffer?.Dispose();foreach(var entity in draw.entities)entity.Dispose();}
        foreach(var t in ownedTextures)Destroy(t);
    }
}
