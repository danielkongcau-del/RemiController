using UnityEngine;

// Only static source settings/resources belong here. Current HDR/Bloom frames
// are supplied by the live pipeline, never embedded capture screenshots.
public sealed class RemielleNativeUIPostProfile : ScriptableObject
{
    public string profileName,sourceManifestSha256;
    public Shader finalShader,bloomShader;
    public TextAsset bloomData;
    public Mesh fullscreenQuad;
    public Texture grain,distortion,dirt;
    public Vector4[] vertex0,vertex1,pixel0,pixel1;
}
