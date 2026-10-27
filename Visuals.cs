using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using System.Windows.Markup;
using System.Web.Script.Serialization;

[assembly: System.Reflection.AssemblyTitle("Quota Clock")]
[assembly: System.Reflection.AssemblyVersion("0.4.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("0.4.0.0")]

namespace QuotaClock
{
    public static class Paint
    {
        public static Color Color(string hex) { return (Color)ColorConverter.ConvertFromString(hex); }
        static readonly Dictionary<uint,SolidColorBrush> brushes=new Dictionary<uint,SolidColorBrush>();
        public static SolidColorBrush Brush(string hex) { return Cached(Color(hex)); }
        static SolidColorBrush Cached(Color c) {
            uint key=(uint)c.A<<24|(uint)c.R<<16|(uint)c.G<<8|c.B;
            lock(brushes){SolidColorBrush b;if(brushes.TryGetValue(key,out b))return b;
                if(brushes.Count>=1024)brushes.Clear();b=new SolidColorBrush(c);b.Freeze();brushes[key]=b;return b;}
        }
        public static SolidColorBrush Alpha(Color c, byte alpha) { c.A=alpha;return Cached(c); }
        // Adapted from Agent Speed HeatPalette stops (MIT, zoefix 2026).
        static readonly string[] Stops = { "#7C8798", "#2DD4BF", "#38BDF8", "#818CF8", "#E879F9", "#FB7185", "#FB923C", "#FACC15" };
        static readonly double[] Places = { 0, .10, .30, .48, .64, .78, .90, 1 };
        public static Color Heat(double value)
        {
            value = Math.Max(0, Math.Min(1, value));
            for (int i = 1; i < Stops.Length; i++)
            {
                if (value > Places[i]) continue;
                double t = (value - Places[i - 1]) / (Places[i] - Places[i - 1]);
                Color a = Color(Stops[i - 1]), b = Color(Stops[i]);
                return System.Windows.Media.Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
            }
            return Color(Stops[Stops.Length - 1]);
        }
    }

    public sealed class ClockFace : FrameworkElement
    {
        public static readonly DependencyProperty ReadingProperty = DependencyProperty.Register("Reading", typeof(double), typeof(ClockFace),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
        public double Reading { get { return (double)GetValue(ReadingProperty); } set { SetValue(ReadingProperty, value); } }
        public bool Known, Fine, Light, Expired;
        public bool ShowReadout = true, Demo, Estimated;
        public string Readout;
        bool initialized;
        string lastReadout;bool lastEstimated,lastDemo,lastLight;
        static readonly Typeface normalFace=new Typeface(new FontFamily("Segoe UI"),FontStyles.Normal,FontWeights.Normal,FontStretches.Normal);
        static readonly Typeface strongFace=new Typeface(new FontFamily("Segoe UI"),FontStyles.Normal,FontWeights.SemiBold,FontStretches.Normal);
        readonly Dictionary<string,FormattedText> textCache=new Dictionary<string,FormattedText>();
        static readonly Dictionary<string,Pen> pens=new Dictionary<string,Pen>();
        static readonly Brush lightFace=FaceBrush(true),darkFace=FaceBrush(false);
        static Brush FaceBrush(bool light){var b=new RadialGradientBrush(Paint.Color(light?"#FFFFFF":"#222A37"),Paint.Color(light?"#E9EFF5":"#10151E"));b.GradientOrigin=new Point(.36,.22);b.Freeze();return b;}
        public void Update(decimal value, bool known, bool fine, bool expired, bool animate)
        {
            bool changed = !initialized || lastLight!=Light || lastReadout!=Readout || lastEstimated!=Estimated || lastDemo!=Demo || Known!=known || Fine!=fine || Expired!=expired || Math.Abs(Reading-(double)Math.Max(0,Math.Min(100,value)))>.0001;
            Known = known; Fine = fine; Expired = expired;
            lastReadout=Readout;lastEstimated=Estimated;lastDemo=Demo;lastLight=Light;
            if(!changed)return;
            double target = (double)Math.Max(0,Math.Min(100,value));
            if (initialized && Demo && animate && known && !expired && SystemParameters.ClientAreaAnimation)
                BeginAnimation(ReadingProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(650)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            else { BeginAnimation(ReadingProperty, null); Reading = target; }
            initialized=true;
            InvalidateVisual();
            AutomationProperties.SetName(this, known && !expired ? "剩余额度 " + QuotaMath.Percent(value, fine) : "额度未知");
        }
        static Point Polar(double radius, double degrees)
        {
            double rad = (degrees - 90) * Math.PI / 180;
            return new Point(180 + Math.Cos(rad) * radius, 180 + Math.Sin(rad) * radius);
        }
        static Pen Pen(Brush b, double width)
        { string key=b.ToString()+"|"+width.ToString(CultureInfo.InvariantCulture);Pen p;
          if(pens.TryGetValue(key,out p))return p;if(pens.Count>=1024)pens.Clear();
          p=new Pen(b,width){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};p.Freeze();pens[key]=p;return p; }
        void Text(DrawingContext dc, string text, double x, double y, double size, Brush brush, bool strong)
        {
            double dpi=VisualTreeHelper.GetDpi(this).PixelsPerDip;
            string key=text+"|"+size+"|"+strong+"|"+brush+"|"+dpi;
            FormattedText f;
            if(!textCache.TryGetValue(key,out f)) {
                if(textCache.Count>=128)textCache.Clear();
                f=new FormattedText(text,CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,strong?strongFace:normalFace,size,brush,dpi);
                textCache[key]=f;
            }
            dc.DrawText(f, new Point(x - f.Width / 2, y - f.Height / 2));
        }
        void Hand(DrawingContext dc, double angle, double length, double width, Brush color, double tail)
        { dc.DrawLine(Pen(color, width), Polar(tail, angle + 180), Polar(length, angle)); }
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            if (ActualWidth < 1 || ActualHeight < 1) return;
            double scale = Math.Min(ActualWidth, ActualHeight) / 360;
            dc.PushTransform(new TranslateTransform((ActualWidth - 360 * scale) / 2, (ActualHeight - 360 * scale) / 2));
            dc.PushTransform(new ScaleTransform(scale, scale));
            Brush main = Paint.Brush(Light ? "#182534" : "#EFF5FC");
            Brush secondary = Paint.Brush(Light ? "#6E7E91" : "#8A9AAD");
            Color color = Paint.Heat(.1 + .8 * (1 - Reading / 100));
            var center = new Point(180, 180);
            var face = Light ? lightFace : darkFace;
            dc.DrawEllipse(Paint.Alpha(color, Light ? (byte)16 : (byte)23), null, center, 177, 177);
            dc.DrawEllipse(face, Pen(Paint.Brush(Light ? "#CBD6E2" : "#344050"), .8), center, 162, 162);
            dc.DrawEllipse(null, Pen(Paint.Brush(Light ? "#DDE5EF" : "#232D3A"), 1), center, 129, 129);
            bool valid = Known && !Expired;
            int active = valid ? (int)Math.Ceiling(Reading / 100 * 60) : 0;
            for (int i = 0; i < 60; i++)
            {
                double angle = i * 6;
                Color track = Paint.Heat(.1 + .8 * i / 59.0);
                Brush brush = i < active ? Paint.Alpha(color, (byte)(Light ? 225 : 245)) : Paint.Alpha(track, Light ? (byte)40 : (byte)39);
                dc.DrawLine(Pen(brush, 3.4), Polar(166, angle), Polar(173, angle));
                bool major = i % 5 == 0;
                dc.DrawLine(Pen(major ? secondary : Paint.Brush(Light ? "#CAD5E2" : "#3C4756"), major ? 1.6 : .8),
                    Polar(major ? 136 : 141, angle), Polar(148, angle));
            }
            for (int hour = 1; hour <= 12; hour++)
            {
                Point pt = Polar(113, hour * 30);
                Text(dc, hour.ToString(), pt.X, pt.Y, hour == 12 ? 19 : 17, hour % 3 == 0 ? main : secondary, hour % 3 == 0);
            }
            Text(dc, "QUOTA", 180, 130, 10, secondary, true);
            if (valid)
            {
                // Interpolate coarse hands smoothly; the fine hand exists only for real fractional or explicit demo data.
                double consumed = 100 - Reading;
                double hourAngle = consumed * 3.6;
                double minuteAngle = (consumed % 10) * 36;
                Hand(dc, hourAngle, 67, 7, Paint.Alpha(Paint.Color(Light ? "#24354B" : "#E5ECF5"), 255), 12);
                Hand(dc, minuteAngle, 94, 4, Paint.Brush(Light ? "#087F8C" : "#65DCD4"), 17);
                if (Fine) {
                    if(Estimated) {
                        var estimatePen=new Pen(Paint.Brush("#EFB947"),1.7){DashStyle=DashStyles.Dash};
                        dc.DrawLine(estimatePen,Polar(24,(consumed%1)*360+180),Polar(102,(consumed%1)*360));
                    } else Hand(dc, (consumed%1)*360, 102, 1.5, Paint.Brush("#FB8B7B"), 24);
                }
                dc.DrawEllipse(Paint.Brush(Light ? "#E3EDF5" : "#263444"), Pen(main, 1.2), center, 7, 7);
                dc.DrawEllipse(Paint.Brush(Fine ? "#FB8B7B" : "#65DCD4"), null, center, 2.3, 2.3);
                if (ShowReadout)
                {
                    Text(dc, Readout ?? QuotaMath.Percent((decimal)Reading, Fine), 180, 235, 27, main, true);
                    Text(dc, Demo ? "演示额度" : Estimated ? "剩余额度 · ≈估算针" : "剩余额度", 180, 264, 11, secondary, false);
                }
                else Text(dc, Reading >= 99.95 ? "FULL" : "REMAINING", 180, 231, 10, secondary, true);
            }
            else
            {
                Text(dc, "—", 180, 181, 26, secondary, false);
                Text(dc, Expired ? "等待更新" : "等待额度", 180, 231, 12, secondary, false);
            }
            dc.Pop(); dc.Pop();
        }
    }

}

