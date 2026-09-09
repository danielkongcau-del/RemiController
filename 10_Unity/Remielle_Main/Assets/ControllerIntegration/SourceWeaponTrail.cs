using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Remielle.ControllerRuntime;
using UnityEngine;

namespace Remielle.Controller
{
    // Finite first effect adapter. Original attachment/windows/assets feed the
    // MIT ribbon generator; neither native trail nor shader parity is claimed.
    public sealed class SourceWeaponTrail : IDisposable
    {
        readonly JObject[] windows;
        readonly HashSet<long> owners=new HashSet<long>();
        readonly HashSet<long> leaving=new HashSet<long>();
        readonly GameObject root,start,end;
        readonly Material material;
        readonly StickWeaponTrailEffect trail;
        readonly float closeDuration;
        float clock,closedAt;
        bool disposed,wasEmitting,resetPending;
        public int Activations { get; private set; }
        public int Releases { get; private set; }
        public int ActiveOwners=>owners.Count;
        public int Vertices=>trail.mesh.vertexCount;
        public int PeakVertices { get; private set; }
        public MeshRenderer Renderer { get; }
        public Transform StartPoint=>start.transform;
        public Transform EndPoint=>end.transform;
        public Mesh Mesh=>trail.mesh;
        public SourceWeaponTrail(string json,NativeControllerSource source,RemielleNativeAnimation driver,Material template)
        {
            var pack=JObject.Parse(json);var ctl=source.GetController("Avatar_Female_Size02_RemielleOrigin_Controller");
            if((string)pack["schema"]!="remielle-source-weapon-trail-v1"||!JToken.DeepEquals(pack["controllerIdentity"],ctl["identity"])||
                (string)pack["controllerRawSha256"]!=(string)ctl["raw"]["sha256"]||!template)throw new ArgumentException("Trail source or material missing");
            windows=pack["windows"].Cast<JObject>().ToArray();
            foreach(var w in windows)if((string)ctl["machines"][0]["states"][(int)w["state"]]["name"]!=(string)w["AnimatorStateName"])throw new ArgumentException("Trail state identity mismatch");
            var link=driver.bones.Single(b=>b.source.name==(string)pack["attachPoint"]["name"]);
            // targetWorld = mappedSourceWorld * basis. The authored effect
            // point therefore enters this target bone through inverse(basis).
            Vector3 Point(string key)=>link.basis.inverse.MultiplyPoint3x4(new Vector3((float)pack[key]["position"][0],(float)pack[key]["position"][1],(float)pack[key]["position"][2]));
            root=new GameObject((string)pack["effect"]=="Eff_RemielleOrigin_Common_24_X-Weapon"?"Remielle_Source_Common24_Trail":"Remielle_Source_"+(string)pack["effect"]);
            start=new GameObject("SourceTrail_PointStart");start.transform.SetParent(link.target,false);start.transform.localPosition=Point("start");
            end=new GameObject("SourceTrail_PointEnd");end.transform.SetParent(link.target,false);end.transform.localPosition=Point("end");
            material=new Material(template){name="Remielle_Common24_Trail_Runtime"};
            trail=root.AddComponent<StickWeaponTrailEffect>();trail.top=end.transform;trail.bottom=start.transform;
            trail.duration=(float)pack["trail"]["MaxFrame"]/(float)pack["trail"]["Fps"];trail.degreeResolution=3;
            closeDuration=(float)pack["trail"]["CloseDuration"];
            root.AddComponent<MeshFilter>().sharedMesh=trail.mesh;Renderer=root.AddComponent<MeshRenderer>();Renderer.sharedMaterial=material;
            Renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;Renderer.receiveShadows=false;Renderer.enabled=false;
        }
        public void Observe(SourceActionNotice notice)
        {
            if(disposed)throw new ObjectDisposedException(nameof(SourceWeaponTrail));
            if(notice.Phase==SourceActionPhase.Leave)leaving.Add(notice.Generation);
            if(notice.Phase==SourceActionPhase.Exit)leaving.Remove(notice.Generation);
            if(notice.Phase==SourceActionPhase.Sample&&leaving.Contains(notice.Generation))return;
            bool active=notice.Phase!=SourceActionPhase.Leave&&notice.Phase!=SourceActionPhase.Exit&&windows.Any(w=>
                (int)w["state"]==notice.State&&notice.Frame>=((bool)w["MaxFrameCountLow"]?notice.LengthFrames:(int)w["FrameCountLow"])&&
                notice.Frame<=((bool)w["MaxFrameCountHigh"]?notice.LengthFrames:(int)w["FrameCountHigh"]));
            if(active)
            {
                if(owners.Add(notice.Generation)&&owners.Count==1){resetPending=true;Activations++;}
            }
            else if(owners.Remove(notice.Generation)&&owners.Count==0){closedAt=clock;Releases++;}
        }
        public void Advance(float delta)
        {
            if(disposed)throw new ObjectDisposedException(nameof(SourceWeaponTrail));
            if(!float.IsFinite(delta)||delta<0)throw new ArgumentOutOfRangeException(nameof(delta));
            if(delta==0)return;
            clock+=delta;bool emitting=owners.Count>0;
            if(resetPending){trail.ClearTrail();resetPending=false;}
            if(emitting||wasEmitting||Vertices>0)
            {
                trail.Sample(clock,emitting);
                material.SetFloat("_EffectTime",clock);
                material.SetFloat("_Opacity",emitting?1:closeDuration>0?Mathf.Clamp01(1-(clock-closedAt)/closeDuration):0);
            }
            wasEmitting=emitting;Renderer.enabled=Vertices>0;PeakVertices=Math.Max(PeakVertices,Vertices);
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;owners.Clear();leaving.Clear();
            // Scene unloading may already have destroyed the independent
            // ribbon root before the controller owner receives OnDestroy.
            if(trail)trail.ClearTrail();if(Renderer)Renderer.enabled=false;
            Destroy(root);Destroy(start);Destroy(end);Destroy(material);
        }
        static void Destroy(UnityEngine.Object o){if(!o)return;if(Application.isPlaying)UnityEngine.Object.Destroy(o);else UnityEngine.Object.DestroyImmediate(o);}
    }
}
