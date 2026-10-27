using System;
using System.IO;
using System.Windows;
namespace QuotaClock
{
    public static class ShellChecks
    {
        public static int Run()
        {
            int count=0;string report;
            Action<bool,string> check=(condition,label)=>{if(!condition)throw new Exception(label);count++;};
            var application=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
            ClockController c=null;
            try
            {
                c=new ClockController(true);c.Start();
                check(c.Orb.IsVisible,"orb shown independently");
                check(!c.Details.IsVisible&&!c.Settings.IsVisible,"no dashboard or settings on startup");
                c.ShowDetails();check(c.Details.IsVisible,"details opens");
                var closedDetails=c.Details;c.Details.Close();check(!closedDetails.IsVisible&&!c.HasDetails,"details close releases window");
                check(c.Orb.IsVisible&&!c.Exiting,"closing details keeps app running");
                c.ShowDetails();check(c.Details.IsVisible&&c.DetailOpenCount==2,"hidden details can reopen");
                c.ShowSettings();check(c.Settings.IsVisible,"settings opens");
                var closedSettings=c.Settings;c.Settings.Close();check(!closedSettings.IsVisible&&!c.Exiting,"closing settings keeps app running");
                c.Prefs.ShowOrb=false;c.ApplyPreferences();check(!c.Orb.IsVisible,"orb switch hides");
                c.Prefs.ShowOrb=true;c.ApplyPreferences();check(c.Orb.IsVisible,"orb switch restores");
                c.Prefs.ShowTrail=true;c.Prefs.TrailLeft=false;c.ApplyPreferences();
                check(c.Orb.Width>c.Prefs.OrbSize*2,"trail expands widget width");
                check(System.Windows.Controls.Grid.GetColumn(c.Orb.Face)==0,"right-scrolling trail leaves clock on left");
                c.Prefs.TrailLeft=true;c.ApplyPreferences();
                check(System.Windows.Controls.Grid.GetColumn(c.Orb.Face)==1,"mirrored trail places clock on right");
                c.Prefs.ShowTrail=false;c.ApplyPreferences();
                check(c.Orb.Width==c.Prefs.OrbSize&&c.Orb.Wave.Visibility==Visibility.Collapsed,"trail can release screen area");
                c.ShowRunHistory();check(c.HasRunHistory&&c.RunWindow.IsVisible,"run history opens lazily");
                var run=c.RunWindow;c.RunWindow.Close();check(!c.HasRunHistory&&!run.IsVisible,"closing run history releases it");
                c.ShowRunHistory();check(c.RunWindow.IsVisible,"run history can reopen");
                c.RunWindow.SelectTestRange(1,100);var lastQuarter=c.RunWindow.Chart.CurrentView;
                check(Math.Abs(lastQuarter.End-lastQuarter.Start-900)<.01,"history zoom selects fifteen minutes");
                c.RunWindow.SelectTestRange(1,0);check(c.RunWindow.Chart.CurrentView.Start==0,"history pan reaches beginning of run");
                c.RunWindow.SelectTestRange(0,100);check(c.RunWindow.Chart.CurrentView.End>=7200,"whole-run mode restores full demo span");
                check(c.Recording.FilePath==null,"preview does not create real run CSV");
                c.SetTrailHand(0,false);check((c.TrailMask&1)==0,"hour projection toggles independently");
                c.SetTrailHand(0,true);check((c.TrailMask&1)!=0,"hour projection restores");
                c.DemoRemaining=73.4m;c.Notify();check(c.Percent=="73.4%","demo precision");
                c.DemoRemaining=0;c.Notify();check(c.Percent=="0.0%","zero demo");
                c.DemoRemaining=100;c.Notify();check(c.Percent=="100.0%","full demo");
                c.Exit();check(c.Exiting,"explicit exit");
                report="PASS: "+count+" shell lifecycle checks. Startup, independent orb, close-to-tray behavior, reopening, display switches, demo and explicit exit.";
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"shell-test-results.txt"),report);return 0;
            }
            catch(Exception e)
            {
                report="FAIL after "+count+": "+e.Message;File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"shell-test-results.txt"),report);
                if(c!=null&&!c.Exiting)c.Exit();return 1;
            }
        }
    }
}

