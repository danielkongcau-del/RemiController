using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using Remielle.ControllerRuntime;
using UnityEngine;

public static class NativePoseResourcesAudit
{
    const string Out="E:/ZZZ/local-only/RemielleControllerImplementation/20260906/";
    static void Require(bool ok,string why){if(!ok)throw new Exception(why);}
    static float Float(JToken t)=>BitConverter.Int32BitsToSingle(unchecked((int)Convert.ToUInt32((string)t,16)));
    static string Bits(float v)=>unchecked((uint)BitConverter.SingleToInt32Bits(v)).ToString("x8");
    static byte[] Bytes(JToken t){string s=(string)t;return Enumerable.Range(0,s.Length/2).Select(i=>Convert.ToByte(s.Substring(i*2,2),16)).ToArray();}
    static string Hash(string path){using(var s=File.OpenRead(path))using(var h=SHA256.Create())return NativeMotionArchive.Hex(h.ComputeHash(s));}
    static void Equal(JToken a,JToken b,string where)
    {Require(JToken.DeepEquals(a,b),where+" differs; actual "+a.ToString(Newtonsoft.Json.Formatting.None)+" expected "+b.ToString(Newtonsoft.Json.Formatting.None));}
    static NativePoseData Pose(JToken rows)=>new NativePoseData(rows[0].Select(Float).ToArray(),rows[1].Select(Float).ToArray(),rows[2].Select(Float).ToArray(),
        rows[3].Select(Float).ToArray(),rows[4].Select(x=>Convert.ToUInt32((string)x,16)).ToArray());
    static JArray PoseRows(NativePoseStream stream)
    {
        var pose=stream.Pose();var rows=new JArray();for(int g=0;g<4;g++)rows.Add(new JArray(pose.FloatChannel(g).Select(Bits)));
        rows.Add(new JArray(pose.DiscreteChannel().Select(v=>v.ToString("x8"))));return rows;
    }
    static JArray Masks(NativePoseStream stream)=>new JArray(stream.Mask().Select(r=>new JArray(r.Select(v=>(int)v))));
    static JToken NullableHex(byte[] data)=>data==null?JValue.CreateNull():new JValue(NativeMotionArchive.Hex(data));
    static JObject Snapshot(NativePoseResources resource,bool ready=true,bool dirty=false)
    {
        var stream=resource.Pose;
        return new JObject{["counts"]=new JArray(stream.Pose().ChannelCounts().Concat(new[]{resource.ExtraByteCount})),["pose"]=PoseRows(stream),
            ["mask"]=Masks(stream),["byteValues"]=new JArray(resource.ByteValues.Select(v=>(int)v)),["root"]=NativeMotionArchive.Hex(resource.RootStorage),
            ["additional"]=new JArray(NullableHex(resource.AdditionalStorage),NullableHex(resource.SecondaryStorage)),
            ["streamFlag"]=stream.StreamFlag,["rootFlag"]=resource.RootFlag,["ready"]=ready,["dirty"]=dirty};
    }
    public static JObject Run(string path)
    {
        var data=JObject.Parse(File.ReadAllText(path));var e=data["evidence"];
        foreach(string k in new[]{"source","sourcePack","generator","oracle","bindingVectors"})Require(Hash((string)e[k]["path"])==(string)e[k]["sha256"],"Resource source changed: "+k);
        foreach(var r in e["dependencies"])Require(Hash((string)r["path"])==(string)r["sha256"],"Resource dependency changed");
        var bindingSource=JObject.Parse(File.ReadAllText((string)e["bindingVectors"]["path"]))["source"];
        int cases=0,variants=0,reuses=0,consumers=0,initializers=0;
        for(int repeat=0;repeat<2;repeat++)
        {
            foreach(var row in data["cases"])
            {
                var result=NativePoseResources.FromDescriptorTypes(row["types"].Select(x=>(int)x).ToArray(),(bool)row["context82"],(bool)row["context88"]);
                Equal(Snapshot(result),row["expected"],"Descriptor resource initialization");cases++;
            }
            foreach(var group in data["layouts"])
            {
                Equal(group["bindings"],bindingSource["layouts"][(string)group["layout"]]["bindings"],"Resource source layout identity");
                var plan=new NativeDynamicSourceBindingPlan(group["bindings"].Cast<JObject>());
                foreach(var row in group["variants"])
                {
                    var node=new NativeDefaultPoseInput(default(NativeMixerInputWeight));
                    Require(!node.ResourceReady&&node.PreparedResources==null,"New default node unexpectedly prepared");
                    node.MarkResourcesDirty();Require(node.BindingsDirty,"Local resource dirty request missing");
                    Require(node.PrepareResources(plan.Layout,(bool)row["context82"],(bool)row["context88"]),"Initial preparation did not create resources");
                    var original=node.PreparedResources;
                    Require(ReferenceEquals(original.Pose.BindingLayout,plan.Layout),"Resource binding layout ownership lost");
                    Equal(Snapshot(original,node.ResourceReady,node.BindingsDirty),row["expected"],"Source resource preparation");
                    node.MarkResourcesDirty();
                    Require(!node.PrepareResources(null,!(bool)row["context82"],!(bool)row["context88"]),"Ready resources were reallocated");
                    Require(ReferenceEquals(original,node.PreparedResources),"Ready cache identity changed");
                    Equal(Snapshot(node.PreparedResources,node.ResourceReady,node.BindingsDirty),row["repeatExpected"],"Ready preparation payload");reuses++;variants++;
                    var consumer=row["consumer"];
                    if(consumer!=null)
                    {
                        var output=new NativePoseStream(Pose(consumer["initial"]),consumer["mask"].Select(r=>r.Select(x=>(byte)x).ToArray()).ToArray(),7,3,5,plan.Layout);
                        int[] selected=consumer["selected"].Select(x=>(int)x).ToArray();
                        node.Gather(original.Pose,null,output,(bool)consumer["prepareTransforms"],selected[0],selected[1],selected[2]);
                        Equal(new JObject{["pose"]=PoseRows(output),["mask"]=Masks(output),["streamFlag"]=output.StreamFlag,
                            ["flags"]=new JArray(output.ReferenceFlag0,output.ReferenceFlag1)},consumer["expected"],"Prepared resource to ordinary consumer");consumers++;
                    }
                }
            }
            foreach(var row in data["initializers"])
            {
                byte[] raw=Bytes(row["initial"]);
                if((string)row["kind"]=="root")NativePoseResources.InitializeRoot(raw);else NativePoseResources.InitializeAdditional(raw);
                Require(NativeMotionArchive.Hex(raw)==(string)row["expected"],"Native structure initialization/padding differs: "+(string)row["kind"]);initializers++;
            }
        }
        return new JObject{["syntheticCases"]=cases/2,["sourceLayouts"]=data["layouts"].Count(),["sourceVariants"]=variants/2,
            ["readyResourcesReused"]=reuses/2,["consumerQueries"]=consumers/2,["initializerCases"]=initializers/2,["repeats"]=2,
            ["allStorageDefaultsBitwiseEqual"]=true,["rootAndAdditionalPaddingPreserved"]=true,["defaultNodePreparationConnected"]=true,
            ["readyDoesNotReallocate"]=true,["sourceLayoutIdentityPreserved"]=true,["engineAllocatorOwnershipQualified"]=false,
            ["originalMergedDescriptorProducerQualified"]=false,["additionalPoseEvaluationQualified"]=false,["wholeFrameEvaluationQualified"]=false,["controllerPlayable"]=false,
            ["vectorsSha256"]=Hash(path),["runtimeSha256"]=Hash(Application.dataPath+"/ControllerRuntime/NativePoseResources.cs"),
            ["defaultNodeSha256"]=Hash(Application.dataPath+"/ControllerRuntime/NativeDefaultPoseInput.cs"),
            ["sourcePlanSha256"]=Hash(Application.dataPath+"/ControllerRuntime/NativeSourceBindingRules.cs"),
            ["auditSha256"]=Hash(Application.dataPath+"/ControllerRuntime/Editor/NativePoseResourcesAudit.cs")};
    }
    public static void RunStandalone()
    {
        var result=new JObject{["pass"]=false};
        try{result["poseResources"]=Run(Out+"native-pose-resources-vectors.json");result["pass"]=true;Debug.Log("NATIVE_POSE_RESOURCES_UNITY_OK");}
        catch(Exception e){result["error"]=e.ToString();throw;}
        finally{File.WriteAllText(Out+"unity-pose-resources-verification.json",result.ToString());}
    }
}
