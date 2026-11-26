using CadDustLab;
using System.Text.Json;

static class Gameplay24Checks
{
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    static void Advance(Combat game,Player player,double seconds)
    {for(double t=0;t<seconds-.000001;t+=.02)game.Step(Math.Min(.02,seconds-t),player,new());}
    static void Advance(WeaponHandling h,WeaponSpec w,double seconds)
    {for(double t=0;t<seconds-.000001;t+=.01)h.Step(w,Math.Min(.01,seconds-t));}
    public static void Run()
    {
        var scene=new Scene(File.ReadAllText("../../assets/scene.json"),true);
        var crashed=new V(-417.3,2134.8,-98);
        bool originalFails=false;try{scene.GroundNear(crashed);}catch(InvalidOperationException){originalFails=true;}
        Check(originalFails,"Original death/drop crash no longer reproduced by fixture");
        var ground=scene.DropGround(crashed,0);
        Check(ground.Z<crashed.Z&&scene.Trace(ground,ground-new V(0,0,3))<1,"Crouched drop did not land on floor");
        foreach(var hint in new[]{crashed+new V(0,0,34),scene.Position(0),new V(-800,2320,-51.5),new V(1520,2448,99.9)})
        {
            var player=new Player(scene);var game=new Combat(scene,true);game.RestartRound(player);
            game.Buy(Combat.Catalog.Single(w=>w.Id=="ak47"));
            player.Place(scene,hint);player.Move(scene,0,0,false,.02,duck:true);Check(player.Crouched,"Crash test could not crouch");
            game.Step(.02,player,new());game.HurtPlayer(500,player);
            Check(game.Dead&&game.DeathScene.Active&&game.Drops.Count==2,"Crouched death did not finish/drop both weapons");
            foreach(var drop in game.Drops)
                Check(CompatibilityChecks.Finite(drop.Position.Z)&&scene.Trace(drop.Position,drop.Position-new V(0,0,3))<1,"Drop floats or is not finite");
            game.RestartRound(player,true);player.Place(scene,hint);player.Move(scene,0,0,false,.02,duck:true);game.Step(.02,player,new());
            Check(game.DropCurrent()&&game.Drops.Count==1,"Manual crouched drop failed");
        }
        var clockPlayer=new Player(scene);var clock=new Combat(scene,true);clock.RestartRound(clockPlayer);
        foreach(var bot in clock.Bots)bot.NextShot=1e9;
        Check(clock.Remaining==300&&Combat.RoundDuration==300,"Round not five minutes");
        Advance(clock,clockPlayer,3.01);Check(clock.CanFight&&clock.Remaining>299.9,"Freeze consumed live round time");
        Advance(clock,clockPlayer,299.8);Check(clock.RoundResult==""&&clock.Remaining>0,"Round ended early");
        Advance(clock,clockPlayer,.3);Check(clock.RoundResult.Length>0,"Round did not end on timeout");

        var ak=Combat.Catalog.Single(w=>w.Id=="ak47");var handling=new WeaponHandling();var origin=new CameraPose(new(0,0,64),0,0);
        var pattern=new List<object>();var rays=new List<V>();
        for(int i=0;i<30;i++)
        {
            double spread=handling.Spread(ak,0,true,false,false);var ray=handling.Direction(handling.Aim(origin),spread);rays.Add(ray);
            pattern.Add(new{shot=i+1,x=ray.X/ray.Y*1000,y=ray.Z/ray.Y*1000,spread,pitch=handling.Pitch*180/Math.PI});
            handling.Kick(ak,0,true,false);Advance(handling,ak,.1);
        }
        Check(rays.Distinct().Count()==30&&rays.Skip(10).Average(v=>v.Z)>rays.Take(3).Average(v=>v.Z)+.01,"AK spray stayed a single ray / no upward drift");
        Check(handling.Pitch<=5.5*Math.PI/180&&Math.Abs(handling.Yaw)<=1.8*Math.PI/180,"Recoil cap failed");
        Advance(handling,ak,3);Check(handling.Heat==0&&handling.Pitch<.0001,"Accuracy/view punch did not recover");
        foreach(var w in Combat.Catalog)
        {
            var h=new WeaponHandling();double calm=h.Spread(w,0,true,false,false);
            if(w.Shape=="knife")
            {h.Kick(w,250,false,false);Check(calm==0&&h.Pitch==0&&h.Direction(origin,0)==new V(0,1,0),"Knife acquired gun spread");continue;}
            Check(calm>0&&h.Spread(w,0,true,true,false)<calm&&h.Spread(w,250,true,false,false)>calm&&h.Spread(w,0,false,false,false)>calm,"Stance spread ordering: "+w.Id);
            if(w.Shape=="sniper")Check(h.Spread(w,0,true,false,true)<calm*.1,"Scoped rifle accuracy missing");
            var initial=h.Aim(origin);h.Kick(w,0,true,false);
            Check(h.Aim(origin).Pitch>initial.Pitch&&h.Spread(w,0,true,false,false)>calm,"Missing actual recoil/bloom: "+w.Id);
            for(int i=0;i<100;i++){var ray=h.Direction(h.Aim(origin),calm);Check(Math.Abs(ray.Length-1)<1e-8,"Invalid shot direction");}
            h.Reset();Check(h.Pitch==0&&h.Yaw==0&&h.Heat==0,"Weapon reset retained recoil");
        }
        var shooter=new Player(scene);var gun=new Combat(scene);gun.Bots.Clear();gun.Buy(ak);Advance(gun,shooter,.2);
        Check(gun.Fire(shooter.Camera),"AK integration shot rejected");
        Check(gun.RenderCamera(shooter).Pitch>shooter.Pitch&&gun.LastShotDirection!=new V(Math.Sin(shooter.Yaw),Math.Cos(shooter.Yaw),0),"Render camera / bullet ray not connected to handling");
        gun.SelectSlot(2);Check(gun.RenderCamera(shooter).Pitch==shooter.Pitch,"Switching kept prior gun recoil");
        gun.RestartRound(shooter,true);Check(gun.RenderCamera(shooter).Pitch==shooter.Pitch,"Round retained recoil");
        Directory.CreateDirectory("../../releases/compat24");
        File.WriteAllText("../../releases/compat24/ak-spray.json",JsonSerializer.Serialize(pattern,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("GAMEPLAY24 CHECKS PASSED: original crash reproduced and fixed; crouched death/manual drops; five-minute round; 22 gun profiles; stance/zoom/heat/recovery; actual rays and camera recoil; knife unaffected.");
    }
}
