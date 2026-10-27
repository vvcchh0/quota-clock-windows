using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace QuotaClock
{
    public sealed class TrailPoint
    {
        public double Time,Consumed;
        public bool Valid,Estimated,CoarseValid;
        public int Epoch;
    }
    public sealed class TrailHistory
    {
        public const int Capacity=256;
        public const double SampleSeconds=15,SecondsPerRadius=300;
        readonly List<TrailPoint> points=new List<TrailPoint>();
        string epoch;int epochNumber;
        public event Action<TrailPoint,string> Sampled;
        public int Revision {get;private set;}
        public IList<TrailPoint> Points {get{return points.AsReadOnly();}}
        public bool CurrentValid {get;private set;}
        public bool CurrentCoarseValid {get;private set;}
        public int CurrentEpoch {get{return epochNumber;}}
        public bool CurrentEstimated {get;private set;}
        public double CurrentConsumed {get;private set;}
        public static double Now {get{return Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency;}}
        public static double Projection(double consumed){return -Math.Cos(consumed*2*Math.PI);}
        public static double Offset(double age,double radius){return Math.Max(0,age)*radius/SecondsPerRadius;}
        public void Clear(){points.Clear();Revision++;}
        public void Capture(double time,decimal remaining,bool valid,bool estimated,string key,bool? coarse=null)
        {
            if(key!=epoch){Clear();epoch=key;epochNumber++;}
            bool coarseValid=coarse??valid;
            CurrentConsumed=(double)(100-Math.Max(0,Math.Min(100,remaining)));
            bool stateChanged=valid!=CurrentValid||estimated!=CurrentEstimated||coarseValid!=CurrentCoarseValid;
            CurrentValid=valid;CurrentEstimated=estimated;CurrentCoarseValid=coarseValid;
            if(points.Count>0&&time<points[points.Count-1].Time){Clear();}
            if(points.Count>0&&time-points[points.Count-1].Time<SampleSeconds&&!stateChanged)return;
            var point=new TrailPoint{Time=time,Consumed=CurrentConsumed,Valid=valid,Estimated=estimated,CoarseValid=coarseValid,Epoch=epochNumber};
            points.Add(point);
            while(points.Count>Capacity||points.Count>1&&time-points[0].Time>3600)points.RemoveAt(0);
            Revision++;
            if(Sampled!=null)Sampled(point,key);
        }
        public static bool CanJoin(TrailPoint a,TrailPoint b,int hand=2)
        {
            double dt=b.Time-a.Time,delta=b.Consumed-a.Consumed;
            return (hand==2?a.Valid&&b.Valid:a.CoarseValid&&b.CoarseValid)&&a.Epoch==b.Epoch&&(hand!=2||a.Estimated==b.Estimated)&&dt>0&&dt<=45&&delta>=-.001&&delta<=(hand==0?100:hand==1?10:1);
        }
    }
    // A cached path, shifted once/second. No per-frame timer or unbounded history.
    public sealed class WaveTrail:FrameworkElement
    {
        readonly TrailHistory history;
        DrawingGroup grid;
        readonly StreamGeometry[] paths=new StreamGeometry[4];
        double sizeKey=-1,anchor,gridWidth;
        int revision=-1;
        bool lightKey,leftKey,demoKey;int maskKey=-1;
        readonly TranslateTransform scroll=new TranslateTransform();
        static readonly Typeface font=new Typeface("Segoe UI, Microsoft YaHei UI");
        static readonly Pen exactPen=MakePen("#FB8B7B",1.4,false),estimatedPen=MakePen("#EFB947",1.4,true);
        static readonly Pen hourLight=MakePen("#24354B",1.25,false),hourDark=MakePen("#E5ECF5",1.25,false),minuteLight=MakePen("#087F8C",1.35,false),minuteDark=MakePen("#65DCD4",1.35,false);
        static readonly Pen faintLight=MakePen("#D4DFE8",.65,false),faintDark=MakePen("#344456",.65,false);
        public bool Light,Left,Demo;
        public int HandMask=7;
        public WaveTrail(TrailHistory value){history=value;IsHitTestVisible=true;ToolTip="三针纵向投影 · 每 15 秒采样\n每格 5 分钟，宽度等于表盘半径\n采样间按相位插值，不代表真实消耗过程；校准跳变和缺失会断线。";}
        static Pen MakePen(string color,double width,bool dashed){var p=new Pen(Paint.Brush(color),width);if(dashed)p.DashStyle=DashStyles.Dash;p.Freeze();return p;}
        static Pen Faint(bool light){return light?faintLight:faintDark;}
        void Text(DrawingContext dc,string text,double x,double y,double fontSize,Brush brush)
        {
            var f=new FormattedText(text,CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,font,fontSize,brush,VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(f,new Point(x,y));
        }
        double Origin {get{return Left?ActualWidth-18:18;}}
        double Direction {get{return Left?-1:1;}}
        double Radius {get{return ActualHeight/2;}}
        static double Length(int hand){return hand==0?67:hand==1?94:102;}
        double Y(double consumed,int hand){double period=hand==0?100:hand==1?10:1;return ActualHeight/2-Math.Cos(consumed*2*Math.PI/period)*ActualHeight*Length(hand)/360;}
        Pen HandPen(int hand,bool estimated){return hand==0?(Light?hourLight:hourDark):hand==1?(Light?minuteLight:minuteDark):estimated?estimatedPen:exactPen;}
        void BuildGrid()
        {
            grid=new DrawingGroup();using(var dc=grid.Open()){
                dc.DrawRoundedRectangle(Paint.Brush(Light?"#F3F7FB":"#151D28"),new Pen(Paint.Brush(Light?"#D8E2EC":"#2A394B"),.7),new Rect(2,10,Math.Max(0,ActualWidth-4),Math.Max(0,ActualHeight-20)),12,12);
                Brush text=Paint.Brush(Light?"#52667B":"#A0B1C4");
                double x0=Origin,x1=Origin+Direction*Radius*3;
                var pen=Faint(Light);
                foreach(double fraction in new[]{-1.0,0,1.0}){
                    double y=ActualHeight/2+fraction*ActualHeight*102/360;
                    dc.DrawLine(pen,new Point(Math.Min(x0,x1),y),new Point(Math.Max(x0,x1),y));
                }
                for(int i=0;i<=3;i++){
                    double x=Origin+Direction*Radius*i;
                    dc.DrawLine(pen,new Point(x,ActualHeight*.2),new Point(x,ActualHeight*.8));
                    string label=i==0?"现在":(i*5)+"分前";
                    double labelX=Math.Max(8,Math.Min(ActualWidth-38,x-12));
                    Text(dc,label,labelX,ActualHeight-29,10,text);
                }
                Text(dc,Demo?"演示 · 三针投影":"三针投影",12,17,12,text);
                for(int h=0;h<3;h++){
                    double x=ActualWidth-112+h*33;Pen legendPen=(HandMask&(1<<h))!=0?HandPen(h,h==2&&history.CurrentEstimated):Faint(Light);
                    dc.DrawLine(legendPen,new Point(x,25),new Point(x+10,25));Text(dc,h==0?"时":h==1?"分":"秒",x+12,18,10,text);
                }
            }grid.Freeze();
        }
        void BuildPaths()
        {
            var items=history.Points;anchor=items.Count==0?TrailHistory.Now:items[items.Count-1].Time;
            paths[0]=Path(0,false,items);paths[1]=Path(1,false,items);paths[2]=Path(2,false,items);paths[3]=Path(2,true,items);revision=history.Revision;
        }
        StreamGeometry Path(int hand,bool estimated,IList<TrailPoint> points)
        {
            var path=new StreamGeometry();using(var dc=path.Open()){
                for(int i=1;i<points.Count;i++){
                    var a=points[i-1];var b=points[i];if(hand==2&&b.Estimated!=estimated||!TrailHistory.CanJoin(a,b,hand))continue;
                    double x=Origin+Direction*TrailHistory.Offset(anchor-a.Time,Radius);
                    dc.BeginFigure(new Point(x,Y(a.Consumed,hand)),false,false);
                    int steps=Math.Max(1,Math.Min(96,(int)Math.Ceiling(Math.Max((b.Time-a.Time)*Radius/900,Math.Abs(b.Consumed-a.Consumed)/(hand==0?100:hand==1?10:1)*32))));
                    for(int k=1;k<=steps;k++){
                        double t=k/(double)steps,time=a.Time+(b.Time-a.Time)*t,phase=a.Consumed+(b.Consumed-a.Consumed)*t;
                        dc.LineTo(new Point(Origin+Direction*TrailHistory.Offset(anchor-time,Radius),Y(phase,hand)),true,false);
                    }
                }
            }path.Freeze();return path;
        }
        public void Advance(){if(IsVisible)InvalidateVisual();}
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);if(ActualHeight<1||ActualWidth<1)return;
            if(grid==null||sizeKey!=ActualHeight||gridWidth!=ActualWidth||lightKey!=Light||leftKey!=Left||demoKey!=Demo||maskKey!=HandMask){
                sizeKey=ActualHeight;gridWidth=ActualWidth;lightKey=Light;leftKey=Left;demoKey=Demo;maskKey=HandMask;BuildGrid();revision=-1;
            }
            if(revision!=history.Revision)BuildPaths();
            dc.DrawDrawing(grid);
            double now=TrailHistory.Now;
            dc.PushClip(new RectangleGeometry(new Rect(8,ActualHeight*.19,ActualWidth-16,ActualHeight*.62)));
            scroll.X=Direction*TrailHistory.Offset(now-anchor,Radius);dc.PushTransform(scroll);
            for(int h=0;h<3;h++)if((HandMask&(1<<h))!=0){
                dc.DrawGeometry(null,HandPen(h,false),paths[h]);if(h==2)dc.DrawGeometry(null,estimatedPen,paths[3]);
            }dc.Pop();
            var items=history.Points;
            for(int hand=0;hand<3;hand++)if((HandMask&(1<<hand))!=0){
                for(int i=0;i<items.Count;i++){
                    var p=items[i];if(!(hand==2?p.Valid:p.CoarseValid))continue;
                    double x=Origin+Direction*TrailHistory.Offset(now-p.Time,Radius);if(x<8||x>ActualWidth-8)continue;
                    dc.DrawEllipse(HandPen(hand,p.Estimated).Brush,null,new Point(x,Y(p.Consumed,hand)),1.35,1.35);
                }
                bool ready=hand==2?history.CurrentValid:history.CurrentCoarseValid;
                if(ready){
                    double y=Y(history.CurrentConsumed,hand);Pen pen=HandPen(hand,history.CurrentEstimated);
                    var live=new TrailPoint{Time=now,Consumed=history.CurrentConsumed,CoarseValid=history.CurrentCoarseValid,Valid=history.CurrentValid,Estimated=history.CurrentEstimated,Epoch=history.CurrentEpoch};
                    if(items.Count>0){var last=items[items.Count-1];if(TrailHistory.CanJoin(last,live,hand))dc.DrawLine(pen,new Point(Origin,y),new Point(Origin+Direction*TrailHistory.Offset(now-last.Time,Radius),Y(last.Consumed,hand)));}
                    dc.DrawEllipse(pen.Brush,null,new Point(Origin,y),2.5,2.5);dc.DrawLine(Faint(Light),new Point(Left?ActualWidth:0,y),new Point(Origin,y));
                }
            }
            dc.Pop();
            if(!history.CurrentCoarseValid)Text(dc,"等待额度数据",Math.Max(15,ActualWidth/2-42),ActualHeight/2-8,12,Paint.Brush(Light?"#64788D":"#8194AA"));
        }
    }
}
