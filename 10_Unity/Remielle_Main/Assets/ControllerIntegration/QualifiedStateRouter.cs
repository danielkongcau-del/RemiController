using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Remielle.ControllerRuntime;
using UnityHFSM;

namespace Remielle.Controller
{
    // Original candidate-list planning feeds HFSM explicit lifecycle commits.
    public sealed class QualifiedStateRouter
    {
        public sealed class Decision
        {
            internal QualifiedStateRouter Owner;
            internal int Revision;
            internal bool Consumed;
            public string BlockedReason { get; internal set; }
            internal bool SealedAccepted;
            public bool Accepted => BlockedReason == null && SealedAccepted;
            public string SourceList { get; internal set; }
            public NativeTransitionSearchResult Result { get; internal set; }
            public NativeLayerTransitionState Transition { get; internal set; }
            public NativeTransitionStartCommand StartCommand { get; internal set; }
            internal NativeParameterBank Parameters;
            internal string ParameterSnapshot;
            internal int SealedTarget;
        }

        readonly NativeControllerSource source;
        readonly string controller;
        readonly int machine;
        readonly JObject definition;
        readonly JArray parameterDefinitions;
        readonly StateMachine<int> lifecycle = new StateMachine<int>();
        readonly Dictionary<int, NativeTransitionSearch> searches = new Dictionary<int, NativeTransitionSearch>();
        int revision;
        public int CurrentState => lifecycle.ActiveStateName;
        public int EnterCount { get; private set; }
        public event Action<int> Entered;

        public QualifiedStateRouter(NativeControllerSource source, string controller, int machine, int initialState)
        {
            this.source = source ?? throw new ArgumentNullException(nameof(source));
            this.controller = controller; this.machine = machine;
            definition = (JObject)source.GetController(controller)["machines"][machine];
            parameterDefinitions = (JArray)source.GetController(controller)["parameters"];
            var states = (JArray)definition["states"];
            if (initialState < 0 || initialState >= states.Count) throw new ArgumentOutOfRangeException(nameof(initialState));
            for (int i = 0; i < states.Count; i++)
            {
                int stateId = i;
                lifecycle.AddState(i, new State<int>(onEnter: _ => { EnterCount++; Entered?.Invoke(stateId); }));
                searches.Add(i, source.CreateTransitionSearch(controller, machine, i));
            }
            searches.Add(-1, source.CreateTransitionSearch(controller, machine, -1));
            lifecycle.SetStartState(initialState);
            lifecycle.Init();
        }

        string Snapshot(NativeParameterBank parameters)
        {
            var result = new JArray();
            foreach (var p in parameterDefinitions)
            {
                uint hash = (uint)p["hash"]; int kind = (int)p["kind"];
                JToken value = kind == 1 ? new JValue(parameters.GetFloat(hash)) :
                    kind == 3 ? new JValue(parameters.GetInt(hash)) :
                    kind == 4 ? new JValue(parameters.GetBool(hash)) : new JValue(parameters.GetTrigger(hash));
                result.Add(new JArray(hash, kind, value));
            }
            return result.ToString(Newtonsoft.Json.Formatting.None);
        }

        public Decision Preview(NativeParameterBank parameters, float previousNormalized,
            float currentNormalized, float sourceDuration, float direction, bool loops,
            NativeTransitionTimingPolicy timing, bool blending = false, bool callbackContextQualified = false)
        {
            if (blending) return new Decision { BlockedReason = "An active transition record is required; use PreviewLayer" };
            var state = new NativeLayerTransitionState { CurrentState = (uint)CurrentState,
                TransitionIndex = -1, TransitionSourceState = -1,
                SourceDuration = sourceDuration, CurrentNormalizedTime = currentNormalized, CurrentSpeedMultiplier = direction };
            return PreviewLayer(parameters,state,previousNormalized,0,loops,false,timing,callbackContextQualified);
        }

        public Decision PreviewLayer(NativeParameterBank parameters, NativeLayerTransitionState live,
            float previousNormalized,float delta,bool currentLoops,bool nextLoops,
            NativeTransitionTimingPolicy timing,bool callbackContextQualified=false)
        {
            var decision = new Decision { Owner = this, Revision = revision, Parameters = parameters };
            if (!callbackContextQualified) { decision.BlockedReason = "G4: external callback context is not qualified"; return decision; }
            if (live.InterruptionActive) { decision.BlockedReason = "Native graph interruption is still active"; return decision; }
            if (!(live.SourceDuration > 0) || float.IsInfinity(live.SourceDuration) || !float.IsFinite(delta) || delta<0)
                throw new ArgumentOutOfRangeException();
            decision.ParameterSnapshot = Snapshot(parameters);
            JArray Edges(int list)=>(JArray)(list==-1?definition["anyTransitions"]:definition["states"][list]["transitions"]);
            int current=checked((int)live.CurrentState),next=checked((int)live.NextState);
            bool active=live.TransitionIndex!=-1;
            JObject edge=active?(JObject)Edges(live.TransitionSourceState)[live.TransitionIndex]:null;
            int origin=live.TransitionSourceState==-1?-1:live.TransitionSourceState==current?0:1;
            var queue=SourceTransitionQueue.Plan(active,(int?)edge?["intr"]??0,(bool?)edge?["ordered"]??false,
                origin,live.TransitionIndex,live.InTransition,live.InterruptionActive,Edges(-1).Count,Edges(current).Count,Edges(next).Count);
            foreach(var entry in queue)
            {
                int list=entry.Kind==0?-1:entry.Kind==1?current:next;
                var state = live.Copy();
                var command = new NativeTransitionStartCommand();
                bool destination=entry.Kind==2;
                float now=destination?live.NextNormalizedTime:live.CurrentNormalizedTime;
                if(destination&&!(live.DestinationDuration>0))throw new InvalidOperationException("Next duration is unavailable");
                float previous=destination?now-delta/live.DestinationDuration:previousNormalized;
                var result = searches[list].EvaluateAndCommit(state, command, parameters,
                    previous,now,destination?live.NextSpeedMultiplier:live.CurrentSpeedMultiplier,
                    destination?nextLoops:currentLoops,timing,list,0,candidateCount:entry.Count);
                decision=new Decision { Owner = this, Revision = revision, Result = result, SealedAccepted=result.Accepted,
                    Transition = state, StartCommand = command, SourceList = entry.Kind==0?"Any":entry.Kind==1?"Current":"Next",
                    Parameters = parameters, ParameterSnapshot = decision.ParameterSnapshot };
                if(decision.Accepted)
                {decision.SealedTarget=checked((int)state.NextState);return decision;}
            }
            return decision;
        }

        // Call at the separately qualified lifecycle boundary. This is not an
        // animation blend-completion event, nor an input-buffer consumer.
        public void CommitLifecycle(Decision decision)
        {
            if (decision == null || decision.Owner != this || !decision.Accepted || decision.Consumed || decision.Revision != revision)
                throw new InvalidOperationException("Rejected, foreign, consumed or stale transition decision");
            if (decision.ParameterSnapshot != Snapshot(decision.Parameters))
                throw new InvalidOperationException("Parameters changed after the decision was evaluated");
            int target = decision.SealedTarget;
            if ((uint)target != decision.Transition.NextState) throw new InvalidOperationException("Transition payload changed after evaluation");
            if (target < 0 || target >= ((JArray)definition["states"]).Count)
                throw new InvalidOperationException("Resolved target is outside the source machine");
            decision.Consumed = true; revision++;
            lifecycle.RequestStateChange(target, forceInstantly: true);
            // No AddTransition/AddTransitionFromAny: HFSM cannot choose or
            // reorder an authored edge. UsedTriggers are left to the host.
        }
    }
}
