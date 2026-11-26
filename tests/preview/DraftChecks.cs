using CadDustLab;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

static class DraftChecks
{
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    static bool Mono(int c)=>((c>>16)&255)==((c>>8)&255)&&((c>>8)&255)==(c&255);
    public static void Run()
    {
        var sw=Stopwatch.StartNew();var scene=new Scene(File.ReadAllText("../../assets/scene.json"),draft:true);
        Console.WriteLine($"Draft: {scene.FaceCount} faces, {scene.DrawingMarkCount} cached vector marks, load {sw.ElapsedMilliseconds} ms");
        Check(scene.FaceCount==4002,"Draft changed map geometry");Check(ActorSlides.FrameCount==192,"Actor frames incomplete");
        RoundChecks.CheckUnchangedWeaponPoses();
        var player=new Player(scene);var combat=new Combat(scene);combat.Buy(Combat.Catalog.First(w=>w.Id=="ak47"));
        var game=combat.Snapshot() with{Actors=Array.Empty<ActorView>(),Notice=""};var costs=new List<double>();
        var locations=new[]{("long",scene.Position(0),player.Yaw),("a",new V(1152,2640,160),Math.PI),("b",new V(-1850,2300,64),0d),("t",scene.TerroristSpawns[0],Math.PI/2),("mid",new V(-384,1068,-11),0d),("tunnels",new V(-1686,800,96),0d)};
        foreach(var (name,hint,yaw) in locations)
        for(int i=0;i<12;i++)
        {
            var eye=scene.GroundNear(hint);sw.Restart();var frame=scene.Render(0,new(eye,yaw+i*Math.PI/6,0),game);VectorBatch.Build(frame);sw.Stop();costs.Add(sw.Elapsed.TotalMilliseconds);
            Check(frame.VisibleFaces>0&&frame.Polygons.Count<80000,$"Bad draft frame {name}/{i}");
            Check(frame.Polygons.Where(p=>p.FaceId>=0).All(p=>Mono(p.Color)),"Map retained coloured materials");
            Check(frame.Labels.All(l=>l.Color==DraftGeometry.White)&&frame.Strokes.All(s=>s.Color==DraftGeometry.White),"HUD is not white CAD text");
            if(i==0)File.WriteAllText($"../../frame-draft-{name}.json",JsonSerializer.Serialize(frame));
        }
        foreach(var (name,snapshot) in new[]{("menu",game with{Menu=4}),("score",game with{Score=true})})
            File.WriteAllText($"../../frame-draft-{name}.json",JsonSerializer.Serialize(scene.Render(0,player.Camera,snapshot)));
        // A wall must hide all parts of the new actor sprites, including their white paths.
        var end=player.Eye+new V(4096,0,0);double hit=scene.Trace(player.Eye,end);var behind=player.Eye+new V(hit*4096+80,0,0);
        var blocked=scene.Render(0,new(player.Eye,Math.PI/2,0),game with{Actors=new[]{new ActorView(77,behind,true,0,0,false)}});
        int actorPixels=0;
        bool Inside(V[] p,V q)
        {
            bool inside=false;for(int i=0,j=p.Length-1;i<p.Length;j=i++)if((p[i].Y>q.Y)!=(p[j].Y>q.Y)&&q.X<(p[j].X-p[i].X)*(q.Y-p[i].Y)/(p[j].Y-p[i].Y)+p[i].X)inside=!inside;
            return inside;
        }
        for(int x=-350;x<=350;x+=7)for(int y=-200;y<=300;y+=7)
        {
            var drawn=blocked.Polygons.LastOrDefault(p=>Inside(p.Points,new(x,y,0)));
            if(drawn?.FaceId==-177)actorPixels++;
        }
        Check(actorPixels==0,"Actor linework showed through BSP wall");
        var forward=(scene.Position(100)-player.Eye).Unit;var right=new V(forward.Y,-forward.X,0).Unit;
        var near=scene.GroundNear(player.Eye+forward*170);
        for(int i=0;i<4;i++)
        {
            var ct=new ActorView(1,near+right*26,true,i*.25,0,false){Speed=220,Heading=player.Yaw+Math.PI};
            var t=new ActorView(2,near-right*26,false,i*.25,0,false){Speed=130,Heading=player.Yaw+Math.PI};
            var frame=scene.Render(i*.1,player.Camera,game with{Actors=new[]{ct,t}});VectorBatch.Build(frame);
            File.WriteAllText($"../../frame-draft-bots-{i}.json",JsonSerializer.Serialize(frame));
        }
        // Exercise every baked pose/direction through BSP clipping and triangulation.
        foreach(bool enemy in new[]{false,true})
        for(int pose=0;pose<12;pose++)
        for(int direction=0;direction<8;direction++)
        {
            double speed=pose is >=1 and <=4?130:pose is >=5 and <=8?220:0;
            double phase=pose==0||pose>=9?0:((pose-1)%4)*.25;
            double fall=pose>=9?(pose-9+.1)/3:0;
            var actor=new ActorView(1,near,enemy,phase,fall,false){Speed=speed,Heading=player.Yaw+Math.PI+direction*Math.PI/4};
            var frame=scene.Render(0,player.Camera,game with{Actors=new[]{actor}});VectorBatch.Build(frame);
            Check(frame.Polygons.Any(p=>p.FaceId==-101),$"Empty actor pose {enemy}/{pose}/{direction}");
            Check(frame.Polygons.Where(p=>p.FaceId==-101).All(p=>Mono(p.Color)),"Actor retained colour");
            if(enemy&&direction==0&&pose>=9)File.WriteAllText($"../../frame-draft-fall-{pose-9}.json",JsonSerializer.Serialize(frame));
        }
        var normal=new V(0,0,1);double[] u={1,0,0,0},v={0,1,0,0};
        var marks=DraftGeometry.Build(new[]{new DraftFace(1,new[]{new V(0,0,0),new V(10,0,0),new V(10,10,0),new V(0,10,0)},normal,"test",u,v),new DraftFace(2,new[]{new V(10,0,0),new V(20,0,0),new V(20,10,0),new V(10,10,0)},normal,"test",u,v)});
        Check(!marks.Values.SelectMany(m=>m).Any(m=>m.Color==DraftGeometry.White&&Math.Abs(m.A.X-10)<.001&&Math.Abs(m.B.X-10)<.001),"Coplanar subdivision seam remained");
        OcclusionChecks.Run(scene);
        costs.Sort();Console.WriteLine($"DRAFT CHECKS PASSED: unchanged geometry/weapons, 192 actor slides, wall occlusion, coplanar seams, monochrome HUD/map; 72 views p95 {costs[(int)(costs.Count*.95)]:F2} ms");
    }
}
