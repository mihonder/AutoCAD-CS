using CadDustLab;
using System.Diagnostics;
using System.Text.Json;

static class CompatibilityChecks
{
    public static void Module(string path)
    {
        // Load the distributable DLL and its embedded assets, without loading
        // Autodesk's native host assemblies. This also exercises LoadFrom probing.
        var assembly=System.Reflection.Assembly.LoadFrom(Path.GetFullPath(path));
        using var stream=assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n=>n.EndsWith("scene.json")))!;
        using var reader=new StreamReader(stream);string json=reader.ReadToEnd();
        var sceneType=assembly.GetType("CadDustLab.Scene",true)!;
        var scene=Activator.CreateInstance(sceneType,new object[]{json,true})!;
        var frame=sceneType.GetMethod("Render")!.Invoke(scene,new object?[]{0d,null,null})!;
        var batch=assembly.GetType("CadDustLab.VectorBatch",true)!.GetMethod("Build")!.Invoke(null,new[]{frame})!;
        int count=((Array)batch.GetType().GetProperty("Chunks")!.GetValue(batch)!).Length;
        if(count==0)throw new Exception("Packaged module produced no geometry");
        var references=assembly.GetReferencedAssemblies();
        Console.WriteLine("PACKAGED MODULE PASSED: "+path+"; "+count+" chunks; "+string.Join(", ",references.Where(a=>a.Name!.StartsWith("Ac")).Select(a=>a.Name+" "+a.Version)));
    }
    public static bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
    public static void Run()
    {
        void Check(bool ok,string message){if(!ok)throw new Exception(message);}
        Check(Compat.Elapsed(0,Stopwatch.Frequency)==TimeSpan.FromSeconds(1),"Stopwatch frequency conversion");
        Check(Compat.Clamp(-2,0,1)==0&&Compat.Clamp(2,0,1)==1&&double.IsNaN(Compat.Clamp(double.NaN,0d,1d)),"Clamp semantics");
        var random=new Random(19);var queue=new MinQueue<int>();
#if !NETFRAMEWORK
        var reference=new PriorityQueue<int,double>();
#endif
        for(int i=0;i<10000;i++)
        {
            int p=random.Next(100);queue.Enqueue(i,p);
#if !NETFRAMEWORK
            reference.Enqueue(i,p);
#endif
        }
        double last=-1;int count=0;
        while(queue.TryDequeue(out int id,out double priority))
        {
            Check(priority>=last,"A* queue order");last=priority;count++;
#if !NETFRAMEWORK
            Check(reference.TryDequeue(out int expected,out double ep)&&id==expected&&priority==ep,"A* tie order differs from original PriorityQueue");
#endif
        }
        Check(count==10000,"A* queue lost entries");
        // Serialization is used for assets, diagnostics, and renderer output.
        using var document=JsonDocument.Parse(JsonSerializer.Serialize(new V(1,2,3)));
        Check(document.RootElement.GetProperty("X").GetDouble()==1,"Framework JSON runtime/dependency binding");
        Console.WriteLine("COMPATIBILITY CHECKS PASSED: timer, clamp, 10000-entry queue, JSON dependency loading.");
    }
}
