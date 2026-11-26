using CadDustLab;
using System.Text.Json;

static class PolishChecks
{
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    public static void Run(Scene scene)
    {
        var player=new Player(scene);var initial=player.Eye;
        for(int i=0;i<20;i++)player.Move(scene,1,0,false,.01);
        Check(player.Speed>245&&player.Speed<=251,"Run did not reach 250 units/s");
        player.Reset(scene);for(int i=0;i<20;i++)player.Move(scene,1,0,true,.01);
        Check(player.Speed>125&&player.Speed<=131,"Shift must slow to 130 units/s");
        player.Reset(scene);for(int i=0;i<20;i++)player.Move(scene,1,1,false,.01,221);
        Check(player.Speed>215&&player.Speed<=222,"Weapon speed / diagonal normalization failed");
        player.Reset(scene);player.Move(scene,1,0,false,10);Check((player.Eye-initial).Length<21,"Long frame teleported player");
        player.Reset(scene);double maxZ=player.Eye.Z;
        for(int i=0;i<100;i++){player.Move(scene,0,0,false,.01,jump:true);maxZ=Math.Max(maxZ,player.Eye.Z);Check(scene.CanOccupy(player.Eye),"Jump entered solid");}
        Check(Math.Abs(maxZ-initial.Z-45)<.6&&player.Grounded&&Math.Abs(player.Eye.Z-initial.Z)<.15,"Jump height / landing / held-Space prevention failed");
        player.Move(scene,0,0,false,.01,jump:false);player.Move(scene,0,0,false,.01,jump:true);Check(!player.Grounded,"Second jump after release failed");
        player.Reset(scene);player.Move(scene,0,0,false,.02,duck:true);
        Check(player.Crouched&&Math.Abs(initial.Z-player.Eye.Z-34)<.2,"Crouch eye height / hull failed");
        player.Move(scene,0,0,false,.02);Check(!player.Crouched&&Math.Abs(player.Eye.Z-initial.Z)<.2,"Stand failed");
        for(int i=0;i<400;i++){player.Move(scene,0,1,false,.04);Check(scene.CanOccupy(player.Eye),"Wall penetration");}
        Check(player.BlockedSteps>0,"Wall check never hit wall");
        player.Reset(scene);
        for(double distance=20;distance<=scene.RouteLength;distance+=20)
        {
            var target=scene.Position(distance);
            for(int i=0;i<100;i++)
            {
                var d=target-player.Eye;if(new V(d.X,d.Y,0).Length<6)break;
                player.Look(Math.IEEERemainder(Math.Atan2(d.X,d.Y)-player.Yaw,2*Math.PI)/.0024,0);
                player.Move(scene,1,0,true,.012);Check(scene.CanOccupy(player.Eye),"Door route penetrated BSP");
            }
            Check((target-player.Eye).Length<10,$"Could not follow door route: {distance}, {player.Eye}");
        }
        player.Look(100000,100000);Check(Math.Abs(player.Pitch)<=1.45&&CompatibilityChecks.Finite(player.Yaw),"Look bounds");
        var keys=new KeyActions();var game=new Combat(scene);player.Reset(scene);
        for(int cycle=0;cycle<40;cycle++)
        {
            // No key-up event delivered between independent presses. Repeats are ignored.
            keys.Press(0x42,false);keys.Press(0x42,true);keys.Press(0x33,false);keys.Press(0x39,false);
            game.Step(.01,player,new(Actions:keys.Drain()));Check(game.Menu==3,"B3 then absent slot9 corrupted menu");
            keys.Press(0x42,false);game.Step(.01,player,new(Actions:keys.Drain()));Check(!game.MenuOpen,"B could not close repeated menu");
        }
        game.Step(.01,player,new(Actions:new[]{0x42,0x34,0x32}));
        Check(game.Weapon.Id=="ak47"&&game.Money==13500&&game.Reserve==0&&game.Ammo==30,"Weapon cost/default ammunition failed");
        game.Handle(0xbc);Check(game.Reserve==30&&game.Money==13420,"Ammo pack purchase failed");
        for(int i=0;i<5;i++)game.Handle(0xbc);Check(game.Reserve==90&&game.Money==13260,"Reserve cap or capped-purchase charge failed");
        var empty=new Combat(scene);empty.Buy(Combat.Catalog.First(w=>w.Id=="awp"));
        foreach(var bot in empty.Bots){bot.Health=0;bot.DeadAt=1e9;}
        for(int i=0;i<10;i++){for(int j=0;j<25;j++)empty.Step(.05,player,new());Check(empty.Fire(player.Camera),"AWP magazine consumption failed");}
        for(int j=0;j<100;j++)empty.Step(.05,player,new(Fire:true));
        Check(empty.Ammo==0&&empty.Reserve==0&&empty.Shots==10,"Reload fabricated ammunition");
        Check(empty.BuyAmmo(true)&&empty.Money==11125&&empty.Reserve==10,"AWP ammo price failed");
        empty.Reload();for(int j=0;j<60;j++)empty.Step(.05,player,new());
        Check(empty.Ammo==10&&empty.Reserve==0,"Reload did not transfer finite reserve");
        empty.Buy(Combat.Catalog.First(w=>w.Id=="m249"));int money=empty.Money;
        Check(!empty.Buy(Combat.Catalog.First(w=>w.Id=="m249"))&&empty.Money==money,"Duplicate purchase changed money");
        empty.Buy(Combat.Catalog.First(w=>w.Id=="g3sg1"));money=empty.Money;
        Check(!empty.Buy(Combat.Catalog.First(w=>w.Id=="awp"))&&empty.Money==money,"Insufficient funds accepted");
        var score=game.Snapshot() with{Score=true};Check(score.Scores.Length==10,"Scoreboard roster incomplete");
        File.WriteAllText("../../frame-score.json",JsonSerializer.Serialize(scene.Render(0,player.Camera,score)));
        Check(ViewModels.Weapons.Count==Combat.Catalog.Length,"Missing MDL viewmodel slides");
        var smg=new Combat(scene);smg.Step(.01,player,new(Actions:new[]{0x42,0x33,0x34}));
        Check(smg.Weapon.Id=="p90"&&!smg.MenuOpen&&smg.Money==13650,"B3-4 must buy the T-menu P90");
        var a=new List<ScreenPolygon>();var b=new List<ScreenPolygon>();ViewModels.Draw(a,score with{Time=1,MoveSpeed=0,Recoil=0});ViewModels.Draw(b,score with{Time=9,MoveSpeed=0,Recoil=0});
        Check(a.SequenceEqual(b),"Idle viewmodel is not static");
        var colors=new HashSet<int>{0xb6c6c6,0xc7d0c5,0xd6d5bf,0xdde0d5};
        string Sky(CameraPose c)=>JsonSerializer.Serialize(scene.Render(0,c).Polygons.TakeWhile(p=>colors.Contains(p.Color)));
        var pose=new CameraPose(initial,0,0);
        Check(Sky(pose)==Sky(pose with{Eye=initial+new V(300,400,50)}),"Sky has camera translation parallax");
        Check(Sky(pose)!=Sky(pose with{Yaw=.6})&&Sky(pose)!=Sky(pose with{Pitch=.4}),"Sky is attached to camera orientation");
        var nav=scene.Navigation;Check(nav.Areas.Count==736,"NAV data incomplete");
        foreach(var dest in new[]{new V(1152,2640,160),new V(-1850,2300,64)})
            Check(nav.Path(scene.TerroristSpawns[0],dest).Count>4&&nav.Path(scene.CounterSpawns[0],dest).Count>4,"NAV cannot connect spawns and bombsites");
        var bots=new Combat(scene);bots.Handle(0x42);var visits=bots.Bots.ToDictionary(b=>b.Id,b=>new HashSet<int>());
        for(int tick=0;tick<900;tick++)
        {
            bots.Step(.05,player,new());
            foreach(var bot in bots.Bots)
            {
                Check(scene.CanOccupy(bot.Eye),$"Bot {bot.Id} penetrated BSP");visits[bot.Id].Add(nav.Nearest(bot.Eye).Id);
                var gap=bot.Eye-player.Eye;
                Check(!bot.Alive||Math.Abs(gap.Z)>=60||new V(gap.X,gap.Y,0).Length>=39.99,$"Bot {bot.Id} walked through the player camera");
            }
        }
        foreach(var bot in bots.Bots)Console.WriteLine($"Bot {bot.Id}: travel {bot.Travel:F0}, areas {visits[bot.Id].Count}, eye {bot.Eye}");
        Check(bots.Bots.All(b=>b.Travel>600)&&visits.Values.All(v=>v.Count>=4),"Bots failed to navigate beyond local spawn");
        // Reproduce a teammate taking a route straight through a stationary camera at T spawn.
        player.Place(scene,scene.TerroristSpawns[0]);var crossing=new Combat(scene);
        foreach(var bot in crossing.Bots){bot.Health=0;bot.DeadAt=1e9;}
        var ally=crossing.Bots[5];V approach=new();
        foreach(var direction in new[]{new V(1,0,0),new V(-1,0,0),new V(0,1,0),new V(0,-1,0)})
            if(scene.TryWalk(player.Eye,direction*64,out var start)&&scene.Trace(player.Eye,start)>.999){approach=direction;ally.Eye=start;break;}
        Check(approach.Length>0,"No open teammate approach at T spawn");
        ally.Health=100;ally.Path=new(){player.Eye-approach*120};ally.RepathAt=1e9;
        for(int tick=0;tick<200;tick++)
        {
            crossing.Step(.05,player,new());var gap=ally.Eye-player.Eye;
            Check(Math.Abs(gap.Z)>=60||new V(gap.X,gap.Y,0).Length>=39.99,"Approaching teammate entered player camera");
        }
        Check(ally.Travel>5,"Player avoidance stopped all teammate movement");
        ally.Home=player.Eye;ally.Health=0;ally.DeadAt=-11;crossing.Step(.05,player,new());
        Check(!ally.Alive,"Teammate respawned inside player camera");
        Console.WriteLine("POLISH CHECKS PASSED: speeds, shift, jump, crouch, walls/door route, dropped-keyup menus, economy, finite ammo, score, slides, sky orientation, NAV, player avoidance and 45s bot movement.");
    }
}
