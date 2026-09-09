using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

public static class NativeRotationAudit
{
    const string Out="E:/ZZZ/ZCode/90_Builds/ModelReadiness/20260904";
    public static void Audit(string file,bool requirePass)
    {
        var manifest=JObject.Parse(File.ReadAllText("Assets/SourceAssets/AnimationInputs/animation_inputs.json"));
        var rows=new JArray();bool pass=true;
        foreach(var row in manifest["clips"])
        {
            string name=(string)row["name"];var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/V3/Animations/"+name+".anim");
            var packed=File.ReadAllBytes((string)row["packed"]);int n=BitConverter.ToInt32(packed,4),start=8+n;
            var header=JObject.Parse(System.Text.Encoding.UTF8.GetString(packed,8,n));
            int tracks=0,intervals=0,signFlips=0,keyCount=0,badIntervals=0;float maxKeyError=0,maxIntervalError=0;string worstPath="";float worstTime=0;
            foreach(var track in header["tracks"])
            {
                if((string)track["property"]!="m_LocalRotation")continue;
                string path=(string)track["path"];int count=(int)track["keyCount"],offset=start+(int)track["offset"];
                var curves=Enumerable.Range(0,4).Select(c=>AnimationUtility.GetEditorCurve(clip,EditorCurveBinding.FloatCurve(path,typeof(Transform),"m_LocalRotation."+"xyzw"[c]))).ToArray();
                if(curves.Any(c=>c==null))throw new Exception("Missing quaternion curve: "+path);
                Quaternion Source(int i)=>new Quaternion(BitConverter.ToSingle(packed,offset+i*52+4),BitConverter.ToSingle(packed,offset+i*52+8),BitConverter.ToSingle(packed,offset+i*52+12),BitConverter.ToSingle(packed,offset+i*52+16));
                float TimeAt(int i)=>BitConverter.ToSingle(packed,offset+i*52);
                Quaternion Actual(float t)=>new Quaternion(curves[0].Evaluate(t),curves[1].Evaluate(t),curves[2].Evaluate(t),curves[3].Evaluate(t));
                int retained=0;
                for(int i=0;i<count;i++)
                {
                    float time=TimeAt(i);if(time>(float)row["duration"]+1e-6)break;
                    retained++;keyCount++;var source=Source(i);
                    maxKeyError=Mathf.Max(maxKeyError,Distance(Actual(time),source));
                    if(i+1>=count||TimeAt(i+1)>(float)row["duration"]+1e-6)continue;
                    var next=Source(i+1);if(Quaternion.Dot(source,next)<0)signFlips++;
                    // Quarter, midpoint and three-quarter samples cover cubic
                    // tangent errors as well as sign cancellation at midpoints.
                    foreach(float f in new[]{.25f,.5f,.75f})
                    {
                        float t=Mathf.Lerp(time,TimeAt(i+1),f);float error=Distance(Actual(t),Quaternion.Lerp(source,next,f));
                        intervals++;if(error>0.0001f)badIntervals++;
                        if(error>maxIntervalError){maxIntervalError=error;worstPath=path;worstTime=t;}
                    }
                }
                if(curves.Any(c=>c.length!=retained))throw new Exception("Quaternion key count changed: "+name+"/"+path);
                tracks++;
            }
            bool ok=maxKeyError<0.00001f&&maxIntervalError<0.0001f;pass&=ok;
            rows.Add(new JObject{["clip"]=name,["pass"]=ok,["quaternionTracks"]=tracks,["sourceKeys"]=keyCount,["intervalSamples"]=intervals,["sourceSignFlipIntervals"]=signFlips,["maxKeyOrientationChordError"]=maxKeyError,["maxIntervalOrientationChordError"]=maxIntervalError,["badIntervalSamples"]=badIntervals,["worstPath"]=worstPath,["worstTime"]=worstTime});
        }
        File.WriteAllText(Path.IsPathRooted(file)?file:Out+"/"+file,new JObject{["pass"]=pass,["reference"]="Shortest-path normalized interpolation of original ACL samples; exact original key times/count",["clips"]=rows}.ToString());
        if(requirePass&&!pass)throw new Exception("Quaternion interpolation mismatch; see "+file);
    }
    static float Distance(Quaternion a,Quaternion b)
    {
        float len=Mathf.Sqrt(Quaternion.Dot(a,a));if(len<1e-8f)return 2;
        a=new Quaternion(a.x/len,a.y/len,a.z/len,a.w/len);b=Quaternion.Normalize(b);
        var av=new Vector4(a.x,a.y,a.z,a.w);var bv=new Vector4(b.x,b.y,b.z,b.w);
        return Mathf.Min((av-bv).magnitude,(av+bv).magnitude);
    }
}
