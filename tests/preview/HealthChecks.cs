using CadDustLab;

static class HealthChecks
{
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    static void Advance(Combat game,Player player,double seconds)
    {for(double t=0;t<seconds;t+=.02)game.Step(.02,player,new());}
    static CameraPose Aim(V from,V to)
    {var d=(to-from).Unit;return new(from,Math.Atan2(d.X,d.Y),Math.Asin(d.Z));}
    public static void Run()
    {
        var scene=new Scene(File.ReadAllText("../../assets/scene.json"),true);
        var freshPlayer=new Player(scene);var round=new Combat(scene,true);round.RestartRound(freshPlayer);
        Check(round.Bots.All(b=>b.Health==100),"Bot did not spawn with 100 HP");
        foreach(var b in round.Bots)b.Health=1;
        round.RestartRound(freshPlayer,true);Check(round.Bots.All(b=>b.Health==100),"Restart retained old damage");
        round.ChangeTeam(Team.CT,freshPlayer);Check(round.Bots.All(b=>b.Health==100),"Team switch retained old damage");
        var baseline=new Dictionary<string,double>{{"glock",25},{"usp",34},{"p228",32},{"deagle",54},{"elite",36},{"fiveseven",20}};
        foreach(var pair in baseline)
        {
            int[] bullets=new int[2];
            for(int zone=0;zone<2;zone++)
            {
                var player=new Player(scene);var game=new Combat(scene);var target=game.Bots[0];
                game.Bots.Clear();game.Bots.Add(target);target.NextShot=1e9;target.Health=100;
                var spec=Combat.Catalog.Single(w=>w.Id==pair.Key);Check(spec.Damage==pair.Value,"Pistol base damage drift: "+pair.Key);
                if(pair.Key!="glock")Check(game.Buy(spec),"Could not equip "+pair.Key);
                Advance(game,player,.2);
                while(target.Alive&&bullets[zone]<10)
                {
                    target.Eye=scene.Position(120);var aim=Aim(player.Eye,target.Eye-new V(0,0,zone==0?32:4));
                    Check(scene.Trace(player.Eye,target.Eye)>.999,"Audit target occluded");
                    double before=target.Health;int shots=game.Shots,hits=game.Hits;
                    Check(game.Fire(aim),"Pistol shot rejected: "+pair.Key);
                    Check(game.Shots==shots+1&&game.Hits==hits+1,"One bullet produced multiple/missing hits");
                    Check(Math.Abs(before-target.Health-pair.Value*(zone==0?1:2.5))<.00001,"Unexpected damage in "+pair.Key+" / "+zone);
                    bullets[zone]++;
                    if(bullets[zone]==1&&!(zone==1&&pair.Key=="deagle"))Check(target.Alive,"Healthy bot killed by one pistol bullet: "+pair.Key);
                    // This audit measures damage for aimed, recovered single shots;
                    // sustained-fire spread/recoil has separate regression checks.
                    if(target.Alive)Advance(game,player,3);
                }
                Check(bullets[zone]==(int)Math.Ceiling(100/(pair.Value*(zone==0?1:2.5))),"Unexpected bullets to kill: "+pair.Key);
                int fired=game.Shots;Advance(game,player,.8);Check(game.Shots==fired,"Shots continued without trigger");
            }
            Console.WriteLine($"100 HP: {pair.Key}, base {pair.Value}, torso {bullets[0]} bullets, upper/head {bullets[1]} bullets (simplified x2.5).");
        }
        // A single finishing hit on an already hurt target is valid, not full-HP damage.
        var finisher=new Combat(scene);var hurt=finisher.Bots[0];finisher.Bots.Clear();finisher.Bots.Add(hurt);
        hurt.Health=20;hurt.Eye=scene.Position(120);
        Check(finisher.Fire(Aim(scene.Position(0),hurt.Eye-new V(0,0,32)))&&!hurt.Alive,"Wounded target did not take finishing damage");
        Console.WriteLine("HEALTH CHECKS PASSED: 100 HP spawns/reset/teams, six pistols through real ray hitboxes, one bullet = one hit, healthy vs wounded targets.");
    }
}
