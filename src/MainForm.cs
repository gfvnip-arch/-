using System;
using System.Drawing;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace IPFlyLink {
    public sealed class MainForm : Form {
        readonly UiLanguage language = new UiLanguage();
        readonly Button languageToggle = new Button();
        readonly ComboBox protocol = new SoftCombo();
        readonly Control protocolReadOnly = new SoftProtocolReadOnly {Visible=false};
        readonly TextBox address = new TextBox(), username = new TextBox(), password = new TextBox();
        readonly Button connect = new SoftButton {Primary=true}, exit = new SoftButton();
        readonly Label state = new SoftStatus {Padding=new Padding(27,0,0,0)}, ip = new Label();
        readonly TextBox details = new TextBox {Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,BorderStyle=BorderStyle.None};
        readonly Label hint = new SoftHint {AutoSize=true};
        readonly Button disclosure = new Button();
        readonly Button upstreamToggle = new Button();
        readonly NumericUpDown upstreamPort = new NumericUpDown {Minimum=1,Maximum=65535,Value=65532};
        readonly TableLayoutPanel upstreamCard = new TableLayoutPanel();
        readonly TableLayoutPanel layout = new TableLayoutPanel();
        readonly List<SoftInput> inputFrames = new List<SoftInput>();
        readonly List<Font> ownedFonts = new List<Font>();
        Image avatarImage;
        Icon windowIcon;
        readonly Timer health = new Timer();
        readonly ConnectionController controller = new ConnectionController();
        Task active;
        bool connected, busy, allowClose, closing, previewing, compactReady, compactPending;
        static readonly Color Blue = Color.FromArgb(151, 127, 211), Muted = Color.FromArgb(113, 105, 137);
        public MainForm() {
            SuspendLayout();
            AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96F, 96F);
            Text = "代理小助手"; ClientSize = new Size(380, 600);
            windowIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); Icon = windowIcon;
            FormBorderStyle = FormBorderStyle.None; MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen;
            Font = OwnFont(10F, FontStyle.Regular); BackColor = Color.FromArgb(253, 251, 254);
            ForeColor = Color.FromArgb(83, 70, 120); AutoScroll=false;
            layout.Dock=DockStyle.Top; layout.AutoSize=true; layout.AutoSizeMode=AutoSizeMode.GrowAndShrink;
            layout.ColumnCount=1; layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            layout.Padding=new Padding(14,10,14,10); layout.Margin=Padding.Empty;
            var body=new Panel {Dock=DockStyle.Fill,AutoScroll=true,BackColor=BackColor,Padding=new Padding(1)};
            body.Controls.Add(layout);Controls.Add(body);
            var chrome=new Panel {Dock=DockStyle.Top,Height=30,BackColor=Color.FromArgb(246,243,251)};Controls.Add(chrome);
            var caption=new Label {Text="代理小助手",AutoSize=true,Location=new Point(40,5),ForeColor=Color.FromArgb(48,39,79),Font=OwnFont(10F,FontStyle.Regular)};
            chrome.Controls.Add(caption);
            var closeWindow=new Button {Text="×",Dock=DockStyle.Right,Width=42,FlatStyle=FlatStyle.Flat,Font=OwnFont(16F,FontStyle.Regular),TabStop=false};closeWindow.FlatAppearance.BorderSize=0;closeWindow.Click+=delegate{Close();};
            var minimize=new Button {Text="─",Dock=DockStyle.Right,Width=42,FlatStyle=FlatStyle.Flat,TabStop=false};minimize.FlatAppearance.BorderSize=0;minimize.Click+=delegate{WindowState=FormWindowState.Minimized;};
            chrome.Controls.Add(minimize);chrome.Controls.Add(closeWindow);
            MouseEventHandler drag=delegate(object sender,MouseEventArgs e){if(e.Button==MouseButtons.Left){ReleaseCapture();SendMessage(Handle,0xA1,(IntPtr)2,IntPtr.Zero);}};chrome.MouseDown+=drag;caption.MouseDown+=drag;
            var brand=new BrandMark {Dock=DockStyle.Bottom,Height=18,BackColor=BackColor,ForeColor=Muted,Font=OwnFont(8F,FontStyle.Regular),Padding=new Padding(0,0,14,2)};Controls.Add(brand);
            Padding=new Padding(1); DoubleBuffered=true;
            var header=new TableLayoutPanel {AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=2,Dock=DockStyle.Top,Margin=new Padding(0,0,0,10)};
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            using(var stream=typeof(MainForm).Assembly.GetManifestResourceStream("IPFlyLink.Avatar.png")) {
                if(stream!=null)using(var original=Image.FromStream(stream))avatarImage=new Bitmap(original);
                else if(windowIcon!=null)avatarImage=windowIcon.ToBitmap();
            }
            var titleIcon=new PictureBox {Image=avatarImage,SizeMode=PictureBoxSizeMode.Zoom,Size=new Size(21,21),Location=new Point(10,4)};chrome.Controls.Add(titleIcon);titleIcon.MouseDown+=drag;
            var portrait=new SoftPortrait {Image=avatarImage,SizeMode=PictureBoxSizeMode.Zoom,Size=new Size(52,52),Margin=new Padding(2,0,14,0)};
            portrait.SizeChanged+=delegate{var previous=portrait.Region;using(var path=SoftShape.Round(new RectangleF(0,0,portrait.Width,portrait.Height),14))portrait.Region=new Region(path);if(previous!=null)previous.Dispose();};
            using(var path=SoftShape.Round(new RectangleF(0,0,52,52),14))portrait.Region=new Region(path);
            header.Controls.Add(portrait,0,0);
            header.Controls.Add(new Label {Text="代理小助手",AutoSize=true,Font=OwnFont(20F,FontStyle.Bold),Anchor=AnchorStyles.Left,Margin=Padding.Empty},1,0);
            AddRow(header);
            var card=new SoftCard {AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=1,Dock=DockStyle.Top,BackColor=Color.White,Padding=new Padding(12),Margin=new Padding(0,0,0,10)};
            card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            AddInput(card,"代理类型",protocol);
            protocol.DropDownStyle = ComboBoxStyle.DropDownList; protocol.Items.AddRange(new object[] {"SOCKS5", "HTTP", "HTTPS"}); protocol.SelectedIndex = 0;
            AddInput(card,"代理地址",address);
            AddInput(card,"代理账号",username);
            AddInput(card,"代理密码",password); password.UseSystemPasswordChar=true;
            password.Margin=new Padding(0,0,0,0); AddRow(card);
            var tools=new FlowLayoutPanel {AutoSize=true,Dock=DockStyle.Top,Margin=new Padding(0,0,0,10),WrapContents=true};
            upstreamToggle.Text="魔法设置  ›";upstreamToggle.AutoSize=true;upstreamToggle.FlatStyle=FlatStyle.Flat;upstreamToggle.FlatAppearance.BorderSize=0;
            upstreamToggle.Click+=delegate{upstreamCard.Visible=!upstreamCard.Visible;upstreamToggle.Text=upstreamCard.Visible?"魔法设置  ‹":"魔法设置  ›";};
            var help=new Button {Text="新手教程",AutoSize=true,FlatStyle=FlatStyle.Flat};help.FlatAppearance.BorderSize=0;
            help.Click+=delegate{try{string guide=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,language.English?"Guide.en.html":"新手教程.html");if(!File.Exists(guide))throw new IOException();System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(guide){UseShellExecute=true});}catch{MessageBox.Show(this,language.Text("未能打开教程。请到程序所在目录双击“新手教程.html”，或阅读“使用说明.md”。"),language.Text("新手教程"));}};
            tools.Controls.Add(upstreamToggle);tools.Controls.Add(help);
            languageToggle.Text="English";languageToggle.AutoSize=true;languageToggle.FlatStyle=FlatStyle.Flat;languageToggle.FlatAppearance.BorderSize=0;tools.Controls.Add(languageToggle);
            languageToggle.Click+=delegate{SetLanguage(!language.English,!previewing);};
            upstreamCard.AutoSize=true;upstreamCard.Dock=DockStyle.Top;upstreamCard.ColumnCount=1;upstreamCard.Padding=new Padding(12);upstreamCard.BackColor=Color.White;upstreamCard.Margin=new Padding(0,0,0,12);upstreamCard.Visible=false;
            upstreamCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            AddInput(upstreamCard,"魔法本地端口（127.0.0.1）",upstreamPort);
            upstreamPort.Margin=Padding.Empty;AddRow(upstreamCard);
            state.AutoSize=true; state.Font=OwnFont(11F,FontStyle.Bold); state.Text="未连接"; state.ForeColor=Muted;
            state.Margin=new Padding(0,0,0,3);AddRow(state);
            ip.AutoSize=true; ip.Text="出口 IP：连接后检测";ip.ForeColor=Muted;ip.Margin=new Padding(0,0,0,8);AddRow(ip);
            hint.ForeColor=Color.FromArgb(125,94,199);hint.BackColor=Color.FromArgb(246,241,255);hint.Padding=new Padding(40,7,10,7);hint.Margin=new Padding(0,0,0,8);hint.Dock=DockStyle.Top;
            hint.Text="准备好就点“连接”吧～";AddRow(hint);
            disclosure.Text="查看连接详情";disclosure.TextAlign=ContentAlignment.MiddleLeft;disclosure.Dock=DockStyle.Top;
            disclosure.AutoSize=true;disclosure.Padding=new Padding(0,5,0,5);disclosure.FlatStyle=FlatStyle.Flat;disclosure.FlatAppearance.BorderSize=0;disclosure.Margin=new Padding(0,1,0,6);
            disclosure.Paint+=delegate(object sender,PaintEventArgs e){using(var pen=new Pen(Color.FromArgb(230,224,240))){e.Graphics.DrawLine(pen,0,0,disclosure.Width,0);e.Graphics.DrawLine(pen,0,disclosure.Height-1,disclosure.Width,disclosure.Height-1); e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;float x=disclosure.Width-12,y=disclosure.Height/2F;using(var arrow=new Pen(ForeColor,1.6F)){float direction=details.Visible?-1:1;e.Graphics.DrawLines(arrow,new[]{new PointF(x-3*direction,y-6),new PointF(x+3*direction,y),new PointF(x-3*direction,y+6)});}}};
            disclosure.Click+=delegate{details.Visible=!details.Visible;disclosure.Text=details.Visible?"收起连接详情":"查看连接详情";};AddRow(disclosure);
            details.Dock=DockStyle.Top;details.Height=100;details.Font=OwnFont(9F,FontStyle.Regular);details.ForeColor=Muted;details.BackColor=BackColor;details.Visible=false;details.Margin=new Padding(0,0,0,12);
            details.Text="代理资料仅在本机加密保存。";AddRow(details);
            var actions=new TableLayoutPanel {AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=2,RowCount=1,Dock=DockStyle.Top,Margin=Padding.Empty};
            actions.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,76));actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,24));
            connect.Font=OwnFont(11F,FontStyle.Bold);exit.Font=OwnFont(11F,FontStyle.Bold);connect.Text="连接";connect.Dock=DockStyle.Top;connect.AutoSize=true;connect.Padding=new Padding(8,6,8,6);connect.Margin=new Padding(0,0,10,0);connect.BackColor=Blue;connect.ForeColor=Color.White;
            connect.FlatStyle = FlatStyle.Flat; connect.FlatAppearance.BorderSize = 0;
            exit.Text="退出";exit.Dock=DockStyle.Top;exit.AutoSize=true;exit.Padding=new Padding(6,6,6,6);exit.Margin=Padding.Empty;exit.FlatStyle=FlatStyle.Flat;exit.FlatAppearance.BorderColor=Blue;
            actions.Controls.Add(connect,0,0);actions.Controls.Add(exit,1,0);AddRow(actions);tools.Margin=new Padding(0,4,0,0);AddRow(tools);
            connect.Click += ConnectClicked; exit.Click += delegate {Close();}; FormClosing += ClosingAsync;
            health.Interval = 30000; health.Tick += HealthTick;
            Shown += delegate {
                if (previewing) return;
                string recovered = RecoveryJournal.Recover(); if (recovered.Length != 0) ShowMessage(recovered,true);
                try { var p = ProfileStore.Load(); protocol.SelectedItem = p.Protocol; if (protocol.SelectedIndex < 0) protocol.SelectedIndex = 0; address.Text = p.Address; username.Text = p.Username; password.Text = p.Password; upstreamPort.Value = p.UpstreamPort >= 1 && p.UpstreamPort <= 65535 ? p.UpstreamPort : 65532; }
                catch (AppError error) { ShowMessage(error.Message,true); }
            };
            language.Attach(this);
            Shown+=delegate{if(!previewing)SetLanguage(UiLanguage.SavedEnglish,false);};
            ResumeLayout(true);
            Shown+=delegate{foreach(var frame in inputFrames)frame.FitContent();foreach(var button in new[]{connect,exit,disclosure}){button.AutoSize=false;button.Height=TextRenderer.MeasureText(button.Text,button.Font).Height+button.Padding.Vertical+4;}layout.PerformLayout();int available=Screen.FromControl(this).WorkingArea.Height-24;int desired=layout.Height+(Height-body.ClientSize.Height)+4;Height=Math.Min(available,desired);compactReady=true;};
        }
        internal void SetLanguage(bool english,bool save) {
            SuspendLayout();bool saved=language.Set(english,save);languageToggle.Text=english?"中文":"English";ResumeLayout(true);
            if(!saved)MessageBox.Show(this,english?"Language changed, but could not save the preference.":"语言已切换，但未能保存偏好。",Text);
        }
        Font OwnFont(float size, FontStyle style) {var font=new Font("Microsoft YaHei UI",size,style);ownedFonts.Add(font);return font;}
        void AddRow(Control control) {int row=layout.RowCount++;layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.Controls.Add(control,0,row);}
        void AddInput(TableLayoutPanel card,string text,Control control) {
            int row=card.RowCount;card.RowCount+=2;
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(new Label {Text=text,AutoSize=true,Font=OwnFont(10F,FontStyle.Bold),Margin=new Padding(0,0,0,3)},0,row);
            var frame=new SoftInput {Dock=DockStyle.Top,Padding=new Padding(9,4,9,4),Margin=new Padding(0,0,0,7)};
            control.Dock=DockStyle.Top;control.Font=OwnFont(10F,FontStyle.Regular);control.Margin=Padding.Empty;control.ForeColor=Muted;control.BackColor=frame.BackColor;
            var input=control as TextBox;
            if(input!=null){input.MaxLength=4096;input.BorderStyle=BorderStyle.None;input.AutoSize=false;}
            Action resize=delegate {
                if(input!=null){input.MinimumSize=new Size(0,input.PreferredHeight+2);input.Height=input.PreferredHeight+2;}
                var combo=control as ComboBox;if(combo!=null)combo.ItemHeight=TextRenderer.MeasureText("Ag国",combo.Font).Height+2;
                frame.PerformLayout();
            };
            control.FontChanged+=delegate{resize();};frame.Controls.Add(control);inputFrames.Add(frame);resize();frame.FitContent();card.Controls.Add(frame,0,row+1);
            if(control==protocol){
                protocolReadOnly.Font=protocol.Font;protocolReadOnly.ForeColor=protocol.ForeColor;protocolReadOnly.BackColor=protocol.BackColor;
                frame.Controls.Add(protocolReadOnly);
                frame.Layout+=delegate {
                    protocolReadOnly.Font=protocol.Font;
                    protocolReadOnly.Bounds=new Rectangle(frame.Padding.Left,frame.Padding.Top,Math.Max(0,frame.ClientSize.Width-frame.Padding.Horizontal),protocol.PreferredSize.Height);
                };
                protocol.TextChanged+=delegate {protocolReadOnly.Text=protocol.Text;};
            }
        }
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool ReleaseCapture();
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr handle,int message,IntPtr w,IntPtr l);
        protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;using(var path=SoftShape.Round(new RectangleF(.5F,.5F,Width-1,Height-1),8))using(var pen=new Pen(Color.FromArgb(171,159,197),1.3F))e.Graphics.DrawPath(pen,path);}
        protected override void OnLayout(LayoutEventArgs e) {
            if(layout!=null&&hint!=null&&state!=null&&ip!=null) {
                int width=Math.Max(80,ClientSize.Width-layout.Padding.Horizontal-SystemInformation.VerticalScrollBarWidth);
                hint.MaximumSize=new Size(width,0);state.MaximumSize=new Size(width,0);ip.MaximumSize=new Size(width,0);
            }
            base.OnLayout(e);
            // Re-measure after the full DPI/layout pass; changing preferred heights
            // during WinForms autoscaling can scale padding more than once.
            if(compactReady&&!compactPending&&IsHandleCreated&&!IsDisposed){
                compactPending=true;
                BeginInvoke((Action)delegate{
                    try {if(!IsDisposed){foreach(var frame in inputFrames)frame.FitContent();}}
                    finally {compactPending=false;}
                });
            }
        }
        internal static string FriendlyProgress(string text) {
            var entry=System.Text.RegularExpressions.Regex.Match(text??"",@"^正在检查 IPFLY 入口 (\d+/\d+)(，最多同时检查两个…| 的出口与 Codex 上游…)$");
            if(entry.Success)return "正在探探入口 "+entry.Groups[1].Value+"…";
            if((text??"").StartsWith("此入口未通过："))return "这个入口没接通，再试试～";
            switch(text) {
                case "正在检查 Nano…": case "正在检查 Nano 本地入口…":return "正在敲敲前置代理的小门…";
                case "即将请求管理员授权，用于启用全局接管…":return "请点一下管理员授权～";
                case "正在测试 Nano → IPFLY 代理链…":return "正在牵起代理小通道…";
                case "代理链已通过，正在启用全局接管…":return "正在铺好全局小通道…";
                case "正在核对全局出口…":return "正在核对出口，稍等一下～";
                default:return NeutralText(text);
            }
        }
        static string NeutralText(string text) {
            if(text=="全局已连接。请重新打开 Codex，使用新的连接验证出口。")return "全局已连接";
            // Keep literal filesystem paths intact in actionable error messages.
            return System.Text.RegularExpressions.Regex.Replace(text??"",@"(?<![\\/\w])Nano(?![\\/\w])","前置代理").Replace("IPFLY","出口代理");
        }
        void ShowMessage(string message,bool error) {
            string raw=NeutralText(message); details.Text=raw;
            var lines=raw.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries);
            string summary=lines.Length==0?"":lines[0];
            hint.Text=summary.Length>160?summary.Substring(0,160)+"…":summary;hint.Visible=details.Text.Length!=0;
            if(error){details.Visible=true;disclosure.Text="收起连接详情";}
        }
        void Progress(string message) {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke((Action)delegate { if (!IsDisposed) {state.Text = "正在连接"; ShowMessage(message,false); hint.Text=FriendlyProgress(message);} });
        }
        void ControlsState() {
            bool protocolLocked=busy||connected;
            // Hide the native HWND before disabling it: Windows cannot draw its
            // disabled dropdown button over the read-only owner-painted face.
            protocolReadOnly.Text=protocol.Text;
            protocol.Visible=!protocolLocked;
            protocolReadOnly.Visible=protocolLocked;
            protocol.Enabled = upstreamPort.Enabled = upstreamToggle.Enabled = !protocolLocked;
            address.ReadOnly = username.ReadOnly = password.ReadOnly = busy || connected;
            connect.Enabled = !closing; connect.Text = busy ? "取消连接" : connected ? "断开连接" : "连接";
        }
        void Apply(Packet answer) {
            connected = answer.Connected;
            state.Text = connected ? (answer.Success ? "全局已连接" : "出口核对未通过，正在复查") : "未连接";
            if (connected && !answer.Success && answer.Message != null) {
                if (answer.Message.Contains("Codex")) state.Text = "Codex 上游不可达，正在复查";
                else if (answer.Message.Contains("不一致")) state.Text = "出口不一致，正在复查";
                else if (answer.Message.Contains("超时")) state.Text = "出口检测超时，正在复查";
                else if (answer.Message.Contains("TLS")) state.Text = "TLS 检测失败，正在复查";
                else if (answer.Message.Contains("解析")) state.Text = "域名解析失败，正在复查";
            }
            state.ForeColor = connected && answer.Success ? Color.FromArgb(27, 128, 89) : answer.Success ? Muted : Color.FromArgb(174, 93, 34);
            ip.Text = connected ? "出口 IP：" + answer.Ip : "出口 IP：连接后检测";
            ShowMessage(answer.Message,!answer.Success);
            if(connected&&answer.Success){hint.Text="通道准备好啦，安心出发吧～";hint.Visible=true;}
            if (connected) health.Start(); else health.Stop();
        }
        async void ConnectClicked(object sender, EventArgs args) {
            if (busy) { controller.Cancel(); ShowMessage("正在收好通道、恢复网络…",false); connect.Enabled = false; return; }
            busy = true; ControlsState();
            try {
                if (connected) { state.Text = "正在断开"; ShowMessage("正在收好小通道，马上就好…",false); var task = controller.DisconnectAsync(); active = task; Apply(await task); }
                else {
                    var profile = new ProxyProfile {Protocol = Convert.ToString(protocol.SelectedItem), Address = address.Text, Username = username.Text, Password = password.Text, UpstreamPort = (int)upstreamPort.Value};
                    var task = controller.ConnectAsync(profile, Progress); active = task; Apply(await task);
                }
            } catch (Exception error) {
                Apply(new Packet {Success = false, Message = error is AppError ? error.Message : "连接已取消或通信中断。正在清理连接；下次启动会检查恢复记录。"});
            } finally { active = null; busy = false; if (!IsDisposed) ControlsState(); }
        }
        async void HealthTick(object sender, EventArgs args) {
            if (busy || !connected || closing) return;
            busy = true;
            try { var task = controller.StatusAsync(); active = task; Apply(await task); }
            catch { Apply(new Packet {Success = false, Message = "连接工作进程已中断，请检查网络恢复情况。"}); controller.Dispose(); }
            finally { active = null; busy = false; if (!IsDisposed) ControlsState(); }
        }
        async void ClosingAsync(object sender, FormClosingEventArgs args) {
            if (allowClose) return;
            args.Cancel = true;
            if (closing) return;
            closing = true; health.Stop(); ControlsState(); exit.Enabled = false;
            state.Text = "正在退出"; ShowMessage("正在收好通道、恢复网络…",false);
            try {
                if (busy) { controller.Cancel(); var prior = active; if (prior != null) { try {await prior;} catch { } } }
                var answer = await controller.DisconnectAsync();
                if (answer.Message != null && (answer.Message.Contains("未能") || answer.Message.Contains("未完成"))) MessageBox.Show(this, language.Text(answer.Message), language.Text("请检查网络恢复"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            } catch { MessageBox.Show(this, language.Text("工作进程通信已中断。如果网络异常，请重新打开本工具检查恢复记录。"), language.Text("退出检查"), MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            finally {controller.Dispose(); allowClose = true; Close();}
        }
        protected override void Dispose(bool disposing) {
            if(disposing){health.Dispose();controller.Dispose();}
            base.Dispose(disposing);
            if(disposing){if(avatarImage!=null){avatarImage.Dispose();avatarImage=null;}if(windowIcon!=null){windowIcon.Dispose();windowIcon=null;}foreach(var font in ownedFonts)font.Dispose();ownedFonts.Clear();}
        }
        public void RenderPreview(string destination) {
            if (File.Exists(destination)) throw new AppError("预览文件已存在，未覆盖。");
            previewing = true; ShowInTaskbar = false; Opacity = 0; Show(); Application.DoEvents();
            using (var bitmap = new Bitmap(Width, Height)) { DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size)); bitmap.Save(destination, System.Drawing.Imaging.ImageFormat.Png); }
            Hide();
        }
    }
}
