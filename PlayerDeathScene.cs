namespace CadDustLab;

// A local third-person death sequence, followed by ordinary spectating.
// The body uses the selected team's baked monochrome death slides.
public sealed class PlayerDeathScene
{
    public const double Duration=2.6,FallDuration=.75;
    public bool HasBody {get;private set;}
    public double Elapsed {get;private set;}
    public bool Active=>HasBody&&Elapsed<Duration;
    public double Remaining=>Active?Duration-Elapsed:0;
    public CameraPose Camera {get;private set;}
    private V startFoot,startEye,cameraOffset;
    private double groundZ,heading;
    private bool isCt;
    private V Foot=>new(startFoot.X,startFoot.Y,Math.Max(groundZ,startFoot.Z-400*Elapsed*Elapsed));
    public ActorView Body=>new(0,Foot+new V(0,0,64),isCt,0,Compat.Clamp(Elapsed/FallDuration,.001,1),false){Heading=heading};
    public void Reset(){HasBody=false;Elapsed=0;}
    public void Begin(Scene scene,Player player,Team team)
    {
        HasBody=true;Elapsed=0;isCt=team==Team.CT;heading=player.Yaw;startEye=player.Eye;
        startFoot=startEye-new V(0,0,player.Crouched?30:64);
        // Trace vertically so a crouching/airborne death keeps its original XY.
        var from=startFoot+new V(0,0,4);var to=from-new V(0,0,1024);
        double ground=scene.Trace(from,to);
        groundZ=ground<1?(from+(to-from)*ground).Z+.05:startFoot.Z;
        var anchor=startFoot+new V(0,0,28);double best=-1;
        // Prefer a rear shoulder view; near a wall, choose a clear side instead.
        foreach(double turn in new[]{Math.PI+.55,Math.PI-.55,Math.PI/2,-Math.PI/2,0d})
        {
            double a=heading+turn;var offset=new V(Math.Sin(a)*130,Math.Cos(a)*130,108);
            var eye=Clear(scene,anchor,startFoot+offset);double distance=(eye-anchor).Length;
            if(distance>best){best=distance;cameraOffset=eye-startFoot;}
            if(distance>140)break;
        }
        UpdateCamera(scene);
    }
    public void Step(Scene scene,double dt)
    {
        if(!HasBody)return;
        Elapsed+=Math.Max(0,dt);if(Active)UpdateCamera(scene);
    }
    private static V Clear(Scene scene,V from,V to)
    {
        var delta=to-from;double distance=delta.Length;if(distance<.0001)return from;
        double trace=scene.Trace(from,to);
        return trace>=1?to:from+delta*Math.Max(0,trace-3/distance);
    }
    private void UpdateCamera(Scene scene)
    {
        double t=Compat.Clamp(Elapsed/.65,0,1);t=t*t*(3-2*t);
        double fall=Compat.Clamp(Elapsed/FallDuration,0,1);
        var anchor=Foot+new V(0,0,48-30*fall);
        var wanted=startEye*(1-t)+(Foot+cameraOffset)*t;
        var eye=Clear(scene,anchor,Clear(scene,startEye,wanted));
        var look=anchor-eye;
        // At the first instant keep a well-defined direction while leaving the body.
        if(look.Length<.01)look=new(Math.Sin(heading),Math.Cos(heading),-.2);
        look=look.Unit;
        Camera=new(eye,Math.Atan2(look.X,look.Y),Math.Asin(Compat.Clamp(look.Z,-1,1)));
    }
}
