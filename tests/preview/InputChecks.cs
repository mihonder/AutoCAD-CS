using CadDustLab;

static class InputChecks
{
    public static void Run()
    {
        void Check(bool ok,string error){if(!ok)throw new Exception(error);}
        var input=new PhysicalInput();var state=new bool[256];double now=1000;
        input.Sample(state,now);input.Capture(true);
        void Edge(int key){state[key]=true;input.Sample(state,now+=4);state[key]=false;input.Sample(state,now+=4);}
        state[0x57]=true;input.Sample(state,now+=4);
        Edge(0x31);var first=input.Read(now+4);Check(first.Held[0x57]&&first.Actions.SequenceEqual(new[]{0x31}),"W swallowed primary selection");
        Edge(0x32);var second=input.Read(now+4);Check(second.Held[0x57]&&second.Actions.SequenceEqual(new[]{0x32}),"W swallowed pistol selection");
        Edge(0x42);Edge(0x34);Edge(0x34);Check(input.Read(now).Actions.SequenceEqual(new[]{0x42,0x34,0x34}),"B-4-4 was coalesced/reordered");
        state[0x52]=true;for(int i=0;i<20;i++)input.Sample(state,now+=4);
        Check(input.Read(now).Actions.SequenceEqual(new[]{0x52}),"Held reload repeats");
        Check(input.Read(now+4).Actions.Length==0,"Reload replayed on next frame");state[0x52]=false;input.Sample(state,now+=4);
        // Many short clicks can create at most one fresh pulse, never a backlog.
        for(int i=0;i<8;i++)Edge(1);
        var click=input.Read(now);Check(click.FireTap&&!click.Held[1],"Short mouse click lost");
        Check(!input.Read(now+16).FireTap,"Click replayed after mouse release");
        Edge(1);Edge(0x52);Edge(0x31);var stale=input.Read(now+1000);
        Check(!stale.FireTap&&stale.Actions.Length==0,"Stalled host replayed stale clicks/actions");
        Edge(32);Check(input.Read(now).JumpTap,"Short jump lost");Check(!input.Read(now+16).JumpTap,"Jump repeated");
        Edge(2);Check(input.Read(now).AimTap,"Scope click lost");Check(!input.Read(now+16).AimTap,"Scope repeated");
        state[1]=true;state[0x57]=true;input.Sample(state,now+=4);input.Capture(false);input.Capture(true);
        var recapture=input.Read(now);Check(!recapture.Held[1]&&!recapture.Held[0x57]&&!recapture.FireTap,"Recapture fires/moves on already-held input");
        state[1]=false;state[0x57]=false;input.Sample(state,now+=4);Edge(1);Check(input.Read(now).FireTap,"Release did not rearm click");
        var queue=new KeyActions();for(int i=0;i<10000;i++)queue.Press(0x31,false,now);
        Check(queue.Drain(now).Length==KeyActions.Capacity&&queue.Overflow==10000-KeyActions.Capacity,"Unbounded action queue");
        queue.Press(0x52,false,now);Check(queue.Drain(now+201).Length==0&&queue.Expired==1,"Stale action survived");
        for(int initial=-3;initial<=5;initial++)
        {
            int count=initial;var cursor=new CursorBalance(show=>show?++count:--count);
            cursor.Hide();Check(count<0,"Windows cursor counter remained visible");cursor.Restore();Check(count==initial&&cursor.Debt==0,"Cursor count not restored");
        }
        Console.WriteLine("INPUT21 CHECKS PASSED: W+1/2, ordered B44, no reload repeat, bounded/expired actions, no click replay, jump/scope edges, recapture rearming, balanced cursor visibility.");
    }
}
