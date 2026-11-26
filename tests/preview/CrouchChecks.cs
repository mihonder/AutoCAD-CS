using CadDustLab;
using System.Text.Json;

static class CrouchChecks
{
    public static void Run()
    {
        var scene=new Scene(File.ReadAllText("../../assets/scene.json"));
        using var source=JsonDocument.Parse(File.ReadAllText("../../assets/scene.json"));
        void Check(bool ok,string message){if(!ok)throw new Exception(message);}
        int ramps=0,blocked=0;double maxLift=0,maxDrift=0;
        foreach(var face in source.RootElement.GetProperty("faces").EnumerateArray())
        {
            var normal=V.Read(face.GetProperty("normal"));if(normal.Z<.7||normal.Z>.999)continue;
            var points=face.GetProperty("points").EnumerateArray().Select(V.Read).ToArray();
            var center=points.Aggregate(new V(),(a,b)=>a+b)*(1.0/points.Length);
            var player=new Player(scene);
            try{player.Place(scene,center+new V(0,0,90));}catch(InvalidOperationException){continue;}
            var eye=player.Eye;if(Math.Abs(eye.X-center.X)>2||Math.Abs(eye.Y-center.Y)>2)continue;
            ramps++;var target=eye-new V(0,0,34);
            if(!scene.CanOccupy(target,duck:true))
            {
                blocked++;double lift=0;while(lift<20&&!scene.CanOccupy(target+new V(0,0,lift),duck:true))lift+=.125;
                maxLift=Math.Max(maxLift,lift);
            }
            for(int cycle=0;cycle<12;cycle++)
            {
                player.Move(scene,0,0,false,.02,duck:true);
                Check(player.Crouched&&scene.CanOccupy(player.Eye,duck:true),"Ramp crouch rejected/inside floor: "+eye);
                var held=player.Eye;
                for(int tick=0;tick<10;tick++)player.Move(scene,0,0,false,.02,duck:true);
                Check((player.Eye-held).Length<.05,"Held Ctrl drifts on ramp: "+eye);
                player.Move(scene,0,0,false,.02);
                Check(!player.Crouched&&scene.CanOccupy(player.Eye),"Cannot stand after ramp crouch: "+eye+" held="+held+" now="+player.Eye+" grounded="+player.Grounded+" cycle="+cycle);
                double drift=(player.Eye-eye).Length;maxDrift=Math.Max(maxDrift,drift);
                Check(drift<.12,"Repeated crouches climb/sink: "+eye+" -> "+player.Eye);
            }
        }
        Check(ramps>100&&blocked>100,"Slope regression fixture missing");
        int ceilings=0;
        foreach(var face in source.RootElement.GetProperty("faces").EnumerateArray())
        {
            if(V.Read(face.GetProperty("normal")).Z<.999)continue;
            var points=face.GetProperty("points").EnumerateArray().Select(V.Read).ToArray();
            var foot=points.Aggregate(new V(),(a,b)=>a+b)*(1.0/points.Length);var eye=foot+new V(0,0,30.05);
            if(!scene.CanOccupy(eye,duck:true)||scene.CanOccupy(eye+new V(0,0,34))||scene.CanOccupy(eye-new V(0,0,.2),duck:true))continue;
            Check(!scene.TryStance(eye,false,true,out _),"Stood through a low ceiling: "+eye);ceilings++;
        }
        Check(ceilings>0,"No low-ceiling fixture found");
        var p=new Player(scene);p.Move(scene,0,0,false,.02,jump:true);p.Move(scene,0,0,false,.02,duck:true);
        Check(p.Crouched&&scene.CanOccupy(p.Eye,duck:true),"Airborne crouch failed");
        Console.WriteLine($"CROUCH CHECKS PASSED: {ramps} ramps / {blocked} previously blocked; 12 cycles each; max drift {maxDrift:F4}; {ceilings} low ceilings; airborne crouch.");
    }
}
