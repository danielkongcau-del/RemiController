using UnityEngine;

// Separate post-Deferred forward pass; the 24 G-buffer draws keep their order.
public sealed class RemielleNativeUILateBodyProfile : ScriptableObject
{
    public string profileName,sourceManifestSha256;
    public Shader shader;
    public RemielleNativeUIProfile geometryProfile;
    public int bodyMeshIndex;
    public Mesh template;
    public RemielleNativeUIProfile.Constant[] constants;
    public RemielleNativeUIProfile.Resource[] resources;
}
