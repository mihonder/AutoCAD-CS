using CadDustLab;
using System.Diagnostics;
using System.Text.Json;
internal static class Perf
{
    private static long? AllocatedBytes()
    {
#if NETFRAMEWORK
        return null;
#else
        return GC.GetAllocatedBytesForCurrentThread();
#endif
    }
    internal static void Run(Scene scene,string label)
    {
        var times=new List<double>();
        for(int i=0;i<10;i++)scene.Render(i,new CameraPose(scene.Position(i*35),i*.07,0));
        long? bytes=AllocatedBytes();int gc0=GC.CollectionCount(0),gc1=GC.CollectionCount(1),gc2=GC.CollectionCount(2);
        int peak=0;
        for(int i=0;i<240;i++)
        {
            var camera=new CameraPose(scene.Position(scene.RouteLength*(i%80)/80),i*.061,Math.Sin(i*.1)*.45);
            long stamp=Stopwatch.GetTimestamp();var frame=scene.Render(i/30.0,camera);
            if(label.Contains("batch"))VectorBatch.Build(frame);
            times.Add(Compat.Elapsed(stamp).TotalMilliseconds);peak=Math.Max(peak,frame.Polygons.Count);
        }
        var report=JsonSerializer.Serialize(new{label,frames=times.Count,meanMs=times.Average(),p95Ms=times.OrderBy(x=>x).ElementAt(227),maxMs=times.Max(),allocatedBytesPerFrame=(AllocatedBytes()-bytes)/times.Count,gen0=GC.CollectionCount(0)-gc0,gen1=GC.CollectionCount(1)-gc1,gen2=GC.CollectionCount(2)-gc2,peak},new JsonSerializerOptions{WriteIndented=true});
        Console.WriteLine(report);File.WriteAllText($"../../perf-{label}.json",report);
    }
}
