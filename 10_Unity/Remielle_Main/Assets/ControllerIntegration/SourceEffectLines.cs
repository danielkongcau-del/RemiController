using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace Remielle.Controller
{
 // Source-authored line windows on the same actor clock as particles.
 public sealed class SourceEffectLines
 {
  readonly LineRenderer[] lines;readonly bool[] emits;readonly float[] starts,cuts;
  readonly MaterialPropertyBlock block=new();bool visible=true;float age;
  public SourceEffectLines(Transform root,JArray nodes)
  {
   nodes??=new JArray();lines=new LineRenderer[nodes.Count];emits=new bool[nodes.Count];starts=new float[nodes.Count];cuts=new float[nodes.Count];
   for(int i=0;i<nodes.Count;i++)
   {
    var row=nodes[i];var t=root;
    foreach(var index in row["siblingPath"])t=t.GetChild((int)index);
    lines[i]=t.GetComponent<LineRenderer>();if(!lines[i])throw new Exception("Missing source line renderer");
    emits[i]=(bool)row["emits"];starts[i]=(float)row["startTime"];cuts[i]=(float)row["cutTime"];
    if(starts[i]<0||cuts[i]<starts[i])throw new Exception("Invalid source line window");
   }
   if(lines.Distinct().Count()!=lines.Length||root.GetComponentsInChildren<LineRenderer>(true).Length!=lines.Length)throw new Exception("Source line identity coverage differs");
  }
  public void Advance(float time)
  {
   age=time;
   for(int i=0;i<lines.Length;i++)
   {
    lines[i].enabled=visible&&emits[i]&&age>=starts[i]&&age<cuts[i];
    lines[i].GetPropertyBlock(block);block.SetFloat("_EffectTime",time);block.SetFloat("_AdapterCustomColor",0);lines[i].SetPropertyBlock(block);block.Clear();
   }
  }
  public void SetVisible(bool value){visible=value;Advance(age);}
  public void Clear(){foreach(var line in lines){line.enabled=false;line.SetPropertyBlock(null);}age=0;}
 }
}
