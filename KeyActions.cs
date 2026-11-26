namespace CadDustLab;

// Short ordered menu sequences (including B-4-4) survive one frame. No action
// may be replayed seconds later, and a stalled host cannot grow this mailbox.
public sealed class KeyActions
{
    public const int NextWeapon=0x1000,PreviousWeapon=0x1001;
    public const int Capacity=64;
    public const double MaximumAgeMs=200;
    private readonly Queue<(int Key,double Time)> pending=new();
    private int wheelRemainder;
    public int Expired {get;private set;}
    public int Overflow {get;private set;}
    public int Peak {get;private set;}
    public double LastAgeMs {get;private set;}
    public static double NowMs=>System.Diagnostics.Stopwatch.GetTimestamp()*(1000d/System.Diagnostics.Stopwatch.Frequency);
    public static int Normalize(int key)=>key>=0x60&&key<=0x69?key-0x30:key;
    public static bool IsAction(int key)=>key is 27 or 0x42 or 0x47 or 0x48 or 0x4d or 0x45 or 0x52 or 0x74 or 0x75 or 0xbc or 0xbe or >=0x30 and <=0x39;
    private void Add(int key,double time)
    {
        if(pending.Count==Capacity){pending.Dequeue();Overflow++;}
        pending.Enqueue((key,time));Peak=Math.Max(Peak,pending.Count);
    }
    public void Wheel(int delta,double? time=null)
    {
        wheelRemainder+=Compat.Clamp(delta,-960,960);double stamp=time??NowMs;
        while(wheelRemainder>=120){Add(PreviousWeapon,stamp);wheelRemainder-=120;}
        while(wheelRemainder<=-120){Add(NextWeapon,stamp);wheelRemainder+=120;}
    }
    public void Press(int key,bool repeat,double? time=null)
    {
        if(repeat)return;
        key=Normalize(key);if(IsAction(key))Add(key,time??NowMs);
    }
    public int[] Drain(double? time=null)
    {
        double now=time??NowMs;var result=new List<int>(pending.Count);LastAgeMs=0;
        while(pending.Count>0)
        {
            var item=pending.Dequeue();double age=Math.Max(0,now-item.Time);
            if(age>MaximumAgeMs){Expired++;continue;}
            result.Add(item.Key);LastAgeMs=Math.Max(LastAgeMs,age);
        }
        return result.ToArray();
    }
    public void Clear(){pending.Clear();wheelRemainder=0;}
}
