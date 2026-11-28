using CadDustLab;
using System.Text.Json;
using System.Diagnostics;
if(args.Length is 1 or 2&&args[0]=="--language"){LanguageChecks.Run(args.Length==2?args[1]:null);return;}
if(args.Length==1&&args[0]=="--compat"){CompatibilityChecks.Run();return;}
if(args.Length==1&&args[0]=="--input"){InputChecks.Run();return;}
if(args.Length==1&&args[0]=="--freeze"){FreezeChecks.Run(new Scene(File.ReadAllText("../../assets/scene.json"),true));return;}
if(args.Length==1&&args[0]=="--health"){HealthChecks.Run();return;}
if(args.Length==1&&args[0]=="--crouch"){CrouchChecks.Run();return;}
if(args.Length==1&&args[0]=="--gameplay24"){Gameplay24Checks.Run();return;}
if(args.Length==2&&args[0]=="--module"){CompatibilityChecks.Module(args[1]);return;}
if(args.Length==1&&args[0]=="--draft"){DraftChecks.Run();return;}
if(args.Length==1&&args[0]=="--rounds"){RoundChecks.Run();return;}
var scene=new Scene(File.ReadAllText("../../assets/scene.json"));
if(args.Length==2&&args[0]=="--bench"){Perf.Run(scene,args[1]);return;}
Console.WriteLine($"{scene.FaceCount} faces; {scene.PatchCount} vector patches; route {scene.RouteLength:F0} units");
PolishChecks.Run(scene);
OcclusionChecks.Run(scene);
PipelineChecks.Run(scene);
CombatChecks.Run(scene);
foreach(double t in new[]{0d,8,16,24,32,40,44})
{
    var sw=Stopwatch.StartNew();var frame=scene.Render(t);sw.Stop();
    File.WriteAllText($"../../frame-{t}.json",JsonSerializer.Serialize(frame));
    Console.WriteLine($"t={t}: {frame.Polygons.Count} polygons; {frame.VisibleFaces} faces; {sw.Elapsed.TotalMilliseconds:F1}ms; eye {frame.Camera}; contents {scene.Contents(frame.Camera)}");
}
// Route clearance and projection invariants, including the turns and near plane.
for(int i=0;i<=220;i++)
{
    var p=scene.Position(scene.RouteLength*i/220);
    foreach(var offset in new[]{new V(0,0,0),new V(16,0,0),new V(-16,0,0),new V(0,16,0),new V(0,-16,0)})
        if(scene.Contents(p+offset)!=-1)throw new Exception($"Route intersects BSP at {p+offset}");
}
Console.WriteLine("Route clearance passed at 221 samples.");
foreach(var p in new[]{new V(860,960,32),new V(1455,1370,32)})Console.WriteLine($"Actor location {p}: contents {scene.Contents(p)}");
