using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
namespace IPFlyLink {
    // A plain painted control has no native ComboBox button or dropdown window.
    // Used only while the selected protocol must remain locked.
    internal sealed class SoftProtocolReadOnly : Control {
        internal SoftProtocolReadOnly(){
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|
                ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
            SetStyle(ControlStyles.Selectable,false);
            TabStop=false;AccessibleRole=AccessibleRole.StaticText;
        }
        protected override void OnTextChanged(System.EventArgs e){base.OnTextChanged(e);Invalidate();}
        protected override void OnPaint(PaintEventArgs e){
            base.OnPaint(e);
            var text=ClientRectangle;text.Width=System.Math.Max(0,text.Width-28);
            TextRenderer.DrawText(e.Graphics,Text,Font,text,ForeColor,
                TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            float x=Width-12,y=Height/2F;
            using(var pen=new Pen(Color.FromArgb(139,125,166),1.6F))
                e.Graphics.DrawLines(pen,new[]{new PointF(x-5,y-2),new PointF(x,y+3),new PointF(x+5,y-2)});
        }
    }
}
