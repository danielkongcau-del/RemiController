using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Remielle.Controller
{
    // Explicit preview host: authored default 0, entered fade 0->1, leaving
    // fade 1->0, then hold the endpoint. Native startup/material priorities
    // are not claimed. Only the qualified right effect blades use this shader.
    public sealed class SourceWeaponSurface : IDisposable
    {
        sealed class Target
        {
            public SourceRendererVisibility.Binding Binding;
            public Material[] Before,Runtime;
            public MaterialPropertyBlock[] Blocks;
            public bool Outline;
        }
        readonly Target[] targets;
        readonly AnimationCurve fadeIn,fadeOut;
        readonly AnimationCurve[] outline;
        readonly MaterialPropertyBlock block=new MaterialPropertyBlock();
        readonly Texture outlineTexture;
        readonly Vector4 outlineST,outlineSpeed;
        readonly float outlineUV2,defaultVisibility,duration;
        readonly bool hasOutline;
        bool disposed,active,pending,hasEdge;
        float age,clock;
        public float Visibility { get; private set; }
        public Vector4 OutlineColor { get; private set; }
        public bool IsTransitioning=>hasEdge&&(pending||age<duration);
        public int TargetCount=>targets.Length;
        public SourceWeaponSurface(string json,IEnumerable<SourceRendererVisibility.Binding> bindings,Shader shader,Texture texture)
        {
            var p=JObject.Parse(json);if((string)p["schema"]!="remielle-source-weapon-material-v1"||!shader||!texture)throw new ArgumentException("Weapon surface source or shader missing");
            var configs=p["configurations"];var actions=p["modifier"];
            var add=configs[(string)actions["OnAdded"][0]["key"]];var remove=configs[(string)actions["OnRemoved"][0]["key"]];
            hasOutline=(bool?)p["hasOutline"]??true;
            var o=hasOutline?configs[(string)actions["OnAdded"][2]["key"]]:null;
            fadeIn=SourceWeaponEmission.ReadCurve(add["curveGroup"]["curveDict"]["_DitherAlpha2:value"]);
            fadeOut=SourceWeaponEmission.ReadCurve(remove["curveGroup"]["curveDict"]["_DitherAlpha2:value"]);
            defaultVisibility=(float)add["properties"]["_DitherAlpha2Default"];
            duration=(float)add["curveGroup"]["duration"];
            if(defaultVisibility!=0||(float)remove["properties"]["_DitherAlpha2Default"]!=0||duration!=(float)remove["curveGroup"]["duration"]||(hasOutline&&duration!=(float)o["curveGroup"]["duration"]))
                throw new ArgumentException("Unsupported weapon surface lifecycle");
            outlineTexture=texture;
            if(hasOutline)
            {
            outline=new[]{"r","g","b","a"}.Select(c=>SourceWeaponEmission.ReadCurve(o["curveGroup"]["curveDict"]["_OverrideOutlineColor:value:"+c])).ToArray();
            var props=o["properties"];var tex=props["_OverrideOutlineTex"];
            outlineST=new Vector4((float)tex["tillingValue"][0],(float)tex["tillingValue"][1],(float)tex["offsetValue"][0],(float)tex["offsetValue"][1]);
            var speed=props["_OverrideOutlineSpeed"]["value"];outlineSpeed=new Vector4((float)speed[0],(float)speed[1],(float)speed[2],(float)speed[3]);
            outlineUV2=(bool)props["_OverrideOutlineUseUV2"]["value"]?1:0;
            }
            string Key(JToken t)=>SourceRendererVisibility.Key((string)t["sourceBlock"],(string)t["cab"],(string)t["rendererPathID"]);
            var outlineIds=new HashSet<string>(p["emissionTargets"].Select(Key));var byId=bindings.ToDictionary(b=>b.Identity);
            targets=p["ditherTargets"].Select(t=>new Target{Binding=byId[Key(t)],Outline=outlineIds.Contains(Key(t))}).ToArray();
            if(targets.Length!=(hasOutline?3:1))throw new ArgumentException("Unexpected qualified weapon family size");
            // Validate the whole set before any material assignment.
            foreach(var t in targets)
            {
                var r=t.Binding.renderer;if(!r||r.sharedMesh!=t.Binding.importedMesh)throw new ArgumentException("Weapon surface mesh changed");
                r.GetPropertyBlock(block);if(!block.isEmpty)throw new ArgumentException("Competing renderer-wide weapon properties");
                t.Before=r.sharedMaterials;
                if(t.Before.Any(m=>!m||m.shader.name!="HoyoToon/Zenless Zone Zero/Character"))throw new ArgumentException("Unexpected base weapon shader");
                t.Blocks=t.Before.Select((_,i)=>{var b=new MaterialPropertyBlock();r.GetPropertyBlock(b,i);return b;}).ToArray();
            }
            try
            {
                foreach(var t in targets)
                {
                    t.Runtime=t.Before.Select(m=>new Material(m){shader=shader,name=m.name+" (Source Weapon Surface)"}).ToArray();
                    t.Binding.renderer.sharedMaterials=t.Runtime;
                }
                Visibility=defaultVisibility;Write();
            }
            catch{Dispose();throw;}
        }
        public void SetActive(bool value)
        {
            Check();if(hasEdge&&active==value)return;
            active=value;hasEdge=true;pending=true;age=0;
        }
        public void Advance(float delta)
        {
            Check();if(!float.IsFinite(delta)||delta<0)throw new ArgumentOutOfRangeException(nameof(delta));if(delta==0)return;
            clock+=delta;
            if(hasEdge)
            {
                if(pending)pending=false;else age+=delta;
                Visibility=(active?fadeIn:fadeOut).Evaluate(Math.Min(age,duration));
                OutlineColor=hasOutline&&age<duration?new Vector4(outline[0].Evaluate(age),outline[1].Evaluate(age),outline[2].Evaluate(age),outline[3].Evaluate(age)):Vector4.zero;
            }
            Write();
        }
        void Write()
        {
            foreach(var t in targets)
            {
                var r=t.Binding.renderer;if(!r||r.sharedMesh!=t.Binding.importedMesh)throw new InvalidOperationException("Weapon surface renderer changed");
                if(!r.sharedMaterials.SequenceEqual(t.Runtime))throw new InvalidOperationException("Another owner replaced the weapon surface materials");
                for(int i=0;i<t.Runtime.Length;i++)
                {
                    // A profile may update its own material later (LUT/tints).
                    // Follow its live values while retaining our private shader.
                    if(t.Before[i])t.Runtime[i].CopyPropertiesFromMaterial(t.Before[i]);
                    r.GetPropertyBlock(block,i);block.SetFloat("_SourceWeaponVisibility",Visibility);
                    block.SetVector("_SourceWeaponOutlineColor",t.Outline?OutlineColor:Vector4.zero);
                    block.SetTexture("_SourceWeaponOutlineTex",outlineTexture);block.SetVector("_SourceWeaponOutlineTex_ST",outlineST);
                    block.SetFloat("_SourceWeaponOutlineUseUV2",outlineUV2);block.SetVector("_SourceWeaponOutlineSpeed",outlineSpeed);block.SetFloat("_SourceWeaponTime",clock);
                    r.SetPropertyBlock(block,i);
                }
            }
        }
        void Check(){if(disposed)throw new ObjectDisposedException(nameof(SourceWeaponSurface));}
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            foreach(var t in targets)
            {
                if(t.Runtime==null)continue;var r=t.Binding.renderer;
                // Another owner may already have restored materials during
                // scene teardown; never reinstall destroyed profile instances.
                if(r&&r.sharedMaterials.SequenceEqual(t.Runtime))
                {
                    r.sharedMaterials=t.Before;
                    for(int i=0;i<t.Blocks.Length;i++)r.SetPropertyBlock(t.Blocks[i].isEmpty?null:t.Blocks[i],i);
                }
                foreach(var m in t.Runtime)if(m){if(Application.isPlaying)UnityEngine.Object.Destroy(m);else UnityEngine.Object.DestroyImmediate(m);}
            }
        }
    }
}
