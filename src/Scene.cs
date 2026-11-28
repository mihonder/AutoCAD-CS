using System.Text.Json;
using System.Text.Json.Serialization;
namespace CadDustLab;

public readonly record struct V(double X,double Y,double Z)
{
    public static V operator +(V a,V b)=>new(a.X+b.X,a.Y+b.Y,a.Z+b.Z);
    public static V operator -(V a,V b)=>new(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
    public static V operator *(V a,double b)=>new(a.X*b,a.Y*b,a.Z*b);
    public double Dot(V b)=>X*b.X+Y*b.Y+Z*b.Z;
    public V Cross(V b)=>new(Y*b.Z-Z*b.Y,Z*b.X-X*b.Z,X*b.Y-Y*b.X);
    [JsonIgnore] public double Length=>Math.Sqrt(Dot(this));
    [JsonIgnore] public V Unit=>this*(1/Math.Max(1e-9,Length));
    public static V Read(JsonElement a)=>new(a[0].GetDouble(),a[1].GetDouble(),a[2].GetDouble());
    public override string ToString()=>$"({X:F1},{Y:F1},{Z:F1})";
}
public sealed record Patch(V[] Points,int Color,V? Normal=null,int FaceId=-1)
{public V Center {get;}=Points.Aggregate(new V(),(a,b)=>a+b)*(1.0/Points.Length);public DraftMark[]? Marks {get;init;}}
public sealed record ScreenPolygon(V[] Points,int Color,int FaceId=-1);
public sealed record ScreenLabel(V Position,string Text,double Height=20,int Color=0x171918);
public sealed record ScreenStroke(V[] Points,bool Closed=false,int Color=0x171918,int Weight=30);
public sealed record Frame(List<ScreenPolygon> Polygons,V Camera,int VisibleFaces,int VisiblePatches)
{public List<ScreenLabel> Labels {get;init;}=new();public List<ScreenStroke> Strokes {get;init;}=new();}

public sealed class Scene
{
    private sealed record Face(int Id,V[] Points,V Normal,int Texture,double[] S,double[] T,List<Patch> Patches)
    {internal V Center=Points.Aggregate(new V(),(a,b)=>a+b)*(1.0/Points.Length);internal int FlatColor;}
    private sealed record Texture(string Name,int Width,int Height,int[] Colors);
    private readonly Dictionary<int,Face> faces=new();
    private readonly Texture[] textures;
    private readonly double[][] planes;
    private readonly int[][] nodes;
    private readonly int[] leaves;
    private readonly int root;
    private readonly int[][] clipnodes;
    private readonly int[] playerClipRoots;
    private readonly int[] duckClipRoots;
    private readonly int[] pointClipRoots;
    private readonly byte[][] visibility;
    private readonly int[][] leafFaces;
    private readonly int faceCapacity;
    private readonly V minimum,maximum;
    public readonly V[] TerroristSpawns,CounterSpawns;
    public readonly Navigation Navigation;
    private readonly V[] route;
    private readonly double[] distances;
    private readonly Dictionary<int,List<Patch>> staticLeaves=new();
    private readonly List<Face> extras=new();
    public bool Draft {get;}
    private readonly Dictionary<int,DraftMark[]> drawing=new();
    private readonly Lazy<PlanDrawing> plan=new(()=>new PlanDrawing());
    public int DrawingMarkCount=>drawing.Values.Sum(m=>m.Length);
    public int LetteringMarkCount {get;}
    public int FaceCount=>faces.Count+extras.Count;
    public int PatchCount=>faces.Values.Sum(f=>f.Patches.Count)+extras.Sum(f=>f.Patches.Count);
    public double RouteLength=>distances[distances.Length-1];
    public const double Duration=44;
    public Scene(string json,bool draft=false)
    {
        Draft=draft;
        using var d=JsonDocument.Parse(json);var r=d.RootElement;
        textures=r.GetProperty("textures").EnumerateArray().Select(t=>new Texture(t.GetProperty("name").GetString()!,t.GetProperty("width").GetInt32(),t.GetProperty("height").GetInt32(),t.GetProperty("colors").EnumerateArray().Select(x=>x.GetInt32()).ToArray())).ToArray();
        planes=r.GetProperty("planes").EnumerateArray().Select(p=>p.EnumerateArray().Select(x=>x.GetDouble()).ToArray()).ToArray();
        nodes=r.GetProperty("nodes").EnumerateArray().Select(p=>p.EnumerateArray().Select(x=>x.GetInt32()).ToArray()).ToArray();
        leaves=r.GetProperty("leaves").EnumerateArray().Select(x=>x.GetInt32()).ToArray();root=r.GetProperty("root").GetInt32();
        clipnodes=r.GetProperty("clipnodes").EnumerateArray().Select(p=>p.EnumerateArray().Select(x=>x.GetInt32()).ToArray()).ToArray();
        playerClipRoots=r.GetProperty("playerClipRoots").EnumerateArray().Select(x=>x.GetInt32()).ToArray();
        duckClipRoots=r.GetProperty("duckClipRoots").EnumerateArray().Select(x=>x.GetInt32()).ToArray();
        pointClipRoots=r.GetProperty("pointClipRoots").EnumerateArray().Select(x=>x.GetInt32()).ToArray();
        visibility=r.GetProperty("visibility").EnumerateArray().Select(x=>Convert.FromBase64String(x.GetString()!)).ToArray();
        leafFaces=r.GetProperty("leafFaces").EnumerateArray().Select(x=>x.EnumerateArray().Select(v=>v.GetInt32()).ToArray()).ToArray();
        minimum=V.Read(r.GetProperty("bounds")[0]);maximum=V.Read(r.GetProperty("bounds")[1]);
        Navigation=new Navigation(r.GetProperty("navigation"));
        V[] Spawns(string name)=>r.GetProperty("entities").EnumerateArray().Where(e=>e.GetProperty("classname").GetString()==name).Select(e=>{var p=e.GetProperty("origin").GetString()!.Split(' ').Select(x=>double.Parse(x,System.Globalization.CultureInfo.InvariantCulture)).ToArray();return new V(p[0],p[1],p[2]+28);}).ToArray();
        TerroristSpawns=Spawns("info_player_deathmatch");CounterSpawns=Spawns("info_player_start");
        route=r.GetProperty("route").EnumerateArray().Select(V.Read).ToArray();
        distances=new double[route.Length];for(int i=1;i<route.Length;i++)distances[i]=distances[i-1]+(route[i]-route[i-1]).Length;
        int worldEnd=r.GetProperty("worldFirstFace").GetInt32()+r.GetProperty("worldFaceCount").GetInt32();
        foreach(var f in r.GetProperty("faces").EnumerateArray())
        {
            var face=new Face(f.GetProperty("id").GetInt32(),f.GetProperty("points").EnumerateArray().Select(V.Read).ToArray(),V.Read(f.GetProperty("normal")),f.GetProperty("texture").GetInt32(),f.GetProperty("s").EnumerateArray().Select(x=>x.GetDouble()).ToArray(),f.GetProperty("t").EnumerateArray().Select(x=>x.GetDouble()).ToArray(),new());
            MakePatches(face);
            for(int i=0;i<face.Patches.Count;i++)face.Patches[i]=face.Patches[i] with{FaceId=face.Id};
            face.FlatColor=textures[face.Texture].Colors[27];
            if(face.Id<worldEnd)faces.Add(face.Id,face);else extras.Add(face);
        }
        if(Draft)
        {
            var all=faces.Values.Concat(extras).ToArray();
            drawing=DraftGeometry.Build(all.Select(f=>new DraftFace(f.Id,f.Points,f.Normal,textures[f.Texture].Name,f.S,f.T)).ToArray());
            LetteringMarkCount=WallLettering.Apply(drawing,all.Select(f=>new DraftFace(f.Id,f.Points,f.Normal,textures[f.Texture].Name,f.S,f.T)).ToArray());
            foreach(var f in extras)Insert(root,new(f.Points,DraftGeometry.Black,f.Normal,f.Id),staticLeaves);
            foreach(var list in staticLeaves.Values)for(int i=0;i<list.Count;i++)
            {var p=list[i];list[i]=p with{Marks=DraftGeometry.ClipMarks(drawing[p.FaceId],p.Points,p.Normal!.Value)};}
        }
        else foreach(var f in extras)foreach(var p in f.Patches)Insert(root,p with{Normal=f.Normal},staticLeaves);
        faceCapacity=faces.Keys.Max()+1;
    }
    private static double UV(V p,double[] axis)=>p.X*axis[0]+p.Y*axis[1]+p.Z*axis[2]+axis[3];
    private static V[] Clip(V[] input,Func<V,double> distance)
    {
        if(input.Length<3)return Array.Empty<V>();
        var output=new List<V>(input.Length+2);V a=input[input.Length-1];double da=distance(a);
        foreach(var b in input)
        {
            double db=distance(b);
            if((da>=-1e-7)!=(db>=-1e-7)) output.Add(a+(b-a)*(da/(da-db)));
            if(db>=-1e-7)output.Add(b);
            a=b;da=db;
        }
        return output.ToArray();
    }
    // Frustum clipping in stack buffers: no LINQ, delegates or six temporary arrays per patch.
    private static double FrustumDistance(V p,int plane,double focal)=>plane switch
    {0=>p.Z-4,1=>8192-p.Z,2=>p.X+p.Z*(800.0/focal),3=>p.Z*(800.0/focal)-p.X,4=>p.Y+p.Z*(450.0/focal),_=>p.Z*(450.0/focal)-p.Y};
    private static V[] ProjectPoints(V[] vertices,V eye,V right,V up,V forward,double focal=720)
    {
        int capacity=vertices.Length+12;
        Span<V> a=capacity<=96?stackalloc V[96]:new V[capacity];
        Span<V> b=capacity<=96?stackalloc V[96]:new V[capacity];
        int count=vertices.Length;
        for(int i=0;i<count;i++){var v=vertices[i]-eye;a[i]=new(v.Dot(right),v.Dot(up),v.Dot(forward));}
        for(int plane=0;plane<6;plane++)
        {
            int inside=0;for(int i=0;i<count;i++)if(FrustumDistance(a[i],plane,focal)>=0)inside++;
            if(inside==0)return Array.Empty<V>();
            if(inside==count)continue;
            int n=0;var prev=a[count-1];double dp=FrustumDistance(prev,plane,focal);
            for(int i=0;i<count;i++)
            {
                var next=a[i];double dn=FrustumDistance(next,plane,focal);
                if((dp>=0)!=(dn>=0))b[n++]=prev+(next-prev)*(dp/(dp-dn));
                if(dn>=0)b[n++]=next;
                prev=next;dp=dn;
            }
            var swap=a;a=b;b=swap;count=n;if(count<3)return Array.Empty<V>();
        }
        double area=0;
        for(int i=0;i<count;i++)a[i]=new(focal*a[i].X/a[i].Z,focal*a[i].Y/a[i].Z,0);
        for(int i=0;i<count;i++){var p=a[i];var q=a[(i+1)%count];area+=p.X*q.Y-p.Y*q.X;}
        if(Math.Abs(area)<.4)return Array.Empty<V>();
        var result=a.Slice(0,count).ToArray();if(area<0)Array.Reverse(result);return result;
    }
    private void MakePatches(Face face)
    {
        var tex=textures[face.Texture];
        if(tex.Name=="black") {face.Patches.Add(new(face.Points,0x242421));return;}
        // Existing mip colors become coarse vector patches in original BSP UV space.
        bool detail=tex.Name.Contains("Door",StringComparison.OrdinalIgnoreCase)||tex.Name.Contains("Crte",StringComparison.OrdinalIgnoreCase);
        bool sand=tex.Name.Contains("Sand",StringComparison.OrdinalIgnoreCase)&&!detail;
        double step=detail?16:32;
        var light=tex.Colors.Where(c=>(((c>>16)&255)+((c>>8)&255)+(c&255))/3>90).ToArray();
        if(light.Length==0)light=tex.Colors;
        int average=(int)light.Average(c=>(c>>16)&255)<<16|(int)light.Average(c=>(c>>8)&255)<<8|(int)light.Average(c=>c&255);
        var svec=new V(face.S[0],face.S[1],face.S[2]);var tvec=new V(face.T[0],face.T[1],face.T[2]);
        double det=svec.Dot(tvec.Cross(face.Normal));
        V udir=tvec.Cross(face.Normal)*(1/det),vdir=face.Normal.Cross(svec)*(1/det);
        double u0=Math.Floor(face.Points.Min(p=>UV(p,face.S))/step)*step,u1=face.Points.Max(p=>UV(p,face.S));
        double v0=Math.Floor(face.Points.Min(p=>UV(p,face.T))/step)*step,v1=face.Points.Max(p=>UV(p,face.T));
        for(double u=u0;u<u1-.001;u+=step)for(double v=v0;v<v1-.001;v+=step)
        {
            var p=Clip(face.Points,p=>UV(p,face.S)-u);p=Clip(p,p=>u+step-UV(p,face.S));
            p=Clip(p,p=>UV(p,face.T)-v);p=Clip(p,p=>v+step-UV(p,face.T));
            if(p.Length<3)continue;
            int x=(int)(8*(((u+step*.5)%tex.Width+tex.Width)%tex.Width)/tex.Width);
            int y=(int)(8*(((v+step*.5)%tex.Height+tex.Height)%tex.Height)/tex.Height);
            // Keep source hues, mildly compress palette variation, no lighting model.
            int rgb=tex.Colors[Compat.Clamp(y,0,7)*8+Compat.Clamp(x,0,7)];
            if(sand && (((rgb>>16)&255)+((rgb>>8)&255)+(rgb&255))/3>85)
            {
                int Blend(int shift)=>(int)((((rgb>>shift)&255)*.25)+(((average>>shift)&255)*.75));
                rgb=Blend(16)<<16|Blend(8)<<8|Blend(0);
            }
            int Quant(int value)=>Compat.Clamp((value+5)/10*10,0,255);
            rgb=(Quant((rgb>>16)&255)<<16)|(Quant((rgb>>8)&255)<<8)|Quant(rgb&255);
            face.Patches.Add(new(p,rgb));
            if(sand&&Math.Abs(det)>.0001)
            {
                var center=p.Aggregate(new V(),(a,b)=>a+b)*(1.0/p.Length);
                // Small vector chips remain fixed in texture/world space.
                uint seed=unchecked((uint)((int)u*73856093^(int)v*19349663^face.Texture*83492791));
                double a=1.5+(seed%5)*.4,b=1.1+((seed>>4)%4)*.3;
                int Shift(int shift)=>Compat.Clamp(((rgb>>shift)&255)-12,0,255);
                int spot=Shift(16)<<16|Shift(8)<<8|Shift(0);
                if(p.Length>=4 && (p[0]-center).Length>7)
                    face.Patches.Add(new(new[]{center-udir*a-vdir*b,center+udir*a-vdir*b*.4,center+udir*a*.5+vdir*b,center-udir*a*.7+vdir*b*.6},spot));
            }
        }
    }
    private double PlaneDistance(V p,int index)
    {var pl=planes[index];return p.X*pl[0]+p.Y*pl[1]+p.Z*pl[2]-pl[3];}
    public int Contents(V p)
    {int n=root;while(n>=0){var a=nodes[n];n=a[PlaneDistance(p,a[0])>=0?1:2];}return leaves[-n-1];}
    private int Leaf(V p)
    {int n=root;while(n>=0){var a=nodes[n];n=a[PlaneDistance(p,a[0])>=0?1:2];}return -n-1;}
    private bool[] VisibleFaces(V eye,out byte[] pvs)
    {
        int leaf=Leaf(eye);pvs=visibility[leaf];var allowed=new bool[faceCapacity];
        void Mark(int l){foreach(int id in leafFaces[l])if(id<allowed.Length)allowed[id]=true;}
        Mark(leaf);
        if(leaf==0){for(int i=0;i<allowed.Length;i++)allowed[i]=true;return allowed;}
        for(int i=1;i<leafFaces.Length&&i<=pvs.Length*8;i++)if((pvs[(i-1)>>3]&(1<<((i-1)&7)))!=0)Mark(i);
        return allowed;
    }
    // Exact point-segment BSP trace; solid brush models participate as well as the world.
    public double Trace(V from,V to)
    {
        double best=1;
        double Visit(int n,V a,V b,double ta,double tb,int depth)
        {
            if(ta>=best||depth>128)return 1;
            if(n<0)return leaves[-n-1] is -2 or -6?ta:1;
            var node=nodes[n];double da=PlaneDistance(a,node[0]),db=PlaneDistance(b,node[0]);
            if(da>=0&&db>=0)return Visit(node[1],a,b,ta,tb,depth+1);
            if(da<0&&db<0)return Visit(node[2],a,b,ta,tb,depth+1);
            double f=Compat.Clamp(da/(da-db),0,1),tm=ta+(tb-ta)*f;var middle=a+(b-a)*f;
            int near=da>=0?1:2;double hit=Visit(node[near],a,middle,ta,tm,depth+1);
            return hit<1?hit:Visit(node[3-near],middle,b,tm,tb,depth+1);
        }
        foreach(int hull in pointClipRoots)best=Math.Min(best,Visit(hull,from,to,0,1,0));
        return best;
    }
    public V GroundNear(V hint)
    {
        V best=hint;double score=double.MaxValue;
        for(int radius=0;radius<=128;radius+=32)
        for(int x=-radius;x<=radius;x+=32)for(int y=-radius;y<=radius;y+=32)
        {
            if(radius>0&&Math.Abs(x)!=radius&&Math.Abs(y)!=radius)continue;
            for(double z=hint.Z+32;z>=hint.Z-160;z-=8)
            {
                var p=new V(hint.X+x,hint.Y+y,z);
                if(!CanOccupy(p)||!TryWalk(p,new V(),out var grounded))continue;
                double cost=(grounded-hint).Length;
                if(cost<score){score=cost;best=grounded;}break;
            }
            if(score<2)return best;
        }
        if(score==double.MaxValue)throw new InvalidOperationException($"No standing ground near {hint}");
        return best;
    }
    public bool CanOccupy(V eye,bool limitArea=true,bool duck=false)
    {
        if(limitArea&&(eye.X<minimum.X||eye.X>maximum.X||eye.Y<minimum.Y||eye.Y>maximum.Y))return false;
        // Original GoldSrc standing-player hull: 32x32x72, eye 28 above origin.
        var center=eye-new V(0,0,duck?12:28);
        foreach(int hull in duck?duckClipRoots:playerClipRoots)
        {
            int n=hull;while(n>=0){var a=clipnodes[n];n=a[PlaneDistance(center,a[0])>=0?1:2];}
            if(n==-2)return false;
        }
        return true;
    }
    public bool TryStance(V eye,bool crouched,bool grounded,out V result)
    {
        result=eye;var target=eye+new V(0,0,crouched?-34:34);
        // Standing and crouched BSP hulls were compiled separately. On a ramp
        // their bottom planes differ slightly; a raw 34-unit shift can be solid.
        // Search only vertically, then settle the NEW hull onto the same floor.
        for(int step=0;step<=(grounded?144:0);step++)
        {
            // Either hull can have the higher floor at a BSP seam. Search both
            // sides of the nominal eye height, nearest first, without changing XY.
            double adjustment=step==0?0:((step+1)/2)*.25*(step%2==1?1:-1);
            var candidate=target+new V(0,0,adjustment);
            if(!CanOccupy(candidate,duck:crouched))continue;
            if(Trace(eye,candidate)<.999)continue;
            if(grounded&&TryWalk(candidate,new(),out var floor,20,crouched))
            {
                if(Math.Abs(floor.Z-target.Z)>18.1)continue;
                result=floor;return true;
            }
            if(!grounded){result=candidate;return true;}
        }
        return false;
    }
    public V DropGround(V eye,double yaw)
    {
        var forward=new V(Math.Sin(yaw),Math.Cos(yaw),0);
        foreach(double distance in new[]{64d,32,0})
        {
            var from=eye+forward*distance;
            if(Trace(eye,from)<.999)continue;
            var to=from-new V(0,0,2048);double fraction=Trace(from,to);
            if(fraction>0&&fraction<1)return from+(to-from)*fraction+new V(0,0,1.5);
        }
        // No floor (e.g. outside valid map bounds): retain a recoverable item at
        // the last position instead of throwing and terminating the entire game.
        return eye;
    }
    public bool TryWalk(V from,V offset,out V result,double maxDrop=192,bool duck=false)
    {
        result=from;var target=from+offset;
        foreach(double rise in new[]{0d,4,8,12,16,18})
        {
            var high=target+new V(0,0,rise);
            if(!CanOccupy(high,duck:duck))continue;
            var low=high;bool floor=false;
            for(int i=0;i<Math.Ceiling(maxDrop/4);i++)
            {
                var next=low-new V(0,0,4);
                if(!CanOccupy(next,false,duck)){floor=true;high=low;low=next;break;}
                low=next;
            }
            if(!floor)continue;
            for(int i=0;i<8;i++){var mid=(low+high)*.5;if(CanOccupy(mid,false,duck))high=mid;else low=mid;}
            result=high+new V(0,0,.02);return true;
        }
        return false;
    }
    private void Insert(int n,Patch patch,Dictionary<int,List<Patch>> target,int depth=0)
    {
        if(patch.Points.Length<3||depth>100)return;
        if(n<0)
        {
            if(leaves[-n-1]==-2)return;
            if(!target.TryGetValue(n,out var list))target[n]=list=new();list.Add(patch);return;
        }
        var node=nodes[n];var ds=patch.Points.Select(p=>PlaneDistance(p,node[0])).ToArray();
        if(ds.All(v=>v>=-.01)){Insert(node[1],patch,target,depth+1);return;}
        if(ds.All(v=>v<=.01)){Insert(node[2],patch,target,depth+1);return;}
        Insert(node[1],new(Clip(patch.Points,p=>PlaneDistance(p,node[0])),patch.Color,patch.Normal,patch.FaceId),target,depth+1);
        Insert(node[2],new(Clip(patch.Points,p=>-PlaneDistance(p,node[0])),patch.Color,patch.Normal,patch.FaceId),target,depth+1);
    }
    public V Position(double distance)
    {
        distance=Compat.Clamp(distance,0,RouteLength);
        for(int i=1;i<route.Length;i++)if(distance<=distances[i])return route[i-1]+(route[i]-route[i-1])*((distance-distances[i-1])/(distances[i]-distances[i-1]));
        return route[route.Length-1];
    }
    public Frame Render(double seconds,CameraPose? camera=null,CombatSnapshot? combat=null)
    {
        if(combat is {Dead:true,Rounds:true,SpectatorMode:2,DeathCamera:false})return RenderPlan(combat);
        double progress=Compat.Clamp(seconds/Duration,0,1),distance=progress*RouteLength;
        var eye=Position(distance);var forward=(Position(Math.Min(RouteLength,distance+110))-Position(Math.Max(0,distance-25))).Unit;
        if(camera is not null)
        {
            eye=camera.Value.Eye;
            forward=new V(Math.Sin(camera.Value.Yaw)*Math.Cos(camera.Value.Pitch),Math.Cos(camera.Value.Yaw)*Math.Cos(camera.Value.Pitch),Math.Sin(camera.Value.Pitch));
        }
        var right=new V(forward.Y,-forward.X,0); // Y-forward convention, screen right.
        right=right.Unit;var up=right.Cross(forward).Unit;
        if(camera is null)eye+=new V(0,0,Math.Sin(seconds*5)*.65);
        var polygons=new List<ScreenPolygon>(7000);int visibleFaces=0,visiblePatches=0;
        var labels=new List<ScreenLabel>();var strokes=new List<ScreenStroke>();
        double focal=combat?.Scoped==true?720/Math.Tan(combat.Fov*Math.PI/360):720;
        var allowed=VisibleFaces(eye,out var pvs);
        void Screen(int color,params V[] p)=>polygons.Add(new(p,color));
        Screen(Draft?DraftGeometry.Black:0xb6c6c6,new(-800,-450,0),new(800,-450,0),new(800,450,0),new(-800,450,0));
        // A distant sky in world orientation. Camera translation has no effect;
        // yaw/pitch change the viewed direction instead of carrying clouds along.
        foreach(var sky in Draft?Array.Empty<Patch>():Skybox.Patches)
        {
            var p=ProjectPoints(sky.Points,new V(),right,up,forward,focal);
            if(p.Length>=3)Screen(sky.Color,p);
        }
        void Project(Patch patch)
        {
            var p=ProjectPoints(patch.Points,eye,right,up,forward,focal);
            if(p.Length<3)return;
            polygons.Add(new(p,patch.Color,patch.FaceId));visiblePatches++;
            if(patch.Marks is not null)foreach(var mark in patch.Marks)
                DraftGeometry.Project(polygons,mark,eye,right,up,forward,focal,patch.FaceId,(patch.Center-eye).Length);
        }
        var actors=new Dictionary<int,List<Patch>>();
        if(combat is null)
        {
        // Demonstration route, retained for the automatic benchmark.
        double actorDistance=camera is null?distance+190:190+(seconds*110)%(RouteLength-300);
        var companion=Position(Math.Min(RouteLength,actorDistance));
        if(actorDistance>RouteLength)companion+=(route[route.Length-1]-route[route.Length-2]).Unit*(actorDistance-RouteLength);
        if(Draft)
        {
            foreach(var actor in new[]{new ActorView(1,companion,false,seconds,0,false){Speed=180},new ActorView(2,new(860,960,64),true,seconds,0,false),new ActorView(3,new(1455,1370,64),true,seconds,0,false)})
                foreach(var p in ActorSlides.Draw(actor,eye,right))Insert(root,p,actors);
        }
        else
        {
            foreach(var p in Character(companion-new V(0,0,64),right,seconds,false,true))Insert(root,p,actors);
            foreach(var p in Character(new V(860,960,0),right,seconds+.4,true,false))Insert(root,p,actors);
            foreach(var p in Character(new V(1455,1370,0),right,seconds+.8,true,false))Insert(root,p,actors);
        }
        }
        else
        {
            foreach(var drop in combat.Drops)foreach(var p in DroppedWeapons.Draw(drop))Insert(root,p,actors);
            foreach(var actor in combat.Actors)
            {
                var foot=actor.Eye-new V(0,0,64);
                foreach(var patch in Draft?ActorSlides.Draw(actor,eye,right):Character(foot,right,actor.WalkPhase,actor.IsCt,false,actor.Fall,actor.Hit))Insert(root,patch,actors);
            }
            foreach(var effect in combat.Effects)
            {
                double age=combat.Time-effect.Time;
                if(effect.Blood)
                {
                    for(int i=0;i<5;i++)
                    {
                        double sign=i%2==0?1:-1;var p=effect.Position+right*(sign*(2+i*2+age*18))+new V(0,0,i*2+age*15-age*age*60);
                        double size=2+(i%3);Insert(root,new Patch(new[]{p-right*size,p+right*size,p+new V(0,0,size*1.5)},Draft?DraftGeometry.White:i%2==0?0x9c2926:0x691d21),actors);
                    }
                }
                else
                {
                    var p=effect.Position;Insert(root,new Patch(new[]{p-right*3,p+new V(0,0,4),p+right*3,p-new V(0,0,3)},Draft?DraftGeometry.White:0x3c3932),actors);
                }
            }
        }
        void DrawNode(int n)
        {
            if(n<0)
            {
                int leaf=-n-1;if(leaf>0&&leaf<=pvs.Length*8&&(pvs[(leaf-1)>>3]&(1<<((leaf-1)&7)))==0)return;
                staticLeaves.TryGetValue(n,out var st);actors.TryGetValue(n,out var ac);
                if(st is not null||ac is not null)
                {
                    var items=(st??Enumerable.Empty<Patch>()).Concat(ac??Enumerable.Empty<Patch>()).Where(p=>p.Normal is null||p.Normal.Value.Dot(eye-p.Points[0])>=.01);
                    if(Draft)
                    {
                        // Preserve mask-before-ink order within each sprite/surface fragment.
                        foreach(var group in items.GroupBy(p=>p.FaceId).OrderByDescending(g=>g.Average(p=>(p.Center-eye).Dot(forward))))
                            foreach(var p in group)Project(p);
                    }
                    else foreach(var p in items.OrderByDescending(p=>(p.Center-eye).Dot(forward)))Project(p);
                }
                return;
            }
            var node=nodes[n];bool front=PlaneDistance(eye,node[0])>=0;
            DrawNode(node[front?2:1]);
            for(int id=node[3];id<node[3]+node[4];id++)if(id<allowed.Length&&allowed[id]&&faces.TryGetValue(id,out var face))
            {
                if(face.Normal.Dot(eye-face.Points[0])<.01)continue;
                var outline=ProjectPoints(face.Points,eye,right,up,forward,focal);if(outline.Length<3)continue;
                visibleFaces++;
                if(Draft)
                {
                    polygons.Add(new(outline,DraftGeometry.Black,face.Id));visiblePatches++;
                    foreach(var mark in drawing[face.Id])DraftGeometry.Project(polygons,mark,eye,right,up,forward,focal,face.Id,(face.Center-eye).Length);
                }
                else if((face.Center-eye).Length>1400){polygons.Add(new(outline,face.FlatColor,face.Id));visiblePatches++;}
                else foreach(var patch in face.Patches)Project(patch);
            }
            DrawNode(node[front?1:2]);
        }
        DrawNode(root);
        if(camera is not null)
        {
            if(combat is not null)GameDrawing.Draw(polygons,combat,labels,strokes,Draft);
            if(combat is null||(!combat.Score&&combat.Menu<0&&!combat.Dead&&!(combat.Scoped&&combat.Weapon.Shape=="sniper")))
            {
            int crosshair=Draft?DraftGeometry.White:0xe6e9d8;
            double gap=3+Math.Min(28,(combat?.Spread??0)*800),outer=gap+7;
            Screen(crosshair,new(-outer,-1,0),new(-gap,-1,0),new(-gap,1,0),new(-outer,1,0));
            Screen(crosshair,new(gap,-1,0),new(outer,-1,0),new(outer,1,0),new(gap,1,0));
            Screen(crosshair,new(-1,-outer,0),new(1,-outer,0),new(1,-gap,0),new(-1,-gap,0));
            Screen(crosshair,new(-1,gap,0),new(1,gap,0),new(1,outer,0),new(-1,outer,0));
            }
        }
        return new(polygons,eye,visibleFaces,visiblePatches){Labels=labels,Strokes=strokes};
    }
    private Frame RenderPlan(CombatSnapshot combat)
    {
        var shapes=new List<ScreenPolygon>{new(new[]{new V(-800,-450,0),new V(800,-450,0),new V(800,450,0),new V(-800,450,0)},DraftGeometry.Black)};
        var labels=new List<ScreenLabel>();var strokes=new List<ScreenStroke>();
        // CAD draws all text and strokes after the filled polygons. Leave the
        // map fills behind an open dialog, and keep its annotations out of it.
        bool annotated=!combat.Score&&combat.Menu<0&&!combat.Paused;
        plan.Value.AddTo(shapes,labels,strokes,combat.Language);
        if(!annotated){labels.Clear();strokes.Clear();}
        V P(V p)=>plan.Value.Project(p);
        foreach(var actor in combat.Actors)
        {
            var p=P(actor.Eye);bool alive=actor.Fall==0;
            if(alive)
            {
                var d=new V(Math.Sin(actor.Heading),Math.Cos(actor.Heading),0);var r=new V(d.Y,-d.X,0);
                shapes.Add(new(new[]{p+d*7,p-d*4+r*4,p-d*4-r*4},DraftGeometry.White));
                if(annotated)
                {
                    // At team spawns several markers can share a line of text.
                    var pos=p+new V(9,3,90);
                    for(int slot=0;slot<=combat.Actors.Length*2;slot++)
                    {
                        double y=pos.Y+(slot%2==0?slot/2:-(slot+1)/2)*18;
                        if(y < -200||y>316)continue;
                        if(labels.Any(l=>l.Position.X<pos.X+46&&l.Position.X+l.Text.Length*l.Height>pos.X
                            &&l.Position.Y<y+16&&l.Position.Y+l.Height+2>y))continue;
                        if(slot!=0)strokes.Add(new(new[]{p,new V(pos.X-2,y+5,85)},Color:0x999999));
                        labels.Add(new(new(pos.X,y,90),$"{(actor.IsCt?"CT":"T")}{actor.Id:00}",13,DraftGeometry.White));
                        break;
                    }
                }
            }
            else {DraftGeometry.AddLine(shapes,p-new V(3,3,0),p+new V(3,3,0),0x777777,1);DraftGeometry.AddLine(shapes,p-new V(-3,3,0),p+new V(-3,3,0),0x777777,1);}
        }
        if(annotated)labels.Add(new(new(-750,352,90),combat.Language.Pick("ПЛАН DUST 2 | НАБЛЮДЕНИЕ","DUST 2 MAP | SPECTATING"),24,DraftGeometry.White));
        GameDrawing.Draw(shapes,combat,labels,strokes,true);
        return new(shapes,new(),faces.Count,0){Labels=labels,Strokes=strokes};
    }
    private static IEnumerable<Patch> Character(V foot,V right,double time,bool ct,bool back,double fall=0,bool hit=false)
    {
        var parts=new List<Patch>();
        int cloth=ct?0x394d59:0x796449,dark=ct?0x293b45:0x514a3a,vest=ct?0x263134:0x41443b,skin=0xbc9470,boots=0x252b2a;
        double stride=new[]{-3.0,0,3.0,0}[(int)(time*7)%4],bob=stride==0?1:0;
        double angle=Compat.Clamp(fall,0,1)*Math.PI*.5;
        V P(double x,double z)=>foot+right*(x*Math.Cos(angle)+z*Math.Sin(angle))+new V(0,0,Math.Max(1,z*Math.Cos(angle)-x*Math.Sin(angle))+bob*(1-fall));
        void Shape(int c,params double[] xy){var p=new V[xy.Length/2];for(int i=0;i<p.Length;i++)p[i]=P(xy[i*2],xy[i*2+1]);parts.Add(new(p,c));}
        // Jointed trouser legs, knee panels, cuffs and boots.
        Shape(dark,-10,34,-1,33,-2+stride,17,-6+stride,4,-13+stride,4,-12+stride,19);
        Shape(cloth,1,33,10,34,12-stride,19,12-stride,4,5-stride,4,2-stride,18);
        Shape(boots,-14+stride,6,-5+stride,6,-3+stride,1,-16+stride,1,-16+stride,3);
        Shape(boots,4-stride,6,13-stride,6,17-stride,2,17-stride,0,4-stride,0);
        Shape(0x5b6258,-12+stride,20,-4+stride,19,-4+stride,14,-11+stride,14);
        Shape(0x5b6258,3-stride,19,11-stride,20,11-stride,14,4-stride,14);
        // Broad shoulders with sloped sleeves; shaped waist instead of a rectangle.
        Shape(cloth,-7,53,-15,49,-18,38,-12,35,-10,42,-9,32,9,32,10,43,14,37,19,40,15,50,7,54);
        Shape(vest,-9,51,9,51,11,41,8,31,-8,31,-11,41);
        Shape(dark,-12,48,-16,39,-11,35,-7,40);
        Shape(boots,-15,38,-10,34,-6,38,-10,42);
        Shape(skin,12,40,17,37,20,40,16,44);
        // Vest straps, belt, magazine pouches and backpack panels.
        Shape(0x71776b,-8,51,-5,51,-4,34,-7,34);
        Shape(0x71776b,5,51,8,51,7,34,4,34);
        Shape(0x535e54,-5,46,5,46,6,36,-6,36);
        Shape(0x323c34,-8,39,-2,39,-2,33,-8,33);
        Shape(0x323c34,2,39,8,39,8,33,2,33);
        Shape(boots,-10,33,10,33,10,30,-10,30);
        Shape(0x9b947e,-2,33,2,33,2,30,-2,30);
        // Neck, eight-sided head; helmet/balaclava and face/goggle band.
        Shape(skin,-3,50,3,50,3,55,-3,55);
        Shape(ct?0x303c40:0x3b3c32,-6,55,-7,61,-4,66,3,67,7,63,6,56,2,53,-3,53);
        if(ct)
        {
            Shape(0x53676a,-8,60,-7,65,-3,69,4,69,8,65,9,60);
            Shape(0x263a3e,-8,60,9,60,8,58,-8,58);
            if(!back){Shape(0x9ab4b0,-5,60,5,60,5,57,-5,57);Shape(0x293433,-1,60,1,60,1,57,-1,57);}
        }
        else
        {
            Shape(0xa39d82,-7,62,7,62,8,59,-7,59);
            if(!back)Shape(skin,-5,60,5,60,5,58,-5,58);
        }
        // Recognizable rifle: stock, receiver, magazine, barrel and front sight.
        Shape(0x2a302f,4,40,10,45,16,44,20,42,32,42,32,39,17,39,15,35,11,36,12,40);
        Shape(0x6b6650,5,40,2,36,-1,37,2,43,8,44);
        Shape(0x1e2727,15,39,19,39,18,32,14,33);
        Shape(0x27312f,27,42,28,46,30,46,30,42);
        Shape(0x60706b,11,43,21,43,21,42,11,42);
        if(hit)Shape(0xa8312a,-5,47,5,45,3,37,-4,39);
        return parts;
    }
}
