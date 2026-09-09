using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Pool;
using Object=UnityEngine.Object;

namespace Remielle.Controller
{
 // Finite ordinary flash adapter. Other source events remain observable and
 // counted as deferred; this is not a claim of complete skill VFX parity.
 public sealed class SourceSkillParticles : IDisposable
 {
  sealed class Definition
  {
   public string Key,Kind;public GameObject Prefab;public RemielleNativeAnimation.BoneLink Bone;
   public int State,Frame,Entry;public bool Attached,OnlyFirstFrame,CancelOnLeave;
   public string[] Paths;public int[][] SiblingPaths;public float[] Cuts;public bool[] Emits;public float Duration;
   public ObjectPool<Instance> Pool;
   public JArray Lines;
  }
  sealed class Instance
  {
   public Definition Definition;public GameObject Root;public ParticleSystem[] Systems;
   public ParticleSystemRenderer[] Renderers;public bool[] Visible,LightEnabled;public long Generation;
   public float Age;public bool Pending;public readonly MaterialPropertyBlock Block=new();
   public SourceEffectLines Lines;
  }
  readonly Dictionary<string,Definition> definitions=new();readonly List<Instance> active=new();
  readonly HashSet<string> hiddenKinds=new();
  readonly SourceFrameEvents events;readonly GameObject holder;
  readonly Transform actor;
  bool disposed,rendering=true;
  public int Activations{get;private set;}public int Completed{get;private set;}public int Cancelled{get;private set;}
  public int Created{get;private set;}public int Prewarmed{get;private set;}public int DeferredEvents{get;private set;}
  public int ActiveCount=>active.Count;public int ParticleCount=>active.Sum(i=>i.Systems.Sum(p=>p.particleCount));
  public double ActorSeconds{get;private set;}
  public int ParticleCountFor(string kind)=>active.Where(i=>i.Definition.Kind==kind).Sum(i=>i.Systems.Sum(p=>p.particleCount));
  public float MaximumAttachmentError{get;private set;}
  public int BurstActivations{get;private set;}public int BurstCompleted{get;private set;}public int BurstCancelled{get;private set;}
  public int SmokeActivations{get;private set;}public int SmokeCompleted{get;private set;}public int SmokeCancelled{get;private set;}
  public int TrailActivations{get;private set;}public int TrailCompleted{get;private set;}public int TrailCancelled{get;private set;}
  public IReadOnlyList<GameObject> ActiveRoots=>active.Select(i=>i.Root).ToArray();
  public SourceSkillParticles(string json,GameObject[] prefabs,SourceFrameEvents events,RemielleNativeAnimation driver,Transform actor=null)
  {
   var pack=JObject.Parse(json);if((string)pack["schema"]!="remielle-special-flash-runtime-v1"&&(string)pack["schema"]!="remielle-special-particles-runtime-v1")throw new ArgumentException("Wrong skill pack");
   this.events=events;this.actor=actor;holder=new GameObject("RemielleSkillParticles");
   try
   {
    int warm=(int)pack["prewarmPerEffect"];if(warm<1||warm>16)throw new ArgumentOutOfRangeException();
    foreach(var row in pack["effects"])
    {
     var d=new Definition{Key=(string)row["key"],Prefab=prefabs.Single(p=>p&&p.name==(string)row["prefabName"]),
      Attached=!string.IsNullOrEmpty((string)row["attachPoint"]),Duration=(float)row["duration"],
      State=(int?)row["state"]??54,Frame=(int)row["frame"],Entry=(int)row["sourceEntry"],Kind=(string)row["kind"]??"flash",
      OnlyFirstFrame=(bool?)row["onlyFirstFrame"]??false,CancelOnLeave=(bool?)row["cancelOnLeave"]??true,
      Paths=row["nodes"].Select(n=>(string)n["path"]).ToArray(),Cuts=row["nodes"].Select(n=>(float)n["cutTime"]).ToArray(),
      SiblingPaths=row["nodes"].Select(n=>n["siblingPath"] is JArray a?a.Select(i=>(int)i).ToArray():null).ToArray(),
      Emits=row["nodes"].Select(n=>(bool)n["emits"]).ToArray()};
     d.Lines=row["lines"] as JArray;
     if(d.Attached)d.Bone=driver.bones.Single(b=>b.source.name==(string)row["attachPoint"]);
     else if(!actor)throw new ArgumentException("Detached effect requires actor transform");
     d.Pool=new ObjectPool<Instance>(()=>Create(d),null,Release,DestroyInstance,true,warm,16);definitions.Add(d.Key,d);
     var items=new Instance[warm];for(int k=0;k<warm;k++)items[k]=d.Pool.Get();foreach(var i in items)d.Pool.Release(i);
    }
    Prewarmed=Created;events.Emitted+=Emit;events.Released+=OnReleased;
   }
   catch{Dispose();throw;}
  }
  Instance Create(Definition d)
  {
   var root=Object.Instantiate(d.Prefab,holder.transform);root.SetActive(false);
   var ps=d.Paths.Select((p,index)=>
   {
    var t=root.transform;
    if(d.SiblingPaths[index]!=null)foreach(int child in d.SiblingPaths[index])t=t.GetChild(child);
    else if(p.Length!=0)t=t.Find(p);
    if(!t||AnimationPath(t,root.transform)!=p)throw new Exception("Skill node identity differs: "+p);
    return t.GetComponent<ParticleSystem>();
   }).ToArray();
   if(ps.Length!=root.GetComponentsInChildren<ParticleSystem>(true).Length||ps.Any(p=>!p)||ps.Distinct().Count()!=ps.Length)throw new Exception("Skill hierarchy differs or aliases duplicate nodes");
   foreach(var p in ps)p.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
   var renderers=ps.Select(p=>p.GetComponent<ParticleSystemRenderer>()).ToArray();Created++;
   return new Instance{Definition=d,Root=root,Systems=ps,Renderers=renderers,Visible=renderers.Select(r=>r.enabled).ToArray(),LightEnabled=ps.Select(p=>p.lights.enabled).ToArray(),Lines=new SourceEffectLines(root.transform,d.Lines)};
  }
  static string AnimationPath(Transform node,Transform root)
  {
   var names=new Stack<string>();for(var t=node;t!=root;t=t.parent){if(!t)throw new Exception("Skill node outside root");names.Push(t.name);}
   return string.Join("/",names);
  }
  void Emit(SourceFrameEvents.Emission e)
  {
   if(e.Type!="AnimatorEventEffectEntry"){DeferredEvents++;return;}
   if(!definitions.TryGetValue((string)e.Fields["EffectPatternName"],out var d)){DeferredEvents++;return;}
   if(e.State!=d.State||e.AuthoredFrame!=d.Frame||e.Index!=d.Entry||(string)e.Fields["AttachPointName"]!=(d.Attached?d.Bone.source.name:""))throw new Exception("Skill event binding differs");
   var i=d.Pool.Get();i.Generation=e.Generation;i.Pending=true;i.Age=0;
   Follow(i);i.Root.SetActive(true);
   i.Lines.Advance(0);i.Lines.SetVisible(rendering&&!hiddenKinds.Contains(d.Kind));
   for(int n=0;n<i.Systems.Length;n++)
   {i.Systems[n].Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
    if(d.Emits[n])i.Systems[n].Simulate(0,false,true,false);
    ApplyVisibility(i,n,rendering&&!hiddenKinds.Contains(d.Kind));}
   active.Add(i);Activations++;if(d.Kind=="burst")BurstActivations++;if(d.Kind=="smoke")SmokeActivations++;if(d.Kind=="trail")TrailActivations++;
  }
  void Follow(Instance i)
  {
   if(!i.Definition.Attached)
   {
    i.Root.transform.SetPositionAndRotation(actor.position,actor.rotation*i.Definition.Prefab.transform.localRotation);
    i.Root.transform.localScale=i.Definition.Prefab.transform.localScale;return;
   }
   // Same per-bone coordinate identity used by the source weapon endpoints.
   var m=i.Definition.Bone.target.localToWorldMatrix*i.Definition.Bone.basis.inverse;
   Vector3 x=m.GetColumn(0),y=m.GetColumn(1),z=m.GetColumn(2),position=m.GetColumn(3);
   // Source follow explicitly excludes attachment scale; retain handedness.
   float sign=Vector3.Dot(Vector3.Cross(x,y),z)<0?-1:1;
   if(y.sqrMagnitude<1e-12f||z.sqrMagnitude<1e-12f)throw new Exception("Collapsed skill attachment");
   i.Root.transform.SetPositionAndRotation(position,Quaternion.LookRotation(z.normalized,y.normalized)*i.Definition.Prefab.transform.localRotation);
   i.Root.transform.localScale=Vector3.Scale(i.Definition.Prefab.transform.localScale,new Vector3(sign,1,1));
   MaximumAttachmentError=Mathf.Max(MaximumAttachmentError,Vector3.Distance(i.Root.transform.position,position));
  }
  public void Advance(float actorDelta)
  {
   if(disposed)return;if(!float.IsFinite(actorDelta)||actorDelta<0)throw new ArgumentOutOfRangeException();ActorSeconds+=actorDelta;
   for(int k=active.Count-1;k>=0;k--)
   {
    var i=active[k];if(!i.Definition.OnlyFirstFrame)Follow(i);float delta=i.Pending?0:actorDelta;i.Pending=false;i.Age+=delta;
    i.Lines.Advance(i.Age);
    for(int n=0;n<i.Systems.Length;n++)
    {
     if(!i.Definition.Emits[n])continue;
     if(i.Age>=i.Definition.Cuts[n])i.Systems[n].Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
     else if(delta>0)i.Systems[n].Simulate(delta,false,false,false);
    }
    for(int n=0;n<i.Renderers.Length;n++)
    {i.Block.Clear();i.Block.SetFloat("_EffectTime",i.Age);var custom=i.Systems[n].customData;
     i.Block.SetFloat("_AdapterCustomColor",custom.enabled&&custom.GetMode(ParticleSystemCustomData.Custom2)!=ParticleSystemCustomDataMode.Disabled?1:0);i.Renderers[n].SetPropertyBlock(i.Block);}
    if(i.Age+1e-6f>=i.Definition.Duration){active.RemoveAt(k);i.Definition.Pool.Release(i);Completed++;if(i.Definition.Kind=="burst")BurstCompleted++;if(i.Definition.Kind=="smoke")SmokeCompleted++;if(i.Definition.Kind=="trail")TrailCompleted++;}
   }
  }
  void OnReleased(long generation,string reason)
  {for(int k=active.Count-1;k>=0;k--)if(active[k].Generation==generation&&active[k].Definition.CancelOnLeave){var i=active[k];active.RemoveAt(k);i.Definition.Pool.Release(i);Cancelled++;if(i.Definition.Kind=="burst")BurstCancelled++;if(i.Definition.Kind=="smoke")SmokeCancelled++;if(i.Definition.Kind=="trail")TrailCancelled++;}}
  public void SetRenderingEnabled(bool value)
  {rendering=value;foreach(var i in active){i.Lines.SetVisible(value&&!hiddenKinds.Contains(i.Definition.Kind));for(int n=0;n<i.Renderers.Length;n++)ApplyVisibility(i,n,value&&!hiddenKinds.Contains(i.Definition.Kind));}}
  static void ApplyVisibility(Instance i,int n,bool visible)
  {
   i.Renderers[n].enabled=visible&&i.Visible[n];
   var lights=i.Systems[n].lights;lights.enabled=visible&&i.LightEnabled[n];
  }
  public void SetKindRenderingEnabled(string kind,bool value)
  {
   if(!definitions.Values.Any(d=>d.Kind==kind))throw new ArgumentException("Unknown particle kind");
   if(value)hiddenKinds.Remove(kind);else hiddenKinds.Add(kind);
   SetRenderingEnabled(rendering);
  }
  static void Release(Instance i)
  {i.Lines.Clear();foreach(var ps in i.Systems)ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);foreach(var r in i.Renderers)r.SetPropertyBlock(null);i.Root.SetActive(false);i.Age=0;i.Pending=false;}
  static void DestroyInstance(Instance i)=>Destroy(i.Root);
  static void Destroy(Object o){if(!o)return;if(Application.isPlaying)Object.Destroy(o);else Object.DestroyImmediate(o);}
  public void Clear(){foreach(var i in active){i.Definition.Pool.Release(i);Cancelled++;if(i.Definition.Kind=="burst")BurstCancelled++;if(i.Definition.Kind=="smoke")SmokeCancelled++;if(i.Definition.Kind=="trail")TrailCancelled++;}active.Clear();}
  public void Dispose()
  {if(disposed)return;disposed=true;events.Emitted-=Emit;events.Released-=OnReleased;Clear();foreach(var d in definitions.Values)d.Pool.Dispose();definitions.Clear();Destroy(holder);}
 }
}
