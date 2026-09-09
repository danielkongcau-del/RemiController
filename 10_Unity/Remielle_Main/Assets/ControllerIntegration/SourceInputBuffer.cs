using System;
using Remielle.ControllerRuntime;

namespace Remielle.Controller
{
    // Three independent pending intents. Original controller conditions and
    // source ordering still decide which one can commit. Duration is a project
    // setting, measured in actor time so hitstop does not eat a buffered press.
    public sealed class SourceInputBuffer : IDisposable
    {
        sealed class Slot
        {
            public uint Hash;
            public bool Pending;
            public double Expires;
        }
        readonly NativeParameterBank parameters;
        readonly Slot[] slots;
        bool disposed;
        public double Clock { get; private set; }
        public double Duration { get; }
        public int Presses { get; private set; }
        public int Refreshed { get; private set; }
        public int Consumed { get; private set; }
        public int Expired { get; private set; }
        public int Cleared { get; private set; }
        public int PendingCount { get {int count=0;foreach(var slot in slots)if(slot.Pending)count++;return count;} }
        public SourceInputBuffer(NativeParameterBank parameters,float duration)
        {
            if(!float.IsFinite(duration)||duration<0)throw new ArgumentOutOfRangeException(nameof(duration));
            this.parameters=parameters??throw new ArgumentNullException(nameof(parameters));Duration=duration;
            slots=new[]{new Slot{Hash=parameters.Hash("Trigger_PressEvade")},
                new Slot{Hash=parameters.Hash("Trigger_PressAttackA")},new Slot{Hash=parameters.Hash("Trigger_PressAttackB")}};
            foreach(var slot in slots)if(parameters.Kind(slot.Hash)!=9)throw new ArgumentException("Buffered input must be a source trigger");
        }
        public void Advance(float actorDelta)
        {
            if(disposed)throw new ObjectDisposedException(nameof(SourceInputBuffer));
            if(!float.IsFinite(actorDelta)||actorDelta<0)throw new ArgumentOutOfRangeException(nameof(actorDelta));
            Clock+=actorDelta;
            foreach(var slot in slots)
            {
                if(!slot.Pending)continue;
                if(!parameters.GetTrigger(slot.Hash)){slot.Pending=false;Consumed++;}
                else if(Clock>slot.Expires+1e-7){parameters.ResetTrigger(slot.Hash);slot.Pending=false;Expired++;}
            }
        }
        public void Press(uint hash)
        {
            if(disposed)throw new ObjectDisposedException(nameof(SourceInputBuffer));
            foreach(var slot in slots)if(slot.Hash==hash)
            {
                if(slot.Pending)Refreshed++;
                slot.Pending=true;slot.Expires=Clock+Duration;parameters.SetTrigger(hash);Presses++;return;
            }
            throw new ArgumentException("Input buffer does not own this trigger",nameof(hash));
        }
        // The session resets only triggers used by an accepted source transition.
        // Reconcile immediately, so a later tick can never republish a used press.
        public void AfterCommit()
        {
            if(disposed)throw new ObjectDisposedException(nameof(SourceInputBuffer));
            foreach(var slot in slots)if(slot.Pending&&!parameters.GetTrigger(slot.Hash)){slot.Pending=false;Consumed++;}
        }
        public void Clear()
        {
            foreach(var slot in slots)
            {
                if(slot.Pending){parameters.ResetTrigger(slot.Hash);slot.Pending=false;Cleared++;}
            }
        }
        public void Dispose(){if(disposed)return;Clear();disposed=true;}
    }
}
