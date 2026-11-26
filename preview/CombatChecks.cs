using CadDustLab;
using System.Text.Json;
using System.Diagnostics;

static class CombatChecks
{
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    static CameraPose Aim(V from,V to){var d=(to-from).Unit;return new(from,Math.Atan2(d.X,d.Y),Math.Asin(d.Z));}
    public static void Run(Scene scene)
    {
        Check(scene.FaceCount==4002,"Full BSP geometry was not loaded");
        var player=new Player(scene);var game=new Combat(scene);
        Check(game.Bots.Count==9&&game.Bots.Count(b=>b.Enemy)==5,"Expected 5 enemies, 4 teammates and player");
        Check(game.Bots.All(b=>scene.CanOccupy(b.Eye)),"Bot spawn inside solid");
        game.Step(.01,player,new(Actions:new[]{0x42,0x34,0x34}));
        Check(game.Weapon.Id=="sg552"&&!game.MenuOpen&&game.Ammo==30,"Ordered B-4-4 purchase failed");
        var view=game.Snapshot();
        foreach(var weapon in Combat.Catalog)
        foreach(double reload in new[]{0d,.5,.95})
        {
            var snapshot=view with{Weapon=weapon,Reload=reload,Recoil=1,Flash=true,HitMarker=true};
            var frame=scene.Render(0,player.Camera,snapshot);VectorBatch.Build(frame);
            Check(frame.Labels.Any(l=>l.Text.Contains("Деньги"))&&frame.Strokes.All(s=>s.Weight==30),"Native HUD/table data missing");
            if(reload==0)File.WriteAllText($"../frame-weapon-{weapon.Id}.json",JsonSerializer.Serialize(frame));
        }
        for(int category=0;category<=5;category++)
        {
            var frame=scene.Render(0,player.Camera,view with{Menu=category});VectorBatch.Build(frame);
            File.WriteAllText($"../frame-menu-{category}.json",JsonSerializer.Serialize(frame));
        }
        game=new Combat(scene);foreach(var b in game.Bots)b.NextShot=1e9;
        var target=game.Bots[0];var origin=player.Eye;
        target.Eye=scene.Position(120);target.Health=1;
        var aim=Aim(origin,target.Eye-new V(0,0,4));
        Check(scene.Trace(origin,target.Eye)>.999,"Test target hidden behind a wall");
        Check(game.Fire(aim)&&game.Kills==1&&!target.Alive,"Visible target not killed");
        Check(!game.Fire(aim)&&game.Shots==1,"Rate limiter allowed an immediate second shot");
        Check(game.Snapshot().Effects.Any(e=>e.Blood)&&game.Snapshot().HitMarker,"Hit feedback missing");
        for(int i=0;i<5;i++)game.Step(.06,player,new());
        Check(game.Snapshot().Actors[0].Fall>.4,"Death fall did not advance");
        File.WriteAllText("../frame-death.json",JsonSerializer.Serialize(scene.Render(.3,aim,game.Snapshot())));
        game.Handle(0x52);int before=game.Ammo;Check(!game.Fire(aim),"Shot allowed while reloading");
        for(int i=0;i<50;i++)game.Step(.06,player,new());
        Check(game.Ammo>before&&game.Ammo==game.Weapon.Magazine&&game.Reserve==39,"Finite reserve reload failed");
        game.Handle(0x42);Check(!game.Fire(aim),"Shot allowed in buy menu");game.Handle(27);Check(!game.MenuOpen,"Escape did not close menu");
        // A target behind the nearest BSP wall must not receive the shot.
        var blocked=new Combat(scene);foreach(var b in blocked.Bots){b.Health=0;b.DeadAt=0;}
        var rayEnd=origin+new V(4096,0,0);double wall=scene.Trace(origin,rayEnd);
        Check(wall>0&&wall<.9,"Wall test has no occluder");
        blocked.Bots[0].Health=100;blocked.Bots[0].Eye=origin+new V(wall*4096+80,0,0);
        blocked.Fire(new(origin,Math.PI/2,0));Check(blocked.Bots[0].Health==100&&blocked.Hits==0,"Bullet passed through a BSP wall");
        blocked=new Combat(scene);foreach(var b in blocked.Bots)b.Health=0;
        blocked.Bots[5].Health=100;blocked.Bots[5].Eye=scene.Position(90);
        blocked.Fire(Aim(origin,blocked.Bots[5].Eye-new V(0,0,4)));Check(blocked.Bots[5].Health==100&&blocked.Kills==0,"Friendly fire enabled");
        var landmarks=new[]{("a",new V(1152,2640,128),Math.PI),("b",new V(-1850,2300,64),0d),("mid",new V(-384,1100,64),0d),("t",scene.TerroristSpawns[0],0d),("ct",scene.CounterSpawns[0],Math.PI/2),("tunnels",new V(-1750,800,64),0d)};
        var costs=new List<double>();
        foreach(var (name,hint,yaw) in landmarks)
        {
            var eye=scene.GroundNear(hint);Check(scene.CanOccupy(eye),$"Invalid {name} standing position");
            Console.WriteLine($"Full map {name}: {eye}");
            for(int i=0;i<12;i++)
            {
                var sw=Stopwatch.StartNew();var frame=scene.Render(i,new(eye,yaw+i*Math.PI/6,0),view);VectorBatch.Build(frame);sw.Stop();costs.Add(sw.Elapsed.TotalMilliseconds);
                Check(frame.VisibleFaces>0,$"Missing map geometry at {name}/{i}");
                if(i==0)File.WriteAllText($"../frame-map-{name}.json",JsonSerializer.Serialize(frame));
            }
        }
        costs.Sort();Console.WriteLine($"COMBAT CHECKS PASSED: B44, {Combat.Catalog.Length} weapons / reload poses, 6 menus, fire rate, hit, kill, blood, fall, reload, wall occlusion, friendly fire; 72 full-map views p95 {costs[(int)(costs.Count*.95)]:F2} ms.");
    }
}
