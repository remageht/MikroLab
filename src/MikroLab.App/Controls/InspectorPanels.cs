using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using MikroLab.Core.Machine;

namespace MikroLab.App.Controls
{
    internal sealed class RegisterPanel : Control
    {
        private MachineSnapshot _snapshot;

        public RegisterPanel()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
            BackColor = AppTheme.Surface;
            Font = AppTheme.MonoSmall;
            Padding = new Padding(12);
        }

        public void UpdateSnapshot(MachineSnapshot snapshot)
        {
            _snapshot = snapshot;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.Clear(AppTheme.Surface);

            if (_snapshot == null)
            {
                TextRenderer.DrawText(g, "Нет данных", AppTheme.UiFont, new Point(16, 16), AppTheme.TextDim);
                return;
            }

            string[,] rows =
            {
                { "A", _snapshot.A.ToString("X2") + "h" },
                { "B", _snapshot.B.ToString("X2") + "h" },
                { "C", _snapshot.C.ToString("X2") + "h" },
                { "D", _snapshot.D.ToString("X2") + "h" },
                { "E", _snapshot.E.ToString("X2") + "h" },
                { "H", _snapshot.H.ToString("X2") + "h" },
                { "L", _snapshot.L.ToString("X2") + "h" },
                { "M", _snapshot.M.ToString("X2") + "h" }
            };

            int top = 12;
            int rowHeight = 25;
            int colWidth = Math.Max(150, Width / 2 - 8);
            int left = 14;

            TextRenderer.DrawText(g, "РЕГИСТРЫ", AppTheme.SmallBoldFont, new Point(left, top), AppTheme.TextDim);
            top += 26;

            using (var line = new Pen(AppTheme.BorderSoft))
            {
                g.DrawLine(line, left, top - 6, Width - 14, top - 6);
            }

            for (int i = 0; i < rows.GetLength(0); i++)
            {
                int column = i / 4;
                int row = i % 4;
                int x = left + column * colWidth;
                int y = top + row * rowHeight;
                bool pair = i == 7;
                TextRenderer.DrawText(g, rows[i, 0], AppTheme.SmallFont, new Point(x, y), AppTheme.TextMuted);
                TextRenderer.DrawText(g, rows[i, 1], AppTheme.MonoBold, new Point(x + 26, y - 1),
                    pair ? AppTheme.Amber : AppTheme.Text);
            }

            top += 4 * rowHeight + 12;
            using (var line = new Pen(AppTheme.BorderSoft))
            {
                g.DrawLine(line, left, top - 6, Width - 14, top - 6);
            }

            TextRenderer.DrawText(g, "PC", AppTheme.SmallFont, new Point(left, top), AppTheme.TextMuted);
            TextRenderer.DrawText(g, _snapshot.PC.ToString("X4") + "h", AppTheme.MonoBold,
                new Point(left + 26, top - 1), AppTheme.Cyan);
            TextRenderer.DrawText(g, "SP", AppTheme.SmallFont, new Point(left + colWidth, top), AppTheme.TextMuted);
            TextRenderer.DrawText(g, _snapshot.SP.ToString("X4") + "h", AppTheme.MonoBold,
                new Point(left + colWidth + 26, top - 1), AppTheme.Cyan);

            top += rowHeight + 10;
            TextRenderer.DrawText(g, "ФЛАГИ", AppTheme.SmallBoldFont, new Point(left, top), AppTheme.TextDim);
            top += 24;

            DrawFlag(g, left, top, "C", _snapshot.Flags.C);
            DrawFlag(g, left + 52, top, "Z", _snapshot.Flags.Z);
            DrawFlag(g, left + 104, top, "S", _snapshot.Flags.S);
            DrawFlag(g, left + 156, top, "P", _snapshot.Flags.P);
            DrawFlag(g, left + 208, top, "AC", _snapshot.Flags.AC);

            top += 40;
            TextRenderer.DrawText(g, "ТАКТЫ", AppTheme.SmallBoldFont, new Point(left, top), AppTheme.TextDim);
            TextRenderer.DrawText(g, _snapshot.Cycles.ToString("N0"), AppTheme.MonoSmall,
                new Point(left + 66, top - 1), AppTheme.Text);
        }

        private static void DrawFlag(Graphics g, int x, int y, string name, bool on)
        {
            Rectangle bounds = new Rectangle(x, y, 38, 26);
            AppTheme.FillRounded(g, bounds, 6, on ? AppTheme.AccentSoft : AppTheme.SurfaceRaised);
            AppTheme.DrawRoundedBorder(g, bounds, 6, on ? AppTheme.Accent : AppTheme.BorderSoft);
            TextRenderer.DrawText(g, name, AppTheme.SmallBoldFont, bounds, on ? AppTheme.Text : AppTheme.TextDim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    internal sealed class StackPanel : UserControl
    {
        private readonly Label _depth;
        private readonly ListBox _list;

        public StackPanel()
        {
            BackColor = AppTheme.Surface;

            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 36,
                BackColor = AppTheme.Surface,
                Padding = new Padding(12, 8, 12, 4)
            };
            Label title = AppTheme.Label("СТЕК ВЫЗОВОВ", AppTheme.TextDim, AppTheme.SmallBoldFont);
            title.Location = new Point(12, 10);
            _depth = AppTheme.Label("Глубина: 0", AppTheme.TextMuted, AppTheme.SmallFont);
            _depth.AutoSize = false;
            _depth.TextAlign = ContentAlignment.MiddleRight;
            _depth.Dock = DockStyle.Right;
            _depth.Width = 110;
            header.Controls.Add(title);
            header.Controls.Add(_depth);

            _list = new ListBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                BackColor = AppTheme.Surface,
                ForeColor = AppTheme.Text,
                Font = AppTheme.MonoSmall,
                IntegralHeight = false,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 24
            };
            _list.DrawItem += (sender, args) =>
            {
                if (args.Index < 0) return;
                bool selected = (args.State & DrawItemState.Selected) == DrawItemState.Selected;
                using (var brush = new SolidBrush(selected ? AppTheme.AccentSoft : AppTheme.Surface))
                {
                    args.Graphics.FillRectangle(brush, args.Bounds);
                }
                string text = _list.Items[args.Index].ToString();
                TextRenderer.DrawText(args.Graphics, text, AppTheme.MonoSmall,
                    new Rectangle(args.Bounds.X + 8, args.Bounds.Y, args.Bounds.Width - 8, args.Bounds.Height),
                    selected ? AppTheme.Text : AppTheme.TextMuted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
            };

            Controls.Add(_list);
            Controls.Add(header);
        }

        public void UpdateStack(MachineSnapshot snapshot, ushort[] stack)
        {
            int depth = snapshot != null ? snapshot.CallStackDepth : 0;
            _depth.Text = "Глубина: " + depth;
            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                if (stack == null || stack.Length == 0)
                {
                    _list.Items.Add("— стек пуст —");
                }
                else
                {
                    for (int i = 0; i < stack.Length; i++)
                    {
                        _list.Items.Add((i + 1) + ".  RET → " + stack[i].ToString("X4") + "h");
                    }
                }
            }
            finally
            {
                _list.EndUpdate();
            }
        }
    }
}
