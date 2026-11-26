using System.Text.Json;
namespace CadDustLab;

// The original map's NAV rectangles and links, with a small independent A*.
public sealed class Navigation
{
    public sealed record Area(int Id,int Flags,V Min,V Max,double NE,double SW,int[][] Links)
    {
        public double Height(double x,double y)
        {double u=Compat.Clamp((x-Min.X)/Math.Max(1,Max.X-Min.X),0,1),v=Compat.Clamp((y-Min.Y)/Math.Max(1,Max.Y-Min.Y),0,1);return (Min.Z*(1-u)+NE*u)*(1-v)+(SW*(1-u)+Max.Z*u)*v;}
        public V Center=>new((Min.X+Max.X)/2,(Min.Y+Max.Y)/2,(Min.Z+Max.Z+NE+SW)/4+64.03);
        public V Closest(V eye)
        {double x=Compat.Clamp(eye.X,Min.X,Max.X),y=Compat.Clamp(eye.Y,Min.Y,Max.Y);return new(x,y,Height(x,y)+64.03);}
    }
    public readonly Dictionary<int,Area> Areas=new();
    public Navigation(JsonElement json)
    {
        foreach(var a in json.EnumerateArray())
        {
            var b=a.GetProperty("box").EnumerateArray().Select(x=>x.GetDouble()).ToArray();var h=a.GetProperty("heights");
            var area=new Area(a.GetProperty("id").GetInt32(),a.GetProperty("flags").GetInt32(),new(b[0],b[1],b[2]),new(b[3],b[4],b[5]),h[0].GetDouble(),h[1].GetDouble(),a.GetProperty("links").EnumerateArray().Select(x=>x.EnumerateArray().Select(v=>v.GetInt32()).ToArray()).ToArray());
            Areas.Add(area.Id,area);
        }
    }
    public Area Nearest(V eye)=>Compat.MinBy(Areas.Values,a=>(a.Closest(eye)-eye).Length)!;
    public List<V> Path(V from,V to,HashSet<int>? avoid=null)
    {
        int start=Nearest(from).Id,end=Nearest(to).Id;
        var queue=new MinQueue<int>();var costs=new Dictionary<int,double>{{start,0}};var previous=new Dictionary<int,int>();queue.Enqueue(start,0);
        while(queue.TryDequeue(out int id,out _))
        {
            if(id==end)break;var area=Areas[id];
            foreach(var link in area.Links)
            {
                var next=Areas[link[0]];if((next.Flags&3)!=0||avoid?.Contains(next.Id)==true)continue;
                double cost=costs[id]+(next.Center-area.Center).Length;
                if(cost>=costs.GetValueOrDefault(next.Id,double.PositiveInfinity))continue;
                costs[next.Id]=cost;previous[next.Id]=id;queue.Enqueue(next.Id,cost+(next.Center-to).Length);
            }
        }
        if(start!=end&&!previous.ContainsKey(end))return new();
        var chain=new List<int>{end};while(chain[chain.Count-1]!=start)chain.Add(previous[chain[chain.Count-1]]);chain.Reverse();
        var points=new List<V>();
        for(int i=0;i<chain.Count-1;i++)
        {
            var a=Areas[chain[i]];var b=Areas[chain[i+1]];int direction=a.Links.First(l=>l[0]==b.Id)[1];
            double x,y;V normal;
            if(direction is 0 or 2)
            {x=(Math.Max(a.Min.X,b.Min.X)+Math.Min(a.Max.X,b.Max.X))/2;y=direction==0?a.Min.Y:a.Max.Y;normal=new(0,direction==0?-1:1,0);}
            else
            {x=direction==1?a.Max.X:a.Min.X;y=(Math.Max(a.Min.Y,b.Min.Y)+Math.Min(a.Max.Y,b.Max.Y))/2;normal=new(direction==1?1:-1,0,0);}
            var portal=new V(x,y,0);var before=portal-normal*12;var after=portal+normal*12;
            points.Add(new(before.X,before.Y,a.Height(before.X,before.Y)+64.03));
            points.Add(new(after.X,after.Y,b.Height(after.X,after.Y)+64.03));
        }
        points.Add(Areas[end].Closest(to));return points;
    }
}
