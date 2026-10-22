using System;
using System.IO;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace IPFlyLink {
    static class Program {
        [STAThread] static int Main(string[] args) {
            if (args.Length != 0 && args[0] == "--worker") return NetworkWorker.Run(args);
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length == 2 && args[0] == "--render") { using (var form = new MainForm()) form.RenderPreview(args[1]); return 0; }
            bool acquired;
            using (var mutex = new Mutex(true, @"Local\IPFlyLink-" + WindowsIdentity.GetCurrent().User.Value, out acquired)) {
                if (!acquired) { MessageBox.Show(UiLanguage.SavedEnglish?UiLanguage.Translate("代理小助手已经运行，请查看现有窗口。"):"代理小助手已经运行，请查看现有窗口。", UiLanguage.SavedEnglish?"Proxy Helper":"代理小助手"); return 0; }
                try { Application.Run(new MainForm()); }
                catch { MessageBox.Show(UiLanguage.SavedEnglish?UiLanguage.Translate("程序遇到异常。请重新打开软件检查网络恢复记录。"):"程序遇到异常。请重新打开软件检查网络恢复记录。", UiLanguage.SavedEnglish?"Proxy Helper":"代理小助手"); return 1; }
                finally { mutex.ReleaseMutex(); }
            }
            return 0;
        }
    }
}
