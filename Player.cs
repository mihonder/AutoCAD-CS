namespace CadDustLab;

public readonly record struct CameraPose(V Eye,double Yaw,double Pitch);
public sealed class Player
{
    public V Eye {get;private set;}
    public double Yaw {get;private set;}
    public double Pitch {get;private set;}
    public double DistanceMoved {get;private set;}
    public double Speed {get;private set;}
    public double WalkPhase {get;private set;}
    public double VerticalSpeed {get;private set;}
    public bool Grounded {get;private set;}=true;
    public bool Crouched {get;private set;}
    public int BlockedSteps {get;private set;}
    public int JumpCount {get;private set;}
    public int CrouchCount {get;private set;}
    private V velocity;
    private bool wasJump;
    public CameraPose Camera=>new(Eye,Yaw,Pitch);
    public Player(Scene scene)=>Reset(scene);
    public void Place(Scene scene,V position,double yaw=0)
    {Eye=scene.GroundNear(position);Yaw=yaw;Pitch=0;velocity=new();VerticalSpeed=0;Grounded=true;Crouched=false;wasJump=false;Speed=0;}
    public void Reset(Scene scene)
    {Place(scene,scene.Position(0));var d=scene.Position(80)-Eye;Yaw=Math.Atan2(d.X,d.Y);}
    public void Look(double dx,double dy)
    {Yaw=Math.IEEERemainder(Yaw+dx*.0024,Math.PI*2);Pitch=Compat.Clamp(Pitch-dy*.0024,-1.45,1.45);}
    public void StopMotion(){velocity=new();Speed=0;wasJump=false;}
    public void Move(Scene scene,double forward,double strafe,bool walk,double dt,double maxSpeed=250,bool jump=false,bool duck=false)
    {
        dt=Compat.Clamp(dt,0,.08);if(dt<=0)return;
        if(duck&&!Crouched)
        {if(scene.TryStance(Eye,true,Grounded,out var p)){Eye=p;Crouched=true;CrouchCount++;}}
        else if(!duck&&Crouched)
        {if(scene.TryStance(Eye,false,Grounded,out var p)){Eye=p;Crouched=false;}}
        if(jump&&!wasJump&&Grounded){VerticalSpeed=Math.Sqrt(2*800*45);Grounded=false;JumpCount++;}
        wasJump=jump;
        double length=Math.Sqrt(forward*forward+strafe*strafe);
        var wish=length>0?(new V(Math.Sin(Yaw),Math.Cos(Yaw),0)*forward+new V(Math.Cos(Yaw),-Math.Sin(Yaw),0)*strafe)*(1/length):new V();
        double wanted=maxSpeed*(Crouched?.333:walk?.52:1);
        double total=0;int ticks=Math.Max(1,(int)Math.Ceiling(dt/.008));double h=dt/ticks;
        for(int tick=0;tick<ticks;tick++)
        {
            if(Grounded)
            {
                double speed=velocity.Length;
                if(speed>0)velocity*=Math.Max(0,speed-Math.Max(100,speed)*4*h)/speed;
                double add=wanted-velocity.Dot(wish);
                if(length>0&&add>0)velocity+=wish*Math.Min(add,10*wanted*h);
            }
            else if(length>0)
            {
                // Limited air acceleration; no instant direction changes.
                double add=Math.Min(30,wanted)-velocity.Dot(wish);
                if(add>0)velocity+=wish*Math.Min(add,10*wanted*h);
            }
            if(velocity.Length>wanted&&Grounded)velocity*=wanted/velocity.Length;
            if(velocity.Length<.001)velocity=new();
            var delta=velocity*h;int steps=Math.Max(1,(int)Math.Ceiling(delta.Length/3));delta*=1.0/steps;
            for(int i=0;i<steps;i++)
            {
                // A stationary grounded player must not repeatedly snap to a re-sampled floor.
                if(delta.Length<1e-9)break;
                var old=Eye;
                bool Try(V d,out V next)
                {
                    if(Grounded&&scene.TryWalk(Eye,d,out next,20,Crouched))return true;
                    next=Eye+d;return scene.CanOccupy(next,duck:Crouched);
                }
                if(Try(delta,out var next))Eye=next;
                else
                {
                    BlockedSteps++;
                    if(Try(new V(delta.X,0,0),out next))Eye=next;else velocity=new(0,velocity.Y,0);
                    if(Try(new V(0,delta.Y,0),out next))Eye=next;else velocity=new(velocity.X,0,0);
                }
                total+=new V(Eye.X-old.X,Eye.Y-old.Y,0).Length;
            }
            bool floor=!scene.CanOccupy(Eye-new V(0,0,.12),duck:Crouched);
            if(Grounded&&!floor)Grounded=false;
            if(!Grounded)
            {
                double dz=VerticalSpeed*h-400*h*h;VerticalSpeed-=800*h;
                int zsteps=Math.Max(1,(int)Math.Ceiling(Math.Abs(dz)/2));double step=dz/zsteps;
                for(int i=0;i<zsteps;i++)
                {
                    var next=Eye+new V(0,0,step);
                    if(scene.CanOccupy(next,duck:Crouched)){Eye=next;continue;}
                    double a=0,b=1;
                    for(int j=0;j<10;j++){double m=(a+b)/2;if(scene.CanOccupy(Eye+new V(0,0,step*m),duck:Crouched))a=m;else b=m;}
                    Eye+=new V(0,0,step*a);if(step<0)Grounded=true;VerticalSpeed=0;break;
                }
            }
        }
        Speed=total/dt;DistanceMoved+=total;if(Grounded)WalkPhase+=total/95;
    }
}
