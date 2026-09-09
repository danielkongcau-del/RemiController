using System;
using System.Collections.Generic;
using UnityEngine;

// Native UI vertex programs consume positions in the original renderer's root
// bone frame. The assembled SMR.rootBone is a common culling/skinning root and
// must not be substituted for that original, source-qualified Transform.
public sealed class RemielleNativeUIMeshBinding : IDisposable
{
    public readonly SkinnedMeshRenderer Renderer;
    public readonly Transform NativeRoot;
    public readonly Vector3[] Positions, Normals, PreviousPositions;
    public readonly Vector4[] Tangents;
    public Matrix4x4 ObjectToWorld { get; private set; }
    public Matrix4x4 PreviousObjectToWorld { get; private set; }
    public bool HasHistory { get; private set; }
    public float PositionRoundTripMaxError { get; private set; }
    public Mesh BakedMesh => baked;
    readonly Mesh sourceMesh, baked;
    readonly List<Vector3> rawPositions, rawNormals;
    readonly List<Vector4> rawTangents;
    bool prepared, disposed;

    public RemielleNativeUIMeshBinding(SkinnedMeshRenderer renderer, Mesh expectedMesh, Transform originalRoot)
    {
        if (!renderer || !expectedMesh || renderer.sharedMesh != expectedMesh || !originalRoot)
            throw new ArgumentException("Native UI source binding does not match the saved mesh");
        Renderer = renderer; sourceMesh = expectedMesh; NativeRoot = originalRoot;
        int count = expectedMesh.vertexCount;
        Positions = new Vector3[count]; Normals = new Vector3[count]; Tangents = new Vector4[count];
        PreviousPositions = new Vector3[count];
        rawPositions = new List<Vector3>(count); rawNormals = new List<Vector3>(count); rawTangents = new List<Vector4>(count);
        baked = new Mesh { name = renderer.name + " native UI skin", hideFlags = HideFlags.HideAndDontSave };
    }

    public void Prepare()
    {
        if (disposed || Renderer.sharedMesh != sourceMesh) throw new InvalidOperationException("Native UI mesh binding changed");
        Renderer.BakeMesh(baked, false);
        rawPositions.Clear(); rawNormals.Clear(); rawTangents.Clear();
        baked.GetVertices(rawPositions); baked.GetNormals(rawNormals); baked.GetTangents(rawTangents);
        if (rawPositions.Count != Positions.Length || rawNormals.Count != Positions.Length || rawTangents.Count != Positions.Length)
            throw new InvalidOperationException("Native UI current skin is missing vertex channels");
        ObjectToWorld = NativeRoot.localToWorldMatrix;
        var rendererToWorld = Renderer.localToWorldMatrix;
        var nativeFromRenderer = ObjectToWorld.inverse * rendererToWorld;
        var normalFromRenderer = nativeFromRenderer.inverse.transpose;
        float determinant = nativeFromRenderer.determinant;
        if (!float.IsFinite(determinant) || Mathf.Abs(determinant) < 1e-8f)
            throw new InvalidOperationException("Native UI root frame collapsed");
        float tangentSign = determinant < 0 ? -1 : 1;
        PositionRoundTripMaxError = 0;
        for (int i = 0; i < Positions.Length; i++)
        {
            Positions[i] = nativeFromRenderer.MultiplyPoint3x4(rawPositions[i]);
            Normals[i] = normalFromRenderer.MultiplyVector(rawNormals[i]);
            var tangent = nativeFromRenderer.MultiplyVector(rawTangents[i]);
            Tangents[i] = new Vector4(tangent.x, tangent.y, tangent.z, rawTangents[i].w * tangentSign);
            var world = ObjectToWorld.MultiplyPoint3x4(Positions[i]);
            var error = Vector3.Distance(world, rendererToWorld.MultiplyPoint3x4(rawPositions[i]));
            if (!Finite(world) || !Finite(Normals[i]) || !Finite(tangent) || !float.IsFinite(Tangents[i].w))
                throw new InvalidOperationException("Nonfinite native UI skin input");
            PositionRoundTripMaxError = Mathf.Max(PositionRoundTripMaxError, error);
        }
        if (!HasHistory)
        {
            Array.Copy(Positions, PreviousPositions, Positions.Length);
            PreviousObjectToWorld = ObjectToWorld;
        }
        prepared = true;
    }

    public void ApplyTo(Mesh privateDrawMesh)
    {
        if (!prepared || !privateDrawMesh || privateDrawMesh.vertexCount != Positions.Length)
            throw new InvalidOperationException("Native UI draw mesh is not prepared");
        privateDrawMesh.SetVertices(Positions);
        privateDrawMesh.SetNormals(Normals);
        privateDrawMesh.SetTangents(Tangents);
        privateDrawMesh.SetUVs(4, PreviousPositions);
        // UV0-3, vertex colors and original ordered indices belong to the
        // source-qualified native draw template; do not copy imported aliases.
        privateDrawMesh.RecalculateBounds();
    }

    // Call after the whole ordered draw chain, not after individual passes.
    public void Commit()
    {
        if (!prepared) throw new InvalidOperationException("Native UI pose was not prepared");
        Array.Copy(Positions, PreviousPositions, Positions.Length);
        PreviousObjectToWorld = ObjectToWorld;
        HasHistory = true; prepared = false;
    }

    static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (Application.isPlaying) UnityEngine.Object.Destroy(baked); else UnityEngine.Object.DestroyImmediate(baked);
    }
}
