using System;
using System.Collections.Generic;

namespace Remielle.Controller
{
    // UnityPlayer b8a21ad9..., cffc3b..cffe95. This is candidate-list planning,
    // not the engine's interrupted graph implementation. Kind 0/1/2 = Any/Current/Next.
    public static class SourceTransitionQueue
    {
        public readonly struct Entry
        {
            public readonly int Kind, Count;
            public Entry(int kind,int count){Kind=kind;Count=count;}
        }
        public static IReadOnlyList<Entry> Plan(bool active,int interruptionSource,bool ordered,
            int origin,int edge,bool priorInTransition,bool interruptionActive,int anyCount,int currentCount,int nextCount)
        {
            if(anyCount<0||currentCount<0||nextCount<0||active&&edge<0)throw new ArgumentOutOfRangeException();
            if(interruptionActive)return Array.Empty<Entry>();
            int[] kinds= !active ? new[]{0,1} : interruptionSource==1 ? new[]{0,1} :
                interruptionSource==2 ? new[]{0,2} : interruptionSource==3 ? new[]{0,1,2} :
                interruptionSource==4 ? new[]{0,2,1} : Array.Empty<int>();
            // origin is the active edge's list relative to current/next, with
            // -1 denoting Any. Unknown non-current origin follows native Next.
            int originKind=origin==-1?0:origin==0?1:2;
            int activeOrdinal=Array.IndexOf(kinds,originKind);
            var rows=new List<Entry>(kinds.Length);
            for(int i=0;i<kinds.Length;i++)
            {
                int kind=kinds[i],count=kind==0?anyCount:kind==1?currentCount:nextCount;
                if(active&&priorInTransition&&ordered)
                {
                    // Native unsigned comparison preserves all lists for -1.
                    if((uint)i>(uint)activeOrdinal)count=0;
                    else if(i==activeOrdinal&&originKind!=2)count=edge;
                }
                rows.Add(new Entry(kind,count));
            }
            return rows;
        }
    }
}
