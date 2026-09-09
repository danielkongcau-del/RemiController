using System;
using UnityEngine;

// Build-time source-qualified data. A Player does not read capture directories.
public sealed class RemielleNativeUIProfile : ScriptableObject
{
    [Serializable] public sealed class SourceMesh
    {
        public string name, sourceBlock, cab, pathID, rootName;
        public Mesh importedMesh;
    }
    [Serializable] public sealed class Constant
    {
        public string stage, shader;
        public int slot;
        public byte[] template;
        public RemielleNativeUIConstants.Field[] fields;
    }
    [Serializable] public sealed class Resource
    {
        public string binding;
        public Texture texture;
        public byte[] structured;
    }
    [Serializable] public sealed class Draw
    {
        public string captureID, role;
        public int relative, meshIndex;
        public Mesh template;
        public bool samplerS2B;
        public Constant[] constants;
        public Resource[] resources;
    }
    public string profileName, sourceManifestSha256;
    public Shader orderedShader, depthReaderShader;
    public SourceMesh[] sourceMeshes;
    public Draw[] draws;
    public Matrix4x4 sceneToProfile, capturedHead;
}
