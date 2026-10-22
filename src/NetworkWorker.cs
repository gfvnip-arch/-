using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace IPFlyLink {
    public sealed class ConnectionService : IDisposable {
        readonly Func<int, bool, CancellationToken, string> checkEgress;
        readonly Action<int, bool, CancellationToken> checkCodex;
        readonly bool diagnostics;
        public ConnectionService() : this(Egress.Check, Egress.CheckCodex) { diagnostics=true; }
        internal ConnectionService(Func<int, bool, CancellationToken, string> check, Action<int, bool, CancellationToken> codex = null) { checkEgress = check; checkCodex = codex ?? delegate {}; }
        CoreRunner core;
        RecoveryRecord recovery;
        int port;
        string ip;
        bool connected;
        int failures;
        int unverified;
        public Packet Connect(ProxyProfile profile, Action<string> progress, CancellationToken cancellation) {
            cancellation.ThrowIfCancellationRequested();
            if (connected) throw new AppError("已有连接，请先断开。");
            progress("正在检查 Nano 本地入口…");
            RecoveryJournal.EnsureReady();
            ProxyEndpoint proxy = profile.Parse(); NanoEndpoint nano = NanoProbe.Detect(profile);
            port = CoreConfig.FreePort();
            progress("正在测试 Nano → IPFLY 代理链…");
            proxy = EndpointSelection.ChooseBounded(proxy, EndpointSelection.Resolve(proxy, cancellation), delegate(ProxyEndpoint candidate, CancellationToken attempt) {
                int testPort = CoreConfig.FreePort();
                using (var test = new CoreRunner()) {
                    test.Start(CoreConfig.Create(candidate, nano, testPort, false), testPort, attempt);
                    Egress.WithServiceStatus(delegate {return checkEgress(testPort, true, attempt);}, delegate {checkCodex(testPort, true, attempt);});
                }
            }, progress, cancellation);
            progress("代理链已通过，正在启用全局接管…");
            cancellation.ThrowIfCancellationRequested();
            using (var self = Process.GetCurrentProcess()) recovery = new RecoveryRecord {Active = true, WorkerId = self.Id, WorkerStart = self.StartTime.ToUniversalTime().Ticks, Previous = SystemProxyState.Read(), Expected = SystemProxyState.ForPort(port)};
            RecoveryJournal.Save(recovery);
            core = new CoreRunner();
            core.Start(CoreConfig.Create(proxy, nano, port, true), port, cancellation);
            cancellation.ThrowIfCancellationRequested();
            SystemProxyState.Write(recovery.Expected);
            if (!recovery.Expected.Same(SystemProxyState.Read())) throw new AppError("Windows 系统代理未通过核对。");
            progress("正在核对全局出口…");
            using(var probe=diagnostics?new PathDiagnostics(port,cancellation):null) {
                bool reported=false;
                try {
                    ip = GlobalCheck.Run(port,checkEgress,checkCodex,cancellation,delegate(string detail){reported=true;GlobalCheck.SaveReport(detail);},12000,probe==null?(Func<string>)null:probe.Snapshot);
                } finally {
                    if(probe!=null&&!reported)GlobalCheck.SaveReport("全局检查被取消或中断。"+Environment.NewLine+probe.Snapshot());
                }
            }
            connected = true; failures = 0; unverified=0;
            return new Packet {Kind = "done", Success = true, Connected = true, Ip = ip, Message = "全局已连接。请重新打开 Codex，使用新的连接验证出口。"};
        }
        public Packet Status(CancellationToken cancellation) {
            if (!connected || core == null) return new Packet {Kind = "done", Success = true, Connected = false, Message = "未连接"};
            if (!core.Alive) {
                string note = Disconnect();
                return new Packet {Kind = "done", Success = false, Connected = false, Message = "代理内核已退出，连接已断开。" + note};
            }
            if (!recovery.Expected.Same(SystemProxyState.Read())) {
                string note = Disconnect();
                return new Packet {Kind = "done", Success = false, Connected = false, Message = "Windows 代理已被其他操作更改，已停止本工具接管。" + note};
            }
            return CheckEgressStatus(cancellation);
        }
        internal Packet CheckEgressStatus(CancellationToken cancellation) {
            var check = HealthCheck.Run(port, checkEgress, checkCodex, cancellation);
            bool changed=check.Verified&&ip!=check.Values[1];
            string decision;
            bool hard = check.Mismatch || check.Unreachable;
            if (check.Verified) { ip=check.Values[1]; unverified=0; if(!hard)failures=0; }
            else unverified++;
            if(hard) failures++; else failures=0;
            if(failures>=3 || unverified>=10) {
                decision = check.Mismatch ? "代理链与全局出口不一致，连续复核失败，已断开并执行恢复。" :
                    failures>=3 ? "通道连续三轮缺少可达证据，已断开并执行恢复。" : "连续十轮无法确认出口一致性，已断开并执行恢复。";
                string note=Disconnect();
                if(diagnostics)HealthCheck.Save(check.Detail+Environment.NewLine+decision+note);
                return new Packet {Kind="done",Success=false,Connected=false,Message=decision+Environment.NewLine+check.Detail+note};
            }
            bool healthy=check.Verified&&check.Passed[2]&&check.Passed[3];
            decision=healthy ? (changed?"出口地址已更新，代理链与全局出口核对一致。":"全局已连接") : hard ? "通道复核未通过，正在复查（"+failures+"/3）…" :
                check.Verified ? "出口一致，服务探测暂未通过；连接保留，继续复查。" :
                "交叉复核有可达证据，连接保留；出口一致性待复查（"+unverified+"/10）。";
            if(diagnostics&&!healthy)HealthCheck.Save(check.Detail+Environment.NewLine+decision);
            return new Packet {Kind="done",Success=healthy,Connected=true,Ip=ip,Message=healthy?decision:decision+Environment.NewLine+check.Detail};
        }
        public string Disconnect() {
            connected = false; failures=0; unverified=0; string warning = "";
            try {
                if (recovery != null && recovery.Active) {
                    var current = SystemProxyState.Read();
                    if (!recovery.Previous.Same(current) && !SystemProxyState.RestoreOwned(recovery.Expected, recovery.Previous)) warning = "系统代理已被其他操作更改，未覆盖新设置。";
                    recovery.Active = false; RecoveryJournal.Save(recovery);
                }
            } catch { warning = "系统代理未能自动恢复，请检查 Windows 代理设置；加密恢复记录已保留。"; }
            finally {
                if (core != null) { try { core.Dispose(); } catch { warning += "内核清理未完成，请检查 IPFlyLink 虚拟网卡。"; } core = null; }
            }
            return warning;
        }
        public void Dispose() { Disconnect(); }
    }
    public static class NetworkWorker {
        public static int Run(string[] args) {
            if (args.Length != 5) return 2;
            int parentId; long parentStart;
            if (!Int32.TryParse(args[2], out parentId) || !Int64.TryParse(args[3], out parentStart)) return 2;
            if (WindowsIdentity.GetCurrent().User.Value != args[4] || !new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)) return 3;
            try {
                using (var parent = Process.GetProcessById(parentId)) {
                    if (parent.StartTime.ToUniversalTime().Ticks != parentStart) return 4;
                    using (var pipe = new NamedPipeClientStream(".", args[1], PipeDirection.InOut, PipeOptions.Asynchronous)) {
                        pipe.Connect(15000);
                        if (!PipeMessages.ServerIs(pipe, parentId)) return 4;
                        using (var service = new ConnectionService()) using (var lifetime = new CancellationTokenSource()) {
                            var monitor = Task.Factory.StartNew(delegate {
                                while (!lifetime.Token.WaitHandle.WaitOne(100)) {
                                    if (parent.HasExited || !PipeMessages.PeerAlive(pipe)) {lifetime.Cancel(); return;}
                                }
                            }, TaskCreationOptions.LongRunning);
                            try {
                            for (;;) {
                                if (parent.HasExited) return 0;
                                Packet request = PipeMessages.Read(pipe), answer;
                                try {
                                    if (request.Operation == "Connect") {
                                        var profile = ProfileStore.Unprotect<ProxyProfile>(request.Data);
                                        answer = service.Connect(profile, delegate(string message) {
                                            if (parent.HasExited) throw new EndOfStreamException();
                                            PipeMessages.Write(pipe, new Packet {Kind = "progress", Message = message});
                                        }, lifetime.Token);
                                    } else if (request.Operation == "Status") answer = service.Status(lifetime.Token);
                                    else if (request.Operation == "Disconnect" || request.Operation == "Exit") answer = new Packet {Kind = "done", Success = true, Connected = false, Message = "已断开。" + service.Disconnect()};
                                    else throw new AppError("未知操作。");
                                } catch (Exception error) {
                                    string note = service.Disconnect();
                                    answer = new Packet {Kind = "done", Success = false, Connected = false, Message = (error is AppError ? error.Message : "连接未完成，请检查 Nano、本地权限和代理参数。") + note};
                                }
                                PipeMessages.Write(pipe, answer);
                                if (request.Operation == "Exit") return 0;
                            }
                            } finally {lifetime.Cancel(); monitor.Wait(2000);}
                        }
                    }
                }
            } catch { return 5; }
        }
    }
}
