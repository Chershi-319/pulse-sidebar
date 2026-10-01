using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Pulse {
public static class Data {
    public static string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PulseSidebar");
    public static Dictionary<string, object> Obj(object x) { return x as Dictionary<string, object> ?? new Dictionary<string, object>(); }
    public static object Get(object x, string k) { object v; return Obj(x).TryGetValue(k, out v) ? v : null; }
    public static double? Num(object x) { double n; return x != null && double.TryParse(Convert.ToString(x, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out n) && !double.IsNaN(n) && !double.IsInfinity(n) ? n : (double?)null; }
    public static string Str(object x) { return x == null ? "" : Convert.ToString(x, CultureInfo.InvariantCulture); }
    public static JavaScriptSerializer Json() { return new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 }; }
    public static object Parse(string s) { return Json().DeserializeObject(s); }
    public static void Write(string path, object value) { Directory.CreateDirectory(Path.GetDirectoryName(path)); string tmp = path + ".tmp"; File.WriteAllText(tmp, Json().Serialize(value)); if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path); }
    public static double UnixNow { get { return (DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds; } }
    public static string Percent(double? n) { return n.HasValue ? n.Value.ToString("0") + "%" : "—"; }
    public static string Temp(double? n) { return n.HasValue ? n.Value.ToString("0") + "°C" : "—"; }
    public static string Tokens(double? n) { if (!n.HasValue) return "—"; double v = n.Value; return v >= 1e9 ? (v/1e9).ToString("0.00")+"B" : v>=1e6 ? (v/1e6).ToString("0.00")+"M" : v>=1e3 ? (v/1e3).ToString("0.0")+"K" : v.ToString("0"); }
    public static string Rate(double? n) { if (!n.HasValue) return "—"; return n >= 1048576 ? (n.Value/1048576).ToString("0.0")+" MB/s" : (n.Value/1024).ToString("0")+" KB/s"; }
    public static string WindowLabel(double? m) { if (!m.HasValue || m<=0) return "周期未提供"; if (m%1440==0) return (m/1440).Value.ToString("0")+" 天"; if (m%60==0) return (m/60).Value.ToString("0")+" 小时"; return m.Value.ToString("0")+" 分钟"; }
    public static string Countdown(double? reset) { if (!reset.HasValue) return "重置时间未提供"; double sec=reset.Value-UnixNow; if (sec<=0) return "等待服务更新"; TimeSpan t=TimeSpan.FromSeconds(sec); return (t.Days>0?t.Days+"天 ":"")+(t.Days>0||t.Hours>0?t.Hours+"时 ":"")+t.Minutes+"分后重置"; }
    public static string Run(string exe, string args, int timeout) {
        using (Process p=new Process()) {
            p.StartInfo=new ProcessStartInfo(exe,args) { UseShellExecute=false, CreateNoWindow=true, RedirectStandardOutput=true, RedirectStandardError=true };
            p.Start(); var output=p.StandardOutput.ReadToEndAsync(); var error=p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(timeout)) { try { p.Kill(); } catch {} throw new TimeoutException(); }
            if (p.ExitCode!=0) throw new IOException("进程返回 " + p.ExitCode);
            return output.GetAwaiter().GetResult();
        }
    }
}
public class QuotaWindow {
    public string Bucket {get;set;} public double? Minutes {get;set;} public double? Remaining {get;set;} public double? Reset {get;set;}
    public string Label { get {return Data.WindowLabel(Minutes);} }
}
public class QuotaSnapshot {
    public List<QuotaWindow> Windows = new List<QuotaWindow>();
    public DateTime Updated; public string Error=""; public string Plan=""; public int? Resets;
    public static QuotaSnapshot From(object result) {
        var s=new QuotaSnapshot {Updated=DateTime.Now};
        var buckets=Data.Obj(Data.Get(result,"rateLimitsByLimitId"));
        if (buckets.Count==0) { var old=Data.Get(result,"rateLimits"); if(old!=null) buckets["codex"]=old; }
        foreach(var kv in buckets.OrderBy(k=>k.Key=="codex"?0:1)) {
            string name=Data.Str(Data.Get(kv.Value,"limitName")); if(name=="") name=kv.Key;
            if(s.Plan=="") s.Plan=Data.Str(Data.Get(kv.Value,"planType"));
            foreach(string key in new[]{"primary","secondary"}) {
                object w=Data.Get(kv.Value,key); if(w==null) continue;
                double? used=Data.Num(Data.Get(w,"usedPercent"));
                s.Windows.Add(new QuotaWindow {Bucket=name, Minutes=Data.Num(Data.Get(w,"windowDurationMins")), Remaining=used.HasValue?Math.Max(0,Math.Min(100,100-used.Value)):(double?)null, Reset=Data.Num(Data.Get(w,"resetsAt"))});
            }
        }
        double? resets=Data.Num(Data.Get(Data.Get(result,"rateLimitResetCredits"),"availableCount")); s.Resets=resets.HasValue?(int?)resets.Value:null;
        return s;
    }
}
public class UsageSnapshot {
    public double? Today, Month, Lifetime; public DateTime Updated; public string Error="";
    public static UsageSnapshot From(object r) {
        var s=new UsageSnapshot {Updated=DateTime.Now, Lifetime=Data.Num(Data.Get(Data.Get(r,"summary"),"lifetimeTokens"))};
        var rows=Data.Get(r,"dailyUsageBuckets") as IEnumerable;
        if(rows!=null) {
            double month=0; bool monthFound=false;
            foreach(var row in rows) {
                string date=Data.Str(Data.Get(row,"startDate")); double? tokens=Data.Num(Data.Get(row,"tokens"));
                if(date==DateTime.Now.ToString("yyyy-MM-dd")) s.Today=tokens;
                if(date.StartsWith(DateTime.Now.ToString("yyyy-MM")) && tokens.HasValue) { month+=tokens.Value; monthFound=true; }
            }
            if(monthFound) s.Month=month;
        }
        return s;
    }
}
public sealed class CodexClient : IDisposable {
    Process process; int nextId; readonly object gate=new object(); readonly Dictionary<int,TaskCompletionSource<object>> pending=new Dictionary<int,TaskCompletionSource<object>>();
    public static string FindExecutable() {
        string configured=Environment.GetEnvironmentVariable("PULSE_CODEX_PATH"); if(!string.IsNullOrEmpty(configured)&&File.Exists(configured)) return configured;
        string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OpenAI","Codex","bin");
        if(Directory.Exists(root)) { var found=Directory.GetFiles(root,"codex.exe",SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault(); if(found!=null) return found; }
        foreach(var dir in (Environment.GetEnvironmentVariable("PATH")??"").Split(';')) { try {string path=Path.Combine(dir.Trim('"'),"codex.exe"); if(File.Exists(path)) return path;} catch{} }
        throw new FileNotFoundException("未找到 Codex，请安装或设置 PULSE_CODEX_PATH");
    }
    public async Task Connect() {
        process=new Process {StartInfo=new ProcessStartInfo(FindExecutable(),"app-server --listen stdio://") {UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=System.Text.Encoding.UTF8}};
        process.OutputDataReceived+=(s,e)=>Receive(e.Data);
        process.ErrorDataReceived+=(s,e)=>{}; // Do not persist potentially sensitive server logs.
        process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine();
        await Request("initialize",new {clientInfo=new {name="pulse_sidebar",title="Pulse Sidebar",version="1.0.0"}});
        Send(new {method="initialized",@params=new {}});
    }
    void Send(object obj) {lock(gate) {process.StandardInput.WriteLine(Data.Json().Serialize(obj));process.StandardInput.Flush();}}
    void Receive(string line) {
        if(line==null) {lock(gate) {foreach(var t in pending.Values) t.TrySetException(new IOException("Codex 连接已关闭"));pending.Clear();} return;}
        try {
            object obj=Data.Parse(line); double? id=Data.Num(Data.Get(obj,"id")); if(!id.HasValue) return;
            TaskCompletionSource<object> t=null; lock(gate) {if(pending.TryGetValue((int)id.Value,out t)) pending.Remove((int)id.Value);}
            if(t!=null) {if(Data.Get(obj,"error")!=null) t.TrySetException(new IOException("Codex 接口暂不可用，请检查登录或网络"));else t.TrySetResult(Data.Get(obj,"result"));}
        } catch {}
    }
    public async Task<object> Request(string method, object args) {
        int id=Interlocked.Increment(ref nextId); var t=new TaskCompletionSource<object>(); lock(gate) pending[id]=t;
        try {Send(new {id=id,method=method,@params=args}); if(await Task.WhenAny(t.Task,Task.Delay(20000))!=t.Task) throw new TimeoutException("Codex 读取超时"); return await t.Task;}
        finally {lock(gate) pending.Remove(id);}
    }
    public void Dispose() { if(process==null)return; try{process.StandardInput.Close(); if(!process.WaitForExit(800))process.Kill();}catch{} process.Dispose();process=null; }
}
public class Settings {
    public bool Expanded=false, Left=false, Topmost=true; public int Screen=0; public double Scale=1; public double Vertical=.45;
    static string FileName {get{return Path.Combine(Data.Root,"settings.json");}}
    public static Settings Load() {try{return Data.Json().Deserialize<Settings>(File.ReadAllText(FileName))??new Settings();}catch{return new Settings();}}
    public void Save() {Data.Write(FileName,this);}
}
}
