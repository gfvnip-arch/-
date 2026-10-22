using System;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;

namespace IPFlyLink {
    internal static class CoreReadiness {
        [DllImport("iphlpapi.dll")] static extern uint GetBestInterfaceEx(byte[] destination,out uint index);
        internal static bool RouteUses(IPAddress address,int expectedIndex) {
            byte[] socketAddress=new byte[28];bool v4=address.AddressFamily==System.Net.Sockets.AddressFamily.InterNetwork;
            socketAddress[0]=(byte)(v4?2:23);Array.Copy(address.GetAddressBytes(),0,socketAddress,v4?4:8,v4?4:16);
            uint actual;return GetBestInterfaceEx(socketAddress,out actual)==0&&actual==(uint)expectedIndex;
        }
        internal static string TunStatus() {
            try {
                foreach(var nic in NetworkInterface.GetAllNetworkInterfaces()) {
                    if(nic.Name!="IPFlyLink")continue;
                    if(nic.OperationalStatus!=OperationalStatus.Up)return "虚拟网卡尚未启用";
                    var properties=nic.GetIPProperties();bool v4=false,v6=false;
                    foreach(var address in properties.UnicastAddresses){v4|=address.Address.Equals(IPAddress.Parse("198.18.254.1"));v6|=address.Address.Equals(IPAddress.Parse("fdfe:dcba:9876:5432::1"));}
                    if(!v4||!v6)return "虚拟网卡地址尚未配置完成";
                    var p4=properties.GetIPv4Properties();var p6=properties.GetIPv6Properties();
                    if(p4==null||!RouteUses(IPAddress.Parse("1.1.1.1"),p4.Index)||!RouteUses(IPAddress.Parse("104.18.32.47"),p4.Index))return "IPv4 全局路由尚未指向本工具";
                    if(p6==null||!RouteUses(IPAddress.Parse("2606:4700::1111"),p6.Index))return "IPv6 全局路由尚未指向本工具";
                    return null;
                }
                return "虚拟网卡尚未出现";
            }catch{return "无法核对虚拟网卡与路由";}
        }
        internal static void Wait(Func<bool> alive,Func<bool> listening,Func<bool> started,Func<string> tunStatus,CancellationToken cancellation,int budgetMs=15000) {
            var watch=Stopwatch.StartNew();string pending="本地端口尚未开放";
            while(watch.ElapsedMilliseconds<budgetMs) {
                cancellation.ThrowIfCancellationRequested();
                if(!alive())throw new AppError("代理内核在完成启动前退出。");
                if(!listening())pending="本地端口尚未开放";
                else if(!started())pending="内核尚未完成全部启动阶段";
                else {
                    pending=tunStatus==null?null:tunStatus();
                    if(pending==null)return;
                }
                cancellation.WaitHandle.WaitOne(50);
            }
            cancellation.ThrowIfCancellationRequested();
            throw new AppError("全局接管尚未就绪："+pending+"；未开始出口检测。");
        }
    }
}
