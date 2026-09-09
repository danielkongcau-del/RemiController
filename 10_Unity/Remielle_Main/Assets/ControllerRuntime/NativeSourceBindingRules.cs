using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Remielle.ControllerRuntime
{
    public readonly struct NativeCurveBinding
    {
        public readonly uint Path, Attribute;
        public readonly int ClassID;
        public readonly byte CustomType, IsPPtr, IsInt;
        public NativeCurveBinding(uint path,uint attribute,int classID,byte customType=0,byte isPPtr=0,byte isInt=0)
        { Path=path;Attribute=attribute;ClassID=classID;CustomType=customType;IsPPtr=isPPtr;IsInt=isInt; }
        public static NativeCurveBinding FromSource(JObject b)
        {
            if ((int)b["script"]["m_FileID"]!=0 || (long)b["script"]["m_PathID"]!=0)
                throw new NotSupportedException("Nonzero script bindings require the original object-identity resolver");
            int type;
            switch ((string)b["typeID"])
            {
                case "Transform":type=4;break;
                case "Animator":type=95;break;
                case "SkinnedMeshRenderer":type=137;break;
                default:throw new NotSupportedException("Source component registration has not been qualified: "+(string)b["typeID"]);
            }
            return new NativeCurveBinding((uint)b["path"],(uint)b["attribute"],type,(byte)b["customType"],(byte)b["isPPtrCurve"],(byte)b["isIntCurve"]);
        }
    }

    // Original c8bb30 equality, c8b9e0 strict ordering, c8c2c0 attribute
    // canonicalization and c80740 special-channel predicate. Script refs are
    // zero in the current complete source set and are not represented here.
    public static class NativeSourceBindingRules
    {
        static bool Transform(NativeCurveBinding b)=>b.ClassID==4;
        static bool Rotation(NativeCurveBinding b)=>Transform(b)&&(b.Attribute==2||b.Attribute==4);
        public static uint CanonicalAttribute(NativeCurveBinding b)=>Transform(b)&&b.Attribute==4 ? 2u : b.Attribute;
        public static bool Special(NativeCurveBinding b)=>b.ClassID==95&&b.CustomType==8;
        public static bool Equivalent(NativeCurveBinding a,NativeCurveBinding b)=>
            a.Path==b.Path&&CanonicalAttribute(a)==CanonicalAttribute(b)&&a.ClassID==b.ClassID&&
            (a.CustomType==b.CustomType||(Rotation(a)&&Rotation(b)))&&a.IsPPtr==b.IsPPtr;
        public static bool Less(NativeCurveBinding a,NativeCurveBinding b,int rankA=3,int rankB=3)
        {
            if(rankA!=rankB)return rankA>rankB;
            bool ta=Transform(a),tb=Transform(b);
            if(ta&&tb)
            {
                if(a.Attribute==b.Attribute||(Rotation(a)&&Rotation(b)))return a.Path<b.Path;
                return CanonicalAttribute(a)<CanonicalAttribute(b);
            }
            if(ta!=tb)return ta;
            if(a.ClassID!=b.ClassID)return unchecked(a.ClassID-b.ClassID)<0;
            if(a.IsPPtr!=b.IsPPtr)return a.IsPPtr<b.IsPPtr;
            if(a.CustomType!=b.CustomType)return a.CustomType<b.CustomType;
            if(a.Path!=b.Path)return a.Path<b.Path;
            return CanonicalAttribute(a)<CanonicalAttribute(b);
        }
    }

    // Current source clips contain only ACL-sampled channels and no native
    // ConstantClip values: their binding-cache rank is 3. This plan preserves
    // original ordering/classification in that scope. Avatar filtering, constant
    // hoisting and root/special-channel application are separate producers.
    public sealed class NativeDynamicSourceBindingPlan
    {
        readonly JObject[] ordered, ordinary, special;
        public NativePoseBindingLayout Layout { get; }
        public JObject[] OrderedBindings()=>ordered.Select(b=>(JObject)b.DeepClone()).ToArray();
        public JObject[] OrdinaryBindings()=>ordinary.Select(b=>(JObject)b.DeepClone()).ToArray();
        public JObject[] SpecialBindings()=>special.Select(b=>(JObject)b.DeepClone()).ToArray();
        public NativeDynamicSourceBindingPlan(IEnumerable<JObject> sourceBindings)
        {
            if(sourceBindings==null)throw new ArgumentNullException(nameof(sourceBindings));
            var unique=new Dictionary<(uint,uint,int,byte),JObject>();
            foreach(var b in sourceBindings)
            {
                var key=NativeCurveBinding.FromSource(b);
                if(key.IsPPtr!=0||key.IsInt!=0)throw new NotSupportedException("Current source plan supports continuous float channels only");
                if(key.ClassID==4&&(key.Attribute<1||key.Attribute>3||key.CustomType!=0))
                    throw new NotSupportedException("Current source plan requires original Q/T/S Transform bindings");
                unique[(key.Path,key.Attribute,key.ClassID,key.CustomType)]=(JObject)b.DeepClone();
            }
            ordered=unique.Values.ToArray();
            Array.Sort(ordered,(a,b)=>{
                var ka=NativeCurveBinding.FromSource(a);var kb=NativeCurveBinding.FromSource(b);
                if(NativeSourceBindingRules.Less(ka,kb))return -1;
                if(NativeSourceBindingRules.Less(kb,ka))return 1;
                if(!NativeSourceBindingRules.Equivalent(ka,kb))throw new InvalidDataException("Distinct bindings compare as an unqualified sort tie");
                return 0;
            });
            ordinary=ordered.Where(b=>!NativeSourceBindingRules.Special(NativeCurveBinding.FromSource(b))).ToArray();
            special=ordered.Where(b=>NativeSourceBindingRules.Special(NativeCurveBinding.FromSource(b))).ToArray();
            Layout=new NativePoseBindingLayout(ordinary,special);
        }
    }
}
