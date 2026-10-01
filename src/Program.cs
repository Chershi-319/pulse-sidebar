using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
namespace Pulse {
public static class Program {
    [STAThread] public static int Main(string[] args) {
        Directory.CreateDirectory(Data.Root);
        if(args.Contains("--capture")||args.Contains("--quit")){Data.Write(Path.Combine(Data.Root,"command.json"),new {action=args.Contains("--capture")?"capture":"quit"});return 0;}
        if(args.Length>1&&args[0]=="--sensors"){int parent;if(!int.TryParse(args[1],out parent))return 2;return SensorWorker.Run(parent,args.Contains("--install-driver"));}
        if(args.Contains("--self-test")){try{Tests.Run();return 0;}catch(Exception e){File.WriteAllText(Path.Combine(Data.Root,"self-test.txt"),e.ToString());return 1;}}
        bool created;using(var mutex=new Mutex(true,"Local\\PulseSidebar.Main",out created)) {
            if(!created){Data.Write(Path.Combine(Data.Root,"command.json"),new {action="show"});return 0;}
            try {
                var app=new Application();app.DispatcherUnhandledException+=(s,e)=>{File.WriteAllText(Path.Combine(Data.Root,"error.txt"),e.Exception.ToString());e.Handled=true;app.Shutdown(1);};
                var window=new Sidebar();if(args.Length>1&&args[0]=="--ui-test")window.Loaded+=async(s,e)=>await window.UiTest(Path.GetFullPath(args[1]));
                if(args.Contains("--install-shortcut"))Startup.Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"Pulse 状态栏.lnk"));
                app.Run(window);return 0;
            }catch(Exception e){File.WriteAllText(Path.Combine(Data.Root,"error.txt"),e.ToString());return 1;}
        }
    }
}
public static class Tests {
    static void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
    public static void Run(){
        var a=QuotaSnapshot.From(Data.Parse(@"{""rateLimits"":{""primary"":{""usedPercent"":25,""windowDurationMins"":10080,""resetsAt"":1700000000},""secondary"":null}}"));Assert(a.Windows.Count==1&&a.Windows[0].Remaining==75&&a.Windows[0].Label=="7 天","weekly mapping");
        var b=QuotaSnapshot.From(Data.Parse(@"{""rateLimitsByLimitId"":{""codex"":{""primary"":{""usedPercent"":25,""windowDurationMins"":300},""secondary"":{""usedPercent"":80,""windowDurationMins"":720}},""other"":{""primary"":{""usedPercent"":110}}}}"));Assert(b.Windows.Count==3&&b.Windows[0].Remaining==75&&b.Windows[1].Label=="12 小时"&&b.Windows[2].Remaining==0,"multiple windows / clamp");
        var c=QuotaSnapshot.From(Data.Parse(@"{""rateLimits"":{""primary"":{""usedPercent"":null}}}"));Assert(c.Windows[0].Remaining==null&&c.Windows[0].Reset==null,"missing is not zero");
        Assert(QuotaSnapshot.From(Data.Parse("{}")).Windows.Count==0,"empty payload");Assert(Data.Countdown(1)=="等待服务更新","expired reset");Assert(Data.Num("NaN")==null,"non-finite values");Assert(Data.WindowLabel(300)=="5 小时","5 hour window");
        Assert(UsageSnapshot.From(Data.Parse("{}")).Today==null,"missing usage");
        File.WriteAllText(Path.Combine(Data.Root,"self-test.txt"),"PASS: quota window mapping, multiple buckets, 5/12-hour windows, null handling, clamp, expiry, finite numbers, missing usage");
    }
}
}
