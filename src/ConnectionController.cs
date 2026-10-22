using System;
using System.Threading.Tasks;

namespace IPFlyLink {
    public sealed class ConnectionController : IDisposable {
        WorkerClient client;
        volatile bool cancelled;
        public Task<Packet> ConnectAsync(ProxyProfile profile, Action<string> progress) {
            cancelled = false;
            return Task.Run(delegate {
                profile.Parse(); ProfileStore.Save(profile);
                progress("正在检查 Nano…"); NanoProbe.Detect(profile);
                if (cancelled) throw new AppError("已取消连接。");
                progress("即将请求管理员授权，用于启用全局接管…");
                var worker = new WorkerClient(); client = worker;
                try {
                    worker.Start();
                    if (cancelled) { worker.Dispose(); throw new AppError("已取消连接。"); }
                    var answer = worker.Call("Connect", ProfileStore.Protect(profile), progress);
                    if (!answer.Connected) {
                        try {worker.Call("Exit", null, null);} catch { }
                        worker.Abort(); worker.WaitForCleanup(); worker.Dispose(); if (client == worker) client = null;
                    }
                    return answer;
                } catch {
                    worker.Abort(); bool clean = worker.WaitForCleanup(); worker.Dispose(); if (client == worker) client = null;
                    if (!clean) throw new AppError("连接工作进程仍在清理，请稍后重新打开软件检查恢复记录。");
                    if (cancelled) {RecoveryJournal.EnsureReady(); throw new AppError("已取消连接，并完成工作进程清理。");}
                    throw;
                }
            });
        }
        public Task<Packet> StatusAsync() {
            return Task.Run(delegate {
                if (client == null) return new Packet {Success = false, Connected = false, Message = "连接工作进程已退出。"};
                var worker = client;
                try {
                    var answer = worker.Call("Status", null, null);
                    if (!answer.Connected) {
                        try {worker.Call("Exit", null, null);} catch { }
                        worker.Abort();
                        if (!worker.WaitForCleanup()) throw new AppError("工作进程仍在清理，请稍后检查网络恢复记录。");
                        worker.Dispose(); if (client == worker) client = null;
                        RecoveryJournal.EnsureReady();
                    }
                    return answer;
                } catch {
                    worker.Abort(); bool clean = worker.WaitForCleanup(); worker.Dispose(); if (client == worker) client = null;
                    if (!clean) throw new AppError("连接工作进程仍在清理，请稍后重新打开软件检查恢复记录。");
                    RecoveryJournal.EnsureReady();
                    throw;
                }
            });
        }
        public Task<Packet> DisconnectAsync() {
            return Task.Run(delegate {
                var worker = client;
                if (worker == null) return new Packet {Success = true, Message = "未连接"};
                try {
                    Packet answer = null;
                    if (worker.Connected) answer = worker.Call("Exit", null, null);
                    worker.Abort();
                    if (!worker.WaitForCleanup()) throw new AppError("工作进程仍在清理，请稍后检查网络恢复记录。");
                    RecoveryJournal.EnsureReady();
                    return answer ?? new Packet {Success = true, Connected = false, Message = "已断开并完成清理。"};
                } finally { worker.Dispose(); if (client == worker) client = null; }
            });
        }
        public void Cancel() { cancelled = true; var worker = client; if (worker != null) worker.Abort(); }
        public void Dispose() { Cancel(); var worker = client; if (worker != null) worker.Dispose(); client = null; }
    }
}
