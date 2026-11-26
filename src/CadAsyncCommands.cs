using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Colors;
using Acad=Autodesk.AutoCAD.ApplicationServices.Core.Application;
using Polyline=Autodesk.AutoCAD.DatabaseServices.Polyline;

namespace CadDustLab;
public class Commands
{
    private static Walk? walk;
    private const string CommandGroup="CSDUST24";
    [CommandMethod(CommandGroup,"DUSTROUNDSTOP24",CommandFlags.Modal)] public void DraftStop()=>Stop();
    private static IEnumerable<Type> LegacyTypes=>AppDomain.CurrentDomain.GetAssemblies().Where(a=>a.GetName().Name is "CadDustCompat23" or "CadDustCompat22" or "CadDustCompat21" or "CadDustCompat20" or "CadDustRound19" or "CadDustRound18" or "CadDustRound17" or "CadDustRound16" or "CadDustRound15" or "CadDustDraft14" or "CadDustPolish13").Select(a=>a.GetType("CadDustLab.Commands")!);
    private static Type? LegacyType=>LegacyTypes.FirstOrDefault(t=>t.GetField("walk",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)?.GetValue(null) is not null);
    private static bool LegacyActive=>LegacyType?.GetField("walk",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)?.GetValue(null) is not null;
    private static void Legacy(string method){foreach(var t in method=="Stop"?LegacyTypes:LegacyTypes.Where(t=>t==LegacyType))t.GetMethod(method)!.Invoke(Activator.CreateInstance(t),null);}
    [CommandMethod(CommandGroup,"DUSTWALK",CommandFlags.Modal)] public void Start(){if(walk is null&&LegacyActive)Legacy("Start");else Launch(false,true);}
    [CommandMethod(CommandGroup,"DUSTPLAY",CommandFlags.Modal)] public void Play(){if(walk is null&&LegacyActive)Legacy("Play");else Launch(true,true);}
    [CommandMethod(CommandGroup,"CS16",CommandFlags.Modal)] public void CombatVersion()=>Launch(true,true);
    [CommandMethod(CommandGroup,"CS16COMPAT24",CommandFlags.Modal)] public void CompatibleVersion()=>Launch(true,true);
    [CommandMethod(CommandGroup,"CS16ROLLBACK19",CommandFlags.Modal)] public void Rollback19()
    {
        var old=LegacyTypes.FirstOrDefault(t=>t.Assembly.GetName().Name=="CadDustRound19");
        if(old is null){Acad.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\nLoad the preserved CadDustRound19.dll first.\n");return;}
        Stop();old.GetMethod("CombatVersion")!.Invoke(Activator.CreateInstance(old),null);
    }
    [CommandMethod(CommandGroup,"DUSTA",CommandFlags.Modal)] public void AreaA(){if(walk is null)Legacy("AreaA");else walk.Go(new(1152,2640,128),Math.PI);}
    [CommandMethod(CommandGroup,"DUSTB",CommandFlags.Modal)] public void AreaB(){if(walk is null)Legacy("AreaB");else walk.Go(new(-1850,2300,64),0);}
    [CommandMethod(CommandGroup,"DUSTMID",CommandFlags.Modal)] public void AreaMid(){if(walk is null)Legacy("AreaMid");else walk.Go(new(-384,1100,64),0);}
    [CommandMethod(CommandGroup,"DUSTT",CommandFlags.Modal)] public void AreaT(){if(walk is null)Legacy("AreaT");else walk.Go(new(-832,-704,204),0);}
    [CommandMethod(CommandGroup,"DUSTCT",CommandFlags.Modal)] public void AreaCt(){if(walk is null)Legacy("AreaCt");else walk.Go(new(352,2240,-60),Math.PI/2);}
    [CommandMethod(CommandGroup,"DUSTTIMER",CommandFlags.Modal)] public void TimerPlay()=>Launch(true,false);
    [CommandMethod(CommandGroup,"DUSTPULSE",CommandFlags.Modal)] public void PulsePlay()=>Launch(true,true);
    [CommandMethod(CommandGroup,"DUSTTIMERWALK",CommandFlags.Modal)] public void TimerWalk()=>Launch(false,false);
    [CommandMethod(CommandGroup,"DUSTPULSEWALK",CommandFlags.Modal)] public void PulseWalk()=>Launch(false,true);
    [CommandMethod(CommandGroup,"DUSTREPORT",CommandFlags.Modal)] public void Report(){if(walk is null)Legacy("Report");else walk.Report();}
    private static void Launch(bool interactive,bool posted)
    {
        if(!HostCompatibility.Check())return;
        // A loaded .NET DLL cannot be unloaded from AutoCAD. Stop the preserved
        // classic instance explicitly when switching back to this command group.
        Legacy("Stop");
        walk?.Dispose();walk=null;
        try{var next=new Walk(Acad.DocumentManager.MdiActiveDocument,interactive,posted);next.Start();walk=next;}
        catch(System.Exception ex)
        {
            try{File.WriteAllText(Path.Combine(HostCompatibility.LogDirectory,"startup.error.txt"),ex.ToString());}catch(IOException){}catch(UnauthorizedAccessException){}
            Acad.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\nCS16 start failed: "+ex.Message+". Details: %LOCALAPPDATA%/CS16/logs/compat24.\n");
        }
    }
    [CommandMethod(CommandGroup,"DUSTSTOP",CommandFlags.Modal)] public void Stop(){walk?.Dispose();walk=null;Legacy("Stop");}
    [CommandMethod(CommandGroup,"DUSTPAUSE",CommandFlags.Modal)] public void Pause(){if(walk is null)Legacy("Pause");else walk.TogglePause();}
    [CommandMethod(CommandGroup,"DUSTRESUME",CommandFlags.Modal)] public void Resume(){if(walk is null)Legacy("Resume");else walk.Resume();}
}

// Every native wrapper is created, used and disposed on the CAD UI thread.
internal sealed class NativeBatch:IDisposable
{
    internal readonly Point3dCollection Points;
    internal readonly IntegerCollection Faces;
    internal readonly FaceData FaceData=new();
    internal readonly EdgeData EdgeData=new();
    internal NativeBatch(VectorChunk batch,FrameDiagnostics diagnostics)
    {
        var points=new Point3d[batch.Vertices.Length];
        using(diagnostics.Measure("convert_point_values"))
            for(int i=0;i<points.Length;i++){var p=batch.Vertices[i];points[i]=new(p.X,p.Y,p.Z);}
        using(diagnostics.Measure("native_collections")){Points=new Point3dCollection(points);Faces=new IntegerCollection(batch.Faces);}
        var colors=new EntityColor[batch.Colors.Length];
        using(diagnostics.Measure("native_colors"))
        {
            for(int i=0;i<colors.Length;i++){int c=batch.Colors[i];colors[i]=new((byte)(c>>16),(byte)(c>>8),(byte)c);}
            FaceData.SetTrueColors(colors);EdgeData.SetVisibility(new byte[batch.Colors.Length*3]);
        }
    }
    public void Dispose()=>Points.Dispose(); // Other batch fields are managed arrays/containers.
}
internal sealed class Painter:DrawableOverrule
{
    private readonly Polyline anchor;
    private readonly FrameDiagnostics diagnostics;
    private NativeBatch[] batches=Array.Empty<NativeBatch>();
    internal long DrawCalls;
    internal string HudText="";
    private ScreenLabel[] labels=Array.Empty<ScreenLabel>();
    private ScreenStroke[] strokes=Array.Empty<ScreenStroke>();
    private readonly Dictionary<double,TextStyle> fonts=new();
    private TextStyle Font(double height)
    {
        if(fonts.TryGetValue(height,out var style))return style;
        string file=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts),"isocpeur.ttf");
        style=new TextStyle{FileName=File.Exists(file)?file:"isocp.shx",TextSize=height,XScale=1};
        _=style.LoadStyleRec;fonts.Add(height,style);return style;
    }
    internal Painter(Polyline anchor,FrameDiagnostics diagnostics){this.anchor=anchor;this.diagnostics=diagnostics;SetCustomFilter();}
    internal void Replace(VectorBatch values)
    {
        var next=new List<NativeBatch>(values.Chunks.Length);
        try{using(diagnostics.Measure("native_convert_frame"))foreach(var chunk in values.Chunks)next.Add(new NativeBatch(chunk,diagnostics));}
        catch{foreach(var item in next)item.Dispose();throw;}
        var old=batches;batches=next.ToArray();using(diagnostics.Measure("native_dispose_frame"))foreach(var item in old)item.Dispose();
        labels=values.Labels;strokes=values.Strokes;
    }
    internal void Clear(){foreach(var item in batches)item.Dispose();batches=Array.Empty<NativeBatch>();foreach(var font in fonts.Values)font.Dispose();fonts.Clear();}
    public override bool IsApplicable(RXObject subject)=>subject.UnmanagedObject==anchor.UnmanagedObject;
    public override int SetAttributes(Drawable drawable,DrawableTraits traits)=>base.SetAttributes(drawable,traits)|(int)DrawableAttributes.RegenDraw;
    public override bool WorldDraw(Drawable drawable,WorldDraw wd)
    {
        using var draw=diagnostics.Measure("world_draw");
        if(batches.Length==0)return true;
        wd.SubEntityTraits.FillType=FillType.FillAlways;
        using(diagnostics.Measure("geometry_shell_frame"))foreach(var batch in batches)wd.Geometry.Shell(batch.Points,batch.Faces,batch.EdgeData,batch.FaceData,null!,false);
        using(diagnostics.Measure("cad_text_tables"))
        {
            foreach(var stroke in strokes)
            {
                wd.SubEntityTraits.TrueColor=new EntityColor((byte)(stroke.Color>>16),(byte)(stroke.Color>>8),(byte)stroke.Color);
                wd.SubEntityTraits.LineWeight=(LineWeight)stroke.Weight;
                var source=stroke.Closed?stroke.Points.Concat(new[]{stroke.Points[0]}):stroke.Points;
                using var points=new Point3dCollection(source.Select(p=>new Point3d(p.X,p.Y,p.Z)).ToArray());
                wd.Geometry.Polyline(points,Vector3d.ZAxis,IntPtr.Zero);
            }
            wd.SubEntityTraits.LineWeight=LineWeight.LineWeight000;
            foreach(var label in labels)
            {
                wd.SubEntityTraits.TrueColor=new EntityColor((byte)(label.Color>>16),(byte)(label.Color>>8),(byte)label.Color);
                wd.Geometry.Text(new(label.Position.X,label.Position.Y,label.Position.Z),Vector3d.ZAxis,Vector3d.XAxis,label.Text,true,Font(label.Height));
            }
            if(HudText.Length>0)
            {
                wd.SubEntityTraits.TrueColor=new EntityColor(16,16,16);
                using var points=new Point3dCollection(new[]{new Point3d(-800,400,99),new Point3d(800,400,99),new Point3d(800,450,99),new Point3d(-800,450,99)});
                wd.Geometry.Polygon(points);wd.SubEntityTraits.TrueColor=new EntityColor(230,230,230);
                wd.Geometry.Text(new(-775,416,100),Vector3d.ZAxis,Vector3d.XAxis,HudText,true,Font(18));
            }
        }
        DrawCalls++;return true;
    }
}
internal sealed record RenderRequest(int Generation,double Time,CameraPose? Camera,long Stamp,CombatSnapshot? Combat=null);
internal sealed record PreparedFrame(RenderRequest Request,VectorBatch Batch,int Polygons,double BuildMs);

internal sealed class Walk:IDisposable
{
    private readonly Document doc;
    private readonly ViewTableRecord savedView;
    private readonly object savedGrid,savedCursor,savedUcs,savedLineweight,savedCursorType,savedPickbox;
    private bool cursorSuppressed;
    private readonly Polyline anchor=new(4);
    private readonly Painter painter;
    private readonly Scene scene;
    private readonly FrameDiagnostics diagnostics;
    private readonly bool posted;
    private MessagePulse? pulse;
    private readonly Player? player;
    private Combat? combat;
    private readonly CameraSmoother camera=new();
    private InputCapture? input;
    private LatestWorker<RenderRequest,PreparedFrame>? worker;
    private readonly bool savedOverruling;
    private readonly IntegerCollection viewports=new();
    private readonly System.Windows.Forms.Timer timer=new(){Interval=8};
    private readonly Stopwatch clock=new();
    private readonly List<double> uiCosts=new(),buildCosts=new(),inputIntervals=new(),ages=new(),presentCosts=new();
    private readonly int[] initialGc={GC.CollectionCount(0),GC.CollectionCount(1),GC.CollectionCount(2)};
    private Task? reportTask;
    private double last,gameTime,lastReport,activeWallSeconds;
    private bool requestCapture,lastCaptured;
    private int frames,peak,generation;
    private bool disposed,finished,registered,added,autoPaused,inTick;
    private string DirectoryPath=>HostCompatibility.LogDirectory;
    internal Walk(Document document,bool interactive,bool posted)
    {
        this.posted=posted;diagnostics=new((posted?"pulse24-":"timer-")+(interactive?"play":"walk"));
        doc=document;savedView=doc.Editor.GetCurrentView();savedGrid=Acad.GetSystemVariable("GRIDMODE");
        savedCursor=Acad.GetSystemVariable("CURSORSIZE");savedUcs=Acad.GetSystemVariable("UCSICON");savedOverruling=Overrule.Overruling;
        savedLineweight=Acad.GetSystemVariable("LWDISPLAY");
        savedCursorType=Acad.GetSystemVariable("CURSORTYPE");savedPickbox=Acad.GetSystemVariable("PICKBOX");
        var asm=typeof(Scene).Assembly;
        using var stream=asm.GetManifestResourceStream(asm.GetManifestResourceNames().Single(x=>x.EndsWith("scene.json")))!;
        using var reader=new StreamReader(stream);scene=new Scene(reader.ReadToEnd(),draft:true);
        if(interactive){player=new Player(scene);combat=new Combat(scene,rounds:true);combat.RestartRound(player);}
        anchor.AddVertexAt(0,new(-800,-450),0,0,0);anchor.AddVertexAt(1,new(800,-450),0,0,0);
        anchor.AddVertexAt(2,new(800,450),0,0,0);anchor.AddVertexAt(3,new(-800,450),0,0,0);anchor.Closed=true;
        painter=new Painter(anchor,diagnostics);
    }
    private PreparedFrame Prepare(RenderRequest request)
    {
        long stamp=Stopwatch.GetTimestamp();Frame frame;
        using(diagnostics.Measure("scene_projection",false))frame=scene.Render(request.Time,request.Camera,request.Combat);
        VectorBatch batch;using(diagnostics.Measure("vector_pack",false))batch=VectorBatch.Build(frame);
        Volatile.Write(ref diagnostics.LatestPrepared,Stopwatch.GetTimestamp());
        return new(request,batch,frame.Polygons.Count,Compat.Elapsed(stamp).TotalMilliseconds);
    }
    internal void Start()
    {
        try
        {
            ResetGameView();
            Acad.SetSystemVariable("GRIDMODE",0);Acad.SetSystemVariable("CURSORSIZE",1);Acad.SetSystemVariable("UCSICON",0);Acad.SetSystemVariable("LWDISPLAY",(short)1);
            Overrule.AddOverrule(RXObject.GetClass(typeof(Polyline)),painter,false);registered=true;Overrule.Overruling=true;
            painter.Replace(Prepare(new(0,0,player?.Camera,Stopwatch.GetTimestamp(),combat is null?null:combat.Snapshot() with{Paused=true})).Batch);
            if(player is not null)painter.HudText="УПРАВЛЕНИЕ AUTOCAD | F6 — войти в игру | DUSTSTOP — закрыть игру";
            added=TransientManager.CurrentTransientManager.AddTransient(anchor,TransientDrawingMode.DirectShortTerm,129,viewports);
            if(!added)throw new InvalidOperationException("AutoCAD rejected the transient drawable");
            worker=new(Prepare);timer.Tick+=Tick;
            Acad.DocumentManager.DocumentToBeDestroyed+=OnDocumentClosing;Acad.EnterModal+=OnModal;Acad.LeaveModal+=OnLeaveModal;Acad.BeginQuit+=OnQuit;
            if(player is not null)
            {
                input=new InputCapture(doc.Window.Handle){Diagnostics=diagnostics};requestCapture=false;
                doc.Editor.WriteMessage("\nCS16: F6 capture/release. WASD run, Shift walk, Space jump, Ctrl duck, B buy, H difficulty, G drop, E swap, RMB scope. Esc releases, hold Esc exits menus. DUSTSTOP restores drawing.\n");
            }
            else doc.Editor.WriteMessage("\nDUSTWALK: 44-second automatic walk. DUSTPAUSE pauses, DUSTSTOP restores view.\n");
            pulse=new(diagnostics,()=>Tick(null,EventArgs.Empty)){DrivesFrames=posted};
            clock.Start();if(!posted)timer.Start();doc.Editor.UpdateScreen();
        }
        catch{Dispose();throw;}
    }
    private void OnDocumentClosing(object sender,DocumentCollectionEventArgs e){if(e.Document==doc)Dispose();}
    private void OnModal(object? sender,EventArgs e){if(input is not null){input.Suspended=true;input.Release("dialog opened");}}
    private void OnLeaveModal(object? sender,EventArgs e){if(input is not null)input.Suspended=false;}
    private void OnQuit(object? sender,EventArgs e)=>Dispose();
    internal void Resume(){if(!disposed&&input is not null)requestCapture=true;}
    internal void Go(V position,double yaw)
    {
        if(disposed||player is null)return;player.Place(scene,position,yaw);camera.Reset();generation++;
        painter.Replace(Prepare(new(generation,gameTime,player.Camera,Stopwatch.GetTimestamp(),combat?.Snapshot())).Batch);Present();
        doc.Editor.WriteMessage($"\nDUST area: {player.Eye}. F6 to play.\n");
    }
    internal void TogglePause()
    {
        if(disposed||finished)return;
        if(input is not null){if(input.Captured)input.Release("paused");else requestCapture=true;return;}
        autoPaused=!autoPaused;
        if(autoPaused){timer.Stop();if(pulse is not null)pulse.DrivesFrames=false;clock.Stop();generation++;}
        else{last=clock.Elapsed.TotalSeconds;clock.Start();if(posted)pulse!.DrivesFrames=true;else timer.Start();}
    }
    private void Tick(object? sender,EventArgs args)
    {
        if(disposed||finished||inTick)return;
        inTick=true;
        bool measuringActive=input is null||input.Captured||requestCapture,saveAfterTick=false;
        diagnostics.BeginTick(measuringActive);
        long tickStamp=Stopwatch.GetTimestamp();
        try
        {
            using(diagnostics.Measure("document_check"))if(Acad.DocumentManager.MdiActiveDocument!=doc){Dispose();return;}
            double now=clock.Elapsed.TotalSeconds,elapsed=now-last,dt=Compat.Clamp(elapsed,0,.08);last=now;
            bool wasActive=input is null||input.Captured;
            if(wasActive){Sample(inputIntervals,elapsed*1000);activeWallSeconds+=elapsed;}
            if(requestCapture){requestCapture=false;input?.Capture();}
            bool changed=false,active=input is null;
            CameraPose? pose=null;
            if(input is not null&&player is not null)
            {
                GameInput keys;
                using(diagnostics.Measure("input_poll"))keys=input.Read();active=input.Captured;changed=lastCaptured!=active;
                if(changed){generation++;camera.Reset();lastCaptured=active;player.StopMotion();if(active)ResetGameView();SetGameCursor(active);}
                if(changed&&!active)saveAfterTick=true;
                if(active)
                {
                    using var movement=diagnostics.Measure("collision_camera");
                    if(keys.Actions?.Contains(0x74)==true){combat?.RestartRound(player,true);camera.Reset();generation++;}
                    if(combat is {Dead:false,MenuOpen:false})player.Look(keys.Dx*combat.MouseScale,keys.Dy*combat.MouseScale);
                    if(combat is {CanMove:true})
                    {
                        player.Move(scene,keys.Forward,keys.Strafe,keys.Walk,dt,combat.MaxSpeed,keys.Jump,keys.Duck);
                    }
                    else player.StopMotion();
                    int oldRound=combat?.RoundNumber??0;bool wasDead=combat?.Dead??false,wasDeathCamera=combat?.DeathScene.Active??false;
                    using(diagnostics.Measure("combat_simulation"))combat?.Step(dt,player,keys);
                    if(combat?.RoundNumber!=oldRound||combat?.Dead!=wasDead||combat?.DeathScene.Active!=wasDeathCamera){camera.Reset();generation++;}
                    input.MenuOpen=combat?.MenuOpen??false;gameTime+=dt;
                    pose=camera.Follow(combat?.RenderCamera(player)??player.Camera,dt);
                }
                string hud=active?"ИГРА | F6 / Esc — отпустить мышь | WASD | Shift — шаг | Ctrl — присесть | B — купить | H — сложность | G — бросить":"УПРАВЛЕНИЕ AUTOCAD | F6 — войти в игру | DUSTSTOP — закрыть игру";
                if(painter.HudText!=hud){painter.HudText=hud;changed=true;}
            }
            if(worker?.TakeError() is { } error)throw new InvalidOperationException("Frame preparation failed",error);
            // Paused overlay is built once on state transition, not every tick.
            if(active||changed)worker?.Submit(new(generation,player is null?Math.Min(now,Scene.Duration):gameTime,pose??combat?.RenderCamera(player!),Stopwatch.GetTimestamp(),combat is null?null:combat.Snapshot() with{Paused=!active}));
            var ready=worker?.Take();bool presented=false;
            if(ready is not null&&ready.Request.Generation==generation)
            {
                long stamp=Stopwatch.GetTimestamp();painter.Replace(ready.Batch);
                Present();Sample(presentCosts,Compat.Elapsed(stamp).TotalMilliseconds);
                Sample(buildCosts,ready.BuildMs);Sample(ages,Compat.Elapsed(ready.Request.Stamp).TotalMilliseconds);
                frames++;peak=Math.Max(peak,ready.Polygons);presented=true;
                if(player is null&&ready.Request.Time>=Scene.Duration)
                {
                    timer.Stop();if(pulse is not null)pulse.DrivesFrames=false;clock.Stop();worker?.Dispose();finished=true;Save("completed");
                    doc.Editor.WriteMessage($"\nDUSTWALK complete: {frames/Math.Max(.001,activeWallSeconds):F1} updates/s. DUSTSTOP clears.\n");
                }
            }
            if(changed&&!presented)Present();
            if(now-lastReport>=2){using(diagnostics.Measure("report_serialize"))ReportInput(false);lastReport=now;}
        }
        catch(System.Exception ex)
        {
            input?.Release("error");
            string failure=$"UTC {DateTime.UtcNow:O}; phase={diagnostics.CurrentPhase}; eye={player?.Eye}; crouched={player?.Crouched}; health={combat?.Health}; weapon={combat?.Weapon.Id}; round={combat?.RoundNumber}\n{ex}";
            WriteLater(Path.Combine(DirectoryPath,"dust-async.error.txt"),failure);
            WriteLater(Path.Combine(DirectoryPath,$"error-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.txt"),failure);
            finished=true;Save("error");Dispose();doc.Editor.WriteMessage("\nDust stopped after an error; see dust-async.error.txt.\n");
        }
        finally
        {
            if(measuringActive){double ms=Compat.Elapsed(tickStamp).TotalMilliseconds;Sample(uiCosts,ms);diagnostics.Add("entire_active_callback",ms);}
            diagnostics.EndTick();inTick=false;
            if(saveAfterTick)diagnostics.Save(DirectoryPath);
        }
    }
    private void Present()
    {
        using(diagnostics.Measure("update_transient"))TransientManager.CurrentTransientManager.UpdateTransient(anchor,viewports);
        using(diagnostics.Measure("update_screen"))doc.Editor.UpdateScreen();
        input?.MaintainCursor();
    }
    private void ResetGameView()
    {
        // Refit the 1600x900 drawing after any pan/zoom/orbit performed while paused.
        // The pre-game savedView is independent and remains available for DUSTSTOP.
        using var view=doc.Editor.GetCurrentView();
        view.ViewDirection=Vector3d.ZAxis;view.Target=Point3d.Origin;view.CenterPoint=Point2d.Origin;
        view.ViewTwist=0;view.PerspectiveEnabled=false;view.Height=900;view.Width=1600;
        doc.Editor.SetCurrentView(view);
    }
    private void SetGameCursor(bool active)
    {
        if(cursorSuppressed==active)return;cursorSuppressed=active;
        Acad.SetSystemVariable("CURSORTYPE",active?(short)1:savedCursorType);
        Acad.SetSystemVariable("PICKBOX",active?(short)0:savedPickbox);
        input?.RefreshNativeCursor();
        doc.Editor.UpdateScreen();
    }
    internal void Report()=>diagnostics.Save(DirectoryPath);
    private static void Sample(List<double> values,double value){if(values.Count>=8192)values.RemoveRange(0,4096);values.Add(value);}
    private static double P95(List<double> values)=>values.Count==0?0:values.OrderBy(x=>x).ElementAt((int)((values.Count-1)*.95));
    private static double Mean(List<double> values)=>values.Count==0?0:values.Average();
    private static double Max(List<double> values)=>values.Count==0?0:values.Max();
    private static Task WriteLater(string path,string text)=>Task.Run(()=>{try{File.WriteAllText(path,text);}catch(IOException){}catch(UnauthorizedAccessException){}});
    private void ReportInput(bool final)
    {
        if(input is null||player is null)return;
        if(!final&&reportTask is {IsCompleted:false})return;
        string path=Path.Combine(DirectoryPath,"input-state.json");
        // Capture values on the UI thread; sorting metrics and JSON serialization run in the writer.
        var ui=uiCosts.ToArray();var builds=buildCosts.ToArray();var presents=presentCosts.ToArray();var intervals=inputIntervals.ToArray();var frameAges=ages.ToArray();
        var state=new{
            captured=input.Captured,reason=input.ReleaseReason,eye=player.Eye,yaw=player.Yaw,pitch=player.Pitch,
            distanceMoved=player.DistanceMoved,blockedSteps=player.BlockedSteps,input.MovementPresses,input.MouseSamples,
            input.EscapeCount,input.CaptureCount,input.MouseDistance,frames,seconds=clock.Elapsed.TotalSeconds,
            coalesced=worker?.Coalesced,completed=worker?.Completed,
            weapon=combat?.Weapon.Id,ammo=combat?.Ammo,reserve=combat?.Reserve,money=combat?.Money,health=combat?.Health,armor=combat?.Armor,kills=combat?.Kills,shots=combat?.Shots,hits=combat?.Hits,buyMenu=combat?.Menu,
            difficulty=combat?.Difficulty,round=combat?.RoundNumber,fov=combat?.Fov,drops=combat?.Drops.Count,spectator=combat is {Dead:true}?combat.Observer.Mode.ToString():"alive",
            player.Grounded,player.Crouched,player.JumpCount,player.CrouchCount,player.Speed,player.VerticalSpeed,input.ScorePresses
            ,inputStats=input.InputStats()
        };
        var previous=reportTask;
        reportTask=Task.Run(async()=>
        {
            if(previous is not null)await previous;
            double Percentile(double[] values){Array.Sort(values);return values.Length==0?0:values[(int)((values.Length-1)*.95)];}
            string text=JsonSerializer.Serialize(new{
                state.captured,state.reason,state.eye,state.yaw,state.pitch,state.distanceMoved,state.blockedSteps,
                state.MovementPresses,state.MouseSamples,state.EscapeCount,state.CaptureCount,state.MouseDistance,state.frames,state.seconds,
                uiP95Ms=Percentile(ui),prepareP95Ms=Percentile(builds),presentP95Ms=Percentile(presents),inputIntervalP95Ms=Percentile(intervals),
                frameAgeP95Ms=Percentile(frameAges),state.coalesced,state.completed,state.weapon,state.ammo,state.reserve,state.money,state.health,state.armor,state.kills,state.shots,state.hits,state.buyMenu,
                state.difficulty,state.round,state.fov,state.drops,state.spectator,
                state.Grounded,state.Crouched,state.JumpCount,state.CrouchCount,state.Speed,state.VerticalSpeed,state.ScorePresses
                ,state.inputStats
            },new JsonSerializerOptions{WriteIndented=true});
            try{File.WriteAllText(path,text);}catch(IOException){}catch(UnauthorizedAccessException){}
        });
    }
    private void Save(string status)
    {
        diagnostics.Save(DirectoryPath);
        string text=JsonSerializer.Serialize(new{
            status,seconds=clock.Elapsed.TotalSeconds,frames,activeSeconds=activeWallSeconds,activeUpdatesPerSecond=frames/Math.Max(.001,activeWallSeconds),
            worldDrawCalls=painter.DrawCalls,sourceFaces=scene.FaceCount,sourcePatches=scene.PatchCount,peakVisiblePolygons=peak,
            meanUiMs=Mean(uiCosts),p95UiMs=P95(uiCosts),maxUiMs=Max(uiCosts),meanPrepareMs=Mean(buildCosts),p95PrepareMs=P95(buildCosts),maxPrepareMs=Max(buildCosts),
            p95PresentMs=P95(presentCosts),p95InputIntervalMs=P95(inputIntervals),maxInputIntervalMs=Max(inputIntervals),p95FrameAgeMs=P95(ages),
            coalescedRequests=worker?.Coalesced,completedFrames=worker?.Completed,
            gcCollections=new[]{GC.CollectionCount(0)-initialGc[0],GC.CollectionCount(1)-initialGc[1],GC.CollectionCount(2)-initialGc[2]},
            note="UI callback/submission timings, not presented FPS. One background calculation, latest pending camera, one completed frame. Native CAD calls remain on UI thread."
        },new JsonSerializerOptions{WriteIndented=true});
        WriteLater(Path.Combine(DirectoryPath,"dust-async-results.json"),text);
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;pulse?.Dispose();input?.Dispose();timer.Stop();timer.Dispose();clock.Stop();worker?.Dispose();
        Acad.DocumentManager.DocumentToBeDestroyed-=OnDocumentClosing;Acad.EnterModal-=OnModal;Acad.LeaveModal-=OnLeaveModal;Acad.BeginQuit-=OnQuit;
        ReportInput(true);if(!finished)Save("stopped");
        if(added)TransientManager.CurrentTransientManager.EraseTransient(anchor,viewports);
        if(registered)Overrule.RemoveOverrule(RXObject.GetClass(typeof(Polyline)),painter);
        painter.Clear();painter.Dispose();anchor.Dispose();
        if(!savedOverruling)Overrule.Overruling=false;
        if(Acad.DocumentManager.MdiActiveDocument==doc)
        {
            doc.Editor.SetCurrentView(savedView);Acad.SetSystemVariable("GRIDMODE",savedGrid);Acad.SetSystemVariable("UCSICON",savedUcs);doc.Editor.UpdateScreen();
        }
        Acad.SetSystemVariable("CURSORTYPE",savedCursorType);Acad.SetSystemVariable("PICKBOX",savedPickbox);
        Acad.SetSystemVariable("CURSORSIZE",savedCursor);Acad.SetSystemVariable("LWDISPLAY",savedLineweight);savedView.Dispose();
    }
}
