using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms=System.Windows.Forms;

namespace Pulse {
public static class Theme {
    public static Brush Text=B("#F1F5F9"),Muted=B("#A6B4C7"),Accent=B("#67E8D3"),Blue=B("#81B9FA"),Purple=B("#BFA3FC"),Orange=B("#FFBC7D"),Track=B("#29364B"),Card=B("#182335");
    public static Brush B(string s){return (Brush)new BrushConverter().ConvertFromString(s);}
    public static TextBlock TextBlock(string text,double size,Brush color=null){return new TextBlock {Text=text,FontSize=size,Foreground=color??Text,FontFamily=new FontFamily("Segoe UI, Microsoft YaHei UI"),TextWrapping=TextWrapping.Wrap};}
    public static Button Button(string text,string label,Action action) {var b=new Button {Content=text,Foreground=Text,Background=B("#223147"),BorderBrush=Brushes.Transparent,BorderThickness=new Thickness(1),Padding=new Thickness(9,6,9,6),Margin=new Thickness(3),Cursor=Cursors.Hand,FontSize=12,MinHeight=30};b.Template=(ControlTemplate)System.Windows.Markup.XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type Button}'><Border x:Name='Surface' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='6' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Surface' Property='Background' Value='#34465F'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Surface' Property='BorderBrush' Value='#67E8D3'/></Trigger><Trigger Property='IsPressed' Value='True'><Setter TargetName='Surface' Property='Background' Value='#405874'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter TargetName='Surface' Property='Opacity' Value='0.65'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");AutomationProperties.SetName(b,label);b.ToolTip=label;b.Click+=(s,e)=>action();return b;}
}
public class Ring : FrameworkElement {
    public double? Value;public string Caption="—";public Brush Color=Theme.Accent;
    public Ring(){Width=66;Height=66;}
    protected override void OnRender(DrawingContext dc) {
        base.OnRender(dc);var center=new Point(33,33);dc.DrawEllipse(null,new Pen(Theme.Track,4),center,28,28);
        if(Value.HasValue&&Value>0) {double angle=Math.Min(99.999,Math.Max(0,Value.Value))/100*2*Math.PI;var g=new StreamGeometry();using(var c=g.Open()){c.BeginFigure(new Point(33,5),false,false);c.ArcTo(new Point(33+28*Math.Sin(angle),33-28*Math.Cos(angle)),new Size(28,28),0,angle>Math.PI,SweepDirection.Clockwise,true,false);}dc.DrawGeometry(null,new Pen(Color,4){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round},g);}
        var t=new FormattedText(Caption,System.Globalization.CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI Semibold"),Caption.Length>5?12:16,Theme.Text,VisualTreeHelper.GetDpi(this).PixelsPerDip);dc.DrawText(t,new Point(33-t.Width/2,33-t.Height/2));
    }
    public void Set(double? value,string caption){Value=value;Caption=caption;AutomationProperties.SetName(this,caption);InvalidateVisual();}
}
public class Metric {
    public Border View;public TextBlock Value,Detail;public ProgressBar Bar;
    public Metric(string title,Brush accent) {
        var body=new StackPanel();var row=new DockPanel();Value=Theme.TextBlock("—",19);Value.FontWeight=FontWeights.SemiBold;DockPanel.SetDock(Value,Dock.Right);row.Children.Add(Value);row.Children.Add(Theme.TextBlock(title,12,Theme.Muted));body.Children.Add(row);
        Bar=new ProgressBar {Minimum=0,Maximum=100,Height=3,Margin=new Thickness(0,9,0,8),Foreground=accent,Background=Theme.Track,BorderThickness=new Thickness(0)};body.Children.Add(Bar);
        Detail=Theme.TextBlock("正在读取…",11,Theme.Muted);body.Children.Add(Detail);
        View=new Border {Background=Theme.Card,CornerRadius=new CornerRadius(11),Padding=new Thickness(13,10,13,10),Margin=new Thickness(0,0,0,8),Child=body};
    }
    public void Set(string value,string detail,double? percent){Value.Text=value;Detail.Text=detail;Bar.Value=percent.HasValue?Math.Max(0,Math.Min(100,percent.Value)):0;Bar.Opacity=percent.HasValue?1:.35;AutomationProperties.SetName(View,value+" "+detail);}
}
public sealed class Sidebar : Window {
    readonly Settings settings=Settings.Load(); readonly HardwareCollector collector=new HardwareCollector();
    readonly DispatcherTimer hardwareTimer=new DispatcherTimer(),quotaTimer=new DispatcherTimer(),clockTimer=new DispatcherTimer();
    CodexClient client; bool hardwareBusy,quotaBusy,closing; Process sensorProcess; Forms.NotifyIcon tray;
    HardwareSnapshot hardware=new HardwareSnapshot(); QuotaSnapshot quota=new QuotaSnapshot(); UsageSnapshot usage=new UsageSnapshot();
    Border shell;StackPanel compact,expanded,quotaRows,diskRows;ScrollViewer scroller;Ring quotaRing,cpuRing,gpuRing,ramRing;
    TextBlock compactTime,compactQuota,compactCpu,compactGpu,compactNetwork,status,quotaStatus,quotaNote,planText,tokenToday,tokenMonth,tokenTotal,usageStatus;Metric cpuMetric,gpuMetric,ramMetric,vramMetric,networkMetric;Button sensorButton;DateTime lastUsage=DateTime.MinValue;
    public Sidebar() {
        Title="Pulse · 电脑与 Codex 状态栏";WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;Background=Brushes.Transparent;ShowInTaskbar=false;Topmost=settings.Topmost;FontFamily=new FontFamily("Segoe UI, Microsoft YaHei UI");
        Build();ApplyLayout();CreateTray();
        hardwareTimer.Interval=TimeSpan.FromSeconds(2);hardwareTimer.Tick+=async(s,e)=>await RefreshHardware();
        quotaTimer.Interval=TimeSpan.FromSeconds(60);quotaTimer.Tick+=async(s,e)=>await RefreshQuota();
        clockTimer.Interval=TimeSpan.FromSeconds(1);clockTimer.Tick+=(s,e)=>{UpdateClock();CheckCommand();};
        Loaded+=async(s,e)=>{ApplyLayout();hardwareTimer.Start();quotaTimer.Start();clockTimer.Start();await Task.WhenAll(RefreshHardware(),RefreshQuota());};
        KeyDown+=(s,e)=>{if(e.Key==Key.Escape&&settings.Expanded)Toggle();};
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged+=DisplayChanged;
        SourceInitialized+=(s,e)=>{var source=HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);if(source!=null)source.AddHook(WindowMessage);};
        Closing+=(s,e)=>{closing=true;hardwareTimer.Stop();quotaTimer.Stop();clockTimer.Stop();Microsoft.Win32.SystemEvents.DisplaySettingsChanged-=DisplayChanged;if(tray!=null)tray.Dispose();if(client!=null)client.Dispose();settings.Save();};
    }
    IntPtr WindowMessage(IntPtr hwnd,int msg,IntPtr wp,IntPtr lp,ref bool handled){if(msg==0x02E0||msg==0x001A)Dispatcher.BeginInvoke(new Action(ApplyLayout));return IntPtr.Zero;}
    void DisplayChanged(object sender,EventArgs e){Dispatcher.BeginInvoke(new Action(ApplyLayout));}
    void Build() {
        Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar),System.Windows.Markup.XamlReader.Parse(@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type ScrollBar}'><Setter Property='Width' Value='7'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type ScrollBar}'><Track x:Name='PART_Track' IsDirectionReversed='True' Orientation='Vertical'><Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType='{x:Type Thumb}'><Border Background='#475569' CornerRadius='3' Margin='1,0'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb></Track></ControlTemplate></Setter.Value></Setter></Style>"));
        shell=new Border {Background=Theme.B("#F5101827"),BorderBrush=Theme.B("#475267"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(20),Padding=new Thickness(12),Margin=new Thickness(6)};
        var all=new Grid();compact=new StackPanel();expanded=new StackPanel();all.Children.Add(compact);all.Children.Add(expanded);shell.Child=all;Content=shell;
        var mark=Theme.TextBlock("P U L S E",10,Theme.Accent);mark.FontWeight=FontWeights.Bold;mark.HorizontalAlignment=HorizontalAlignment.Center;mark.Margin=new Thickness(0,8,0,13);mark.ToolTip="拖动可调整位置，右键打开设置";mark.MouseLeftButtonDown+=Drag;compact.Children.Add(mark);
        quotaRing=CompactMetric("CODEX",Theme.Accent,out compactQuota);cpuRing=CompactMetric("CPU",Theme.Blue,out compactCpu);gpuRing=CompactMetric("GPU",Theme.Purple,out compactGpu);TextBlock ramDetail;ramRing=CompactMetric("内存",Theme.Orange,out ramDetail);ramDetail.Text="使用率";
        compactNetwork=Theme.TextBlock("↓ —",10,Theme.Muted);compactNetwork.TextAlignment=TextAlignment.Center;compactNetwork.Margin=new Thickness(0,6,0,9);compact.Children.Add(compactNetwork);
        compact.Children.Add(Theme.Button("展开", "展开详细监控",Toggle));compactTime=Theme.TextBlock("",12,Theme.Muted);compactTime.HorizontalAlignment=HorizontalAlignment.Center;compactTime.Margin=new Thickness(0,11,0,5);compact.Children.Add(compactTime);
        var header=new DockPanel();var controls=new StackPanel {Orientation=Orientation.Horizontal};controls.Children.Add(Theme.Button("···","设置",ShowMenu));controls.Children.Add(Theme.Button("›","收起侧边栏",Toggle));DockPanel.SetDock(controls,Dock.Right);header.Children.Add(controls);
        var brand=new StackPanel();var name=Theme.TextBlock("PULSE",18,Theme.Accent);name.FontWeight=FontWeights.Bold;brand.Children.Add(name);brand.Children.Add(Theme.TextBlock("电脑与 AI · 一眼掌握",11,Theme.Muted));brand.MouseLeftButtonDown+=Drag;header.Children.Add(brand);expanded.Children.Add(header);
        scroller=new ScrollViewer {VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Margin=new Thickness(0,15,0,0)};var content=new StackPanel {Margin=new Thickness(0,0,4,0)};scroller.Content=content;expanded.Children.Add(scroller);
        var quotaCard=new StackPanel();var title=new DockPanel();planText=Theme.TextBlock("",11,Theme.Muted);DockPanel.SetDock(planText,Dock.Right);title.Children.Add(planText);var qt=Theme.TextBlock("Codex 额度",17);qt.FontWeight=FontWeights.SemiBold;title.Children.Add(qt);quotaCard.Children.Add(title);
        quotaStatus=Theme.TextBlock("正在连接本机 Codex…",11,Theme.Muted);quotaStatus.Margin=new Thickness(0,5,0,11);quotaCard.Children.Add(quotaStatus);quotaRows=new StackPanel();quotaCard.Children.Add(quotaRows);quotaNote=Theme.TextBlock("额度窗口以账号实际返回为准",11,Theme.Muted);quotaNote.Margin=new Thickness(0,6,0,0);quotaCard.Children.Add(quotaNote);
        content.Children.Add(new Border {Background=Theme.B("#182D36"),BorderBrush=Theme.B("#315050"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(13),Padding=new Thickness(14),Child=quotaCard});
        content.Children.Add(Section("硬件概览", "2 秒刷新"));
        cpuMetric=new Metric("CPU",Theme.Blue);content.Children.Add(cpuMetric.View);gpuMetric=new Metric("GPU",Theme.Purple);content.Children.Add(gpuMetric.View);ramMetric=new Metric("内存",Theme.Orange);content.Children.Add(ramMetric.View);vramMetric=new Metric("显存",Theme.Purple);content.Children.Add(vramMetric.View);networkMetric=new Metric("网络",Theme.Accent);content.Children.Add(networkMetric.View);
        sensorButton=Theme.Button("连接 CPU 温度", "安装必要驱动并连接温度传感器（Windows 授权）",EnableSensors);sensorButton.HorizontalAlignment=HorizontalAlignment.Stretch;content.Children.Add(sensorButton);
        content.Children.Add(Section("磁盘空间", "本地磁盘"));diskRows=new StackPanel();content.Children.Add(diskRows);
        content.Children.Add(Section("Codex Token 用量", "服务端统计"));
        var tokens=new Grid();tokens.ColumnDefinitions.Add(new ColumnDefinition());tokens.ColumnDefinitions.Add(new ColumnDefinition());tokenToday=TokenCard(tokens,0,"今日");tokenMonth=TokenCard(tokens,1,"本月已返回记录");content.Children.Add(tokens);
        tokenTotal=Theme.TextBlock("累计 —",11,Theme.Muted);tokenTotal.Margin=new Thickness(2,5,0,4);content.Children.Add(tokenTotal);usageStatus=Theme.TextBlock("每日统计可能延迟更新",10,Theme.Muted);content.Children.Add(usageStatus);
        var footer=new StackPanel {Orientation=Orientation.Horizontal,Margin=new Thickness(0,12,0,0)};footer.Children.Add(Theme.Button("刷新额度","立即刷新 Codex 额度",async()=>await RefreshQuota()));footer.Children.Add(Theme.Button("隐藏","隐藏到系统托盘",Hide));footer.Children.Add(Theme.Button("设置","打开侧边栏设置",ShowMenu));content.Children.Add(footer);
        status=Theme.TextBlock("",10,Theme.Muted);status.Margin=new Thickness(3,8,0,2);content.Children.Add(status);
        shell.MouseRightButtonUp+=(s,e)=>{ShowMenu();e.Handled=true;};
    }
    Ring CompactMetric(string label,Brush color,out TextBlock detail) {
        var block=new StackPanel();var title=Theme.TextBlock(label,10,Theme.Muted);title.HorizontalAlignment=HorizontalAlignment.Center;block.Children.Add(title);var ring=new Ring {Color=color,Margin=new Thickness(0,5,0,0)};block.Children.Add(ring);detail=Theme.TextBlock("—",10,Theme.Muted);detail.HorizontalAlignment=HorizontalAlignment.Center;detail.Margin=new Thickness(0,1,0,10);block.Children.Add(detail);
        var button=new Button {Content=block,Background=Brushes.Transparent,BorderThickness=new Thickness(0),Cursor=Cursors.Hand,Padding=new Thickness(0),HorizontalContentAlignment=HorizontalAlignment.Center};AutomationProperties.SetName(button,label+"，展开详情");button.Click+=(s,e)=>Toggle();compact.Children.Add(button);return ring;
    }
    UIElement Section(string name,string hint){var d=new DockPanel {Margin=new Thickness(2,16,2,9)};var h=Theme.TextBlock(hint,10,Theme.Muted);DockPanel.SetDock(h,Dock.Right);d.Children.Add(h);var n=Theme.TextBlock(name,12,Theme.Muted);n.FontWeight=FontWeights.SemiBold;d.Children.Add(n);return d;}
    TextBlock TokenCard(Grid parent,int col,string label){var p=new StackPanel();p.Children.Add(Theme.TextBlock(label,10,Theme.Muted));var t=Theme.TextBlock("—",23);t.FontWeight=FontWeights.SemiBold;t.Margin=new Thickness(0,4,0,0);p.Children.Add(t);var b=new Border {Background=Theme.Card,CornerRadius=new CornerRadius(10),Padding=new Thickness(12),Margin=new Thickness(col==0?0:4,0,col==0?4:0,0),Child=p};Grid.SetColumn(b,col);parent.Children.Add(b);return t;}
    public void Toggle(){settings.Expanded=!settings.Expanded;ApplyLayout();settings.Save();}
    public void ApplyLayout() {
        if(shell==null)return;settings.Scale=Math.Max(.8,Math.Min(1.3,settings.Scale));
        compact.Visibility=settings.Expanded?Visibility.Collapsed:Visibility.Visible;expanded.Visibility=settings.Expanded?Visibility.Visible:Visibility.Collapsed;
        shell.LayoutTransform=new ScaleTransform(settings.Scale,settings.Scale);
        var screens=Forms.Screen.AllScreens;if(settings.Screen<0||settings.Screen>=screens.Length)settings.Screen=0;var area=screens[settings.Screen].WorkingArea;
        double factor=1;var source=PresentationSource.FromVisual(this);if(source!=null&&source.CompositionTarget!=null)factor=source.CompositionTarget.TransformToDevice.M11;
        double ax=area.Left/factor,ay=area.Top/factor,aw=area.Width/factor,ah=area.Height/factor;
        Width=(settings.Expanded?366:110)*settings.Scale;Height=Math.Min((settings.Expanded?830:640)*settings.Scale,ah-16);scroller.MaxHeight=Math.Max(160,Height/settings.Scale-110);
        if(!settings.Expanded && Height<640*settings.Scale) shell.LayoutTransform=new ScaleTransform(settings.Scale*(Height/(640*settings.Scale)),settings.Scale*(Height/(640*settings.Scale)));
        Left=settings.Left?ax+4:ax+aw-Width-4;Top=ay+8+Math.Max(0,ah-Height-16)*Math.Max(0,Math.Min(1,settings.Vertical));Topmost=settings.Topmost;
    }
    void Drag(object sender,MouseButtonEventArgs e) {
        if(e.ClickCount==2){Toggle();return;}try{DragMove();var screen=Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle);settings.Screen=Array.IndexOf(Forms.Screen.AllScreens,screen);var source=PresentationSource.FromVisual(this);double scale=source==null?1:source.CompositionTarget.TransformToDevice.M11;var a=screen.WorkingArea;settings.Left=(Left+Width/2)*scale<a.Left+a.Width/2;settings.Vertical=(Top-a.Top/scale-8)/Math.Max(1,a.Height/scale-Height-16);ApplyLayout();settings.Save();}catch{}
    }
    async Task RefreshHardware() {
        if(hardwareBusy||closing)return;hardwareBusy=true;
        try{hardware=await Task.Run(()=>collector.Read());if(!closing)RenderHardware();}catch{if(!closing)status.Text="硬件读取暂时失败，正在重试";}finally{hardwareBusy=false;}
    }
    async Task RefreshQuota() {
        if(quotaBusy||closing)return;quotaBusy=true;quotaStatus.Text="正在刷新…";
        try {
            if(client==null){client=new CodexClient();await client.Connect();}
            var result=await client.Request("account/rateLimits/read",new {});quota=QuotaSnapshot.From(result);if(closing)return;RenderQuota();
            if((DateTime.Now-lastUsage).TotalMinutes>=5) {
                try{usage=UsageSnapshot.From(await client.Request("account/usage/read",new {}));lastUsage=DateTime.Now;}
                catch{usage.Error="用量暂不可用";}if(!closing)RenderUsage();
            }
        }catch(Exception e){quota.Error=e is TimeoutException?"额度读取超时":"额度连接失败，请检查 Codex 登录与网络";if(client!=null)client.Dispose();client=null;if(!closing)RenderQuota();}
        finally{quotaBusy=false;}
    }
    void RenderHardware() {
        cpuRing.Set(hardware.Cpu,Data.Percent(hardware.Cpu));compactCpu.Text=Data.Temp(hardware.CpuTemp);gpuRing.Set(hardware.Gpu,Data.Percent(hardware.Gpu));compactGpu.Text=Data.Temp(hardware.GpuTemp);ramRing.Set(hardware.RamPercent,Data.Percent(hardware.RamPercent));compactNetwork.Text="↓ "+Data.Rate(hardware.Down);
        cpuMetric.Set(Data.Percent(hardware.Cpu)+"  ·  "+Data.Temp(hardware.CpuTemp),hardware.CpuName,hardware.Cpu);cpuMetric.View.ToolTip=hardware.SensorStatus;
        gpuMetric.Set(Data.Percent(hardware.Gpu)+"  ·  "+Data.Temp(hardware.GpuTemp),hardware.GpuError!=""?hardware.GpuError:hardware.GpuName+(hardware.Power.HasValue?" · "+hardware.Power.Value.ToString("0.0")+" W":""),hardware.Gpu);
        ramMetric.Set(Data.Percent(hardware.RamPercent),GB(hardware.RamUsed)+" / "+GB(hardware.RamTotal)+" GB",hardware.RamPercent);vramMetric.Set(Data.Percent(hardware.VramPercent),GB(hardware.VramUsed)+" / "+GB(hardware.VramTotal)+" GB",hardware.VramPercent);
        networkMetric.Set("↓ "+Data.Rate(hardware.Down),"上传 ↑ "+Data.Rate(hardware.Up)+"  ·  活动物理网卡",null);networkMetric.Bar.Visibility=Visibility.Collapsed;
        sensorButton.Content=hardware.CpuTemp.HasValue?"CPU 温度已连接 · "+Data.Temp(hardware.CpuTemp):"连接 CPU 温度 · 需要系统授权";sensorButton.ToolTip=hardware.SensorStatus;sensorButton.IsEnabled=!hardware.CpuTemp.HasValue;
        diskRows.Children.Clear();foreach(var disk in hardware.Disks){var m=new Metric(disk.Name,disk.Percent>=90?Theme.Orange:Theme.Blue);m.Set(disk.Percent.ToString("0")+"%",disk.Used.ToString("0")+" / "+disk.Total.ToString("0")+" GB",disk.Percent);diskRows.Children.Add(m.View);}
        status.Text="硬件 "+hardware.Updated.ToString("HH:mm:ss")+" · 右键设置 / Esc 收起";
    }
    static string GB(double? n){return n.HasValue?n.Value.ToString("0.0"):"—";}
    void RenderQuota() {
        quotaRows.Children.Clear();planText.Text=quota.Plan==""?"":quota.Plan.ToUpperInvariant();
        foreach(var w in quota.Windows) {var box=new StackPanel {Margin=new Thickness(0,0,0,11)};var row=new DockPanel();var val=Theme.TextBlock(Data.Percent(w.Remaining)+" 剩余",19);val.FontWeight=FontWeights.SemiBold;DockPanel.SetDock(val,Dock.Right);row.Children.Add(val);row.Children.Add(Theme.TextBlock((w.Bucket=="codex"?"":w.Bucket+" · ")+w.Label,12,Theme.Muted));box.Children.Add(row);box.Children.Add(new ProgressBar {Value=w.Remaining??0,Maximum=100,Height=5,Margin=new Thickness(0,8,0,6),Foreground=w.Remaining<15?Theme.Orange:Theme.Accent,Background=Theme.Track,BorderThickness=new Thickness(0)});var countdown=Theme.TextBlock(Data.Countdown(w.Reset),11,Theme.Muted);countdown.Tag=w;box.Children.Add(countdown);quotaRows.Children.Add(box);}
        if(quota.Windows.Count==0)quotaRows.Children.Add(Theme.TextBlock(quota.Error==""?"账号未返回额度窗口":"尚无可显示的额度",14,Theme.Muted));
        var primary=quota.Windows.FirstOrDefault();quotaRing.Set(primary==null?null:primary.Remaining,primary==null?"—":Data.Percent(primary.Remaining));
        bool expected=quota.Windows.Any(w=>w.Minutes==300)||quota.Windows.Any(w=>w.Minutes==720);
        quotaNote.Text=(expected?"窗口由账号接口提供":"账号未返回 5 / 12 小时窗口")+(quota.Resets.HasValue?"\n可用重置次数 "+quota.Resets.Value:"");
        UpdateClock();
    }
    void RenderUsage(){tokenToday.Text=Data.Tokens(usage.Today);tokenMonth.Text=Data.Tokens(usage.Month);tokenTotal.Text="累计 "+Data.Tokens(usage.Lifetime)+" tokens";usageStatus.Text=usage.Error!=""?usage.Error:"每日数据按服务端日期归属 · "+usage.Updated.ToString("HH:mm")+" 更新";}
    void UpdateClock(){compactTime.Text=DateTime.Now.ToString("HH:mm");bool stale=quota.Updated!=default(DateTime)&&(DateTime.Now-quota.Updated).TotalSeconds>150;bool bad=quota.Error!=""||stale;var first=quota.Windows.FirstOrDefault();compactQuota.Text=bad?"数据待更新":first==null?"读取中":first.Label+"剩余";quotaRing.Opacity=bad?.45:1;
        quotaStatus.Text=quotaBusy?"正在刷新…":quota.Error!=""?quota.Error+(quota.Updated!=default(DateTime)?" · 上次 "+quota.Updated.ToString("HH:mm"):""):quota.Updated==default(DateTime)?"连接中…":(stale?"数据已过期 · ":"已同步 · ")+quota.Updated.ToString("HH:mm:ss");quotaStatus.Foreground=bad?Theme.Orange:Theme.Muted;
        foreach(var panel in quotaRows.Children.OfType<StackPanel>())foreach(var txt in panel.Children.OfType<TextBlock>()){var w=txt.Tag as QuotaWindow;if(w!=null)txt.Text=Data.Countdown(w.Reset);}
    }
    void ShowMenu() {
        var menu=new ContextMenu();Add(menu,settings.Expanded?"收起侧边栏":"展开详情",Toggle);Add(menu,"隐藏到托盘",Hide);menu.Items.Add(new Separator());
        Add(menu,"停靠左侧",()=>{settings.Left=true;ApplyLayout();settings.Save();},settings.Left);Add(menu,"停靠右侧",()=>{settings.Left=false;ApplyLayout();settings.Save();},!settings.Left);
        for(int i=0;i<Forms.Screen.AllScreens.Length;i++){int index=i;Add(menu,"显示器 "+(i+1),()=>{settings.Screen=index;ApplyLayout();settings.Save();},settings.Screen==i);}
        Add(menu,"始终置顶",()=>{settings.Topmost=!settings.Topmost;ApplyLayout();settings.Save();},settings.Topmost);
        Add(menu,"开机启动",()=>{try{Startup.Toggle();}catch(Exception ex){MessageBox.Show("无法修改开机启动："+ex.Message,"Pulse");}},Startup.Enabled);
        foreach(double scale in new[]{.8,1,1.2}){double captured=scale;Add(menu,"界面大小 "+(scale*100).ToString("0")+"%",()=>{settings.Scale=captured;ApplyLayout();settings.Save();},Math.Abs(settings.Scale-scale)<.01);}
        menu.Items.Add(new Separator());Add(menu,"连接 CPU 温度（系统授权）",EnableSensors);Add(menu,"立即刷新额度",async()=>await RefreshQuota());Add(menu,"打开使用说明",()=>Process.Start(new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","README.md")){UseShellExecute=true}));Add(menu,"退出 Pulse",Close);menu.PlacementTarget=shell;menu.IsOpen=true;
    }
    static void Add(ContextMenu m,string text,Action action,bool check=false){var item=new MenuItem {Header=text,IsChecked=check};item.Click+=(s,e)=>action();m.Items.Add(item);}
    void CreateTray(){tray=new Forms.NotifyIcon {Text="Pulse · 电脑与 Codex 状态栏",Icon=System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location),Visible=true};tray.DoubleClick+=(s,e)=>Dispatcher.Invoke(new Action(()=>{Show();ApplyLayout();Activate();}));var menu=new Forms.ContextMenuStrip();menu.Items.Add("显示状态栏",null,(s,e)=>Dispatcher.Invoke(new Action(()=>{Show();ApplyLayout();Activate();})));menu.Items.Add("展开 / 收起",null,(s,e)=>Dispatcher.Invoke(new Action(()=>{Show();Toggle();})));menu.Items.Add("连接 CPU 温度",null,(s,e)=>Dispatcher.Invoke(new Action(EnableSensors)));menu.Items.Add("退出",null,(s,e)=>Dispatcher.Invoke(new Action(Close)));tray.ContextMenuStrip=menu;}
    public void EnableSensors(){
        try{if(sensorProcess!=null&&!sensorProcess.HasExited)return;
            sensorProcess=Process.Start(new ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location,"--sensors "+Process.GetCurrentProcess().Id+" --install-driver") {UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden});sensorButton.Content="正在连接传感器…";
        }catch(System.ComponentModel.Win32Exception){sensorButton.Content="授权未完成，点击重试";}catch{sensorButton.Content="连接失败，点击重试";}
    }
    public async Task UiTest(string dir) {
        await Task.Delay(18000);await CaptureUi(dir);Close();
    }
    async void CheckCommand(){string path=Path.Combine(Data.Root,"command.json");if(!File.Exists(path))return;try{string action=Data.Str(Data.Get(Data.Parse(File.ReadAllText(path)),"action"));File.Delete(path);if(action=="quit")Close();else if(action=="show"){Show();Activate();ApplyLayout();}else if(action=="capture")await CaptureUi(Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","artifacts")));}catch{}}
    async Task CaptureUi(string dir) {
        Directory.CreateDirectory(dir);bool wasExpanded=settings.Expanded;bool wasLeft=settings.Left;
        settings.Expanded=true;ApplyLayout();await Task.Delay(500);Capture(Path.Combine(dir,"expanded.png"));
        scroller.ScrollToBottom();await Task.Delay(300);Capture(Path.Combine(dir,"expanded-bottom.png"));scroller.ScrollToTop();
        settings.Expanded=false;ApplyLayout();await Task.Delay(500);Capture(Path.Combine(dir,"compact.png"));
        settings.Left=true;ApplyLayout();bool dockLeft=Left<Forms.Screen.PrimaryScreen.WorkingArea.Width/2;settings.Left=false;ApplyLayout();bool dockRight=Left>Forms.Screen.PrimaryScreen.WorkingArea.Width/2;
        Hide();bool hidden=!IsVisible;Show();settings.Left=wasLeft;settings.Expanded=wasExpanded;ApplyLayout();
        Data.Write(Path.Combine(dir,"ui-check.json"),new {quotaWindows=quota.Windows,quotaError=quota.Error,hardware=hardware,usage=usage,dockLeft=dockLeft,dockRight=dockRight,hideShow=hidden&&IsVisible,compactWidth=110,expandedWidth=366});
    }
    void Capture(string path){UpdateLayout();var bmp=new RenderTargetBitmap((int)Math.Ceiling(ActualWidth),(int)Math.Ceiling(ActualHeight),96,96,PixelFormats.Pbgra32);bmp.Render(this);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bmp));using(var file=File.Create(path))encoder.Save(file);}
}
public static class Startup {
    static string Link {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup),"Pulse 状态栏.lnk");}}
    public static bool Enabled {get{return File.Exists(Link);}}
    public static void Toggle(){if(Enabled)File.Delete(Link);else Shortcut(Link);}
    public static void Shortcut(string path) {Type type=Type.GetTypeFromProgID("WScript.Shell");dynamic shell=Activator.CreateInstance(type);dynamic link=shell.CreateShortcut(path);link.TargetPath=System.Reflection.Assembly.GetExecutingAssembly().Location;link.WorkingDirectory=AppDomain.CurrentDomain.BaseDirectory;link.Description="Pulse · Codex 额度与硬件状态栏";link.Save();System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link);System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);}
}
}
