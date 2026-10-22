using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace IPFlyLink {
 internal sealed class HealthCheck {
  static int reportSlot;
  internal readonly bool[] Passed=new bool[4];
  internal readonly string[] Values=new string[2];
  internal string Detail;
  internal bool Mismatch {get{return Passed[0]&&Passed[1]&&Values[0]!=Values[1];}}
  internal bool Verified {get{return Passed[0]&&Passed[1]&&!Mismatch;}}
  internal bool Unreachable {get{return (!Passed[0]&&!Passed[2])||(!Passed[1]&&!Passed[3]);}}
  internal static HealthCheck Run(int port,Func<int,bool,CancellationToken,string> ip,Action<int,bool,CancellationToken> service,CancellationToken token){
   token.ThrowIfCancellationRequested();var result=new HealthCheck();var lines=new string[4];
   string[] names={"代理链出口 IP","全局出口 IP","代理链服务","全局服务"};
   using(var budget=CancellationTokenSource.CreateLinkedTokenSource(token)){
    budget.CancelAfter(12000);var tasks=new Task[4];
    for(int index=0;index<4;index++){int i=index;tasks[i]=Task.Run(delegate{
     Egress.AttemptTrace=null;var watch=Stopwatch.StartNew();string status;
     try{budget.Token.ThrowIfCancellationRequested();if(i<2)result.Values[i]=ip(port,i==0,budget.Token);else service(port,i==2,budget.Token);budget.Token.ThrowIfCancellationRequested();result.Passed[i]=true;status="通过";}
     catch(OperationCanceledException){status="超时或取消";}
     catch(Exception e){status=e is AppError?e.Message:SafeError(e);}
     lines[i]=names[i]+"："+status+"（"+watch.ElapsedMilliseconds+"ms）";
     if(i<2&&Egress.AttemptTrace!=null)lines[i]+=Environment.NewLine+"  "+String.Join("；",Egress.AttemptTrace);
    });}
    Task.WhenAll(tasks).GetAwaiter().GetResult();
   }
   token.ThrowIfCancellationRequested();result.Detail=String.Join(Environment.NewLine,lines)+(result.Mismatch?Environment.NewLine+"代理链与全局出口不一致。":"");return result;
  }
  internal static string SafeError(Exception error){
   var b=new StringBuilder();int depth=0;
   for(var e=error;e!=null&&depth++<4;e=e.InnerException){if(b.Length>0)b.Append(" > ");b.Append(e.GetType().Name);var web=e as System.Net.WebException;if(web!=null){b.Append(":").Append(web.Status);var response=web.Response as System.Net.HttpWebResponse;if(response!=null)b.Append(" HTTP=").Append((int)response.StatusCode);}var socket=e as System.Net.Sockets.SocketException;if(socket!=null)b.Append(":").Append(socket.SocketErrorCode);}
   return b.ToString();
  }
  internal static void Save(string detail){
   // Bounded ring of controlled diagnostics, never exception messages/response bodies.
   try{string dir=Path.Combine(ProfileStore.DirectoryPath,"diagnostics");Directory.CreateDirectory(dir);int slot=(Interlocked.Increment(ref reportSlot)&Int32.MaxValue)%20;File.WriteAllText(Path.Combine(dir,"health-"+slot.ToString("00")+".txt"),DateTime.Now.ToString("s")+Environment.NewLine+detail,Encoding.UTF8);}catch{}
  }
 }
}
