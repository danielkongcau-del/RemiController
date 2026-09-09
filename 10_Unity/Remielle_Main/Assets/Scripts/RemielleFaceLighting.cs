using System;
using UnityEngine;

// Supply explicit world-space directions. Unity's live skinning path can use
// a GPU object matrix different from the CPU Transform/Renderer matrices;
// transforming these directions again in the shader breaks the face SDF.
[ExecuteAlways, DefaultExecutionOrder(100)]
public class RemielleFaceLighting : MonoBehaviour
{
    public Transform head;
    public Renderer[] renderers = Array.Empty<Renderer>();
    public Vector3 headLocalForward, headLocalRight, headLocalUp;
    MaterialPropertyBlock properties;
    static readonly int Forward = Shader.PropertyToID("_headForwardVector");
    static readonly int Right = Shader.PropertyToID("_headRightVector");
    static readonly int Up = Shader.PropertyToID("_headUpVector");
    static readonly int WorldSpace = Shader.PropertyToID("_HeadDirectionsWorldSpace");

    // Call once in the verified upright, +Z-facing bind pose, never in an
    // arbitrary animation pose: that would silently redefine the face axes.
    public void Calibrate(Transform bindHead, Renderer[] targets)
    {
        head = bindHead; renderers = targets;
        headLocalForward = head.InverseTransformDirection(Vector3.forward);
        headLocalRight = head.InverseTransformDirection(Vector3.left);
        headLocalUp = head.InverseTransformDirection(Vector3.up);
        Apply();
    }
    public void Apply()
    {
        if (head == null) return;
        properties ??= new MaterialPropertyBlock();
        foreach (var renderer in renderers)
        {
            if (renderer == null) continue;
            renderer.GetPropertyBlock(properties);
            properties.SetFloat(WorldSpace, 1);
            properties.SetVector(Forward, head.TransformDirection(headLocalForward).normalized);
            properties.SetVector(Right, head.TransformDirection(headLocalRight).normalized);
            properties.SetVector(Up, head.TransformDirection(headLocalUp).normalized);
            renderer.SetPropertyBlock(properties);
        }
    }
    void OnEnable() { Apply(); }
    void LateUpdate() { Apply(); }
}
