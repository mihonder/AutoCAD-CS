using CadDustLab;
using System.Collections.Concurrent;
using System.Diagnostics;
internal static class PipelineChecks
{
    private sealed record Job(int Id);
    internal static void Run(Scene scene)
    {
        void Check(bool ok,string error){if(!ok)throw new Exception(error);}
        using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
        var executed=new ConcurrentQueue<int>();int active=0,peak=0;
        using(var worker=new LatestWorker<Job,Job>(job=>{
            int current=Interlocked.Increment(ref active);peak=Math.Max(peak,current);executed.Enqueue(job.Id);
            if(job.Id==1){entered.Set();if(!release.Wait(3000))throw new Exception("Test gate timed out");}
            Interlocked.Decrement(ref active);return job;
        }))
        {
            worker.Submit(new(1));Check(entered.Wait(3000),"Worker did not start");
            var sw=Stopwatch.StartNew();for(int i=2;i<=100;i++)worker.Submit(new(i));
            Check(sw.ElapsedMilliseconds<200,"Submitting waits for blocked renderer");
            release.Set();Check(SpinWait.SpinUntil(()=>worker.Completed==2,3000),"Latest work did not complete");
            Check(worker.Take()?.Id==100,"Did not publish latest frame");
            Check(peak==1&&executed.SequenceEqual(new[]{1,100})&&worker.Coalesced==98,"Unbounded or concurrent rendering");
        }
        using var entered2=new ManualResetEventSlim();using var release2=new ManualResetEventSlim();using var exited2=new ManualResetEventSlim();
        var stopped=new LatestWorker<Job,Job>(job=>{entered2.Set();release2.Wait(3000);exited2.Set();return job;});
        stopped.Submit(new(1));Check(entered2.Wait(3000),"Stop test did not start");
        var stopTime=Stopwatch.StartNew();stopped.Dispose();Check(stopTime.ElapsedMilliseconds<200,"Dispose blocks UI");
        release2.Set();Check(exited2.Wait(3000),"Stopped work did not finish");Check(stopped.Take() is null,"Stopped worker published stale frame");
        using(var failed=new LatestWorker<Job,Job>(_=>throw new InvalidOperationException("injected")))
        {
            failed.Submit(new(0));Exception? error=null;
            Check(SpinWait.SpinUntil(()=> (error=failed.TakeError()) is not null,3000)&&error?.Message=="injected","Worker exception was lost");
        }
        var smoothing=new CameraSmoother();smoothing.Follow(new(new(1,2,64),Math.PI-.01,0),.016);
        var pose=smoothing.Follow(new(new(2,3,64),-Math.PI+.01,.1),.016);
        Check(Math.Abs(pose.Yaw-Math.PI)<.02&&pose.Eye==new V(2,3,64),"Smoothing turns long way or changes collision position");
        for(int i=0;i<240;i++)
        {
            var frame=scene.Render(i/30.0,new CameraPose(scene.Position(scene.RouteLength*(i%80)/80),i*.061,Math.Sin(i*.1)*.45));
            var batch=VectorBatch.Build(frame);double triangles=0,polygons=0;
            foreach(var p in frame.Polygons){double area=0;for(int k=0;k<p.Points.Length;k++){var a=p.Points[k];var b=p.Points[(k+1)%p.Points.Length];area+=a.X*b.Y-a.Y*b.X;}polygons+=Math.Abs(area)*.5;}
            foreach(var chunk in batch.Chunks)
            {
              Check(chunk.Vertices.Length<=1024&&chunk.Faces.Length*4<85000,"Batch exceeds small object heap size");
              Check(chunk.Colors.Length*4==chunk.Faces.Length,"Missing face colors");
              for(int k=0;k<chunk.Faces.Length;k+=4)
              {
                Check(chunk.Faces[k]==3,"Non-triangle shell face");var a=chunk.Vertices[chunk.Faces[k+1]];var b=chunk.Vertices[chunk.Faces[k+2]];var c=chunk.Vertices[chunk.Faces[k+3]];
                Check(a.Z==b.Z&&b.Z==c.Z,"Mixed paint order in triangle");
                double area=((b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X))*.5;
                Check(area>=-1e-7,"Backwards triangle");triangles+=area;
              }
            }
            Check(Math.Abs(triangles-polygons)<.001*Math.Max(1,polygons),$"Triangulation changed silhouette at frame {i}");
        }
        Console.WriteLine("PIPELINE CHECKS PASSED: latest request, bounded work, nonblocking stop, error propagation, camera wrap, 240 triangulated views.");
    }
}
