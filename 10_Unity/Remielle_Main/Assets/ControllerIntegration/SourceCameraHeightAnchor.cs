using System;
using System.Linq;
using UnityEngine;

namespace Remielle.Controller
{
    // Preview framing policy: follow the authored body's height above the
    // motor. Flight poses can rise while the collision capsule stays grounded.
    // This is independent of state selection and does not modify any bone.
    public sealed class SourceCameraHeightAnchor : IDisposable
    {
        readonly Transform focus,actor;
        readonly RemielleNativeAnimation.BoneLink pelvis;
        readonly Vector3 home;
        readonly float restHeight;
        bool disposed;
        public SourceCameraHeightAnchor(Transform focus,Transform actor,RemielleNativeAnimation driver)
        {
            this.focus=focus;this.actor=actor;
            if(!focus||focus.parent!=actor)throw new ArgumentException("Camera height anchor must be a direct motor child");
            pelvis=driver.bones.Single(b=>b.source.name=="Bip001");home=focus.localPosition;restHeight=Height();
        }
        float Height()=>actor.InverseTransformPoint(pelvis.target.TransformPoint(pelvis.basis.inverse.MultiplyPoint3x4(Vector3.zero))).y;
        public void Advance(float delta)
        {
            if(disposed)throw new ObjectDisposedException(nameof(SourceCameraHeightAnchor));
            if(!float.IsFinite(delta)||delta<0)throw new ArgumentOutOfRangeException(nameof(delta));
            if(delta==0)return;
            var p=home;p.y= Mathf.Lerp(focus.localPosition.y,home.y+Height()-restHeight,1-Mathf.Exp(-delta/.08f));
            focus.localPosition=p;
        }
        public void Dispose(){if(disposed)return;if(focus)focus.localPosition=home;disposed=true;}
    }
}
