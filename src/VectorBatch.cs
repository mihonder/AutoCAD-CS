namespace CadDustLab;

// Already projected flat vectors. Z only defines paint order; no 3D scene is sent to CAD.
public sealed record VectorChunk(V[] Vertices,int[] Faces,int[] Colors);
public sealed record VectorBatch(VectorChunk[] Chunks)
{
    public ScreenLabel[] Labels {get;init;}=Array.Empty<ScreenLabel>();
    public ScreenStroke[] Strokes {get;init;}=Array.Empty<ScreenStroke>();
    public static VectorBatch Build(Frame frame)
    {
        // Keep every per-frame array below the 85 KB large-object heap threshold.
        var vertices=new List<V>(1024);var faces=new List<int>(4096);var colors=new List<int>(1024);
        var chunks=new List<VectorChunk>(32);
        void Flush(){if(vertices.Count==0)return;chunks.Add(new(vertices.ToArray(),faces.ToArray(),colors.ToArray()));vertices.Clear();faces.Clear();colors.Clear();}
        int order=0;
        foreach(var polygon in frame.Polygons)
        {
            if(vertices.Count+polygon.Points.Length>1024)Flush();
            AddPolygon(polygon,order++*.001,vertices,faces,colors);
        }
        Flush();return new(chunks.ToArray()){Labels=frame.Labels.ToArray(),Strokes=frame.Strokes.ToArray()};
    }
    private static void AddPolygon(ScreenPolygon polygon,double z,List<V> vertices,List<int> faces,List<int> colors)
    {
        var p=polygon.Points;int first=vertices.Count;
        foreach(var v in p)vertices.Add(new(v.X,v.Y,z));
        Span<int> indices=p.Length<=96?stackalloc int[96]:new int[p.Length];int count=p.Length;
        double area=0;for(int i=0;i<p.Length;i++)area+=Cross(p[i],p[(i+1)%p.Length]);
        for(int i=0;i<count;i++)indices[i]=area<0?count-1-i:i;
        while(count>3)
        {
            bool found=false;
            for(int i=0;i<count;i++)
            {
                int a=indices[(i+count-1)%count],b=indices[i],c=indices[(i+1)%count];
                double turn=Cross(p[b]-p[a],p[c]-p[b]);
                if(Math.Abs(turn)<1e-9){indices.Slice(i+1,count-i-1).CopyTo(indices.Slice(i));count--;found=true;break;}
                if(turn<0)continue;
                bool contains=false;
                for(int j=0;j<count;j++){int k=indices[j];if(k!=a&&k!=b&&k!=c&&Cross(p[b]-p[a],p[k]-p[a])>=-1e-8&&Cross(p[c]-p[b],p[k]-p[b])>=-1e-8&&Cross(p[a]-p[c],p[k]-p[c])>=-1e-8){contains=true;break;}}
                if(contains)continue;
                Triangle(first+a,first+b,first+c,polygon.Color,faces,colors);
                indices.Slice(i+1,count-i-1).CopyTo(indices.Slice(i));count--;found=true;break;
            }
            if(!found)throw new InvalidOperationException("Cannot triangulate projected polygon");
        }
        if(count==3)Triangle(first+indices[0],first+indices[1],first+indices[2],polygon.Color,faces,colors);
    }
    private static void Triangle(int a,int b,int c,int color,List<int> faces,List<int> colors){faces.Add(3);faces.Add(a);faces.Add(b);faces.Add(c);colors.Add(color);}
    private static double Cross(V a,V b)=>a.X*b.Y-a.Y*b.X;
}
