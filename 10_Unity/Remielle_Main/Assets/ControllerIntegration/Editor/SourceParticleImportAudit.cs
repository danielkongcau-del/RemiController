using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Remielle.Controller.Editor
{
    // Establish which source fields Unity's native particle serializer consumes.
    // This audit creates temporary objects only; it does not publish a prefab.
    public static class SourceParticleImportAudit
    {
        const string Output="E:/ZZZ/ZCode/90_Builds/ControllerDependencies/implementation/";
        public static void Run()
        { RunFor(Output+"hit-particle-prefix.json",Output); }
        public static void RunSpecialFlash()
        { RunFor(Output+"skill-fx-sources/particle-prefix.json",Output+"skill-fx-sources/",true); }
        public static void RunSpecialBurst()
        { RunFor(Output+"skill-burst-sources/particle-prefix.json",Output+"skill-burst-sources/",true,10f); }
        public static void RunSpecialSmoke()
        { RunFor(Output+"skill-smoke-sources/particle-prefix.json",Output+"skill-smoke-sources/",true,15f,true); }
        public static void RunSpecialTrail()
        { RunFor(Output+"skill-trail-sources/particle-prefix.json",Output+"skill-trail-sources/",true,30f,true); }
        static void RunFor(string input,string output,bool allowForkRing=false,float lastSample=.8f,bool qualifiedLights=false)
        {
            var report=new JObject{["pass"]=false};var cases=new JArray();report["cases"]=cases;
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                var pack=JObject.Parse(File.ReadAllText(input));
                foreach(JObject row in pack["rows"])
                {
                    if(row["error"].Type!=JTokenType.Null)throw new Exception("Source particle parse failed");
                    var source=(JObject)row["unityEditorJson"].DeepClone();source.Remove("m_GameObject");
                    JObject lightSource=null;
                    JObject shapeLink=null;Mesh shapeMesh=null;
                    var shapePointer=source["ShapeModule"]?["m_Mesh"] as JObject;
                    if(qualifiedLights && shapePointer!=null && (long)shapePointer["m_PathID"]!=0)
                    {
                        shapeLink=JObject.Parse(File.ReadAllText(output+"shape-mesh-links.json"))["rows"].OfType<JObject>().Single(l=>JToken.DeepEquals(l["particleIdentity"],row["identity"]));
                        if((long)shapeLink["pathID"]!=(long)shapePointer["m_PathID"])throw new Exception("Shape mesh identity differs");
                        var manifest=File.ReadLines(output+"mesh-json/manifest.ndjson").Select(JObject.Parse).Single(m=>(string)m["cab"]==(string)shapeLink["cab"]&&(long)m["pathID"]==(long)shapeLink["pathID"]);
                        var meshPath=Path.Combine(output+"mesh-json",(string)manifest["files"][0]["relativePath"]);
                        using(var hash=System.Security.Cryptography.SHA256.Create())
                            if(BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(meshPath))).Replace("-","").ToLowerInvariant()!=((string)manifest["files"][0]["sha256"]).ToLowerInvariant())throw new Exception("Shape mesh file changed");
                        var data=JObject.Parse(File.ReadAllText(meshPath));int count=(int)data["m_VertexCount"];
                        var vertices=new Vector3[count];var normals=new Vector3[count];
                        for(int i=0;i<count;i++){vertices[i]=new Vector3((float)data["m_Vertices"][i*3],(float)data["m_Vertices"][i*3+1],(float)data["m_Vertices"][i*3+2]);normals[i]=new Vector3((float)data["m_Normals"][i*3],(float)data["m_Normals"][i*3+1],(float)data["m_Normals"][i*3+2]);}
                        shapeMesh=new Mesh{vertices=vertices,normals=normals,triangles=data["m_Indices"].Select(i=>(int)i).ToArray()};
                        shapeLink["meshManifest"]=manifest.DeepClone();shapePointer["m_FileID"]=0;shapePointer["m_PathID"]=0;
                    }
                    var lightPointer=source["LightsModule"]?["light"] as JObject;
                    if(qualifiedLights && lightPointer!=null && (long)lightPointer["m_PathID"]!=0)
                    {
                        if((int)lightPointer["m_FileID"]!=0)throw new Exception("External particle light requires owner-qualified resolution");
                        var lightRows=JObject.Parse(File.ReadAllText(output+"light-data.json"))["rows"];
                        lightSource=lightRows.OfType<JObject>().Single(l=>
                            (string)l["identity"]["sourceBlock"]==(string)row["identity"]["sourceBlock"] &&
                            (string)l["identity"]["cab"]==(string)row["identity"]["cab"] &&
                            (long)l["identity"]["pathID"]==(long)lightPointer["m_PathID"]);
                        if(!(bool)lightSource["fullByteRoundTrip"])throw new Exception("Unverified light source");
                        // Canonical JSON cannot contain transient instance IDs.
                        // Preserve qualified linkage separately for prefab assembly.
                        lightPointer["m_PathID"]=0;
                    }
                    var go=new GameObject((string)row["identity"]["nodePaths"][0]);
                    var ps=go.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
                    var clean=(JObject)Clean(source);
                    int sourceRing=(int)clean["ringBufferMode"];
                    bool ringFallback=!Enum.IsDefined(typeof(ParticleSystemRingBufferMode),sourceRing);
                    if(ringFallback)
                    {
                        // The preserved fork bytes contain 3, outside Unity's
                        // public enum. Explicit preview policy, not native parity.
                        if(!allowForkRing||sourceRing!=3)throw new Exception("Unsupported ring buffer mode");
                        clean["ringBufferMode"]=(int)ParticleSystemRingBufferMode.Disabled;
                    }
                    var expectedVelocity=(bool)clean["useRigidbodyForVelocity"]?ParticleSystemEmitterVelocityMode.Rigidbody:ParticleSystemEmitterVelocityMode.Transform;
                    clean.Remove("useRigidbodyForVelocity");clean["emitterVelocityMode"]=(int)expectedVelocity;
                    // Migration to the current root version must accompany the
                    // new field, otherwise Unity's old-version upgrade replaces it.
                    clean["serializedVersion"]=JObject.Parse(EditorJsonUtility.ToJson(ps))["ParticleSystem"]["serializedVersion"].DeepClone();
                    EditorJsonUtility.FromJsonOverwrite(new JObject{["ParticleSystem"]=clean}.ToString(Newtonsoft.Json.Formatting.None),ps);
                    var actual=(JObject)JObject.Parse(EditorJsonUtility.ToJson(ps))["ParticleSystem"];
                    // Explicitly map the removed legacy flag; JSON import alone
                    // ignores it. Check runtime meaning and empty collider slots.
                    if(ps.main.emitterVelocityMode!=expectedVelocity)throw new Exception("Emitter velocity migration changed semantics: "+ps.main.emitterVelocityMode+" / "+expectedVelocity);
                    for(int i=0;i<6;i++)
                    {
                        if((int)clean["CollisionModule"]["plane"+i]["instanceID"]!=0 ||
                           (int)clean["TriggerModule"]["collisionShape"+i]["instanceID"]!=0)
                            throw new Exception("Non-null collider requires explicit migration");
                        ((JObject)clean["CollisionModule"]).Remove("plane"+i);
                        ((JObject)clean["TriggerModule"]).Remove("collisionShape"+i);
                    }
                    for(int i=0;i<ps.collision.planeCount;i++)
                        if(ps.collision.GetPlane(i)!=null)throw new Exception("Unexpected collision plane after migration");
                    for(int i=0;i<ps.trigger.colliderCount;i++)
                        if(ps.trigger.GetCollider(i)!=null)throw new Exception("Unexpected trigger collider after migration");
                    var differences=new JArray();Compare(clean,actual,"",differences);
                    if(shapeMesh)
                    {
                        var shape=ps.shape;shape.mesh=shapeMesh;
                        if(ps.shape.mesh!=shapeMesh)throw new Exception("Shape mesh binding failed");
                        File.WriteAllText(output+"particle-shape-link-"+(string)row["identity"]["pathID"]+".json",shapeLink.ToString());
                    }
                    GameObject lightObject=null;
                    if(lightSource!=null)
                    {
                        lightObject=new GameObject("Source particle light template");
                        var template=lightObject.AddComponent<Light>();
                        var values=lightSource["values"];
                        template.type=(LightType)(int)values["m_Type"];
                        template.intensity=(float)values["m_Intensity"];
                        template.range=(float)values["m_Range"];
                        template.enabled=(int)values["m_Enabled"]!=0;
                        var module=ps.lights;module.light=template;
                        if(ps.lights.light!=template)throw new Exception("Particle light binding failed");
                        File.WriteAllText(output+"particle-light-link-"+(string)row["identity"]["pathID"]+".json",
                            new JObject{["particleIdentity"]=row["identity"].DeepClone(),["lightSource"]=lightSource.DeepClone(),
                                ["canonicalReferenceDeferred"]=true,["prefabBindingVerified"]=false}.ToString());
                    }
                    var samples=new JArray();
                    ps.useAutoRandomSeed=false;ps.randomSeed=14591;
                    var sampleTimes=new System.Collections.Generic.List<float>{0f,.02f,.05f,.1f,.2f,.4f,lastSample};
                    float delay=ps.main.startDelay.constantMax;
                    sampleTimes.Add(delay+.02f);sampleTimes.Add(delay+ps.main.duration*.5f);
                    var bursts=new ParticleSystem.Burst[ps.emission.burstCount];ps.emission.GetBursts(bursts);
                    foreach(var burst in bursts)sampleTimes.Add(delay+burst.time+.02f);
                    foreach(float time in sampleTimes.Where(t=>t<=lastSample).Distinct().OrderBy(t=>t))
                    {
                        ps.Simulate(time,false,true,false);
                        samples.Add(new JObject{["time"]=time,["particles"]=ps.particleCount});
                    }
                    bool emits=(bool)source["EmissionModule"]["enabled"];
                    if(emits!=samples.Any(s=>(int)s["particles"]>0))throw new Exception("Emission presence differs from source");
                    if(!ps.main.loop && (int)samples.Last["particles"]!=0)throw new Exception("One-shot particles did not expire");
                    ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
                    if(ps.particleCount!=0)throw new Exception("Particle cancellation did not clear");
                    cases.Add(new JObject{["pathID"]=row["identity"]["pathID"],["node"]=go.name,
                        ["differences"]=differences,["samples"]=samples,["legacyVelocityAndNullColliderMigrationChecked"]=true,
                        ["sourceRingBufferMode"]=sourceRing,["ringBufferPreviewFallback"]=ringFallback});
                    File.WriteAllText(output+"particle-import-"+(string)row["identity"]["pathID"]+".json",actual.ToString());
                    UnityEngine.Object.DestroyImmediate(go);
                    if(lightObject!=null)UnityEngine.Object.DestroyImmediate(lightObject);
                    if(shapeMesh)UnityEngine.Object.DestroyImmediate(shapeMesh);
                }
                report["pass"]=cases.All(c=>!c["differences"].Any());
                report["rendered"]=false;
            }
            catch(Exception e){report["error"]=e.ToString();Debug.LogException(e);}
            File.WriteAllText(output+"source-particle-import-verification.json",report.ToString());
            if(!(bool)report["pass"])throw new Exception("Particle import verification requires review");
        }
        internal static JToken Clean(JToken value)
        {
            if(value is JObject o)
            {
                if(o["m_FileID"]!=null && o["m_PathID"]!=null)
                {
                    if((long)o["m_PathID"]!=0)throw new Exception("Particle object reference requires qualified linking");
                    return new JObject{["instanceID"]=0};
                }
                var r=new JObject();foreach(var p in o.Properties())
                    if(p.Name!="unresolvedForkField")r[p.Name]=Clean(p.Value);
                return r;
            }
            if(value is JArray a)return new JArray(a.Select(Clean));
            return value.DeepClone();
        }
        internal static void Compare(JToken expected,JToken actual,string path,JArray differences)
        {
            if(actual==null){differences.Add(path+": missing");return;}
            if(path.EndsWith("/serializedVersion",StringComparison.Ordinal))
            {
                if(int.Parse((string)actual)<int.Parse((string)expected))differences.Add(path+": version regressed");
                return;
            }
            if(expected is JObject o)
            {foreach(var p in o.Properties())Compare(p.Value,actual[p.Name],path+"/"+p.Name,differences);return;}
            if(expected is JArray a)
            {
                if(!(actual is JArray b)||a.Count!=b.Count){differences.Add(path+": array size");return;}
                for(int i=0;i<a.Count;i++)Compare(a[i],b[i],path+"/"+i,differences);return;
            }
            if(expected.Type==JTokenType.Float||expected.Type==JTokenType.Integer)
            {
                if(actual.Type!=JTokenType.Float&&actual.Type!=JTokenType.Integer){differences.Add(path+": numeric type");return;}
                double x=(double)expected,y=(double)actual;
                if(Math.Abs(x-y)>1e-6*Math.Max(1,Math.Abs(x)))differences.Add(path+": numeric value");
            }
            else if(!JToken.DeepEquals(expected,actual))differences.Add(path+": value");
        }
    }
}
