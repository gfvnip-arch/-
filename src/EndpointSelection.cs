using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
namespace IPFlyLink {
    public static class EndpointSelection {
        public static ProxyEndpoint ChooseBounded(ProxyEndpoint source, string[] addresses, Action<ProxyEndpoint,CancellationToken> verify, Action<string> progress, CancellationToken cancellation, int totalMs=20000, int attemptMs=12000) {
            if(addresses.Length==0)throw new AppError("IPFLY 入口域名没有可用地址。");
            var gate=new object();var errors=new string[addresses.Length];int next=-1;ProxyEndpoint winner=null;
            using(var budget=CancellationTokenSource.CreateLinkedTokenSource(cancellation)) {
                budget.CancelAfter(totalMs);
                var workers=new List<Task>();
                for(int w=0;w<Math.Min(2,addresses.Length);w++)workers.Add(Task.Run(delegate {
                    while(!budget.IsCancellationRequested) {
                        int i=Interlocked.Increment(ref next);if(i>=addresses.Length)return;
                        var candidate=new ProxyEndpoint{Protocol=source.Protocol,Host=source.Host,Port=source.Port,Username=source.Username,Password=source.Password,DialAddress=addresses[i]};
                        lock(gate)progress("正在检查 IPFLY 入口 "+(i+1)+"/"+addresses.Length+"，最多同时检查两个…");
                        using(var attempt=CancellationTokenSource.CreateLinkedTokenSource(budget.Token)) {
                            attempt.CancelAfter(attemptMs);
                            try {
                                verify(candidate,attempt.Token);attempt.Token.ThrowIfCancellationRequested();
                                lock(gate){if(winner==null)winner=candidate;}
                                budget.Cancel();return;
                            } catch(OperationCanceledException) { errors[i]="达到检测时限或已取消"; }
                            catch(AppError e) { errors[i]=e.Message; }
                            catch { errors[i]="检测请求失败，未取得有效结果"; }
                        }
                    }
                }));
                Task.WhenAll(workers).GetAwaiter().GetResult();
                cancellation.ThrowIfCancellationRequested();
                if(winner!=null)return winner;
                var lines=new List<string>{"IPFLY 入口检查未通过（网络检测预算 "+(totalMs/1000)+" 秒）。"};
                for(int i=0;i<errors.Length;i++)lines.Add("入口 "+(i+1)+"："+(errors[i]??"总时限已到，未测试"));
                throw new AppError(String.Join(Environment.NewLine,lines));
            }
        }
        public static string[] Resolve(ProxyEndpoint proxy, CancellationToken cancellation) {
            cancellation.ThrowIfCancellationRequested();
            IPAddress literal;
            if(IPAddress.TryParse(proxy.Host,out literal)) return new[]{literal.ToString()};
            // Preserve the name in Nano's CONNECT request. Local resolution can
            // select a different endpoint than the upstream network would use.
            return new[]{proxy.Host};
        }
        public static ProxyEndpoint Choose(ProxyEndpoint source, string[] addresses, Action<ProxyEndpoint> verify, Action<string> progress, CancellationToken cancellation) {
            AppError last=null;
            for(int i=0;i<addresses.Length;i++) {
                cancellation.ThrowIfCancellationRequested();
                var candidate=new ProxyEndpoint{Protocol=source.Protocol,Host=source.Host,Port=source.Port,Username=source.Username,Password=source.Password,DialAddress=addresses[i]};
                progress("正在检查 IPFLY 入口 "+(i+1)+"/"+addresses.Length+" 的出口与 Codex 上游…");
                try{verify(candidate);cancellation.ThrowIfCancellationRequested();return candidate;}
                catch(OperationCanceledException){throw;}
                catch(AppError error){last=error;progress("此入口未通过："+error.Message);}
            }
            throw new AppError("IPFLY 所有候选入口均未通过检查。"+(last==null?"":last.Message));
        }
    }
}
