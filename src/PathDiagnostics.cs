using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IPFlyLink {
    // Bounded, unauthenticated observations. No remote text, IPs or credentials enter the report.
    internal sealed class PathDiagnostics : IDisposable {
        sealed class Row {
            internal string Name, Stage="等待开始", Result="进行中";
            internal readonly Stopwatch Watch=Stopwatch.StartNew();
            internal long StageStart;
            internal readonly List<string> Completed=new List<string>();
        }
        readonly object gate=new object();
        readonly List<Row> rows=new List<Row>();
        readonly CancellationTokenSource stop;
        readonly CancellationToken token;
        bool frozen;
        internal PathDiagnostics(int port, CancellationToken cancellation, int budgetMs=12000, Func<IPAddress[]> resolver=null, bool environment=true) {
            stop=CancellationTokenSource.CreateLinkedTokenSource(cancellation); token=stop.Token; stop.CancelAfter(budgetMs);
            Launch("代理链原始协议",delegate(Row r){Probe(r,IPAddress.Loopback,port,true);});
            Launch("系统 DNS",delegate(Row r){
                Step(r,"DNS");
                var lookup=resolver==null?Task<IPAddress[]>.Factory.FromAsync(Dns.BeginGetHostAddresses,Dns.EndGetHostAddresses,"chatgpt.com",null):Task.Run(resolver);
                lookup.ContinueWith(delegate(Task<IPAddress[]> t){var ignored=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);
                lookup.Wait(token); var addresses=lookup.Result;
                int v4=0,v6=0;
                foreach(var address in addresses.Distinct()) {
                    token.ThrowIfCancellationRequested();
                    bool ipv4=address.AddressFamily==AddressFamily.InterNetwork;
                    if(!ipv4&&address.AddressFamily!=AddressFamily.InterNetworkV6)continue;
                    int number=ipv4?++v4:++v6;
                    if(number>2)continue;
                    var target=address;
                    Launch("全局 "+(ipv4?"IPv4":"IPv6")+" 地址"+number,delegate(Row p){Probe(p,target,443,false);});
                }
                Finish(r,"通过，IPv4="+v4+" IPv6="+v6+"；每族最多测2个地址");
                if(v4==0)Note("全局 IPv4","DNS 无此地址族，未测试");
                if(v6==0)Note("全局 IPv6","DNS 无此地址族，未测试");
            });
            if(!environment)return;
            Launch("本地 Codex 转发端口",delegate(Row r){
                Step(r,"TCP 10100（仅监听检查）");
                Finish(r,ObserveLocalForwarder(10100,token));
            });
            Launch("网络环境",delegate(Row r){
                Step(r,"读取网卡"); int n=0;
                foreach(var nic in NetworkInterface.GetAllNetworkInterfaces()) {
                    token.ThrowIfCancellationRequested();
                    if(nic.OperationalStatus!=OperationalStatus.Up||nic.NetworkInterfaceType==NetworkInterfaceType.Loopback)continue;
                    var props=nic.GetIPProperties(); var p4=props.GetIPv4Properties(); var p6=props.GetIPv6Properties();
                    Note("活动网卡"+(++n),"类型="+nic.NetworkInterfaceType+" IPv4 MTU="+(p4==null?"无":p4.Mtu.ToString())+" IPv6 MTU="+(p6==null?"无":p6.Mtu.ToString()));
                }
                foreach(string name in new[]{"HTTP_PROXY","HTTPS_PROXY","ALL_PROXY","NO_PROXY"})Note(name,String.IsNullOrEmpty(Environment.GetEnvironmentVariable(name))?"当前检查进程未设置":"当前检查进程已设置（不输出值）");
                Finish(r,"已记录；不代表 Codex 进程环境或实际路由");
            });
        }
        internal static string ObserveLocalForwarder(int port,CancellationToken token) {
            token.ThrowIfCancellationRequested();
            using(var socket=new TcpClient(AddressFamily.InterNetwork))using(token.Register(delegate{socket.Close();})) {
                try {socket.Connect(IPAddress.Loopback,port);token.ThrowIfCancellationRequested();return "监听可达；不代表模型请求成功";}
                catch(SocketException error) {token.ThrowIfCancellationRequested();if(error.SocketErrorCode==SocketError.ConnectionRefused)return "未运行本地转发服务（可选，不影响代理连接）";throw;}
            }
        }
        void Launch(string name,Action<Row> action) {
            var r=new Row{Name=name}; lock(gate){if(frozen)return;rows.Add(r);}
            Task.Factory.StartNew(delegate {
                try{token.ThrowIfCancellationRequested();action(r);}
                catch(Exception e){
                    string reason=token.IsCancellationRequested?"预算到期或检查结束/取消":SafeError(e);
                    Finish(r,reason,false);
                }
            },CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default);
        }
        static string SafeError(Exception e) {
            while(e is AggregateException&&e.InnerException!=null)e=e.InnerException;
            var socket=e as SocketException;if(socket!=null)return "Socket:"+socket.SocketErrorCode;
            if(e is AuthenticationException)return "TLS 认证失败";
            if(e is IOException)return "连接关闭或读写失败";
            if(e is OperationCanceledException)return "取消";
            return e.GetType().Name;
        }
        void Note(string name,string result){lock(gate){if(!frozen)rows.Add(new Row{Name=name,Stage="记录",Result=result});}}
        void Step(Row r,string stage){lock(gate){if(frozen)return;if(r.Stage!="等待开始")r.Completed.Add(r.Stage+"="+(r.Watch.ElapsedMilliseconds-r.StageStart)+"ms");r.Stage=stage;r.StageStart=r.Watch.ElapsedMilliseconds;}}
        void Finish(Row r,string result,bool complete=true){lock(gate){if(frozen)return;if(complete)r.Completed.Add(r.Stage+"="+(r.Watch.ElapsedMilliseconds-r.StageStart)+"ms");r.Result=result;}}
        void Probe(Row r,IPAddress address,int port,bool proxy) {
            Step(r,"TCP");
            using(var client=new TcpClient(address.AddressFamily))using(token.Register(delegate{client.Close();})) {
                client.NoDelay=true; client.Connect(address,port);token.ThrowIfCancellationRequested();
                var stream=client.GetStream();
                if(proxy) {
                    Step(r,"HTTP CONNECT");Send(stream,"CONNECT chatgpt.com:443 HTTP/1.1\r\nHost: chatgpt.com:443\r\n\r\n");
                    int code=ReadStatus(stream);if(code!=200){Finish(r,"CONNECT 状态="+code,false);return;}
                }
                Step(r,"TLS");
                using(var tls=new SslStream(stream,false,delegate(object sender,System.Security.Cryptography.X509Certificates.X509Certificate cert,System.Security.Cryptography.X509Certificates.X509Chain chain,SslPolicyErrors errors){
                    if(errors!=SslPolicyErrors.None)Note(r.Name+" 证书",errors.ToString());return errors==SslPolicyErrors.None;
                })) {
                    tls.AuthenticateAsClient("chatgpt.com",null,System.Security.Authentication.SslProtocols.Tls12,true);
                    Step(r,"HTTP 首部");Send(tls,"GET /backend-api/codex/responses HTTP/1.1\r\nHost: chatgpt.com\r\nConnection: close\r\n\r\n");
                    int code=ReadStatus(tls);Finish(r,"HTTP="+code+((code==401||code==405)?" 可达（未认证模型调用）":"，服务拒绝或其他状态"));
                }
            }
        }
        static void Send(Stream stream,string text){var bytes=Encoding.ASCII.GetBytes(text);stream.Write(bytes,0,bytes.Length);}
        internal static int ReadStatus(Stream stream) {
            // Consume the complete bounded header, but retain only the status line.
            // No caller uses response headers or reuses this connection afterward.
            var bytes=new List<byte>();int matched=0,count=0;bool firstLineEnded=false;byte[] end={13,10,13,10};
            while(count<16384) {
                int b=stream.ReadByte();if(b<0)throw new IOException();count++;
                if(!firstLineEnded){if(b==13)firstLineEnded=true;else bytes.Add((byte)b);}
                matched=b==end[matched]?matched+1:(b==13?1:0);if(matched==4)break;
            }
            if(matched!=4)throw new IOException();
            string first=Encoding.ASCII.GetString(bytes.ToArray());var fields=first.Split(' ');int code;
            if(fields.Length<2||!fields[0].StartsWith("HTTP/",StringComparison.Ordinal)||!Int32.TryParse(fields[1],out code)||code<100||code>599)throw new IOException();
            return code;
        }
        internal string Snapshot() {
            lock(gate) {
                var lines=new List<string>{"分段诊断 0.1.8 / "+DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),"仅诊断，不放宽连接判定。时长为各阶段耗时；未完成不等于该阶段已失败。"};
                foreach(var r in rows)lines.Add(r.Name+"："+(r.Result=="进行中"?"未完成（预算或主检查结束）":r.Result)+"；当前阶段="+r.Stage+"；"+(r.Result=="进行中"?"阶段已用="+(r.Watch.ElapsedMilliseconds-r.StageStart)+"ms；":"")+String.Join("，",r.Completed));
                lines.Add("边界：未发送认证模型请求；未抓包、未测路径MTU或重传；网卡MTU不能证明路径MTU正常。");
                frozen=true;return String.Join(Environment.NewLine,lines);
            }
        }
        public void Dispose(){stop.Cancel();stop.Dispose();}
    }
}
