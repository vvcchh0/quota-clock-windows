using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace QuotaClock
{
    public static class Program
    {
        public static bool Inspect;
        [STAThread] public static int Main(string[] args)
        {
            Inspect=args.Contains("--inspect");
            if(args.Contains("--exit")){
                try{using(var quit=EventWaitHandle.OpenExisting("Local.QuotaClock.Exit.v3"))quit.Set();return 0;}catch{return 1;}
            }
            if(args.Contains("--test"))return SelfTests.Run();
            if(args.Contains("--test-client"))return ClientChecks.Run();
            if(args.Contains("--test-history"))return RunHistoryChecks.Run();
            if(args.Contains("--test-wave"))return WaveChecks.Run();
            if(args.Contains("--probe-reuse"))return WaveChecks.Probe();
            if(args.Contains("--test-shell"))return ShellChecks.Run();
            if(args.Contains("--probe"))return Probe.Run();
            if(args.Contains("--test-calibration"))return CalibrationTests.Run();
            if(args.Contains("--probe-local"))return LocalProbe.Run();
            if(args.Contains("--render-previews"))return Preview.Run();
            bool created;
            using(var mutex=new Mutex(true,"Local.QuotaClock.v1",out created))
            {
                if(!created)
                {
                    try{using(var signal=EventWaitHandle.OpenExisting(args.Contains("--history")?"Local.QuotaClock.ShowHistory.v1":"Local.QuotaClock.ShowDetails.v2"))signal.Set();}catch{}
                    return 0;
                }
                if(!args.Contains("--hardware-render"))RenderOptions.ProcessRenderMode=System.Windows.Interop.RenderMode.SoftwareOnly;
                var application=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
                ClockController controller=null;
                using(var signal=new EventWaitHandle(false,EventResetMode.AutoReset,"Local.QuotaClock.ShowDetails.v2"))
                using(var historySignal=new EventWaitHandle(false,EventResetMode.AutoReset,"Local.QuotaClock.ShowHistory.v1"))
                using(var quitSignal=new EventWaitHandle(false,EventResetMode.AutoReset,"Local.QuotaClock.Exit.v3"))
                {
                    RegisteredWaitHandle wait=null,quitWait=null,historyWait=null;
                    application.DispatcherUnhandledException+=delegate(object sender,DispatcherUnhandledExceptionEventArgs e)
                    {
                        try{File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"app-errors.log"),DateTimeOffset.Now.ToString("o")+" "+e.Exception.GetType().Name+": "+e.Exception.Message+Environment.NewLine+e.Exception.StackTrace+Environment.NewLine);}catch{}
                        e.Handled=true;if(controller!=null)controller.Exit();else application.Shutdown(1);
                    };
                    application.Startup+=delegate
                    {
                        controller=new ClockController(false);controller.Start();
                        wait=ThreadPool.RegisterWaitForSingleObject(signal,delegate{application.Dispatcher.BeginInvoke(new Action(controller.ShowDetails));},null,Timeout.Infinite,false);
                        quitWait=ThreadPool.RegisterWaitForSingleObject(quitSignal,delegate{application.Dispatcher.BeginInvoke(new Action(controller.Exit));},null,Timeout.Infinite,false);
                        historyWait=ThreadPool.RegisterWaitForSingleObject(historySignal,delegate{application.Dispatcher.BeginInvoke(new Action(controller.ShowRunHistory));},null,Timeout.Infinite,false);
                        if(args.Contains("--history"))controller.ShowRunHistory();
                        if(args.Contains("--details"))controller.ShowDetails();
                        if(args.Contains("--settings"))controller.ShowSettings();
                        if(args.Contains("--demo"))controller.SetDemo(true);
                    };
                    application.Exit+=delegate{if(wait!=null)wait.Unregister(null);if(quitWait!=null)quitWait.Unregister(null);if(historyWait!=null)historyWait.Unregister(null);if(controller!=null)controller.Dispose();};
                    application.Run();
                }
            }
            return 0;
        }
    }
    public static class Preview
    {
        static void Capture(FrameworkElement element,string name)
        {
            element.UpdateLayout();var dpi=VisualTreeHelper.GetDpi(element);
            var image=new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth*dpi.DpiScaleX),(int)Math.Ceiling(element.ActualHeight*dpi.DpiScaleY),96*dpi.DpiScaleX,96*dpi.DpiScaleY,PixelFormats.Pbgra32);
            image.Render(element);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));
            using(var file=File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,name)))png.Save(file);
        }
        public static int Run()
        {
            RenderOptions.ProcessRenderMode=System.Windows.Interop.RenderMode.SoftwareOnly;
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
            var controller=new ClockController(true);controller.Start();controller.SetWaveDemo(true);
            controller.ShowDetails();controller.ShowSettings();controller.ShowRunHistory();
            var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(1200)};
            timer.Tick+=delegate
            {
                timer.Stop();
                Capture(controller.Orb,"preview-orb.png");
                controller.Prefs.Theme="dark";controller.ApplyPreferences();Capture(controller.Orb,"preview-wave-dark.png");
                controller.Prefs.TrailLeft=true;controller.ApplyPreferences();Capture(controller.Orb,"preview-wave-mirror.png");
                controller.Prefs.TrailLeft=false;controller.ApplyPreferences();
                Capture(controller.Details,"preview-details.png");
                Capture(controller.Settings,"preview-settings.png");Capture(controller.RunWindow,"preview-run-history.png");
                controller.Exit();
            };
            timer.Start();app.Run();return 0;
        }
    }
}



