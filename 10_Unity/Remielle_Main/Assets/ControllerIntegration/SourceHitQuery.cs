using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Remielle.Controller
{
    // Source-authored instantaneous shapes, practical Unity training targets.
    // Skill-table damage/SP resolution is intentionally a separate consumer.
    public sealed class SourceHitQuery : IDisposable
    {
        public const int TargetLayer=30;
        public sealed class Receipt
        {
            public long Generation,Cycle;
            public int State,Entry;
            public double Frame;
            public string Key;
            public JObject Config;
            public SourceTrainingTarget Target;
            public Vector3 HurtboxCenter;
            public Quaternion AttackerRotation;
        }
        public sealed class Result
        {
            public long Generation,Cycle;
            public int State,Entry,TargetCount;
            public string Key;
            public JObject Config;
        }
        readonly Dictionary<(int,int),JObject> attacks=new Dictionary<(int,int),JObject>();
        readonly SourceFrameEvents events;
        readonly Transform actor;
        readonly HashSet<SourceTrainingTarget> seen=new HashSet<SourceTrainingTarget>();
        Collider[] overlaps=new Collider[16];
        RaycastHit[] blockers=new RaycastHit[16];
        bool disposed;
        public int Queries { get; private set; }
        public int Hits { get; private set; }
        public int Misses { get; private set; }
        public int UnsupportedEvents { get; private set; }
        public int WallRejected { get; private set; }
        public int BufferCapacity=>overlaps.Length;
        public string LastAttack { get; private set; }
        public event Action<Receipt> Hit;
        public event Action<Result> Resolved;
        public SourceHitQuery(string json,SourceFrameEvents events,Transform actor)
        {
            var p=JObject.Parse(json);if((string)p["schema"]!="remielle-hit-query-v1")throw new ArgumentException("Wrong hit pack");
            foreach(JObject r in p["attacks"])attacks.Add(((int)r["state"],(int)r["entry"]),r);
            this.actor=actor;this.events=events;events.Emitted+=OnEvent;
        }
        public static Vector3 Offset(JToken p)=>new Vector3((float)p["CenterXOffset"],(float)p["CenterYOffset"],(float)p["CenterZOffset"]);
        // Exact closest-point test for a sphere and a finite closed cylinder,
        // or a box extending forward from its authored origin. No AABB-only hits.
        public static bool Intersects(JObject row,Vector3 localSphereCenter,float sphereRadius)
        {
            var p=row["config"]["AttackPattern"];Vector3 q=localSphereCenter-Offset(p);
            if((string)row["shape"]=="cylinder")
            {
                float radial=Mathf.Max(0,new Vector2(q.x,q.z).magnitude-(float)p["Radius"]);
                float vertical=Mathf.Max(0,Mathf.Abs(q.y)-(float)p["Height"]*.5f);
                return radial*radial+vertical*vertical<=sphereRadius*sphereRadius;
            }
            if((string)row["shape"]!="box")throw new NotSupportedException("Unsupported hit shape");
            float x=Mathf.Max(0,Mathf.Abs(q.x)-(float)p["width"]*.5f);
            float y=Mathf.Max(0,Mathf.Abs(q.y)-(float)p["height"]*.5f);
            float z=Mathf.Max(0,Mathf.Max(-q.z,q.z-(float)p["distance"]));
            return x*x+y*y+z*z<=sphereRadius*sphereRadius;
        }
        void OnEvent(SourceFrameEvents.Emission e)
        {
            if(e.Type!="AnimatorEventAnimEventHandlerEntry")return;
            if(!attacks.TryGetValue((e.State,e.Index),out var row)){UnsupportedEvents++;return;}
            if((string)e.Fields["AnimEventID"]!=(string)row["key"]||e.AuthoredFrame!=(double)row["frame"])
                throw new InvalidOperationException("Hit event identity changed");
            if((actor.lossyScale-Vector3.one).sqrMagnitude>1e-8f)throw new NotSupportedException("Hit actor requires unit world scale");
            var p=row["config"]["AttackPattern"];var offset=Offset(p);Vector3 half;
            if((string)row["shape"]=="cylinder")half=new Vector3((float)p["Radius"],(float)p["Height"]*.5f,(float)p["Radius"]);
            else{half=new Vector3((float)p["width"],(float)p["height"],(float)p["distance"])*.5f;offset.z+=half.z;}
            Vector3 center=actor.position+actor.rotation*offset;
            // Input/motor/fixture transforms may have moved since FixedUpdate.
            Physics.SyncTransforms();int count;
            while(true)
            {
                count=Physics.OverlapBoxNonAlloc(center,half,overlaps,actor.rotation,1<<TargetLayer,QueryTriggerInteraction.Collide);
                if(count<overlaps.Length)break;
                Array.Resize(ref overlaps,checked(overlaps.Length*2));
            }
            Queries++;LastAttack=(string)row["key"];seen.Clear();int accepted=0;
            for(int i=0;i<count;i++)
            {
                var collider=overlaps[i];overlaps[i]=null;
                var target=collider.GetComponentInParent<SourceTrainingTarget>();
                if(!target||!target.isActiveAndEnabled||!target.vulnerable||!target.IsAlive||seen.Contains(target)||target.gameObject==actor.gameObject)continue;
                if(!(collider is SphereCollider sphere))throw new NotSupportedException("Training hurtbox must be a SphereCollider");
                Vector3 world=sphere.transform.TransformPoint(sphere.center),scale=sphere.transform.lossyScale;
                float radius=sphere.radius*Mathf.Max(Mathf.Abs(scale.x),Mathf.Max(Mathf.Abs(scale.y),Mathf.Abs(scale.z)));
                if(!Intersects(row,Quaternion.Inverse(actor.rotation)*(world-actor.position),radius))continue;
                if(!(bool)row["config"]["AttackProperty"]["IsIgnoreWallCheck"]&&
                    WallBlocks(actor.position+actor.rotation*Offset(p),world))
                {WallRejected++;continue;}
                seen.Add(target);accepted++;Hits++;
                var receipt=new Receipt{Generation=e.Generation,State=e.State,Entry=e.Index,Cycle=e.Cycle,Frame=e.AuthoredFrame,
                    Key=LastAttack,Config=(JObject)row["config"],Target=target,HurtboxCenter=world,AttackerRotation=actor.rotation};
                target.Receive(receipt);Hit?.Invoke(receipt);
            }
            if(accepted==0)Misses++;
            seen.Clear();
            Resolved?.Invoke(new Result{Generation=e.Generation,Cycle=e.Cycle,State=e.State,Entry=e.Index,
                TargetCount=accepted,Key=LastAttack,Config=(JObject)row["config"]});
        }
        bool WallBlocks(Vector3 origin,Vector3 target)
        {
            Vector3 delta=target-origin;float distance=delta.magnitude;if(distance<1e-6f)return false;
            int count;
            while(true)
            {
                count=Physics.RaycastNonAlloc(origin,delta/distance,blockers,distance,1,QueryTriggerInteraction.Ignore);
                if(count<blockers.Length)break;
                Array.Resize(ref blockers,checked(blockers.Length*2));
            }
            // A box may start behind the attacker. The actor's collision
            // capsule and any attached physical parts are never a wall.
            for(int i=0;i<count;i++)if(!blockers[i].collider.transform.IsChildOf(actor))return true;
            return false;
        }
        public void Dispose(){if(disposed)return;events.Emitted-=OnEvent;Hit=null;Resolved=null;seen.Clear();Array.Clear(overlaps,0,overlaps.Length);Array.Clear(blockers,0,blockers.Length);disposed=true;}
    }
}
