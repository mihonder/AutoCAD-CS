using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using CadDustLab;

// No AutoCAD, visible windows, focus changes, cursor operations, keyboard hooks
// or injected input. Only this process's own HWND_MESSAGE handles are exercised.
internal static class Program
{
    [StructLayout(LayoutKind.Sequential)] private struct Msg
    {public nint Hwnd;public uint Message;public nuint WParam;public nint LParam;public uint Time;public int X,Y;public uint Private;}
    [DllImport("user32.dll")] private static extern bool PeekMessage(out Msg msg,nint hwnd,uint min,uint max,uint flags);
    [DllImport("user32.dll")] private static extern nint DispatchMessage(ref Msg msg);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint hwnd,int msg,nint w,nint l);
    [DllImport("user32.dll")] private static extern nuint SetTimer(nint hwnd,nuint id,uint ms,nint callback);
    [DllImport("user32.dll")] private static extern bool KillTimer(nint hwnd,nuint id);
    private static void Check(bool ok,string error){if(!ok)throw new Exception(error);}
    private static void Pump(int ms)
    {
        var clock=Stopwatch.StartNew();
        while(clock.ElapsedMilliseconds<ms)
        {if(PeekMessage(out var msg,0,0,0,1))DispatchMessage(ref msg);else Thread.Sleep(1);}
    }
    private sealed class BusyQueue:NativeWindow,IDisposable
    {
        internal bool Busy=true;internal int Messages;
        internal BusyQueue(){CreateHandle(new CreateParams{Parent=(nint)(-3)});PostMessage(Handle,0x8001,0,0);}
        protected override void WndProc(ref Message m)
        {
            if(m.Msg==0x8001){Messages++;if(Busy){PostMessage(Handle,0x8001,0,0);Thread.Sleep(1);}return;}
            base.WndProc(ref m);
        }
        public void Dispose(){Busy=false;DestroyHandle();}
    }
    private sealed class OldTimer:NativeWindow,IDisposable
    {
        private readonly nuint timer;internal int Frames;
        internal OldTimer(){CreateHandle(new CreateParams{Parent=(nint)(-3)});timer=SetTimer(Handle,1,16,0);Check(timer!=0,"SetTimer failed");}
        protected override void WndProc(ref Message m){if(m.Msg==0x113){Frames++;return;}base.WndProc(ref m);}
        public void Dispose(){KillTimer(Handle,timer);DestroyHandle();}
    }
    [STAThread] private static void Main()
    {
        // Reproduce the former mechanism: no ticks while the posted queue stays
        // nonempty. The same production MessagePulse must keep making progress.
        using var old=new OldTimer();using var flood=new BusyQueue();
        int frames=0,depth=0,maxDepth=0;long previous=0;double maxGap=0;
        var diagnostics=new FrameDiagnostics("message-pump-test"){LatestPrepared=Stopwatch.GetTimestamp()};
        using var pulse=new MessagePulse(diagnostics,()=>
        {
            depth++;maxDepth=Math.Max(maxDepth,depth);
            long now=Stopwatch.GetTimestamp();if(previous!=0)maxGap=Math.Max(maxGap,Compat.Elapsed(previous,now).TotalMilliseconds);previous=now;
            frames++;Thread.Sleep(3);depth--;
        }){DrivesFrames=true};
        Pump(1800);
        Console.WriteLine($"Measured queue: {flood.Messages} messages, {old.Frames} timer ticks, {frames} posted frames; max gap {maxGap:F1} ms");
        Check(flood.Messages>20,"Queue fixture did not stay busy");
        Check(old.Frames==0,"Fixture failed to reproduce WM_TIMER starvation");
        Check(frames>=15,"Posted frames starved with the old timer");
        Check(maxGap<500,"Posted frame gap exceeded 500 ms");
        Console.WriteLine($"STARVATION: old timer {old.Frames} frames; fixed {frames}; max gap {maxGap:F1} ms; queue messages {flood.Messages}");
        flood.Busy=false;Pump(80);Check(old.Frames>0,"Baseline timer itself was broken");
        pulse.DrivesFrames=false;Pump(80);

        int nestedFrames=0,nestedDepth=0,nestedMax=0;bool waitInside=true;
        using var nested=new MessagePulse(diagnostics,()=>
        {
            nestedDepth++;nestedMax=Math.Max(nestedMax,nestedDepth);nestedFrames++;
            if(waitInside){waitInside=false;Pump(220);} // CAD can pump messages inside UpdateScreen.
            nestedDepth--;
        }){DrivesFrames=true};
        Pump(400);Check(nestedMax==1&&nestedFrames>1,"Reentrant callback or no recovery");
        nested.DrivesFrames=false;Pump(80);
        long posts=nested.PostedCount;
        nested.DrivesFrames=true;Thread.Sleep(450); // An actual stalled UI thread.
        Check(nested.PostedCount-posts==1,"Frame wakeups accumulated during a blocked UI thread");
        int before=nestedFrames;Pump(4);Check(nestedFrames-before<=1,"Catch-up burst after UI stall");
        Pump(80);Check(nestedFrames>before,"No recovery after UI stall");
        nested.Dispose();int stopped=nestedFrames;Pump(80);Check(nestedFrames==stopped,"Callback after disposal");
        Console.WriteLine($"LIFECYCLE: nested depth {nestedMax}; single pending wakeup through 450 ms stall; no catch-up burst; dispose OK.");
        Check(maxDepth==1,"Unexpected normal callback recursion");
        Console.WriteLine("MESSAGE PUMP CHECKS PASSED (no AutoCAD or desktop interaction)");
    }
}
