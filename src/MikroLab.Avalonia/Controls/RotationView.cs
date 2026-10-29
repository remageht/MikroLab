using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using MikroLab.Core.Cpu;
using MikroLab.Core.Machine;

namespace MikroLab.Studio.Controls
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
        private const int CellSize = 22;
        private const int CellGap = 4;

        private readonly DispatcherTimer _timer;
        private MachineSnapshot _previous;
        private MachineSnapshot _current;
        private RotationKind? _kind;
        private int _frame;

        public RotationView()
        {
            Height = 86;
            ClipToBounds = true;

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(28) };
            _timer.Tick += (sender, args) =>
            {
                _frame++;
                if (_frame >= AnimationFrames)
                {
                    _timer.Stop();
                    _frame = AnimationFrames;
                }
                InvalidateVisual();
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
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            context.DrawRectangle(AppTheme.Brush(AppTheme.Surface), null, new Rect(0, 0, Bounds.Width, Bounds.Height));

            if (!_kind.HasValue || _current == null)
            {
                AppTheme.DrawText(context, "СДВИГИ · RLC / RRC / RAL / RAR",
                    new Point(14, 8), AppTheme.SmallSize, AppTheme.Brush(AppTheme.TextDim), true);
                AppTheme.DrawText(context, "Выполните команду сдвига, чтобы увидеть перенос бита",
                    new Point(14, 30), AppTheme.SmallSize, AppTheme.Brush(AppTheme.TextDim));
                return;
            }

            string title = _kind == RotationKind.LeftCircular ? "RLC"
                : _kind == RotationKind.RightCircular ? "RRC"
                : _kind == RotationKind.LeftThroughCarry ? "RAL"
                : "RAR";
            AppTheme.DrawText(context, "СДВИГ · " + title,
                new Point(14, 6), AppTheme.SmallSize, AppTheme.Brush(AppTheme.Accent), true);
            AppTheme.DrawText(context, Description(_kind.Value),
                new Point(96, 7), AppTheme.SmallSize, AppTheme.Brush(AppTheme.TextDim));

            const int cells = 9;
            int width = cells * CellSize + (cells - 1) * CellGap;
            int left = Math.Max(14, (int)((Bounds.Width - width) / 2));
            int top = 30;

            for (int i = 0; i < cells; i++)
            {
                var bounds = new Rect(left + i * (CellSize + CellGap), top, CellSize, CellSize);
                AppTheme.FillRounded(context, bounds, 5,
                    AppTheme.Brush(i == 0 ? AppTheme.SurfaceRaised : AppTheme.EditorBackground));
                AppTheme.DrawRoundedBorder(context, bounds, 5, AppTheme.Pen(AppTheme.BorderSoft));

                string label = i == 0 ? "C" : (8 - i).ToString();
                FormattedText text = AppTheme.MakeText(label, AppTheme.SmallSize, AppTheme.Brush(AppTheme.TextDim));
                context.DrawText(text, new Point(
                    bounds.X + (CellSize - text.Width) / 2,
                    bounds.Y + (CellSize - text.Height) / 2));
            }

            double t = _frame / (double)AnimationFrames;
            t = t * t * (3 - 2 * t);

            DrawBit(context, left, top, 0, _current.Flags.C, AppTheme.TextDim);
            for (int bit = 0; bit < 8; bit++)
            {
                DrawBit(context, left, top, 1 + (7 - bit), ((_current.A >> bit) & 1) != 0, AppTheme.TextDim);
            }

            byte oldA = _previous.A;
            bool oldCarry = _previous.Flags.C;

            for (int bit = 0; bit < 8; bit++)
            {
                int source = 1 + (7 - bit);
                int destination = Destination(_kind.Value, source);
                bool value = ((oldA >> bit) & 1) != 0;
                DrawMovingDot(context, left, top, source, destination, t, value);
            }

            if (_kind == RotationKind.LeftThroughCarry)
            {
                DrawMovingDot(context, left, top, 0, 8, t, oldCarry);
            }
            else if (_kind == RotationKind.RightThroughCarry)
            {
                DrawMovingDot(context, left, top, 0, 1, t, oldCarry);
            }
            else if (_kind == RotationKind.LeftCircular)
            {
                DrawMovingDot(context, left, top, 1, 0, t, (oldA & 0x80) != 0);
            }
            else
            {
                DrawMovingDot(context, left, top, 8, 0, t, (oldA & 0x01) != 0);
            }

            AppTheme.DrawText(context, "старое",
                new Point(left, top + CellSize + 4), AppTheme.SmallSize, AppTheme.Brush(AppTheme.TextDim));
            AppTheme.DrawText(context, "→ новое",
                new Point(left + width - 56, top + CellSize + 4), AppTheme.SmallSize, AppTheme.Brush(AppTheme.TextDim));
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

        private static void DrawBit(DrawingContext context, int left, int top, int cell, bool value, Color color)
        {
            double x = left + cell * (CellSize + CellGap) + CellSize / 2.0;
            double y = top + CellSize / 2.0;
            if (value)
            {
                context.DrawEllipse(AppTheme.Brush(Color.FromArgb(90, color.R, color.G, color.B)), null,
                    new Point(x, y), 4, 4);
            }
        }

        private static void DrawMovingDot(
            DrawingContext context,
            int left,
            int top,
            int source,
            int destination,
            double t,
            bool value)
        {
            double sourceX = left + source * (CellSize + CellGap) + CellSize / 2.0;
            double destinationX = left + destination * (CellSize + CellGap) + CellSize / 2.0;
            double x = sourceX + (destinationX - sourceX) * t;
            double y = top + CellSize / 2.0;

            Color color = value ? AppTheme.Accent : AppTheme.TextDim;
            var pen = new Pen(AppTheme.Brush(Color.FromArgb(value ? (byte)220 : (byte)110, color.R, color.G, color.B)), 2);
            context.DrawEllipse(null, pen, new Point(x, y), 6, 6);
            if (value)
            {
                context.DrawEllipse(AppTheme.Brush(color), null, new Point(x, y), 3, 3);
            }
        }
    }
}
