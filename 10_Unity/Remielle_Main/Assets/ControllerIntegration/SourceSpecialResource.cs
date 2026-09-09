using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Remielle.ControllerRuntime;

namespace Remielle.Controller
{
    // Finite CurSP producer for the recovered SpecialSkill property rule.
    // Selection stays in the original controller; only source events spend SP.
    public sealed class SourceSpecialResource : IDisposable
    {
        readonly NativeParameterBank parameters;
        readonly SourceFrameEvents events;
        readonly uint branch;
        readonly float threshold,cost;
        readonly int costState,costIndex;
        readonly int normalState,normalIndex;
        readonly double normalFrame;
        readonly string normalKey;
        readonly HashSet<long> countedNormal=new HashSet<long>();
        bool disposed;
        public float Energy { get; private set; }
        public int BranchIndex=>Energy>=threshold?1:0;
        public int Charges { get; private set; }
        public float TotalSpent { get; private set; }
        public int NormalSpecialsPerCharge { get; }
        public int NormalSpecialsTowardCharge { get; private set; }
        public int NormalSpecialUses { get; private set; }
        public int Recharges { get; private set; }
        public float TotalRestored { get; private set; }
        public SourceSpecialResource(string json,NativeParameterBank parameters,SourceFrameEvents events,int normalSpecialsPerCharge=2)
        {
            var p=JObject.Parse(json);if((string)p["schema"]!="remielle-special-resource-v1")throw new ArgumentException("Wrong special resource pack");
            this.parameters=parameters;this.events=events;branch=parameters.Hash("Int_BranchIndex");threshold=(float)p["threshold"];
            var row=p["costs"][0];costState=(int)row["state"];costIndex=(int)row["entry"]["index"];cost=(float)row["entry"]["fields"]["Amount"];
            if(normalSpecialsPerCharge<1)throw new ArgumentOutOfRangeException(nameof(normalSpecialsPerCharge));
            NormalSpecialsPerCharge=normalSpecialsPerCharge;
            var normal=p["normalSpecialActivation"];normalState=(int)normal["state"];normalIndex=(int)normal["entry"]["index"];
            normalFrame=(double)normal["entry"]["frame"];normalKey=(string)normal["entry"]["fields"]["AnimEventID"];
            if(threshold<=0||cost<=0||(float)row["entry"]["fields"]["Percentage"]!=0)throw new ArgumentException("Unsupported SP resource rule");
            // Explicit non-StreamingGame host context: DefaultModifier starts
            // its cooldown at zero, then replaces CurSP below 60 with 60.
            SetEnergy((float)p["startupGrant"]["Amount"]);
            events.Emitted+=OnEvent;events.Released+=OnRelease;
        }
        public void SetEnergy(float value)
        {
            if(disposed)throw new ObjectDisposedException(nameof(SourceSpecialResource));
            if(!float.IsFinite(value)||value<0)throw new ArgumentOutOfRangeException(nameof(value));
            Energy=value;parameters.SetInt(branch,BranchIndex);
        }
        void OnEvent(SourceFrameEvents.Emission e)
        {
            if(e.Type=="AnimatorEventAnimEventHandlerEntry"&&e.State==normalState&&e.Index==normalIndex)
            {
                if(e.AuthoredFrame!=normalFrame||(string)e.Fields["AnimEventID"]!=normalKey)
                    throw new InvalidOperationException("Normal special activation identity changed");
                if(!countedNormal.Add(e.Generation))return;
                NormalSpecialUses++;
                // Casting drives this project rule, independently of target count.
                // A full bank keeps one charge and does not bank extra progress.
                if(Energy>=threshold)return;
                NormalSpecialsTowardCharge++;
                if(NormalSpecialsTowardCharge>=NormalSpecialsPerCharge)
                {
                    TotalRestored+=threshold-Energy;SetEnergy(threshold);
                    NormalSpecialsTowardCharge=0;Recharges++;
                }
                return;
            }
            if(e.Type!="AnimatorEventDecreaseSpEntry")return;
            if(e.State!=costState||e.Index!=costIndex||(float)e.Fields["Amount"]!=cost||(float)e.Fields["Percentage"]!=0)
                throw new InvalidOperationException("Unqualified resource cost event");
            float spent=Math.Min(Energy,cost);SetEnergy(Energy-spent);TotalSpent+=spent;Charges++;
        }
        void OnRelease(long id,string reason)=>countedNormal.Remove(id);
        public void Dispose(){if(disposed)return;events.Emitted-=OnEvent;events.Released-=OnRelease;countedNormal.Clear();disposed=true;}
    }
}
