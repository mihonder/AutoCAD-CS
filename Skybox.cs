namespace CadDustLab;

public static class Skybox
{
    public static readonly Patch[] Patches=Build();
    private static Patch[] Build()
    {
        var p=new List<Patch>();const double r=3000;
        V At(int side,double horizontal,double z)=>side switch
        {0=>new(horizontal,r,z),1=>new(r,-horizontal,z),2=>new(-horizontal,-r,z),_=>new(-r,horizontal,z)};
        for(int side=0;side<4;side++)
        {
            p.Add(new(new[]{At(side,-r,-r),At(side,r,-r),At(side,r,0),At(side,-r,0)},0xd6d5bf));
            p.Add(new(new[]{At(side,-r,0),At(side,r,0),At(side,r,350),At(side,-r,350)},0xc7d0c5));
            p.Add(new(new[]{At(side,-r,350),At(side,r,350),At(side,r,r),At(side,-r,r)},0xb6c6c6));
        }
        p.Add(new(new[]{new V(-r,-r,r),new V(r,-r,r),new V(r,r,r),new V(-r,r,r)},0xb6c6c6));
        p.Add(new(new[]{new V(-r,-r,-r),new V(-r,r,-r),new V(r,r,-r),new V(r,-r,-r)},0xd6d5bf));
        for(int side=0;side<4;side++)for(int i=0;i<2;i++)
        {
            double x=-1500+i*2100+side*140,z=1000+(side%2)*250+i*300;
            p.Add(new(new[]{At(side,x-520,z),At(side,x-100,z+90),At(side,x+480,z+25),At(side,x+270,z-65),At(side,x-420,z-55)},0xdde0d5));
        }
        return p.ToArray();
    }
}
