using System.Drawing;
using System.Windows.Forms;

namespace IPFlyLink {
    // Compiled attribution: no external asset, localization key or user setting.
    // This is persistent branding, not a claim of tamper-proof executable code.
    internal sealed class BrandMark : Control {
        internal const string Attribution = "@GFVNIP";
        internal BrandMark() {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            TabStop=false;
            AccessibleName=Attribution;
            AccessibleRole=AccessibleRole.StaticText;
        }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);
            var bounds=new Rectangle(Padding.Left,Padding.Top,
                Width-Padding.Horizontal,Height-Padding.Vertical);
            TextRenderer.DrawText(e.Graphics,Attribution,Font,bounds,ForeColor,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }
    }
}
