using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;

namespace IPFlyLink {
    public sealed class Packet {
        public string Operation { get; set; }
        public string Data { get; set; }
        public string Kind { get; set; }
        public string Message { get; set; }
        public string Ip { get; set; }
        public bool Success { get; set; }
        public bool Connected { get; set; }
    }
    public static class PipeMessages {
        public static void Write(Stream stream, Packet packet) {
            byte[] bytes = Encoding.UTF8.GetBytes(Json.Encode(packet));
            if (bytes.Length > 65536) throw new IOException("消息超长");
            byte[] size = BitConverter.GetBytes(bytes.Length);
            stream.Write(size, 0, size.Length); stream.Write(bytes, 0, bytes.Length); stream.Flush();
        }
        static byte[] Exact(Stream stream, int length) {
            byte[] data = new byte[length]; int offset = 0;
            while (offset < length) { int read = stream.Read(data, offset, length - offset); if (read == 0) throw new EndOfStreamException(); offset += read; }
            return data;
        }
        public static Packet Read(Stream stream) {
            int length = BitConverter.ToInt32(Exact(stream, 4), 0);
            if (length < 2 || length > 65536) throw new IOException("消息长度无效");
            return Json.Decode<Packet>(Encoding.UTF8.GetString(Exact(stream, length)));
        }
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetNamedPipeClientProcessId(IntPtr pipe, out uint pid);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetNamedPipeServerProcessId(IntPtr pipe, out uint pid);
        public static bool ClientIs(PipeStream stream, int expected) { uint pid; return GetNamedPipeClientProcessId(stream.SafePipeHandle.DangerousGetHandle(), out pid) && pid == expected; }
        public static bool ServerIs(PipeStream stream, int expected) { uint pid; return GetNamedPipeServerProcessId(stream.SafePipeHandle.DangerousGetHandle(), out pid) && pid == expected; }
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool PeekNamedPipe(IntPtr pipe, IntPtr buffer, uint size, IntPtr read, IntPtr available, IntPtr remaining);
        public static bool PeerAlive(PipeStream stream) { try {return PeekNamedPipe(stream.SafePipeHandle.DangerousGetHandle(), IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);} catch {return false;} }
    }
    public sealed class WorkerClient : IDisposable {
        NamedPipeServerStream pipe;
        Process worker;
        readonly object gate = new object();
        public bool Connected { get {var channel = pipe; return channel != null && channel.IsConnected;} }
        public void Start() {
            string name = "IPFlyLink-" + Guid.NewGuid().ToString("N");
            var sid = WindowsIdentity.GetCurrent().User;
            var security = new PipeSecurity();
            security.SetAccessRuleProtection(true, false); security.SetOwner(sid);
            security.AddAccessRule(new PipeAccessRule(sid, PipeAccessRights.FullControl, AccessControlType.Allow));
            pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 65536, 65536, security);
            string args;
            using (var self = Process.GetCurrentProcess()) args = "--worker " + name + " " + self.Id + " " + self.StartTime.ToUniversalTime().Ticks + " " + sid.Value;
            try {
                worker = Process.Start(new ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location, args) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden });
                if (worker == null) throw new AppError("管理员工作进程未启动。");
                var waiting = Task.Factory.FromAsync(pipe.BeginWaitForConnection, pipe.EndWaitForConnection, null);
                if (!waiting.Wait(30000) || !PipeMessages.ClientIs(pipe, worker.Id)) throw new AppError("无法确认管理员工作进程身份。");
            } catch (System.ComponentModel.Win32Exception e) {
                Dispose();
                if (e.NativeErrorCode == 1223) throw new AppError("已取消管理员授权，网络设置未更改。");
                throw new AppError("管理员工作进程启动失败。");
            } catch { Dispose(); throw; }
        }
        public Packet Call(string operation, string data, Action<string> progress) {
            lock (gate) {
                var channel = pipe;
                if (channel == null || !channel.IsConnected) throw new AppError("与连接工作进程的通信已中断。");
                PipeMessages.Write(channel, new Packet {Operation = operation, Data = data});
                for (;;) {
                    var answer = PipeMessages.Read(channel);
                    if (answer.Kind == "progress") { if (progress != null) progress(answer.Message); continue; }
                    return answer;
                }
            }
        }
        public void Abort() {
            var old = pipe; pipe = null;
            if (old != null) old.Dispose();
        }
        public bool WaitForCleanup() { try {return worker == null || worker.HasExited || worker.WaitForExit(20000);} catch {return false;} }
        public void Dispose() {
            Abort();
            if (worker != null) { worker.Dispose(); worker = null; }
        }
    }
}
