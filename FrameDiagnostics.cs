using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.IO;
namespace CadDustLab;

internal sealed class FrameDiagnostics
{
    private sealed class Metric
    {
        internal readonly double[] Values=new double[8192];
        internal long Count;internal double Total,Max;
        internal void Add(double ms){lock(this){Values[Count++%Values.Length]=ms;Total+=ms;Max=Math.Max(Max,ms);}}
        internal object Snapshot(){lock(this){var sorted=Values.Take((int)Math.Min(Count,Values.Length)).OrderBy(x=>x).ToArray();return new{count=Count,meanMs=Total/Math.Max(1,Count),maxMs=Max,p95Ms=sorted.Length==0?0:sorted[(int)((sorted.Length-1)*.95)]};}}
    }
    private readonly Dictionary<string,Metric> metrics=new();
    private readonly Dictionary<int,long> hostMessages=new();
    private readonly List<object> gaps=new();
    private long lastEnd,lastEndCpu,lastInputCount,lastProbeCount;
    private TimeSpan? lastGc;
    private bool lastActive;
    internal long LatestPrepared,ProbeCount,LastProbe,MaxProbeDelayTicks,InputMessages;
    internal string CurrentPhase="outside tick";
    private readonly long started=Stopwatch.GetTimestamp();
    internal readonly string Mode;
    [DllImport("kernel32.dll")] private static extern nint GetCurrentThread();
    [DllImport("kernel32.dll")] private static extern bool GetThreadTimes(nint thread,out long created,out long exited,out long kernel,out long user);
    private static long Cpu(){GetThreadTimes(GetCurrentThread(),out _,out _,out long kernel,out long user);return kernel+user;}
    internal FrameDiagnostics(string mode)=>Mode=mode;
    internal void ObserveHostMessage(int id)
    {
        InputMessages++;hostMessages.TryGetValue(id,out long count);hostMessages[id]=count+1;
    }
    internal void Add(string name,double ms)
    {
        Metric metric;lock(metrics){if(!metrics.TryGetValue(name,out metric!))metrics.Add(name,metric=new());}metric.Add(ms);
    }
    internal Scope Measure(string name,bool ui=true)=>new(this,name,ui);
    internal readonly struct Scope:IDisposable
    {
        private readonly FrameDiagnostics log;private readonly string name,previous;private readonly long stamp;private readonly bool ui;
        internal Scope(FrameDiagnostics log,string name,bool ui){this.log=log;this.name=name;this.ui=ui;previous=log.CurrentPhase;stamp=Stopwatch.GetTimestamp();if(ui)log.CurrentPhase=name;}
        public void Dispose(){log.Add(name,Compat.Elapsed(stamp).TotalMilliseconds);if(ui)log.CurrentPhase=previous;}
    }
    internal void BeginTick(bool active)
    {
        long now=Stopwatch.GetTimestamp(),cpu=Cpu();
        var gc=Compat.GcPause;
        long probeMax=Interlocked.Exchange(ref MaxProbeDelayTicks,0);
        if(lastEnd!=0&&active&&lastActive)
        {
            double gap=Compat.Elapsed(lastEnd,now).TotalMilliseconds;Add("outside_game_callback",gap);
            if(gap>=65&&gaps.Count<512)gaps.Add(new{
                atSeconds=Compat.Elapsed(started,now).TotalSeconds,gapMs=gap,
                uiThreadCpuMs=(cpu-lastEndCpu)/10000.0,gcPauseMs=(gc-lastGc)?.TotalMilliseconds,
                probeReplies=ProbeCount-lastProbeCount,probeMaxDelayMs=probeMax*1000.0/Stopwatch.Frequency,
                latestPreparedAgeMs=LatestPrepared==0?-1:Compat.Elapsed(LatestPrepared,now).TotalMilliseconds,
                inputMessages=InputMessages-lastInputCount
            });
        }
        lastActive=active;CurrentPhase="tick";
    }
    internal void EndTick(){CurrentPhase="outside tick";lastEnd=Stopwatch.GetTimestamp();lastEndCpu=Cpu();lastGc=Compat.GcPause;lastProbeCount=ProbeCount;lastInputCount=InputMessages;}
    internal void OnProbe(long sent)
    {
        long now=Stopwatch.GetTimestamp(),delay=now-sent;
        LastProbe=now;ProbeCount++;if(delay>MaxProbeDelayTicks)MaxProbeDelayTicks=delay;
        Add("posted_message_delivery",delay*1000.0/Stopwatch.Frequency);
    }
    internal string Snapshot()
    {
        Dictionary<string,object> stats;lock(metrics)stats=metrics.ToDictionary(x=>x.Key,x=>x.Value.Snapshot());
        var messageTypes=hostMessages.OrderByDescending(x=>x.Value).Take(20).ToDictionary(x=>"0x"+x.Key.ToString("X4"),x=>x.Value);
        return JsonSerializer.Serialize(new{mode=Mode,seconds=Compat.Elapsed(started).TotalSeconds,probeReplies=ProbeCount,inputMessages=InputMessages,hostMessageTypes=messageTypes,metrics=stats,gaps=gaps.ToArray(),note="Nested timings overlap. UpdateScreen return is not a GPU Present measurement; outside gaps exclude our callback. inputMessages counts ALL CAD-filter messages; hostMessageTypes identifies them. Posted delivery is the single bounded frame wakeup."},new JsonSerializerOptions{WriteIndented=true});
    }
    internal void Save(string directory)
    {
        string text=Snapshot(),path=Path.Combine(directory,"pipeline-"+Mode+".json");
        _=Task.Run(()=>{try{File.WriteAllText(path,text);}catch(IOException){}catch(UnauthorizedAccessException){}});
    }
}

// WM_TIMER can starve for seconds while CAD handles input/paint messages.
// Post at most ONE wakeup, including while a callback pumps nested messages.
// Input is sampled on its own thread; it does not depend on this queue's order.
internal sealed class MessagePulse:System.Windows.Forms.NativeWindow,IDisposable
{
    private const int Pulse=0x8000+473;
    [DllImport("user32.dll",SetLastError=true)] private static extern bool PostMessage(nint hwnd,int msg,nint wparam,nint lparam);
    private readonly object gate=new();
    private readonly ManualResetEvent stop=new(false);
    private readonly nint target;
    private readonly FrameDiagnostics log;
    private readonly Action action;
    private bool stopped,pending;
    private long previous,sent;
    internal volatile bool DrivesFrames;
    internal long PostedCount {get;private set;}
    internal long DeliveredCount {get;private set;}
    internal MessagePulse(FrameDiagnostics log,Action action)
    {
        this.log=log;this.action=action;
        CreateHandle(new System.Windows.Forms.CreateParams{Parent=(nint)(-3),Caption="CS16 bounded frame pulse"});target=Handle;
        try{new Thread(Run){IsBackground=true,Name="CS16 frame wakeup"}.Start();}
        catch{stop.Dispose();DestroyHandle();throw;}
    }
    private void Run()
    {
        try
        {
            while(!stop.WaitOne(16))
            {
                lock(gate)
                {
                    if(stopped)return;
                    if(pending||!DrivesFrames)continue;
                    pending=true;sent=Stopwatch.GetTimestamp();
                    if(PostMessage(target,Pulse,0,0))PostedCount++;
                    else{pending=false;log.Add("frame_post_failure",1);}
                }
            }
        }
        finally{lock(gate)stop.Dispose();}
    }
    protected override void WndProc(ref System.Windows.Forms.Message m)
    {
        if(m.Msg==Pulse)
        {
            long stamp;
            lock(gate){if(stopped||!pending)return;stamp=sent;DeliveredCount++;}
            log.OnProbe(stamp);
            long now=Stopwatch.GetTimestamp();if(previous!=0)log.Add("ui_pulse_interval",Compat.Elapsed(previous,now).TotalMilliseconds);previous=now;
            try{if(DrivesFrames)action();}
            finally{lock(gate)pending=false;}
            return;
        }
        base.WndProc(ref m);
    }
    public void Dispose()
    {
        // Serialize the last PostMessage with handle destruction. Never wait on
        // the UI thread for the worker (CAD may be inside a nested message pump).
        lock(gate){if(stopped)return;stopped=true;DrivesFrames=false;stop.Set();DestroyHandle();}
    }
}
