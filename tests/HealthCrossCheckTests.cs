using System;
using System.Threading;
namespace IPFlyLink {
 internal static class HealthCrossCheckTests {
  static void Assert(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);}
  static int Main(){try{return Run();}catch(Exception e){Console.WriteLine("FAIL "+e.Message);return 1;}}
  static int Run(){
   bool ipFail=true,serviceFail=false,mismatch=false;
   var s=new ConnectionService(delegate(int p,bool proxy,CancellationToken t){t.ThrowIfCancellationRequested();if(ipFail&&proxy)throw new AppError("SendFailure");return proxy&&mismatch?"203.0.113.9":"203.0.113.8";},delegate(int p,bool proxy,CancellationToken t){if(serviceFail)throw new AppError("SendFailure");});
   for(int i=0;i<3;i++)Assert(s.CheckEgressStatus(CancellationToken.None).Connected,"station-only-preserves-"+i);
   ipFail=false;Assert(s.CheckEgressStatus(CancellationToken.None).Success,"recovered");
   serviceFail=true;Assert(s.CheckEgressStatus(CancellationToken.None).Connected,"service-only-preserves-matched-egress");
   ipFail=true;for(int i=1;i<=3;i++)Assert(s.CheckEgressStatus(CancellationToken.None).Connected==(i<3),"unreachable-protection-"+i);
   ipFail=false;serviceFail=false;mismatch=true;
   for(int i=1;i<=3;i++)Assert(s.CheckEgressStatus(CancellationToken.None).Connected==(i<3),"mismatch-protection-"+i);
   mismatch=false;Assert(s.CheckEgressStatus(CancellationToken.None).Success,"reset");
   ipFail=true;for(int i=1;i<=10;i++)Assert(s.CheckEgressStatus(CancellationToken.None).Connected==(i<10),"unverified-bound-"+i);
   using(var c=new CancellationTokenSource()){c.Cancel();bool cancelled=false;try{s.CheckEgressStatus(c.Token);}catch(OperationCanceledException){cancelled=true;}Assert(cancelled,"cancel-not-network-failure");}
   var hidden=HealthCheck.SafeError(new System.Net.WebException("private-text",new System.Net.Sockets.SocketException(10054),System.Net.WebExceptionStatus.SendFailure,null));
   Assert(hidden.Contains("SendFailure")&&hidden.Contains("ConnectionReset")&&!hidden.Contains("private-text"),"diagnostic-codes-without-secret-message");
   using(var ready=new CountdownEvent(4)){
    Action wait=delegate{ready.Signal();if(!ready.Wait(2000))throw new Exception("not parallel");};
    var check=HealthCheck.Run(0,delegate{wait();return "203.0.113.8";},delegate{wait();},CancellationToken.None);
    Assert(check.Verified&&check.Passed[2]&&check.Passed[3],"four-probes-parallel");
    Assert(!check.Detail.Contains("203.0.113.8"),"diagnostics-omit-exit-IP");
   }
   using(var cancel=new CancellationTokenSource()){
    cancel.CancelAfter(80);bool threw=false;
    try{HealthCheck.Run(0,delegate(int p,bool proxy,CancellationToken t){t.WaitHandle.WaitOne();t.ThrowIfCancellationRequested();return "";},delegate(int p,bool proxy,CancellationToken t){t.WaitHandle.WaitOne();t.ThrowIfCancellationRequested();},cancel.Token);}catch(OperationCanceledException){threw=true;}
    Assert(threw,"in-flight-cancellation");
   }
   return 0;
  }
 }
}
