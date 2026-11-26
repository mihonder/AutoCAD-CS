using CadDustLab;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

static class RoundChecks
{
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    public static void CheckUnchangedWeaponPoses()
    {
        // Hashes of the original draft14 idle/fire poses keep this regression
        // check independent of historical DLLs outside the repository.
        using var baseline=JsonDocument.Parse(File.ReadAllText("fixtures/weapon-poses-draft14.json"));
        using var sha=SHA256.Create();
        using var file=File.OpenRead("../weapon-slides.json.gz");using var zip=new GZipStream(file,CompressionMode.Decompress);using var current=JsonDocument.Parse(zip);
        foreach(var weapon in Combat.Catalog)
        {
            foreach(string pose in new[]{"idle","fire"})
            {
                string raw=current.RootElement.GetProperty("weapons").GetProperty(weapon.Id).GetProperty(pose).GetRawText();
                string digest=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(raw))).Replace("-","").ToLowerInvariant();
                Check(digest==baseline.RootElement.GetProperty(weapon.Id).GetProperty(pose).GetString(),"Weapon idle/fire pose changed: "+weapon.Id);
            }
            for(int i=0;i<5;i++)Check(current.RootElement.GetProperty("weapons").GetProperty(weapon.Id).TryGetProperty("reload"+i,out _),"Missing reload pose");
        }
    }
    static void Advance(Combat game,Player player,double seconds,GameInput input=default){for(double t=0;t<seconds-.00001;t+=.02)game.Step(Math.Min(.02,seconds-t),player,input);}
    static void Save(string name,Scene scene,Player player,Combat game)
    {
        var frame=scene.Render(game.Time,game.RenderCamera(player),game.Snapshot());VectorBatch.Build(frame);
        File.WriteAllText("../frame-round-"+name+".json",JsonSerializer.Serialize(frame));
    }
    public static void Run()
    {
        var scene=new Scene(File.ReadAllText("../scene.json"),true);var player=new Player(scene);
        foreach(var hint in scene.TerroristSpawns.Take(3).Concat(scene.CounterSpawns.Take(2)).Concat(new[]{scene.Position(0)}))
        {
            player.Place(scene,hint);var initial=player.Eye;
            for(int i=0;i<3000;i++){player.Look(13*Math.Sin(i*.3),Math.Cos(i*.2));player.Move(scene,0,0,false,.016);}
            Check((initial-player.Eye).Length<.00001,"Mouse-only stationary camera drift: "+(player.Eye-initial));
        }
        player.Reset(scene);player.Move(scene,1,0,false,.08);player.StopMotion();var stopped=player.Eye;
        player.Move(scene,0,0,false,.05);Check((stopped-player.Eye).Length<.001,"Capture release retained velocity");
        CheckUnchangedWeaponPoses();Check(DroppedWeapons.Count==22,"Missing world gun contours");
        var game=new Combat(scene,true);game.RestartRound(player);Check(!game.CanFight&&game.Bots.All(b=>b.Alive)&&game.Health==100,"Round initial state");
        Check((player.Eye-game.Bots[5].Eye).Length>25,"Player shares teammate spawn");
        foreach(var b in game.Bots)b.NextShot=1e9;
        Advance(game,player,3.1);Check(game.CanFight,"Freeze never ended");
        game.Handle(0x48);Check(game.Menu==6,"H did not open difficulty");Save("difficulty",scene,player,game);game.Handle(0x34);Check(game.Difficulty==3&&!game.MenuOpen,"Difficulty selection failed");
        Check(Combat.BotSkills[3].Reaction<Combat.BotSkills[1].Reaction&&Combat.BotSkills[3].Accuracy>Combat.BotSkills[1].Accuracy,"Difficulty does not improve reactions/aim");
        game.Buy(Combat.Catalog.First(w=>w.Id=="ak47"));game.BuyAmmo(true);game.Fire(player.Camera);int ammo=game.Ammo,spare=game.Reserve;
        Check(game.DropCurrent()&&!game.Owns("ak47")&&game.Weapon.Id=="glock","G did not free primary slot");
        var drop=game.Drops.Single();Check(drop.Magazine==ammo&&drop.Reserve==spare,"Drop duplicated/lost ammo");
        Check(!game.PickupNearby(false),"Drop instantly re-picked itself");
        Advance(game,player,1.3);Check(!game.Owns("ak47"),"Stationary player re-picked dropped gun");
        player.Place(scene,drop.Position+new V(0,0,64));Advance(game,player,1.3);
        Check(game.Owns("ak47")&&game.Ammo==ammo&&game.Reserve==spare&&game.Drops.Count==0,"Automatic pickup failed/ammo changed");
        // Select every slot after a pickup, repeatedly, through the same action queue.
        var actions=new KeyActions();
        for(int i=0;i<12;i++)
        {
            actions.Press(0x32,false);game.Step(.02,player,new(Actions:actions.Drain()));Check(game.Weapon.Id=="glock"&&game.Owns("ak47"),"Pistol lost after primary pickup");
            actions.Press(0x33,false);game.Step(.02,player,new(Actions:actions.Drain()));Check(game.Weapon.Id=="knife","Knife slot failed");
            actions.Press(0x31,false);game.Step(.02,player,new(Actions:actions.Drain()));Check(game.Weapon.Id=="ak47"&&game.Ammo==ammo&&game.Reserve==spare,"Primary selection changed inventory/ammo");
        }
        actions.Wheel(-60);Check(actions.Drain().Length==0,"Partial wheel delta switched early");
        actions.Wheel(-60);game.Step(.02,player,new(Actions:actions.Drain()));Check(game.Weapon.Id=="glock","Wheel did not select pistol");
        actions.Wheel(-240);game.Step(.02,player,new(Actions:actions.Drain()));Check(game.Weapon.Id=="ak47","Wheel did not wrap through knife");
        actions.Wheel(120);game.Step(.02,player,new(Actions:actions.Drain()));Check(game.Weapon.Id=="knife","Reverse wheel failed");
        game.SelectSlot(4);Check(game.Weapon.Id=="knife"&&game.Snapshot().Notice.Contains("Гранат"),"Empty grenade slot silent/changed weapon");
        game.SelectSlot(1);
        game.DropCurrent();Advance(game,player,1.3);var simultaneous=game.Drops.Single();
        player.Place(scene,simultaneous.Position+new V(0,0,64));
        game.Step(.02,player,new(Actions:new[]{0x32}));
        Check(game.Owns("ak47")&&game.Weapon.Id=="glock","Auto-pickup overrode explicit pistol selection in same tick");
        game.SelectSlot(1);Check(game.Ammo==ammo&&game.Reserve==spare,"Simultaneous pickup changed ammo");
        game.Buy(Combat.Catalog.First(w=>w.Id=="scout"));Check(game.Drops.Any(d=>d.Weapon=="ak47")&&!game.Owns("ak47"),"Buying replacement deleted prior gun");
        game.ToggleScope();Check(game.Fov==40,"Scout low zoom");Save("scope",scene,player,game);
        game.ToggleScope();Check(game.Fov==15,"Scout high zoom");game.ToggleScope();Check(game.Fov==90,"Scope did not cycle off");
        player.Place(scene,scene.Position(0));game.Step(.01,player,new());game.DropCurrent();
        var flat=game.Drops.Last();var pose=new CameraPose(flat.Position+new V(-25,-70,70),Math.Atan2(25,70),-.75);
        var dropFrame=scene.Render(0,pose,game.Snapshot() with{Actors=Array.Empty<ActorView>()});VectorBatch.Build(dropFrame);Check(dropFrame.Polygons.Any(p=>p.FaceId==-10000-flat.Id),"Drop not rendered");File.WriteAllText("../frame-round-drop.json",JsonSerializer.Serialize(dropFrame));
        game.HurtPlayer(300,player);Check(game.Dead,"Death did not happen");
        Advance(game,player,12);Check(game.Dead,"Player auto-respawned mid-round");
        Save("chase",scene,player,game);
        game.Step(.05,player,new(Jump:true));game.Step(.05,player,new());Check(game.Observer.Mode==SpectatorMode.Free,"Short Space did not change observer mode");
        var observer=game.RenderCamera(player).Eye;game.Step(.05,player,new(Forward:1,Dx:20));Check((game.RenderCamera(player).Eye-observer).Length>10,"Free look did not move");Save("free",scene,player,game);
        game.Step(.05,player,new(Jump:true));game.Step(.05,player,new());Check(game.Observer.Mode==SpectatorMode.Plan,"Plan observer mode missing");Save("plan",scene,player,game);
        int beforeRound=game.RoundNumber;Advance(game,player,1.3,new(Jump:true){SpaceHeld=true});Check(!game.Dead&&game.RoundNumber==beforeRound+1&&game.Bots.All(b=>b.Alive)&&game.Drops.Count==0,"Held Space did not restart everyone/clear drops");
        foreach(var b in game.Bots)b.NextShot=1e9;
        Advance(game,player,3.1);foreach(var b in game.Bots.Where(b=>b.Enemy)){b.Health=0;b.DeadAt=game.Time;}
        game.Step(.02,player,new());Check(game.RoundResult=="ПОБЕДА T"&&game.TWins==1,"Elimination did not finish round");Save("win",scene,player,game);
        Advance(game,player,5.1);Check(game.RoundNumber==beforeRound+2&&game.Bots.All(b=>b.Alive),"Next round did not respawn all bots");
        // No corpse resurrection even after the old 10-second respawn delay.
        foreach(var b in game.Bots)b.NextShot=1e9;
        var corpse=game.Bots[0];corpse.Health=0;corpse.DeadAt=game.Time;Advance(game,player,14);Check(!corpse.Alive,"Bot respawned in active round");
        foreach(var weapon in Combat.Catalog.Where(w=>w.Shape!="knife"))foreach(double reload in new[]{.01,.2,.35,.5,.7,.95})
        {var f=scene.Render(0,player.Camera,game.Snapshot() with{Weapon=weapon,Reload=reload});VectorBatch.Build(f);}
        Console.WriteLine("ROUND CHECKS PASSED: stationary mouse-only camera; unchanged colour idle/fire, five reload slides; 22 world guns; finite drop/pickup; scope levels; H difficulty; round elimination/restart; no mid-round respawn; three observer modes, tap vs held Space.");
        var plan=new PlanDrawing();Check(plan.ContourCount<50&&plan.HatchCount>100,"Plan not simplified/hatched");
        Check(scene.LetteringMarkCount>1500,"Missing wall inscriptions");
        for(int i=0;i<WallLettering.PreviewViews.Length;i++)
        {
            var f=scene.Render(0,WallLettering.PreviewViews[i],game.Snapshot() with{Dead=false,Actors=Array.Empty<ActorView>(),RoundResult="",Notice="",Freeze=0});
            VectorBatch.Build(f);File.WriteAllText("../frame-lettering-"+i+".json",JsonSerializer.Serialize(f));
        }
        Console.WriteLine($"POLISH18 CHECKS PASSED: slot selection after pickup, wheel accumulation/wrap, empty grenade slot; plan {plan.ContourCount} contours/{plan.HatchCount} hatches; {scene.LetteringMarkCount} wall text marks.");
        DeathTeamChecks.Run(scene);
    }
}
