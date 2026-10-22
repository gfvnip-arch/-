using System;
using System.Runtime.InteropServices;

namespace IPFlyLink {
    public sealed class ProxySnapshot {
        public uint Flags { get; set; }
        public string Server { get; set; }
        public string Bypass { get; set; }
        public string AutoConfig { get; set; }
        public bool Same(ProxySnapshot other) {
            return other != null && Flags == other.Flags && (Server ?? "") == (other.Server ?? "") && (Bypass ?? "") == (other.Bypass ?? "") && (AutoConfig ?? "") == (other.AutoConfig ?? "");
        }
    }
    public static class SystemProxyState {
        [StructLayout(LayoutKind.Explicit, Size = 8)] struct OptionValue { [FieldOffset(0)] public uint Number; [FieldOffset(0)] public IntPtr Text; }
        [StructLayout(LayoutKind.Sequential)] struct Option { public uint Kind; public OptionValue Value; }
        [StructLayout(LayoutKind.Sequential)] struct OptionList { public uint Size; public IntPtr Connection; public uint Count, Error; public IntPtr Options; }
        [DllImport("wininet.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool InternetQueryOption(IntPtr internet, uint option, ref OptionList list, ref uint length);
        [DllImport("wininet.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool InternetSetOption(IntPtr internet, uint option, ref OptionList list, uint length);
        [DllImport("wininet.dll", EntryPoint = "InternetSetOptionW", SetLastError = true)] static extern bool InternetNotify(IntPtr internet, uint option, IntPtr buffer, uint length);
        [DllImport("kernel32.dll")] static extern IntPtr GlobalFree(IntPtr address);
        static readonly uint[] Kinds = {1, 2, 3, 4};
        public static ProxySnapshot Read() {
            int itemSize = Marshal.SizeOf(typeof(Option));
            IntPtr items = Marshal.AllocHGlobal(itemSize * 4);
            try {
                for (int i = 0; i < 4; i++) Marshal.StructureToPtr(new Option {Kind = Kinds[i]}, IntPtr.Add(items, i * itemSize), false);
                var list = new OptionList {Size = (uint)Marshal.SizeOf(typeof(OptionList)), Count = 4, Options = items};
                uint size = list.Size;
                if (!InternetQueryOption(IntPtr.Zero, 75, ref list, ref size)) throw new AppError("无法读取当前 Windows 系统代理，已停止连接。");
                var result = new ProxySnapshot();
                for (int i = 0; i < 4; i++) {
                    var item = (Option)Marshal.PtrToStructure(IntPtr.Add(items, i * itemSize), typeof(Option));
                    if (i == 0) result.Flags = item.Value.Number;
                    else {
                        string value = item.Value.Text == IntPtr.Zero ? "" : Marshal.PtrToStringUni(item.Value.Text);
                        if (i == 1) result.Server = value;
                        if (i == 2) result.Bypass = value;
                        if (i == 3) result.AutoConfig = value;
                        if (item.Value.Text != IntPtr.Zero) GlobalFree(item.Value.Text);
                    }
                }
                return result;
            } finally { Marshal.FreeHGlobal(items); }
        }
        public static ProxySnapshot ForPort(int port) { return new ProxySnapshot {Flags = 3, Server = "127.0.0.1:" + port, Bypass = "<local>;localhost;127.0.0.1;127.*;[::1]", AutoConfig = ""}; }
        public static void Write(ProxySnapshot state) {
            int itemSize = Marshal.SizeOf(typeof(Option));
            IntPtr items = Marshal.AllocHGlobal(itemSize * 4);
            var strings = new IntPtr[3];
            try {
                string[] values = {state.Server ?? "", state.Bypass ?? "", state.AutoConfig ?? ""};
                for (int i = 0; i < 4; i++) {
                    var item = new Option {Kind = Kinds[i]};
                    if (i == 0) item.Value.Number = state.Flags;
                    else { strings[i - 1] = Marshal.StringToHGlobalUni(values[i - 1]); item.Value.Text = strings[i - 1]; }
                    Marshal.StructureToPtr(item, IntPtr.Add(items, i * itemSize), false);
                }
                var list = new OptionList {Size = (uint)Marshal.SizeOf(typeof(OptionList)), Count = 4, Options = items};
                if (!InternetSetOption(IntPtr.Zero, 75, ref list, list.Size)) throw new AppError("Windows 系统代理写入失败，请检查网络设置。");
                InternetNotify(IntPtr.Zero, 39, IntPtr.Zero, 0); InternetNotify(IntPtr.Zero, 37, IntPtr.Zero, 0);
            } finally {
                foreach (var p in strings) if (p != IntPtr.Zero) Marshal.FreeHGlobal(p);
                Marshal.FreeHGlobal(items);
            }
        }
        public static bool RestoreOwned(ProxySnapshot expected, ProxySnapshot previous) {
            return RestoreOwned(expected, previous, Read, Write);
        }
        public static bool RestoreOwned(ProxySnapshot expected, ProxySnapshot previous, Func<ProxySnapshot> read, Action<ProxySnapshot> write) {
            if (!expected.Same(read())) return false;
            write(previous);
            if (!previous.Same(read())) throw new AppError("系统代理恢复后核对未通过，请检查 Windows 代理设置。");
            return true;
        }
    }
}
