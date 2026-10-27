using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Web.Script.Serialization;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace QuotaClock
{
    public sealed class Preferences
    {
        public int Version = 5;
        public bool RecordRun=true, HourTrail=true, MinuteTrail=true, FineTrail=true;
        public int RefreshSeconds = 15;
        public bool ShowTrail = true, TrailLeft;
        public bool EstimateFine = true, ReuseConnection = true;
        public string Theme = "system";
        public bool ShowOrb = true, ShowTaskbar = true, Topmost = true;
        public double OrbSize = 200, OrbOpacity = 1, OrbLeft = Double.NaN, OrbTop = Double.NaN;
        public int RefreshMinutes = 5, TaskbarOffset = 0;
        public string SelectedId;
        public static Preferences Load()
        {
            var result = new Preferences();
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preferences.json");
                if (!File.Exists(path)) return result;
                var json = new JavaScriptSerializer();
                var map = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
                if (map.ContainsKey("Version")) result = json.Deserialize<Preferences>(File.ReadAllText(path));
                else if (map.ContainsKey("light")) result.Theme = Convert.ToBoolean(map["light"]) ? "light" : "dark";
            }
            catch { }
            result.Version=5;
            result.RefreshSeconds = Math.Max(5, Math.Min(1800, result.RefreshSeconds));
            result.OrbSize = Math.Max(140, Math.Min(320, result.OrbSize));
            result.OrbOpacity = Math.Max(.35, Math.Min(1, result.OrbOpacity));
            result.RefreshMinutes = Math.Max(1, Math.Min(30, result.RefreshMinutes));
            result.TaskbarOffset = Math.Max(0, Math.Min(600, result.TaskbarOffset));
            if (result.Theme != "light" && result.Theme != "dark") result.Theme = "system";
            return result;
        }
        public void Save()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preferences.json");
            try
            {
                // JSON has no NaN: use an unset location sentinel outside the virtual desktop.
                var copy = (Preferences)MemberwiseClone();
                if (Double.IsNaN(copy.OrbLeft)) copy.OrbLeft = -100000;
                if (Double.IsNaN(copy.OrbTop)) copy.OrbTop = -100000;
                string temp = path + ".tmp";
                File.WriteAllText(temp, new JavaScriptSerializer().Serialize(copy));
                if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
            }
            catch { }
        }
    }

    public sealed class ClockController : IDisposable
    {
        public readonly Preferences Prefs;
        public readonly bool Preview;
        public bool Exiting, Demo, Busy;
        public decimal DemoRemaining = 73.4m;
        public bool WaveDemoPlaying;
        double demoStart, demoConsumed;
        int failures,readSuccesses;
        public readonly TrailHistory History = new TrailHistory();
        public readonly RunRecording Recording;
        RunHistoryWindow runHistory;
        public bool HasRunHistory {get{return runHistory!=null;}}
        public int TrailMask {get{return (Prefs.HourTrail?1:0)|(Prefs.MinuteTrail?2:0)|(Prefs.FineTrail?4:0);}}
        public Snapshot Snapshot;
        public QuotaWindow Selected;
        public string Error;
        public DateTimeOffset LastAttempt;
        public event EventHandler Changed, Tick;
        readonly CodexClient client = new CodexClient();
        readonly DispatcherTimer timer = new DispatcherTimer(), saveTimer = new DispatcherTimer();
        readonly HashSet<string> resetAttempts = new HashSet<string>();
        public readonly OrbWindow Orb;
        DetailsWindow details;
        SettingsWindow settings;
        public DetailsWindow Details { get { if(details==null)details=new DetailsWindow(this);return details; } }
        public SettingsWindow Settings { get { if(settings==null)settings=new SettingsWindow(this);return settings; } }
        public bool HasDetails { get { return details!=null; } }
        public void ForgetDetails(DetailsWindow value) { if(details==value)details=null;WriteStatus(); }
        public void ForgetSettings(SettingsWindow value) { if(settings==value)settings=null;WriteStatus(); }
        LocalQuotaMeter meter;
        int meterGeneration;bool started;
        public EstimateView MeterView = new EstimateView();
        string lastRuntime;
        public NativeTaskbar Taskbar;
        Forms.NotifyIcon tray;
        Drawing.Icon icon;
        bool disposed;
        public int DetailOpenCount, SettingsOpenCount, OrbDoubleClickCount, TaskbarInvokeCount;
        public bool Light { get { return Prefs.Theme == "light" || Prefs.Theme == "system" && UI.SystemLight(false); } }
        public bool Known { get { return Demo || Selected != null; } }
        public bool Expired { get { return !Demo && Selected != null && QuotaMath.Expired(Selected, DateTimeOffset.Now); } }
        public bool Estimated { get { return !Demo && Prefs.EstimateFine && MeterView.Available && !MeterView.Exact && Known && !Expired; } }
        public bool LocalFine { get { return !Demo && Prefs.EstimateFine && MeterView.Available && Known && !Expired; } }
        public bool Fine { get { return Demo || Selected != null && Selected.Fractional || LocalFine; } }
        public decimal PositionRemaining { get { return LocalFine ? MeterView.Remaining : Remaining; } }
        public string PrecisionText { get {
            if(Demo)return "演示精度 0.1% · 当前不是账号额度";
            if(Selected!=null&&Selected.Fractional)return "服务端小数额度";
            if(!Prefs.EstimateFine)return "官方整数额度 · 本地估算已关闭";
            if(LocalFine)return MeterView.Exact ? "本地原始小数 "+QuotaMath.Percent(MeterView.Remaining,true) :
                "≈ "+QuotaMath.Percent(MeterView.Remaining,true)+" · 本地估算，非官方精度";
            return "官方整数额度 · "+MeterView.State;
        } }
        public decimal Remaining { get { return Demo ? QuotaMath.Display(DemoRemaining) : Selected == null ? 0 : Selected.Remaining; } }
        public string Percent { get { return Known && !Expired ? QuotaMath.Percent(Remaining, Demo || Selected != null && Selected.Fractional) : "—"; } }
        public string Source { get { return Demo ? "演示数据" : Selected == null ? "Codex · 暂无额度" : Selected.Label; } }
        public string ResetText { get { return Demo ? "演示数据，不代表账号额度" : Selected == null ? "尚未取得重置时间" : QuotaMath.ResetText(Selected, DateTimeOffset.Now); } }
        public string FreshText
        {
            get
            {
                if (Demo) return "演示模式";
                if (Busy) return "正在读取额度…";
                if (Error != null) return Error + (Selected == null ? "" : " · 保留上次数据");
                return Snapshot == null ? "等待额度" : "更新于 " + Snapshot.Fetched.ToLocalTime().ToString("HH:mm:ss") + " · 每 " + Prefs.RefreshSeconds + " 秒更新";
            }
        }
        public ClockController(bool preview)
        {
            Preview = preview; Prefs = preview ? new Preferences { Theme = "light" } : Preferences.Load();
            Demo = preview;
            Recording=new RunRecording(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"history"),Prefs.RecordRun&&!preview,!preview);
            History.Sampled+=delegate(TrailPoint point,string key){if(!Demo)Recording.Record(point,key);};
            Orb = new OrbWindow(this);
            saveTimer.Interval = TimeSpan.FromMilliseconds(350);
            saveTimer.Tick += delegate { saveTimer.Stop(); if (!Preview) Prefs.Save(); };
            timer.Interval = TimeSpan.FromSeconds(1);
            timer.Tick += delegate
            {
                if (Exiting) return;
                if(Demo && WaveDemoPlaying){DemoRemaining=100-(decimal)((demoConsumed+(TrailHistory.Now-demoStart)/300)%100);Notify();}
                CaptureTrail();
                if (Tick != null) Tick(this, EventArgs.Empty);
                if (!Demo && !Busy && !Preview)
                {
                    bool periodic = DateTimeOffset.Now - LastAttempt >= TimeSpan.FromSeconds(Math.Min(1800,Prefs.RefreshSeconds*Math.Pow(2,Math.Min(failures,4))));
                    bool expired = Expired && resetAttempts.Add(Selected.Id + ":" + Selected.Reset);
                    if (periodic || expired) { var task = Refresh(); }
                }
            };
        }
        public void Start()
        {
            started=true;
            if (!Preview)
            {
                icon = UI.MakeIcon();
                tray = new Forms.NotifyIcon { Icon = icon, Text = "额度时钟 · 正在读取", Visible = true };
                tray.DoubleClick += delegate { Dispatch(ShowDetails); };
                var menu = new Forms.ContextMenuStrip();
                menu.Items.Add("额度详情", null, delegate { Dispatch(ShowDetails); });
                menu.Items.Add("三针运行轨迹",null,delegate{Dispatch(ShowRunHistory);});
                menu.Items.Add("设置", null, delegate { Dispatch(ShowSettings); });
                menu.Items.Add("显示 / 隐藏悬浮表盘", null, delegate { Dispatch(delegate { Prefs.ShowOrb = !Prefs.ShowOrb; ApplyPreferences(); }); });
                menu.Items.Add("刷新额度", null, delegate { Dispatch(delegate { Demo = false; var task = Refresh(); }); });
                menu.Items.Add(new Forms.ToolStripSeparator());
                menu.Items.Add("退出额度时钟", null, delegate { Dispatch(Exit); });
                tray.ContextMenuStrip = menu;
                Taskbar = new NativeTaskbar(this);
            }
            if (Prefs.ShowOrb) Orb.Show();
            ApplyPreferences();
            if (!Preview) {
                ConfigureMeter();
                timer.Start(); var task = Refresh();
            }
        }
        void ConfigureMeter()
        {
            if(Preview)return;
            if(!Prefs.EstimateFine) {
                if(meter!=null){meterGeneration++;meter.Dispose();meter=null;}
                MeterView=new EstimateView{State="本地估算已关闭"};return;
            }
            if(meter==null) {
                int generation=++meterGeneration;
                meter=new LocalQuotaMeter(delegate(EstimateView view) {
                    if(Exiting)return;
                    Application.Current.Dispatcher.BeginInvoke(new Action(delegate {
                        if(Exiting||generation!=meterGeneration)return;MeterView=view;Notify();
                    }));
                },!Demo);
                if(Snapshot!=null)meter.Observe(Selected,Snapshot.Fetched);
            }
            meter.Enabled=!Demo;
        }
        void Dispatch(Action action) { Application.Current.Dispatcher.BeginInvoke(action); }
        public async System.Threading.Tasks.Task Refresh()
        {
            if (Busy || Exiting || Preview) return;
            Busy = true; LastAttempt = DateTimeOffset.Now; Notify();
            try
            {
                var result = await client.ReadAsync(Prefs.ReuseConnection && Prefs.RefreshSeconds<=60);
                if (Exiting) return;
                Snapshot = result; failures=0;readSuccesses++;
                Selected = result.Windows.FirstOrDefault(w => w.Id == Prefs.SelectedId) ?? result.Windows.FirstOrDefault();
                Error = result.Windows.Count == 0 ? "此账号暂未提供额度窗口" : null;
                if (Selected != null) Prefs.SelectedId = Selected.Id;
                if(meter!=null)meter.Observe(Selected,result.Fetched);
            }
            catch (Exception e)
            {
                failures++;
                if (!Exiting) Error = e is InvalidOperationException || e is TimeoutException ? e.Message : "额度读取失败，请稍后刷新";
            }
            finally { Busy = false; if(Demo||!Prefs.ReuseConnection||Prefs.RefreshSeconds>60)client.ReleaseIdle(); if (!Exiting) Notify(); }
        }
        public void Select(QuotaWindow w) { Selected = w; Prefs.SelectedId = w.Id; if(meter!=null)meter.Observe(w,Snapshot.Fetched); Notify(); Save(); }
        public void SetDemo(bool value) { Demo = value;Recording.SetEnabled(Prefs.RecordRun&&!value); if(!value)WaveDemoPlaying=false;else client.ReleaseIdle(); if(started)ConfigureMeter(); Notify(); if (!value && Snapshot == null) { var task = Refresh(); } }
        void CaptureTrail()
        {
            bool coarse=Known && !Expired && (Error==null || Snapshot!=null && DateTimeOffset.Now-Snapshot.Fetched<TimeSpan.FromMinutes(2));
            bool valid=coarse && Fine;
            History.Capture(TrailHistory.Now,PositionRemaining,valid,Estimated,Demo?"demo":Selected==null?"unknown":Selected.Id+":"+Selected.Reset,coarse);
        }
        public void SetWaveDemo(bool playing)
        {
            if(playing){SetDemo(true);demoStart=TrailHistory.Now;demoConsumed=(double)(100-DemoRemaining);History.Clear();
                for(int i=60;i>=0;i--){double t=demoStart-i*15;History.Capture(t,100-(decimal)(demoConsumed-i*15/300.0),true,false,"demo");}
            }
            WaveDemoPlaying=playing;Notify();
        }
        public void ApplyPreferences()
        {
            if (Exiting) return;
            if(!Preview && (!Prefs.ReuseConnection||Prefs.RefreshSeconds>60))client.ReleaseIdle();
            Recording.SetEnabled(Prefs.RecordRun&&!Demo);
            Orb.Apply();
            if (Prefs.ShowOrb) { if (!Orb.IsVisible) Orb.Show(); } else Orb.Hide();
            if(details!=null)details.ApplyTheme(); if(settings!=null)settings.ApplyTheme();if(runHistory!=null)runHistory.ApplyTheme();
            if(started)ConfigureMeter();
            if (Taskbar != null) Taskbar.Apply();
            Notify(); Save();
        }
        public void Save() { if (!Preview) { saveTimer.Stop(); saveTimer.Start(); } }
        public void Notify()
        {
            CaptureTrail();
            if (Changed != null) Changed(this, EventArgs.Empty);
            if (Taskbar != null) Taskbar.Update();
            if (tray != null)
            {
                string text = "额度时钟 · " + Source + " · 剩余 " + Percent;
                tray.Text = text.Length > 62 ? text.Substring(0, 62) : text;
            }
            WriteStatus();
        }
        public void ShowDetails()
        {
            if (Exiting) return;
            DetailOpenCount++;
            Details.PlaceNearOrb();
            Details.Show(); Details.WindowState = WindowState.Normal; Details.Activate();
            WriteStatus();
        }
        public void ShowRunHistory()
        {
            if(Exiting)return;if(runHistory==null)runHistory=new RunHistoryWindow(this);
            runHistory.Show();runHistory.WindowState=WindowState.Normal;runHistory.Activate();WriteStatus();
        }
        public RunHistoryWindow RunWindow {get{return runHistory;}}
        public void ForgetRunHistory(RunHistoryWindow value){if(runHistory==value)runHistory=null;WriteStatus();}
        public void SetTrailHand(int hand,bool visible)
        {
            if(hand==0)Prefs.HourTrail=visible;else if(hand==1)Prefs.MinuteTrail=visible;else Prefs.FineTrail=visible;ApplyPreferences();
        }
        public void ShowSettings() { if (Exiting) return; SettingsOpenCount++; Settings.Show(); Settings.WindowState = WindowState.Normal; Settings.Activate(); WriteStatus(); }
        public ContextMenu Menu()
        {
            var menu = new ContextMenu(); UI.Theme(menu, Light);
            Action<string, Action> add = (label, action) => { var item = new MenuItem { Header = label }; item.Click += delegate { action(); }; menu.Items.Add(item); };
            add("额度详情", ShowDetails);add("三针运行轨迹",ShowRunHistory); add("设置", ShowSettings);
            add(Prefs.ShowOrb ? "隐藏悬浮表盘" : "显示悬浮表盘", delegate { Prefs.ShowOrb = !Prefs.ShowOrb; ApplyPreferences(); });
            add(Prefs.ShowTrail ? "隐藏三针轨迹" : "显示三针轨迹", delegate { Prefs.ShowTrail = !Prefs.ShowTrail; ApplyPreferences(); });
            add("刷新额度", delegate { Demo = false; var task = Refresh(); });
            menu.Items.Add(new Separator()); add("退出额度时钟", Exit);
            return menu;
        }
        public void WriteStatus()
        {
            if (Preview) return;
            try
            {
                string json = new JavaScriptSerializer().Serialize(new {
                    version = "0.5.0", historySamples=Recording.Timeline.Count, historyWritten=Recording.Written, historyError=Recording.Error, historyDropped=Recording.Dropped, historyWindowVisible=runHistory!=null&&runHistory.IsVisible, trailMask=TrailMask, refreshSeconds=Prefs.RefreshSeconds, trailVisible=Prefs.ShowTrail, trailPoints=History.Points.Count,
                    connectionPid=client.ProcessId, connectionStarts=client.ConnectionsStarted, readSuccesses=readSuccesses, trayVisible = tray != null && tray.Visible,
                    orbVisible = Orb.IsVisible, detailsVisible = details!=null&&details.IsVisible, settingsVisible = settings!=null&&settings.IsVisible,
                    taskbar = Taskbar == null ? "not-started" : Taskbar.Presence,
                    taskbarBounds = Taskbar == null ? null : Taskbar.BoundsText,
                    taskbarChildVerified = Taskbar != null && Taskbar.VerifiedChild,
                    taskbarInvokes = TaskbarInvokeCount, orbDoubleClicks = OrbDoubleClickCount,
                    detailsOpened = DetailOpenCount, settingsOpened = SettingsOpenCount,
                    demo = Demo, busy = Busy, windows = Snapshot == null ? 0 : Snapshot.Windows.Count,
                    estimateAvailable=LocalFine, estimateState=MeterView.State,
                    error = Error == null ? null : "quota-read-error"
                });
                if(json!=lastRuntime){lastRuntime=json;File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtime-status.json"), json);}
            }
            catch { }
        }
        public void Exit() { if (Exiting) return; Exiting = true; Dispose(); Orb.Close(); if(details!=null)details.Close(); if(settings!=null)settings.Close();if(runHistory!=null)runHistory.Close(); Application.Current.Shutdown(); }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            timer.Stop(); saveTimer.Stop(); client.Dispose(); if(meter!=null)meter.Dispose();Recording.Dispose();
            if (!Preview) Prefs.Save();
            if (Taskbar != null) Taskbar.Dispose();
            if (tray != null) { tray.Visible = false; tray.ContextMenuStrip.Dispose(); tray.Dispose(); }
            if (icon != null) icon.Dispose();
        }
    }

    public sealed class OrbWindow : Window
    {
        readonly ClockController app;
        public readonly ClockFace Face = new ClockFace();
        public readonly WaveTrail Wave;
        readonly Grid layout=new Grid();
        bool placed,lastLeft,lastTrail;
        double lastWaveWidth;
        public OrbWindow(ClockController controller)
        {
            app = controller; Title = "额度时钟 · 悬浮表盘";
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize; ShowInTaskbar = Program.Inspect; ShowActivated = false;
            Wave=new WaveTrail(app.History);
            layout.ColumnDefinitions.Add(new ColumnDefinition());layout.ColumnDefinitions.Add(new ColumnDefinition());
            layout.Children.Add(Face);layout.Children.Add(Wave);Content = layout;layout.Cursor=Cursors.SizeAll;
            app.Tick+=delegate{if(IsVisible&&app.Prefs.ShowTrail)Wave.Advance();};
            Apply();
            bool down = false, moved = false;
            Point startPoint = new Point(), lastPoint = new Point();
            int lastClick = 0;
            PreviewMouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                down = true; moved = false; startPoint = e.GetPosition(this); CaptureMouse(); e.Handled = true;
            };
            PreviewMouseMove += delegate(object sender, MouseEventArgs e)
            {
                if (!down || e.LeftButton != MouseButtonState.Pressed) return;
                Point point = e.GetPosition(this);
                if (Math.Abs(point.X - startPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                    Math.Abs(point.Y - startPoint.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                moved = true; down = false; ReleaseMouseCapture();
                try { DragMove(); app.Prefs.OrbLeft = Left; app.Prefs.OrbTop = Top; app.Save(); } catch (InvalidOperationException) { }
            };
            PreviewMouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e)
            {
                ReleaseMouseCapture(); if (!down || moved) return; down = false;
                Point screen = PointToScreen(e.GetPosition(this)); int now = Environment.TickCount;
                int elapsed = unchecked(now - lastClick);
                if (lastClick != 0 && elapsed >= 0 && elapsed <= System.Windows.Forms.SystemInformation.DoubleClickTime &&
                    Math.Abs(screen.X - lastPoint.X) <= System.Windows.Forms.SystemInformation.DoubleClickSize.Width &&
                    Math.Abs(screen.Y - lastPoint.Y) <= System.Windows.Forms.SystemInformation.DoubleClickSize.Height)
                { lastClick = 0; app.OrbDoubleClickCount++; app.ShowDetails(); }
                else { lastClick = now; lastPoint = screen; }
                e.Handled = true;
            };
            MouseRightButtonUp += delegate { ContextMenu = app.Menu(); ContextMenu.IsOpen = true; };
            SourceInitialized += delegate { if (!Program.Inspect) Native.MarkTool(new WindowInteropHelper(this).Handle); };
            app.Changed += delegate { Render(); };
            Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e)
            { if (!app.Exiting) { e.Cancel = true; app.Prefs.ShowOrb = false; Hide(); app.Save(); app.WriteStatus(); } };
        }
        public void Apply()
        {
            double size=app.Prefs.OrbSize,waveWidth=size*1.5+36;
            double x=app.Prefs.OrbLeft,y=app.Prefs.OrbTop;
            if(placed && lastLeft!=app.Prefs.TrailLeft && lastTrail && app.Prefs.ShowTrail)x=Left+(lastLeft?lastWaveWidth:0)-(app.Prefs.TrailLeft?waveWidth:0);
            Face.Width=Face.Height=size;Wave.Width=waveWidth;Wave.Height=size;
            Wave.Visibility=app.Prefs.ShowTrail?Visibility.Visible:Visibility.Collapsed;
            Wave.Light=app.Light;Wave.Left=app.Prefs.TrailLeft;Wave.Demo=app.Demo;Wave.HandMask=app.TrailMask;
            layout.ColumnDefinitions[0].Width=new GridLength(app.Prefs.ShowTrail&&app.Prefs.TrailLeft?waveWidth:size);
            layout.ColumnDefinitions[1].Width=new GridLength(app.Prefs.ShowTrail?(app.Prefs.TrailLeft?size:waveWidth):0);
            Grid.SetColumn(Face,app.Prefs.ShowTrail&&app.Prefs.TrailLeft?1:0);Grid.SetColumn(Wave,app.Prefs.TrailLeft?0:1);
            Width=size+(app.Prefs.ShowTrail?waveWidth:0);Height=size; Opacity = app.Prefs.OrbOpacity; Topmost = app.Prefs.Topmost;
            var work = SystemParameters.WorkArea;
            
            if (Double.IsNaN(x) || Double.IsNaN(y) || x < SystemParameters.VirtualScreenLeft - 10 ||
                y < SystemParameters.VirtualScreenTop - 10 || x + 60 > SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth ||
                y + 60 > SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight)
            { x = work.Right - Width - 245; y = work.Bottom - Height - 24; }
            if(x>=work.Left && x<work.Right && y>=work.Top && y<work.Bottom)x=Math.Max(work.Left+8,Math.Min(work.Right-Width-8,x));
            Left=x;Top=y;app.Prefs.OrbLeft=x;app.Prefs.OrbTop=y;
            placed=true;lastLeft=app.Prefs.TrailLeft;lastTrail=app.Prefs.ShowTrail;lastWaveWidth=waveWidth;Render();
        }
        void Render()
        {
            Face.Light = app.Light; Face.Demo = app.Demo;
            Face.Estimated=app.Estimated; Face.Readout=app.Percent;
            Face.Update(app.PositionRemaining, app.Known, app.Fine, app.Expired, !app.WaveDemoPlaying);
            Wave.Light=app.Light;Wave.Left=app.Prefs.TrailLeft;Wave.Demo=app.Demo;Wave.HandMask=app.TrailMask;Wave.Advance();
            Face.ToolTip = app.Source + " · 剩余 " + app.Percent + "\n" + app.ResetText + "\n" + app.PrecisionText + "\n双击查看详情 · 拖动调整位置 · 右键打开菜单";
        }
    }
}




