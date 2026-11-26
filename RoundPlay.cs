namespace CadDustLab;

public sealed record DropView(int Id,string Weapon,V Position,double Heading,int Magazine,int Reserve,double PickupAt);
public sealed record BotSkill(string Name,double Reaction,double Accuracy,double Pause,int Burst,double Sight,double Turn,double Memory,int Damage);
public enum Team {T,CT}

public sealed partial class Combat
{
    public static readonly BotSkill[] BotSkills={
        new("Лёгкая",.65,.25,1.2,3,1600,2.8,3,12),
        new("Обычная",.4,.42,.8,4,2000,4,5,18),
        new("Сложная",.25,.6,.55,5,2400,5,7,22),
        new("Эксперт",.18,.75,.35,6,2800,6,9,26)
    };
    public bool Rounds {get;}
    public int Difficulty {get;private set;}
    public int ZoomLevel {get;private set;}
    public double Fov=>ZoomLevel==0?90:Weapon.Id switch{
        "awp"=>ZoomLevel==1?40:10,"scout"=>ZoomLevel==1?40:15,"g3sg1"=>ZoomLevel==1?40:15,
        "aug" or "sg552"=>55,_=>90};
    public double MouseScale=>Fov/90;
    public int RoundNumber {get;private set;}
    public int TWins {get;private set;}
    public int CtWins {get;private set;}
    public string RoundResult {get;private set;}="";
    private double freezeEnds,roundEnds,resultEnds;
    public double FreezeRemaining=>Rounds?Math.Max(0,freezeEnds-Time):0;
    public const double RoundDuration=300;
    public double Remaining=>Rounds?Math.Min(RoundDuration,Math.Max(0,roundEnds-Time)):0;
    public bool CanFight=>!Rounds||(Time>=freezeEnds&&RoundResult.Length==0);
    public bool CanMove=>!Dead&&!MenuOpen&&CanFight;
    public Spectator Observer {get;}=new();
    public PlayerDeathScene DeathScene {get;}=new();
    public Team PlayerTeam {get;private set;}
    private Team? requestedTeam;
    public void ChangeTeam(Team team,Player player)
    {
        CloseMenu();if(team==PlayerTeam)return;
        PlayerTeam=team;
        // Four fixed bots on each side. Bot 05 fills the side without the player.
        foreach(var bot in Bots)
        {
            bot.IsCt=bot.Id<=4||(bot.Id==5&&team==Team.T);
            bot.Enemy=bot.IsCt!=(team==Team.CT);
            var spawns=bot.IsCt?scene.CounterSpawns:scene.TerroristSpawns;
            int index=bot.Id==5?4:bot.Id<=4?bot.Id-1:bot.Id-6;
            bot.Home=scene.GroundNear(spawns[Math.Min(index,spawns.Length-1)]);
            bot.WeaponId=bot.IsCt?"m4a1":"ak47";
        }
        RestartRound(player,true);
        Show($"Вы за {team}. Составы выровнены: 5 на 5. B — покупка");
    }
    private V lastEye;
    private double lastYaw;
    private bool lastCrouched;
    private int nextDrop;
    private readonly List<DropView> drops=new();
    private readonly HashSet<int> ownDropsUnderfoot=new();
    public IReadOnlyList<DropView> Drops=>drops;
    public void SetDifficulty(int level){Difficulty=Compat.Clamp(level,0,3);Show("Сложность: "+BotSkills[Difficulty].Name);}
    public void ToggleScope()
    {
        if(Dead||MenuOpen||reloadEnds>Time)return;
        int levels=Weapon.Shape=="sniper"?2:Weapon.Id is "aug" or "sg552"?1:0;
        ZoomLevel=levels==0?0:(ZoomLevel+1)%(levels+1);
    }
    public void RestartRound(Player player,bool manual=false)
    {
        bool survived=!Dead&&RoundNumber>0&&!manual;
        if(!survived)
        {
            primary="";secondary=PlayerTeam==Team.CT?"usp":"glock";Weapon=Catalog.First(w=>w.Id==secondary);Armor=0;
            foreach(var key in magazines.Keys.ToArray())magazines[key]=0;
            foreach(var key in reserves.Keys.ToArray())reserves[key]=0;
            magazines[secondary]=Weapon.Magazine;magazines["knife"]=1;reserves[Weapon.Rule.Ammo.Id]=PlayerTeam==Team.CT?24:40;
        }
        if(manual||RoundNumber==0)Money=16000;
        Health=100;Scoped=false;CloseMenu();reloadEnds=0;lastShot=-100;hitUntil=0;effects.Clear();drops.Clear();ownDropsUnderfoot.Clear();feed.Clear();
        freezeEnds=Time+3;roundEnds=freezeEnds+RoundDuration;resultEnds=0;RoundResult="";RoundNumber++;aiAccumulator=0;handling.Reset();
        nextShot=freezeEnds;Observer.Reset();DeathScene.Reset();requestedTeam=null;
        var playerSpawns=PlayerTeam==Team.CT?scene.CounterSpawns:scene.TerroristSpawns;
        player.Place(scene,playerSpawns[Math.Min(4,playerSpawns.Length-1)],PlayerTeam==Team.CT?Math.PI:Math.PI/2+4*Math.PI/180);
        lastEye=player.Eye;lastYaw=player.Yaw;lastCrouched=false;shotSpeed=0;shotGrounded=true;shotCrouched=false;
        foreach(var bot in Bots)
        {
            bot.Eye=bot.Home;bot.Health=100;bot.DeadAt=-1;bot.HitUntil=0;bot.WalkPhase=0;bot.Speed=0;
            bot.Path.Clear();bot.Waypoint=0;bot.TargetId=0;bot.Burst=0;bot.LastSeen=0;bot.RepathAt=freezeEnds;bot.StuckTime=0;bot.Avoid.Clear();
            bot.NextShot=freezeEnds+.5;bot.ReloadUntil=0;bot.Magazine=30;bot.ReserveAmmo=90;
            bot.Heading=bot.IsCt?Math.PI:0;
        }
        Show($"Раунд {RoundNumber}. Подготовка 3 с. B — покупка, H — сложность");
    }
    private void UpdateRound(Player player)
    {
        if(RoundResult.Length>0){if(Time>=resultEnds)RestartRound(player);return;}
        if(Time<freezeEnds)return;
        int ct=Bots.Count(b=>b.IsCt&&b.Alive)+(!Dead&&PlayerTeam==Team.CT?1:0),t=Bots.Count(b=>!b.IsCt&&b.Alive)+(!Dead&&PlayerTeam==Team.T?1:0);
        if(ct>0&&t>0&&Time<roundEnds)return;
        bool tWins=ct==0&&t>0,draw=ct==0&&t==0;
        RoundResult=draw?"НИЧЬЯ":tWins?"ПОБЕДА T":"ПОБЕДА CT";
        if(!draw){if(tWins)TWins++;else CtWins++;bool playerWon=tWins==(PlayerTeam==Team.T);Money=Math.Min(16000,Money+(playerWon?3250:1400));}
        resultEnds=Time+5;Scoped=false;CloseMenu();player.StopMotion();
    }
    public void HurtPlayer(int damage,Player player)
    {
        if(Dead)return;
        int taken=Armor>0?(int)Math.Ceiling(damage*.5):damage;Armor=Math.Max(0,Armor-damage/2);Health=Math.Max(0,Health-taken);
        if(!Dead)return;
        Deaths++;deadAt=Time;Scoped=false;CloseMenu();reloadEnds=0;player.StopMotion();handling.Reset();
        foreach(string id in new[]{primary,secondary})if(id.Length>0)DropOwned(id,player.Eye,player.Yaw,0);
        Weapon=Catalog.First(w=>w.Id=="knife");DeathScene.Begin(scene,player,PlayerTeam);Observer.Begin(player.Camera,Bots);
    }
    private void AddDrop(string id,V eye,double yaw,int magazine,int reserve,double delay)
    {
        if(id=="knife")return;
        V ground=scene.DropGround(eye,yaw);
        if(drops.Count>=80)drops.RemoveAt(0);
        drops.Add(new(++nextDrop,id,ground,yaw,magazine,reserve,Time+delay));
    }
    private void DropOwned(string id,V eye,double yaw,double delay)
    {
        if(id=="knife"||!Owns(id))return;
        var spec=Catalog.First(w=>w.Id==id);string other=id==primary?secondary:primary;
        bool shared=other.Length>0&&Catalog.First(w=>w.Id==other).Rule.Ammo.Id==spec.Rule.Ammo.Id;
        int spare=shared?0:ReserveFor(spec);
        AddDrop(id,eye,yaw,magazines[id],spare,delay);
        if(!shared)reserves[spec.Rule.Ammo.Id]=0;magazines[id]=0;
        if(id==primary)primary="";if(id==secondary)secondary="";
    }
    public bool DropCurrent()
    {
        if(Dead||Weapon.Shape=="knife"||MenuOpen)return false;
        DropOwned(Weapon.Id,lastEye,lastYaw,1.2);
        ownDropsUnderfoot.Add(nextDrop);
        string id=primary.Length>0?primary:secondary.Length>0?secondary:"knife";
        Equip(Catalog.First(w=>w.Id==id),false);return true;
    }
    public bool PickupNearby(bool replace,bool autoEquip=true)
    {
        if(Dead||MenuOpen)return false;
        var foot=lastEye-new V(0,0,lastCrouched?30:64);
        foreach(var drop in drops.OrderBy(d=>(d.Position-foot).Length).ToArray())
        {
            if((drop.Position-foot).Length>42){ownDropsUnderfoot.Remove(drop.Id);continue;}
            if(Time<drop.PickupAt||(!replace&&ownDropsUnderfoot.Contains(drop.Id))||scene.Trace(lastEye,drop.Position+new V(0,0,10))<.999)continue;
            var spec=Catalog.First(w=>w.Id==drop.Weapon);string occupied=spec.Category==1?secondary:primary;
            if(occupied.Length>0&&!replace)continue;
            if(occupied==drop.Weapon)continue;
            if(occupied.Length>0)DropOwned(occupied,lastEye,lastYaw,1.2);
            magazines[spec.Id]=drop.Magazine;reserves[spec.Rule.Ammo.Id]=Math.Min(spec.Rule.Ammo.Limit,ReserveFor(spec)+drop.Reserve);
            Acquire(spec);if(autoEquip)Equip(spec,false);drops.Remove(drop);ownDropsUnderfoot.Remove(drop.Id);Show("Подобрано: "+spec.Name);return true;
        }
        return false;
    }
    public CameraPose RenderCamera(Player player)=>Dead?(DeathScene.Active?DeathScene.Camera:Rounds?Observer.Camera:player.Camera):handling.Aim(player.Camera);
}

public enum SpectatorMode {Chase,Free,Plan}
public sealed class Spectator
{
    public SpectatorMode Mode {get;private set;}
    public CameraPose Camera {get;private set;}
    public string TargetName {get;private set;}="";
    public double RestartProgress=>Compat.Clamp(spaceTime/1.2,0,1);
    private int targetId;
    private double yaw,pitch,spaceTime;
    private bool spaceDown,fireDown,aimDown,waitSpaceUp;
    public void Reset(){Mode=SpectatorMode.Chase;spaceTime=0;spaceDown=false;waitSpaceUp=true;targetId=0;fireDown=false;aimDown=false;}
    public void Begin(CameraPose camera,List<Combat.Bot> bots){Reset();Camera=camera;yaw=camera.Yaw;pitch=.15;Choose(bots,1);}
    private void Choose(List<Combat.Bot> bots,int direction)
    {
        var alive=bots.Where(b=>b.Alive).ToArray();if(alive.Length==0){targetId=0;TargetName="Нет живых игроков";return;}
        int index=Array.FindIndex(alive,b=>b.Id==targetId);index=(index+direction+alive.Length)%alive.Length;
        targetId=alive[index].Id;TargetName=$"{(alive[index].IsCt?"CT":"T")} — Бот {targetId:00}";
    }
    public bool Step(Scene scene,List<Combat.Bot> bots,Player player,double dt,GameInput input,bool allowViewChange=true)
    {
        bool space=input.SpaceHeld||input.Jump;
        if(waitSpaceUp){if(!space)waitSpaceUp=false;space=false;}
        if(space){spaceTime+=dt;spaceDown=true;if(spaceTime>=1.2)return true;}
        else if(spaceDown){if(allowViewChange)Mode=(SpectatorMode)(((int)Mode+1)%3);spaceDown=false;spaceTime=0;}
        if(!allowViewChange){fireDown=input.Fire;aimDown=input.Aim;return false;}
        if(input.Fire&&!fireDown)Choose(bots,1);if(input.Aim&&!aimDown)Choose(bots,-1);
        fireDown=input.Fire;aimDown=input.Aim;
        if(!bots.Any(b=>b.Id==targetId&&b.Alive))Choose(bots,1);
        yaw=Math.IEEERemainder(yaw+input.Dx*.0024,Math.PI*2);pitch=Compat.Clamp(pitch-input.Dy*.0024,-1.3,1.3);
        var forward=new V(Math.Sin(yaw)*Math.Cos(pitch),Math.Cos(yaw)*Math.Cos(pitch),Math.Sin(pitch));var right=new V(Math.Cos(yaw),-Math.Sin(yaw),0);
        if(Mode==SpectatorMode.Free)
        {
            var move=forward*input.Forward+right*input.Strafe-new V(0,0,input.Duck?1:0);
            Camera=new(Camera.Eye+move*(dt*(input.Walk?140:420)/Math.Max(1,move.Length)),yaw,pitch);
        }
        else if(bots.FirstOrDefault(b=>b.Id==targetId&&b.Alive) is { } target)
        {
            var anchor=target.Eye+new V(0,0,12);var destination=anchor-forward*125+new V(0,0,30);
            double trace=scene.Trace(anchor,destination);var eye=anchor+(destination-anchor)*Math.Max(.05,trace-.04);
            var look=(anchor-eye).Unit;Camera=new(eye,Math.Atan2(look.X,look.Y),Math.Asin(look.Z));
        }
        return false;
    }
}
