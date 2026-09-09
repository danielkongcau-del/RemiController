using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Remielle.ControllerRuntime;
using UnityEngine;

namespace Remielle.Controller
{
    // Finite HoyoToon adapter for the original right-weapon emission pulse.
    // Native dither/outline and the general material-priority scheduler are
    // deliberately not claimed by this consumer. It owns only these slots'
    // property blocks while a pulse is active; shared materials stay untouched.
    public sealed class SourceWeaponEmission : IDisposable
    {
        sealed class Slot
        {
            public SourceRendererVisibility.Binding Binding;
            public int Index;
            public MaterialPropertyBlock Before=new MaterialPropertyBlock(), Work=new MaterialPropertyBlock();
        }
        readonly Slot[] slots;
        readonly JObject[] windows;
        readonly AnimationCurve[] colors;
        readonly Texture texture;
        readonly Vector4 textureST,speed;
        readonly float uv2,channel,maskChannel;
        readonly HashSet<long> owners=new HashSet<long>(),leaving=new HashSet<long>();
        bool disposed,pending,captured;
        float age;
        public float Duration { get; }
        public float Age=>age;
        public bool IsPulsing { get; private set; }
        public int ActiveOwners=>owners.Count;
        public int Activations { get; private set; }
        public int Releases { get; private set; }
        public int Pulses { get; private set; }
        public Vector4 CurrentColor { get; private set; }
        public float PeakColor { get; private set; }
        public event Action<bool> ModifierChanged;
        public SourceWeaponEmission(string json,NativeControllerSource source,IEnumerable<SourceRendererVisibility.Binding> bindings,Texture emissionTexture)
        {
            var p=JObject.Parse(json);var ctl=source.GetController("Avatar_Female_Size02_RemielleOrigin_Controller");
            if((string)p["schema"]!="remielle-source-weapon-material-v1"||!JToken.DeepEquals(p["controllerIdentity"],ctl["identity"])||
                (string)p["controllerRawSha256"]!=(string)ctl["raw"]["sha256"]||!emissionTexture)throw new ArgumentException("Emission source identity/texture missing");
            windows=p["windows"].Cast<JObject>().ToArray();
            foreach(var w in windows)if((string)ctl["machines"][0]["states"][(int)w["state"]]["name"]!=(string)w["AnimatorStateName"])throw new ArgumentException("Emission state identity mismatch");
            var config=p["configurations"][(string)p["emissionKey"]];var props=config["properties"];var group=config["curveGroup"];
            if((float)props["EnterDuration"]!=0||(float)props["ExitDuration"]!=0||(bool)group["isLoop"]||
                (float)props["KeepDuration"]!=(float)group["duration"]||(bool)props["_SecondaryEmissionMultiplyAlbedo"]["value"]||
                (string)props["_SecondaryEmissionMaskTex"]["texPath"]!="")throw new ArgumentException("Unsupported emission configuration");
            Duration=(float)group["duration"];texture=emissionTexture;
            colors=new[]{"r","g","b","a"}.Select(c=>ReadCurve(group["curveDict"]["_SecondaryEmissionColor:value:"+c])).ToArray();
            var tex=props["_SecondaryEmissionTex"];
            textureST=new Vector4((float)tex["tillingValue"][0],(float)tex["tillingValue"][1],(float)tex["offsetValue"][0],(float)tex["offsetValue"][1]);
            var v=props["_SecondaryEmissionTexSpeed"]["value"];speed=new Vector4((float)v[0],(float)v[1],(float)v[2],(float)v[3]);
            uv2=(bool)props["_SecondaryEmissionUseUV2"]["value"]?1:0;channel=(float)props["_SecondaryEmissionChannel"]["value"];maskChannel=(float)props["_SecondaryEmissionMaskChannel"]["value"];
            var byId=bindings.ToDictionary(b=>b.Identity);var list=new List<Slot>();
            foreach(var target in p["emissionTargets"])
            {
                var b=byId[SourceRendererVisibility.Key((string)target["sourceBlock"],(string)target["cab"],(string)target["rendererPathID"])];
                if(!b.renderer||b.renderer.sharedMesh!=b.importedMesh)throw new ArgumentException("Emission renderer no longer matches its qualified mesh");
                var mats=b.renderer.sharedMaterials;
                for(int i=0;i<mats.Length;i++)
                {
                    if(!mats[i]||!mats[i].HasProperty("_SecondaryEmissionColor")||!mats[i].HasProperty("_MultiplyAlbedo"))
                        throw new ArgumentException("Emission adapter requires HoyoToon secondary emission");
                    list.Add(new Slot{Binding=b,Index=i});
                }
            }
            slots=list.ToArray();if(slots.Length==0)throw new ArgumentException("No emission slots");
        }
        public static AnimationCurve ReadCurve(JToken value)
        {
            var curve=new AnimationCurve(value[0][0].Select(k=>new Keyframe((float)k["m_Time"],(float)k["m_Value"],
                (float)k["m_InTangent"],(float)k["m_OutTangent"],(float)k["m_InWeight"],(float)k["m_OutWeight"]){weightedMode=(WeightedMode)(int)k["m_WeightedMode"]}).ToArray());
            curve.preWrapMode=(WrapMode)(int)value[1];curve.postWrapMode=(WrapMode)(int)value[2];return curve;
        }
        public void Observe(SourceActionNotice n)
        {
            Check();
            if(n.Phase==SourceActionPhase.Leave)leaving.Add(n.Generation);
            if(n.Phase==SourceActionPhase.Exit)leaving.Remove(n.Generation);
            if(n.Phase==SourceActionPhase.Sample&&leaving.Contains(n.Generation))return;
            bool active=n.Phase!=SourceActionPhase.Leave&&n.Phase!=SourceActionPhase.Exit&&windows.Any(w=>(int)w["state"]==n.State&&
                n.Frame>=((bool)w["MaxFrameCountLow"]?n.LengthFrames:(int)w["FrameCountLow"])&&n.Frame<=((bool)w["MaxFrameCountHigh"]?n.LengthFrames:(int)w["FrameCountHigh"]));
            if(active){if(owners.Add(n.Generation)&&owners.Count==1){Activations++;Pulse();ModifierChanged?.Invoke(true);}}
            else if(owners.Remove(n.Generation)&&owners.Count==0){Releases++;if(n.Reason!="owner-disposed"){Pulse();ModifierChanged?.Invoke(false);}}
        }
        void Pulse(){age=0;pending=true;IsPulsing=true;Pulses++;}
        public void Advance(float delta)
        {
            Check();if(!float.IsFinite(delta)||delta<0)throw new ArgumentOutOfRangeException(nameof(delta));
            if(delta==0||!IsPulsing)return;
            // Events arrive at this host frame boundary. Do not charge the
            // preceding frame delta to a pulse which has just been created.
            if(pending)pending=false;else age+=delta;
            if(age>=Duration){Restore();IsPulsing=false;CurrentColor=Vector4.zero;return;}
            CurrentColor=new Vector4(colors[0].Evaluate(age),colors[1].Evaluate(age),colors[2].Evaluate(age),colors[3].Evaluate(age));
            PeakColor=Math.Max(PeakColor,Math.Max(CurrentColor.x,Math.Max(CurrentColor.y,CurrentColor.z)));
            if(!captured)
            {
                // Validate every slot before the first write. Per-material
                // blocks override renderer-wide blocks, so reject competing
                // owners rather than accidentally hiding their properties.
                foreach(var s in slots)
                {
                    if(!s.Binding.renderer||s.Binding.renderer.sharedMesh!=s.Binding.importedMesh)throw new InvalidOperationException("Emission renderer replaced");
                    s.Binding.renderer.GetPropertyBlock(s.Work);
                    if(!s.Work.isEmpty)throw new InvalidOperationException("Emission renderer has a competing renderer-wide property block");
                    s.Binding.renderer.GetPropertyBlock(s.Before,s.Index);
                }
                captured=true;
            }
            foreach(var s in slots)
            {
                var r=s.Binding.renderer;if(!r)throw new InvalidOperationException("Emission renderer destroyed during pulse");
                r.GetPropertyBlock(s.Work,s.Index);
                s.Work.SetFloat("_SecondaryEmission",1);s.Work.SetFloat("_SecondaryEmissionUseUV2",uv2);
                // In this shader _MultiplyAlbedo is consumed only by
                // secondary_emission; it maps the source's longer property.
                s.Work.SetFloat("_MultiplyAlbedo",0);
                s.Work.SetFloat("_SecondaryEmissionChannel",channel);s.Work.SetFloat("_SecondaryEmissionMaskChannel",maskChannel);
                s.Work.SetTexture("_SecondaryEmissionTex",texture);s.Work.SetVector("_SecondaryEmissionTex_ST",textureST);
                s.Work.SetTexture("_SecondaryEmissionMaskTex",Texture2D.whiteTexture);s.Work.SetVector("_SecondaryEmissionMaskTex_ST",new Vector4(1,1,0,0));
                s.Work.SetVector("_SecondaryEmissionTexSpeed",speed);s.Work.SetFloat("_SecondaryEmissionTexRotation",0);
                // Odin stores the authored float components; avoid implicit
                // SetColor sRGB conversion changing these shader values.
                s.Work.SetVector("_SecondaryEmissionColor",CurrentColor);r.SetPropertyBlock(s.Work,s.Index);
            }
        }
        void Restore()
        {
            if(!captured)return;
            foreach(var s in slots)if(s.Binding.renderer)s.Binding.renderer.SetPropertyBlock(s.Before.isEmpty?null:s.Before,s.Index);
            captured=false;
        }
        void Check(){if(disposed)throw new ObjectDisposedException(nameof(SourceWeaponEmission));}
        public void Dispose(){if(disposed)return;Restore();disposed=true;owners.Clear();leaving.Clear();IsPulsing=false;CurrentColor=Vector4.zero;}
    }
}
