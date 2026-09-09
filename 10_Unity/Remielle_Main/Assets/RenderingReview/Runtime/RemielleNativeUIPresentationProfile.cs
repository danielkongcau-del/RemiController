using System;
using UnityEngine;

// A Player-ready composition of source-qualified native stages. No paths to
// capture files or editor-only AssetDatabase lookups are needed at runtime.
public sealed class RemielleNativeUIPresentationProfile : ScriptableObject
{
    public string profileName,deferredSourceSha256;
    public RemielleNativeUIProfile geometry;
    public RemielleNativeUILateBodyProfile lateBody;
    public RemielleNativeUITemporalProfile temporal;
    public RemielleNativeUIPostProfile post;
    public Shader deferredShader,depthHierarchyShader;
    public Texture characterLut;
    public Vector4[] deferredConstants;
    public void Validate()
    {
        if(!geometry||!lateBody||!temporal||!post||!deferredShader||!depthHierarchyShader||!characterLut||
            deferredConstants?.Length!=185||lateBody.geometryProfile!=geometry||
            geometry.profileName!=profileName||temporal.profileName!=profileName||post.profileName!=profileName)
            throw new InvalidOperationException("Incomplete or mixed native presentation profile: "+name);
        foreach(var shader in new[]{geometry.orderedShader,geometry.depthReaderShader,lateBody.shader,
            temporal.shader,post.finalShader,post.bloomShader,deferredShader,depthHierarchyShader})
            if(!shader||!shader.isSupported)throw new InvalidOperationException("Native presentation shader unavailable: "+shader);
    }
}
