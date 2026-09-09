using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using Remielle.ControllerRuntime;
using UnityEditor;
using UnityEngine;

public static class NativeSamplePoseAudit
{
    const string Out = "E:/ZZZ/local-only/RemielleControllerImplementation/20260906/";
    static void Require(bool ok, string why) { if (!ok) throw new Exception(why); }
    static float Float(JToken x) => BitConverter.Int32BitsToSingle(unchecked((int)Convert.ToUInt32((string)x, 16)));
    static byte[][] Mask(JToken rows) => rows == null || rows.Type == JTokenType.Null ? null : rows.Select(r => r.Select(v => (byte)v).ToArray()).ToArray();
    static byte[][] Mask(int[] counts, int seed) => counts.Select((n,g) => Enumerable.Range(0,n).Select(i => (byte)((i+g+seed)%3 != 0 ? 1 : 0)).ToArray()).ToArray();
    static NativePoseData Pose(JToken r) => new NativePoseData(r[0].Select(Float).ToArray(), r[1].Select(Float).ToArray(),
        r[2].Select(Float).ToArray(), r[3].Select(Float).ToArray(), r[4].Select(v => Convert.ToUInt32((string)v,16)).ToArray());
    static string Hash(string path) { using (var f = File.OpenRead(path)) using (var h = SHA256.Create()) return NativeMotionArchive.Hex(h.ComputeHash(f)); }
    static string HashBytes(byte[] bytes) { using (var h = SHA256.Create()) return NativeMotionArchive.Hex(h.ComputeHash(bytes)); }
    static string PoseHash(NativePoseData p)
    {
        using (var s = new MemoryStream())
        {
            for (int g=0; g<4; g++) { var a=p.FloatChannel(g); var b=new byte[a.Length*4]; Buffer.BlockCopy(a,0,b,0,b.Length); s.Write(b,0,b.Length); }
            var discrete=p.DiscreteChannel(); var tail=new byte[discrete.Length*4]; Buffer.BlockCopy(discrete,0,tail,0,tail.Length); s.Write(tail,0,tail.Length);
            return HashBytes(s.ToArray());
        }
    }
    static string MapHash(int[][] offsets)
    {
        using (var s = new MemoryStream()) using (var w = new BinaryWriter(s))
        { foreach (var r in offsets) foreach (int v in r) w.Write(checked((short)v)); return HashBytes(s.ToArray()); }
    }
    static void Equal(NativePoseStream stream, JToken expected, string where)
    {
        string pose = PoseHash(stream.Pose()), mask = HashBytes(stream.Mask().SelectMany(r=>r).ToArray());
        Require(pose==(string)expected["poseSha256"], where+" pose bytes differ: "+pose);
        Require(mask==(string)expected["maskSha256"], where+" mask bytes differ: "+mask);
        Require(stream.StreamFlag==3 && stream.ReferenceFlag0==5 && stream.ReferenceFlag1==7, where+" changed unrelated stream flags");
    }
    static void Reject(Action action, string where)
    {
        try { action(); } catch (ArgumentException) { return; }
        throw new Exception("Expected binding/input rejection: "+where);
    }
    public static JObject Run(string path)
    {
        var data=JObject.Parse(File.ReadAllText(path)); var evidence=data["evidence"];
        foreach (string key in new[]{"source","sourcePack","bank","generator","oracle","transformVectors","transformBytes"})
            Require(Hash((string)evidence[key]["path"])==(string)evidence[key]["sha256"], "Sample pose input changed: "+key);
        foreach (var r in evidence["dependencies"]) Require(Hash((string)r["path"])==(string)r["sha256"], "Sample pose dependency changed");
        var source=data["source"]; var bindings=source["bindings"].Cast<JObject>().ToArray();
        var layout=new NativePoseBindingLayout(bindings); int[] counts=layout.Counts();
        Require(JToken.DeepEquals(new JArray(layout.Keys().Select(r=>new JArray(r))),source["keys"]), "Transport binding identity differs");
        Require(JToken.DeepEquals(new JArray(counts),source["counts"]), "Transport binding dimensions differ");
        string directory=Path.GetDirectoryName((string)evidence["bank"]["path"]);
        var bank=new NativeMotionBank(directory);
        Require(new HashSet<string>(bank.AssetIDs).SetEquals(source["motions"].Select(r=>(string)r["assetID"])), "Source motion coverage differs");
        int fixtures=0, motions=0, queries=0, rejected=0;
        for (int repeat=0; repeat<2; repeat++)
        {
            foreach (var row in data["fixtures"])
            {
                var output=new NativePoseStream(Pose(row["initial"]),Mask(row["initialMask"]),3,5,7);
                var samples=row["sourceBits"].Select(Float).ToArray(); var offsets=row["offsets"].Select(r=>r.Select(v=>(int)v).ToArray()).ToArray();
                NativeSamplePoseGather.Gather(samples, offsets, Pose(row["defaults"]), output, (bool)row["markDefaultsActive"], Mask(row["writeMask"]));
                Equal(output,row["expected"],"Original gather fixture "+fixtures); fixtures++;
            }
            var defaults=new NativePoseStream(Pose(source["defaults"]),Mask(source["initialMask"]),bindingLayout:layout);
            var state=new NativePoseStream(Pose(source["initial"]),Mask(source["initialMask"]),3,5,7,layout);
            foreach (var row in source["motions"])
            {
                string id=(string)row["assetID"];
                Require((string)bank.Record(id)["sha256"]==(string)row["archiveSha256"], "Source archive identity changed");
                using (var sampler=new NativeSampledPoseSource(directory,id,layout))
                {
                    Require(MapHash(sampler.Map.Offsets())==(string)row["sampleMapSha256"], "Source-to-layout mapping differs: "+id);
                    foreach (var q in row["queries"])
                    {
                        var wm=q["writeMaskSeed"].Type==JTokenType.Null ? null : Mask(counts,(int)q["writeMaskSeed"]);
                        sampler.Sample(Float(q["timeBits"]),defaults,state,(bool)q["markDefaultsActive"],wm);
                        Equal(state,q["expected"],"Source sample "+id+" time="+(string)q["timeBits"]); queries++;
                    }
                    if (motions==0)
                    {
                        // Equal dimensions cannot establish binding identity.
                        var changed=bindings.Select(b=>(JObject)b.DeepClone()).ToArray();
                        changed[0]["path"]=(uint)changed[0]["path"] ^ 0x80000000u;
                        var other=new NativePoseBindingLayout(changed);
                        Require(other.Counts().SequenceEqual(counts),"Identity rejection fixture changed dimensions");
                        var wrong=new NativePoseStream(state.Pose(),state.Mask(),bindingLayout:other);
                        var values=new float[sampler.Map.SourceFloatCount];
                        Reject(()=>sampler.Map.Gather(values,defaults,wrong,false),"same size, different identities"); rejected++;
                        Reject(()=>sampler.Map.Gather(values,wrong,state,false),"wrong defaults identity"); rejected++;
                        Reject(()=>sampler.Map.Gather(new float[values.Length-1],defaults,state,false),"truncated samples"); rejected++;
                        var weights=counts.Take(3).Concat(new[]{counts[4],counts[3]}).Select(n=>new float[n]).ToArray();
                        var mixer=new NativePoseChannelMixer(state,state.Mask(),weights);
                        var empty=NativeMixerEvaluation.Evaluate(Array.Empty<NativeMixerInputWeight>(),Array.Empty<NativeMixerLink>(),Array.Empty<NativeMixerNode>(),0,0,0);
                        Reject(()=>mixer.Evaluate(empty,wrong,null,null,false,false,(n,s)=>{}),"mixer layout identity"); rejected++;
                    }
                }
                motions++;
            }
        }
        Require(fixtures==2*(int)evidence["fixtureCases"] && motions==2*(int)evidence["sourceMotions"] && queries==2*(int)evidence["sourceQueries"] && rejected==4,"Sample pose coverage differs");
        return new JObject {
            ["fixtureCases"]=fixtures/2,["sourceMotions"]=motions/2,["sourceQueries"]=queries/2,["repeats"]=2,["identityAndInputRejections"]=rejected,
            ["counts"]=new JArray(counts),["comparedPoseFloat32Values"]=evidence["comparedPoseFloat32Values"].DeepClone(),
            ["sourceMappedWrites"]=evidence["sourceMappedWrites"].DeepClone(),["missingDefaultWrites"]=evidence["missingDefaultWrites"].DeepClone(),["restrictedRetainedChannels"]=evidence["restrictedRetainedChannels"].DeepClone(),
            ["originalFloatQuaternionGatherBitwiseEqual"]=true,["completeTierSourceSamplingToPoseComposed"]=true,["allSourceBindingsRetained"]=true,
            ["nativeBindingPolicyQualified"]=false,["defaultAndWriteMaskProducersQualified"]=false,["eulerAndDiscretePathsQualified"]=false,
            ["completeClipPoseCallbackQualified"]=false,["controllerPlayable"]=false,
            ["vectorsSha256"]=Hash(path),["runtimeSha256"]=Hash("Assets/ControllerRuntime/NativeSamplePoseGather.cs"),["auditSha256"]=Hash("Assets/ControllerRuntime/Editor/NativeSamplePoseAudit.cs"),
            ["streamRuntimeSha256"]=Hash("Assets/ControllerRuntime/NativePoseChannelMixer.cs"),["archiveRuntimeSha256"]=Hash("Assets/ControllerRuntime/NativeMotionArchive.cs"),
            ["codecRuntimeSha256"]=Hash("Assets/ControllerRuntime/NativeMotionCodec.cs")
        };
    }
    public static void RunBatch()
    {
        var result=new JObject{["pass"]=false};
        try { result["samplePose"]=Run(Out+"native-sample-pose-vectors.json");result["pass"]=true;Debug.Log("NATIVE_SAMPLE_POSE_AUDIT_OK "+result.ToString(Newtonsoft.Json.Formatting.None)); }
        catch(Exception ex) { result["error"]=ex.ToString();Debug.LogException(ex); }
        finally { File.WriteAllText(Out+"unity-sample-pose-verification.json",result.ToString()); }
        if (!(bool)result["pass"]) EditorApplication.Exit(1);
    }
}
