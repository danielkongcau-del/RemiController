using System;
namespace Remielle.Controller
{
    public enum SourceActionPhase { Enter,Sample,Leave,Exit }
    public readonly struct SourceActionNotice
    {
        public readonly SourceActionPhase Phase;
        public readonly long Generation;
        public readonly int State;
        public readonly double Frame,LengthFrames;
        public readonly bool Loop;
        public readonly string Reason;
        public SourceActionNotice(SourceActionPhase phase,long generation,int state,double frame,double length,bool loop,string reason)
        {Phase=phase;Generation=generation;State=state;Frame=frame;LengthFrames=length;Loop=loop;Reason=reason;}
    }
}
