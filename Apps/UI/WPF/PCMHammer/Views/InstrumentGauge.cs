// SPDX-License-Identifier: GPL-3.0-only
// 2026-10-18: Original vector instrumentation; no vendor artwork or controls.
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace PCMHammer.Views;

public sealed class InstrumentGauge : FrameworkElement
{
    private static readonly FrameworkPropertyMetadata Render=new FrameworkPropertyMetadata(null,FrameworkPropertyMetadataOptions.AffectsRender);
    public static readonly DependencyProperty ValueProperty=DependencyProperty.Register(nameof(Value),typeof(double),typeof(InstrumentGauge),new FrameworkPropertyMetadata(double.NaN,FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty MaximumProperty=DependencyProperty.Register(nameof(Maximum),typeof(double),typeof(InstrumentGauge),new FrameworkPropertyMetadata(160d,FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty CaptionProperty=DependencyProperty.Register(nameof(Caption),typeof(string),typeof(InstrumentGauge),new FrameworkPropertyMetadata("SPEED",FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty UnitsProperty=DependencyProperty.Register(nameof(Units),typeof(string),typeof(InstrumentGauge),new FrameworkPropertyMetadata("mph",FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty IsTachometerProperty=DependencyProperty.Register(nameof(IsTachometer),typeof(bool),typeof(InstrumentGauge),new FrameworkPropertyMetadata(false,FrameworkPropertyMetadataOptions.AffectsRender));
    public double Value {get=>(double)GetValue(ValueProperty);set=>SetValue(ValueProperty,value);}
    public double Maximum {get=>(double)GetValue(MaximumProperty);set=>SetValue(MaximumProperty,value);}
    public string Caption {get=>(string)GetValue(CaptionProperty);set=>SetValue(CaptionProperty,value);}
    public string Units {get=>(string)GetValue(UnitsProperty);set=>SetValue(UnitsProperty,value);}
    public bool IsTachometer {get=>(bool)GetValue(IsTachometerProperty);set=>SetValue(IsTachometerProperty,value);}
    private static Brush Color(string hex)=>new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString(hex));
    private static Point At(Point center,double radius,double degrees)
    {double a=degrees*Math.PI/180;return new Point(center.X+radius*Math.Cos(a),center.Y+radius*Math.Sin(a));}
    private static void Arc(DrawingContext dc,Point c,double r,double from,double to,Brush brush,double thickness)
    {
        var path=new StreamGeometry();using(var g=path.Open()){g.BeginFigure(At(c,r,from),false,false);g.ArcTo(At(c,r,to),new Size(r,r),0,to-from>180,SweepDirection.Clockwise,true,false);}
        dc.DrawGeometry(null,new Pen(brush,thickness){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round},path);
    }
    private void Text(DrawingContext dc,string value,double size,Brush brush,Point position,bool bold=false)
    {
        var text=new FormattedText(value,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface(new FontFamily("Segoe UI"),FontStyles.Normal,bold?FontWeights.SemiBold:FontWeights.Normal,FontStretches.Normal),size,brush,VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(text,new Point(position.X-text.Width/2,position.Y-text.Height/2));
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);if(ActualWidth<20||ActualHeight<20)return;
        double scale=Math.Min(ActualWidth/350,ActualHeight/252);
        dc.PushTransform(new TranslateTransform((ActualWidth-350*scale)/2,(ActualHeight-252*scale)/2));dc.PushTransform(new ScaleTransform(scale,scale));
        var white=Color("#ECF6FA");var muted=Color("#8FAABD");var accent=Color(IsTachometer?"#FFBB62":"#37D9CA");
        dc.DrawRoundedRectangle(new LinearGradientBrush(System.Windows.Media.Color.FromRgb(23,47,66),System.Windows.Media.Color.FromRgb(10,27,42),90),new Pen(Color("#355367"),1),new Rect(1,1,348,250),14,14);
        dc.DrawLine(new Pen(Color("#38566A"),1),new Point(18,39),new Point(332,39));
        Text(dc,Caption,11,muted,new Point(175,23),true);
        Point center=new(175,157);double radius=99;
        Arc(dc,center,radius,150,390,Color("#294758"),9);
        double maximum=double.IsFinite(Maximum)&&Maximum>0?Maximum:160;
        bool valid=double.IsFinite(Value)&&Value>=0;
        double fraction=valid?Math.Clamp(Value/maximum,0,1):0;
        if(fraction>0)Arc(dc,center,radius,150,150+240*fraction,accent,5);
        // Amber sector is a visual scale marker, not a configured engine rev limit.
        if(IsTachometer)Arc(dc,center,radius+8,330,390,Color("#D89548"),2);
        int major=IsTachometer?8:8;
        for(int n=0;n<=major*5;n++)
        {
            double angle=150+240d*n/(major*5);bool big=n%5==0;
            dc.DrawLine(new Pen(big?muted:Color("#527184"),big?1.7:0.8),At(center,radius-9,angle),At(center,radius-(big?20:14),angle));
            if(big)
            {
                double label=maximum*n/(major*5);if(IsTachometer)label/=1000;
                Text(dc,label.ToString("0",CultureInfo.CurrentCulture),10,muted,At(center,radius-33,angle));
            }
        }
        if(valid)
        {
            double angle=150+240*fraction;var tip=At(center,radius-22,angle);var tail=At(center,10,angle+180);
            dc.DrawLine(new Pen(Color("#FFFFFF"),2.3){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round},tail,tip);
            dc.DrawEllipse(Color("#E7F4F8"),new Pen(accent,2),center,5,5);
        }
        Text(dc,valid?Value.ToString("N0",CultureInfo.CurrentCulture):"—",34,valid?white:muted,new Point(175,203),true);
        Text(dc,valid?(IsTachometer?"RPM  ·  scale ×1000":Units):"NO CURRENT READING",10,muted,new Point(175,231));
        dc.Pop();dc.Pop();
    }
}
