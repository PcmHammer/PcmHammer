using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
namespace PCMHammer.Views;

public readonly record struct PlotPoint(double X,double Y);
public sealed class TelemetryChart : FrameworkElement
{
    public static readonly DependencyProperty PointsProperty=DependencyProperty.Register(nameof(Points),typeof(IReadOnlyList<PlotPoint>),typeof(TelemetryChart),new FrameworkPropertyMetadata(Array.Empty<PlotPoint>(),FrameworkPropertyMetadataOptions.AffectsRender));
    public IReadOnlyList<PlotPoint> Points {get=>(IReadOnlyList<PlotPoint>)GetValue(PointsProperty);set=>SetValue(PointsProperty,value);}
    private double zoom=1;
    private Point? cursor;
    public TelemetryChart(){MinHeight=120;MouseMove+=(_,e)=>{cursor=e.GetPosition(this);InvalidateVisual();};MouseLeave+=(_,_)=>{cursor=null;InvalidateVisual();};MouseWheel+=(_,e)=>{zoom=Math.Clamp(zoom*(e.Delta>0?1.5:1/1.5),1,32);InvalidateVisual();e.Handled=true;};MouseLeftButtonDown+=(_,e)=>{if(e.ClickCount==2){zoom=1;InvalidateVisual();}};}
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(15,30,49)),null,new Rect(0,0,ActualWidth,ActualHeight),10,10);
        var data=Points.Where(p=>double.IsFinite(p.X)&&double.IsFinite(p.Y)).ToArray();
        if(data.Length==0){Text(dc,"No samples yet · record, open a CSV, or try Demo",20,25,Brushes.LightSlateGray);return;}
        var area=new Rect(64,24,Math.Max(1,ActualWidth-86),Math.Max(1,ActualHeight-62));
        int count=Math.Max(2,(int)(data.Length/zoom));data=data.Skip(Math.Max(0,data.Length-count)).ToArray();
        double xmin=data.Min(p=>p.X),xmax=data.Max(p=>p.X);if(xmax==xmin)xmax=xmin+1;
        double ymin=data.Min(p=>p.Y),ymax=data.Max(p=>p.Y);double pad=Math.Max((ymax-ymin)*0.1,Math.Max(0.01,Math.Abs(ymax)*0.01));ymin-=pad;ymax+=pad;
        Point Map(PlotPoint p)=>new(area.Left+(p.X-xmin)/(xmax-xmin)*area.Width,area.Bottom-(p.Y-ymin)/(ymax-ymin)*area.Height);
        var grid=new Pen(new SolidColorBrush(Color.FromRgb(38,57,79)),1);
        for(int i=0;i<=4;i++){double y=area.Top+i*area.Height/4;dc.DrawLine(grid,new Point(area.Left,y),new Point(area.Right,y));Text(dc,(ymax-i*(ymax-ymin)/4).ToString("0.##",CultureInfo.InvariantCulture),4,y-7,Brushes.LightSlateGray);}
        Text(dc,xmin.ToString("0.##",CultureInfo.InvariantCulture),area.Left,area.Bottom+9,Brushes.LightSlateGray);
        Text(dc,xmax.ToString("0.##",CultureInfo.InvariantCulture),Math.Max(area.Left,area.Right-50),area.Bottom+9,Brushes.LightSlateGray);
        // Min/max per pixel bucket preserves short spikes when displaying long recordings.
        var drawn=data.Length>2000 ? data.Select((p,i)=>(p,i)).GroupBy(p=>p.i/(Math.Max(1,data.Length/1000)))
            .SelectMany(g=>new[]{g.MinBy(p=>p.p.Y),g.MaxBy(p=>p.p.Y)}.OrderBy(p=>p.i)).Select(p=>p.p).ToArray():data;
        var path=new StreamGeometry();using(var geometry=path.Open()){geometry.BeginFigure(Map(drawn[0]),false,false);geometry.PolyLineTo(drawn.Skip(1).Select(Map).ToArray(),true,false);}path.Freeze();
        dc.DrawGeometry(null,new Pen(new SolidColorBrush(Color.FromRgb(44,216,208)),2),path);
        if(cursor is Point mouse&&area.Contains(mouse))
        {
            double target=xmin+(mouse.X-area.Left)/area.Width*(xmax-xmin);
            PlotPoint nearest=data.MinBy(p=>Math.Abs(p.X-target));Point point=Map(nearest);
            dc.DrawLine(new Pen(Brushes.SlateGray,1),new Point(point.X,area.Top),new Point(point.X,area.Bottom));
            dc.DrawEllipse(Brushes.White,null,point,4,4);
            Text(dc,$"x {nearest.X:0.###}   value {nearest.Y:0.###}",area.Left+8,area.Top+4,Brushes.White);
        }
    }
    private void Text(DrawingContext dc,string value,double x,double y,Brush brush)=>dc.DrawText(new FormattedText(value,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),11,brush,VisualTreeHelper.GetDpi(this).PixelsPerDip),new Point(x,y));
}
