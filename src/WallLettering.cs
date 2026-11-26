using System.Text.Json;
namespace CadDustLab;

public static class WallLettering
{
    public static readonly CameraPose[] PreviewViews={
        new(new(1408,1430,64),Math.PI/2,.56),
        new(new(-410,2250,-64),0,.19)
    };
    public static int Apply(Dictionary<int,DraftMark[]> drawing,DraftFace[] faces)
    {
        var asm=typeof(WallLettering).Assembly;
        using var stream=asm.GetManifestResourceStream(asm.GetManifestResourceNames().Single(n=>n.EndsWith("wall-lettering.json")))!;
        using var json=JsonDocument.Parse(stream);var data=json.RootElement;
        double width=data.GetProperty("width").GetDouble(),height=data.GetProperty("height").GetDouble();
        var paths=data.GetProperty("paths").EnumerateArray().Select(path=>path.EnumerateArray().Select(p=>new V(p[0].GetDouble(),p[1].GetDouble(),0)).ToArray()).ToArray();
        // Long A: right wall when coming from T. CT->B: outside wall opposite mid doors.
        var locations=new[]{(Origin:new V(1600,1560,190),Right:new V(0,-1,0),Normal:new V(-1,0,0)),
                            (Origin:new V(-550,2510,-35),Right:new V(1,0,0),Normal:new V(0,-1,0))};
        int count=0;
        foreach(var place in locations)
        foreach(var face in faces)
        {
            if(face.Normal.Dot(place.Normal)<.85||Math.Abs((face.Points[0]-place.Origin).Dot(place.Normal))>85)continue;
            V Map(V uv)
            {
                var p=place.Origin+place.Right*uv.X+new V(0,0,uv.Y);
                return p+place.Normal*((face.Points[0]-p).Dot(face.Normal)/place.Normal.Dot(face.Normal));
            }
            var marks=new List<DraftMark>();
            foreach(var path in paths)for(int i=1;i<path.Length;i++)marks.Add(new(Map(path[i-1]),Map(path[i]),DraftGeometry.White,.95));
            var clipped=DraftGeometry.ClipMarks(marks.ToArray(),face.Points,face.Normal);
            if(clipped.Length==0)continue;
            count+=clipped.Length;
            // Keep text legible by removing hatch only inside its wall-aligned bounds.
            var kept=new List<DraftMark>();
            foreach(var mark in drawing[face.Id])
            {
                if(mark.Color==DraftGeometry.White){kept.Add(mark);continue;}
                var a=mark.A-place.Origin;var b=mark.B-place.Origin;
                double ax=a.Dot(place.Right),ay=a.Z,dx=(b-a).Dot(place.Right),dy=b.Z-a.Z,lo=0,hi=1;
                bool Clip(double value,double delta,double min,double max)
                {
                    if(Math.Abs(delta)<1e-9)return value>=min&&value<=max;
                    double p=(min-value)/delta,q=(max-value)/delta;
                    lo=Math.Max(lo,Math.Min(p,q));hi=Math.Min(hi,Math.Max(p,q));return hi>lo;
                }
                if(!Clip(ax,dx,-9,width+9)||!Clip(ay,dy,-8,height+8)){kept.Add(mark);continue;}
                var d=mark.B-mark.A;
                if(lo>0)kept.Add(mark with{B=mark.A+d*lo});
                if(hi<1)kept.Add(mark with{A=mark.A+d*hi});
            }
            drawing[face.Id]=kept.Concat(clipped).ToArray();
        }
        return count;
    }
}
