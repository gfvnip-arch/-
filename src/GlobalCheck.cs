using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace IPFlyLink {
    public static class GlobalCheck {
        internal static string Run(int port, Func<int,bool,CancellationToken,string> ipCheck, Action<int,bool,CancellationToken> serviceCheck, CancellationToken cancellation, Action<string> report, int budgetMs=12000, Func<string> diagnostics=null) {
            var values=new string[4];var status=new string[4];var elapsed=new long[4];
            string[] names={"代理链出口 IP","全局出口 IP","代理链 Codex","全局 Codex"};
            using(var budget=CancellationTokenSource.CreateLinkedTokenSource(cancellation)) {
                budget.CancelAfter(budgetMs);var tasks=new Task[4];
                for(int index=0;index<4;index++) {
                    int i=index;
                    tasks[i]=Task.Run(delegate {
                        var watch=Stopwatch.StartNew();
                        try {budget.Token.ThrowIfCancellationRequested();if(i<2)values[i]=ipCheck(port,i==0,budget.Token);else serviceCheck(port,i==2,budget.Token);budget.Token.ThrowIfCancellationRequested();status[i]="通过";}
                        catch(OperationCanceledException){status[i]="超时或取消";}
                        catch(Exception e){status[i]=e is AppError?e.Message:"检测失败";}
                        finally{elapsed[i]=watch.ElapsedMilliseconds;}
                    });
                }
                Task.WhenAll(tasks).GetAwaiter().GetResult();
            }
            cancellation.ThrowIfCancellationRequested();
            var lines=new string[4];bool passed=true;
            for(int i=0;i<4;i++){passed&=status[i]=="通过";lines[i]=names[i]+"："+status[i]+"（"+elapsed[i]+"ms）";}
            bool mismatch=status[0]=="通过"&&status[1]=="通过"&&values[0]!=values[1];
            string detail=String.Join(Environment.NewLine,lines)+(mismatch?Environment.NewLine+"代理链与全局出口不一致。":"");
            if(diagnostics!=null)detail+=Environment.NewLine+diagnostics();
            if(report!=null)report(detail);
            if(!passed||mismatch)throw new AppError("全局接管检查未通过："+Environment.NewLine+detail);
            return values[1];
        }
        internal static void SaveReport(string detail) {
            // Only fixed probe labels, elapsed times and controlled AppError messages.
            // Never write configuration, credentials, headers, response bodies or IPs.
            try {
                string folder=Path.Combine(ProfileStore.DirectoryPath,"diagnostics");Directory.CreateDirectory(folder);
                string path=Path.Combine(folder,"global-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+"-"+Guid.NewGuid().ToString("N")+".txt");
                File.WriteAllText(path,detail,Encoding.UTF8);
            }catch { /* The UI still displays all results if local report storage fails. */ }
        }
    }
}
