using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IPFlyLink {
    public sealed class CoreRunner : IDisposable {
        const string ExpectedHash = "5c19bb3f8c12d4e85b3d47fe63123a7624b314b58dd7a8e32f51004f1c4d2faf";
        Process child;
        volatile bool fullyStarted;
        static readonly object ConsoleCleanupGate = new object();
        IntPtr job;
        [StructLayout(LayoutKind.Sequential)] struct BasicLimits { public long UserTime, JobTime; public uint Flags; public UIntPtr MinWorking, MaxWorking; public uint ActiveCount; public UIntPtr Affinity; public uint Priority, Scheduling; }
        [StructLayout(LayoutKind.Sequential)] struct IoCounters { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
        [StructLayout(LayoutKind.Sequential)] struct ExtendedLimits { public BasicLimits Basic; public IoCounters Io; public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory; }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr attributes, string name);
        [DllImport("kernel32.dll")] static extern bool SetInformationJobObject(IntPtr job, int info, ref ExtendedLimits limits, uint length);
        [DllImport("kernel32.dll")] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll")] static extern bool AttachConsole(uint process);
        [DllImport("kernel32.dll")] static extern bool FreeConsole();
        [DllImport("kernel32.dll")] static extern uint GetConsoleProcessList([Out] uint[] processes, uint count);
        [DllImport("kernel32.dll")] static extern bool SetConsoleCtrlHandler(IntPtr handler, bool add);
        [DllImport("kernel32.dll")] static extern bool GenerateConsoleCtrlEvent(uint control, uint group);
        public bool Alive { get { try { return child != null && !child.HasExited; } catch { return false; } } }
        public int ProcessId { get { return child == null ? 0 : child.Id; } }
        public static string Executable { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "core", "sing-box.exe"); } }
        static void VerifyFile() {
            if (!File.Exists(Executable)) throw new AppError("缺少随附代理内核，请重新解压完整交付目录。");
            using (var stream = File.OpenRead(Executable)) using (var hash = SHA256.Create()) {
                if (BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant() != ExpectedHash) throw new AppError("代理内核校验失败，已停止启动。");
            }
        }
        public static bool Check(string config, CancellationToken cancellation = default(CancellationToken)) {
            cancellation.ThrowIfCancellationRequested();
            VerifyFile();
            var info = StartInfo("check -c stdin"); info.CreateNoWindow = true;
            using (var process = Process.Start(info)) {
                process.OutputDataReceived += delegate { }; process.ErrorDataReceived += delegate { };
                process.BeginOutputReadLine(); process.BeginErrorReadLine();
                WriteConfig(process, config);
                var elapsed=Stopwatch.StartNew();
                while(!process.WaitForExit(100)) {
                    if(cancellation.IsCancellationRequested || elapsed.ElapsedMilliseconds>=15000) {
                        process.Kill();process.WaitForExit(3000);cancellation.ThrowIfCancellationRequested();
                        throw new AppError("内核配置检查超时。");
                    }
                }
                return process.ExitCode == 0;
            }
        }
        static ProcessStartInfo StartInfo(string arguments) {
            return new ProcessStartInfo(Executable, arguments) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(Executable), WindowStyle = ProcessWindowStyle.Hidden, CreateNoWindow = true };
        }
        static void WriteConfig(Process process, string config) {
            byte[] bytes = Encoding.UTF8.GetBytes(config);
            try { process.StandardInput.BaseStream.Write(bytes, 0, bytes.Length); process.StandardInput.BaseStream.Flush(); }
            finally { Array.Clear(bytes, 0, bytes.Length); process.StandardInput.Close(); }
        }
        public void Start(string config, int port, CancellationToken cancellation = default(CancellationToken)) {
            cancellation.ThrowIfCancellationRequested();
            // The mixed listener starts before TUN's PostStart phase. A listening
            // port alone must never release traffic into an unfinished tunnel.
            var options=Json.Decode<System.Collections.Generic.Dictionary<string,object>>(config);
            bool tun=false;
            foreach(System.Collections.Generic.Dictionary<string,object> inbound in (System.Collections.IList)options["inbounds"])
                if((string)inbound["type"]=="tun")tun=true;
            options["log"]=Json.Obj("level","info","timestamp",false,"disabled",false);
            config=Json.Encode(options);fullyStarted=false;
            if (!Check(config,cancellation)) throw new AppError("内核配置检查未通过，尚未更改系统代理。");
            cancellation.ThrowIfCancellationRequested();
            job = CreateJobObject(IntPtr.Zero, null);
            var limits = new ExtendedLimits(); limits.Basic.Flags = 0x2000;
            if (job == IntPtr.Zero || !SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf(typeof(ExtendedLimits)))) throw new AppError("无法建立内核进程保护。");
            child = Process.Start(StartInfo("run -c stdin"));
            if (!AssignProcessToJobObject(job, child.Handle)) { child.Kill(); throw new AppError("无法关联内核进程保护，已取消连接。"); }
            child.OutputDataReceived += ObserveStartup; child.ErrorDataReceived += ObserveStartup;
            child.BeginOutputReadLine(); child.BeginErrorReadLine();
            WriteConfig(child, config);
            try {
                CoreReadiness.Wait(delegate{return Alive;},delegate{return NanoProbe.OwnsPort(child.Id,port);},delegate{return fullyStarted;},tun?(Func<string>)CoreReadiness.TunStatus:null,cancellation);
            } catch(AppError error) {
                if(tun)GlobalCheck.SaveReport("启动检查 0.1.9："+error.Message+Environment.NewLine+"内核完整启动事件="+fullyStarted+"；"+CoreReadiness.TunStatus());
                throw;
            }
        }
        void ObserveStartup(object sender,DataReceivedEventArgs e) {
            // Discard raw log lines: only this fixed lifecycle event is retained.
            if(!fullyStarted&&e.Data!=null&&System.Text.RegularExpressions.Regex.IsMatch(e.Data,@"\bsing-box started \([0-9.]+s\)(?:\x1B\[[0-9;]*m)?$"))fullyStarted=true;
        }
        public void Dispose() {
            lock(ConsoleCleanupGate) DisposeCore();
        }
        void DisposeCore() {
            try {
                if (Alive) {
                    // Some hosts supply a hidden console even with CREATE_NO_WINDOW.
                    // Signal only a console containing this worker and its own core.
                    if (AttachConsole((uint)child.Id)) {
                        try {
                            var peers = new uint[3]; uint count = GetConsoleProcessList(peers, 3);
                            using (var self = Process.GetCurrentProcess()) {
                                bool owned = count > 0 && count <= 2;
                                for (int i = 0; i < count && i < peers.Length; i++) owned &= peers[i] == self.Id || peers[i] == child.Id;
                                if (owned) { SetConsoleCtrlHandler(IntPtr.Zero, true); GenerateConsoleCtrlEvent(0, 0); child.WaitForExit(5000); }
                            }
                        } finally { FreeConsole(); SetConsoleCtrlHandler(IntPtr.Zero, false); }
                    }
                    if (Alive) child.Kill();
                    child.WaitForExit(3000);
                }
            } finally {
                if (job != IntPtr.Zero) { CloseHandle(job); job = IntPtr.Zero; }
                if (child != null) { child.Dispose(); child = null; }
            }
        }
    }
    public static class Egress {
        [ThreadStatic] internal static System.Collections.Generic.List<string> AttemptTrace;
        internal static string WithServiceStatus(Func<string> exit, Action service) {
            string ipState="尚未完成",serviceState="尚未完成";var gate=new object();
            try {
                return WithServiceCheck(delegate {
                    try {string value=exit();lock(gate)ipState="已通过";return value;}
                    catch(OperationCanceledException){lock(gate)ipState="超时或取消";throw;}
                    catch(Exception e){lock(gate)ipState=e is AppError?e.Message:"请求失败";throw;}
                },delegate {
                    try{service();lock(gate)serviceState="已通过";}
                    catch(OperationCanceledException){lock(gate)serviceState="超时或取消";throw;}
                    catch(Exception e){lock(gate)serviceState=e is AppError?e.Message:"请求失败";throw;}
                });
            } catch {lock(gate)throw new AppError("出口 IP："+ipState+"；Codex 上游："+serviceState+"。");}
        }
        internal static string WithServiceCheck(Func<string> exit, Action service) {
            var ip=Task.Run(exit);var target=Task.Run(service);
            Task.WhenAll(ip,target).GetAwaiter().GetResult();return ip.Result;
        }
        public static void CheckCodex(int port, bool useProxy, CancellationToken cancellation = default(CancellationToken)) {
            cancellation.ThrowIfCancellationRequested();
            ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
            var request=(HttpWebRequest)WebRequest.Create("https://chatgpt.com/backend-api/codex/responses");
            request.Proxy=useProxy?new WebProxy("http://127.0.0.1:"+port):null;
            request.Timeout=12000;request.ReadWriteTimeout=12000;request.KeepAlive=false;request.AllowAutoRedirect=false;
            try {using(cancellation.Register(request.Abort))using(var response=(HttpWebResponse)request.GetResponse()) {
                if(!CodexReachableStatus((int)response.StatusCode))throw new AppError("Codex 上游探测返回 HTTP "+(int)response.StatusCode+"，未确认可达。");
            }}catch(WebException error){
                cancellation.ThrowIfCancellationRequested();
                using(var response=error.Response as HttpWebResponse){
                    if(response!=null&&CodexReachableStatus((int)response.StatusCode))return;
                    throw new AppError("Codex 上游连接未通过："+(response==null?HealthCheck.SafeError(error):"HTTP "+(int)response.StatusCode)+"。");
                }
            }
        }
        internal static bool CodexReachableStatus(int status){return status==401||status==405;}
        internal static string CheckPair(int port, CancellationToken cancellation, Func<int, bool, CancellationToken, string> check) {
            cancellation.ThrowIfCancellationRequested();
            var chained = Task.Run(delegate { return check(port, true, cancellation); });
            var global = Task.Run(delegate { return check(port, false, cancellation); });
            Task.WhenAll(chained, global).GetAwaiter().GetResult();
            cancellation.ThrowIfCancellationRequested();
            if (chained.Result != global.Result) throw new AppError("代理链与全局出口不一致；请检查其他代理设置，或 IPFLY 是否按请求更换出口。");
            return global.Result;
        }
        public static string Check(int proxyPort, bool useProxy, CancellationToken cancellation = default(CancellationToken)) {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            string reason = "检测站未返回有效 IP";
            var attempts = new System.Collections.Generic.List<string>(); AttemptTrace=attempts;
            foreach (string target in new[] {"https://api.ipify.org/", "https://checkip.amazonaws.com/"}) {
                cancellation.ThrowIfCancellationRequested();
                var watch=Stopwatch.StartNew();
                reason="检测站未返回有效 IP";
                try {
                    var request = (HttpWebRequest)WebRequest.Create(target);
                    request.Proxy = useProxy ? new WebProxy("http://127.0.0.1:" + proxyPort) : null;
                    request.Timeout = 12000; request.ReadWriteTimeout = 12000; request.KeepAlive = false; request.AllowAutoRedirect = false;
                    using (cancellation.Register(request.Abort)) using (var response = request.GetResponse()) using (var stream = response.GetResponseStream()) {
                        byte[] data = new byte[128]; int length = 0, read;
                        while (length < data.Length && (read = stream.Read(data, length, data.Length - length)) > 0) length += read;
                        IPAddress ip; string value = Encoding.ASCII.GetString(data, 0, length).Trim();
                        if (IPAddress.TryParse(value, out ip)) {attempts.Add(new Uri(target).Host+": 通过（"+watch.ElapsedMilliseconds+"ms）");return ip.ToString();}
                    }
                } catch (WebException error) {
                    cancellation.ThrowIfCancellationRequested();
                    reason = HealthCheck.SafeError(error);
                    if(error.Response!=null)error.Response.Close();
                } catch (Exception error) { cancellation.ThrowIfCancellationRequested(); reason = HealthCheck.SafeError(error); }
                attempts.Add(new Uri(target).Host+": "+reason+"（"+watch.ElapsedMilliseconds+"ms）");
            }
            throw new AppError((useProxy ? "代理链" : "全局通道") + "出口检测未通过：" + String.Join("；",attempts) + "。");
        }
    }
}
