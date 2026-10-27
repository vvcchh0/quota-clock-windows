using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using Microsoft.Win32;
using Drawing=System.Drawing;

namespace QuotaClock
{
    public static class UI
    {
        public static bool SystemLight(bool taskbar)
        {
            try { using(var k=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                return k==null||Convert.ToInt32(k.GetValue(taskbar?"SystemUsesLightTheme":"AppsUseLightTheme",1))!=0; } catch{return true;}
        }
        public static TextBlock Text(string text,double size=14,bool bold=false)
        {return new TextBlock{Text=text,FontSize=size,FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal,TextWrapping=TextWrapping.Wrap};}
        public static TextBlock Muted(string text,double size=12){var t=Text(text,size);t.SetResourceReference(TextBlock.ForegroundProperty,"Muted");return t;}
        public static Button Button(string text,Action action)
        {
            var b=new Button{Content=text,Padding=new Thickness(13,7,13,7),Cursor=Cursors.Hand};
            AutomationProperties.SetName(b,text);b.Click+=delegate{action();};return b;
        }
        public static Button Glyph(string symbol,string name,Action action)
        {
            var b=Button("",action);b.Content=new TextBlock{Text=symbol,FontFamily=new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),FontSize=17};
            b.ToolTip=name;AutomationProperties.SetName(b,name);return b;
        }
        public static CheckBox Switch(bool value,string name,Action<bool> change)
        {
            var b=new CheckBox{IsChecked=value,VerticalAlignment=VerticalAlignment.Center};
            b.SetResourceReference(FrameworkElement.StyleProperty,"Switch");AutomationProperties.SetName(b,name);b.ToolTip=name;
            b.Checked+=delegate{change(true);};b.Unchecked+=delegate{change(false);};return b;
        }
        public static Border Card(UIElement child,Thickness? padding=null)
        {
            var b=new Border{Child=child,CornerRadius=new CornerRadius(9),BorderThickness=new Thickness(1),Padding=padding??new Thickness(18),Margin=new Thickness(0,0,0,8)};
            b.SetResourceReference(Border.BackgroundProperty,"Card");b.SetResourceReference(Border.BorderBrushProperty,"Stroke");return b;
        }
        const string Styles=@"<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
<Style TargetType='Button'><Setter Property='HorizontalContentAlignment' Value='Center'/><Setter Property='VerticalContentAlignment' Value='Center'/><Setter Property='Foreground' Value='{DynamicResource Text}'/><Setter Property='Background' Value='{DynamicResource Button}'/><Setter Property='BorderBrush' Value='{DynamicResource Stroke}'/><Setter Property='BorderThickness' Value='1'/><Setter Property='FontSize' Value='13'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'><Border x:Name='plate' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='6' Padding='{TemplateBinding Padding}'><ContentPresenter VerticalAlignment='{TemplateBinding VerticalContentAlignment}' HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='plate' Property='Background' Value='{DynamicResource Hover}'/></Trigger><Trigger Property='IsPressed' Value='True'><Setter TargetName='plate' Property='Opacity' Value='.7'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='plate' Property='BorderBrush' Value='#0078D4'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter TargetName='plate' Property='Opacity' Value='.45'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
<Style x:Key='Switch' TargetType='CheckBox'><Setter Property='Cursor' Value='Hand'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='CheckBox'><Grid Width='44' Height='24'><Border x:Name='track' CornerRadius='12' BorderThickness='1' BorderBrush='{DynamicResource Muted}' Background='{DynamicResource Button}'/><Ellipse x:Name='knob' Width='12' Height='12' Fill='{DynamicResource Muted}' HorizontalAlignment='Left' Margin='6,0,0,0'/></Grid><ControlTemplate.Triggers><Trigger Property='IsChecked' Value='True'><Setter TargetName='track' Property='Background' Value='#0078D4'/><Setter TargetName='track' Property='BorderBrush' Value='#0078D4'/><Setter TargetName='knob' Property='Fill' Value='White'/><Setter TargetName='knob' Property='HorizontalAlignment' Value='Right'/><Setter TargetName='knob' Property='Margin' Value='0,0,6,0'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='track' Property='BorderThickness' Value='2'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
<Style TargetType='ListBoxItem'><Setter Property='Padding' Value='13,12'/><Setter Property='Margin' Value='0,0,0,5'/><Setter Property='HorizontalContentAlignment' Value='Stretch'/><Setter Property='Foreground' Value='{DynamicResource Text}'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ListBoxItem'><Border x:Name='item' Background='Transparent' CornerRadius='6' Padding='{TemplateBinding Padding}'><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property='IsSelected' Value='True'><Setter TargetName='item' Property='Background' Value='{DynamicResource Selected}'/></Trigger><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='item' Property='Background' Value='{DynamicResource Hover}'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
</ResourceDictionary>";
        public static void Theme(FrameworkElement element,bool light)
        {
            if(element.Resources.MergedDictionaries.Count==0)element.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(Styles));
            var r=element.Resources;
            r["Text"]=Paint.Brush(light?"#202020":"#F0F3F8");r["Muted"]=Paint.Brush(light?"#7A7A7A":"#A2ADB9");
            r["Background"]=Paint.Brush(light?"#F3F3F3":"#15191F");r["Card"]=Paint.Brush(light?"#FCFCFC":"#222830");
            r["Button"]=Paint.Brush(light?"#FFFFFF":"#2A323D");r["Stroke"]=Paint.Brush(light?"#E0E0E0":"#343E4B");
            r["Hover"]=Paint.Brush(light?"#E8E8E8":"#343F4C");r["Selected"]=Paint.Brush(light?"#E6E6E6":"#2B3541");
            var window=element as Window;
            if(window!=null){window.Background=(Brush)r["Background"];window.Foreground=(Brush)r["Text"];Native.ThemeWindow(window,light);}
            var menu=element as ContextMenu;
            if(menu!=null){menu.Background=(Brush)r["Card"];menu.Foreground=(Brush)r["Text"];}
        }
        public static Drawing.Icon MakeIcon()
        {
            using(var b=new Drawing.Bitmap(32,32))
            using(var g=Drawing.Graphics.FromImage(b))
            {
                g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;g.Clear(Drawing.Color.Transparent);
                using(var fill=new Drawing.SolidBrush(Drawing.Color.FromArgb(28,36,48)))g.FillEllipse(fill,1,1,30,30);
                using(var pen=new Drawing.Pen(Drawing.Color.FromArgb(45,212,191),2.4f))g.DrawEllipse(pen,3,3,26,26);
                using(var pen=new Drawing.Pen(Drawing.Color.White,2.5f)){g.DrawLine(pen,16,16,11,11);g.DrawLine(pen,16,16,23,12);}
                using(var pen=new Drawing.Pen(Drawing.Color.FromArgb(251,113,133),1))g.DrawLine(pen,16,16,17,24);
                IntPtr h=b.GetHicon();try{return (Drawing.Icon)Drawing.Icon.FromHandle(h).Clone();}finally{Native.DestroyIcon(h);}
            }
        }
    }

    public sealed class DetailsWindow:Window
    {
        readonly ClockController app;
        readonly ClockFace face=new ClockFace();
        readonly TextBlock source=UI.Muted(""),fresh=UI.Muted(""),reset=UI.Text("",13),precision=UI.Muted("");
        readonly StackPanel rows=new StackPanel();
        readonly CheckBox showOrb;
        readonly EventHandler changedHandler, tickHandler;
        public DetailsWindow(ClockController controller)
        {
            app=controller;Title="额度时钟 · 额度详情";Width=390;Height=640;ResizeMode=ResizeMode.NoResize;
            ShowInTaskbar=Program.Inspect;WindowStyle=WindowStyle.None;
            FontFamily=new FontFamily("Segoe UI, Microsoft YaHei UI");FontSize=13;
            var body=new Grid{Margin=new Thickness(20,16,20,16)};
            foreach(var height in new[]{GridLength.Auto,GridLength.Auto,new GridLength(240),GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto,GridLength.Auto})body.RowDefinitions.Add(new RowDefinition{Height=height});
            var header=new DockPanel{Margin=new Thickness(0,0,0,8)};
            var close=UI.Glyph("\uE711","收起额度详情",Close);DockPanel.SetDock(close,Dock.Right);header.Children.Add(close);
            var history=UI.Glyph("\uE9D9","查看三针运行轨迹",app.ShowRunHistory);history.Margin=new Thickness(0,0,8,0);DockPanel.SetDock(history,Dock.Right);header.Children.Add(history);
            header.Children.Add(UI.Text("额度概览",22,true));body.Children.Add(header);
            source.Margin=new Thickness(0,0,0,4);Grid.SetRow(source,1);body.Children.Add(source);
            face.ShowReadout=true;Grid.SetRow(face,2);body.Children.Add(face);
            var info=new StackPanel{Margin=new Thickness(0,0,0,12)};info.Children.Add(reset);precision.Margin=new Thickness(0,4,0,0);info.Children.Add(precision);Grid.SetRow(info,3);body.Children.Add(info);
            var scroll=new ScrollViewer{Content=rows,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Grid.SetRow(scroll,4);body.Children.Add(scroll);
            fresh.TextWrapping=TextWrapping.Wrap;fresh.Margin=new Thickness(0,10,0,14);Grid.SetRow(fresh,5);body.Children.Add(fresh);
            var bottom=new DockPanel();
            var right=new StackPanel{Orientation=Orientation.Horizontal};
            right.Children.Add(UI.Glyph("\uE72C","刷新额度",delegate{app.Demo=false;var task=app.Refresh();}));
            var settings=UI.Glyph("\uE713","打开设置",app.ShowSettings);settings.Margin=new Thickness(7,0,7,0);right.Children.Add(settings);
            right.Children.Add(UI.Glyph("\uE7E8","退出额度时钟",app.Exit));DockPanel.SetDock(right,Dock.Right);bottom.Children.Add(right);
            var left=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
            showOrb=UI.Switch(app.Prefs.ShowOrb,"显示悬浮表盘",delegate(bool v){app.Prefs.ShowOrb=v;app.ApplyPreferences();});left.Children.Add(showOrb);
            var caption=UI.Text("悬浮表盘",12);caption.Margin=new Thickness(8,0,0,0);caption.VerticalAlignment=VerticalAlignment.Center;left.Children.Add(caption);bottom.Children.Add(left);
            Grid.SetRow(bottom,6);body.Children.Add(bottom);
            var border=UI.Card(body,new Thickness(0));border.Margin=new Thickness(0);border.CornerRadius=new CornerRadius(12);Content=border;
            SourceInitialized+=delegate{if(!Program.Inspect)Native.MarkTool(new WindowInteropHelper(this).Handle);Native.ThemeWindow(this,app.Light);};
            Closed+=delegate { app.Changed-=changedHandler;app.Tick-=tickHandler;app.ForgetDetails(this); };
            StateChanged+=delegate{if(WindowState==WindowState.Minimized)Close();};
            KeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Escape)Close();};
            changedHandler=delegate{if(IsVisible)Render();};app.Changed+=changedHandler;
            IsVisibleChanged+=delegate{if(IsVisible)Render();};
            tickHandler=delegate{if(IsVisible){reset.Text=app.ResetText;fresh.Text=app.FreshText;}};app.Tick+=tickHandler;
            ApplyTheme();Render();
        }
        public void ApplyTheme(){UI.Theme(this,app.Light);face.Light=app.Light;face.InvalidateVisual();}
        public void PlaceNearOrb()
        {
            var work=SystemParameters.WorkArea;double x=app.Orb.Left+app.Orb.Width+12;
            if(x+Width>work.Right)x=app.Orb.Left-Width-12;
            Left=Math.Max(work.Left+8,Math.Min(work.Right-Width-8,x));
            Top=Math.Max(work.Top+8,Math.Min(work.Bottom-Height-8,app.Orb.Top));
        }
        void Render()
        {
            source.Text=app.Source;reset.Text=app.ResetText;fresh.Text=app.FreshText;
            face.Light=app.Light;face.Demo=app.Demo;face.Estimated=app.Estimated;face.Readout=app.Percent;face.Update(app.PositionRemaining,app.Known,app.Fine,app.Expired,!app.WaveDemoPlaying);
            precision.Text=app.PrecisionText;
            if(showOrb.IsChecked!=app.Prefs.ShowOrb)showOrb.IsChecked=app.Prefs.ShowOrb;
            rows.Children.Clear();
            if(app.Demo){rows.Children.Add(UI.Card(UI.Text("演示模式\n可在设置 → 数据来源中调整示例额度。",13)));}
            else if(app.Snapshot!=null)
            foreach(var w in app.Snapshot.Windows)
            {
                var panel=new StackPanel();var line=new DockPanel();
                var number=UI.Text(QuotaMath.Expired(w,DateTimeOffset.Now)?"—":QuotaMath.Percent(w.Remaining,w.Fractional),20,true);DockPanel.SetDock(number,Dock.Right);line.Children.Add(number);
                var name=UI.Text(w.Label,13,true);name.Margin=new Thickness(0,3,8,0);line.Children.Add(name);panel.Children.Add(line);
                var progress=new ProgressBar{Minimum=0,Maximum=100,Value=QuotaMath.Expired(w,DateTimeOffset.Now)?0:(double)w.Remaining,Height=4,Margin=new Thickness(0,10,0,8),Foreground=Paint.Brush("#23BFA9"),Background=Paint.Brush(app.Light?"#E8ECF1":"#35414F"),BorderThickness=new Thickness(0)};
                panel.Children.Add(progress);panel.Children.Add(UI.Muted(QuotaMath.ResetText(w,DateTimeOffset.Now),11));
                QuotaWindow choice=w;var button=UI.Button("",delegate{app.Select(choice);});button.Content=panel;button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.Margin=new Thickness(0,0,0,8);button.Padding=new Thickness(12);
                if(app.Selected!=null&&w.Id==app.Selected.Id)button.BorderBrush=Paint.Brush("#0078D4");
                rows.Children.Add(button);
            }
            else rows.Children.Add(UI.Card(UI.Muted("暂未取得额度，可点击下方刷新。")));
        }
    }

    public sealed class SettingsWindow:Window
    {
        readonly ClockController app;
        readonly ListBox navigation=new ListBox();
        readonly TextBlock title=UI.Text("通用",30,true);
        readonly StackPanel page=new StackPanel();
        readonly string[] names={"通用","任务栏","悬浮窗","数据来源","关于"};
        readonly string[] glyphs={"\uE713","\uE7F4","\uEA3A","\uE8B7","\uE946"};
        readonly ScrollViewer scroll;
        Snapshot lastSnapshot;
        readonly EventHandler changedHandler;
        TextBlock localState;
        bool building;
        public SettingsWindow(ClockController controller)
        {
            app=controller;Title="额度时钟 · 设置";Width=1010;Height=740;MinWidth=830;MinHeight=620;
            FontFamily=new FontFamily("Segoe UI, Microsoft YaHei UI");FontSize=14;WindowStartupLocation=WindowStartupLocation.CenterScreen;
            var layout=new Grid{Margin=new Thickness(20,18,24,20)};
            layout.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(230)});layout.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(28)});layout.ColumnDefinitions.Add(new ColumnDefinition());
            var sidebar=new DockPanel();
            var brand=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(10,8,0,25)};
            var logo=UI.Text("◷",30,true);logo.Foreground=Paint.Brush("#12B7A6");brand.Children.Add(logo);
            var name=UI.Text("额度时钟",20,true);name.Margin=new Thickness(12,6,0,0);brand.Children.Add(name);DockPanel.SetDock(brand,Dock.Top);sidebar.Children.Add(brand);
            var hint=UI.Muted("关闭设置后，仍在托盘中运行",11);hint.Margin=new Thickness(10,14,0,0);DockPanel.SetDock(hint,Dock.Bottom);sidebar.Children.Add(hint);
            navigation.Background=Brushes.Transparent;navigation.BorderThickness=new Thickness(0);
            for(int i=0;i<names.Length;i++)
            {
                var row=new StackPanel{Orientation=Orientation.Horizontal};
                row.Children.Add(new TextBlock{Text=glyphs[i],FontFamily=new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),FontSize=20,Width=33,VerticalAlignment=VerticalAlignment.Center});
                row.Children.Add(UI.Text(names[i],16));navigation.Items.Add(new ListBoxItem{Content=row});
            }
            navigation.SelectionChanged+=delegate{BuildPage();};sidebar.Children.Add(navigation);layout.Children.Add(sidebar);
            var main=new DockPanel();title.Margin=new Thickness(0,4,0,22);DockPanel.SetDock(title,Dock.Top);main.Children.Add(title);
            scroll=new ScrollViewer{Content=page,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new Thickness(0,0,12,0)};main.Children.Add(scroll);
            Grid.SetColumn(main,2);layout.Children.Add(main);Content=layout;
            Closed+=delegate { app.Changed-=changedHandler;app.ForgetSettings(this); };
            StateChanged+=delegate{if(WindowState==WindowState.Minimized)Close();};
            SourceInitialized+=delegate{Native.ThemeWindow(this,app.Light);};
            changedHandler=delegate{if(IsVisible&&localState!=null)localState.Text=app.PrecisionText;if(IsVisible&&lastSnapshot!=app.Snapshot&&navigation.SelectedIndex==3&&!building){lastSnapshot=app.Snapshot;BuildPage();}};app.Changed+=changedHandler;
            ApplyTheme();navigation.SelectedIndex=0;
        }
        public void ApplyTheme(){UI.Theme(this,app.Light);}
        Border Row(string glyph,string label,string description,UIElement control)
        {
            var grid=new Grid();grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(36)});grid.ColumnDefinitions.Add(new ColumnDefinition());grid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            grid.Children.Add(new TextBlock{Text=glyph,FontFamily=new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),FontSize=24,VerticalAlignment=VerticalAlignment.Center});
            var copy=new StackPanel{Margin=new Thickness(8,0,18,0),VerticalAlignment=VerticalAlignment.Center};copy.Children.Add(UI.Text(label,15));var hint=UI.Muted(description,12);hint.Margin=new Thickness(0,4,0,0);copy.Children.Add(hint);Grid.SetColumn(copy,1);grid.Children.Add(copy);
            control.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);Grid.SetColumn(control,2);grid.Children.Add(control);return UI.Card(grid);
        }
        void Toggle(string glyph,string label,string description,bool value,Action<bool> change)
        {page.Children.Add(Row(glyph,label,description,UI.Switch(value,label,delegate(bool v){change(v);app.ApplyPreferences();})));}
        void Range(string label,string hint,double min,double max,double value,Func<double,string> format,Action<double> changed)
        {
            var panel=new StackPanel();panel.Children.Add(UI.Text(label,15));var desc=UI.Muted(hint);desc.Margin=new Thickness(0,5,0,14);panel.Children.Add(desc);
            var line=new DockPanel();var display=UI.Muted(format(value));display.MinWidth=64;display.TextAlignment=TextAlignment.Right;DockPanel.SetDock(display,Dock.Right);line.Children.Add(display);
            var slider=new Slider{Minimum=min,Maximum=max,Value=value,TickFrequency=label=="示例剩余额度"?.1:1,IsSnapToTickEnabled=true,Margin=new Thickness(0,0,14,0)};
            AutomationProperties.SetName(slider,label);slider.ValueChanged+=delegate{display.Text=format(slider.Value);changed(slider.Value);app.ApplyPreferences();};line.Children.Add(slider);panel.Children.Add(line);page.Children.Add(UI.Card(panel));
        }
        ComboBox Choice(string[] items,int index,Action<int> selected)
        {
            var combo=new ComboBox{Width=170,MinHeight=34,ItemsSource=items,SelectedIndex=index,FontSize=13,Padding=new Thickness(8,5,8,5)};
            combo.SelectionChanged+=delegate{if(!building)selected(combo.SelectedIndex);};return combo;
        }
        void BuildPage()
        {
            if(navigation.SelectedIndex<0)return;building=true;localState=null;page.Children.Clear();title.Text=names[navigation.SelectedIndex];scroll.ScrollToTop();
            int index=navigation.SelectedIndex;
            if(index==0)
            {
                var theme=Choice(new[]{"跟随系统","浅色","深色"},app.Prefs.Theme=="light"?1:app.Prefs.Theme=="dark"?2:0,delegate(int n){app.Prefs.Theme=n==1?"light":n==2?"dark":"system";app.ApplyPreferences();});
                page.Children.Add(Row("\uE790","外观","更改立即生效。",theme));
                int[] periods={5,10,15,20,30,45,60,300,600,1800};var interval=Choice(periods.Select(n=>n<60?n+" 秒":(n/60)+" 分钟").ToArray(),Math.Max(0,Array.IndexOf(periods,app.Prefs.RefreshSeconds)),delegate(int n){if(n>=0){app.Prefs.RefreshSeconds=periods[n];app.ApplyPreferences();}});
                page.Children.Add(Row("\uE787","自动更新间隔","默认 15 秒；高频刷新复用连接，失败时退避重试。",interval));
                Toggle("\uE945","轻量后台连接","减少反复启动带来的 CPU 开销；关闭后每次独立读取。",app.Prefs.ReuseConnection,delegate(bool v){app.Prefs.ReuseConnection=v;});
                page.Children.Add(Row("\uE73E","托盘常驻","关闭详情或设置后继续运行。通过托盘菜单退出。",UI.Text("已启用",13,true)));
                page.Children.Add(UI.Card(UI.Muted("日常查看悬浮表盘；双击查看额度详情。任务栏组件单击也可打开详情。")));
            }
            else if(index==1)
            {
                Toggle("\uE7F4","在任务栏显示额度","在系统托盘左侧显示紧凑额度组件。",app.Prefs.ShowTaskbar,delegate(bool v){app.Prefs.ShowTaskbar=v;});
                Range("向左偏移","自动避开 Agent Speed，可继续向左移动以避让其他组件。",0,600,app.Prefs.TaskbarOffset,v=>((int)v)+" px",v=>app.Prefs.TaskbarOffset=(int)v);
                page.Children.Add(UI.Card(UI.Muted("点击组件查看详情，右键打开菜单。Explorer 重新加载后会尝试恢复组件。任务栏自动隐藏时组件随之隐藏；全屏时暂时收起。")));
            }
            else if(index==2)
            {
                Toggle("\uEA3A","显示悬浮表盘","在桌面上显示独立的圆形额度时钟。",app.Prefs.ShowOrb,delegate(bool v){app.Prefs.ShowOrb=v;});
                Toggle("\uE718","始终置顶","让表盘保持在其他窗口上方。",app.Prefs.Topmost,delegate(bool v){app.Prefs.Topmost=v;});
                Toggle("\uE9D9","三针轨迹","时、分、秒纵向投影；桌面显示最近十五分钟。",app.Prefs.ShowTrail,delegate(bool v){app.Prefs.ShowTrail=v;});
                Toggle("\uE823","时针投影","低频的额度变化轨迹。",app.Prefs.HourTrail,delegate(bool v){app.Prefs.HourTrail=v;});
                Toggle("\uE823","分针投影","每消耗 10% 转一圈。",app.Prefs.MinuteTrail,delegate(bool v){app.Prefs.MinuteTrail=v;});
                Toggle("\uE823","秒针投影","每消耗 1% 转一圈，估算仍以虚线标记。",app.Prefs.FineTrail,delegate(bool v){app.Prefs.FineTrail=v;});
                Toggle("\uE74E","记录整个运行过程","每十五秒保存本地 CSV；关闭程序后保留，不上传。",app.Prefs.RecordRun,delegate(bool v){app.Prefs.RecordRun=v;});
                page.Children.Add(Row("\uE9D9","三针运行轨迹","缩放、回看整次运行，或打开以前的记录。",UI.Button("查看全部",app.ShowRunHistory)));
                Toggle("\uE8AB","历史向左滚动","镜像放到表盘左侧；关闭时历史向右滚动。",app.Prefs.TrailLeft,delegate(bool v){app.Prefs.TrailLeft=v;});
                page.Children.Add(Row("\uE74D","清空轨迹","仅清除桌面滚动视图，不删除运行记录。",UI.Button("清空",delegate{app.History.Clear();app.Notify();})));
                Range("表盘大小","拖动表盘可移动位置；双击打开额度详情。",140,320,app.Prefs.OrbSize,v=>((int)v)+" px",v=>app.Prefs.OrbSize=v);
                Range("不透明度","调整表盘的可见程度。",35,100,app.Prefs.OrbOpacity*100,v=>((int)v)+"%",v=>app.Prefs.OrbOpacity=v/100);
                page.Children.Add(Row("\uE777","恢复默认位置","将表盘放回桌面右下方的预设位置。",UI.Button("恢复",delegate{app.Prefs.OrbLeft=Double.NaN;app.Prefs.OrbTop=Double.NaN;app.ApplyPreferences();})));
            }
            else if(index==3)
            {
                page.Children.Add(Row("\uE8B7","Codex 额度","通过本机已登录的 Codex 读取剩余额度。",UI.Button("立即刷新",delegate{app.Demo=false;var task=app.Refresh();})));
                if(app.Snapshot!=null&&app.Snapshot.Windows.Count>0)
                {
                    var windows=app.Snapshot.Windows;int selected=app.Selected==null?0:Math.Max(0,windows.FindIndex(w=>w.Id==app.Selected.Id));
                    var combo=Choice(windows.Select(w=>w.Label).ToArray(),selected,delegate(int n){if(n>=0)app.Select(windows[n]);});
                    page.Children.Add(Row("\uE823","显示的额度窗口","仅列出服务端实际提供的窗口。",combo));
                }
                Toggle("\uE9D9","估算秒针（实验）","按本地加权 token 与官方百分比变化校准；估算以琥珀色虚线针标记。",app.Prefs.EstimateFine,delegate(bool v){app.Prefs.EstimateFine=v;});
                localState=UI.Muted(app.PrecisionText);page.Children.Add(UI.Card(localState));
                var toggle=UI.Switch(app.Demo,"演示模式",delegate(bool v){app.SetDemo(v);BuildPage();});
                page.Children.Add(Row("\uE7FC","演示模式","使用明确标记的示例额度；暂停定时读取。",toggle));
                if(app.Demo)page.Children.Add(Row("\uE768","匀速波形演示","每 5 分钟消耗 1% 的示例，带 15 分钟演示轨迹。",UI.Button(app.WaveDemoPlaying?"暂停演示":"播放演示",delegate{app.SetWaveDemo(!app.WaveDemoPlaying);BuildPage();})));
                if(app.Demo)Range("示例剩余额度","演示数据不代表当前账号额度。",0,100,(double)app.DemoRemaining,v=>v.ToString("0.0")+"%",v=>{app.WaveDemoPlaying=false;app.DemoRemaining=(decimal)v;app.Notify();});
                page.Children.Add(UI.Card(UI.Muted("数字保留官方额度。优先使用原始小数；没有小数时，实验性估算经过本地校准才显示，拟合不稳时暂停。估算不具有官方 0.1% 精度。")));
            }
            else
            {
                page.Children.Add(UI.Card(UI.Text("额度时钟\nQuota Clock 0.5.0",24,true)));
                page.Children.Add(UI.Card(UI.Text("传统十二小时表盘，将剩余额度留在视线里。\n\n指针随已消耗额度顺时针前进；时针让 0–100% 消耗走满一圈。数字与外环显示剩余额度。琥珀色虚线细针表示本地估算。",14)));
                page.Children.Add(UI.Card(UI.Muted("参考 Agent Speed 的 WPF 桌面实现、字体层级与表盘色谱。保留上游 MIT 声明。\n\ngithub.com/zoefix/agent-speed\n\n本机保存显示偏好；额度读取由 Codex 处理。")));
            }
            building=false;
        }
    }
}




