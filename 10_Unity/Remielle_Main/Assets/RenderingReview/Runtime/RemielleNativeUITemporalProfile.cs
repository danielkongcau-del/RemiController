using UnityEngine;

public sealed class RemielleNativeUITemporalProfile : ScriptableObject
{
    public string profileName,sourceManifestSha256;
    public Shader shader;
    public Vector4[] constants;
}
