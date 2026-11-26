namespace CadDustLab;

// Only physical high-bit key state enters here. Queued WM_KEY*/mouse messages
// never produce game actions. Sampling and consuming synchronize on a short lock.
internal sealed class PhysicalInput
{
    internal static readonly int[] Keys=new[]{0x75,27,0x42,0x48,0x4d,1,2,9,16,17,18,32,0x57,0x41,0x53,0x44,0x52,0x47,0x45,0x74,0xbc,0xbe,0x5b,0x5c}
        .Concat(Enumerable.Range(0x30,10)).Concat(Enumerable.Range(0x60,10)).ToArray();
    private readonly object gate=new();
    private readonly bool[] down=new bool[256],blocked=new bool[256];
    private readonly double[] taps=Enumerable.Repeat(double.NegativeInfinity,256).ToArray();
    private readonly KeyActions actions=new();
    private bool active;
    private double escapeAt,lastSample;
    internal long Samples {get;private set;}
    internal double MaxSampleGapMs {get;private set;}
    internal int MovementPresses {get;private set;}
    internal int ScorePresses {get;private set;}
    internal readonly record struct Snapshot(bool[] Held,int[] Actions,bool FireTap,bool AimTap,bool JumpTap,double EscapeHeldMs,double AgeMs);
    internal void Capture(bool enabled)
    {
        lock(gate){active=enabled;actions.Clear();foreach(int key in Keys){blocked[key]=down[key];taps[key]=double.NegativeInfinity;}}
    }
    internal void Sample(bool[] physical,double now)
    {
        lock(gate)
        {
            if(lastSample!=0)MaxSampleGapMs=Math.Max(MaxSampleGapMs,now-lastSample);lastSample=now;Samples++;
            foreach(int key in Keys)
            {
                bool value=physical[key],pressed=value&&!down[key];down[key]=value;
                if(!value)blocked[key]=false;
                if(!pressed||blocked[key]||(!active&&key!=0x75))continue;
                taps[key]=now;
                if(key==27)escapeAt=now;
                if(key is 0x57 or 0x41 or 0x53 or 0x44)MovementPresses++;
                if(key==9)ScorePresses++;
                actions.Press(key,false,now);
            }
        }
    }
    internal void Wheel(int delta,double now){lock(gate)if(active)actions.Wheel(delta,now);}
    internal Snapshot Read(double now)
    {
        lock(gate)
        {
            var held=new bool[256];foreach(int key in Keys)held[key]=down[key]&&!blocked[key];
            bool Tap(int key)=>now-taps[key]<=80;
            var result=new Snapshot(held,actions.Drain(now),Tap(1),Tap(2),Tap(32),held[27]?now-escapeAt:0,actions.LastAgeMs);
            foreach(int key in Keys)taps[key]=double.NegativeInfinity;
            return result;
        }
    }
    internal object Stats(){lock(gate)return new{samples=Samples,maxSampleGapMs=MaxSampleGapMs,actionPeak=actions.Peak,expiredActions=actions.Expired,overflowActions=actions.Overflow};}
}

// ShowCursor is a counter, not a boolean. Own every decrement and repay only
// those decrements on release, even when the initial counter was already > 0.
internal sealed class CursorBalance
{
    private readonly Func<bool,int> show;
    internal int Debt {get;private set;}
    internal CursorBalance(Func<bool,int> show)=>this.show=show;
    internal void Hide()
    {
        while(Debt<32){int count=show(false);Debt++;if(count<0)return;}
    }
    internal void Restore(){while(Debt>0){show(true);Debt--;}}
}
