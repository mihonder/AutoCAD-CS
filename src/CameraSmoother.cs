namespace CadDustLab;
public sealed class CameraSmoother
{
    private CameraPose? previous;
    public void Reset()=>previous=null;
    public CameraPose Follow(CameraPose target,double dt)
    {
        if(previous is not { } p)return (previous=target).Value;
        double alpha=1-Math.Exp(-Compat.Clamp(dt,0,.08)/.012);
        // Only a 12 ms angular filter; position stays collision-correct and no motion is predicted.
        return (previous=new CameraPose(target.Eye,p.Yaw+Math.IEEERemainder(target.Yaw-p.Yaw,Math.PI*2)*alpha,p.Pitch+(target.Pitch-p.Pitch)*alpha)).Value;
    }
}
