using System;
using System.Linq;
using System.Runtime.InteropServices;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static class RemielleUILightRigAudit
{
    public static void RunAll()
    {
        RemielleUILightRigRasterAudit.Run();
        RemielleUILightRigDeferredAudit.Run();
        RemielleUILightRigLateAudit.Run();
    }
    public static void MoveModel(RemielleNativeAnimation driver,int index)
    {
        driver.transform.position=index==0?Vector3.zero:new Vector3(2,0,-1);
    }
    public static JObject Apply(RemielleNativeUILightRig rig,RemielleNativeUIRenderer geometry,
        RemielleNativeUILateBody late,RemielleNativeAnimation driver,int index)
    {
        var pelvis=driver.bones.Single(b=>b.source.name=="Bip001 Pelvis").source;
        var anchor=index==0?rig.CapturedAnchor:geometry.CurrentFrame.sceneToProfile.MultiplyPoint3x4(pelvis.position);
        var rotation=Quaternion.Euler(0,index==2?90:index==3?180:0,0);
        var main=index==4?new Vector3(.55f,.8f,1.1f):Vector3.one;
        float ambient=index==4?.45f:1;
        var before=geometry.Draws.SelectMany(d=>d.constants).Select(c=>(byte[])c.bytes.Clone()).ToArray();
        rig.Apply(geometry,late,anchor,rotation,main,ambient);
        if(index==0&&!geometry.Draws.SelectMany(d=>d.constants).Select((c,i)=>c.bytes.SequenceEqual(before[i])).All(v=>v))
            throw new Exception("Identity rig changed captured lighting constants");
        var entities=geometry.Draws.SelectMany(d=>d.entities).Concat(late?.Entities??Enumerable.Empty<RemielleNativeUIEntityBuffer>());
        int checkedBuffers=0;
        foreach(var entity in entities)
        {
            if(!entity.Bytes.Skip(128).SequenceEqual(entity.Source.structured.Skip(128)))throw new Exception("Rig modified unrelated entities");
            if(index==0&&!entity.Bytes.SequenceEqual(entity.Source.structured))throw new Exception("Identity rig changed captured entities");
            var gpu=new Entity[entity.Bytes.Length/128];entity.Buffer.GetData(gpu);
            var bytes=new byte[entity.Bytes.Length];var pin=GCHandle.Alloc(gpu,GCHandleType.Pinned);
            try{Marshal.Copy(pin.AddrOfPinnedObject(),bytes,0,bytes.Length);}finally{pin.Free();}
            if(!bytes.SequenceEqual(entity.Bytes))throw new Exception("GPU light buffer differs from exported current data");
            checkedBuffers++;
        }
        return new JObject{["preset"]=index,["anchor"]=V(anchor),["capturedAnchor"]=V(rig.CapturedAnchor),
            ["rotation"]=new JArray(rotation.x,rotation.y,rotation.z,rotation.w),["mainMultiplier"]=V(main),
            ["ambientMultiplier"]=ambient,["identityBytePreserved"]=index==0,["unrelatedEntitiesBytePreserved"]=true,
            ["gpuStructuredBuffersChecked"]=checkedBuffers,["policy"]="captured lighting under explicit movable presentation rig; original CPU producer not recovered"};
    }
    static JArray V(Vector3 p)=>new(p.x,p.y,p.z);
    [StructLayout(LayoutKind.Sequential)] struct Entity{public Vector4 a,b,c,d,e,f,g,h;}
}
