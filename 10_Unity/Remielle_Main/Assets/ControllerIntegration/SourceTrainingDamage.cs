using System;

namespace Remielle.Controller
{
    // User-selected training rule: one damage and one stagger per accepted hit.
    // No skill tables, character stats or native damage formula are required.
    public sealed class SourceTrainingDamage : IDisposable
    {
        public const float DamagePerHit=1,StaggerPerHit=1;
        readonly SourceHitQuery query;
        bool disposed;
        public int Applications { get; private set; }
        public double TotalApplied { get; private set; }
        public double TotalStagger { get; private set; }
        public SourceTrainingTarget LastTarget { get; private set; }
        public SourceTrainingDamage(SourceHitQuery query)
        {this.query=query??throw new ArgumentNullException(nameof(query));query.Hit+=OnHit;}
        void OnHit(SourceHitQuery.Receipt hit)
        {
            TotalApplied+=hit.Target.ApplyDamage(DamagePerHit);
            hit.Target.AddStagger(StaggerPerHit);TotalStagger+=StaggerPerHit;
            Applications++;LastTarget=hit.Target;
        }
        public void Dispose(){if(disposed)return;query.Hit-=OnHit;LastTarget=null;disposed=true;}
    }
}
