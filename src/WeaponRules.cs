namespace CadDustLab;

public sealed record AmmoSpec(string Id,int Price,int Pack,int Limit);
public sealed record WeaponRule(int Price,double Speed,AmmoSpec Ammo,double ZoomSpeed=0);
public static class WeaponRules
{
    private static readonly AmmoSpec Nine=new("9mm",20,30,120),Acp=new("45acp",25,12,100),Sig=new("357sig",50,13,52),Ae=new("50ae",40,7,35),Buck=new("buckshot",65,8,32),Nato=new("556",60,30,90),NatoBox=new("556box",60,30,200),Seven=new("762",80,30,90),Mag=new("338",125,10,30),Five=new("57",50,50,100),None=new("none",0,0,0);
    public static readonly Dictionary<string,WeaponRule> All=new()
    {
        ["glock"]=new(400,250,Nine),["usp"]=new(500,250,Acp),["p228"]=new(600,250,Sig),["deagle"]=new(650,250,Ae),
        ["elite"]=new(800,250,Nine),["fiveseven"]=new(750,250,Five),["tmp"]=new(1250,250,Nine),["ump45"]=new(1700,250,Acp),
        ["m3"]=new(1700,230,Buck),["xm1014"]=new(3000,240,Buck),["mac10"]=new(1400,250,Acp),["mp5"]=new(1500,250,Nine),["p90"]=new(2350,245,Five),
        ["galil"]=new(2000,240,Nato),["ak47"]=new(2500,221,Seven),["scout"]=new(2750,260,Seven,220),["sg552"]=new(3500,235,Nato,200),
        ["awp"]=new(4750,210,Mag,150),["g3sg1"]=new(5000,210,Seven,150),["m4a1"]=new(3100,230,Nato),["aug"]=new(3500,240,Nato),["m249"]=new(5750,220,NatoBox),["knife"]=new(0,250,None)
    };
}
