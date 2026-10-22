using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace IPFlyLink {
    public sealed class AppError : Exception {
        public AppError(string message) : base(message) { }
    }
    public static class Json {
        public static string Encode(object value) { return new JavaScriptSerializer { MaxJsonLength = 131072 }.Serialize(value); }
        public static T Decode<T>(string text) { return new JavaScriptSerializer { MaxJsonLength = 131072 }.Deserialize<T>(text); }
        public static Dictionary<string, object> Obj(params object[] items) {
            var result = new Dictionary<string, object>();
            for (int i = 0; i < items.Length; i += 2) result.Add((string)items[i], items[i + 1]);
            return result;
        }
    }
    public sealed class ProxyProfile {
        public string Protocol { get; set; }
        public string Address { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public int UpstreamPort { get; set; }
        public ProxyProfile() { UpstreamPort = 65532; Protocol = "SOCKS5"; Address = ""; Username = ""; Password = ""; }
        public ProxyEndpoint Parse() {
            if (UpstreamPort < 1 || UpstreamPort > 65535) throw new AppError("魔法本地端口必须为 1 至 65535。请填写魔法设置中的 HTTP / SOCKS5 / Mixed 端口。");
            string protocol = (Protocol ?? "").ToUpperInvariant();
            if (protocol != "HTTP" && protocol != "HTTPS" && protocol != "SOCKS5") throw new AppError("请选择 HTTP、HTTPS 或 SOCKS5。");
            string address = (Address ?? "").Trim();
            if (address.Length == 0 || address.Length > 512 || address.IndexOfAny(new[] {'/', '@', '\\', '?', '#', ' ', '\t', '\r', '\n'}) >= 0)
                throw new AppError("代理地址请填写 主机:端口 或 [IPv6]:端口，不包含账号密码。");
            string host, portText;
            if (address.StartsWith("[", StringComparison.Ordinal)) {
                int end = address.IndexOf(']');
                if (end < 2 || end + 1 >= address.Length || address[end + 1] != ':') throw new AppError("IPv6 地址格式应为 [IPv6]:端口。");
                host = address.Substring(1, end - 1); portText = address.Substring(end + 2);
                IPAddress ip;
                if (!IPAddress.TryParse(host, out ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6 || host.Contains("%")) throw new AppError("IPv6 地址无效。");
            } else {
                int end = address.LastIndexOf(':');
                if (end < 1 || address.IndexOf(':') != end) throw new AppError("代理地址需要明确端口，例如 proxy.example:12345。");
                host = address.Substring(0, end); portText = address.Substring(end + 1);
                try { host = new IdnMapping().GetAscii(host); } catch { throw new AppError("代理主机名无效。"); }
                if (Uri.CheckHostName(host) == UriHostNameType.Unknown) throw new AppError("代理主机名或 IP 无效。");
            }
            int port;
            if (!Int32.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out port) || port < 1 || port > 65535) throw new AppError("端口必须为 1 至 65535 的整数。");
            if ((Username ?? "").Length > 4096 || (Password ?? "").Length > 4096) throw new AppError("账号或密码过长。");
            if (String.IsNullOrEmpty(Username) != String.IsNullOrEmpty(Password)) throw new AppError("请同时填写代理账号和密码，或同时留空。");
            return new ProxyEndpoint { Protocol = protocol, Host = host, Port = port, Username = Username ?? "", Password = Password ?? "" };
        }
    }
    public sealed class ProxyEndpoint {
        public string Protocol, Host, Username, Password, DialAddress;
        public int Port;
    }
    public static class ProfileStore {
        public static readonly string DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IPFlyLink");
        public static string Protect(object value) {
            byte[] plain = Encoding.UTF8.GetBytes(Json.Encode(value));
            try { return Convert.ToBase64String(ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser)); }
            finally { Array.Clear(plain, 0, plain.Length); }
        }
        public static T Unprotect<T>(string cipher) {
            byte[] plain = ProtectedData.Unprotect(Convert.FromBase64String(cipher), null, DataProtectionScope.CurrentUser);
            try { return Json.Decode<T>(Encoding.UTF8.GetString(plain)); }
            finally { Array.Clear(plain, 0, plain.Length); }
        }
        public static void WriteEncrypted(string name, object value) {
            Directory.CreateDirectory(DirectoryPath);
            string destination = Path.Combine(DirectoryPath, name), temp = destination + ".new";
            File.WriteAllText(temp, Protect(value), new UTF8Encoding(false));
            if (File.Exists(destination)) File.Replace(temp, destination, destination + ".previous", true);
            else File.Move(temp, destination);
        }
        public static T ReadEncrypted<T>(string name) {
            return Unprotect<T>(File.ReadAllText(Path.Combine(DirectoryPath, name), Encoding.UTF8));
        }
        public static ProxyProfile Load() {
            if (!File.Exists(Path.Combine(DirectoryPath, "profile.bin"))) return new ProxyProfile();
            try { return ReadEncrypted<ProxyProfile>("profile.bin"); }
            catch { throw new AppError("已保存的代理资料无法解密，请重新填写。原加密文件已保留。"); }
        }
        public static void Save(ProxyProfile profile) { profile.Parse(); WriteEncrypted("profile.bin", profile); }
    }
}
