using System;
using UnityEngine;

namespace Remielle.Controller
{
    // A visible training hurtbox, not an imitation of the game's enemy stats.
    public sealed class SourceTrainingTarget : MonoBehaviour
    {
        public int Hits { get; private set; }
        public string LastAttack { get; private set; }
        public bool vulnerable=true;
        [SerializeField,Min(1)] float maximumHealth=100;
        float health;
        bool initialized;
        public float Health=>initialized?health:maximumHealth;
        public float MaximumHealth=>maximumHealth;
        public bool IsAlive=>Health>0;
        public double DamageTaken { get; private set; }
        public int DamageApplications { get; private set; }
        public double StaggerTaken { get; private set; }
        Renderer display;
        MaterialPropertyBlock block;
        float flashUntil;
        public void Receive(SourceHitQuery.Receipt hit)
        {
            Hits++;LastAttack=hit.Key;flashUntil=Time.time+.18f;ApplyColor();
        }
        void Awake(){display=GetComponent<Renderer>();block=new MaterialPropertyBlock();ResetHealth(maximumHealth);}
        public void ResetHealth(float maximum)
        {
            if(!float.IsFinite(maximum)||maximum<=0)throw new ArgumentOutOfRangeException(nameof(maximum));
            maximumHealth=health=maximum;initialized=true;DamageTaken=0;DamageApplications=0;StaggerTaken=0;
            Hits=0;LastAttack=null;flashUntil=0;ApplyColor();
        }
        public float ApplyDamage(float amount)
        {
            if(!float.IsFinite(amount)||amount<0)throw new ArgumentOutOfRangeException(nameof(amount));
            if(!initialized){health=maximumHealth;initialized=true;}
            if(!IsAlive||amount==0)return 0;
            float actual=Math.Min(health,amount);health-=actual;DamageTaken+=actual;DamageApplications++;
            ApplyColor();return actual;
        }
        void Update()=>ApplyColor();
        public void AddStagger(float amount)
        {
            if(!float.IsFinite(amount)||amount<0)throw new ArgumentOutOfRangeException(nameof(amount));
            StaggerTaken+=amount;
        }
        void ApplyColor()
        {
            if(!display)return;
            display.GetPropertyBlock(block);
            block.SetColor("_Color",!IsAlive?new Color(.22f,.22f,.22f):Time.time<flashUntil?new Color(1,.5f,.08f):new Color(.2f,.65f,.85f));
            display.SetPropertyBlock(block);
        }
        public static SourceTrainingTarget Create(string name,Vector3 position)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Sphere);go.name=name;
            go.layer=SourceHitQuery.TargetLayer;go.transform.position=position;go.transform.localScale=Vector3.one*.9f;
            go.GetComponent<SphereCollider>().isTrigger=true;
            return go.AddComponent<SourceTrainingTarget>();
        }
    }
}
