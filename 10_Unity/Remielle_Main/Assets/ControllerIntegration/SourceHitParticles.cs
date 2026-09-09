using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Pool;
using Object=UnityEngine.Object;

namespace Remielle.Controller
{
 // Unity simulates the source particles. This host supplies per-hit lifetime,
 // world time and pooling, independently of the actor's local hitstop clock.
 public sealed class SourceHitParticles : IDisposable
 {
  sealed class Definition
  {
   public string Key;public GameObject Prefab;public float Start,Duration;
   public string[] Paths;public float[] Cuts;public bool[] Emits;
   public ObjectPool<Instance> Pool;
  }
  sealed class Instance
  {
   public Definition Definition;public GameObject Root;public ParticleSystem[] Systems;
   public ParticleSystemRenderer[] Renderers;public bool[] RendererEnabled;
   public float Age;public bool Used;
   public readonly MaterialPropertyBlock Block=new MaterialPropertyBlock();
  }
  public readonly struct Sample
  {
   public readonly GameObject Root;public readonly string Key;public readonly float Age;public readonly int Particles;
   public Sample(GameObject root,string key,float age,int particles){Root=root;Key=key;Age=age;Particles=particles;}
  }
  readonly SourceHitQuery query;
  readonly Dictionary<string,Definition> definitions=new();
  readonly List<Instance> active=new();
  readonly GameObject holder;
  readonly float visualScale;
  bool disposed,rendering=true;
  public int Activations {get;private set;}
  public int Completed {get;private set;}
  public int Cancelled {get;private set;}
  public int Created {get;private set;}
  public int Reused {get;private set;}
  public int Unsupported {get;private set;}
  public int Prewarmed {get;private set;}
  public int ActiveCount=>active.Count;
  public double WorldSeconds {get;private set;}
  public IReadOnlyList<Sample> Snapshot()=>active.Select(i=>new Sample(i.Root,i.Definition.Key,i.Age,i.Systems.Sum(p=>p.particleCount))).ToArray();
  public SourceHitParticles(string json,GameObject[] prefabs,SourceHitQuery query,float visualScale=1)
  {
   var pack=JObject.Parse(json);if((string)pack["schema"]!="remielle-hit-particles-runtime-v1")throw new ArgumentException("Wrong hit particle pack");
   if(!float.IsFinite(visualScale)||visualScale<=0)throw new ArgumentOutOfRangeException(nameof(visualScale));this.visualScale=visualScale;
   int prewarm=(int)pack["policy"]["prewarmPerEffect"];if(prewarm<1||prewarm>32)throw new ArgumentOutOfRangeException("prewarmPerEffect");
   holder=new GameObject("RemielleHitParticles");this.query=query;
   try
   {
    foreach(var row in pack["effects"])
    {
     if(!(bool)row["ignoreOwnerTimeScale"]||(bool)row["ignoreWorldTimeScale"]||!(bool)row["onlyFirstFrame"])throw new NotSupportedException("Different effect clock/follow policy");
     var prefab=prefabs.Single(p=>p&&p.name==(string)row["prefabName"]);
     var d=new Definition{Key=(string)row["key"],Prefab=prefab,Start=(float)row["startTimeOffset"],Duration=(float)row["duration"],
      Paths=row["nodes"].Select(n=>(string)n["path"]).ToArray(),Cuts=row["nodes"].Select(n=>(float)n["cutTime"]).ToArray(),Emits=row["nodes"].Select(n=>(bool)n["emits"]).ToArray()};
     d.Pool=new ObjectPool<Instance>(()=>Create(d),null,Release,DestroyInstance,true,prewarm,32);definitions.Add(d.Key,d);
     var warm=new Instance[prewarm];for(int i=0;i<warm.Length;i++)warm[i]=d.Pool.Get();foreach(var i in warm)d.Pool.Release(i);
    }
    Prewarmed=Created;if(query!=null)query.Hit+=Emit;
   }
   catch{Dispose();throw;}
  }
  Instance Create(Definition d)
  {
   var root=Object.Instantiate(d.Prefab,holder.transform);root.SetActive(false);
   var systems=d.Paths.Select(p=>(p.Length==0?root.transform:root.transform.Find(p)).GetComponent<ParticleSystem>()).ToArray();
   if(systems.Any(p=>!p)||systems.Length!=root.GetComponentsInChildren<ParticleSystem>(true).Length)throw new InvalidOperationException("Particle hierarchy changed");
   foreach(var p in systems)p.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
   var renderers=systems.Select(p=>p.GetComponent<ParticleSystemRenderer>()).ToArray();Created++;
   return new Instance{Definition=d,Root=root,Systems=systems,Renderers=renderers,RendererEnabled=renderers.Select(r=>r.enabled).ToArray()};
  }
  public void Emit(SourceHitQuery.Receipt receipt)
  {
   if(disposed)throw new ObjectDisposedException(nameof(SourceHitParticles));
   foreach(var effect in receipt.Config["AttackEffect"]["AttackEffects"])
   {
    if(!definitions.TryGetValue((string)effect["EffectName"],out var d)){Unsupported++;continue;}
    var i=d.Pool.Get();if(i.Used)Reused++;i.Used=true;
    // The source follow plugin only captures the first frame. Deliberately do
    // not parent the instance to a moving target or actor.
    i.Root.transform.SetPositionAndRotation(receipt.HurtboxCenter,receipt.AttackerRotation*
     Quaternion.Euler((float)effect["XRotOffset"],(float)effect["YRotOffset"],(float)effect["ZRotOffset"])*d.Prefab.transform.localRotation);
    i.Root.transform.localScale=d.Prefab.transform.localScale*visualScale;i.Age=d.Start;i.Root.SetActive(true);
    for(int n=0;n<i.Systems.Length;n++)
    {
     var ps=i.Systems[n];ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
     if(d.Emits[n]&&i.Age<d.Cuts[n])ps.Simulate(i.Age,false,true,false);
     i.Renderers[n].enabled=rendering&&i.RendererEnabled[n];
    }
    SetTime(i);active.Add(i);Activations++;
   }
  }
  static void SetTime(Instance i)
  {
   i.Block.Clear();i.Block.SetFloat("_EffectTime",i.Age);
   foreach(var r in i.Renderers)r.SetPropertyBlock(i.Block);
  }
  public void Advance(float worldDelta)
  {
   if(disposed)return;
   if(!float.IsFinite(worldDelta)||worldDelta<0)throw new ArgumentOutOfRangeException(nameof(worldDelta));
   if(worldDelta==0)return;WorldSeconds+=worldDelta;
   for(int k=active.Count-1;k>=0;k--)
   {
    var i=active[k];i.Age+=worldDelta;
    for(int n=0;n<i.Systems.Length;n++)
    {
     if(!i.Definition.Emits[n])continue;
     if(i.Age>=i.Definition.Cuts[n])i.Systems[n].Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
     else i.Systems[n].Simulate(worldDelta,false,false,false);
    }
    SetTime(i);
    if(i.Age+1e-6f>=i.Definition.Duration){active.RemoveAt(k);i.Definition.Pool.Release(i);Completed++;}
   }
  }
  public void SetRenderingEnabled(bool enabled)
  {
   rendering=enabled;foreach(var i in active)for(int n=0;n<i.Renderers.Length;n++)i.Renderers[n].enabled=enabled&&i.RendererEnabled[n];
  }
  static void Release(Instance i)
  {
   foreach(var p in i.Systems)p.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
   foreach(var r in i.Renderers)r.SetPropertyBlock(null);i.Root.SetActive(false);i.Age=0;
  }
  static void DestroyInstance(Instance i){if(Application.isPlaying)Object.Destroy(i.Root);else Object.DestroyImmediate(i.Root);}
  public void Clear()
  {
   foreach(var i in active){i.Definition.Pool.Release(i);Cancelled++;}active.Clear();
  }
  public void Dispose()
  {
   if(disposed)return;disposed=true;if(query!=null)query.Hit-=Emit;Clear();
   foreach(var d in definitions.Values)d.Pool.Dispose();definitions.Clear();
   if(holder){if(Application.isPlaying)Object.Destroy(holder);else Object.DestroyImmediate(holder);}
  }
 }
}
