using System;
using System.Diagnostics;
using System.IO;

namespace IPFlyLink {
    public sealed class RecoveryRecord {
        public bool Active { get; set; }
        public int WorkerId { get; set; }
        public long WorkerStart { get; set; }
        public ProxySnapshot Previous { get; set; }
        public ProxySnapshot Expected { get; set; }
    }
    public static class RecoveryJournal {
        public static void Save(RecoveryRecord state) { ProfileStore.WriteEncrypted("recovery.bin", state); }
        public static void EnsureReady() {
            if (!File.Exists(Path.Combine(ProfileStore.DirectoryPath, "recovery.bin"))) return;
            RecoveryRecord record;
            try { record = ProfileStore.ReadEncrypted<RecoveryRecord>("recovery.bin"); }
            catch { throw new AppError("无法读取上次恢复记录，连接已停止，请先检查 Windows 系统代理。"); }
            if (!record.Active) return;
            Recover();
            try { record = ProfileStore.ReadEncrypted<RecoveryRecord>("recovery.bin"); }
            catch { throw new AppError("恢复记录仍不可读，连接已停止。"); }
            if (record.Active) throw new AppError("上次连接尚未完成清理或恢复，请稍后重新打开软件检查。");
        }
        public static string Recover() {
            if (!File.Exists(Path.Combine(ProfileStore.DirectoryPath, "recovery.bin"))) return "";
            RecoveryRecord record;
            try { record = ProfileStore.ReadEncrypted<RecoveryRecord>("recovery.bin"); }
            catch { return "恢复记录无法读取。请先检查 Windows 代理设置，再尝试连接。"; }
            if (!record.Active) return "";
            try {
                using (var prior = Process.GetProcessById(record.WorkerId))
                    if (!prior.HasExited && prior.StartTime.ToUniversalTime().Ticks == record.WorkerStart) return "上次连接仍在清理，请稍后重新打开软件。";
            } catch (ArgumentException) { }
            catch { return "无法核对上次工作进程，未更改现有网络设置。"; }
            try {
                var current = SystemProxyState.Read();
                if (record.Previous.Same(current)) {record.Active = false; Save(record); return "上次未完成连接的系统代理已恢复。";}
                if (!SystemProxyState.RestoreOwned(record.Expected, record.Previous)) {record.Active = false; Save(record); return "检测到上次连接记录，但系统代理已被其他操作更改，未覆盖新设置。";}
                record.Active = false; Save(record);
                return "已恢复上次异常退出前的系统代理。";
            } catch { return "上次系统代理未能自动恢复，请检查 Windows 代理设置中的本机代理项。"; }
        }
    }
}
