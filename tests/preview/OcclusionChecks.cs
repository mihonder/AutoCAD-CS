using CadDustLab;
using System.Text.Json;

static class OcclusionChecks
{
    public static void Run(Scene scene)
    {
        using var json=JsonDocument.Parse(File.ReadAllText("../../assets/scene.json"));var r=json.RootElement;
        int worldEnd=r.GetProperty("worldFirstFace").GetInt32()+r.GetProperty("worldFaceCount").GetInt32();
        var faces=r.GetProperty("faces").EnumerateArray().Select(f=>(Id:f.GetProperty("id").GetInt32(),Points:f.GetProperty("points").EnumerateArray().Select(V.Read).ToArray(),Normal:V.Read(f.GetProperty("normal")))).ToArray();
        bool Inside(V[] polygon,V point,V normal)
        {
            bool positive=false,negative=false;
            for(int i=0;i<polygon.Length;i++)
            {
                var a=polygon[i];double side=(polygon[(i+1)%polygon.Length]-a).Cross(point-a).Dot(normal);
                positive|=side>1e-5;negative|=side< -1e-5;if(positive&&negative)return false;
            }
            return true;
        }
        int checkedCrates=0;
        var game=new Combat(scene).Snapshot() with{Actors=Array.Empty<ActorView>(),Effects=Array.Empty<Impact>(),Notice="",Menu=-1};
        foreach(var hint in new[]{scene.TerroristSpawns[0],new V(-1200,-500,192),new V(1152,2640,160),new V(-1850,2300,64)})
        for(int angle=0;angle<8;angle++)
        {
            var eye=scene.GroundNear(hint);double yaw=angle*Math.PI/4;var forward=new V(Math.Sin(yaw),Math.Cos(yaw),0);var right=new V(Math.Cos(yaw),-Math.Sin(yaw),0);
            var frame=scene.Render(0,new(eye,yaw,0),game);
            if(hint==scene.TerroristSpawns[0]&&angle%2==0)File.WriteAllText($"../../frame-t-crates-{angle}.json",JsonSerializer.Serialize(frame));
            for(int x=-650;x<=650;x+=100)for(int y=-160;y<=260;y+=70)
            {
                var direction=(forward+right*(x/720.0)+new V(0,0,y/720.0)).Unit;double best=double.PositiveInfinity;var closest=new HashSet<int>();
                foreach(var f in faces)
                {
                    double denominator=f.Normal.Dot(direction);if(denominator>=-1e-8)continue;
                    double t=f.Normal.Dot(f.Points[0]-eye)/denominator;if(t<4||t>best+.05)continue;
                    if(!Inside(f.Points,eye+direction*t,f.Normal))continue;
                    if(t<best-.05){best=t;closest.Clear();}closest.Add(f.Id);
                }
                if(closest.Count==0||closest.All(id=>id<worldEnd))continue;
                int drawn=-1;
                foreach(var p in frame.Polygons)
                    if(Inside(p.Points,new V(x,y,0),new V(0,0,1)))drawn=p.FaceId;
                if(drawn<0)continue; // weapon/sky overlay, not a map sample
                if(!closest.Contains(drawn))throw new Exception($"Brush face occlusion: eye {eye}, yaw {yaw:F2}, pixel {x},{y}, wanted {string.Join(",",closest)}, drew {drawn}");
                checkedCrates++;
            }
        }
        if(checkedCrates<30)throw new Exception("Insufficient crate face samples");
        Console.WriteLine($"OCCLUSION CHECKS PASSED: {checkedCrates} brush pixels match nearest ray-traced BSP face across T/A/B viewpoints.");
    }
}
