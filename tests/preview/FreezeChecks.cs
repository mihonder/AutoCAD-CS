using CadDustLab;

static class FreezeChecks
{
    public static void Run(Scene scene)
    {
        void Check(bool ok,string message){if(!ok)throw new Exception(message);}
        var player=new Player(scene);var game=new Combat(scene,true);game.RestartRound(player);
        Frame Draw(double remaining,int menu=-1,bool score=false,bool paused=false)
            =>scene.Render(game.Time,player.Camera,game.Snapshot() with{Freeze=remaining,Menu=menu,Score=score,Paused=paused});
        var a=Draw(2.9);var b=Draw(2.6);
        var digitA=a.Labels.Single(x=>x.Text=="3");var digitB=b.Labels.Single(x=>x.Text=="3");
        Check(digitA.Height>=40&&digitA.Color!=digitB.Color,"Freeze digit does not blink / too small");
        Check(Draw(1.9).Labels.Any(x=>x.Text=="2")&&Draw(.9).Labels.Any(x=>x.Text=="1"),"Countdown sequence missing");
        foreach(var frame in new[]{Draw(2.9,4),Draw(2.9,score:true)})
            Check(frame.Labels.Any(x=>x.Text.StartsWith("ПОДГОТОВКА РАУНДА")),"Menu/Tab hides freeze countdown");
        Check(!Draw(0).Labels.Any(x=>x.Text=="ПОДГОТОВКА РАУНДА"),"Countdown survives live round");
        var paused=Draw(3,paused:true);
        Check(paused.Labels.Any(x=>x.Text=="F6 — ВОЙТИ В ИГРУ")&&!paused.Labels.Any(x=>x.Text=="ПОДГОТОВКА РАУНДА"),"Pause confused with freeze countdown");
        var freezeEye=player.Eye;int shots=game.Shots;
        game.Step(.1,player,new(Fire:true));Check(!game.CanFight&&!game.CanMove&&game.Shots==shots&&player.Eye==freezeEye,"Freeze allows fighting / movement");
        for(int i=0;i<150;i++)game.Step(.02,player,new());
        Check(game.CanFight&&game.FreezeRemaining==0,"Freeze never expires");
        File.WriteAllText("../../frame-freeze-bright.json",System.Text.Json.JsonSerializer.Serialize(b));
        File.WriteAllText("../../frame-freeze-dim.json",System.Text.Json.JsonSerializer.Serialize(a));
        File.WriteAllText("../../frame-freeze-buy.json",System.Text.Json.JsonSerializer.Serialize(Draw(2.9,4)));
        File.WriteAllText("../../frame-paused.json",System.Text.Json.JsonSerializer.Serialize(paused));
        Console.WriteLine("FREEZE CHECKS PASSED: 3-2-1 pulses, menu/Tab visibility, separate pause prompt, no fire/movement, live round after 3 s.");
    }
}
