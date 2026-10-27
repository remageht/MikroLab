using System;
using System.Drawing;
using System.Windows.Forms;

namespace MikroLab.App.Controls
{
    internal sealed class CardPanel : Panel
    {
        public CardPanel()
        {
            BackColor = AppTheme.Surface;
            Padding = new Padding(0);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(AppTheme.Border))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
        }
    }

    internal sealed class SeparatorLine : Control
    {
        public SeparatorLine()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Width = 1;
            BackColor = AppTheme.Border;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
        }
    }
}
