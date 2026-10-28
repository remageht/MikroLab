using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using MikroLab.Core.Cpu;
using MikroLab.Core.Machine;

namespace MikroLab.App.Controls
{
    /// <summary>
    /// Slow, educational animation of RLC/RRC/RAL/RAR. Cells show the carry
    /// flag and accumulator bits; dots travel from their old position to the
    /// new one so a student can see the bit that enters the carry and the bit
    /// that wraps around.
    /// </summary>
    internal sealed class RotationView : Control
    {
        private const int AnimationFrames = 18;
        private const int FrameMilliseconds = 28;
        private const int CellSize = 22;
        private const int CellGap = 4;

        private readonly Timer _timer;
        private MachineSnapshot _previous;
        private MachineSnapshot _current;
        private RotationKind? _kind;
        private int _frame;

        public RotationView()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
            BackColor = AppTheme.Surface;
            Height = 86;

            _timer = new Timer { Interval = FrameMilliseconds };
            _timer.Tick += (sender, args) =>
            {
                _frame++;
                if (_frame >= AnimationFrames)
                {
                    _timer.Stop();
                    _frame = AnimationFrames;
                }
                Invalidate();
            };
        }

        /// <summary>
        /// Starts an animation when the latest executed instruction is a
        /// rotation and the accumulator or carry actually changed.
        /// </summary>
        public void Observe(MachineSnapshot previous, MachineSnapshot current, string instruction)
        {
            if (previous == null || current == null) return;

            RotationKind? kind = RotationMath.FromMnemonic(instruction);
            if (!kind.HasValue) return;
            if (previous.A == current.A && previous.Flags.C == current.Flags.C) return;

            _previous = previous;
            _current = current;
            _kind = kind;
            _frame = 0;
            _timer.Stop();
            _timer.Start();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            if (!_kind.HasValue || _current == null)
            {
                TextRenderer.DrawText(
                    g,
                    "СДВИГИ · RLC / RRC / RAL / RAR",
                    AppTheme.SmallBoldFont,
                    new Point(14, 8),
                    AppTheme.TextDim);
                TextRenderer.DrawText(
                    g,
                    "Выполните команду сдвига, чтобы увидеть перенос бита",
                    AppTheme.SmallFont,
                    new Rectangle(14, 30, Width - 28, 22),
                    AppTheme.TextDim,
                    TextFormatFlags.Left | TextFormatFlags.NoPadding);
                return;
            }

            string title = _kind == RotationKind.LeftCircular ? "RLC"
                : _kind == RotationKind.RightCircular ? "RRC"
                : _kind == RotationKind.LeftThroughCarry ? "RAL"
                : "RAR";
            TextRenderer.DrawText(g, "СДВИГ · " + title, AppTheme.SmallBoldFont,
                new Point(14, 6), AppTheme.Accent);
            TextRenderer.DrawText(g, Description(_kind.Value), AppTheme.SmallFont,
                new Rectangle(96, 7, Width - 108, 18), AppTheme.TextDim,
                TextFormatFlags.Left | TextFormatFlags.NoPadding);

            int cells = 9;
            int width = cells * CellSize + (cells - 1) * CellGap;
            int left = Math.Max(14, (Width - width) / 2);
            int top = 30;

            for (int i = 0; i < cells; i++)
            {
                var bounds = new Rectangle(left + i * (CellSize + CellGap), top, CellSize, CellSize);
                AppTheme.FillRounded(g, bounds, 5, i == 0 ? AppTheme.SurfaceRaised : AppTheme.EditorBackground);
                AppTheme.DrawRoundedBorder(g, bounds, 5, AppTheme.BorderSoft);

                string label = i == 0 ? "C" : (8 - i).ToString();
                TextRenderer.DrawText(g, label, AppTheme.SmallFont, bounds, AppTheme.TextDim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

            float t = _frame / (float)AnimationFrames;
            t = t * t * (3f - 2f * t);

            // Static target bits are faint; travelling dots are bright.
            DrawBit(g, left, top, 0, _current.Flags.C, AppTheme.TextDim);
            for (int bit = 0; bit < 8; bit++)
            {
                DrawBit(g, left, top, 1 + (7 - bit), ((_current.A >> bit) & 1) != 0, AppTheme.TextDim);
            }

            byte oldA = _previous.A;
            bool oldCarry = _previous.Flags.C;

            for (int bit = 0; bit < 8; bit++)
            {
                int source = 1 + (7 - bit);
                int destination = Destination(_kind.Value, source);
                bool value = ((oldA >> bit) & 1) != 0;
                DrawMovingDot(g, left, top, source, destination, t, value);
            }

            if (_kind == RotationKind.LeftThroughCarry)
            {
                DrawMovingDot(g, left, top, 0, 8, t, oldCarry);
            }
            else if (_kind == RotationKind.RightThroughCarry)
            {
                DrawMovingDot(g, left, top, 0, 1, t, oldCarry);
            }
            else if (_kind == RotationKind.LeftCircular)
            {
                // A7 goes to the carry and also wraps to A0.
                DrawMovingDot(g, left, top, 1, 0, t, (oldA & 0x80) != 0);
            }
            else
            {
                // A0 goes to the carry and also wraps to A7.
                DrawMovingDot(g, left, top, 8, 0, t, (oldA & 0x01) != 0);
            }

            TextRenderer.DrawText(g, "старое", AppTheme.SmallFont,
                new Point(left, top + CellSize + 4), AppTheme.TextDim);
            TextRenderer.DrawText(g, "→ новое", AppTheme.SmallFont,
                new Point(left + width - 56, top + CellSize + 4), AppTheme.TextDim);
        }

        private static string Description(RotationKind kind)
        {
            switch (kind)
            {
                case RotationKind.LeftCircular: return "бит 7 → C и → бит 0";
                case RotationKind.RightCircular: return "бит 0 → C и → бит 7";
                case RotationKind.LeftThroughCarry: return "бит 7 → C, старый C → бит 0";
                default: return "бит 0 → C, старый C → бит 7";
            }
        }

        /// <summary>Destination display index for an accumulator bit (1..8).</summary>
        private static int Destination(RotationKind kind, int source)
        {
            switch (kind)
            {
                case RotationKind.LeftCircular:
                case RotationKind.LeftThroughCarry:
                    if (source == 1) return kind == RotationKind.LeftCircular ? 8 : 0;
                    return source - 1;
                default:
                    if (source == 8) return kind == RotationKind.RightCircular ? 1 : 0;
                    return source + 1;
            }
        }

        private static void DrawBit(Graphics g, int left, int top, int cell, bool value, Color color)
        {
            int x = left + cell * (CellSize + CellGap) + CellSize / 2;
            int y = top + CellSize / 2;
            if (value)
            {
                using (var brush = new SolidBrush(Color.FromArgb(90, color)))
                {
                    g.FillEllipse(brush, x - 4, y - 4, 8, 8);
                }
            }
        }

        private static void DrawMovingDot(
            Graphics g,
            int left,
            int top,
            int source,
            int destination,
            float t,
            bool value)
        {
            float sourceX = left + source * (CellSize + CellGap) + CellSize / 2f;
            float destinationX = left + destination * (CellSize + CellGap) + CellSize / 2f;
            float x = sourceX + (destinationX - sourceX) * t;
            float y = top + CellSize / 2f;

            Color color = value ? AppTheme.Accent : AppTheme.TextDim;
            using (var pen = new Pen(Color.FromArgb(value ? 220 : 110, color), 2f))
            {
                g.DrawEllipse(pen, x - 6, y - 6, 12, 12);
            }
            if (value)
            {
                using (var brush = new SolidBrush(color))
                {
                    g.FillEllipse(brush, x - 3, y - 3, 6, 6);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Stop();
                _timer.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
