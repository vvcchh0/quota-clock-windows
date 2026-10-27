using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace QuotaClock
{
    public static class Native
    {
        [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; public int Width { get { return Right-Left; } } public int Height { get { return Bottom-Top; } } }
        public delegate bool EnumProc(IntPtr window, IntPtr parameter);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string name);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string name);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumProc callback, IntPtr parameter);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr window, StringBuilder buffer, int length);
        [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr window);
        [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hwnd,int index);
        [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr hwnd,int index,IntPtr value);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr icon);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        public static void MarkTool(IntPtr hwnd)
        {
            if(hwnd==IntPtr.Zero)return;
            long style=GetWindowLongPtr(hwnd,-20).ToInt64();
            SetWindowLongPtr(hwnd,-20,new IntPtr(style|0x80L));
        }
        public static void ThemeWindow(Window window,bool light)
        {
            try { IntPtr hwnd=new WindowInteropHelper(window).Handle; if(hwnd==IntPtr.Zero)return; int dark=light?0:1; DwmSetWindowAttribute(hwnd,20,ref dark,4); int round=2; DwmSetWindowAttribute(hwnd,33,ref round,4); } catch {}
        }
        public static string Class(IntPtr hwnd) { var b=new StringBuilder(256); GetClassName(hwnd,b,b.Capacity); return b.ToString(); }
        public static bool Fullscreen(IntPtr taskbar)
        {
            IntPtr fg=GetForegroundWindow(); if(fg==IntPtr.Zero||fg==taskbar)return false;
            string cls=Class(fg); if(cls=="Progman"||cls=="WorkerW"||cls=="Shell_TrayWnd")return false;
            Rect r; if(!GetWindowRect(fg,out r))return false;
            var bounds=System.Windows.Forms.Screen.FromHandle(fg).Bounds;
            return r.Left<=bounds.Left&&r.Top<=bounds.Top&&r.Right>=bounds.Right&&r.Bottom>=bounds.Bottom;
        }
    }

    public static class TaskbarPlacement
    {
        public static int Left(int barLeft,int edge,int width,int offset)
        { return Math.Max(barLeft+8,edge-width-Math.Max(0,offset)); }
    }

    // An owned child HWND inside Explorer's taskbar. No injection, shell patching, or tasklist resizing.
    public sealed class NativeTaskbar : IDisposable
    {
        readonly ClockController app;
        readonly DispatcherTimer timer=new DispatcherTimer();
        HwndSource source;
        IntPtr parent;
        Border root;
        TextBlock value,label;
        bool disposed;
        string renderKey;
        public string Presence="starting", BoundsText;
        public bool VerifiedChild { get { return source!=null && Native.IsWindow(source.Handle) && Native.GetParent(source.Handle)==parent && parent==Native.FindWindow("Shell_TrayWnd",null); } }
        public NativeTaskbar(ClockController controller)
        {
            app=controller;
            timer.Interval=TimeSpan.FromSeconds(3); timer.Tick+=delegate { Apply(); };
            timer.Start();
        }
        public void Apply()
        {
            if(disposed)return;
            if(!app.Prefs.ShowTaskbar) { Teardown(); Presence="disabled"; app.WriteStatus();return; }
            IntPtr bar=Native.FindWindow("Shell_TrayWnd",null);
            Native.Rect b,t;
            if(bar==IntPtr.Zero||!Native.GetWindowRect(bar,out b)||b.Width<=b.Height)
            {Teardown();Presence="unavailable";app.WriteStatus();return;}
            uint dpi=96;try{dpi=Native.GetDpiForWindow(bar);}catch{} if(dpi==0)dpi=96;
            double scale=dpi/96.0; int gap=(int)Math.Round(6*scale);
            IntPtr tray=Native.FindWindowEx(bar,IntPtr.Zero,"TrayNotifyWnd",null);
            int edge=tray!=IntPtr.Zero&&Native.GetWindowRect(tray,out t)?t.Left-gap:b.Right-(int)(220*scale);
            Native.EnumChildWindows(bar,delegate(IntPtr child,IntPtr unused)
            {
                if(Native.Class(child)=="AgentSpeed.TaskbarWidget"&&Native.IsWindowVisible(child))
                {Native.Rect other;if(Native.GetWindowRect(child,out other)&&other.Width>0)edge=Math.Min(edge,other.Left-gap);}
                return true;
            },IntPtr.Zero);
            int width=(int)Math.Round(112*scale),height=Math.Max(20,b.Height-(int)(8*scale));
            int x=TaskbarPlacement.Left(b.Left,edge,width,(int)(app.Prefs.TaskbarOffset*scale));
            int y=b.Top+(b.Height-height)/2;
            if(source!=null&&(!Native.IsWindow(source.Handle)||parent!=bar))Teardown();
            if(source==null)
            {
                try
                {
                    var p=new HwndSourceParameters("额度时钟 · 任务栏");
                    p.ParentWindow=bar;p.WindowStyle=unchecked((int)0x54000000);
                    p.ExtendedWindowStyle=0x08000084;p.PositionX=x-b.Left;p.PositionY=y-b.Top;p.Width=width;p.Height=height;
                    source=new HwndSource(p);parent=bar;
                    source.AddHook(delegate(IntPtr hwnd,int msg,IntPtr wp,IntPtr lp,ref bool handled)
                    {if(msg==0x21){handled=true;return new IntPtr(3);}if(msg==0x7B){handled=true;return IntPtr.Zero;}return IntPtr.Zero;});
                    var row=new Grid();
                    row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(29)});
                    row.ColumnDefinitions.Add(new ColumnDefinition());
                    var glyph=new TextBlock{Text="◷",FontSize=25,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center,Foreground=Paint.Brush("#34CBB9")};
                    row.Children.Add(glyph);
                    var column=new StackPanel{VerticalAlignment=VerticalAlignment.Center};
                    value=new TextBlock{FontSize=14,FontWeight=FontWeights.SemiBold};
                    label=new TextBlock{FontSize=9,Opacity=.7};
                    column.Children.Add(value);column.Children.Add(label);Grid.SetColumn(column,1);row.Children.Add(column);
                    root=new Border{CornerRadius=new CornerRadius(5),Padding=new Thickness(5,0,3,0),Child=row,Cursor=Cursors.Hand};
                    root.MouseLeftButtonUp+=delegate {app.TaskbarInvokeCount++;app.ShowDetails();};
                    root.MouseRightButtonUp+=delegate {var menu=app.Menu();menu.PlacementTarget=root;menu.IsOpen=true;};
                    source.RootVisual=root;Presence="embedded";
                }
                catch {Teardown();Presence="unavailable";app.WriteStatus();return;}
            }
            root.Width=width/scale;root.Height=height/scale;
            string bounds=x+","+y+","+width+","+height;
            if(BoundsText!=bounds)Native.SetWindowPos(source.Handle,IntPtr.Zero,x-b.Left,y-b.Top,width,height,0x0010|0x0040);
            bool fullscreen=Native.Fullscreen(bar);
            if(Presence!=(fullscreen?"hidden-fullscreen":"embedded"))Native.ShowWindow(source.Handle,fullscreen?0:4);
            Presence=fullscreen?"hidden-fullscreen":"embedded";
            BoundsText=bounds;
            Update(); app.WriteStatus();
        }
        public void Update()
        {
            if(source==null||root==null)return;
            bool light=UI.SystemLight(true);
            string key=light+"|"+app.Percent+"|"+app.Source+"|"+app.ResetText;
            if(key==renderKey)return;renderKey=key;
            var bg=Paint.Brush(light?"#F2F2F2":"#202020");
            root.Background=bg;root.BorderBrush=Paint.Brush(light?"#DDDDDD":"#353535");root.BorderThickness=new Thickness(0);
            value.Foreground=Paint.Brush(light?"#202020":"#F3F6FB");label.Foreground=value.Foreground;
            value.Text=app.Percent;label.Text=app.Demo?"演示额度":"剩余额度";
            root.ToolTip=app.Source+" · 剩余 "+app.Percent+"\n"+app.ResetText+"\n单击查看详情 · 右键打开菜单";
            System.Windows.Automation.AutomationProperties.SetName(root,"额度时钟，剩余 "+app.Percent);
            source.CompositionTarget.BackgroundColor=bg.Color;
        }
        void Teardown()
        {
            if(source!=null){try{source.Dispose();}catch{}source=null;}
            renderKey=null;root=null;value=null;label=null;parent=IntPtr.Zero;BoundsText=null;
        }
        public void Dispose(){if(disposed)return;disposed=true;timer.Stop();Teardown();Presence="disposed";}
    }
}


