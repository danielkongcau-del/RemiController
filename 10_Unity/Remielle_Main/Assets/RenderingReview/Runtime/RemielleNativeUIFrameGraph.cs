using System;
using System.Linq;
using UnityEngine;

// One graph per camera. The camera's history and private resources must never
// be shared with a Scene view, second camera or another profile.
public sealed class RemielleNativeUIFrameGraph : IDisposable
{
    public readonly RemielleNativeUIPresentationProfile Profile;
    public readonly RemielleNativeUIRenderer Geometry;
    public readonly RemielleNativeUILateBody LateBody;
    public readonly RemielleNativeUIDepthHierarchy DepthHierarchy;
    public readonly RemielleNativeUILighting Lighting;
    public readonly RemielleNativeUILightRig LightRig;
    public readonly RemielleNativeUITemporal Temporal;
    public readonly RemielleNativeUIPost Post;
    public long Frames {get;private set;}
    public RenderTexture FinalColor=>Post.FinalColor;
    public bool DepthHierarchyUpdated {get;private set;}
    readonly Camera camera;
    readonly RemielleNativeAnimation model;
    readonly Transform pelvis;
    bool disposed;

    public RemielleNativeUIFrameGraph(RemielleNativeUIPresentationProfile profile,RemielleNativeAnimation model,Camera camera)
    {
        if(!profile||!model||!camera)throw new ArgumentException("Native presentation needs model, profile and camera");
        profile.Validate();Profile=profile;this.model=model;this.camera=camera;
        pelvis=model.bones.Single(b=>b.source.name=="Bip001 Pelvis").source;
        try
        {
            Geometry=new RemielleNativeUIRenderer(profile.geometry,model,camera);
            LateBody=new RemielleNativeUILateBody(profile.lateBody);
            DepthHierarchy=new RemielleNativeUIDepthHierarchy(profile.depthHierarchyShader);
            Lighting=new RemielleNativeUILighting(profile.deferredShader,profile.characterLut,profile.deferredConstants);
            LightRig=new RemielleNativeUILightRig(profile.geometry);
            Temporal=new RemielleNativeUITemporal(profile.temporal);Post=new RemielleNativeUIPost(profile.post);
        }
        catch{Dispose();throw;}
    }
    public void Render(int width,int height,Quaternion lightRotation,Vector3 mainMultiplier,float ambientMultiplier,bool resetHistory)
    {
        if(disposed||width<4||height<4||camera.orthographic)throw new InvalidOperationException("Native presentation requires a perspective viewport of at least 4 pixels per axis");
        var jitter=Temporal.BeginFrame(width,height,resetHistory);
        Geometry.Prepare(width,height,jitter);LateBody.Prepare(Geometry,model);
        var anchor=Geometry.CurrentFrame.sceneToProfile.MultiplyPoint3x4(pelvis.position);
        LightRig.Apply(Geometry,LateBody,anchor,lightRotation,mainMultiplier,ambientMultiplier);
        Geometry.Render();DepthHierarchy.Render(Geometry,camera);DepthHierarchyUpdated=true;
        // The half-resolution targets are produced for downstream consumers.
        // UI Deferred itself uses the original full-resolution inputs.
        Lighting.Render(Geometry,camera,LightRig);LateBody.Draw(Lighting.Hdr,Geometry.Depth);
        Temporal.Render(Lighting.SampledDepth,Lighting.Auxiliary,Lighting.Hdr);
        Post.Render(Lighting.Hdr,Temporal.Color);Frames++;
    }
    public void FrameCamera(float yaw,float elevation,float distanceMultiplier=1)
    {
        var points=Geometry.Meshes.SelectMany(mesh=>{mesh.Prepare();return mesh.Positions.Select(p=>mesh.ObjectToWorld.MultiplyPoint3x4(p));}).ToArray();
        if(points.Length==0)throw new InvalidOperationException("Native framing geometry missing");
        var bounds=new Bounds(points[0],Vector3.zero);foreach(var p in points)bounds.Encapsulate(p);
        var offset=Quaternion.Euler(elevation,yaw,0)*Vector3.forward;
        var rotation=Quaternion.LookRotation(-offset,Vector3.up);var inverse=Quaternion.Inverse(rotation);
        float ty=Mathf.Tan(camera.fieldOfView*Mathf.Deg2Rad*.5f),tx=ty*camera.aspect,distance=0;
        foreach(var p in points){var local=inverse*(p-bounds.center);distance=Mathf.Max(distance,Mathf.Abs(local.x)/tx-local.z,Mathf.Abs(local.y)/ty-local.z);}
        camera.transform.SetPositionAndRotation(bounds.center+offset*(distance*1.1f*distanceMultiplier),rotation);
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        Post?.Dispose();Temporal?.Dispose();Lighting?.Dispose();DepthHierarchy?.Dispose();LateBody?.Dispose();Geometry?.Dispose();
    }
}
