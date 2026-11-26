using System.IO.Compression;
using System.Text.Json;
namespace CadDustLab;

public static class DroppedWeapons
{
    private static readonly Lazy<Dictionary<string,ScreenPolygon[]>> data=new(Load);
    public static int Count=>data.Value.Count;
    private static Dictionary<string,ScreenPolygon[]> Load()
    {
        var asm=typeof(DroppedWeapons).Assembly;
        using var source=asm.GetManifestResourceStream(asm.GetManifestResourceNames().Single(n=>n.EndsWith("world-weapons.json.gz")))!;
        using var gzip=new GZipStream(source,CompressionMode.Decompress);using var doc=JsonDocument.Parse(gzip);var root=doc.RootElement;
        double cx=root.GetProperty("width").GetDouble()/2,cy=root.GetProperty("height").GetDouble()/2;
        var result=new Dictionary<string,ScreenPolygon[]>();
        foreach(var weapon in root.GetProperty("weapons").EnumerateObject())
        {
            var shapes=new List<ScreenPolygon>();double scale=weapon.Value.GetProperty("scale").GetDouble();
            V P(double x,double y)=>new((x-cx)/scale,(cy-y)/scale,0);
            foreach(var r in weapon.Value.GetProperty("mask").EnumerateArray())
            {
                double x=r[0].GetDouble(),y=r[1].GetDouble(),w=r[2].GetDouble(),h=r[3].GetDouble();
                shapes.Add(new(new[]{P(x,y),P(x+w,y),P(x+w,y+h),P(x,y+h)},DraftGeometry.Black));
            }
            foreach(var path in weapon.Value.GetProperty("paths").EnumerateArray())
            {
                // Ground weapons retain contours/details, without fine body hatching.
                if(path.GetProperty("kind").GetInt32()>1)continue;
                var p=path.GetProperty("points").EnumerateArray().Select(v=>P(v[0].GetDouble(),v[1].GetDouble())).ToArray();bool closed=path.GetProperty("closed").GetBoolean();
                for(int i=0;i<p.Length-(closed?0:1);i++)
                {
                    var a=p[i];var b=p[(i+1)%p.Length];var d=b-a;if(d.Length<.01)continue;
                    var side=new V(-d.Y,d.X,0)*(.1/d.Length);shapes.Add(new(new[]{a-side,b-side,b+side,a+side},DraftGeometry.White));
                }
            }
            result[weapon.Name]=shapes.ToArray();
        }
        return result;
    }
    public static IEnumerable<Patch> Draw(DropView drop)
    {
        var x=new V(Math.Sin(drop.Heading),Math.Cos(drop.Heading),0);var y=new V(Math.Cos(drop.Heading),-Math.Sin(drop.Heading),0);
        foreach(var p in data.Value[drop.Weapon])yield return new(p.Points.Select(v=>drop.Position+x*v.X+y*v.Y).ToArray(),p.Color,FaceId:-10000-drop.Id);
    }
}
