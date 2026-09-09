using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using Remielle.ControllerRuntime;
using UnityEditor;
using UnityEngine;

public static class NativeSourceBindingAudit
{
    const string Out="E:/ZZZ/ZCode/90_Builds/ControllerImplementation/20260906/";
    static void Require(bool ok,string why){if(!ok)throw new Exception(why);}
    static float Float(JToken t)=>BitConverter.Int32BitsToSingle(unchecked((int)Convert.ToUInt32((string)t,16)));
    static string Bits(float f)=>unchecked((uint)BitConverter.SingleToInt32Bits(f)).ToString("x8");
    static string Hash(string path){using(var f=File.OpenRead(path))using(var h=SHA256.Create())return NativeMotionArchive.Hex(h.ComputeHash(f));}
    static string Digest(byte[] data){using(var h=SHA256.Create())return NativeMotionArchive.Hex(h.ComputeHash(data));}
    static NativeCurveBinding Binding(JToken b)=>new NativeCurveBinding((uint)b["path"],(uint)b["attribute"],(int)b["classID"],(byte?)b["customType"]??0,(byte?)b["isPPtr"]??0,(byte?)b["isInt"]??0);
    static JObject Rules(NativeCurveBinding a,NativeCurveBinding b,int ra,int rb)=>new JObject{
        ["equal"]=NativeSourceBindingRules.Equivalent(a,b),["less"]=NativeSourceBindingRules.Less(a,b,ra,rb),
        ["special"]=NativeSourceBindingRules.Special(a),["canonicalAttribute"]=NativeSourceBindingRules.CanonicalAttribute(a)};
    static void Equal(JToken a,JToken b,string where){Require(JToken.DeepEquals(a,b),where+" differs: "+a.ToString(Newtonsoft.Json.Formatting.None));}
    static NativePoseData Pose(JToken r)=>new NativePoseData(r[0].Select(Float).ToArray(),r[1].Select(Float).ToArray(),r[2].Select(Float).ToArray(),r[3].Select(Float).ToArray(),r[4].Select(v=>Convert.ToUInt32((string)v,16)).ToArray());
    static byte[][] Mask(JToken r)=>r.Select(a=>a.Select(v=>(byte)v).ToArray()).ToArray();
    static string PoseHash(NativePoseData pose)
    {
        using(var s=new MemoryStream())
        {
            for(int g=0;g<4;g++){var a=pose.FloatChannel(g);var b=new byte[a.Length*4];Buffer.BlockCopy(a,0,b,0,b.Length);s.Write(b,0,b.Length);}
            Require(pose.DiscreteChannel().Length==0,"Unexpected discrete source pose");return Digest(s.ToArray());
        }
    }
    static string MapHash(int[][] offsets)
    {using(var s=new MemoryStream())using(var w=new BinaryWriter(s)){foreach(var r in offsets)foreach(int x in r)w.Write(checked((short)x));return Digest(s.ToArray());}}
    static void Rejected(Action action,string why)
    {try{action();}catch(NotSupportedException){return;}throw new Exception("Unsupported source binding accepted: "+why);}
    public static JObject Run(string path)
    {
        var data=JObject.Parse(File.ReadAllText(path));var ev=data["evidence"];var source=data["source"];
        foreach(string k in new[]{"source","sourcePack","bank","generator","oracle","priorVectors","transformVectors","transformBytes"})
            Require(Hash((string)ev[k]["path"])==(string)ev[k]["sha256"],"Source binding input changed: "+k);
        foreach(var r in ev["dependencies"])Require(Hash((string)r["path"])==(string)r["sha256"],"Binding dependency changed");
        foreach(var r in ev["sourceRankInputs"])
        {Require(Hash((string)r["source"]["path"])==(string)r["source"]["sha256"],"Native source cache rank input changed");Require((int)r["constantCount"]==0,"Source requires constant-hoisting policy");}
        int cases=0,sorts=0,layouts=0,motions=0,specialValues=0,missingSpecial=0,rejections=0;
        var all=source["bindings"].Cast<JObject>().ToArray();
        var plan=new NativeDynamicSourceBindingPlan(all);
        Equal(new JArray(plan.OrderedBindings()),new JArray(source["originalOrder"].Select(i=>(JToken)all[(int)i])),"Complete native source sort");
        Equal(new JArray(plan.OrdinaryBindings()),source["ordinaryBindings"],"Ordinary source partition");
        Equal(new JArray(plan.SpecialBindings()),source["specialBindings"],"Special source partition");
        Equal(new JArray(plan.Layout.Keys().Select(r=>new JArray(r))),source["keys"],"Native ordinary channel order");
        Equal(new JArray(plan.Layout.Counts()),source["counts"],"Native ordinary dimensions");
        foreach(var property in ((JObject)source["layouts"]).Properties())
        {
            var bindings=property.Value["bindings"].Cast<JObject>().ToArray();var actual=new NativeDynamicSourceBindingPlan(bindings);
            Equal(new JArray(actual.OrderedBindings()),new JArray(property.Value["originalOrder"].Select(i=>(JToken)bindings[(int)i])),"Native source layout "+property.Name);layouts++;
        }
        var bad=(JObject)all.First(b=>(string)b["typeID"]=="Transform").DeepClone();bad["attribute"]=4;
        Rejected(()=>new NativeDynamicSourceBindingPlan(new[]{bad}),"Euler gather");rejections++;
        bad=(JObject)all[0].DeepClone();bad["script"]["m_PathID"]=1;
        Rejected(()=>new NativeDynamicSourceBindingPlan(new[]{bad}),"external script identity");rejections++;
        bad=(JObject)all[0].DeepClone();bad["isIntCurve"]=1;
        Rejected(()=>new NativeDynamicSourceBindingPlan(new[]{bad}),"integer source path");rejections++;
        string bankDirectory=Path.GetDirectoryName((string)ev["bank"]["path"]);var bank=new NativeMotionBank(bankDirectory);
        Require(bank.AssetIDs.OrderBy(x=>x).SequenceEqual(source["motions"].Select(r=>(string)r["assetID"]).OrderBy(x=>x)),"Native binding source coverage differs");
        for(int repeat=0;repeat<2;repeat++)
        {
            foreach(var row in data["cases"])
            {Equal(Rules(Binding(row["a"]),Binding(row["b"]),(int)row["rankA"],(int)row["rankB"]),row["expected"],"Native binding rule "+cases);cases++;}
            foreach(var row in data["sortCases"])
            {
                var bindings=row["bindings"].Select(Binding).ToArray();var ranks=row["ranks"].Select(x=>(int)x).ToArray();var order=Enumerable.Range(0,bindings.Length).ToArray();
                Array.Sort(order,(i,j)=>NativeSourceBindingRules.Less(bindings[i],bindings[j],ranks[i],ranks[j])?-1:NativeSourceBindingRules.Less(bindings[j],bindings[i],ranks[j],ranks[i])?1:0);
                Equal(new JArray(order),row["expected"],"Native whole sort "+sorts);sorts++;
            }
            var defaults=new NativePoseStream(Pose(source["defaults"]),Mask(source["initialMask"]),bindingLayout:plan.Layout);
            var output=new NativePoseStream(Pose(source["initial"]),Mask(source["initialMask"]),3,5,7,plan.Layout);
            foreach(var row in source["motions"])
            {
                string id=(string)row["assetID"];
                Require((string)bank.Record(id)["sha256"]==(string)row["archiveSha256"],"Source motion changed");
                using(var sampler=new NativeSampledPoseSource(bankDirectory,id,plan.Layout))
                {
                    Require(MapHash(sampler.Map.Offsets())==(string)row["sampleMapSha256"],"Native source gather map differs: "+id);
                    Equal(new JArray(sampler.Map.SpecialOffsets()),row["specialOffsets"],"Special source offsets");
                    sampler.Sample(Float(row["timeBits"]),defaults,output,(bool)row["markDefaultsActive"]);
                    Require(PoseHash(output.Pose())==(string)row["expected"]["poseSha256"],"Native ordinary pose bytes differ: "+id);
                    Require(Digest(output.Mask().SelectMany(r=>r).ToArray())==(string)row["expected"]["maskSha256"],"Native ordinary mask bytes differ: "+id);
                    Require(output.StreamFlag==3&&output.ReferenceFlag0==5&&output.ReferenceFlag1==7,"Gather changed stream flags");
                    var special=sampler.SpecialSamples();Equal(new JArray(special.Select(v=>v.HasValue?(JToken)Bits(v.Value):JValue.CreateNull())),row["specialBits"],"Separate original special values");
                    specialValues+=special.Count(v=>v.HasValue);missingSpecial+=special.Count(v=>!v.HasValue);
                }
                motions++;
            }
        }
        Require(cases==2*(int)ev["cases"]&&sorts==2*(int)ev["sortCases"]&&layouts==(int)ev["sourceLayouts"]&&motions==2*(int)ev["sourceMotions"]&&specialValues==2*(int)ev["specialSampleValues"],"Native binding rule coverage differs");
        return new JObject{
            ["cases"]=cases/2,["sortCases"]=sorts/2,["sourceBindings"]=all.Length,["sourceLayouts"]=layouts,["sourceMotions"]=motions/2,["sourceQueries"]=motions/2,
            ["ordinaryCounts"]=new JArray(plan.Layout.Counts()),["specialBindings"]=plan.SpecialBindings().Length,["specialSampleValues"]=specialValues/2,["absentSpecialSampleValues"]=missingSpecial/2,["unsupportedInputsRejected"]=rejections,["repeats"]=2,
            ["originalBindingRulesBitwiseEqual"]=true,["originalDynamicBindingOrderVerified"]=true,["specialChannelsSeparatedAndPreserved"]=true,["actualSourceSamplingToNativeOrderedPoseComposed"]=true,
            ["completeNativeBindingPolicyQualified"]=false,["constantHoistingQualified"]=false,["avatarFilteringQualified"]=false,["defaultAndMaskProducersQualified"]=false,["specialRootMotionApplicationQualified"]=false,["controllerPlayable"]=false,
            ["vectorsSha256"]=Hash(path),["runtimeSha256"]=Hash("Assets/ControllerRuntime/NativeSourceBindingRules.cs"),["gatherRuntimeSha256"]=Hash("Assets/ControllerRuntime/NativeSamplePoseGather.cs"),["auditSha256"]=Hash("Assets/ControllerRuntime/Editor/NativeSourceBindingAudit.cs")
        };
    }
    public static void RunBatch()
    {
        var report=new JObject{["pass"]=false};
        try{report["sourceBindingRules"]=Run(Out+"native-binding-rules-vectors.json");report["pass"]=true;Debug.Log("NATIVE_SOURCE_BINDING_AUDIT_OK "+report.ToString(Newtonsoft.Json.Formatting.None));}
        catch(Exception ex){report["error"]=ex.ToString();Debug.LogException(ex);}
        finally{File.WriteAllText(Out+"unity-source-binding-verification.json",report.ToString());}
        if(!(bool)report["pass"])EditorApplication.Exit(1);
    }
}
