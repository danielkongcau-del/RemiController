using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Remielle.ControllerRuntime;
using UnityEngine;

namespace Remielle.Controller
{
    // One primary source layer, explicit caller-owned parameters. This joins
    // qualified source edges/clocks to the practical presentation adapter.
    // Input buffering and external callback systems remain host-owned.
    public sealed class SourceActionSession : IDisposable
    {
        sealed class ActionState
        {
            public int Index;
            public SourceMotionSampler Motion;
            public NativeStateClock Clock;
        }
        readonly NativeControllerSource source;
        readonly string controller,directory;
        readonly JObject definition,profile;
        readonly NativeMotionBank bank;
        readonly RemielleNativeAnimation driver;
        readonly QualifiedStateRouter router;
        readonly NativeControllerTimeSettings settings;
        readonly List<ActionState> resources=new List<ActionState>();
        readonly BlendedRootTransport transport;
        NativeLayerTransitionState layer;
        NativeTransitionStartCommand command=new NativeTransitionStartCommand();
        ActionState current,next;
        bool disposed,started;
        Action<SourceActionNotice> observer;
        long generation=1,currentGeneration=1,nextGeneration;
        public int PreparedSamplerCount => resources.Count;
        public NativeParameterBank Parameters { get; }
        public int CurrentState => current.Index;
        public int? NextState => next?.Index;
        /// <summary>主层状态索引 → 原生状态名（供宿主策略与审计使用）。</summary>
        public string StateName(int index) => (string)definition["machines"][0]["states"][index]["name"];
        public bool IsBlending => next!=null;
        public float ActionFrames => IsBlending?layer.NextFrameCount:layer.CurrentFrameCount;
        public string BlockedReason { get; private set; }
        public List<JObject> Trace { get; }=new List<JObject>();
        public int DroppedTraceEntries { get; private set; }
        void Record(JObject entry)
        {
            if(Trace.Count>=4096){Trace.RemoveAt(0);DroppedTraceEntries++;}
            Trace.Add(entry);
        }
        public void ObserveActions(Action<SourceActionNotice> sink)
        {
            if(disposed||started||observer!=null)throw new InvalidOperationException("Attach the action observer once before playback starts");
            observer=sink??throw new ArgumentNullException(nameof(sink));
            Notify(SourceActionPhase.Enter,current,currentGeneration,layer.CurrentFrameCount,"initial");
        }
        void Notify(SourceActionPhase phase,ActionState state,long id,float frame,string reason)
        {
            // Match the clock's float multiplication and non-loop terminal
            // frame cap. A double duration*60 can exceed that cap and strand
            // max-frame cleanup until a later transition.
            float length=state.Motion.Duration*60f;
            double terminal=state.Motion.Loop?length:Math.Floor((double)(length+.5f));
            observer?.Invoke(new SourceActionNotice(phase,id,state.Index,frame,terminal,state.Motion.Loop,reason));
        }

        public SourceActionSession(NativeControllerSource source,string controller,string bankDirectory,
            NativeControllerTimeSettings settings,NativeControllerAvatarBindings avatars,
            RemielleNativeAnimation driver,Transform visual,RemielleAuthoredMotor motor,int initialState=32)
        {
            this.source=source;this.controller=controller;directory=bankDirectory;this.settings=settings;this.driver=driver;
            definition=source.GetController(controller);profile=avatars.Resolve(definition);bank=new NativeMotionBank(directory);
            Parameters=source.CreateParameters(controller);router=new QualifiedStateRouter(source,controller,0,initialState);
            current=Prepare(initialState);
            layer=new NativeLayerTransitionState{CurrentState=(uint)initialState,SourceDuration=current.Motion.Duration,
                TransitionIndex=-1,TransitionSourceState=-1};
            transport=new BlendedRootTransport(current.Motion,driver,visual,motor,1);
        }

        // Do disk reads/ACL allocation before input starts. Three instances
        // allow current + destination + interrupted self-transition target.
        public void Prewarm(params string[] stateNames)
        {
            if(disposed)throw new ObjectDisposedException(nameof(SourceActionSession));
            if(started)throw new InvalidOperationException("Prewarm must precede the first advancing tick");
            var states=(JArray)definition["machines"][0]["states"];
            try
            {
                foreach(string name in stateNames.Distinct())
                {
                    int index=states.Select((s,i)=>(s,i)).Single(x=>(string)x.s["name"]==name).i;
                    while(resources.Count(s=>s.Index==index)<3)Prepare(index,false);
                }
            }
            finally{transport.RefreshPose();}
        }

        ActionState Prepare(int index,bool reuse=true)
        {
            if(reuse)foreach(var entry in resources)if(entry.Index==index&&!ReferenceEquals(entry,current)&&!ReferenceEquals(entry,next))return entry;
            var state=(JObject)definition["machines"][0]["states"][index];
            int tree=(int)state["blendTreeIndices"][(int)definition["layers"][0]["smms"]];
            var node=state["trees"][tree]["nodes"][0];
            if(((JArray)node["childIndices"]).Count!=0||(float)node["duration"]!=1||(float)node["cycleOffset"]!=0||(bool)node["mirror"]||(float)state["speed"]<0)
                throw new NotSupportedException("State requires its blend tree, clip-direction or offset adapter: "+state["name"]);
            // ResolveLeaf validates the source-qualified selected PPtr as well.
            if(source.ResolveLeaf(controller,0,index)==null)throw new NotSupportedException("State has no source motion");
            var action=new ActionState{Index=index,Clock=new NativeStateClock(state),
                Motion=new SourceMotionSampler(directory,bank,controller,(int)node["clipIndex"],driver.nativeAnimation.transform,profile)};
            resources.Add(action);return action;
        }

        NativeStateClockResult Advance(ActionState state,bool isCurrent,float delta)
        {
            return state.Clock.Advance(layer,command,Parameters,state.Motion.Duration,delta,1,
                new NativeStateClockOptions(isCurrent,state.Motion.Loop,false,false,0,false,false,false));
        }
        double CurrentTime => (double)(layer.CurrentNormalizedTime*current.Motion.Duration);
        double NextTime => (double)(layer.NextNormalizedTime*next.Motion.Duration);

        // FrameCount production here is an explicit primary-layer adapter
        // (truncate the native clock's frame value), not a claim about the
        // original gameplay writer or either unknown correction flag.
        void PublishFrames()=>Parameters.SetInt(Parameters.Hash("FrameCount"),(int)ActionFrames);

        public void Tick(float delta)
        {
            if(disposed)throw new ObjectDisposedException(nameof(SourceActionSession));
            if(!float.IsFinite(delta)||delta<0)throw new ArgumentOutOfRangeException(nameof(delta));
            BlockedReason=null;
            if(delta==0)return; // Pause does not advance or consume a request.
            started=true;
            float previous=Advance(current,true,delta).PreviousNormalized;
            if(IsBlending)
            {
                Advance(next,false,delta);
                float weight=NativeTransitionProgress.Advance(layer,delta,0,false,false).Weight;
                transport.Sample(CurrentTime,NextTime,weight);
                Notify(SourceActionPhase.Sample,current,currentGeneration,layer.CurrentFrameCount,"update");
                Notify(SourceActionPhase.Sample,next,nextGeneration,layer.NextFrameCount,"update");
                if(weight>=1){Complete();previous=layer.CurrentNormalizedTime;}
            }
            else {transport.Sample(CurrentTime);Notify(SourceActionPhase.Sample,current,currentGeneration,layer.CurrentFrameCount,"update");}
            PublishFrames();
            var decision=router.PreviewLayer(Parameters,layer,previous,delta,current.Motion.Loop,next?.Motion.Loop??false,
                settings.TransitionPolicy(layer.EndTransitionMarker),callbackContextQualified:true);
            layer.EndTransitionMarker=false;
            if(!decision.Accepted){BlockedReason=decision.BlockedReason;return;}
            // Capture before Prepare: loading a new sampler touches the shared
            // source rig. The snapshot must be the actual outgoing mixed pose.
            bool interrupted=IsBlending;
            int interruptedTarget=next?.Index??-1;
            var snapshot=interrupted?transport.CaptureInterruption():null;
            var destination=Prepare((int)decision.Transition.NextState);
            float frames=layer.CurrentFrameCount;
            var accepted=decision.Transition;
            accepted.CurrentFrameCount=layer.CurrentFrameCount;accepted.CurrentSpeedMultiplier=layer.CurrentSpeedMultiplier;
            router.CommitLifecycle(decision);
            // Consume the accepted input before new-state events can publish a
            // new request using that same trigger hash.
            foreach(uint trigger in decision.Result.UsedTriggers)Parameters.ResetTrigger(trigger);
            if(interrupted)
            {
                Notify(SourceActionPhase.Leave,next,nextGeneration,layer.NextFrameCount,"interrupted");
                Notify(SourceActionPhase.Exit,next,nextGeneration,layer.NextFrameCount,"interrupted");
            }
            else Notify(SourceActionPhase.Leave,current,currentGeneration,layer.CurrentFrameCount,"transition");
            layer=accepted;command=decision.StartCommand;next=destination;
            nextGeneration=++generation;
            Advance(next,false,0);
            if(interrupted)transport.InterruptBlend(next.Motion,NextTime,snapshot);
            else transport.BeginBlend(next.Motion,NextTime);
            float initialWeight=NativeTransitionProgress.Advance(layer,0,decision.Result.LastGateOvershoot,true,false).Weight;
            transport.Sample(CurrentTime,NextTime,initialWeight);
            Notify(SourceActionPhase.Enter,next,nextGeneration,layer.NextFrameCount,"transition");
            Record(new JObject{["event"]="transition-start",["sourceState"]=current.Index,["targetState"]=next.Index,
                ["interrupted"]=interrupted,["interruptedTarget"]=interruptedTarget,
                ["sourceList"]=decision.SourceList,["edge"]=layer.TransitionIndex,["sourceFrames"]=frames,
                ["destinationFrames"]=layer.NextFrameCount,["duration"]=layer.TransitionDuration,
                ["fixedDuration"]=layer.FixedDuration,["usedTriggers"]=new JArray(decision.Result.UsedTriggers)});
            // Single-layer local commit consumption. The host buffer observes
            // these resets; cross-layer consumption remains a separate system.
            if(initialWeight>=1)Complete();
            PublishFrames();
        }
        void Complete()
        {
            int old=current.Index;
            Notify(SourceActionPhase.Exit,current,currentGeneration,layer.CurrentFrameCount,"blend-complete");
            transport.CompleteBlend();
            if(!NativeTransitionProgress.TryComplete(layer,out uint eventCode))throw new InvalidOperationException("Native completion did not commit");
            current=next;next=null;currentGeneration=nextGeneration;
            Record(new JObject{["event"]="blend-complete",["sourceState"]=old,["targetState"]=current.Index,
                ["destinationFrames"]=layer.CurrentFrameCount,["eventCode"]=eventCode});
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            try{Notify(SourceActionPhase.Exit,current,currentGeneration,layer.CurrentFrameCount,"owner-disposed");}
            finally
            {
                try{if(next!=null)Notify(SourceActionPhase.Exit,next,nextGeneration,layer.NextFrameCount,"owner-disposed");}
                finally{observer=null;transport?.Dispose();foreach(var state in resources)state.Motion.Dispose();resources.Clear();}
            }
        }
    }
}
