using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using Remielle.ControllerRuntime;
using UnityEditor;
using UnityEngine;

public static class NativeClipPoseChannelsAudit
{
    const string Out="E:/ZZZ/ZCode/90_Builds/ControllerImplementation/20260906/";
    static void Require(bool value,string why){if(!value)throw new Exception(why);}
    static float Float(JToken t)=>BitConverter.Int32BitsToSingle(unchecked((int)Convert.ToUInt32((string)t,16)));
    static string Bits(float v)=>unchecked((uint)BitConverter.SingleToInt32Bits(v)).ToString("x8");
    static string Hash(string path){using(var s=File.OpenRead(path))using(var h=SHA256.Create())return NativeMotionArchive.Hex(h.ComputeHash(s));}
    static string Digest(byte[] bytes){using(var h=SHA256.Create())return NativeMotionArchive.Hex(h.ComputeHash(bytes));}
    static NativePoseData Pose(JToken r)=>new NativePoseData(r[0].Select(Float).ToArray(),r[1].Select(Float).ToArray(),r[2].Select(Float).ToArray(),r[3].Select(Float).ToArray(),r[4].Select(v=>Convert.ToUInt32((string)v,16)).ToArray());
    static byte[][] Mask(JToken r)=>r==null || r.Type==JTokenType.Null ? null : r.Select(a=>a.Select(v=>(byte)v).ToArray()).ToArray();
    static NativePoseStream Stream(JToken data,JToken mask,NativePoseBindingLayout layout=null)=>data==null || data.Type==JTokenType.Null ? null : new NativePoseStream(Pose(data),Mask(mask),3,5,7,layout);
    static NativeClipPoseContext Context(JToken r,NativePoseStream d,NativePoseStream ov,NativePoseStream ev,byte[][] mask)
    {
        var f=r["flags"];var selected=r["selected"];
        return new NativeClipPoseContext(d,ov,ev,(bool)f["evaluation18"],(bool)f["clip194"],(bool)f["prepareTransforms"],(bool)f["prepareScalars"],
            (int)selected[0],(int)selected[1],(int)selected[2],mask);
    }
    static void Equal(NativePoseStream output,JToken expected,string where)
    {
        var pose=output.Pose();using(var s=new MemoryStream())
        {
            for(int g=0;g<4;g++){var a=pose.FloatChannel(g);var bytes=new byte[a.Length*4];Buffer.BlockCopy(a,0,bytes,0,bytes.Length);s.Write(bytes,0,bytes.Length);}
            Require(pose.DiscreteChannel().Length==0,"Unexpected discrete input");
            Require(Digest(s.ToArray())==(string)expected["poseSha256"],where+" pose differs");
        }
        Require(Digest(output.Mask().SelectMany(r=>r).ToArray())==(string)expected["maskSha256"],where+" mask differs");
        Require(output.StreamFlag==3 && output.ReferenceFlag0==5 && output.ReferenceFlag1==7,where+" stream flags changed");
    }
    static void Special(NativeSampledPoseSource sampler,JToken expected,string where)
    {
        var actual=new JArray(sampler.SpecialSamples().Select(v=>v.HasValue?(JToken)Bits(v.Value):JValue.CreateNull()));
        Require(JToken.DeepEquals(actual,expected),where+" special samples changed");
    }
    public static JObject Run(string path)
    {
        var data=JObject.Parse(File.ReadAllText(path));var e=data["evidence"];var source=data["source"];
        foreach(string k in new[]{"source","sourcePack","bank","generator","oracle","bindingVectors","transformVectors","transformBytes"})
            Require(Hash((string)e[k]["path"])==(string)e[k]["sha256"],"Clip channel input changed: "+k);
        foreach(var r in e["dependencies"])Require(Hash((string)r["path"])==(string)r["sha256"],"Clip channel dependency changed");
        var plan=new NativeDynamicSourceBindingPlan(source["bindings"].Cast<JObject>());
        string bankDirectory=Path.GetDirectoryName((string)e["bank"]["path"]);var bank=new NativeMotionBank(bankDirectory);
        Require(bank.AssetIDs.OrderBy(x=>x).SequenceEqual(source["motions"].Select(r=>(string)r["assetID"]).OrderBy(x=>x)),"Clip channel source coverage changed");
        int fixtures=0,fixtureStages=0,motions=0,sourceStages=0,specialChecks=0;
        for(int repeat=0;repeat<2;repeat++)
        {
            foreach(var r in data["fixtures"])
            {
                var initialMask=r["initialMask"];var d=Stream(r["defaults"],initialMask);var ov=Stream(r["overrideDefaults"],initialMask);var ev=Stream(r["evaluationDefaults"],initialMask);
                var output=Stream(r["initial"],initialMask);var context=Context(r,d,ov,ev,Mask(r["writeMask"]));
                var samples=r["sourceBits"].Select(Float).ToArray();var offsets=r["offsets"].Select(a=>a.Select(v=>(int)v).ToArray()).ToArray();
                foreach(var step in r["steps"])
                {
                    switch((string)step["stage"])
                    {
                        case "prepare":NativeClipPoseChannels.PrepareSamples(samples,offsets,context,output);break;
                        case "gather":NativeClipPoseChannels.GatherSamples(samples,offsets,context,output);break;
                        case "empty":NativeClipPoseChannels.WriteEmpty(context,output);break;
                        default:throw new Exception("Unknown Clip stage");
                    }
                    Equal(output,step["expected"],"Fixture "+fixtures+" "+(string)step["stage"]);fixtureStages++;
                }
                fixtures++;
            }
            var mask=source["initialMask"];var defaults=Stream(source["defaults"],mask,plan.Layout);
            var over=Stream(source["overrideDefaults"],mask,plan.Layout);var evaluation=Stream(source["evaluationDefaults"],mask,plan.Layout);
            var current=Stream(source["initial"],mask,plan.Layout);var counts=plan.Layout.Counts();
            foreach(var r in source["motions"])
            {
                string id=(string)r["assetID"];Require((string)bank.Record(id)["sha256"]==(string)r["archiveSha256"],"Clip source archive changed");
                int seed=(int)r["writeMaskSeed"];
                var writeMask=counts.Select((n,g)=>Enumerable.Range(0,n).Select(i=>(byte)((i+g+seed)%3!=0?1:0)).ToArray()).ToArray();
                var context=Context(r,defaults,over,evaluation,writeMask);
                using(var sampler=new NativeSampledPoseSource(bankDirectory,id,plan.Layout))
                {
                    foreach(var step in r["steps"])
                    {
                        switch((string)step["stage"])
                        {
                            case "prepare":sampler.SampleForPreparation(Float(r["timeBits"]),context,current);break;
                            case "gather":sampler.GatherCurrent(context,current);break;
                            case "empty":NativeClipPoseChannels.WriteEmpty(context,current);break;
                            default:throw new Exception("Unknown source Clip stage");
                        }
                        Equal(current,step["expected"],id+" "+(string)step["stage"]);sourceStages++;
                        Special(sampler,r["specialBits"],id);specialChecks++;
                    }
                }
                motions++;
            }
        }
        Require(fixtures==2*(int)e["fixtureCases"] && fixtureStages==2*(int)e["fixtureStages"] && motions==2*(int)e["sourceMotions"] && sourceStages==2*(int)e["sourceStages"],"Clip channel coverage differs");
        return new JObject{
            ["fixtureCases"]=fixtures/2,["fixtureStages"]=fixtureStages/2,["sourceMotions"]=motions/2,["sourceStages"]=sourceStages/2,["specialPreservationChecks"]=specialChecks/2,["repeats"]=2,
            ["originalStageOutputBitwiseEqual"]=true,["defaultSelectionConsumersVerified"]=true,["originalEmptyOrdinaryBranchVerified"]=true,["sameDecodedSamplesReusedByBothStages"]=true,
            ["contextAndSelectedChannelProducersQualified"]=false,["rootLoopAdditiveCorrectionsQualified"]=false,["completeClipCallbackQualified"]=false,["controllerPlayable"]=false,
            ["vectorsSha256"]=Hash(path),["runtimeSha256"]=Hash("Assets/ControllerRuntime/NativeClipPoseChannels.cs"),["gatherRuntimeSha256"]=Hash("Assets/ControllerRuntime/NativeSamplePoseGather.cs"),["auditSha256"]=Hash("Assets/ControllerRuntime/Editor/NativeClipPoseChannelsAudit.cs")};
    }
    public static void RunBatch()
    {
        var report=new JObject{["pass"]=false};
        try{report["clipPoseChannels"]=Run(Out+"native-clip-pose-channels-vectors.json");report["pass"]=true;Debug.Log("NATIVE_CLIP_POSE_CHANNELS_OK "+report.ToString(Newtonsoft.Json.Formatting.None));}
        catch(Exception ex){report["error"]=ex.ToString();Debug.LogException(ex);}
        finally{File.WriteAllText(Out+"unity-clip-pose-channels-verification.json",report.ToString());}
        if(!(bool)report["pass"])EditorApplication.Exit(1);
    }
}
