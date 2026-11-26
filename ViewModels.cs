using System.IO.Compression;
using System.Text.Json;
namespace CadDustLab;

public static class ViewModels
{
    private const double Scale=.8;
    private static readonly Lazy<Dictionary<string,Dictionary<string,ScreenPolygon[]>>> slides=new(Load);
    public static IReadOnlyCollection<string> Weapons=>slides.Value.Keys;
    private static Dictionary<string,Dictionary<string,ScreenPolygon[]>> Load()
    {
        var asm=typeof(ViewModels).Assembly;
        using var source=asm.GetManifestResourceStream(asm.GetManifestResourceNames().Single(n=>n.EndsWith("weapon-slides.json.gz")))!;
        using var gzip=new GZipStream(source,CompressionMode.Decompress);using var json=JsonDocument.Parse(gzip);
        var root=json.RootElement;double sx=1600.0/root.GetProperty("width").GetInt32(),sy=900.0/root.GetProperty("height").GetInt32();
        var weapons=new Dictionary<string,Dictionary<string,ScreenPolygon[]>>();
        foreach(var weapon in root.GetProperty("weapons").EnumerateObject())
        {
            var states=new Dictionary<string,ScreenPolygon[]>();
            foreach(var state in weapon.Value.EnumerateObject())
                states[state.Name]=state.Value.EnumerateArray().Select(r=>
                {
                    double x=r[0].GetDouble()*sx-800,y=450-r[1].GetDouble()*sy,w=r[2].GetDouble()*sx,h=r[3].GetDouble()*sy;
                    // Scale the complete baked weapon/hand frame around the bottom-right viewport corner.
                    x=800+(x-800)*Scale;y=-450+(y+450)*Scale;w*=Scale;h*=Scale;
                    return new ScreenPolygon(new[]{new V(x,y-h,0),new V(x+w,y-h,0),new V(x+w,y,0),new V(x,y,0)},r[4].GetInt32());
                }).ToArray();
            weapons[weapon.Name]=states;
        }
        return weapons;
    }
    public static void Draw(List<ScreenPolygon> shapes,CombatSnapshot game)
    {
        if(game.Dead||(game.Scoped&&game.Weapon.Shape=="sniper"))return;
        // Five colour slides, 1.3x visual playback; ammunition transfers at the original reload deadline.
        string state=game.Reload>0?"reload"+Math.Min(4,(int)(game.Reload*1.3*5)):game.Flash?"fire":"idle";
        var frame=slides.Value[game.Weapon.Id][state];
        // At rest the original baked pose is exactly static. Only movement adds bob.
        double amount=Math.Min(1,game.MoveSpeed/180),x=Math.Sin(game.WalkPhase*Math.PI*2)*5*amount,y=Math.Abs(Math.Cos(game.WalkPhase*Math.PI*2))*4*amount;
        y+=game.Recoil*4;
        if(Math.Abs(x)+Math.Abs(y)<.001){shapes.AddRange(frame);return;}
        foreach(var shape in frame)shapes.Add(new(shape.Points.Select(p=>new V(p.X+x,p.Y+y,0)).ToArray(),shape.Color));
    }
}
