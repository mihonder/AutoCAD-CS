using System.Diagnostics;

namespace CadDustLab
{
    // Deliberately shared by all targets: the same numerics and A* ordering.
    internal static class Compat
    {
        internal static double Clamp(double value,double min,double max)=>value<min?min:value>max?max:value;
        internal static int Clamp(int value,int min,int max)=>value<min?min:value>max?max:value;
        internal static TimeSpan Elapsed(long start)=>Elapsed(start,Stopwatch.GetTimestamp());
        internal static TimeSpan Elapsed(long start,long end)=>TimeSpan.FromTicks((long)((end-start)*(double)TimeSpan.TicksPerSecond/Stopwatch.Frequency));
        internal static TimeSpan? GcPause
        {
#if NETFRAMEWORK
            get=>null; // Framework does not expose total GC pause duration.
#else
            get=>GC.GetTotalPauseDuration();
#endif
        }
        internal static T MinBy<T>(IEnumerable<T> source,Func<T,double> key)
        {
            using var e=source.GetEnumerator();if(!e.MoveNext())throw new InvalidOperationException("Empty sequence");
            T result=e.Current;double minimum=key(result);
            while(e.MoveNext()){double next=key(e.Current);if(next<minimum){result=e.Current;minimum=next;}}
            return result;
        }
        internal static TValue GetValueOrDefault<TKey,TValue>(this Dictionary<TKey,TValue> source,TKey key,TValue fallback=default!) where TKey:notnull
            =>source.TryGetValue(key,out var value)?value:fallback;
    }

    // Four-way min heap, matching PriorityQueue's first-child tie ordering.
    internal sealed class MinQueue<T>
    {
        private readonly List<(T Value,double Priority)> heap=new();
        internal void Enqueue(T value,double priority)
        {
            int index=heap.Count;heap.Add(default);
            while(index>0){int parent=(index-1)/4;if(priority>=heap[parent].Priority)break;heap[index]=heap[parent];index=parent;}
            heap[index]=(value,priority);
        }
        internal bool TryDequeue(out T value,out double priority)
        {
            if(heap.Count==0){value=default!;priority=default;return false;}
            (value,priority)=heap[0];var last=heap[heap.Count-1];heap.RemoveAt(heap.Count-1);
            if(heap.Count==0)return true;
            int index=0;
            while(index*4+1<heap.Count)
            {
                int child=index*4+1,limit=Math.Min(child+4,heap.Count),best=child;
                for(int i=child+1;i<limit;i++)if(heap[i].Priority<heap[best].Priority)best=i;
                if(last.Priority<=heap[best].Priority)break;
                heap[index]=heap[best];index=best;
            }
            heap[index]=last;return true;
        }
    }
}

#if NETFRAMEWORK
namespace System.Runtime.CompilerServices { internal static class IsExternalInit { } }
#endif
