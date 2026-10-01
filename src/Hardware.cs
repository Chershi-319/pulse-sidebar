using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using LibreHardwareMonitor.Hardware;

namespace Pulse {
public class HardwareSnapshot {
    public double? Cpu, CpuTemp, Gpu, GpuTemp, RamUsed, RamTotal, VramUsed, VramTotal, Power, Down, Up;
    public string CpuName="CPU",GpuName="GPU",GpuError="",SensorStatus="CPU 温度尚未连接";
    public DateTime Updated;
    public List<DiskSnapshot> Disks=new List<DiskSnapshot>();
    public double? RamPercent {get{return RamTotal>0?RamUsed/RamTotal*100:null;}}
    public double? VramPercent {get{return VramTotal>0?VramUsed/VramTotal*100:null;}}
}
public class DiskSnapshot {public string Name;public double Used,Total;public double Percent {get{return Total>0?Used/Total*100:0;}}}
public sealed class HardwareCollector {
    [StructLayout(LayoutKind.Sequential)] struct Mem {public uint Length,Load;public ulong TotalPhys,AvailPhys,TotalPage,AvailPage,TotalVirtual,AvailVirtual,AvailExtended;}
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool GlobalMemoryStatusEx(ref Mem m);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetSystemTimes(out ulong idle,out ulong kernel,out ulong user);
    ulong previousIdle,previousTotal; bool cpuReady;
    readonly Dictionary<string,long[]> network=new Dictionary<string,long[]>();
    readonly Stopwatch networkClock=Stopwatch.StartNew(); double previousNetworkTime;
    string cpuName="CPU"; int tick; List<DiskSnapshot> disks=new List<DiskSnapshot>();
    public HardwareCollector() {try{using(var key=Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))cpuName=Convert.ToString(key.GetValue("ProcessorNameString")).Trim();}catch{}}
    public HardwareSnapshot Read() {
        var s=new HardwareSnapshot {CpuName=cpuName,Updated=DateTime.Now};
        ulong idle,kernel,user;
        if(GetSystemTimes(out idle,out kernel,out user)) {ulong total=kernel+user;if(cpuReady&&total>previousTotal&&idle>=previousIdle)s.Cpu=Math.Max(0,Math.Min(100,100*(1.0-(double)(idle-previousIdle)/(total-previousTotal))));previousIdle=idle;previousTotal=total;cpuReady=true;}
        Mem mem=new Mem();mem.Length=(uint)Marshal.SizeOf(typeof(Mem));if(GlobalMemoryStatusEx(ref mem)){s.RamTotal=mem.TotalPhys/1073741824.0;s.RamUsed=(mem.TotalPhys-mem.AvailPhys)/1073741824.0;}
        try {
            string path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"nvidia-smi.exe");
            string result=Data.Run(File.Exists(path)?path:"nvidia-smi.exe","--query-gpu=name,temperature.gpu,utilization.gpu,memory.used,memory.total,power.draw --format=csv,noheader,nounits",4000);
            string[] fields=result.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries)[0].Split(',').Select(v=>v.Trim()).ToArray();
            if(fields.Length>=6){s.GpuName=fields[0];s.GpuTemp=Data.Num(fields[1]);s.Gpu=Data.Num(fields[2]);s.VramUsed=Data.Num(fields[3])/1024;s.VramTotal=Data.Num(fields[4])/1024;s.Power=Data.Num(fields[5]);}
        }catch{s.GpuError="NVIDIA 驱动数据不可用";}
        try {
            double time=networkClock.Elapsed.TotalSeconds,dt=time-previousNetworkTime;long rx=0,tx=0;bool matched=false;var live=new HashSet<string>();
            foreach(var nic in NetworkInterface.GetAllNetworkInterfaces()) {
                // Physical Ethernet and Wi-Fi only, avoiding VPN/tunnel double counting.
                if(nic.OperationalStatus!=OperationalStatus.Up||(nic.NetworkInterfaceType!=NetworkInterfaceType.Ethernet&&nic.NetworkInterfaceType!=NetworkInterfaceType.Wireless80211)) continue;
                if(nic.Description.IndexOf("virtual",StringComparison.OrdinalIgnoreCase)>=0||nic.Description.IndexOf("vpn",StringComparison.OrdinalIgnoreCase)>=0||nic.Description.IndexOf("tap",StringComparison.OrdinalIgnoreCase)>=0) continue;
                var stats=nic.GetIPv4Statistics();long[] old;live.Add(nic.Id);
                if(network.TryGetValue(nic.Id,out old)&&stats.BytesReceived>=old[0]&&stats.BytesSent>=old[1]){rx+=stats.BytesReceived-old[0];tx+=stats.BytesSent-old[1];matched=true;}
                network[nic.Id]=new[]{stats.BytesReceived,stats.BytesSent};
            }
            foreach(var id in network.Keys.Where(k=>!live.Contains(k)).ToList())network.Remove(id);
            if(matched&&dt>0){s.Down=rx/dt;s.Up=tx/dt;}previousNetworkTime=time;
        }catch{}
        if(tick++%15==0) {var next=new List<DiskSnapshot>();foreach(var d in DriveInfo.GetDrives()){try{if(d.DriveType==DriveType.Fixed&&d.IsReady)next.Add(new DiskSnapshot{Name=d.Name.Substring(0,2),Total=d.TotalSize/1073741824.0,Used=(d.TotalSize-d.TotalFreeSpace)/1073741824.0});}catch{}}disks=next;}
        s.Disks=disks;
        ReadSensor(s);return s;
    }
    static void ReadSensor(HardwareSnapshot s) {
        string file=Path.Combine(Data.Root,"sensors.json");
        try {
            var obj=Data.Parse(File.ReadAllText(file));double? stamp=Data.Num(Data.Get(obj,"timestamp"));
            if(stamp.HasValue && Data.UnixNow-stamp.Value<12 && Data.UnixNow-stamp.Value>=-2) {s.CpuTemp=Data.Num(Data.Get(obj,"cpuTemp"));s.SensorStatus=Data.Str(Data.Get(obj,"status"));return;}
            s.SensorStatus="温度采集已停止，点击连接";
        }catch {s.SensorStatus=LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled?"连接 CPU 温度需要系统授权":"CPU 温度需要安装 PawnIO 传感器驱动";}
    }
}
public static class SensorWorker {
    public static int Run(int parent, bool install) {
        Directory.CreateDirectory(Data.Root);
        try {
            if(install&&!LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled) {
                string installer=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"PawnIO_setup.exe");
                using(var p=Process.Start(new ProcessStartInfo(installer,"-install -silent"){UseShellExecute=false,CreateNoWindow=true})) {if(!p.WaitForExit(120000)){p.Kill();throw new IOException("驱动安装超时");} if(p.ExitCode!=0)throw new IOException("驱动安装未完成："+p.ExitCode);}
                // PawnIO caches its installed version statically. Reopen in a fresh worker after setup.
                using(var worker=Process.Start(new ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location,"--sensors "+parent){UseShellExecute=false,CreateNoWindow=true})) {worker.WaitForExit();return worker.ExitCode;}
            }
            var c=new Computer {IsCpuEnabled=true};c.Open();
            try {
                while(ParentAlive(parent)) {
                    double? value=null;string name="";
                    foreach(var hw in c.Hardware) {hw.Update();foreach(var sub in hw.SubHardware)sub.Update();var temps=hw.Sensors.Where(x=>x.SensorType==SensorType.Temperature&&x.Value.HasValue).ToList();
                        var preferred=temps.FirstOrDefault(x=>x.Name.IndexOf("Tctl",StringComparison.OrdinalIgnoreCase)>=0)??temps.FirstOrDefault(x=>x.Name.IndexOf("Package",StringComparison.OrdinalIgnoreCase)>=0)??temps.OrderByDescending(x=>x.Value).FirstOrDefault();
                        if(preferred!=null){value=preferred.Value;name=preferred.Name;break;}}
                    Data.Write(Path.Combine(Data.Root,"sensors.json"),new {timestamp=Data.UnixNow,cpuTemp=value,sensor=name,status=value.HasValue?"LibreHardwareMonitor · "+name:"CPU 传感器无读数；检查驱动与权限"});Thread.Sleep(2000);
                }
            }finally{c.Close();}
            return 0;
        } catch(Exception e) {Data.Write(Path.Combine(Data.Root,"sensors.json"),new {timestamp=Data.UnixNow,cpuTemp=(double?)null,status="温度连接失败："+e.GetType().Name});return 1;}
    }
    static bool ParentAlive(int pid){try{using(var p=Process.GetProcessById(pid))return !p.HasExited;}catch{return false;}}
}
}
