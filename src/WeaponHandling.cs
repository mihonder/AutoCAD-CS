namespace CadDustLab;

// A deliberately moderate arcade adaptation of CS-style shot bloom and view
// punch, not a port of GoldSrc's prediction/RNG or every weapon's accuracy formula.
public readonly record struct HandlingProfile(double BaseSpread,double MaxSpread,double Bloom,double Kick,double Side,double MaxKick,double Recovery);
public sealed class WeaponHandling
{
    private readonly Random random;
    private double sinceShot=100;
    private int shot;
    public double Heat {get;private set;}
    public double Pitch {get;private set;}
    public double Yaw {get;private set;}
    public WeaponHandling(int seed=2416)=>random=new Random(seed);
    public static HandlingProfile Profile(WeaponSpec weapon)=>weapon.Id switch
    {
        "knife"=>new(0,0,0,0,0,0,.25),
        "ak47"=>new(.0055,.034,.17,1,.25,5.5,.42),
        "m4a1"=>new(.004,.026,.14,.75,.16,4.6,.38),
        "deagle"=>new(.005,.025,.3,1.5,.2,4,.32),
        "awp"=>new(.028,.05,.6,2.6,.16,5,.35),
        "scout"=>new(.02,.04,.5,1.7,.12,4,.32),
        _=>weapon.Shape switch
        {
            "pistol"=>new(.004,.018,.2,.7,.12,3,.28),
            "smg"=>new(.007,.032,.1,.35,.1,3,.27),
            "shotgun"=>new(.035,.048,.15,weapon.Id=="m3"?1.7:1.3,.15,4,.35),
            "sniper"=>new(.026,.055,.4,1.5,.16,4.5,.36),
            "machinegun"=>new(.006,.04,.09,.65,.2,5,.4),
            _=>new(.0045,.028,.15,.8,.18,4.8,.38)
        }
    };
    public void Reset(){Heat=Pitch=Yaw=0;sinceShot=100;shot=0;}
    public void ClearAccuracy()=>Heat=0;
    public void Step(WeaponSpec weapon,double dt)
    {
        dt=Compat.Clamp(dt,0,.08);sinceShot+=dt;
        if(sinceShot>.12)Heat=Math.Max(0,Heat-dt*2.4);
        double decay=Math.Exp(-dt/Profile(weapon).Recovery);Pitch*=decay;Yaw*=decay;
        if(Math.Abs(Pitch)<1e-6)Pitch=0;if(Math.Abs(Yaw)<1e-6)Yaw=0;
    }
    public double Spread(WeaponSpec weapon,double speed,bool grounded,bool crouched,bool scoped)
    {
        var profile=Profile(weapon);if(profile.MaxSpread==0)return 0;
        double spread=profile.BaseSpread+(profile.MaxSpread-profile.BaseSpread)*Heat;
        if(scoped)spread*=weapon.Shape=="sniper"?.025:.75;
        double movement=Compat.Clamp(speed/250,0,1);
        spread=spread*(1+movement*1.4)+movement*.006;
        if(!grounded)spread=spread*2.5+.028;
        else if(crouched)spread*=.72;
        return Math.Min(.18,spread);
    }
    public CameraPose Aim(CameraPose camera)=>camera with{
        Yaw=Math.IEEERemainder(camera.Yaw+Yaw,Math.PI*2),Pitch=Compat.Clamp(camera.Pitch+Pitch,-1.45,1.45)};
    public V Direction(CameraPose aim,double spread)
    {
        var forward=new V(Math.Sin(aim.Yaw)*Math.Cos(aim.Pitch),Math.Cos(aim.Yaw)*Math.Cos(aim.Pitch),Math.Sin(aim.Pitch));
        if(spread==0)return forward;
        var right=new V(Math.Cos(aim.Yaw),-Math.Sin(aim.Yaw),0);var up=right.Cross(forward).Unit;
        // Centre-weighted disc, with a hard boundary and a separate RNG from AI.
        double x,y;do{x=random.NextDouble()+random.NextDouble()-1;y=random.NextDouble()+random.NextDouble()-1;}while(x*x+y*y>1);
        return (forward+right*(x*spread)+up*(y*spread)).Unit;
    }
    public void Kick(WeaponSpec weapon,double speed,bool grounded,bool crouched)
    {
        var p=Profile(weapon);if(p.Kick==0)return;
        double stance=!grounded?1.3:crouched?.72:speed>140?1.12:1;
        const double rad=Math.PI/180;
        Pitch=Math.Min(p.MaxKick*rad,Pitch+p.Kick*(1+Heat*.35)*stance*rad);
        Yaw=Compat.Clamp(Yaw+Math.Sin(++shot*1.7)*p.Side*(1+Heat)*stance*rad,-1.8*rad,1.8*rad);
        Heat=Math.Min(1,Heat+p.Bloom);sinceShot=0;
    }
}
