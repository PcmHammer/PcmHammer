// SPDX-License-Identifier: GPL-3.0-only
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace PCMHammer.Views;

public sealed record SurfaceData(int Rows,int Columns,double[] Values,string[] RowLabels,string[] ColumnLabels,string RowAxis,string ColumnAxis,string Units);

/// <summary>Orthographic table surface. Rotation changes the view only, never table values.</summary>
public sealed class CalibrationSurface : FrameworkElement
{
    public static readonly DependencyProperty DataProperty=DependencyProperty.Register(nameof(Data),typeof(SurfaceData),typeof(CalibrationSurface),new FrameworkPropertyMetadata(null,FrameworkPropertyMetadataOptions.AffectsRender));
    public SurfaceData? Data {get=>(SurfaceData?)GetValue(DataProperty);set=>SetValue(DataProperty,value);}
    private double angle=-0.65,elevation=0.6,zoom=1;
    private Point? drag;
    public CalibrationSurface()
    {
        MinHeight=140;ClipToBounds=true;
        MouseLeftButtonDown+=(_,e)=>{if(e.ClickCount==2){angle=-0.65;elevation=0.6;zoom=1;InvalidateVisual();return;}drag=e.GetPosition(this);CaptureMouse();};
        MouseLeftButtonUp+=(_,_)=>{drag=null;ReleaseMouseCapture();};
        LostMouseCapture+=(_,_)=>drag=null;
        MouseMove+=(_,e)=>{if(drag is not Point last)return;Point next=e.GetPosition(this);angle+=(next.X-last.X)*0.008;elevation=Math.Clamp(elevation+(next.Y-last.Y)*0.006,0.15,1.4);drag=next;InvalidateVisual();};
        MouseWheel+=(_,e)=>{zoom=Math.Clamp(zoom*(e.Delta>0?1.12:1/1.12),0.55,1.8);InvalidateVisual();e.Handled=true;};
    }
    public static Color Heat(double fraction)
    {
        double t=Math.Clamp(fraction,0,1)*4;int band=Math.Min(3,(int)t);double f=t-band;
        Color[] stops={Color.FromRgb(50,90,222),Color.FromRgb(39,193,220),Color.FromRgb(65,202,105),Color.FromRgb(247,217,53),Color.FromRgb(227,69,43)};
        Color a=stops[band],b=stops[band+1];return Color.FromRgb((byte)(a.R+(b.R-a.R)*f),(byte)(a.G+(b.G-a.G)*f),(byte)(a.B+(b.B-a.B)*f));
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(15,30,49)),null,new Rect(0,0,ActualWidth,ActualHeight),8,8);
        var data=Data;
        if(data==null||data.Values.Length!=data.Rows*data.Columns||data.Values.Length==0){Text(dc,"Load a table or try the demo spark table",18,22,Brushes.LightSlateGray);return;}
        if(data.Values.Any(v=>!double.IsFinite(v)))return;
        double min=data.Values.Min(),max=data.Values.Max(),span=Math.Max(0.001,max-min);
        double scale=Math.Max(10,Math.Min((ActualWidth-190)/3.5,(ActualHeight-66)/2.25))*zoom;
        double cx=(ActualWidth-70)/2,cy=ActualHeight/2+30;
        (Point Point,double Depth) Project(double x,double y,double z)
        {
            double across=x*Math.Cos(angle)-y*Math.Sin(angle),depth=x*Math.Sin(angle)+y*Math.Cos(angle);
            return(new Point(cx+across*scale*1.8,cy+depth*Math.Sin(elevation)*scale-z*Math.Cos(elevation)*scale),depth);
        }
        double X(int c)=>data.Columns==1?0:-1+2.0*c/(data.Columns-1);
        double Y(int r)=>data.Rows==1?0:-1+2.0*r/(data.Rows-1);
        Point At(int r,int c)=>Project(X(c),Y(r),(data.Values[r*data.Columns+c]-min)/span).Point;
        var wire=new Pen(new SolidColorBrush(Color.FromRgb(23,43,61)),0.6);
        var faces=new List<(double Depth,Point[] Corners,double Value)>();
        for(int r=0;r<data.Rows-1;r++)for(int c=0;c<data.Columns-1;c++)
            faces.Add((Project((X(c)+X(c+1))/2,(Y(r)+Y(r+1))/2,0).Depth,new[]{At(r,c),At(r,c+1),At(r+1,c+1),At(r+1,c)},(data.Values[r*data.Columns+c]+data.Values[r*data.Columns+c+1]+data.Values[(r+1)*data.Columns+c]+data.Values[(r+1)*data.Columns+c+1])/4));
        foreach(var face in faces.OrderBy(f=>f.Depth))
        {
            var shape=new StreamGeometry();using(var path=shape.Open()){path.BeginFigure(face.Corners[0],true,true);path.PolyLineTo(face.Corners.Skip(1).ToArray(),true,false);}shape.Freeze();dc.DrawGeometry(new SolidColorBrush(Heat((face.Value-min)/span)),wire,shape);
        }
        if(data.Rows==1||data.Columns==1)
            for(int i=1;i<data.Values.Length;i++)dc.DrawLine(new Pen(Brushes.Turquoise,2),At((i-1)/data.Columns,(i-1)%data.Columns),At(i/data.Columns,i%data.Columns));
        Point origin=Project(-1,1,0).Point,acrossEnd=Project(1,1,0).Point,backEnd=Project(-1,-1,0).Point;
        var axis=new Pen(Brushes.LightSlateGray,1);dc.DrawLine(axis,origin,acrossEnd);dc.DrawLine(axis,origin,backEnd);
        void Tick(Point point,string value,bool row){dc.DrawEllipse(Brushes.LightSlateGray,null,point,2,2);Text(dc,value,point.X-(row?32:10),point.Y+(row?-12:5),Brushes.LightGray);}
        foreach(int c in new[]{0,(data.Columns-1)/3,2*(data.Columns-1)/3,data.Columns-1}.Distinct())Tick(Project(X(c),1,0).Point,data.ColumnLabels[c],false);
        foreach(int r in new[]{0,(data.Rows-1)/3,2*(data.Rows-1)/3,data.Rows-1}.Distinct())Tick(Project(-1,Y(r),0).Point,data.RowLabels[r],true);
        Text(dc,data.ColumnAxis+" / "+data.RowAxis,16,9,Brushes.White);
        Text(dc,"Drag to rotate · wheel to zoom · double-click to reset",16,ActualHeight-22,Brushes.LightSlateGray);
        double legendX=ActualWidth-76,legendHeight=Math.Max(30,ActualHeight-90);
        for(int i=0;i<48;i++)dc.DrawRectangle(new SolidColorBrush(Heat(1-i/47.0)),null,new Rect(legendX,36+i*legendHeight/48,12,legendHeight/48+1));
        Text(dc,max.ToString("0.##",CultureInfo.InvariantCulture),legendX+17,32,Brushes.White);
        Text(dc,min.ToString("0.##",CultureInfo.InvariantCulture),legendX+17,32+legendHeight,Brushes.White);
        Text(dc,data.Units,legendX-40,14,Brushes.LightGray);
    }
    private void Text(DrawingContext dc,string value,double x,double y,Brush brush)=>dc.DrawText(new FormattedText(value,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),11,brush,VisualTreeHelper.GetDpi(this).PixelsPerDip),new Point(x,y));
}
