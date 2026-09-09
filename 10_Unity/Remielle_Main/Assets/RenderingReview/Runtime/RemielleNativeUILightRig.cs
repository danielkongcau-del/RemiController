using System;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine;

// A movable presentation rig built from the captured native lighting inputs.
// Field meanings come from source reflection/bytecode. Rigid anchoring and
// user-controlled rotation/intensity are reconstruction policies, not a claim
// that the game's WeatherConfig or light-blending CPU producer was recovered.
public sealed class RemielleNativeUILightRig
{
    public readonly RemielleNativeUIProfile Profile;
    public readonly Vector3 CapturedAnchor;
    public Vector3 Anchor {get;private set;}
    public Quaternion Rotation {get;private set;}=Quaternion.identity;
    public Vector3 MainMultiplier {get;private set;}=Vector3.one;
    public float AmbientMultiplier {get;private set;}=1;
    RemielleNativeUIConstants.Frame frame;

    public RemielleNativeUILightRig(RemielleNativeUIProfile profile)
    {
        if(!profile)throw new ArgumentNullException(nameof(profile));
        Profile=profile;
        var anchors=profile.draws.SelectMany(d=>d.constants).SelectMany(cb=>cb.fields
            .Where(f=>f.name=="_MiddlePointPosition")
            .Select(f=>Read(cb.template,f.offsetBytes))).Distinct().ToArray();
        if(anchors.Length!=1||anchors[0].w!=1)throw new InvalidOperationException("Native rig needs one captured character anchor");
        CapturedAnchor=Anchor=anchors[0];
        // Both UI profiles select entity zero and have no local additional
        // lights. Refuse other layouts instead of rewriting unrelated records.
        int packedCount=0;
        foreach(var cb in profile.draws.SelectMany(d=>d.constants))
        foreach(var field in cb.fields.Where(f=>f.name=="_PackedParams1"))
        {
            var p=Read(cb.template,field.offsetBytes);packedCount++;
            if(p.x!=0||BitConverter.ToUInt32(cb.template,field.offsetBytes+8)!=0)
                throw new NotSupportedException("Unverified entity selection/additional-light layout");
        }
        if(packedCount==0)throw new InvalidOperationException("Native light entity selection missing");
    }

    // Call after geometry.Prepare and late.Prepare, before any native draw.
    // Anchor is a point in the profile coordinate system. Rotation is a world
    // light rotation in that same frame, independent of camera and head pose.
    public void Apply(RemielleNativeUIRenderer geometry,RemielleNativeUILateBody late,
        Vector3 anchor,Quaternion rotation,Vector3 mainMultiplier,float ambientMultiplier=1)
    {
        if(geometry.Profile!=Profile||geometry.CurrentFrame==null||geometry.CurrentFrame==frame||
            (late!=null&&late.Profile.geometryProfile!=Profile))
            throw new InvalidOperationException("Native light rig frame/profile mismatch");
        float norm=rotation.x*rotation.x+rotation.y*rotation.y+rotation.z*rotation.z+rotation.w*rotation.w;
        if(!Finite(anchor)||!Finite(mainMultiplier)||mainMultiplier.x<0||mainMultiplier.y<0||mainMultiplier.z<0||
            !float.IsFinite(ambientMultiplier)||ambientMultiplier<0||!float.IsFinite(norm)||Mathf.Abs(norm-1)>1e-4f)
            throw new ArgumentException("Invalid native light rig state");
        Anchor=anchor;Rotation=rotation;MainMultiplier=mainMultiplier;AmbientMultiplier=ambientMultiplier;
        frame=geometry.CurrentFrame;
        foreach(var draw in geometry.Draws)
        {
            foreach(var cb in draw.constants)PatchConstant(cb);
            foreach(var entity in draw.entities)PatchEntity(entity);
        }
        if(late!=null)
        {
            foreach(var cb in late.Constants)PatchConstant(cb);
            foreach(var entity in late.Entities)PatchEntity(entity);
        }
    }
    Vector3 Rotate(Vector3 value)=>Rotation.Equals(Quaternion.identity)?value:Rotation*value;
    Vector3 Point(Vector3 value)=>Anchor.Equals(CapturedAnchor)&&Rotation.Equals(Quaternion.identity)?value:Anchor+Rotate(value-CapturedAnchor);
    Vector4 Direction(Vector4 value) { var p=Rotate(value);return new Vector4(p.x,p.y,p.z,value.w); }
    Vector4 Main(Vector4 value)=>new(value.x*MainMultiplier.x,value.y*MainMultiplier.y,value.z*MainMultiplier.z,value.w);
    Vector4 Ambient(Vector4 value)=>new(value.x*AmbientMultiplier,value.y*AmbientMultiplier,value.z*AmbientMultiplier,value.w);
    void PatchEntity(RemielleNativeUIEntityBuffer entity)
    {
        if(entity.Bytes.Length!=8192)throw new InvalidOperationException("Unverified native UI entity extent");
        entity.Reset();var b=entity.Bytes;
        Put(b,0,Main(Read(b,0)));
        foreach(int offset in new[]{16,80})
        {
            var old=Read(b,offset);var p=Point(old);Put(b,offset,new Vector4(p.x,p.y,p.z,old.w));
        }
        Put(b,96,Ambient(Read(b,96)));Put(b,112,Ambient(Read(b,112)));
        entity.Upload();
    }
    void PatchConstant(RemielleNativeUIRenderer.ConstantState cb)
    {
        bool changed=false;
        foreach(var f in cb.source.fields)
        {
            if(f.offsetBytes+16>cb.bytes.Length)continue;
            Vector4 value;
            switch(f.name)
            {
                case "_AmbientGradientShape":
                    var old=Read(cb.source.template,f.offsetBytes);var n=Rotate(old);
                    // Shader evaluates dot(n,p)-w. Preserve that value under
                    // p'=anchor+R*(p-capturedAnchor), using double for the offset.
                    double offset=old.w+Dot(n,Anchor)-Dot(old,CapturedAnchor);
                    value=new Vector4(n.x,n.y,n.z,(float)offset);break;
                case "_OverrideMainLightBody":case "_OverrideMainLightHair":
                    value=Direction(Read(cb.source.template,f.offsetBytes));break;
                case "_MainLightPosition":
                    value=Read(cb.source.template,f.offsetBytes);
                    if(value.w!=0)throw new NotSupportedException("Native UI main light is expected to be directional");
                    value=Direction(value);break;
                case "_MainLightColor":case "_AvatarMainLightColor":
                    value=Main(Read(cb.source.template,f.offsetBytes));break;
                case "_CharacterAmbient":
                    value=Ambient(Read(cb.source.template,f.offsetBytes));break;
                default:continue;
            }
            Put(cb.bytes,f.offsetBytes,value);changed=true;
        }
        if(!changed)return;
        var pin=GCHandle.Alloc(cb.upload,GCHandleType.Pinned);
        try { Marshal.Copy(cb.bytes,0,pin.AddrOfPinnedObject(),cb.bytes.Length); } finally { pin.Free(); }
        cb.buffer.SetData(cb.upload);
    }
    // Original UI Deferred cb0[6]/[7] are main-light direction/color. Called
    // after copying its captured template, so intensity never accumulates.
    public void PatchDeferred(Vector4[] constants,RemielleNativeUIConstants.Frame currentFrame)
    {
        if(frame==null||frame!=currentFrame||constants.Length!=185)
            throw new InvalidOperationException("Native Deferred light state does not match geometry");
        if(constants[6].w!=0)throw new NotSupportedException("Unverified Deferred positional main light");
        constants[6]=Direction(constants[6]);constants[7]=Main(constants[7]);
    }
    static double Dot(Vector3 a,Vector3 b)=>(double)a.x*b.x+(double)a.y*b.y+(double)a.z*b.z;
    static bool Finite(Vector3 v)=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z);
    static Vector4 Read(byte[] b,int offset)=>new(BitConverter.ToSingle(b,offset),BitConverter.ToSingle(b,offset+4),BitConverter.ToSingle(b,offset+8),BitConverter.ToSingle(b,offset+12));
    static void Put(byte[] b,int offset,Vector4 v)
    {
        for(int i=0;i<4;i++)
        {
            if(!float.IsFinite(v[i]))throw new InvalidOperationException("Non-finite native light field");
            BitConverter.TryWriteBytes(b.AsSpan(offset+i*4,4),v[i]);
        }
    }
}
