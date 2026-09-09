using System;
using UnityEngine;

// The Animator source rig keeps its authored local transform conventions.
// A constant per-bone basis maps those poses into the assembled mesh skeleton.
// No animation speed multiplication, key resampling or bind-pose replacement.
public class RemielleNativeAnimation : MonoBehaviour
{
    [Serializable] public struct BoneLink
    {
        public Transform source,target;
        public Matrix4x4 parentBasisInverse,basis;
        public Vector3 sourceRestPosition,sourceRestScale;
        public Quaternion sourceRestRotation;
    }
    public Animation nativeAnimation;
    [Serializable] public struct MorphLink { public SkinnedMeshRenderer source,target; }
    public MorphLink[] morphs=Array.Empty<MorphLink>();
    public string initialClip="Idle_Loop";
    public BoneLink[] bones;
    public bool autoplay=true;
    public void Play(string clip,float fade=0.18f)
    {
        if(nativeAnimation==null||nativeAnimation[clip]==null)throw new ArgumentException("Unavailable native clip: "+clip);
        nativeAnimation.CrossFade(clip,fade,PlayMode.StopAll);
    }
    void Start(){if(autoplay)Play(initialClip,0);}
    // Legacy Animation only writes channels present in an active clip. Restore
    // the authored source defaults before evaluation, so omitted skirt/twist/FX
    // channels cannot inherit an unrelated previous action. This also supplies
    // a stable base for partially weighted cross-fades without rewriting clips.
    void Update()
    {
        if(nativeAnimation!=null&&nativeAnimation.enabled&&nativeAnimation.isPlaying&&Time.deltaTime>0)
            ResetSourcePose();
    }
    public void ResetSourcePose()
    {
        foreach(var link in bones)
        {
            link.source.localPosition=link.sourceRestPosition;
            link.source.localRotation=link.sourceRestRotation;
            link.source.localScale=link.sourceRestScale;
        }
        foreach(var link in morphs)
            for(int i=0;i<link.source.sharedMesh.blendShapeCount;i++)link.source.SetBlendShapeWeight(i,0);
    }
    void LateUpdate(){ApplyPose();}
    public void ApplyPose()
    {
        foreach(var link in bones)
        {
            var local=Matrix4x4.TRS(link.source.localPosition,link.source.localRotation,link.source.localScale);
            var mapped=link.parentBasisInverse*local*link.basis;
            link.target.localPosition=mapped.GetColumn(3);
            Vector3 x=mapped.GetColumn(0),y=mapped.GetColumn(1),z=mapped.GetColumn(2);
            var scale=new Vector3(x.magnitude,y.magnitude,z.magnitude);
            if(Vector3.Dot(Vector3.Cross(x,y),z)<0)scale.x=-scale.x;
            // Animation scales can deliberately collapse hidden accessories.
            // Preserve a stable rotation when an axis has no orientation.
            if(Mathf.Abs(scale.x)>1e-8f&&scale.y>1e-8f&&scale.z>1e-8f)
                link.target.localRotation=Quaternion.LookRotation(z/scale.z,y/scale.y);
            link.target.localScale=scale;
        }
        foreach(var link in morphs)
            for(int i=0;i<link.source.sharedMesh.blendShapeCount;++i)
                link.target.SetBlendShapeWeight(i,link.source.GetBlendShapeWeight(i));
        // Also refresh for explicit editor Sample() calls, outside LateUpdate.
        GetComponent<RemielleFaceLighting>()?.Apply();
    }
    public void Sample(string clip,float time)
    {
        var state=nativeAnimation[clip];
        if(state==null)throw new ArgumentException(clip);
        foreach(AnimationState other in nativeAnimation)other.enabled=false;
        ResetSourcePose();
        state.enabled=true;state.weight=1;state.time=time;nativeAnimation.Sample();ApplyPose();state.enabled=false;
    }
}
