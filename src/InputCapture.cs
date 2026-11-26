using System.Runtime.InteropServices;
namespace CadDustLab;

// Host filters only consume input. A separate Win32 input thread receives fresh
// key/button transitions before they enter AutoCAD's UI queue.
internal sealed class InputCapture:IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private struct Point {public int X,Y;}
    [StructLayout(LayoutKind.Sequential)] private struct Rect {public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)] private struct Msg
    {public nint Hwnd;public uint Message;public nuint WParam;public nint LParam;public uint Time;public Point Position;public uint Private;}
    [StructLayout(LayoutKind.Sequential)] private struct CursorInfo {public int Size,Flags;public nint Cursor;public Point Position;}
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardEvent {public uint Key,Scan,Flags,Time;public nuint Extra;}
    [StructLayout(LayoutKind.Sequential)] private struct MouseEvent {public Point Position;public uint Data,Flags,Time;public nuint Extra;}
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return:MarshalAs(UnmanagedType.I1)] private delegate bool Filter(ref Msg message);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return:MarshalAs(UnmanagedType.I1)] private delegate bool FilterOperation(Filter callback);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] private static extern nint GetModuleHandle(string name);
    [DllImport("kernel32.dll",CharSet=CharSet.Ansi,ExactSpelling=true)] private static extern nint GetProcAddress(nint module,string name);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll")] private static extern uint GetTickCount();
    private static FilterOperation ResolveFilter(string operation)
    {
        nint module=GetModuleHandle("accore.dll");
        foreach(string pointer in new[]{"Q6A","P6A"})
        {
            nint address=GetProcAddress(module,"?aced"+operation+"FilterWinMsg@@YA_N"+pointer+"_NPEAUtagMSG@@@Z@Z");
            if(address!=0)return Marshal.GetDelegateForFunctionPointer<FilterOperation>(address);
        }
        throw new NotSupportedException("CS16: unsupported x64 input-filter export ("+operation+").");
    }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window,uint flags);
    [DllImport("user32.dll")] private static extern bool IsChild(nint parent,nint child);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint window,out Rect rectangle);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint window,ref Point point);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")] private static extern bool ClipCursor(ref Rect rectangle);
    [DllImport("user32.dll",EntryPoint="ClipCursor")] private static extern bool UnclipCursor(nint none);
    [DllImport("user32.dll")] private static extern nint SetCursor(nint cursor);
    [DllImport("user32.dll")] private static extern int ShowCursor(bool show);
    [DllImport("user32.dll")] private static extern bool GetCursorInfo(ref CursorInfo info);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window,out uint process);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern int GetMessageTime();
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate nint KeyboardHook(int code,nuint key,nint flags);
    [DllImport("user32.dll",SetLastError=true)] private static extern nint SetWindowsHookEx(int id,KeyboardHook callback,nint module,uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook,int code,nuint key,nint flags);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out Msg message,nint window,uint min,uint max,uint remove);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Msg message);
    [DllImport("user32.dll")] private static extern nint DispatchMessage(ref Msg message);
    [DllImport("user32.dll",SetLastError=true)] private static extern uint MsgWaitForMultipleObjectsEx(uint count,nint[] handles,uint milliseconds,uint wakeMask,uint flags);

    private readonly nint viewport,owner;
    private readonly Filter callback;
    private readonly FilterOperation Register,Remove;
    private readonly KeyboardHook keyboardCallback;
    private nint keyboardHook,previousCursor;
    private readonly PhysicalInput physical=new();
    private readonly CursorBalance cursor=new(ShowCursor);
    private readonly ManualResetEvent stopSampling=new(false);
    private readonly ManualResetEvent inputReady=new(false);
    private string? inputFailure;
    private long physicalEvents;
    private int lastPhysicalKey;
    private Thread? sampler;
    private CursorWindow? cursorWindow,mouseWindow;
    private Rect bounds;
    private Point center,baseline;
    private bool registered,disposed,cursorRefresh,hasCaptureRange;
    private uint captureBegin,releaseAt;
    private int focusLost;
    internal volatile bool MenuOpen,Suspended;
    internal FrameDiagnostics? Diagnostics;
    internal bool Captured {get;private set;}
    internal bool ResetRequested=>false;
    internal string ReleaseReason {get;private set;}="not started";
    internal int MovementPresses=>physical.MovementPresses;
    internal int ScorePresses=>physical.ScorePresses;
    internal int MouseSamples {get;private set;}
    internal int EscapeCount {get;private set;}
    internal int CaptureCount {get;private set;}
    internal int SuppressedMessages {get;private set;}
    internal int StaleMessages {get;private set;}
    internal double MaximumMessageAgeMs {get;private set;}
    internal int CursorMessages {get;private set;}
    internal int CursorRepairs {get;private set;}
    internal double MouseDistance {get;private set;}
    internal object InputStats()=>new{source="dedicated Win32 input thread; host messages suppress only",physical=physical.Stats(),physicalEvents=Interlocked.Read(ref physicalEvents),lastPhysicalKey,SuppressedMessages,StaleMessages,MaximumMessageAgeMs,CursorMessages,CursorRepairs,cursorHideDebt=cursor.Debt};

    internal InputCapture(nint viewport)
    {
        this.viewport=viewport;owner=GetAncestor(viewport,3);callback=OnMessage;keyboardCallback=OnKeyboard;
        Register=ResolveFilter("Register");Remove=ResolveFilter("Remove");
        try
        {
            registered=Register(callback);if(!registered)throw new InvalidOperationException("AutoCAD input filter registration failed.");
            keyboardHook=SetWindowsHookEx(2,keyboardCallback,0,GetCurrentThreadId());
            if(keyboardHook==0)throw new InvalidOperationException("AutoCAD keyboard hook failed: "+Marshal.GetLastWin32Error());
            cursorWindow=new CursorWindow(this,viewport);
            var initial=new bool[256];ReadPhysical(initial);physical.Sample(initial,KeyActions.NowMs);physical.Capture(false);
            sampler=new Thread(SampleLoop){IsBackground=true,Name="CS16 physical input"};sampler.Start();
            if(!inputReady.WaitOne(3000)||inputFailure is not null)throw new InvalidOperationException(inputFailure??"CS16 input thread did not start.");
        }
        catch{Dispose();throw;}
    }
    private static void ReadPhysical(bool[] state)
    {
        foreach(int key in PhysicalInput.Keys)state[key]=(GetAsyncKeyState(key)&0x8000)!=0;
        if(GetSystemMetrics(23)!=0){bool swap=state[1];state[1]=state[2];state[2]=swap;}
    }
    private void SampleLoop()
    {
        var state=new bool[256];var raw=new bool[256];nint keyHook=0,mouseHook=0;
        bool focused=false;
        void Prime()
        {
            ReadPhysical(state);for(int key=0xa0;key<=0xa5;key++)raw[key]=(GetAsyncKeyState(key)&0x8000)!=0;
            physical.Sample(state,KeyActions.NowMs);physical.Capture(false);
        }
        bool Active()=>!Suspended&&GetAncestor(GetForegroundWindow(),3)==owner;
        KeyboardHook keys=(code,message,data)=>
        {
            if(code>=0&&Active())
            {
                var e=Marshal.PtrToStructure<KeyboardEvent>(data);int key=(int)e.Key;
                if(key<256)
                {
                    bool down=message is 0x100 or 0x104;raw[key]=down;
                    if(key is 0xa0 or 0xa1){key=16;down=raw[0xa0]||raw[0xa1];}
                    else if(key is 0xa2 or 0xa3){key=17;down=raw[0xa2]||raw[0xa3];}
                    else if(key is 0xa4 or 0xa5){key=18;down=raw[0xa4]||raw[0xa5];}
                    state[key]=down;lastPhysicalKey=key;Interlocked.Increment(ref physicalEvents);
                    physical.Sample(state,KeyActions.NowMs-unchecked(GetTickCount()-e.Time));
                }
            }
            return CallNextHookEx(keyHook,code,message,data);
        };
        KeyboardHook mouse=(code,message,data)=>
        {
            if(code>=0&&message is 0x201 or 0x202 or 0x204 or 0x205 or 0x20a&&Active())
            {
                var e=Marshal.PtrToStructure<MouseEvent>(data);double time=KeyActions.NowMs-unchecked(GetTickCount()-e.Time);
                Interlocked.Increment(ref physicalEvents);
                if(message==0x20a)physical.Wheel(unchecked((short)(e.Data>>16)),time);
                else
                {
                    int key=message is 0x201 or 0x202?1:2;if(GetSystemMetrics(23)!=0)key=3-key;
                    state[key]=message is 0x201 or 0x204;physical.Sample(state,time);
                }
            }
            return CallNextHookEx(mouseHook,code,message,data);
        };
        try
        {
            // The hooks run on this message-pumping thread, never on AutoCAD's
            // render thread. Callbacks only update bounded in-memory input state.
            PeekMessage(out _,0,0,0,0);Prime();focused=Active();
            keyHook=SetWindowsHookEx(13,keys,GetModuleHandle("user32.dll"),0);
            mouseHook=SetWindowsHookEx(14,mouse,GetModuleHandle("user32.dll"),0);
            if(keyHook==0||mouseHook==0)throw new InvalidOperationException("CS16 physical input hook failed: "+Marshal.GetLastWin32Error());
            inputReady.Set();
            var waits=new[]{stopSampling.SafeWaitHandle.DangerousGetHandle()};
            while(true)
            {
                // Low-level hooks arrive as sent messages. Sleeping here delays
                // the system's input delivery, especially with a fast mouse.
                // Wake immediately for messages OR shutdown, with a focus check
                // every 16 ms even when no input is arriving. No global changes.
                uint wake=MsgWaitForMultipleObjectsEx(1,waits,16,0x04ff,4);
                if(wake==0)break;
                if(wake==uint.MaxValue)throw new InvalidOperationException("CS16 input wait failed: "+Marshal.GetLastWin32Error());
                bool foreground=Active();
                if(!foreground){physical.Capture(false);Interlocked.Exchange(ref focusLost,1);}
                else if(!focused)Prime();
                focused=foreground;
                int count=0;while(count++<64&&PeekMessage(out var message,0,0,0,1)){TranslateMessage(ref message);DispatchMessage(ref message);}
                if(foreground)physical.Sample(state,KeyActions.NowMs);
            }
        }
        catch(Exception ex){inputFailure=ex.Message;physical.Capture(false);Interlocked.Exchange(ref focusLost,1);inputReady.Set();}
        finally{if(keyHook!=0)UnhookWindowsHookEx(keyHook);if(mouseHook!=0)UnhookWindowsHookEx(mouseHook);GC.KeepAlive(keys);GC.KeepAlive(mouse);}
    }
    private bool Foreground=>GetAncestor(GetForegroundWindow(),3)==owner;
    private bool RefreshBounds()
    {
        if(!GetClientRect(viewport,out var client))return false;
        var start=new Point{X=client.Left,Y=client.Top};var end=new Point{X=client.Right,Y=client.Bottom};
        if(!ClientToScreen(viewport,ref start)||!ClientToScreen(viewport,ref end)||end.X-start.X<200||end.Y-start.Y<150)return false;
        bounds=new Rect{Left=start.X+3,Top=start.Y+3,Right=end.X-3,Bottom=end.Y-3};
        center=new Point{X=(bounds.Left+bounds.Right)/2,Y=(bounds.Top+bounds.Bottom)/2};return true;
    }
    internal bool Capture()
    {
        if(disposed||Suspended||Captured||!Foreground||!RefreshBounds())return Captured;
        if(!ClipCursor(ref bounds))return false;
        if(!SetCursorPos(center.X,center.Y)){UnclipCursor(0);return false;}
        GetCursorPos(out baseline);
        captureBegin=GetTickCount();hasCaptureRange=true;Interlocked.Exchange(ref focusLost,0);
        physical.Capture(true);Captured=true;CaptureCount++;ReleaseReason="captured";
        cursor.Hide();previousCursor=SetCursor(0);
        nint child=WindowFromPoint(center);
        if(child!=viewport&&IsChild(viewport,child)&&GetWindowThreadProcessId(child,out _)==GetCurrentThreadId())
            mouseWindow=new CursorWindow(this,child);
        return true;
    }
    internal void Release(string reason)
    {
        if(Captured){releaseAt=GetTickCount();UnclipCursor(0);}
        Captured=false;physical.Capture(false);ReleaseReason=reason;
        mouseWindow?.ReleaseHandle();mouseWindow=null;
        if(cursor.Debt>0){cursor.Restore();SetCursor(previousCursor);previousCursor=0;}
    }
    internal GameInput Read()
    {
        if(!Foreground||Suspended||Interlocked.Exchange(ref focusLost,0)!=0)
        {if(Captured)Release("focus lost");return new();}
        var sample=physical.Read(KeyActions.NowMs);
        if(sample.Actions.Contains(0x75)){if(Captured)Release("F6");else Capture();return new();}
        if(!Captured)return new();
        if(sample.Held[18]||sample.Held[0x5b]||sample.Held[0x5c]){Release("system shortcut");return new();}
        if(sample.Actions.Contains(27))
        {
            EscapeCount++;
            if(!MenuOpen){Release("escape");return new();}
        }
        if(sample.EscapeHeldMs>=650){Release("held Escape");return new();}
        if(sample.Actions.Length>0)Diagnostics?.Add("physical_action_to_tick",sample.AgeMs);
        var oldCenter=center;if(!RefreshBounds()){Release("viewport unavailable");return new();}ClipCursor(ref bounds);
        double dx=0,dy=0;
        if(GetCursorPos(out var p))
        {
            if(oldCenter.X!=center.X||oldCenter.Y!=center.Y){SetCursorPos(center.X,center.Y);GetCursorPos(out baseline);p=baseline;}
            dx=p.X-baseline.X;dy=p.Y-baseline.Y;
            if(dx!=0||dy!=0){MouseSamples++;MouseDistance+=Math.Sqrt(dx*dx+dy*dy);SetCursorPos(center.X,center.Y);GetCursorPos(out baseline);}
        }
        MaintainCursor();
        var keys=sample.Held;
        return new((keys[0x57]?1:0)-(keys[0x53]?1:0),(keys[0x44]?1:0)-(keys[0x41]?1:0),keys[16],dx,dy,
            keys[1]||sample.FireTap,sample.AimTap,sample.Actions,keys[32]||sample.JumpTap,keys[17],keys[9]){SpaceHeld=keys[32]};
    }
    internal void MaintainCursor()
    {
        if(!Captured||!Foreground||Suspended)return;
        var info=new CursorInfo{Size=Marshal.SizeOf<CursorInfo>()};
        if(GetCursorInfo(ref info)&&(info.Flags&1)!=0){CursorRepairs++;cursor.Hide();}
        SetCursor(0);
    }
    internal void RefreshNativeCursor()
    {
        cursorRefresh=true;
        if(Captured){SetCursorPos(center.X+1,center.Y);GetCursorPos(out baseline);}
    }
    private bool DuringCapture(uint time)=>hasCaptureRange&&unchecked(time-captureBegin)<=unchecked((Captured?GetTickCount():releaseAt)-captureBegin);
    private void ObserveMessage(uint time)
    {
        SuppressedMessages++;uint age=unchecked(GetTickCount()-time);
        MaximumMessageAgeMs=Math.Max(MaximumMessageAgeMs,age);if(age>KeyActions.MaximumAgeMs)StaleMessages++;
    }
    private nint OnKeyboard(int code,nuint key,nint flags)
    {
        try
        {
            if(code==0&&!disposed&&!Suspended&&Foreground)
            {
                int k=(int)(key&255);uint time=unchecked((uint)GetMessageTime());
                if(k is 0x12 or 0x5b or 0x5c){if(Captured)Release("system shortcut");}
                else if(Captured||k==0x75||DuringCapture(time)){ObserveMessage(time);return 1;}
            }
        }
        catch{Release("keyboard error");}
        return CallNextHookEx(keyboardHook,code,key,flags);
    }
    private bool OnMessage(ref Msg message)
    {
        Diagnostics?.ObserveHostMessage(unchecked((int)message.Message));
        try
        {
            if(disposed)return false;
            if(message.Message==0x1c&&message.WParam==0){Release("focus lost");return false;}
            if(!Foreground||Suspended){if(Captured)Release("focus lost");return false;}
            // Exactly one synthetic move lets CAD erase its green hardware cursor.
            if(message.Message==0x200&&cursorRefresh&&message.Position.X==center.X+1&&message.Position.Y==center.Y)
            {cursorRefresh=false;return false;}
            bool keyboard=message.Message is >=0x100 and <=0x107;
            bool mouse=message.Message is >=0x200 and <=0x20e;
            if(keyboard||mouse||message.Message==0x302)
            {
                bool f6=keyboard&&(message.WParam&255)==0x75;
                if(!Captured&&!f6&&!DuringCapture(message.Time))return false;
                ObserveMessage(message.Time);
                return true;
            }
            return false;
        }
        catch{Release("input error");return false;}
    }
    // WM_SETCURSOR is sent synchronously, so a GetMessage filter cannot reliably
    // intercept it. Subclass only the current document/viewport while playing.
    private sealed class CursorWindow:System.Windows.Forms.NativeWindow
    {
        private readonly InputCapture input;
        internal CursorWindow(InputCapture input,nint handle){this.input=input;AssignHandle(handle);}
        protected override void WndProc(ref System.Windows.Forms.Message message)
        {
            if(message.Msg==0x20&&input.Captured&&!input.Suspended&&input.Foreground)
            {input.CursorMessages++;SetCursor(0);message.Result=(nint)1;return;}
            base.WndProc(ref message);
        }
    }
    public void Dispose()
    {
        if(disposed)return;Release("stopped");disposed=true;
        stopSampling.Set();sampler?.Join(250);
        if(sampler is null||!sampler.IsAlive){stopSampling.Dispose();inputReady.Dispose();}
        cursorWindow?.ReleaseHandle();cursorWindow=null;
        if(keyboardHook!=0){UnhookWindowsHookEx(keyboardHook);keyboardHook=0;}
        if(registered){Remove(callback);registered=false;}
        GC.KeepAlive(callback);GC.KeepAlive(keyboardCallback);
    }
}
