using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using Remielle.ControllerRuntime;
using UnityEditor;
using UnityEngine;

public static class NativeStateClipBindingsAudit
{
    const string Out="E:/ZZZ/ZCode/90_Builds/ControllerImplementation/20260906/";
    static void Require(bool ok,string why){if(!ok)throw new Exception(why);}
    static float Float(JToken t)=>BitConverter.Int32BitsToSingle(unchecked((int)Convert.ToUInt32((string)t,16)));
    static string Bits(float v)=>unchecked((uint)BitConverter.SingleToInt32Bits(v)).ToString("x8");
    static string Hash(string path){using(var s=File.OpenRead(path))using(var h=SHA256.Create())return NativeMotionArchive.Hex(h.ComputeHash(s));}
    static string Digest(byte[] b){using(var h=SHA256.Create())return NativeMotionArchive.Hex(h.ComputeHash(b));}
    static void Equal(JToken actual,JToken expected,string where){Require(JToken.DeepEquals(actual,expected),where+" differs: "+actual.ToString(Newtonsoft.Json.Formatting.None));}
    static JObject Snapshot(NativeStateClipBindings set)=>new JObject{
        ["boundLeafCount"]=set.BoundLeafCount,["ikOnFeet"]=set.IKOnFeet,["writeDefaultValues"]=set.WriteDefaultValues,["stateSpeedBits"]=Bits(set.StateSpeed),
        ["slots"]=new JArray(set.Slots.Select(s=>new JObject{["assetID"]=s.AssetID==null?JValue.CreateNull():new JValue(s.AssetID),["nameID"]=s.NameID,["pathID"]=s.PathID,["fullPathID"]=s.FullPathID,["tagID"]=s.TagID,
            ["stateLoop"]=s.StateLoop,["dirty"]=s.BindingsDirty,["ready"]=s.ResourceReady,["ikOnFeet"]=s.IKOnFeet,["writeDefaultValues"]=s.WriteDefaultValues,["stateSpeedBits"]=Bits(s.StateSpeed),["samplingLoop"]=s.SamplingLoop}))};
    static JObject Fields(NativeStateClipFields s)=>new JObject{["nameid"]=s.NameID,["path"]=s.PathID,["full"]=s.FullPathID,["tag"]=s.TagID,
        ["sp"]=s.SpeedParamID,["mp"]=s.MirrorParamID,["cp"]=s.CycleOffsetParamID,["extra"]=s.TimeParamID,["speed"]=Bits(s.Speed),["cycle"]=Bits(s.CycleOffset),
        ["ikf"]=s.IKOnFeet,["wdv"]=s.WriteDefaultValues,["loop"]=s.Loop,["mir"]=s.Mirror};
    static NativePoseData Pose(JToken rows)=>new NativePoseData(rows[0].Select(Float).ToArray(),rows[1].Select(Float).ToArray(),rows[2].Select(Float).ToArray(),rows[3].Select(Float).ToArray(),Array.Empty<uint>());
    static JObject PoseDigest(NativePoseStream stream)
    {
        using(var s=new MemoryStream())
        {
            var pose=stream.Pose();for(int g=0;g<4;g++){var a=pose.FloatChannel(g);var b=new byte[a.Length*4];Buffer.BlockCopy(a,0,b,0,b.Length);s.Write(b,0,b.Length);}
            return new JObject{["poseSha256"]=Digest(s.ToArray()),["maskSha256"]=Digest(stream.Mask().SelectMany(r=>r).ToArray())};
        }
    }
    public static JObject Run(string path,NativeControllerSource controllers)
    {
        var data=JObject.Parse(File.ReadAllText(path));var e=data["evidence"];
        foreach(string k in new[]{"source","sourcePack","bank","intervals","generator","oracle"})
            Require(Hash((string)e[k]["path"])==(string)e[k]["sha256"],"State Clip source changed: "+k);
        foreach(var r in e["dependencies"].Concat(e["sourceMotionSettings"]))Require(Hash((string)r["path"])==(string)r["sha256"],"State Clip dependency changed");
        var bank=new NativeMotionBank(Path.GetDirectoryName((string)e["bank"]["path"]));
        int fields=0,sourceSteps=0,fixtureSteps=0,times=0,poseChecks=0;var seen=new System.Collections.Generic.HashSet<string>();
        var pf=data["poseFixture"];var mask=pf["counts"].Select(n=>Enumerable.Repeat((byte)1,(int)n).ToArray()).ToArray();
        var d=new NativePoseStream(Pose(pf["defaults"]),mask);var ov=new NativePoseStream(Pose(pf["overrideDefaults"]),mask);var ev=new NativePoseStream(Pose(pf["evaluationDefaults"]),mask);
        for(int repeat=0;repeat<2;repeat++)
        {
            foreach(var r in data["fields"])
            {
                var c=controllers.GetController((string)r["controller"]);var s=(JObject)c["machines"][(int)r["machine"]]["states"][(int)r["state"]];
                Equal(Fields(new NativeStateClipFields(s)),r["expected"],"Source StateConstant fields");fields++;
            }
            foreach(bool fixture in new[]{false,true})foreach(var group in data[fixture?"fixtures":"groups"])
            {
                var set=new NativeStateClipBindings((int)group["capacity"]);
                foreach(var r in group["steps"])
                {
                    JObject state=r["state"].Type==JTokenType.Null?null:(JObject)r["state"];
                    JObject tree=r["tree"].Type==JTokenType.Null?null:(JObject)r["tree"];
                    if(fixture)
                    {
                        NativeStateClipFields exact=null;
                        if(state!=null)
                        {
                            var s=new NativeStateClipFields(state);
                            exact=new NativeStateClipFields(s.NameID,s.PathID,s.FullPathID,s.TagID,s.SpeedParamID,s.MirrorParamID,s.CycleOffsetParamID,s.TimeParamID,
                                Float(r["stateFloatBits"]["speed"]),Float(r["stateFloatBits"]["cycle"]),s.IKOnFeet,s.WriteDefaultValues,s.Loop,s.Mirror);
                        }
                        set.Apply(exact,tree,i=>(string)group["table"][i.ToString()]);
                    }
                    else
                    {
                        var src=r["source"];string name=(string)src["controller"];var c=controllers.GetController(name);
                        var sourceState=(JObject)c["machines"][(int)src["machine"]]["states"][(int)src["state"]];
                        JObject sourceTree=(int)src["tree"]<0?null:(JObject)sourceState["trees"][(int)src["tree"]];
                        set.ApplySource(sourceState,sourceTree,name,bank);
                    }
                    Equal(Snapshot(set),r["expectedSetup"],"State Clip setup");
                    foreach(var slot in set.Slots)if(slot.AssetID!=null)
                        slot.CompleteResourceBinding((bool)data["sourceLoops"][slot.AssetID],(bool)r["overrideEnabled"],(bool)r["overrideValue"]);
                    Equal(Snapshot(set),r["expectedReady"],"State Clip resource publication");
                    bool positive=set.DispatchPolicy(r["weights"].Select(Float).ToArray());
                    Equal(new JObject{["anyPositive"]=positive,["state"]=Snapshot(set)},r["expectedDispatch"],"State Clip policy dispatch");
                    foreach(var slot in set.Slots)if(slot.AssetID!=null)for(int variant=0;variant<2;variant++)
                    {
                        var output=new NativePoseStream(Pose(pf["initial"]),mask);
                        var context=slot.CreatePoseContext(d,ov,ev,variant!=0,true,true,-1,-1,-1,mask);
                        NativeClipPoseChannels.GatherSamples(Array.Empty<float>(),mask.Select(row=>row.Select(_=>-1).ToArray()).ToArray(),context,output);
                        var expected=pf["cases"].Single(x=>(bool)x["writeDefaults"]==slot.WriteDefaultValues && (bool)x["evaluation18"]==(variant!=0));
                        Equal(PoseDigest(output),expected["expected"],"Dispatched policy to actual Clip default consumer");poseChecks++;
                    }
                    if(!fixture)
                    {
                        foreach(var query in r["sampleTimes"])
                        {
                            var slot=set.Slots[(int)query["slot"]];string id=(string)query["assetID"];Require(slot.AssetID==id,"Time source identity differs");seen.Add(id);
                            var interval=data["intervals"][id];var playback=new NativeClipPlayback(new NativeMotionInterval(Float(interval["startBits"]),Float(interval["stopBits"])));
                            playback.ApplyClock((float)r["normalized"],(float)r["previous"],false);
                            var value=slot.GetSampleTime(playback,new NativeLeafTiming(1f,0f,false),Float(data["sourceCycles"][id]),0f,1f);
                            Equal(new JObject{["secondsBits"]=Bits(value.Seconds),["phaseBits"]=Bits(value.Phase),["cycleCountBits"]=Bits(value.CycleCount)},query["expected"],"Source policy to original sample time");times++;
                        }
                        sourceSteps++;
                    }
                    else fixtureSteps++;
                }
            }
        }
        Require(fields==2*(int)e["sourceStates"] && sourceSteps==2*(int)e["sourceSteps"] && fixtureSteps==2*(int)e["fixtureSteps"] && times==2*(int)e["sourceSamplingQueries"] && seen.Count==(int)e["sourceMotions"],"State Clip coverage differs");
        return new JObject{["sourceStates"]=fields/2,["sourceFieldComparisons"]=fields/2*14,["sourceSteps"]=sourceSteps/2,["sourceMotions"]=seen.Count,["sourceSamplingQueries"]=times/2,["fixtureSteps"]=fixtureSteps/2,["posePolicyQueries"]=poseChecks/2,["repeats"]=2,
            ["originalStateFieldsVerified"]=true,["originalSetupAndDispatchBitwiseEqual"]=true,["stateWriteDefaultsProducerConnected"]=true,["sourceAndOverrideSamplingLoopConnected"]=true,["poseAndTimeConsumersComposed"]=true,
            ["graphAllocationAndParentDirtyPropagationQualified"]=false,["completeResourceBinderQualified"]=false,["evaluationAndRootChannelProducersQualified"]=false,["controllerPlayable"]=false,
            ["vectorsSha256"]=Hash(path),["runtimeSha256"]=Hash("Assets/ControllerRuntime/NativeStateClipBindings.cs"),["poseRuntimeSha256"]=Hash("Assets/ControllerRuntime/NativeClipPoseChannels.cs"),["timeRuntimeSha256"]=Hash("Assets/ControllerRuntime/NativeClipTime.cs"),["auditSha256"]=Hash("Assets/ControllerRuntime/Editor/NativeStateClipBindingsAudit.cs")};
    }
    public static void RunBatch()
    {
        var report=new JObject{["pass"]=false};
        try
        {
            var source=new NativeControllerSource(File.ReadAllText("Assets/ControllerRuntime/Data/source-controller-pack.json"));
            report["stateClipBindings"]=Run(Out+"native-state-clip-bindings-vectors.json",source);report["pass"]=true;
            Debug.Log("NATIVE_STATE_CLIP_BINDINGS_OK "+report.ToString(Newtonsoft.Json.Formatting.None));
        }
        catch(Exception ex){report["error"]=ex.ToString();Debug.LogException(ex);}
        finally{File.WriteAllText(Out+"unity-state-clip-bindings-verification.json",report.ToString());}
        if(!(bool)report["pass"])EditorApplication.Exit(1);
    }
}
