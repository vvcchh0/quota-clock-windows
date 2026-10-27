using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace QuotaClock
{
    public sealed class RunHistoryChart:FrameworkElement
    {
        ProjectionView view=new ProjectionView();
        DrawingGroup drawing;
        public bool Light;
        public int HandMask=7;
        internal ProjectionView CurrentView {get{return view;}}
        double width=-1,height=-1;bool lastLight;int lastMask;
        static readonly Typeface font=new Typeface("Segoe UI, Microsoft YaHei UI");
        public void SetView(ProjectionView value){view=value;drawing=null;InvalidateVisual();}
        public void Refresh(){drawing=null;InvalidateVisual();}
        void Text(DrawingContext dc,string value,double x,double y,double size,Brush brush)
        {
            dc.DrawText(new FormattedText(value,CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,font,size,brush,VisualTreeHelper.GetDpi(this).PixelsPerDip),new Point(x,y));
        }
        public static string TimeLabel(double seconds)
        {
            var t=TimeSpan.FromSeconds(Math.Max(0,seconds));return t.TotalDays>=1?((int)t.TotalDays)+"天 "+t.ToString(@"hh\:mm"):t.ToString(@"hh\:mm\:ss");
        }
        static Pen Pen(Brush brush,double thickness,bool dashed=false){var p=new Pen(brush,thickness);if(dashed)p.DashStyle=DashStyles.Dash;p.Freeze();return p;}
        Brush Color(int h,bool estimate){return Paint.Brush(h==0?(Light?"#24354B":"#E5ECF5"):h==1?(Light?"#087F8C":"#65DCD4"):estimate?"#EFB947":"#FB8B7B");}
        void Build()
        {
            drawing=new DrawingGroup();width=ActualWidth;height=ActualHeight;lastLight=Light;lastMask=HandMask;
            using(var dc=drawing.Open()){
                Brush text=Paint.Brush(Light?"#52667B":"#A0B1C4");
                dc.DrawRoundedRectangle(Paint.Brush(Light?"#F3F7FB":"#151D28"),null,new Rect(0,0,width,height),10,10);
                var plot=new Rect(56,38,Math.Max(1,width-78),Math.Max(1,height-92));double center=plot.Top+plot.Height/2,amplitude=plot.Height*.45;
                Func<double,double> x=t=>plot.Left+(t-view.Start)/Math.Max(1,view.End-view.Start)*plot.Width;
                Func<double,double> y=v=>center+v*amplitude;
                var grid=Pen(Paint.Brush(Light?"#D4DFE8":"#344456"),.7);
                for(int i=0;i<3;i++){double yy=plot.Top+i*plot.Height/2;dc.DrawLine(grid,new Point(plot.Left,yy),new Point(plot.Right,yy));Text(dc,i==0?"向上":i==1?"表心":"向下",10,yy-8,11,text);}
                for(int i=0;i<=5;i++){
                    double xx=plot.Left+plot.Width*i/5;dc.DrawLine(grid,new Point(xx,plot.Top),new Point(xx,plot.Bottom));
                    string label=TimeLabel(view.Start+(view.End-view.Start)*i/5);Text(dc,label,Math.Max(plot.Left-10,Math.Min(plot.Right-70,xx-25)),plot.Bottom+12,10,text);
                }
                Text(dc,"针尖纵向位置 · 与实际针长同比例",15,12,11,text);
                Text(dc,view.Aggregated?"长时间视图：峰谷范围；放大读取原始采样":"15 秒采样 · 缺失与校准跳变断线",Math.Max(15,width-310),12,11,text);
                dc.PushClip(new RectangleGeometry(plot));
                for(int h=0;h<3;h++)if((HandMask&(1<<h))!=0){
                    for(int mode=0;mode<(h==2?2:1);mode++){
                        bool estimated=mode==1;var geometry=new StreamGeometry();ProjectionBucket previous=null;
                        using(var path=geometry.Open())foreach(var b in view.Buckets){
                            if(b.Last.Time<view.Start||b.First.Time>view.End){previous=b;continue;}
                            bool source=h!=2||b.First.Estimated==estimated;
                            if(previous!=null&&source&&ProjectionSample.CanJoin(previous.Last,b.First,h)){
                                path.BeginFigure(new Point(x(previous.Last.Time),y(previous.Last.Y(h))),false,false);path.LineTo(new Point(x(b.First.Time),y(b.First.Y(h))),true,false);
                            }
                            if(b.Count>1&&b.Min[h]<=b.Max[h]&&mode==0){
                                double left=Math.Max(plot.Left,x(b.First.Time)),right=Math.Min(plot.Right,x(b.Last.Time));
                                var color=(Color)Color(h,b.Estimated).GetValue(SolidColorBrush.ColorProperty);
                                dc.DrawRectangle(Paint.Alpha(color,45),null,new Rect(left,Math.Min(y(b.Min[h]),y(b.Max[h])),Math.Max(1,right-left),Math.Max(.8,Math.Abs(y(b.Max[h])-y(b.Min[h])))));
                            }
                            if(b.Count==1&&source&&b.First.Valid(h))dc.DrawEllipse(Color(h,estimated),null,new Point(x(b.First.Time),y(b.First.Y(h))),1.3,1.3);
                            previous=b;
                        }
                        geometry.Freeze();dc.DrawGeometry(null,Pen(Color(h,estimated),1.4,estimated),geometry);
                    }
                }
                dc.Pop();
                if(view.Samples==0)Text(dc,"本次运行还没有记录，开启记录后逐步积累。",Math.Max(60,width/2-150),height/2-8,13,text);
                Text(dc,"运行时间",Math.Max(plot.Left,width/2-22),height-18,10,text);
            }drawing.Freeze();
        }
        protected override void OnRender(DrawingContext dc){base.OnRender(dc);if(ActualWidth<100||ActualHeight<100)return;if(drawing==null||width!=ActualWidth||height!=ActualHeight||lastLight!=Light||lastMask!=HandMask)Build();dc.DrawDrawing(drawing);}
    }
    public sealed class RunHistoryWindow:Window
    {
        readonly ClockController app;
        readonly List<CheckBox> handChecks=new List<CheckBox>();
        readonly RunHistoryChart chart=new RunHistoryChart();
        readonly ComboBox range=new ComboBox{Width=135,MinHeight=32,ItemsSource=new[]{"全程","15 分钟","1 小时","6 小时","24 小时"},SelectedIndex=0};
        readonly Slider pan=new Slider{Minimum=0,Maximum=100,Value=100,Margin=new Thickness(10,3,0,0)};
        readonly CheckBox follow=new CheckBox{Content="跟随最新",IsChecked=true,VerticalAlignment=VerticalAlignment.Center};
        readonly TextBlock summary=UI.Muted(""),status=UI.Muted(""),source=UI.Muted("");
        readonly DispatcherTimer debounce=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(180)};
        readonly EventHandler tick,changed;
        bool building,closed,loading,lastDemo;
        int request,lastRevision=-1;
        CancellationTokenSource cancel;
        string loadedPath,lastRecordState;
        ProjectionView loaded;
        public RunHistoryChart Chart {get{return chart;}}
        public RunHistoryWindow(ClockController value)
        {
            app=value;lastDemo=app.Demo;Title="额度时钟 · 运行轨迹";Width=1010;Height=660;MinWidth=760;MinHeight=520;WindowStartupLocation=WindowStartupLocation.CenterScreen;
            FontFamily=new FontFamily("Segoe UI, Microsoft YaHei UI");FontSize=13;
            var body=new Grid{Margin=new Thickness(24)};
            foreach(var row in new[]{GridLength.Auto,GridLength.Auto,GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto,GridLength.Auto})body.RowDefinitions.Add(new RowDefinition{Height=row});
            var header=new DockPanel{Margin=new Thickness(0,0,0,8)};
            var buttons=new StackPanel{Orientation=Orientation.Horizontal};
            buttons.Children.Add(UI.Button("本次运行",delegate{loaded=null;loadedPath=null;follow.IsEnabled=true;follow.IsChecked=true;Schedule();}));
            var open=UI.Button("打开记录…",Open);open.Margin=new Thickness(8,0,0,0);open.IsEnabled=!app.Preview;buttons.Children.Add(open);
            var export=UI.Button("导出 CSV…",Export);export.Margin=new Thickness(8,0,0,0);export.IsEnabled=!app.Preview;buttons.Children.Add(export);
            DockPanel.SetDock(buttons,Dock.Right);header.Children.Add(buttons);header.Children.Add(UI.Text("三针运行轨迹",26,true));body.Children.Add(header);
            source.Margin=new Thickness(0,0,0,14);Grid.SetRow(source,1);body.Children.Add(source);
            var options=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,14)};
            options.Children.Add(range);
            for(int h=0;h<3;h++){
                int hand=h;var box=new CheckBox{Content=h==0?"时针":h==1?"分针":"秒针",IsChecked=(app.TrailMask&(1<<h))!=0,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(20,0,0,0)};
                Action update=delegate{app.SetTrailHand(hand,box.IsChecked==true);chart.HandMask=app.TrailMask;chart.Refresh();};box.Checked+=delegate{update();};box.Unchecked+=delegate{update();};handChecks.Add(box);options.Children.Add(box);
            }
            follow.SetResourceReference(Control.ForegroundProperty,"Text");follow.Margin=new Thickness(26,0,0,0);options.Children.Add(follow);
            AutomationProperties.SetName(range,"显示时间范围");AutomationProperties.SetName(pan,"历史区间位置");
            Grid.SetRow(options,2);body.Children.Add(options);Grid.SetRow(chart,3);body.Children.Add(chart);
            var navigation=new DockPanel{Margin=new Thickness(0,12,0,8)};navigation.Children.Add(UI.Muted("拖动回看",11));navigation.Children.Add(pan);Grid.SetRow(navigation,4);body.Children.Add(navigation);
            var footer=new StackPanel();footer.Children.Add(summary);status.Margin=new Thickness(0,5,0,0);footer.Children.Add(status);Grid.SetRow(footer,5);body.Children.Add(footer);Content=body;
            range.SelectionChanged+=delegate{if(!building){follow.IsChecked=loadedPath==null;Schedule();}};
            follow.Checked+=delegate{if(!building){building=true;pan.Value=100;building=false;Schedule();}};
            pan.ValueChanged+=delegate{if(!building){follow.IsChecked=false;Schedule();}};
            debounce.Tick+=delegate{debounce.Stop();Reload();};
            tick=delegate{if(IsVisible)UpdateRecordState();if(IsVisible&&!loading&&loadedPath==null&&!app.Demo&&follow.IsChecked==true&&lastRevision!=app.Recording.Timeline.Revision)Reload();};app.Tick+=tick;
            changed=delegate{if(IsVisible){chart.HandMask=app.TrailMask;ApplyTheme();if(lastDemo!=app.Demo){lastDemo=app.Demo;if(loadedPath==null){loaded=null;Schedule();}}}};app.Changed+=changed;
            Closed+=delegate{closed=true;request++;if(cancel!=null)cancel.Cancel();debounce.Stop();app.Tick-=tick;app.Changed-=changed;app.ForgetRunHistory(this);};
            SourceInitialized+=delegate{Native.ThemeWindow(this,app.Light);};
            KeyDown+=delegate(object sender,System.Windows.Input.KeyEventArgs e){if(e.Key==System.Windows.Input.Key.Escape)Close();};
            ApplyTheme();Reload();
        }
        void UpdateRecordState()
        {
            if(app.Demo||loadedPath!=null)return;
            string state=app.Recording.Enabled+"|"+app.Recording.Error+"|"+app.Recording.Dropped;
            if(state==lastRecordState)return;lastRecordState=state;
            status.Text=app.Recording.Error??((app.Recording.Enabled?"记录中 · CSV 在程序目录 history/ · 关闭窗口后继续记录":"记录已暂停 · 已写入的 CSV 保留")+(app.Recording.Dropped>0?" · 未写入 "+app.Recording.Dropped+" 点":""));
        }
        internal void SelectTestRange(int index,double position)
        {
            building=true;range.SelectedIndex=index;follow.IsChecked=false;pan.Value=position;building=false;Reload();
        }
        public void ApplyTheme(){UI.Theme(this,app.Light);chart.Light=app.Light;chart.HandMask=app.TrailMask;for(int i=0;i<handChecks.Count;i++)handChecks[i].Foreground=Paint.Brush(i==0?(app.Light?"#24354B":"#E5ECF5"):i==1?(app.Light?"#087F8C":"#65DCD4"):"#FB8B7B");chart.Refresh();}
        void Schedule(){if(closed)return;if(cancel!=null)cancel.Cancel();debounce.Stop();debounce.Start();}
        static ProjectionView DemoView(decimal remaining)
        {
            double used=(double)(100-remaining),duration=Math.Min(7200,used*300),start=used-duration/300;var timeline=new RunTimeline();
            for(double t=0;t<=duration;t+=15)timeline.Add(ProjectionSample.Create(t,start+t/300,7,false,1));
            return timeline.View(0,duration,DateTimeOffset.Now.AddSeconds(-duration));
        }
        async void Reload()
        {
            if(closed)return;int version=++request;loading=true;if(cancel!=null)cancel.Cancel();cancel=new CancellationTokenSource();var operation=cancel;var token=operation.Token;
            try{
                bool demo=app.Demo&&loadedPath==null;
                if(demo&&loaded==null)loaded=DemoView(app.DemoRemaining);
                double duration=loadedPath!=null||demo?loaded.End:app.Recording.Timeline.Duration;
                double[] spans={Double.PositiveInfinity,900,3600,21600,86400};double span=spans[Math.Max(0,range.SelectedIndex)];
                bool all=Double.IsInfinity(span)||span>=duration;
                building=true;pan.IsEnabled=!all;
                if(follow.IsChecked==true)pan.Value=100;
                double end=all?duration:Math.Min(duration,span+(duration-span)*pan.Value/100),start=all?0:Math.Max(0,end-span);building=false;
                ProjectionView view;
                if(demo)view=Slice(loaded,start,end);
                else if(loadedPath!=null){string path=loadedPath;view=all?loaded:await Task.Run(()=>RunRecording.ReadFile(path,start,end,0,loaded.Started,token));}
                else{lastRevision=app.Recording.Timeline.Revision;view=all?app.Recording.Timeline.View(start,end,app.Recording.Started):await Task.Run(()=>app.Recording.ReadRange(start,end,token));}
                if(closed||version!=request)return;
                chart.SetView(view);
                source.Text=demo?"演示数据 · 两小时示例 · 不写入真实运行记录":loadedPath!=null?"已保存记录 · "+Path.GetFileName(loadedPath):"本次启动 "+app.Recording.Started.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")+" · 每 15 秒本地记录";
                long count=loadedPath!=null||demo?loaded.Samples:app.Recording.Timeline.Count;
                summary.Text="记录跨度 "+RunHistoryChart.TimeLabel(duration)+" · "+count.ToString("N0")+" 个采样 · 当前区间 "+RunHistoryChart.TimeLabel(start)+" — "+RunHistoryChart.TimeLabel(end)+(view.Aggregated?" · 总览已保留峰谷范围":"");
                status.Text=demo?"这是匀速示例；真实历史从启用新版记录时开始积累。":loadedPath!=null?"原始采样可缩放查看或导出；记录只保存在本机。":app.Recording.Error??(app.Recording.Enabled?"记录中 · CSV 在程序目录 history/ · 关闭窗口后继续记录":"记录已暂停 · 已写入的 CSV 保留")+(app.Recording.Dropped>0?" · 未写入 "+app.Recording.Dropped+" 点":"");
            }catch(OperationCanceledException){}
            catch(Exception){if(!closed&&version==request)status.Text="读取记录失败；请确认是本程序生成的 CSV，且文件仍可访问。";}
            finally{if(version==request)loading=false;operation.Dispose();if(Object.ReferenceEquals(cancel,operation))cancel=null;}
        }
        static ProjectionView Slice(ProjectionView input,double start,double end){return new ProjectionView{Buckets=input.Buckets.Where(b=>b.Last.Time>=start-15&&b.First.Time<=end+15).ToArray(),Start=start,End=Math.Max(start+1,end),Samples=input.Samples,Started=input.Started};}
        async void Open()
        {
            var dialog=new OpenFileDialog{Title="打开额度时钟运行记录",Filter="运行轨迹 CSV|*.csv",InitialDirectory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"history")};
            if(dialog.ShowDialog(this)!=true)return;
            int version=++request;loading=true;if(cancel!=null)cancel.Cancel();cancel=new CancellationTokenSource();var operation=cancel;var token=operation.Token;status.Text="正在读取记录…";
            try{var result=await Task.Run(()=>RunRecording.ReadFile(dialog.FileName,0,Double.PositiveInfinity,0,DateTimeOffset.Now,token));if(closed||version!=request)return;loadedPath=dialog.FileName;loaded=result;follow.IsChecked=false;follow.IsEnabled=false;building=true;range.SelectedIndex=0;pan.Value=100;building=false;Reload();}
            catch(OperationCanceledException){}catch(Exception){status.Text="无法打开：需要本程序生成的有效 CSV。";}finally{if(version==request)loading=false;operation.Dispose();if(Object.ReferenceEquals(cancel,operation))cancel=null;}
        }
        async void Export()
        {
            string path=loadedPath??app.Recording.FilePath;
            if(path==null||!File.Exists(path)){status.Text="还没有可导出的记录。";return;}
            var dialog=new SaveFileDialog{Title="导出运行轨迹",Filter="CSV 文件|*.csv",FileName=Path.GetFileName(path),OverwritePrompt=true};if(dialog.ShowDialog(this)!=true)return;
            try{if(Path.GetFullPath(path).Equals(Path.GetFullPath(dialog.FileName),StringComparison.OrdinalIgnoreCase)){status.Text="这就是原始记录文件，无需覆盖。";return;}await Task.Run(()=>File.Copy(path,dialog.FileName,true));if(!closed)status.Text="已导出写入完成的原始采样。";}catch(Exception){if(!closed)status.Text="导出失败，请选择可写目录。";}
        }
    }
}
