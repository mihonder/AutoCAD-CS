namespace CadDustLab;

public sealed record DraftMark(V A,V B,int Color,double Width,int Index=0,double Spacing=0);
public sealed record DraftFace(int Id,V[] Points,V Normal,string Texture,double[] S,double[] T);

// World-attached drawing marks, prepared once. Each opaque face and its marks are
// submitted together in BSP paint order, so near surfaces hide distant linework.
public static class DraftGeometry
{
    public const int Black=0x101010,White=0xe6e6e6,Hatch=0x747474;
    private sealed record Edge(int Face,V A,V B);
    private static string Key(V p,double precision=1000)=>$"{(long)Math.Round(p.X*precision)},{(long)Math.Round(p.Y*precision)},{(long)Math.Round(p.Z*precision)}";
    public static Dictionary<int,DraftMark[]> Build(DraftFace[] faces)
    {
        var edges=new Dictionary<string,List<Edge>>();var outlines=faces.ToDictionary(f=>f.Id,_=>new List<DraftMark>());
        foreach(var face in faces)
        for(int i=0;i<face.Points.Length;i++)
        {
            var a=face.Points[i];var b=face.Points[(i+1)%face.Points.Length];var direction=(b-a).Unit;
            if(direction.X<-.00001||(Math.Abs(direction.X)<.00001&&direction.Y<-.00001)||(Math.Abs(direction.X)+Math.Abs(direction.Y)<.00001&&direction.Z<0))direction*=-1;
            var origin=a-direction*a.Dot(direction);
            string key=$"{Key(face.Normal)}:{(long)Math.Round(face.Normal.Dot(a)*100)}:{face.Texture}:{Key(direction)}:{Key(origin,100)}";
            if(!edges.TryGetValue(key,out var list))edges[key]=list=new();list.Add(new(face.Id,a,b));
        }
        foreach(var bucket in edges.Values)
        foreach(var edge in bucket)
        {
            var direction=(edge.B-edge.A).Unit;double length=(edge.B-edge.A).Length;
            var intervals=new List<(double A,double B)>{(0,length)};
            // Remove coplanar subdivision seams, including edges with T-junctions.
            foreach(var other in bucket)
            {
                if(other.Face==edge.Face)continue;
                double x=(other.A-edge.A).Dot(direction),y=(other.B-edge.A).Dot(direction),lo=Math.Min(x,y),hi=Math.Max(x,y);
                var remaining=new List<(double,double)>();
                foreach(var (a,b) in intervals)
                {
                    if(hi<=a+.001||lo>=b-.001){remaining.Add((a,b));continue;}
                    if(lo>a+.001)remaining.Add((a,Math.Min(lo,b)));
                    if(hi<b-.001)remaining.Add((Math.Max(hi,a),b));
                }
                intervals=remaining;
            }
            foreach(var (a,b) in intervals)if(b-a>.05)outlines[edge.Face].Add(new(edge.A+direction*a,edge.A+direction*b,White,1.1));
        }
        return faces.ToDictionary(f=>f.Id,f=>Pattern(f).Concat(outlines[f.Id]).ToArray());
    }
    private static IEnumerable<DraftMark> Pattern(DraftFace face)
    {
        var result=new List<DraftMark>();string name=face.Texture.ToLowerInvariant();
        if(name is "black" or "sky" or "clip" or "aaatrigger")return result;
        var u=new V(face.S[0],face.S[1],face.S[2]);var v=new V(face.T[0],face.T[1],face.T[2]);
        void Lines(double a,double b,double step,double offset,int color,double width,bool lod=true)
        {
            var axis=u*a+v*b;double bias=face.S[3]*a+face.T[3]*b;
            var values=face.Points.Select(p=>p.Dot(axis)+bias).ToArray();
            int first=(int)Math.Ceiling((values.Min()-offset)/step),last=(int)Math.Floor((values.Max()-offset)/step);
            for(int n=first;n<=last;n++)
            {
                double cut=n*step+offset;var intersections=new List<V>();
                for(int i=0;i<values.Length;i++)
                {
                    int j=(i+1)%values.Length;double da=values[i]-cut,db=values[j]-cut;
                    if(Math.Abs(da)<1e-6)intersections.Add(face.Points[i]);
                    if((da<0)!=(db<0)&&Math.Abs(da-db)>1e-8)intersections.Add(face.Points[i]+(face.Points[j]-face.Points[i])*(da/(da-db)));
                }
                if(intersections.Count<2)continue;
                var tangent=face.Normal.Cross(axis).Unit;var sorted=intersections.OrderBy(p=>p.Dot(tangent)).ToArray();
                if((sorted[sorted.Length-1]-sorted[0]).Length>.1)result.Add(new(sorted[0],sorted[sorted.Length-1],color,width,n,lod?step/Math.Max(.001,axis.Length):0));
            }
        }
        bool crate=name.Contains("crte")||name.Contains("crt");
        if(crate)
        {
            Lines(1,1,24,0,0x595959,.55);
            Lines(1,0,128,6,0xbcbcbc,.9,false);Lines(1,0,128,122,0xbcbcbc,.9,false);
            Lines(0,1,128,6,0xbcbcbc,.9,false);Lines(0,1,128,122,0xbcbcbc,.9,false);
            Lines(1,1,128,0,0xb0b0b0,.9,false);Lines(1,-1,128,0,0xb0b0b0,.9,false);
        }
        else if(Math.Abs(face.Normal.Z)>.65)
        {
            Lines(1,0,32,0,0x373737,.5);Lines(0,1,32,0,0x373737,.5);
            Lines(1,0,128,0,0x696969,.7,false);Lines(0,1,128,0,0x696969,.7,false);
        }
        else if(name.Contains("door"))
        {
            Lines(1,0,16,0,0x898989,.75);Lines(0,1,96,0,0xc0c0c0,.9,false);
            Lines(1,1,32,0,0x4f4f4f,.5);
        }
        else if(name.Contains("wndw"))
        {
            Lines(1,0,16,0,0xa0a0a0,.8);Lines(0,1,64,0,0xb0b0b0,.9,false);
        }
        else
        {
            Lines(1,1,12,0,Hatch,.65);
            if(name.Contains("bta")||name.Contains("trim"))Lines(1,-1,24,0,0x505050,.5);
            Lines(0,1,128,0,0x999999,.8,false);
        }
        return result;
    }
    public static DraftMark[] ClipMarks(DraftMark[] marks,V[] polygon,V normal)
    {
        var center=polygon.Aggregate(new V(),(a,b)=>a+b)*(1.0/polygon.Length);var output=new List<DraftMark>();
        foreach(var mark in marks)
        {
            double low=0,high=1;var d=mark.B-mark.A;
            for(int i=0;i<polygon.Length&&low<=high;i++)
            {
                var a=polygon[i];var inward=normal.Cross(polygon[(i+1)%polygon.Length]-a).Unit;
                if((center-a).Dot(inward)<0)inward*=-1;
                double start=(mark.A-a).Dot(inward),change=d.Dot(inward);
                if(Math.Abs(change)<1e-9){if(start<-.001)high=-1;continue;}
                double t=-start/change;if(change>0)low=Math.Max(low,t);else high=Math.Min(high,t);
            }
            if(high-low>.0001)output.Add(mark with{A=mark.A+d*low,B=mark.A+d*high});
        }
        return output.ToArray();
    }
    public static void Project(List<ScreenPolygon> output,DraftMark mark,V eye,V right,V up,V forward,double focal,int faceId,double distance)
    {
        if(mark.Spacing>0)
        {
            int stride=1;while(mark.Spacing*focal*stride/Math.Max(1,distance)<3.5&&stride<64)stride*=2;
            if(mark.Index%stride!=0)return;
        }
        V View(V point){var d=point-eye;return new(d.Dot(right),d.Dot(up),d.Dot(forward));}
        var a=View(mark.A);var b=View(mark.B);double low=0,high=1;
        double Plane(V p,int n)=>n switch{0=>p.Z-4,1=>8192-p.Z,2=>p.X+p.Z*800/focal,3=>p.Z*800/focal-p.X,4=>p.Y+p.Z*450/focal,_=>p.Z*450/focal-p.Y};
        for(int plane=0;plane<6;plane++)
        {
            double da=Plane(a,plane),db=Plane(b,plane);
            if(da<0&&db<0)return;
            if((da<0)!=(db<0)){double t=da/(da-db);if(da<0)low=Math.Max(low,t);else high=Math.Min(high,t);}
        }
        if(low>=high)return;var d=b-a;b=a+d*high;a+=d*low;
        AddLine(output,new(focal*a.X/a.Z,focal*a.Y/a.Z,0),new(focal*b.X/b.Z,focal*b.Y/b.Z,0),mark.Color,mark.Width,faceId);
    }
    public static void AddLine(List<ScreenPolygon> output,V a,V b,int color,double width,int faceId=-1)
    {
        var d=b-a;double length=d.Length;if(length<.15)return;
        var side=new V(-d.Y,d.X,0)*(width*.5/length);
        output.Add(new(new[]{a-side,b-side,b+side,a+side},color,faceId));
    }
}
