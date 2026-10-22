using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace IPFlyLink {
    public sealed class NanoEndpoint {
        public int Port;
        public string Protocol, ProcessPath;
    }
    public static class NanoProbe {
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)] static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref int size);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        public static string TryGetProcessPath(int pid) {
            if (pid <= 0) return null;
            IntPtr handle = OpenProcess(0x1000, false, pid);
            if (handle == IntPtr.Zero) return null;
            try {
                int size = 32768; var path = new StringBuilder(size);
                return QueryFullProcessImageName(handle, 0, path, ref size) ? path.ToString() : null;
            } finally { CloseHandle(handle); }
        }
        [DllImport("iphlpapi.dll", SetLastError = true)] static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int family, int tableClass, uint reserved);
        public static bool OwnsPort(int pid, int port) {
            int length = 0; GetExtendedTcpTable(IntPtr.Zero, ref length, false, 2, 3, 0);
            if (length < 4 || length > 16 * 1024 * 1024) return false;
            IntPtr data = Marshal.AllocHGlobal(length);
            try {
                if (GetExtendedTcpTable(data, ref length, false, 2, 3, 0) != 0) return false;
                int count = Marshal.ReadInt32(data);
                for (int i = 0; i < count && 4 + (i + 1) * 24 <= length; i++) {
                    int offset = 4 + i * 24;
                    int localPort = ((Marshal.ReadByte(data, offset + 8) << 8) | Marshal.ReadByte(data, offset + 9));
                    uint localAddress = unchecked((uint)Marshal.ReadInt32(data, offset + 4));
                    if (Marshal.ReadInt32(data, offset) == 2 && localPort == port && Marshal.ReadInt32(data, offset + 20) == pid && (localAddress == 0 || localAddress == 0x0100007F)) return true;
                }
                return false;
            } finally { Marshal.FreeHGlobal(data); }
        }
        public static TcpClient Open(int port, int timeout) {
            var socket = new TcpClient(AddressFamily.InterNetwork);
            try {
                var task = socket.ConnectAsync(IPAddress.Loopback, port);
                if (!task.Wait(timeout)) throw new TimeoutException();
                socket.ReceiveTimeout = timeout; socket.SendTimeout = timeout;
                return socket;
            } catch { socket.Close(); throw; }
        }
        public static string Handshake(int port) {
            try {
                using (var socket = Open(port, 2000)) {
                    var stream = socket.GetStream(); stream.Write(new byte[] {5, 1, 0}, 0, 3);
                    if (stream.ReadByte() == 5 && stream.ReadByte() == 0) return "socks";
                }
            } catch { }
            try {
                using (var socket = Open(port, 5000)) {
                    var stream = socket.GetStream(); byte[] data = Encoding.ASCII.GetBytes("CONNECT 1.1.1.1:443 HTTP/1.1\r\nHost: 1.1.1.1:443\r\n\r\n");
                    stream.Write(data, 0, data.Length);
                    var line = new StringBuilder();
                    for (int i = 0; i < 256; i++) { int c = stream.ReadByte(); if (c < 0 || c == 10) break; line.Append((char)c); }
                    if (line.ToString().StartsWith("HTTP/1.", StringComparison.Ordinal) && line.ToString().Contains(" 200 ")) return "http";
                }
            } catch { }
            throw new AppError("魔法本地端口未通过 HTTP/SOCKS5 握手。请确认已连接、端口正确且本地代理无需认证。");
        }
        public static int ListenerProcess(int port) {
            int length=0; GetExtendedTcpTable(IntPtr.Zero,ref length,false,2,3,0);
            if(length<4||length>16*1024*1024) return 0;
            IntPtr data=Marshal.AllocHGlobal(length);
            try {
                if(GetExtendedTcpTable(data,ref length,false,2,3,0)!=0)return 0;
                int count=Marshal.ReadInt32(data), found=0;
                for(int i=0;i<count&&4+(i+1)*24<=length;i++) {
                    int offset=4+i*24;
                    int localPort=(Marshal.ReadByte(data,offset+8)<<8)|Marshal.ReadByte(data,offset+9);
                    uint address=unchecked((uint)Marshal.ReadInt32(data,offset+4));
                    if(Marshal.ReadInt32(data,offset)==2&&localPort==port&&(address==0||address==0x0100007F)) {
                        int pid=Marshal.ReadInt32(data,offset+20);
                        if(found!=0&&found!=pid)throw new AppError("魔法端口有多个监听进程，请检查本地代理设置。");
                        found=pid;
                    }
                }
                return found;
            } finally {Marshal.FreeHGlobal(data);}
        }
        // Retained for historical developer diagnostics; production supplies the chosen port.
        public static Process FindNanoProcess() {
            int pid=ListenerProcess(65532);
            if(pid==0)throw new AppError("默认魔法端口未监听。");
            return Process.GetProcessById(pid);
        }
        public static NanoEndpoint Detect() {return Detect(new ProxyProfile());}
        public static NanoEndpoint Detect(ProxyProfile profile) {
            int port=profile.UpstreamPort;
            if(port<1||port>65535)throw new AppError("魔法本地端口无效。");
            int pid=ListenerProcess(port);
            if(pid==0)throw new AppError("127.0.0.1:"+port+" 没有可用的魔法入口。请先连接魔法，并在“魔法设置”填写其本地 HTTP / SOCKS5 / Mixed 端口。");
            string path=TryGetProcessPath(pid);
            if(String.IsNullOrEmpty(path))throw new AppError("无法核对魔法端口所属程序，请检查魔法是否正在运行及双方运行权限。");
            if(String.Equals(path,CoreRunner.Executable,StringComparison.OrdinalIgnoreCase)||String.Equals(path,typeof(NanoProbe).Assembly.Location,StringComparison.OrdinalIgnoreCase))
                throw new AppError("不能把小助手自己的端口填作魔法入口。请填写前置魔法程序的本地端口。");
            string protocol=Handshake(port);
            if(!OwnsPort(pid,port)||!String.Equals(path,TryGetProcessPath(pid),StringComparison.OrdinalIgnoreCase))
                throw new AppError("魔法进程在检查过程中发生变化，请稳定连接后重试。");
            return new NanoEndpoint {Port=port,Protocol=protocol,ProcessPath=path};
        }
    }
}
