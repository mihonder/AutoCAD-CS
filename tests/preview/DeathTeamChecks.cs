using CadDustLab;
using System.Text.Json;

static class DeathTeamChecks
{
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    static void Advance(Combat game,Player player,double seconds,GameInput input=default)
    {for(double t=0;t<seconds-.00001;t+=.02)game.Step(Math.Min(.02,seconds-t),player,input);}
    static void Quiet(Combat game){foreach(var bot in game.Bots)bot.NextShot=1e9;}
    static void Balance(Combat game)
    {
        Check(game.Bots.Count==9&&game.Bots.Count(b=>b.Enemy)==5&&game.Bots.Count(b=>!b.Enemy)==4,"Team change broke 5v5 balance");
        Check(game.Bots.Count(b=>b.IsCt)+(game.PlayerTeam==Team.CT?1:0)==5,"CT does not have five players");
        Check(game.Bots.All(b=>b.Enemy==(b.IsCt!=(game.PlayerTeam==Team.CT))),"Absolute team confused with hostility");
        var scores=game.Snapshot().Scores;
        Check(scores.Count(s=>s.IsCt)==5&&scores.Count(s=>!s.IsCt)==5,"Scoreboard teams incorrect");
    }
    public static void Run(Scene scene)
    {
        var player=new Player(scene);var game=new Combat(scene,true);game.RestartRound(player);
        var actions=new KeyActions();
        foreach(var team in new[]{Team.CT,Team.T,Team.CT,Team.T})
        {
            int round=game.RoundNumber;
            actions.Press(0x4d,false);game.Step(.02,player,new(Actions:actions.Drain()));
            Check(game.Menu==7,"M did not open team menu");
            if(team==Team.CT)File.WriteAllText("../../frame-round-team-menu.json",JsonSerializer.Serialize(scene.Render(game.Time,player.Camera,game.Snapshot())));
            actions.Press(team==Team.CT?0x32:0x31,false);game.Step(.02,player,new(Actions:actions.Drain()));
            Check(game.PlayerTeam==team&&game.RoundNumber==round+1&&!game.MenuOpen,"Team selection did not restart on selected side");
            Balance(game);Quiet(game);
            Check(game.Bots.All(b=>b.Alive&&(b.Eye-player.Eye).Length>25),"Overlapping/dead players after team change");
            var spawn=scene.GroundNear((team==Team.CT?scene.CounterSpawns:scene.TerroristSpawns)[4]);
            Check((player.Eye-spawn).Length<.01,"Wrong team spawn");
            Check(game.Weapon.Id==(team==Team.CT?"usp":"glock")&&game.Ammo==(team==Team.CT?12:20),"Wrong starting pistol");
            game.Handle(0x4d);game.Handle(team==Team.CT?0x32:0x31);game.Step(.02,player,new());
            Check(game.RoundNumber==round+1,"Selecting current team needlessly restarted round");
        }
        foreach(var team in new[]{Team.T,Team.CT})
        {
            game.ChangeTeam(team,player);game.RestartRound(player,true);Quiet(game);Advance(game,player,3.1);
            player.Place(scene,scene.Position(0),0);game.Step(.02,player,new());
            var ally=game.Bots.First(b=>!b.Enemy);var enemy=game.Bots.First(b=>b.Enemy);
            ally.Eye=player.Eye+new V(0,80,0);enemy.Eye=player.Eye+new V(0,150,0);
            Check(scene.Trace(player.Eye,enemy.Eye)>.999,"Friendly-fire test fixture blocked");
            double hp=ally.Health;game.Fire(player.Camera);Check(ally.Health==hp,"Shot damaged teammate after changing side");
            ally.Eye=ally.Home;Advance(game,player,.3);enemy.Eye=player.Eye+new V(0,100,0);
            hp=enemy.Health;game.Fire(player.Camera);Check(enemy.Health<hp,"Could not damage opposite team");
            game.HurtPlayer(300,player);
            Check(game.Dead&&game.DeathScene.Active&&game.Snapshot().DeathCamera,"Death skipped third-person scene");
            Check(game.Snapshot().Actors.Single(a=>a.Id==0).IsCt==(team==Team.CT),"Death model does not match selected team");
            var position=player.Eye;var frames=new[]{.15,.4,.75,1.8};double elapsed=0;
            foreach(double at in frames)
            {
                Advance(game,player,at-elapsed,new(Forward:1,Dx:140,Dy:70));elapsed=at;
                Check((player.Eye-position).Length<.001&&game.DeathScene.Active,"Input moved player/skipped death scene");
                var frame=scene.Render(game.Time,game.RenderCamera(player),game.Snapshot());VectorBatch.Build(frame);
                Check(frame.Polygons.Any(p=>p.FaceId==-100),"Player body absent from death camera");
                File.WriteAllText($"../../frame-death-{team}-{at*100:000}.json",JsonSerializer.Serialize(frame));
            }
            game.Step(.02,player,new(Jump:true));game.Step(.02,player,new());
            Check(game.Observer.Mode==SpectatorMode.Chase,"Death-scene tap prematurely changed spectator mode");
            Advance(game,player,1);Check(!game.DeathScene.Active&&game.Dead,"Death scene did not finish/stay dead");
            Check(game.Snapshot().Actors.Any(a=>a.Id==0&&a.Fall==1),"Player corpse vanished after death scene");
            game.Step(.02,player,new(Jump:true));game.Step(.02,player,new());
            Check(game.Observer.Mode==SpectatorMode.Free,"Spectator modes did not resume after death scene");
            int round=game.RoundNumber;Advance(game,player,1.3,new(Jump:true){SpaceHeld=true});
            Check(!game.Dead&&game.RoundNumber==round+1&&!game.DeathScene.HasBody&&game.PlayerTeam==team,"Held restart left a corpse/wrong team");
            Balance(game);Quiet(game);Advance(game,player,3.1);
            game.Buy(Combat.Catalog.First(w=>w.Id=="awp"));int money=game.Money;
            foreach(var b in game.Bots.Where(b=>b.Enemy)){b.Health=0;b.DeadAt=game.Time;}
            game.Step(.02,player,new());
            Check(game.RoundResult=="ПОБЕДА "+team&&game.Money==Math.Min(16000,money+3250),"Winner/reward does not follow player team");
        }
        // Collision clearance, low ceilings, crouching and airborne deaths.
        int views=0;
        foreach(var location in scene.TerroristSpawns.Take(2).Concat(scene.CounterSpawns.Take(2)).Concat(new[]{scene.Position(0),new V(-1686,800,96)}))
        foreach(bool crouched in new[]{false,true})
        {
            player.Place(scene,location,.4);player.Move(scene,0,0,false,.02,jump:!crouched,duck:crouched);
            var death=new PlayerDeathScene();death.Begin(scene,player,Team.CT);
            for(int i=0;i<130;i++)
            {
                death.Step(scene,.02);var camera=death.Camera;
                Check(scene.Contents(camera.Eye)!=-2&&CompatibilityChecks.Finite(camera.Pitch)&&CompatibilityChecks.Finite(camera.Yaw),"Death camera entered solid/nonfinite angle");
                if(i%25==0)
                {
                    var snapshot=game.Snapshot() with{Dead=true,DeathCamera=true,Actors=new[]{death.Body}};
                    VectorBatch.Build(scene.Render(game.Time,camera,snapshot));views++;
                }
            }
        }
        game.RestartRound(player,true);Quiet(game);Advance(game,player,3.1);game.HurtPlayer(300,player);game.Step(.02,player,new());
        Advance(game,player,1.3,new(Jump:true){SpaceHeld=true});
        Check(!game.Dead&&!game.DeathScene.HasBody,"Held Space could not restart during death animation");
        game.HurtPlayer(300,player);game.Handle(0x4d);game.Handle(game.PlayerTeam==Team.T?0x32:0x31);game.Step(.02,player,new());
        Check(!game.Dead&&!game.DeathScene.HasBody,"Team switch during death did not clear corpse");Balance(game);
        Console.WriteLine($"CS16 CHECKS PASSED: M team switching/autobalance, spawns, pistols, friendly fire, team wins/rewards; T/CT death models, timed fall/hold/spectator, corpse cleanup, restart during death; {views} collision-safe camera views.");
    }
}
