using System.IO.Compression;
using System.Text.Json;
namespace CadDustLab;

public static class ActorSlides
{
    private sealed record Part(V[] Points,int Color,int Kind);
    private static readonly Lazy<Dictionary<string,Dictionary<string,Part[][]>>> data=new(Load);
    public static int FrameCount=>data.Value.Values.Sum(states=>states.Values.Sum(angles=>angles.Length));
    private static Dictionary<string,Dictionary<string,Part[][]>> Load()
    {
        var asm=typeof(ActorSlides).Assembly;
        using var stream=asm.GetManifestResourceStream(asm.GetManifestResourceNames().Single(n=>n.EndsWith("actor-slides.json.gz")))!;
        using var gzip=new GZipStream(stream,CompressionMode.Decompress);using var json=JsonDocument.Parse(gzip);var root=json.RootElement;
        double cx=root.GetProperty("width").GetDouble()/2,ground=root.GetProperty("ground").GetDouble(),scale=root.GetProperty("scale").GetDouble();
        V P(double x,double y)=>new((x-cx)/scale,(ground-y)/scale,0);
        var result=new Dictionary<string,Dictionary<string,Part[][]>>();
        foreach(var team in root.GetProperty("actors").EnumerateObject())
        {
            var states=new Dictionary<string,Part[][]>();
            foreach(var state in team.Value.EnumerateObject())
            {
                states[state.Name]=state.Value.EnumerateArray().Select(frame=>
                {
                    var parts=new List<Part>();
                    foreach(var r in frame.GetProperty("mask").EnumerateArray())
                    {
                        double x=r[0].GetDouble(),y=r[1].GetDouble(),w=r[2].GetDouble(),h=r[3].GetDouble();
                        parts.Add(new(new[]{P(x,y+h),P(x+w,y+h),P(x+w,y),P(x,y)},DraftGeometry.Black,-1));
                    }
                    foreach(var path in frame.GetProperty("paths").EnumerateArray())
                    {
                        var points=path.GetProperty("points").EnumerateArray().Select(p=>P(p[0].GetDouble(),p[1].GetDouble())).ToArray();
                        bool closed=path.GetProperty("closed").GetBoolean();double width=path.GetProperty("width").GetDouble()/scale;
                        int color=path.GetProperty("color").GetInt32(),kind=path.GetProperty("kind").GetInt32();
                        for(int i=0;i<points.Length-(closed?0:1);i++)
                        {
                            var a=points[i];var b=points[(i+1)%points.Length];var d=b-a;if(d.Length<.01)continue;
                            var side=new V(-d.Y,d.X,0)*(width*.5/d.Length);
                            parts.Add(new(new[]{a-side,b-side,b+side,a+side},color,kind));
                        }
                    }
                    return parts.ToArray();
                }).ToArray();
            }
            result[team.Name]=states;
        }
        return result;
    }
    public static string State(ActorView actor)=>actor.Fall>0?"fall"+Math.Min(2,(int)(actor.Fall*3)):actor.Speed>8?(actor.Speed>150?"run":"walk")+((int)(actor.WalkPhase*4)%4):"idle";
    public static IEnumerable<Patch> Draw(ActorView actor,V eye,V right)
    {
        var delta=eye-actor.Eye;double angle=Math.Atan2(delta.Y,delta.X)+actor.Heading-Math.PI/2;
        int direction=((int)Math.Round(angle/(Math.PI/4))%8+8)%8;
        var parts=data.Value[actor.IsCt?"ct":"t"][State(actor)][direction];var foot=actor.Eye-new V(0,0,64);
        foreach(var part in parts)
        {
            if(delta.Length>1000&&part.Kind>0)continue;
            yield return new(part.Points.Select(p=>foot+right*p.X+new V(0,0,p.Y)).ToArray(),part.Color,FaceId:-100-actor.Id);
        }
    }
}
