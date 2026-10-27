using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using MikroLab.Core.Machine;

namespace MikroLab.App.Controls
{
    internal sealed class SevenSegmentDisplay : Control
    {
        private byte[] _digits = new byte[8];

        public SevenSegmentDisplay()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
            BackColor = AppTheme.EditorBackground;
            Height = 92;
        }

        public void SetDigits(byte[] digits)
        {
            _digits = new byte[8];
            if (digits != null)
            {
                Array.Copy(digits, _digits, Math.Min(8, digits.Length));
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            int gap = 8;
            int digitWidth = Math.Max(18, (Width - gap * 9) / 8);
            int digitHeight = Math.Max(45, Height - 12);
            int top = 6;
            int x = gap / 2;

            for (int i = 0; i < 8; i++)
            {
                var bounds = new Rectangle(x, top, digitWidth, digitHeight);
                DrawDigit(g, bounds, _digits[i]);
                x += digitWidth + gap;
                if (i == 3) x += 10;
            }
        }

        private static void DrawDigit(Graphics g, Rectangle bounds, byte segments)
        {
            int thickness = Math.Max(4, bounds.Width / 8);
            int half = bounds.Height / 2;
            Rectangle a = new Rectangle(bounds.X + thickness, bounds.Y, bounds.Width - thickness * 2, thickness);
            Rectangle gseg = new Rectangle(bounds.X + thickness, bounds.Y + half - thickness / 2,
                bounds.Width - thickness * 2, thickness);
            Rectangle d = new Rectangle(bounds.X + thickness, bounds.Bottom - thickness,
                bounds.Width - thickness * 2, thickness);
            Rectangle f = new Rectangle(bounds.X, bounds.Y + thickness, thickness, half - thickness);
            Rectangle b = new Rectangle(bounds.Right - thickness, bounds.Y + thickness, thickness, half - thickness);
            Rectangle e = new Rectangle(bounds.X, bounds.Y + half + 2, thickness, half - thickness - 2);
            Rectangle c = new Rectangle(bounds.Right - thickness, bounds.Y + half + 2, thickness, half - thickness - 2);

            DrawSegment(g, a, (segments & 0x01) != 0, thickness);
            DrawSegment(g, b, (segments & 0x02) != 0, thickness);
            DrawSegment(g, c, (segments & 0x04) != 0, thickness);
            DrawSegment(g, d, (segments & 0x08) != 0, thickness);
            DrawSegment(g, e, (segments & 0x10) != 0, thickness);
            DrawSegment(g, f, (segments & 0x20) != 0, thickness);
            DrawSegment(g, gseg, (segments & 0x40) != 0, thickness);

            if ((segments & 0x80) != 0)
            {
                int dot = Math.Max(3, thickness);
                using (var brush = new SolidBrush(AppTheme.Amber))
                {
                    g.FillEllipse(brush, bounds.Right - dot, bounds.Bottom - dot, dot, dot);
                }
            }
        }

        private static void DrawSegment(Graphics g, Rectangle bounds, bool on, int radius)
        {
            Color color = on ? AppTheme.Amber : Color.FromArgb(34, 38, 47);
            AppTheme.FillRounded(g, bounds, Math.Max(1, radius / 2), color);
        }
    }

    internal sealed class LedBar : Control
    {
        private byte _value;

        public LedBar()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
            BackColor = AppTheme.Surface;
            Height = 58;
        }

        public void SetValue(byte value)
        {
            _value = value;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            int count = 8;
            int cell = Math.Max(20, (Width - 8) / count);
            int diameter = Math.Min(20, Math.Max(12, cell - 12));
            int start = 4 + (cell - diameter) / 2;

            for (int i = 0; i < count; i++)
            {
                bool on = (_value & (1 << i)) != 0;
                int x = start + i * cell;
                int y = 8;

                if (on)
                {
                    using (var glow = new SolidBrush(Color.FromArgb(45, AppTheme.Green)))
                    {
                        g.FillEllipse(glow, x - 4, y - 4, diameter + 8, diameter + 8);
                    }
                }

                using (var brush = new SolidBrush(on ? AppTheme.Green : Color.FromArgb(35, 40, 48)))
                {
                    g.FillEllipse(brush, x, y, diameter, diameter);
                }
                using (var pen = new Pen(on ? AppTheme.Mix(AppTheme.Green, Color.White, 0.35) : AppTheme.Border))
                {
                    g.DrawEllipse(pen, x, y, diameter, diameter);
                }

                var label = new Rectangle(x - 6, y + diameter + 3, diameter + 12, 16);
                TextRenderer.DrawText(g, "D" + i, AppTheme.SmallFont, label,
                    on ? AppTheme.Green : AppTheme.TextDim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
    }

    internal sealed class ToggleSwitch : CheckBox
    {
        private bool _hover;

        public ToggleSwitch()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
            AutoSize = false;
            Height = 30;
            BackColor = Color.Transparent;
            ForeColor = AppTheme.Text;
            Font = AppTheme.SmallFont;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs eventargs)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(eventargs);
        }

        protected override void OnMouseLeave(EventArgs eventargs)
        {
            _hover = false;
            Invalidate();
            base.OnMouseLeave(eventargs);
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            Graphics g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : AppTheme.Surface);

            Rectangle textBounds = new Rectangle(0, 0, Math.Max(10, Width - 54), Height);
            TextRenderer.DrawText(g, Text, Font, textBounds, Enabled ? ForeColor : AppTheme.TextDim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            int trackWidth = 38;
            int trackHeight = 20;
            int x = Width - trackWidth - 2;
            int y = (Height - trackHeight) / 2;
            Rectangle track = new Rectangle(x, y, trackWidth, trackHeight);
            Color trackColor = Checked ? AppTheme.Accent : (_hover ? AppTheme.SurfaceHover : AppTheme.SurfaceRaised);
            AppTheme.FillRounded(g, track, trackHeight / 2, trackColor);
            AppTheme.DrawRoundedBorder(g, track, trackHeight / 2, Checked ? AppTheme.Accent : AppTheme.Border);

            int knob = trackHeight - 6;
            int knobX = Checked ? x + trackWidth - knob - 3 : x + 3;
            using (var brush = new SolidBrush(Checked ? AppTheme.Text : AppTheme.TextMuted))
            {
                g.FillEllipse(brush, knobX, y + 3, knob, knob);
            }
        }
    }

    internal sealed class VoltageSlider : Control
    {
        private double _value;
        private bool _dragging;
        private bool _hover;

        public int Channel { get; set; }
        public event Action<int, double> ValueChanged;

        public VoltageSlider()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
            BackColor = AppTheme.Surface;
            Height = 44;
            Cursor = Cursors.Hand;
        }

        public double Value
        {
            get => _value;
            set
            {
                _value = Math.Max(0, Math.Min(10, value));
                Invalidate();
            }
        }

        public void SetValueSilent(double value)
        {
            _value = Math.Max(0, Math.Min(10, value));
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            _dragging = true;
            Capture = true;
            UpdateFromX(e.X);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging) UpdateFromX(e.X);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = false;
            Capture = false;
        }

        private void UpdateFromX(int x)
        {
            int left = 88;
            int right = Math.Max(left + 20, Width - 62);
            double ratio = (double)(x - left) / (right - left);
            ratio = Math.Max(0, Math.Min(1, ratio));
            _value = Math.Round(ratio * 10.0, 1);
            Invalidate();
            ValueChanged?.Invoke(Channel, _value);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            int left = 88;
            int right = Math.Max(left + 20, Width - 62);
            int y = Height / 2 - 2;

            TextRenderer.DrawText(g, "Д" + (Channel + 1), AppTheme.SmallBoldFont,
                new Rectangle(0, 0, 34, Height), AppTheme.TextMuted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            Rectangle track = new Rectangle(left, y - 2, right - left, 5);
            AppTheme.FillRounded(g, track, 3, AppTheme.SurfaceRaised);
            int filled = (int)((right - left) * (_value / 10.0));
            if (filled > 0)
            {
                AppTheme.FillRounded(g, new Rectangle(left, y - 2, filled, 5), 3, AppTheme.Accent);
            }

            int knobX = left + filled;
            int knobSize = _hover || _dragging ? 16 : 14;
            using (var brush = new SolidBrush(AppTheme.Text))
            {
                g.FillEllipse(brush, knobX - knobSize / 2, y - knobSize / 2, knobSize, knobSize);
            }
            using (var pen = new Pen(AppTheme.Accent, 2))
            {
                g.DrawEllipse(pen, knobX - knobSize / 2, y - knobSize / 2, knobSize, knobSize);
            }

            TextRenderer.DrawText(g, _value.ToString("0.0") + " В", AppTheme.MonoSmall,
                new Rectangle(Width - 58, 0, 58, Height), AppTheme.Text,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    /// <summary>
    /// Shared device dashboard: lamps, seven-segment indicators, tumblers and
    /// analog sensors. The same control is used by Studio and by the lab view.
    /// </summary>
    internal sealed class DevicesPanel : UserControl
    {
        private readonly Label _portA;
        private readonly Label _portB;
        private readonly Label _portC;
        private readonly Label _adc;
        private readonly SevenSegmentDisplay _display;
        private readonly LedBar _lamps;
        private readonly ToggleSwitch[] _tumblers = new ToggleSwitch[3];
        private readonly VoltageSlider[] _sliders = new VoltageSlider[4];
        private bool _syncing;

        public event Action<int, bool> TumblerChanged;
        public event Action<int, double> AnalogChanged;

        public DevicesPanel()
        {
            BackColor = AppTheme.Surface;
            AutoScroll = true;
            Padding = new Padding(12, 0, 12, 12);

            var stack = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 0,
                BackColor = AppTheme.Surface,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            int row = 0;
            AddSection(stack, ref row, "ПОРТЫ ВВ55");
            _portA = AddValueRow(stack, ref row, "Порт A · лампы 01h");
            _portB = AddValueRow(stack, ref row, "Порт B · переключатели FAh");
            _portC = AddValueRow(stack, ref row, "Порт C · управление 02h");

            AddSection(stack, ref row, "ИНДИКАТОРЫ");
            _display = new SevenSegmentDisplay
            {
                Dock = DockStyle.Top,
                Height = 82,
                Margin = new Padding(0, 0, 0, 6)
            };
            AddControlRow(stack, ref row, _display);

            AddSection(stack, ref row, "СВЕТОДИОДЫ");
            _lamps = new LedBar
            {
                Dock = DockStyle.Top,
                Height = 52,
                Margin = new Padding(0, 0, 0, 6)
            };
            AddControlRow(stack, ref row, _lamps);

            AddSection(stack, ref row, "ПЕРЕКЛЮЧАТЕЛИ");
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                _tumblers[i] = new ToggleSwitch
                {
                    Dock = DockStyle.Top,
                    Text = "SA" + (i + 1),
                    Height = 27,
                    Margin = new Padding(0, 0, 0, 3)
                };
                _tumblers[i].CheckedChanged += (sender, args) =>
                {
                    if (_syncing) return;
                    TumblerChanged?.Invoke(index, _tumblers[index].Checked);
                };
                AddControlRow(stack, ref row, _tumblers[i]);
            }

            AddSection(stack, ref row, "АНАЛОГОВЫЕ ДАТЧИКИ");
            for (int i = 0; i < 4; i++)
            {
                int index = i;
                _sliders[i] = new VoltageSlider
                {
                    Dock = DockStyle.Top,
                    Channel = i,
                    Height = 38,
                    Margin = new Padding(0, 0, 0, 2)
                };
                _sliders[i].ValueChanged += (channel, value) =>
                {
                    if (_syncing) return;
                    AnalogChanged?.Invoke(index, value);
                };
                AddControlRow(stack, ref row, _sliders[i]);
            }

            _adc = new Label
            {
                Text = "АЦП: 0 · канал 0",
                ForeColor = AppTheme.TextMuted,
                Font = AppTheme.MonoSmall,
                AutoSize = true,
                Margin = new Padding(0, 4, 0, 0)
            };
            AddControlRow(stack, ref row, _adc);

            Controls.Add(stack);
        }

        private static void AddSection(TableLayoutPanel stack, ref int row, string title)
        {
            var label = new Label
            {
                Text = title,
                ForeColor = AppTheme.TextDim,
                Font = AppTheme.SmallBoldFont,
                AutoSize = true,
                Margin = new Padding(0, 8, 0, 4)
            };
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            stack.Controls.Add(label, 0, row++);
        }

        private static Label AddValueRow(TableLayoutPanel stack, ref int row, string label)
        {
            var panel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 22,
                Margin = new Padding(0)
            };
            var caption = new Label
            {
                Text = label,
                ForeColor = AppTheme.TextMuted,
                Font = AppTheme.SmallFont,
                AutoSize = true,
                Location = new Point(0, 4)
            };
            var value = new Label
            {
                Text = "00h",
                ForeColor = AppTheme.Text,
                Font = AppTheme.MonoSmall,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleRight,
                Dock = DockStyle.Right,
                Width = 58
            };
            panel.Controls.Add(value);
            panel.Controls.Add(caption);
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            stack.Controls.Add(panel, 0, row++);
            return value;
        }

        private static void AddControlRow(TableLayoutPanel stack, ref int row, Control control)
        {
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            stack.Controls.Add(control, 0, row++);
        }

        public void UpdateSnapshot(MachineSnapshot snapshot)
        {
            if (snapshot == null) return;

            _syncing = true;
            try
            {
                _portA.Text = snapshot.PpiLamps.ToString("X2") + "h";
                _portB.Text = snapshot.PpiInputWord.ToString("X2") + "h";
                _portC.Text = snapshot.PpiControlWord.ToString("X2") + "h";
                _display.SetDigits(snapshot.Digits);
                _lamps.SetValue(snapshot.PpiLamps);

                _tumblers[0].Checked = snapshot.Tumbler1;
                _tumblers[1].Checked = snapshot.Tumbler2;
                _tumblers[2].Checked = snapshot.Tumbler3;

                if (snapshot.AnalogVoltages != null)
                {
                    for (int i = 0; i < _sliders.Length && i < snapshot.AnalogVoltages.Length; i++)
                    {
                        _sliders[i].SetValueSilent(snapshot.AnalogVoltages[i]);
                    }
                }

                _adc.Text = "АЦП: " + snapshot.AdcValue + " · канал " + snapshot.SelectedAnalogChannel;
            }
            finally
            {
                _syncing = false;
            }
        }
    }
}
