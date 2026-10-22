using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
namespace IPFlyLink {
    internal static class SoftShape {
        internal static void Star(Graphics g,float x,float y,float size,Color color){
            var pts=new PointF[8];for(int i=0;i<8;i++){double angle=i*Math.PI/4-Math.PI/2;float r=i%2==0?size:size*.32F;pts[i]=new PointF(x+(float)Math.Cos(angle)*r,y+(float)Math.Sin(angle)*r);}
            using(var b=new SolidBrush(color))g.FillPolygon(b,pts);
        }
        internal static GraphicsPath Round(RectangleF r,float radius) {
            float d=Math.Min(radius*2,Math.Min(r.Width,r.Height));
            var path=new GraphicsPath();
            path.AddArc(r.X,r.Y,d,d,180,90);path.AddArc(r.Right-d,r.Y,d,d,270,90);
            path.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);path.AddArc(r.X,r.Bottom-d,d,d,90,90);path.CloseFigure();return path;
        }
        internal static void Surface(Control c,Graphics g,Color fill,Color border,int radius) {
            g.SmoothingMode=SmoothingMode.AntiAlias;
            g.Clear(c.Parent==null?Color.FromArgb(253,251,254):c.Parent.BackColor);
            if(c.Width<2||c.Height<2)return;
            using(var path=Round(new RectangleF(.5F,.5F,c.Width-1,c.Height-1),radius))
            using(var brush=new SolidBrush(fill))using(var pen=new Pen(border)) {g.FillPath(brush,path);g.DrawPath(pen,path);}
        }
    }
    internal sealed class SoftCard : TableLayoutPanel {
        public Color BorderColor=Color.FromArgb(232,225,248);
        public SoftCard(){DoubleBuffered=true;BackColor=Color.White;}
        protected override void OnPaintBackground(PaintEventArgs e){SoftShape.Surface(this,e.Graphics,BackColor,BorderColor,12);}
    }
    internal sealed class SoftInput : Panel {
        public SoftInput(){DoubleBuffered=true;BackColor=Color.FromArgb(246,245,250);}
        protected override void OnPaintBackground(PaintEventArgs e){SoftShape.Surface(this,e.Graphics,BackColor,Color.FromArgb(222,216,236),7);}
        internal void FitContent(){
            if(Controls.Count==0)return;
                var child=Controls[0];var text=child as TextBox;
                if(text!=null)text.MinimumSize=Size.Empty;
                int content=text==null?child.PreferredSize.Height:Math.Max(text.PreferredHeight+2,text.PreferredSize.Height);
                if(text!=null){text.MinimumSize=new Size(0,content);text.Height=content;}
                Height=content+Padding.Vertical;
        }
    }
    internal sealed class SoftHint : Label {
        public SoftHint(){SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer,true);}
        protected override void OnPaintBackground(PaintEventArgs e){SoftShape.Surface(this,e.Graphics,BackColor,Color.FromArgb(227,217,255),10);}
        protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;float scale=Font.SizeInPoints/10F;float x=21*scale,y=Height/2F;var color=Color.FromArgb(155,117,232);SoftShape.Star(e.Graphics,x,y,7*scale,color);SoftShape.Star(e.Graphics,x+10*scale,y-7*scale,3.4F*scale,color);SoftShape.Star(e.Graphics,x+10*scale,y+6*scale,3.4F*scale,color);}
    }
    internal sealed class SoftStatus : Label {
        protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;float d=Math.Min(Height-2,Font.SizeInPoints*1.25F);using(var b=new SolidBrush(ForeColor))e.Graphics.FillEllipse(b,1,(Height-d)/2,d,d);}
    }
    internal sealed class SoftButton : Button {
        public bool Primary;
        bool hover;
        public SoftButton(){SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer,true);FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;Cursor=Cursors.Hand;}
        protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
        protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
        protected override void OnPaint(PaintEventArgs e){
            var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Parent.BackColor);
            using(var path=SoftShape.Round(new RectangleF(1,1,Width-2,Height-2),9)) {
                Color a=Primary?Color.FromArgb(174,150,235):Parent.BackColor,b=Primary?Color.FromArgb(161,139,226):Parent.BackColor;
                if(hover&&Enabled){a=Primary?Color.FromArgb(164,138,227):Color.FromArgb(246,242,253);b=a;}
                using(var fill=new LinearGradientBrush(ClientRectangle,a,b,35F))g.FillPath(fill,path);
                using(var pen=new Pen(Color.FromArgb(180,157,240),1.2F))g.DrawPath(pen,path);
            }
            TextRenderer.DrawText(g,Text,Font,ClientRectangle,Enabled?(Primary?Color.White:ForeColor):Color.FromArgb(151,139,176),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
            if(Focused&&ShowFocusCues){var r=ClientRectangle;r.Inflate(-6,-6);ControlPaint.DrawFocusRectangle(g,r);}
        }
    }
    internal sealed class SoftCombo : ComboBox {
        public SoftCombo(){DrawMode=DrawMode.OwnerDrawFixed;FlatStyle=FlatStyle.Flat;BackColor=Color.FromArgb(246,245,250);}
        protected override void OnDrawItem(DrawItemEventArgs e){
            if(e.Index<0)return;
            using(var brush=new SolidBrush((e.State&DrawItemState.Selected)!=0&&DroppedDown?Color.FromArgb(234,225,250):BackColor))e.Graphics.FillRectangle(brush,e.Bounds);
            TextRenderer.DrawText(e.Graphics,GetItemText(Items[e.Index]),Font,e.Bounds,ForeColor,TextFormatFlags.Left|TextFormatFlags.VerticalCenter);
        }
        protected override void WndProc(ref Message m){
            base.WndProc(ref m);
            if((m.Msg==0xF||m.Msg==0x317||m.Msg==0x318)&&IsHandleCreated)using(var g=m.Msg==0xF?Graphics.FromHwnd(Handle):Graphics.FromHdc(m.WParam)) {
                using(var background=new SolidBrush(BackColor))g.FillRectangle(background,ClientRectangle);var rect=ClientRectangle;rect.Width-=28;
                TextRenderer.DrawText(g,Text,Font,rect,ForeColor,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
                g.SmoothingMode=SmoothingMode.AntiAlias;
                float x=Width-12,y=Height/2F;
                using(var pen=new Pen(Color.FromArgb(139,125,166),1.6F))g.DrawLines(pen,new[]{new PointF(x-5,y-2),new PointF(x,y+3),new PointF(x+5,y-2)});
                if(Focused&&ShowFocusCues){var focus=rect;focus.Inflate(-1,-1);ControlPaint.DrawFocusRectangle(g,focus);}
            }
        }
    }
    internal sealed class SoftPortrait : PictureBox {
        protected override void OnPaint(PaintEventArgs e){
            var saved=e.Graphics.Save();e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(var path=SoftShape.Round(new RectangleF(0,0,Width,Height),14))e.Graphics.SetClip(path);
            base.OnPaint(e);e.Graphics.Restore(saved);
        }
    }
}
