namespace CadDustLab;

public sealed record WeaponSpec(string Id,string Name,int Category,int MenuSlot,int Magazine,double Damage,double Interval,double Reload,string Shape,int Color,int Pellets=1,double Range=4096)
{public WeaponRule Rule=>WeaponRules.All[Id];public int Price=>Rule.Price;}
public readonly record struct GameInput(double Forward=0,double Strafe=0,bool Walk=false,double Dx=0,double Dy=0,bool Fire=false,bool Aim=false,int[]? Actions=null,bool Jump=false,bool Duck=false,bool Score=false)
{public bool SpaceHeld {get;init;}}
public sealed record ActorView(int Id,V Eye,bool IsCt,double WalkPhase,double Fall,bool Hit)
{public double Heading {get;init;}public double Speed {get;init;}}
public sealed record Impact(V Position,double Time,bool Blood);
public sealed record ScoreRow(string Name,bool IsCt,int Kills,int Deaths,bool Dead);
public sealed record CombatSnapshot(double Time,ActorView[] Actors,Impact[] Effects,WeaponSpec Weapon,int Ammo,double Reload,double Recoil,bool Flash,bool HitMarker,int Health,int Armor,int Kills,int Menu,bool Dead,bool Scoped,string Notice,string[] Feed)
{public UiLanguage Language {get;init;}
 public int Money {get;init;}public int Reserve {get;init;}public bool Score {get;init;}public ScoreRow[] Scores {get;init;}=Array.Empty<ScoreRow>();public string[] Owned {get;init;}=Array.Empty<string>();public double WalkPhase {get;init;}public double MoveSpeed {get;init;}
 public DropView[] Drops {get;init;}=Array.Empty<DropView>();public int Difficulty {get;init;}public int ZoomLevel {get;init;}public double Fov {get;init;}=90;
 public bool Rounds {get;init;}public bool Paused {get;init;}public int Round {get;init;}public double Remaining {get;init;}public double Freeze {get;init;}public string RoundResult {get;init;}="";public int TWins {get;init;}public int CtWins {get;init;}
 public int SpectatorMode {get;init;}public string SpectatorName {get;init;}="";public double RestartProgress {get;init;}
 public bool DeathCamera {get;init;}public double DeathRemaining {get;init;}public Team PlayerTeam {get;init;}public double Spread {get;init;}}

// Simulation owns mutable state on the UI thread; rendering receives value snapshots only.
public sealed partial class Combat
{
    public static readonly WeaponSpec[] Catalog={
        // CS 1.6 base damage (unarmoured torso, before range/hitgroup modifiers).
        // This game's two-zone approximation remains body x1 / upper box x2.5;
        // bot armour, detailed limb hitboxes and range attenuation are not simulated.
        new("glock","GLOCK 18",1,1,20,25,.18,2.2,"pistol",0x45504a),
        new("usp","USP",1,2,12,34,.2,2.7,"pistol",0x363e40),
        new("p228","P228",1,3,13,32,.2,2.7,"pistol",0x606462),
        new("deagle","DESERT EAGLE",1,4,7,54,.35,2.2,"pistol",0xa2a9a4),
        new("elite","DUAL ELITES",1,5,30,36,.16,4.5,"pistol",0x55595a),
        new("fiveseven","FIVE-SEVEN",1,6,20,20,.15,2.7,"pistol",0x55595a),
        new("m3","M3 SHOTGUN",2,1,8,15,.8,2.4,"shotgun",0x625943,7,1600),
        new("xm1014","XM1014",2,2,7,13,.3,2.4,"shotgun",0x46524b,6,1600),
        new("mac10","MAC 10",3,1,30,24,.075,3.15,"smg",0x565a4f),
        new("mp5","MP5 NAVY",3,2,30,26,.085,2.63,"smg",0x394349),
        new("ump45","UMP45",3,3,25,30,.1,3.5,"smg",0x394349),
        new("p90","P90",3,4,50,23,.065,3.4,"smg",0x6c7155),
        new("tmp","TMP",3,5,30,20,.07,2.12,"smg",0x394349),
        new("galil","GALIL",4,1,35,30,.09,2.45,"rifle",0x7f8262),
        new("ak47","AK 47",4,2,30,36,.1,2.45,"rifle",0x9b673e),
        new("scout","SCOUT",4,3,10,75,1,2,"sniper",0x6b7757),
        new("sg552","SG 552",4,4,30,33,.09,3,"rifle",0x687555),
        new("awp","AWP",4,5,10,115,1.15,2.5,"sniper",0x718256),
        new("g3sg1","G3 SG1",4,6,20,60,.3,3.5,"sniper",0x54705b),
        new("m4a1","M4 A1",4,7,30,33,.09,3.05,"rifle",0x465052),
        new("aug","AUG",4,8,30,32,.09,3.3,"rifle",0x728066),
        new("m249","M249",5,1,100,30,.08,4.7,"machinegun",0x62674f),
        new("knife","KNIFE",0,0,1,40,.45,0,"knife",0xa4b4b5,1,82)
    };
    public sealed class Bot
    {
        public int Id;public bool Enemy,IsCt;public V Eye,Home;public double Health=100,DeadAt=-1,HitUntil,WalkPhase,Heading,TurnAt,NextShot;public int Deaths,Kills;
        public List<V> Path=new();public int Waypoint,TargetId,Burst;public double ReactAt,LastSeen,RepathAt,StuckTime,Travel,Speed;public V LastKnown;
        public HashSet<int> Avoid=new();
        public string WeaponId="ak47";public int Magazine=30,ReserveAmmo=90;public double ReloadUntil;
        public bool Alive=>Health>0;
    }
    private readonly Scene scene;
    private readonly Random random=new(16044);
    private readonly WeaponHandling handling=new();
    private double shotSpeed;
    private bool shotGrounded=true,shotCrouched;
    public double BulletSpread=>handling.Spread(Weapon,shotSpeed,shotGrounded,shotCrouched,Scoped);
    public V LastShotDirection {get;private set;}
    private readonly List<Impact> effects=new();
    private readonly List<(double Time,UiMessage Text)> feed=new();
    private readonly Dictionary<string,int> magazines=new();
    private readonly Dictionary<string,int> reserves=new();
    public readonly List<Bot> Bots=new();
    public WeaponSpec Weapon {get;private set;}=Catalog.First(w=>w.Id=="glock");
    public int Ammo=>magazines[Weapon.Id];
    public int Health {get;private set;}=100;
    public int Armor {get;private set;}
    public int Money {get;private set;}=16000;
    public int Reserve=>ReserveFor(Weapon);
    public int Deaths {get;private set;}
    public double MaxSpeed=>Scoped&&Weapon.Rule.ZoomSpeed>0?Weapon.Rule.ZoomSpeed:Weapon.Rule.Speed;
    private bool score;
    private double walkPhase,moveSpeed;
    public int Kills {get;private set;}
    public int Shots {get;private set;}
    public int Hits {get;private set;}
    public int Menu {get;private set;}=-1;
    public const int LanguageMenu=8;
    public UiLanguage Language {get;private set;}=UiLanguage.Russian;
    public bool MenuOpen=>Menu>=0;
    internal bool LanguageMenuInput(GameInput input)=>Menu==LanguageMenu||input.Actions?.Contains(KeyActions.LanguageKey)==true;
    public bool Dead=>Health<=0;
    public bool Scoped {get=>ZoomLevel>0;private set {if(!value)ZoomLevel=0;else if(ZoomLevel==0)ZoomLevel=1;}}
    public double Time {get;private set;}
    private double nextShot,reloadEnds,reloadStart,lastShot=-100,hitUntil,deadAt,aiAccumulator;
    private string primary="",secondary="glock";
    private UiMessage notice=new("B / И — покупка. Деньги и боезапас конечные.","B — buy. Money and ammo are limited.");
    private double noticeUntil=8;
    public Combat(Scene scene,bool rounds=false)
    {
        this.scene=scene;Rounds=rounds;Difficulty=rounds?1:0;lastEye=scene.Position(0);foreach(var w in Catalog){magazines[w.Id]=0;reserves[w.Rule.Ammo.Id]=0;}magazines["glock"]=20;magazines["knife"]=1;reserves["9mm"]=40;
        var enemyHints=scene.CounterSpawns.Take(5).ToArray();
        var friendlyHints=scene.TerroristSpawns.Take(4).ToArray();
        int id=1;
        foreach(var hint in enemyHints.Concat(friendlyHints))
        {
            var eye=scene.GroundNear(hint);Bots.Add(new Bot{Id=id,Enemy=id<=5,IsCt=id<=5,Eye=eye,Home=eye,Heading=random.NextDouble()*Math.PI*2,NextShot=3+id*.15,TurnAt=2+id,WeaponId=id<=5?"m4a1":"ak47"});id++;
        }
    }
    public void CloseMenu()=>Menu=-1;
    public int ReserveFor(WeaponSpec weapon)=>reserves.GetValueOrDefault(weapon.Rule.Ammo.Id);
    public bool Owns(string id)=>id=="knife"||id==primary||id==secondary;
    private bool Spend(int amount)
    {if(Money<amount){Show("Недостаточно денег","Not enough money");return false;}Money-=amount;return true;}
    public bool BuyAmmo(bool forPrimary)
    {
        string id=forPrimary?primary:secondary;if(id.Length==0){Show("Сначала купите оружие","Buy a weapon first");return false;}
        var spec=Catalog.First(w=>w.Id==id).Rule.Ammo;int current=reserves[spec.Id];
        if(current>=spec.Limit){Show("Запас патронов полный","Ammo reserve is full");return false;}
        if(!Spend(spec.Price))return false;
        int amount=Math.Min(spec.Pack,spec.Limit-current);reserves[spec.Id]+=amount;Show($"Куплено патронов: {amount} за {spec.Price} $",$"Bought {amount} rounds for ${spec.Price}");return true;
    }
    public void Handle(int key)
    {
        if(key==KeyActions.LanguageKey){Menu=Menu==LanguageMenu?-1:LanguageMenu;return;}
        if(Menu==LanguageMenu)
        {
            switch(key)
            {
                case 0x31: Language=UiLanguage.Russian;CloseMenu();break;
                case 0x32: Language=UiLanguage.English;CloseMenu();break;
                case 27: case 0x30: CloseMenu();break;
            }
            return;
        }
        if(key==0x4d){Menu=Menu==7?-1:7;Scoped=false;return;}
        if(Menu==7)
        {
            if(key==27||key==0x30){CloseMenu();return;}
            if(key is 0x31 or 0x32){requestedTeam=key==0x31?Team.T:Team.CT;CloseMenu();}
            return;
        }
        if(key==0x48){Menu=Menu==6?-1:6;Scoped=false;return;}
        if(Menu==6){if(key==27||key==0x30){CloseMenu();return;}if(key>=0x31&&key<=0x34){SetDifficulty(key-0x31);CloseMenu();}return;}
        if(Dead){if(key==27)CloseMenu();return;}
        if(key==0x42){Menu=MenuOpen?-1:0;Scoped=false;return;}
        if(key==27){CloseMenu();return;}
        if(key==0xbc){BuyAmmo(true);return;}if(key==0xbe){BuyAmmo(false);return;}
        if(MenuOpen)
        {
            int digit=key-0x30;if(digit<0||digit>9)return;
            if(digit==0){Menu=Menu==0?-1:0;return;}
            if(Menu==0){if(digit<=5)Menu=digit;else if(digit is 6 or 7)BuyAmmo(digit==6);else if(digit==8){if(Armor==100)Show("Броня уже полная","Armor is already full");else if(Spend(650)){Armor=100;Show("Куплен бронежилет: 650 $","Bought Kevlar for $650");Menu=-1;}}return;}
            var choice=Catalog.FirstOrDefault(w=>w.Category==Menu&&w.MenuSlot==digit);
            if(choice is not null){if(Buy(choice))Menu=-1;}else Show("Такого пункта нет. 0 — назад, B — закрыть","Invalid choice. 0 — back, B — close");return;
        }
        if(key==0x52)Reload();
        if(key==0x47)DropCurrent();
        if(key==0x45)PickupNearby(true);
        if(key is >=0x31 and <=0x34)SelectSlot(key-0x30);
        if(key==KeyActions.NextWeapon)CycleWeapon(1);
        if(key==KeyActions.PreviousWeapon)CycleWeapon(-1);
    }
    public void SelectSlot(int slot)
    {
        if(Dead||MenuOpen)return;
        string id=slot switch{1=>primary,2=>secondary,3=>"knife",_=>""};
        if(id.Length==0){Show(slot==4?"4 — Гранат пока нет":$"{slot} — Слот пуст. B — покупка",slot==4?"4 — No grenades available":$"{slot} — Empty slot. B — buy");return;}
        var spec=Catalog.First(w=>w.Id==id);if(Weapon.Id!=id)Equip(spec,false);
        Show($"{slot} — {spec.Name}",$"{slot} — {spec.Name}");
    }
    public void CycleWeapon(int direction)
    {
        if(Dead||MenuOpen)return;
        var slots=new[]{primary,secondary,"knife"}.Where(id=>id.Length>0).ToArray();
        int index=Array.IndexOf(slots,Weapon.Id),next=(index+Math.Sign(direction)+slots.Length)%slots.Length;
        string id=slots[next];SelectSlot(id==primary?1:id==secondary?2:3);
    }
    private void Acquire(WeaponSpec weapon)
    {if(weapon.Category==1)secondary=weapon.Id;else if(weapon.Category>1)primary=weapon.Id;}
    public bool Buy(WeaponSpec weapon)
    {
        if(Dead)return false;
        if(Owns(weapon.Id)){Show("Это оружие уже есть","You already own this weapon");return false;}
        if(!Spend(weapon.Price))return false;
        string previous=weapon.Category==1?secondary:primary;
        if(previous.Length>0)DropOwned(previous,lastEye,lastYaw,0);
        Equip(weapon,true);return true;
    }
    private void Equip(WeaponSpec weapon,bool buy)
    {
        Weapon=weapon;handling.Reset();reloadEnds=0;Scoped=false;nextShot=Time+.12;
        if(buy){Acquire(weapon);magazines[weapon.Id]=weapon.Magazine;Show($"Куплено: {weapon.Name} за {weapon.Price} $",$"Bought {weapon.Name} for ${weapon.Price}");}
    }
    private void Show(string russian,string english){notice=new(russian,english);noticeUntil=Time+3;}
    public void Reload()
    {
        if(Dead||reloadEnds>Time||Ammo==Weapon.Magazine||Weapon.Shape=="knife")return;
        if(Reserve==0){Show("Нет запасных патронов. B–6 / B–7 — купить","No spare ammo. B–6 / B–7 — buy");return;}
        reloadStart=Time;reloadEnds=Time+Weapon.Reload;Scoped=false;handling.ClearAccuracy();
    }
    public void Step(double dt,Player player,GameInput input)
    {
        dt=Compat.Clamp(dt,0,.08);Time+=dt;score=input.Score;walkPhase=player.WalkPhase;moveSpeed=player.Grounded?player.Speed:0;lastEye=player.Eye;lastYaw=player.Yaw;
        shotSpeed=player.Speed;shotGrounded=player.Grounded;shotCrouched=player.Crouched;lastCrouched=player.Crouched;handling.Step(Weapon,dt);
        bool choosingLanguage=LanguageMenuInput(input);
        if(input.Actions is not null)foreach(int key in input.Actions)Handle(key);
        if(requestedTeam is { } team){requestedTeam=null;ChangeTeam(team,player);return;}
        if(reloadEnds>0&&Time>=reloadEnds){int amount=Math.Min(Weapon.Magazine-Ammo,Reserve);magazines[Weapon.Id]+=amount;reserves[Weapon.Rule.Ammo.Id]-=amount;reloadEnds=0;}
        if(Dead)
        {
            DeathScene.Step(scene,dt);
            if(Rounds){if(!choosingLanguage&&!MenuOpen&&Observer.Step(scene,Bots,player,dt,input,!DeathScene.Active)){RestartRound(player,true);return;}}
            else if(Time-deadAt>3){Health=100;Armor=0;handling.Reset();DeathScene.Reset();player.Reset(scene);primary="";secondary="glock";Weapon=Catalog[0];foreach(var key in reserves.Keys.ToArray())reserves[key]=0;reserves["9mm"]=40;magazines["glock"]=20;reloadEnds=0;nextShot=Time+.5;Show("Вы вернулись. B / И — покупка","You respawned. B — buy");}
        }
        else if(!choosingLanguage&&!MenuOpen)
        {
            if(input.Aim)ToggleScope();
            if(input.Fire&&CanFight)Fire(player.Camera);
            // A pickup in the same tick must not override an explicit slot/wheel selection.
            bool selected=input.Actions?.Any(k=>k is >=0x31 and <=0x34 or KeyActions.NextWeapon or KeyActions.PreviousWeapon)==true;
            if(CanFight)PickupNearby(false,!selected);
        }
        effects.RemoveAll(e=>Time-e.Time>(e.Blood?.6:2));feed.RemoveAll(e=>Time-e.Time>6);
        aiAccumulator+=dt;
        if(aiAccumulator>=.05){double elapsed=Math.Min(aiAccumulator,.1);aiAccumulator=0;if(CanFight)UpdateBots(player,elapsed);}
        if(Rounds)UpdateRound(player);
    }
    public bool Fire(CameraPose camera)
    {
        if(Dead||MenuOpen||!CanFight||reloadEnds>Time||Time<nextShot)return false;
        if(Ammo<=0){Reload();return false;}
        nextShot=Time+Weapon.Interval;lastShot=Time;Shots++;if(Weapon.Shape!="knife")magazines[Weapon.Id]--;
        var aim=handling.Aim(camera);double spread=BulletSpread;
        for(int pellet=0;pellet<Weapon.Pellets;pellet++)
        {
            var direction=handling.Direction(aim,spread);LastShotDirection=direction;
            var end=camera.Eye+direction*Weapon.Range;double fraction=scene.Trace(camera.Eye,end);Bot? victim=null;double hit=fraction;
            foreach(var bot in Bots.Where(b=>b.Alive))
            {
                double t=HitBox(camera.Eye,end,bot.Eye);
                if(t<hit){hit=t;victim=bot;}
            }
            var point=camera.Eye+(end-camera.Eye)*Math.Max(0,hit-.0003);
            if(victim is not null)
            {
                if(victim.Enemy)
                {
                    Hits++;hitUntil=Time+.18;bool head=point.Z>victim.Eye.Z-10;
                    Damage(victim,Weapon.Damage*(head?2.5:1),true,point);
                }
            }
            else if(fraction<1)AddImpact(new(point,Time,false));
        }
        handling.Kick(Weapon,shotSpeed,shotGrounded,shotCrouched);
        return true;
    }
    public static double HitBox(V from,V to,V eye)
    {
        var delta=to-from;var min=eye-new V(13,13,64);var max=eye+new V(13,13,7);double low=0,high=1;
        bool Axis(double p,double d,double a,double b)
        {
            if(Math.Abs(d)<1e-9)return p>=a&&p<=b;
            double t0=(a-p)/d,t1=(b-p)/d;if(t0>t1)(t0,t1)=(t1,t0);
            low=Math.Max(low,t0);high=Math.Min(high,t1);return low<=high;
        }
        return Axis(from.X,delta.X,min.X,max.X)&&Axis(from.Y,delta.Y,min.Y,max.Y)&&Axis(from.Z,delta.Z,min.Z,max.Z)?low:double.PositiveInfinity;
    }
    private void AddImpact(Impact impact){if(effects.Count>=48)effects.RemoveAt(0);effects.Add(impact);}
    private void Damage(Bot bot,double damage,bool player,V point)
    {
        if(!bot.Alive)return;bot.Health-=damage;bot.HitUntil=Time+.18;AddImpact(new(point,Time,true));
        if(bot.Alive)return;
        bot.DeadAt=Time;bot.Deaths++;
        AddDrop(bot.WeaponId,bot.Eye,bot.Heading,bot.Magazine,bot.ReserveAmmo,0);
        if(player){Kills++;Money=Math.Min(16000,Money+300);string target=$"{(bot.IsCt?"CT":"T")} {bot.Id:00}  [{Weapon.Name}]";feed.Add((Time,new("Вы  >  "+target,"You  >  "+target)));}
        else {string entry=$"{(bot.IsCt?"T":"CT")} BOT  >  {(bot.IsCt?"CT":"T")} {bot.Id:00}";feed.Add((Time,new(entry,entry)));}
        if(feed.Count>4)feed.RemoveAt(0);
    }
    private static readonly V[] Goals={new(1152,2640,160),new(-1850,2300,64),new(-384,1200,0),new(1400,1400,64),new(-1600,500,64),new(-832,-704,192),new(448,2464,-64)};
    private void Plan(Bot bot,V? goal=null)
    {
        if(bot.Avoid.Count>12)bot.Avoid.Clear();
        var target=goal??Goals[random.Next(Goals.Length)];
        bot.Path=scene.Navigation.Path(bot.Eye,target,bot.Avoid);bot.Waypoint=0;bot.RepathAt=Time+3+random.NextDouble()*2;
    }
    private void UpdateBots(Player player,double dt)
    {
        foreach(var bot in Bots)
        {
            bot.Speed=0;
            if(!bot.Alive)
            {
                if(Rounds)continue;
                var spawnGap=bot.Home-player.Eye;
                if(!Dead&&Math.Abs(spawnGap.Z)<60&&new V(spawnGap.X,spawnGap.Y,0).Length<40)continue;
                if(Time-bot.DeadAt>10){bot.Eye=bot.Home;bot.Health=100;bot.DeadAt=-1;bot.NextShot=Time+1;bot.Path.Clear();bot.TargetId=0;bot.Avoid.Clear();}
                continue;
            }
            var skill=BotSkills[Difficulty];
            V? target=null;Bot? other=null;double nearest=skill.Sight;int targetId=0;
            bool Sees(V point)
            {
                var d=point-bot.Eye;double length=d.Length;
                if(length>=nearest||length<1)return false;
                if((Math.Sin(bot.Heading)*d.X+Math.Cos(bot.Heading)*d.Y)/length<-.15)return false;
                return scene.Trace(bot.Eye,point)>.999;
            }
            if(bot.Enemy&&!Dead&&Sees(player.Eye)){target=player.Eye;nearest=(bot.Eye-player.Eye).Length;targetId=-1;}
            foreach(var enemy in Bots)
            {
                if(enemy.Enemy==bot.Enemy||!enemy.Alive||!Sees(enemy.Eye))continue;
                target=enemy.Eye;other=enemy;nearest=(bot.Eye-enemy.Eye).Length;targetId=enemy.Id;
            }
            V motion=new();double speed=bot.IsCt?230:221;
            if(target is not null)
            {
                if(bot.TargetId!=targetId){bot.TargetId=targetId;bot.ReactAt=Time+skill.Reaction+random.NextDouble()*skill.Reaction*.65;bot.Burst=0;}
                bot.LastSeen=Time;bot.LastKnown=target.Value;
                var d=target.Value-bot.Eye;double desired=Math.Atan2(d.X,d.Y);
                double turn=Compat.Clamp(Math.IEEERemainder(desired-bot.Heading,2*Math.PI),-skill.Turn*dt,skill.Turn*dt);bot.Heading+=turn;
                if(Rounds&&bot.Magazine<=0&&bot.ReserveAmmo>0)
                {
                    if(bot.ReloadUntil==0)bot.ReloadUntil=Time+Catalog.First(w=>w.Id==bot.WeaponId).Reload;
                    else if(Time>=bot.ReloadUntil){int amount=Math.Min(30,bot.ReserveAmmo);bot.Magazine=amount;bot.ReserveAmmo-=amount;bot.ReloadUntil=0;}
                }
                if(Time>=bot.ReactAt&&Time>=bot.NextShot&&Math.Abs(Math.IEEERemainder(desired-bot.Heading,2*Math.PI))<.2)
                {
                    bot.Burst++;bot.NextShot=Time+(bot.Burst<skill.Burst?.13:skill.Pause+random.NextDouble()*.4);if(bot.Burst==skill.Burst)bot.Burst=0;
                    bool canShoot=!Rounds||bot.Magazine>0;if(Rounds&&canShoot)bot.Magazine--;
                    double chance=skill.Accuracy*Compat.Clamp(650/nearest,.3,1);
                    if(canShoot&&random.NextDouble()<chance)
                    {
                        if(other is not null){bool alive=other.Alive;Damage(other,skill.Damage,false,other.Eye-new V(0,0,20));if(alive&&!other.Alive)bot.Kills++;}
                        else if(!MenuOpen)
                        {
                            bool alive=!Dead;HurtPlayer(skill.Damage,player);
                            if(alive&&Dead){bot.Kills++;string killer=$"{(bot.IsCt?"CT":"T")} {bot.Id:00}";feed.Add((Time,new(killer+"  >  Вы",killer+"  >  You")));}
                        }
                    }
                }
                if(nearest<480)
                {
                    // Imperfect intermittent strafing, not a stationary turret.
                    double side=Math.Sin(Time*.9+bot.Id)>0?1:-1;
                    motion=new V(Math.Cos(bot.Heading)*side,-Math.Sin(bot.Heading)*side,0);
                    if(nearest<160)motion-=new V(Math.Sin(bot.Heading),Math.Cos(bot.Heading),0)*.6;
                    speed=Time<bot.ReactAt?55:110;
                }
                else if(Time>=bot.RepathAt)Plan(bot,target.Value);
            }
            else
            {
                bot.TargetId=0;
                if(Time>=bot.RepathAt||bot.Waypoint>=bot.Path.Count)Plan(bot,Time-bot.LastSeen<skill.Memory&&bot.LastSeen>0?bot.LastKnown:null);
            }
            if(motion.Length<.01)
            {
                while(bot.Waypoint<bot.Path.Count&&new V(bot.Path[bot.Waypoint].X-bot.Eye.X,bot.Path[bot.Waypoint].Y-bot.Eye.Y,0).Length<14)bot.Waypoint++;
                if(bot.Waypoint>=bot.Path.Count){Plan(bot);continue;}
                var d=bot.Path[bot.Waypoint]-bot.Eye;motion=new V(d.X,d.Y,0).Unit;
                if(target is null)bot.Heading+=Compat.Clamp(Math.IEEERemainder(Math.Atan2(d.X,d.Y)-bot.Heading,2*Math.PI),-3.5*dt,3.5*dt);
                speed=Math.Min(speed,new V(d.X,d.Y,0).Length/dt);
            }
            // Local steering keeps actors apart; the player also occupies space.
            var separation=new V();
            foreach(var neighbor in Bots)
            {
                if(neighbor==bot||!neighbor.Alive)continue;var d=bot.Eye-neighbor.Eye;d=new V(d.X,d.Y,0);
                if(d.Length>1&&d.Length<34)separation+=d.Unit*((34-d.Length)/34);
            }
            double playerFloor=player.Eye.Z-(player.Crouched?30:64);
            bool nearPlayerFloor=!Dead&&Math.Abs(bot.Eye.Z-64-playerFloor)<60;
            var fromPlayer=new V(bot.Eye.X-player.Eye.X,bot.Eye.Y-player.Eye.Y,0);
            if(nearPlayerFloor&&fromPlayer.Length>1&&fromPlayer.Length<72)
                separation+=fromPlayer.Unit*(3*(72-fromPlayer.Length)/72);
            motion=(motion+separation*.7).Unit;var offset=motion*(speed*dt);
            int steps=Math.Max(1,(int)Math.Ceiling(offset.Length/3));var before=bot.Eye;
            for(int i=0;i<steps;i++)
            {
                var delta=offset*(1.0/steps);
                bool TryMove(V d,out V next,double drop)
                {
                    if(!scene.TryWalk(bot.Eye,d,out next,drop))return false;
                    if(!nearPlayerFloor)return true;
                    double oldDistance=new V(bot.Eye.X-player.Eye.X,bot.Eye.Y-player.Eye.Y,0).Length;
                    double distance=new V(next.X-player.Eye.X,next.Y-player.Eye.Y,0).Length;
                    return distance>=40||distance>oldDistance+.001; // Permit escape from an overlapping spawn.
                }
                if(TryMove(delta,out var next,64))bot.Eye=next;
                else
                {
                    if(TryMove(new V(delta.X,0,0),out next,32))bot.Eye=next;
                    if(TryMove(new V(0,delta.Y,0),out next,32))bot.Eye=next;
                }
            }
            double moved=(bot.Eye-before).Length;bot.Travel+=moved;bot.WalkPhase+=moved/100;bot.Speed=moved/dt;
            if(moved<speed*dt*.15)bot.StuckTime+=dt;else bot.StuckTime=Math.Max(0,bot.StuckTime-dt);
            if(bot.StuckTime>.7)
            {
                if(bot.Waypoint<bot.Path.Count)bot.Avoid.Add(scene.Navigation.Nearest(bot.Path[bot.Waypoint]).Id);
                bot.StuckTime=0;Plan(bot);
            }
        }
    }
    private IEnumerable<ActorView> ActorViews()
    {
        foreach(var b in Bots)yield return new(b.Id,b.Eye,b.IsCt,b.WalkPhase,b.Alive?0:Compat.Clamp((Time-b.DeadAt)/.55,0,1),b.Alive&&b.HitUntil>Time){Heading=b.Heading,Speed=b.Speed};
        if(Dead&&DeathScene.HasBody)yield return DeathScene.Body;
    }
    public CombatSnapshot Snapshot()=>new(Time,ActorViews().ToArray(),effects.ToArray(),Weapon,Ammo,reloadEnds>Time?(Time-reloadStart)/Weapon.Reload:0,Math.Max(0,1-(Time-lastShot)/.13),Time-lastShot<.075,Time<hitUntil,Health,Armor,Kills,Menu,Dead,Scoped,Time<noticeUntil?notice.In(Language):"",feed.Select(f=>f.Text.In(Language)).ToArray())
    {
        Language=Language,Money=Money,Reserve=Reserve,Score=score&&Menu!=LanguageMenu,WalkPhase=walkPhase,MoveSpeed=moveSpeed,
        Drops=drops.ToArray(),Difficulty=Difficulty,ZoomLevel=ZoomLevel,Fov=Fov,Spread=BulletSpread,Rounds=Rounds,Round=RoundNumber,Remaining=Remaining,Freeze=FreezeRemaining,RoundResult=RoundResult,TWins=TWins,CtWins=CtWins,
        SpectatorMode=(int)Observer.Mode,SpectatorName=Observer.NameIn(Language),RestartProgress=Observer.RestartProgress,
        DeathCamera=DeathScene.Active,DeathRemaining=DeathScene.Remaining,PlayerTeam=PlayerTeam,
        Owned=new[]{primary,secondary,"knife"},
        Scores=new[]{new ScoreRow(Language.Pick("Вы","You"),PlayerTeam==Team.CT,Kills,Deaths,Dead)}.Concat(Bots.Select(b=>new ScoreRow(Language.BotName(b.IsCt,b.Id),b.IsCt,b.Kills,b.Deaths,!b.Alive))).ToArray()
    };
}
