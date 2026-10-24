using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PeripheralBatteryLite
{
    internal static class UiTheme
    {
        public static readonly Color Background = Color.FromArgb(15, 20, 29);
        public static readonly Color Card = Color.FromArgb(24, 32, 44);
        public static readonly Color CardBorder = Color.FromArgb(41, 53, 71);
        public static readonly Color Text = Color.FromArgb(240, 247, 254);
        public static readonly Color Secondary = Color.FromArgb(149, 168, 188);
        public static readonly Color Accent = Color.FromArgb(94, 212, 242);
        public static readonly Color Track = Color.FromArgb(45, 60, 76);
        public static readonly Color Warning = Color.FromArgb(255, 178, 107);
        public static readonly Color Good = Color.FromArgb(119, 218, 184);

        public static GraphicsPath Rounded(Rectangle rect, int radius)
        {
            int d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
            var p = new GraphicsPath();
            p.AddArc(rect.Left, rect.Top, d, d, 180, 90);
            p.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
            p.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            p.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Color color, Rectangle rect, int radius)
        {
            using (var brush = new SolidBrush(color))
            using (var path = Rounded(rect, radius))
                g.FillPath(brush, path);
        }

        public static void BorderRound(Graphics g, Color color, Rectangle rect, int radius)
        {
            using (var pen = new Pen(color, 1))
            using (var path = Rounded(rect, radius))
                g.DrawPath(pen, path);
        }

        public static void TextAt(Graphics g, string text, float size, bool bold,
            Color color, RectangleF rect, ContentAlignment align)
        {
            using (var font = new Font("Microsoft YaHei UI", size,
                bold ? FontStyle.Bold : FontStyle.Regular))
            using (var brush = new SolidBrush(color))
            using (var fmt = new StringFormat())
            {
                fmt.LineAlignment = StringAlignment.Center;
                fmt.Alignment = align == ContentAlignment.MiddleRight ?
                    StringAlignment.Far : align == ContentAlignment.MiddleCenter ?
                    StringAlignment.Center : StringAlignment.Near;
                fmt.Trimming = StringTrimming.EllipsisCharacter;
                fmt.FormatFlags = StringFormatFlags.NoWrap;
                g.DrawString(text ?? "", font, brush, rect, fmt);
            }
        }
    }

    internal sealed class SoftButton : Button
    {
        public bool Primary { get; set; }
        private bool hovered;

        public SoftButton(bool primary)
        {
            Primary = primary;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hovered = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            var g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color baseColor = !Enabled ? Color.FromArgb(49, 62, 73) :
                Primary ? (hovered ? Color.FromArgb(118, 223, 248) : UiTheme.Accent) :
                (hovered ? Color.FromArgb(63, 77, 96) : Color.FromArgb(39, 52, 69));
            UiTheme.FillRound(g, baseColor, new Rectangle(0, 0, Width - 1, Height - 1), 10);
            UiTheme.TextAt(g, Text, 9.3f, true,
                Primary ? Color.FromArgb(12, 29, 40) : (Enabled ? UiTheme.Text : UiTheme.Secondary),
                new RectangleF(4, 1, Width - 8, Height - 2), ContentAlignment.MiddleCenter);
        }
    }
}
