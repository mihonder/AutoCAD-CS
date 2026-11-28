using System.Text.Json;
using CadDustLab;

static class LanguageChecks
{
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}

    internal static void Run(string? exportPath)
    {
        var scene=new Scene(File.ReadAllText("../../assets/scene.json"),true);
        var player=new Player(scene);
        var game=new Combat(scene,true);
        game.RestartRound(player);
        var input=new PhysicalInput();
        var keys=new bool[256];
        double now=100;
        void Press(params int[] sequence)
        {
            foreach(int key in sequence)
            {
                keys[key]=true;input.Sample(keys,now+=2);
                keys[key]=false;input.Sample(keys,now+=2);
            }
            game.Step(.001,player,new(Actions:input.Read(now).Actions));
        }
        const int L=KeyActions.LanguageKey;
        Press(L,0x32);
        Check(game.Language==UiLanguage.Russian&&!game.MenuOpen,"Paused input switched language");
        input.Capture(true);
        var samples=new List<object>();
        void Inspect(string name,CombatSnapshot snapshot,params string[] expected)
        {
            var frame=scene.Render(game.Time,game.RenderCamera(player),snapshot);
            var batch=VectorBatch.Build(frame);
            var strings=batch.Labels.Select(l=>l.Text).ToArray();
            Check(expected.All(strings.Contains),"Missing label in "+name+": "+string.Join(" / ",expected));
            Check(strings.Contains(snapshot.Paused?"F6, then L to change language":"Press L to change language"),"Missing language hint");
            if(snapshot.Language==UiLanguage.English)
                Check(!strings.Any(s=>s.Any(c=>c>='А'&&c<='я'||c=='Ё'||c=='ё')),"Cyrillic in English "+name);
            if(exportPath!=null)samples.Add(new{name,language=snapshot.Language.ToString(),hud=snapshot.Language.Hud(!snapshot.Paused),batch.Labels,batch.Strokes});
        }

        var before=game.Snapshot();
        foreach(var lang in new[]{UiLanguage.Russian,UiLanguage.English})
        {
            Press(L,lang==UiLanguage.English?0x62:0x61); // keypad passes through physical normalization
            Check(game.Language==lang&&!game.MenuOpen&&game.RoundNumber==before.Round&&game.Money==before.Money&&game.Weapon==before.Weapon,"Selection changed game state");
            string Pick(string ru,string en)=>lang.Pick(ru,en);
            var live=game.Snapshot() with{Freeze=0};
            Inspect("hud",live,Pick("Здоровье: 100","Health: 100"));
            Inspect("reload",live with{Reload=.5},Pick("Перезарядка...","Reloading..."));
            Inspect("result",live with{RoundResult=Pick("ПОБЕДА CT","COUNTER-TERRORISTS WIN"),Feed=new[]{Pick("Вы","You")+"  >  CT 01  [DESERT EAGLE]"}},Pick("Следующий раунд через 5 секунд","Next round in 5 seconds"));
            Inspect("paused",live with{Paused=true},Pick("F6 — ВОЙТИ В ИГРУ","F6 — ENTER GAME"));
            Inspect("freeze",game.Snapshot(),Pick("ПОДГОТОВКА РАУНДА","ROUND WARMUP"));
            Inspect("score",live with{Score=true},Pick("Вы","You"),Pick("DUST 2 — ТАБЛИЦА СЧЁТА","DUST 2 — SCOREBOARD"));
            Inspect("score-freeze",game.Snapshot() with{Score=true},Pick("Игрок","Player"));
            Press(0x42);
            Inspect("buy",game.Snapshot(),Pick("8  Бронежилет","8  Kevlar vest"));
            for(int category=1;category<=5;category++)
            {
                Press(0x30+category);
                Inspect("buy-"+category,game.Snapshot(),"1  "+Combat.Catalog.First(w=>w.Category==category).Name);
                Press(0x30);
            }
            Press(27,0x48);
            Inspect("difficulty",game.Snapshot(),Pick("4  Эксперт","4  Expert"));
            Press(27,0x4d);
            Inspect("team",game.Snapshot(),Pick("ВЫБОР КОМАНДЫ","CHOOSE TEAM"));
            Press(L,0x33,0x42,0x4d);
            Check(game.Menu==Combat.LanguageMenu,"Language menu leaked unrelated actions");
            Inspect("language",game.Snapshot(),lang==UiLanguage.English?"2  English  [SELECTED]":"1  Russian  [ВЫБРАНО]");
            foreach(int cancel in new[]{0x30,L,27}){Press(cancel);Check(!game.MenuOpen&&game.Language==lang,"Cancel changed language");Press(L);}
            Press(27);
            var dead=live with{Dead=true,SpectatorName=lang.BotName(true,5),Notice="",DeathRemaining=1.2};
            Inspect("death",dead with{DeathCamera=true},Pick("ВЫ УБИТЫ","YOU DIED"));
            Inspect("chase",dead);
            Inspect("free",dead with{SpectatorMode=1});
            Inspect("plan",dead with{SpectatorMode=2},Pick("МИД","MID"),Pick("ТН — ТОННЕЛИ","TN — TUNNELS"));
            Inspect("plan-menu",dead with{SpectatorMode=2,Menu=Combat.LanguageMenu});
            Inspect("plan-score",dead with{SpectatorMode=2,Score=true});
        }
        Press(0x42,0x34,0x32);
        var purchase=game.Snapshot();
        Check(purchase.Weapon.Id=="ak47"&&purchase.Notice.StartsWith("Bought"),"English purchase failed");
        Press(L,0x31);
        Check(game.Snapshot().Notice.StartsWith("Куплено")&&purchase.Notice.StartsWith("Bought")&&game.Money==purchase.Money,"Existing purchase or earlier snapshot changed incorrectly");
        game.HurtPlayer(500,player);
        Press(L,0x32);
        Check(game.Dead&&game.Snapshot().SpectatorName.Contains("Bot"),"Dead player could not switch language");
        game.RestartRound(player,true);
        Check(game.Language==UiLanguage.English,"Round restart reset language");
        // Holding fire while opening, using or dismissing the dialog must not fire.
        for(int i=0;i<155;i++)game.Step(.02,player,new());
        int shots=game.Shots;
        foreach(int key in new[]{L,0x33,0x31})
            game.Step(.02,player,new(Fire:true,Actions:new[]{key}));
        Check(game.Shots==shots&&game.Language==UiLanguage.Russian&&!game.MenuOpen,"Language menu leaked held fire");
        if(exportPath!=null)File.WriteAllText(exportPath,JsonSerializer.Serialize(samples));
        Console.WriteLine("LANGUAGE CHECKS PASSED: physical input, both locales, all menus, HUD, countdown, scores, purchase, death and spectator map.");
    }
}
