using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace IPFlyLink {
    // Presentation only: never translate credentials, addresses or worker packets.
    internal sealed class UiLanguage {
        internal bool English;
        bool updating;
        readonly Dictionary<Control,string> originals=new Dictionary<Control,string>();
        static string SettingPath {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"IPFlyLink","language.txt");}}
        internal static bool SavedEnglish {get{try{return File.ReadAllText(SettingPath).Trim()=="en";}catch{return false;}}}
        internal void Attach(Control c) {
            if(c is Label || c is Button || c is Form || (c is TextBox && ((TextBox)c).Multiline)) {
                originals[c]=c.Text;
                c.TextChanged+=delegate {if(updating)return; originals[c]=c.Text;Render(c);};
            }
            foreach(Control child in c.Controls)Attach(child);
        }
        void Render(Control c){updating=true;try{c.Text=Text(originals[c]);}finally{updating=false;}}
        internal bool Set(bool english,bool save){English=english;foreach(var c in originals.Keys)Render(c);if(!save)return true;try{Directory.CreateDirectory(Path.GetDirectoryName(SettingPath));File.WriteAllText(SettingPath,english?"en":"zh");return true;}catch{return false;}}
        internal string Text(string source){return English?Translate(source):source;}
        internal static string Translate(string source) {
            if(String.IsNullOrEmpty(source))return source;
            string exact;if(Words.TryGetValue(source,out exact))return exact;
            string result=Regex.Replace(source,@"正在探探入口 (\d+/\d+)…","Trying entry $1…");
            foreach(var pair in Words.Where(p=>p.Key.Length>2).OrderByDescending(p=>p.Key.Length))result=result.Replace(pair.Key,pair.Value);
            result=Regex.Replace(result,@"正在探探入口 (\d+/\d+)…","Trying entry $1…");
            return result;
        }
        static readonly Dictionary<string,string> Words=new Dictionary<string,string> {
            {"未运行本地转发服务（可选，不影响代理连接）","Local forwarder not running (optional; does not affect proxy connection)"},
            {"本地 Codex 转发端口","Local Codex forwarding port"},{"TCP 10100（仅监听检查）","TCP 10100 (listener check only)"},{"监听可达；不代表模型请求成功","Listener reachable; this does not verify model requests"},
            {"正在检查 前置代理…","Checking the upstream…"},{"正在检查 前置代理 本地入口…","Checking the local upstream…"},{"即将请求管理员授权，用于启用全局接管…","Requesting administrator approval for global routing…"},{"正在测试 前置代理 → 出口代理 代理链…","Testing the upstream-to-exit proxy chain…"},{"代理链已通过，正在启用全局接管…","Proxy chain passed; enabling global routing…"},{"正在核对全局出口…","Checking the global exit…"},
            {"代理小助手","Proxy Helper"},{"代理类型","Proxy type"},{"代理地址","Proxy address"},{"代理账号","Username"},{"代理密码","Password"},
            {"魔法设置  ›","Upstream  ›"},{"魔法设置  ‹","Upstream  ‹"},{"魔法本地端口（127.0.0.1）","Local port (127.0.0.1)"},{"新手教程","Guide"},
            {"全局已连接","Globally connected"},{"未连接","Disconnected"},{"出口 IP：连接后检测","Exit IP: available after connecting"},{"出口 IP：","Exit IP: "},
            {"准备好就点“连接”吧～","Ready? Let's connect!"},{"通道准备好啦，安心出发吧～","All set. Enjoy your connection!"},
            {"查看连接详情","Connection details"},{"收起连接详情","Hide details"},{"代理资料仅在本机加密保存。","Proxy settings are encrypted on this PC."},
            {"断开连接","Disconnect"},{"取消连接","Cancel"},{"连接","Connect"},{"退出","Exit"},
            {"正在连接","Connecting"},{"正在断开","Disconnecting"},{"正在退出","Exiting"},
            {"正在敲敲前置代理的小门…","Checking the upstream…"},{"请点一下管理员授权～","Please approve the admin prompt."},
            {"正在牵起代理小通道…","Linking your proxies…"},{"正在铺好全局小通道…","Setting up global routing…"},{"正在核对出口，稍等一下～","Checking your exit IP…"},
            {"这个入口没接通，再试试～","Entry unavailable. Trying again…"},{"正在收好通道、恢复网络…","Restoring your network…"},{"正在收好小通道，马上就好…","Closing the connection…"},
            {"出口核对未通过，正在复查","Rechecking the exit"},{"Codex 上游不可达，正在复查","Rechecking Codex access"},{"出口不一致，正在复查","Rechecking exit mismatch"},{"出口检测超时，正在复查","Rechecking exit timeout"},{"TLS 检测失败，正在复查","Rechecking TLS failure"},{"域名解析失败，正在复查","Rechecking DNS failure"},
            {"请检查网络恢复","Check network recovery"},{"退出检查","Exit check"},
            {"未能打开教程。请到程序所在目录双击“新手教程.html”，或阅读“使用说明.md”。","Could not open the guide. Open Guide.en.html in the app folder."},
            {"连接已取消或通信中断。正在清理连接；下次启动会检查恢复记录。","Connection cancelled or interrupted. Cleaning up; recovery will be checked at next launch."},
            {"连接工作进程已中断，请检查网络恢复情况。","The connection worker stopped. Check network recovery."},
            {"工作进程通信已中断。如果网络异常，请重新打开本工具检查恢复记录。","Worker communication stopped. If the network is unavailable, reopen the app to check recovery."},
            {"代理小助手已经运行，请查看现有窗口。","Proxy Helper is already running. Use its existing window."},{"程序遇到异常。请重新打开软件检查网络恢复记录。","An unexpected error occurred. Reopen the app to check network recovery."},
            {"魔法本地端口必须为 1 到 65535。请填写魔法软件中的 HTTP / SOCKS5 / Mixed 端口。","Enter the upstream HTTP / SOCKS5 / Mixed port (1–65535)."},
            {"请选择 HTTP、HTTPS 或 SOCKS5。","Choose HTTP, HTTPS or SOCKS5."},{"代理地址请填写 主机:端口 或 [IPv6]:端口，不要包含账号密码。","Enter host:port or [IPv6]:port, without credentials."},
            {"IPv6 地址格式应为 [IPv6]:端口。","Use [IPv6]:port."},{"IPv6 地址无效。","Invalid IPv6 address."},{"代理地址需要明确端口，例如 proxy.example:12345。","Include a port, e.g. proxy.example:12345."},
            {"代理主机名无效。","Invalid proxy hostname."},{"代理主机名或 IP 无效。","Invalid proxy hostname or IP."},{"端口必须为 1 到 65535 的整数。","Port must be an integer from 1 to 65535."},
            {"账号或密码过长。","Username or password is too long."},{"请同时填写代理账号和密码，或同时留空。","Provide both username and password, or leave both empty."},
            {"已保存的代理资料无法解密，请重新填写。原加密文件已保留。","Saved settings cannot be decrypted. Enter them again; the encrypted file was retained."},
            {"已取消管理员授权，网络设置未更改。","Admin approval cancelled. Network settings were not changed."},{"管理员工作进程未就绪。","Admin worker is not ready."},{"管理员工作进程启动失败。","Could not start the admin worker."},
            {"已断开并完成清理。","Disconnected and cleaned up."},{"已取消连接，并完成工作进程清理。","Cancelled and cleaned up the worker."},{"已取消连接。","Connection cancelled."},{"已断开。","Disconnected."},
            {"魔法本地端口未通过 HTTP/SOCKS5 握手。请确认已连接、端口正确且本地代理无需认证。","Local HTTP/SOCKS5 handshake failed. Check upstream connection and port; local authentication is unsupported."},
            {"没有可用的魔法入口。请先连接魔法，并在“魔法设置”填写其本地 HTTP / SOCKS5 / Mixed 端口。","No upstream listener found. Connect your upstream app and enter its HTTP / SOCKS5 / Mixed port under Upstream."},
            {"不能把小助手自己的端口作为魔法入口。请填写前置魔法软件的本地端口。","Do not use this helper's own port. Enter your upstream app's local port."},
            {"系统代理未能自动恢复。请检查 Windows 代理设置；加密恢复记录已保留。","Automatic proxy recovery failed. Check Windows proxy settings; the encrypted recovery record was retained."},
            {"系统代理已被其他操作更改，未覆盖现有设置。","Another operation changed the system proxy. Existing settings were not overwritten."},
            {"缺少随附代理内核，请重新解压完整程序目录。","Missing bundled core. Extract the complete package again."},{"代理内核校验失败，已停止连接。","Core verification failed. Connection stopped."},
            {"代理链与全局出口不一致。","Proxy-chain and global exit IPs differ."},{"全局接管检查未通过：","Global routing check failed: "},{"Codex 上游连接未通过：","Codex connectivity check failed: "},
            {"代理链出口 IP","Proxy-chain exit IP"},{"全局出口 IP","Global exit IP"},{"代理链 Codex","Proxy-chain Codex"},{"全局 Codex","Global Codex"},
            {"达到检测时限或已取消","Deadline reached or cancelled"},{"超时或取消","Timed out or cancelled"},{"检查失败","Check failed"},{"通过","Passed"},
            {"出口代理 入口检查未通过（网络检测预算 ","Exit proxy checks failed (budget: "},{" 秒）："," s): "},{"入口 ","Entry "},
            {"通道连续三轮缺少可达证据，已断开并执行恢复。","No reachability evidence for three checks. Disconnected and restored settings."},
            {"连续十轮无法确认出口一致性，已断开并执行恢复。","Exit consistency unverified for ten checks. Disconnected and restored settings."},
            {"代理链与全局出口不一致，连续复核失败，已断开并执行恢复。","Repeated exit mismatch. Disconnected and restored settings."},
            {"出口一致，服务探测暂未通过；连接保留，继续复查。","Exit IPs match. Service probe failed; keeping connection and rechecking."},
            {"交叉复核有可达证据，连接保留；出口一致性待复查（","Reachability confirmed; keeping connection. Exit verification pending ("},
            {"通道复核未通过，正在复查（","Channel check failed; rechecking ("},
            {"代理链服务","Proxy-chain service"},
            {"全局服务","Global service"},
            {"出口 IP 可用不代表模型服务可达。","A working exit IP does not guarantee model service access."}
        };
    }
}
