using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace IPFlyLink {
    public static class CoreConfig {
        public static bool SubnetsOverlap(IPAddress first, int firstPrefix, IPAddress second, int secondPrefix) {
            if (first.AddressFamily != second.AddressFamily) return false;
            byte[] a = first.GetAddressBytes(), b = second.GetAddressBytes(); int bits = Math.Min(firstPrefix, secondPrefix);
            if (bits < 0 || bits > a.Length * 8) throw new AppError("网卡地址前缀无效。");
            for (int i = 0; i < bits; i++) if ((a[i / 8] & (128 >> (i % 8))) != (b[i / 8] & (128 >> (i % 8)))) return false;
            return true;
        }
        public static string Create(ProxyEndpoint proxy, NanoEndpoint nano, int localPort, bool global) {
            var ipfly = Json.Obj("type", proxy.Protocol == "SOCKS5" ? "socks" : "http", "tag", "ipfly", "server", proxy.DialAddress ?? proxy.Host, "server_port", proxy.Port, "detour", "nano");
            if (proxy.Protocol == "SOCKS5") ipfly.Add("version", "5");
            if (proxy.Username.Length != 0) { ipfly.Add("username", proxy.Username); ipfly.Add("password", proxy.Password); }
            if (proxy.Protocol == "HTTPS") ipfly.Add("tls", Json.Obj("enabled", true, "server_name", proxy.Host));
            var upstream = Json.Obj("type", nano.Protocol, "tag", "nano", "server", "127.0.0.1", "server_port", nano.Port);
            if (nano.Protocol == "socks") upstream.Add("version", "5");
            var inbounds = new List<object> { Json.Obj("type", "mixed", "tag", "local", "listen", "127.0.0.1", "listen_port", localPort) };
            if (global) {
                foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
                    foreach (var addr in adapter.GetIPProperties().UnicastAddresses)
                        if (adapter.OperationalStatus == OperationalStatus.Up && (SubnetsOverlap(addr.Address, addr.PrefixLength, IPAddress.Parse("198.18.254.1"), 30) || SubnetsOverlap(addr.Address, addr.PrefixLength, IPAddress.Parse("fdfe:dcba:9876:5432::1"), 126))) throw new AppError("本工具拟用的虚拟网卡地址已被占用，请先处理其他隧道连接。");
                inbounds.Add(Json.Obj("type", "tun", "tag", "global", "interface_name", "IPFlyLink", "address", new[] { "198.18.254.1/30", "fdfe:dcba:9876:5432::1/126" }, "mtu", 1500, "auto_route", true, "strict_route", true, "dns_mode", "hijack", "stack", "mixed"));
            }
            var rules = new List<object>();
            // Some clients proxy loopback despite Windows bypass settings. Keep
            // local application bridges on this machine, never at the IPFLY exit.
            rules.Add(Json.Obj("inbound", new[] {"local"}, "ip_cidr", new[] {"127.0.0.0/8", "::1/128"}, "action", "route", "outbound", "direct"));
            rules.Add(Json.Obj("inbound", new[] {"local"}, "domain", new[] {"localhost"}, "action", "route", "outbound", "direct", "override_address", "127.0.0.1"));
            if (global) {
                rules.Add(Json.Obj("process_path", new[] {nano.ProcessPath}, "action", "route", "outbound", "direct"));
                rules.Add(Json.Obj("port", new[] {53}, "action", "hijack-dns"));
                // TUN supplies a resolved IP. Preserve Codex's TLS destination
                // name at the SOCKS hop, as the verified explicit proxy path does.
                rules.Add(Json.Obj("inbound", new[] {"global"}, "network", "tcp", "port", new[] {443}, "action", "sniff", "sniffer", new[] {"tls"}, "timeout", "300ms"));
                rules.Add(Json.Obj("inbound", new[] {"global"}, "network", "tcp", "port", new[] {443}, "domain", new[] {"chatgpt.com"}, "action", "route", "outbound", "ipfly", "override_address", "chatgpt.com"));
            }
            rules.Add(Json.Obj("network", "udp", "action", "reject"));
            return Json.Encode(Json.Obj(
                "log", Json.Obj("disabled", true),
                "dns", Json.Obj("servers", new object[] {Json.Obj("type", "https", "tag", "remote-dns", "server", "1.1.1.1", "server_port", 443, "path", "/dns-query", "tls", Json.Obj("enabled", true, "server_name", "cloudflare-dns.com"), "detour", "ipfly")}, "final", "remote-dns"),
                "inbounds", inbounds,
                "outbounds", new object[] { ipfly, upstream, Json.Obj("type", "direct", "tag", "direct") },
                "route", Json.Obj("rules", rules, "final", "ipfly", "auto_detect_interface", true)));
        }
        public static int FreePort() {
            var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start(); int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port;
        }
    }
}
