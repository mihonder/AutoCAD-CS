using System.Text.Json;
namespace CadDustLab;

// Cached, simplified floor-plan linework, independent of surface triangulation.
public sealed class PlanDrawing
{
    private readonly List<ScreenPolygon> shapes=new();
    private readonly List<ScreenLabel> labels=new();
    private readonly List<ScreenStroke> strokes=new();
    private readonly double centerX,centerY,scale;
    public int HatchCount {get;}
    public int ContourCount {get;}
    public PlanDrawing()
    {
        var asm=typeof(PlanDrawing).Assembly;
        using var stream=asm.GetManifestResourceStream(asm.GetManifestResourceNames().Single(n=>n.EndsWith("plan-vectors.json")))!;
        using var doc=JsonDocument.Parse(stream);var data=doc.RootElement;var bounds=data.GetProperty("bounds");
        double x0=bounds[0][0].GetDouble(),y0=bounds[0][1].GetDouble(),x1=bounds[1][0].GetDouble(),y1=bounds[1][1].GetDouble();
        centerX=(x0+x1)/2;centerY=(y0+y1)/2;scale=Math.Min(1160/(x1-x0),555/(y1-y0));
        foreach(var ring in data.GetProperty("walls").EnumerateArray())
        {
            var points=ring.GetProperty("points").EnumerateArray().Select(p=>Project(new(p[0].GetDouble(),p[1].GetDouble(),0))).ToArray();
            bool solid=ring.GetProperty("depth").GetInt32()%2==0;
            shapes.Add(new(points,solid?0x242424:DraftGeometry.Black));
            // Polyline contours stay continuous regardless of zoom level.
            strokes.Add(new(points,true,DraftGeometry.White,30));ContourCount++;
        }
        foreach(var line in data.GetProperty("hatch").EnumerateArray())
        {
            DraftGeometry.AddLine(shapes,Project(new(line[0].GetDouble(),line[1].GetDouble(),0)),Project(new(line[2].GetDouble(),line[3].GetDouble(),0)),0x969696,.65);
            HatchCount++;
        }
        foreach(var label in data.GetProperty("labels").EnumerateArray())
        {
            var p=Project(new(label[1].GetDouble(),label[2].GetDouble(),0));
            labels.Add(new(new(p.X-7,p.Y,90),label[0].GetString()!,16,0xa0a0a0));
        }
        labels.Add(new(new(-740,295,90),"СТЕНЫ — ШТРИХОВКА",17,DraftGeometry.White));
        labels.Add(new(new(-740,265,90),"ПРОХОДЫ — БЕЗ ЗАЛИВКИ",17,DraftGeometry.White));
        labels.Add(new(new(-740,235,90),"ТН — ТОННЕЛИ",17,DraftGeometry.White));
    }
    public V Project(V point)=>new((point.X-centerX)*scale,(point.Y-centerY)*scale+65,0);
    public void AddTo(List<ScreenPolygon> output,List<ScreenLabel> text,List<ScreenStroke> lines)
    {output.AddRange(shapes);text.AddRange(labels);lines.AddRange(strokes);}
}
