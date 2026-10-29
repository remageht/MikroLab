using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using MikroLab.Core.Machine;

namespace MikroLab.Studio.Controls
{
    /// <summary>Eight seven-segment digits driven by the board's display port.</summary>
    internal sealed class SevenSegmentDisplay : Control
    {
        private byte[] _digits = new byte[8];

        public SevenSegmentDisplay()
        {
            Height = 82;
            ClipToBounds = true;
        }

        public void SetDigits(byte[] digits)
        {
            _digits = new byte[8];
            if (digits != null)
            {
                Array.Copy(digits, _digits, Math.Min(8, digits.Length));
            }
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            context.DrawRectangle(AppTheme.Brush(AppTheme.EditorBackground), null,
                new Rect(0, 0, Bounds.Width, Bounds.Height));

            const double gap = 8;
            double digitWidth = Math.Max(18, (Bounds.Width - gap * 9) / 8);
            double digitHeight = Math.Max(45, Bounds.Height - 12);
            double top = 6;
            double x = gap / 2;

            for (int i = 0; i < 8; i++)
            {
                DrawDigit(context, new Rect(x, top, digitWidth, digitHeight), _digits[i]);
                x += digitWidth + gap;
                if (i == 3) x += 10;
            }
        }

        private static void DrawDigit(DrawingContext context, Rect bounds, byte segments)
        {
            double thickness = Math.Max(4, bounds.Width / 8);
            double half = bounds.Height / 2;

            var a = new Rect(bounds.X + thickness, bounds.Y, bounds.Width - thickness * 2, thickness);
            var g = new Rect(bounds.X + thickness, bounds.Y + half - thickness / 2,
                bounds.Width - thickness * 2, thickness);
            var d = new Rect(bounds.X + thickness, bounds.Bottom - thickness,
                bounds.Width - thickness * 2, thickness);
            var f = new Rect(bounds.X, bounds.Y + thickness, thickness, half - thickness);
            var b = new Rect(bounds.Right - thickness, bounds.Y + thickness, thickness, half - thickness);
            var e = new Rect(bounds.X, bounds.Y + half + 2, thickness, half - thickness - 2);
            var c = new Rect(bounds.Right - thickness, bounds.Y + half + 2, thickness, half - thickness - 2);

            DrawSegment(context, a, (segments & 0x01) != 0, thickness);
            DrawSegment(context, b, (segments & 0x02) != 0, thickness);
            DrawSegment(context, c, (segments & 0x04) != 0, thickness);
            DrawSegment(context, d, (segments & 0x08) != 0, thickness);
            DrawSegment(context, e, (segments & 0x10) != 0, thickness);
            DrawSegment(context, f, (segments & 0x20) != 0, thickness);
            DrawSegment(context, g, (segments & 0x40) != 0, thickness);

            if ((segments & 0x80) != 0)
            {
                double dot = Math.Max(3, thickness);
                context.DrawEllipse(AppTheme.Brush(AppTheme.Amber), null,
                    new Point(bounds.Right - dot, bounds.Bottom - dot), dot, dot);
            }
        }

        private static void DrawSegment(DrawingContext context, Rect bounds, bool on, double radius)
        {
            Color color = on ? AppTheme.Amber : Color.FromRgb(34, 38, 47);
            AppTheme.FillRounded(context, bounds, Math.Max(1, radius / 2), AppTheme.Brush(color));
        }
    }

    /// <summary>Eight lamps connected to port B.</summary>
    internal sealed class LedBar : Control
    {
        private byte _value;

        public LedBar()
        {
            Height = 52;
            ClipToBounds = true;
        }

        public void SetValue(byte value)
        {
            _value = value;
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            context.DrawRectangle(AppTheme.Brush(AppTheme.Surface), null,
                new Rect(0, 0, Bounds.Width, Bounds.Height));

            const int count = 8;
            double cell = Math.Max(20, (Bounds.Width - 8) / count);
            double diameter = Math.Min(20, Math.Max(12, cell - 12));
            double start = 4 + (cell - diameter) / 2;

            for (int i = 0; i < count; i++)
            {
                bool on = (_value & (1 << i)) != 0;
                double x = start + i * cell;
                const double y = 8;

                if (on)
                {
                    context.DrawEllipse(AppTheme.Brush(Color.FromArgb(45, AppTheme.Green.R,
                        AppTheme.Green.G, AppTheme.Green.B)), null,
                        new Point(x + diameter / 2, y + diameter / 2), diameter / 2 + 4, diameter / 2 + 4);
                }

                context.DrawEllipse(
                    AppTheme.Brush(on ? AppTheme.Green : Color.FromRgb(35, 40, 48)),
                    AppTheme.Pen(on ? AppTheme.Mix(AppTheme.Green, Colors.White, 0.35) : AppTheme.Border),
                    new Point(x + diameter / 2, y + diameter / 2), diameter / 2, diameter / 2);

                FormattedText label = AppTheme.MakeText("D" + i, AppTheme.SmallSize,
                    AppTheme.Brush(on ? AppTheme.Green : AppTheme.TextDim));
                context.DrawText(label, new Point(
                    x + (diameter - label.Width) / 2,
                    y + diameter + 3));
            }
        }
    }

    /// <summary>Custom switch used for tumblers and "follow" options.</summary>
    internal sealed class ToggleSwitch : Control
    {
        private bool _checked;
        private bool _hover;

        public ToggleSwitch()
        {
            Height = 30;
            Focusable = true;
            Cursor = new Cursor(StandardCursorType.Hand);
        }

        public event Action<bool> CheckedChanged;

        public string Text { get; set; } = string.Empty;

        public bool Checked
        {
            get => _checked;
            set
            {
                if (_checked == value) return;
                _checked = value;
                InvalidateVisual();
                CheckedChanged?.Invoke(value);
            }
        }

        protected override void OnPointerEntered(PointerEventArgs e)
        {
            _hover = true;
            InvalidateVisual();
            base.OnPointerEntered(e);
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            _hover = false;
            InvalidateVisual();
            base.OnPointerExited(e);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            Checked = !Checked;
            e.Handled = true;
        }

        public override void Render(DrawingContext context)
        {
            context.DrawRectangle(AppTheme.Brush(AppTheme.Surface), null,
                new Rect(0, 0, Bounds.Width, Bounds.Height));

            FormattedText text = AppTheme.MakeText(Text, AppTheme.SmallSize,
                AppTheme.Brush(IsEnabled ? AppTheme.Text : AppTheme.TextDim));
            using (context.PushClip(new Rect(0, 0, Math.Max(10, Bounds.Width - 54), Bounds.Height)))
            {
                context.DrawText(text, new Point(0, (Bounds.Height - text.Height) / 2));
            }

            const double trackWidth = 38;
            const double trackHeight = 20;
            double x = Bounds.Width - trackWidth - 2;
            double y = (Bounds.Height - trackHeight) / 2;
            var track = new Rect(x, y, trackWidth, trackHeight);

            Color trackColor = _checked
                ? AppTheme.Accent
                : (_hover ? AppTheme.SurfaceHover : AppTheme.SurfaceRaised);
            AppTheme.FillRounded(context, track, trackHeight / 2, AppTheme.Brush(trackColor));
            AppTheme.DrawRoundedBorder(context, track, trackHeight / 2,
                AppTheme.Pen(_checked ? AppTheme.Accent : AppTheme.Border));

            double knob = trackHeight - 6;
            double knobX = _checked ? x + trackWidth - knob - 3 : x + 3;
            context.DrawEllipse(AppTheme.Brush(_checked ? AppTheme.Text : AppTheme.TextMuted), null,
                new Point(knobX + knob / 2, y + 3 + knob / 2), knob / 2, knob / 2);
        }
    }

    /// <summary>Analog input slider for one ADC channel.</summary>
    internal sealed class VoltageSlider : Control
    {
        private double _value;
        private bool _dragging;
        private bool _hover;

        public VoltageSlider()
        {
            Height = 38;
            Cursor = new Cursor(StandardCursorType.Hand);
        }

        public int Channel { get; set; }

        public event Action<int, double> ValueChanged;

        public double Value
        {
            get => _value;
            set
            {
                _value = Math.Max(0, Math.Min(10, value));
                InvalidateVisual();
            }
        }

        public void SetValueSilent(double value)
        {
            _value = Math.Max(0, Math.Min(10, value));
            InvalidateVisual();
        }

        protected override void OnPointerEntered(PointerEventArgs e)
        {
            _hover = true;
            InvalidateVisual();
            base.OnPointerEntered(e);
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            _hover = false;
            InvalidateVisual();
            base.OnPointerExited(e);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            _dragging = true;
            e.Pointer.Capture(this);
            UpdateFromX(e.GetPosition(this).X);
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            if (_dragging) UpdateFromX(e.GetPosition(this).X);
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            if (!_dragging) return;
            _dragging = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }

        private void UpdateFromX(double x)
        {
            double left = 88;
            double right = Math.Max(left + 20, Bounds.Width - 62);
            double ratio = (x - left) / (right - left);
            ratio = Math.Max(0, Math.Min(1, ratio));
            _value = Math.Round(ratio * 10.0, 1);
            InvalidateVisual();
            ValueChanged?.Invoke(Channel, _value);
        }

        public override void Render(DrawingContext context)
        {
            context.DrawRectangle(AppTheme.Brush(AppTheme.Surface), null,
                new Rect(0, 0, Bounds.Width, Bounds.Height));

            double left = 88;
            double right = Math.Max(left + 20, Bounds.Width - 62);
            double y = Bounds.Height / 2 - 2;

            AppTheme.DrawText(context, "Д" + (Channel + 1), new Point(0, Bounds.Height / 2 - 8),
                AppTheme.SmallSize, AppTheme.Brush(AppTheme.TextMuted), true);

            var track = new Rect(left, y - 2, right - left, 5);
            AppTheme.FillRounded(context, track, 3, AppTheme.Brush(AppTheme.SurfaceRaised));
            double filled = (right - left) * (_value / 10.0);
            if (filled > 0)
            {
                AppTheme.FillRounded(context, new Rect(left, y - 2, filled, 5), 3,
                    AppTheme.Brush(AppTheme.Accent));
            }

            double knobX = left + filled;
            double knobSize = _hover || _dragging ? 16 : 14;
            context.DrawEllipse(AppTheme.Brush(AppTheme.Text), AppTheme.Pen(AppTheme.Accent, 2),
                new Point(knobX, y), knobSize / 2, knobSize / 2);

            FormattedText value = AppTheme.MakeText(_value.ToString("0.0") + " В",
                AppTheme.MonoSmallSize, AppTheme.Brush(AppTheme.Text));
            context.DrawText(value, new Point(Bounds.Width - 58, (Bounds.Height - value.Height) / 2));
        }
    }

    /// <summary>
    /// Shared device dashboard: lamps, seven-segment indicators, tumblers and
    /// analog sensors. The same control is used by Studio and by the lab view.
    /// </summary>
    internal sealed class DevicesPanel : UserControl
    {
        private readonly TextBlock _portA;
        private readonly TextBlock _portB;
        private readonly TextBlock _portC;
        private readonly TextBlock _control;
        private readonly TextBlock _ppiMode;
        private readonly TextBlock _adc;
        private readonly SevenSegmentDisplay _display;
        private readonly LedBar _lamps;
        private readonly ToggleSwitch[] _tumblers = new ToggleSwitch[3];
        private readonly VoltageSlider[] _sliders = new VoltageSlider[4];
        private readonly MatrixKeyboardControl _keyboard;
        private bool _syncing;

        public DevicesPanel()
        {
            var stack = new StackPanel
            {
                Margin = new Thickness(12, 0, 12, 12),
                Spacing = 2
            };

            AddSection(stack, "ПОРТЫ ВВ55");
            _portA = AddValueRow(stack, "Порт A · вход FAh");
            _portB = AddValueRow(stack, "Порт B · лампы F9h");
            _portC = AddValueRow(stack, "Порт C · стробы F8h");
            _control = AddValueRow(stack, "Контроль · FBh");
            _ppiMode = AppTheme.Label("Режим A0/B0 · A вход · B выход · C выход/выход",
                AppTheme.TextMuted, AppTheme.MonoSmallSize);
            _ppiMode.TextWrapping = TextWrapping.Wrap;
            _ppiMode.Height = 38;
            _ppiMode.Margin = new Thickness(0, 2, 0, 0);
            stack.Children.Add(_ppiMode);

            AddSection(stack, "ИНДИКАТОРЫ");
            _display = new SevenSegmentDisplay { Height = 82, Margin = new Thickness(0, 0, 0, 6) };
            stack.Children.Add(_display);

            AddSection(stack, "СВЕТОДИОДЫ");
            _lamps = new LedBar { Height = 52, Margin = new Thickness(0, 0, 0, 6) };
            stack.Children.Add(_lamps);

            AddSection(stack, "ПЕРЕКЛЮЧАТЕЛИ");
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                _tumblers[i] = new ToggleSwitch
                {
                    Text = "SA" + (i + 1),
                    Height = 27,
                    Margin = new Thickness(0, 0, 0, 3)
                };
                _tumblers[i].CheckedChanged += value =>
                {
                    if (_syncing) return;
                    TumblerChanged?.Invoke(index, value);
                };
                stack.Children.Add(_tumblers[i]);
            }

            AddSection(stack, "АНАЛОГОВЫЕ ДАТЧИКИ");
            for (int i = 0; i < 4; i++)
            {
                int index = i;
                _sliders[i] = new VoltageSlider
                {
                    Channel = i,
                    Height = 38,
                    Margin = new Thickness(0, 0, 0, 2)
                };
                _sliders[i].ValueChanged += (channel, value) =>
                {
                    if (_syncing) return;
                    AnalogChanged?.Invoke(index, value);
                };
                stack.Children.Add(_sliders[i]);
            }

            _adc = AppTheme.Label("АЦП: 0 · канал 0", AppTheme.TextMuted, AppTheme.MonoSmallSize);
            _adc.Margin = new Thickness(0, 4, 0, 0);
            stack.Children.Add(_adc);

            AddSection(stack, "МАТРИЧНАЯ КЛАВИАТУРА · ПОРТ C");
            _keyboard = new MatrixKeyboardControl { Height = 190, Margin = new Thickness(0, 0, 0, 6) };
            _keyboard.KeyChanged += (rowIndex, column, pressed) =>
                KeyChanged?.Invoke(rowIndex, column, pressed);
            stack.Children.Add(_keyboard);

            Content = new ScrollViewer
            {
                Content = stack,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                Background = AppTheme.Brush(AppTheme.Surface)
            };
        }

        public event Action<int, bool> TumblerChanged;
        public event Action<int, double> AnalogChanged;
        public event Action<int, int, bool> KeyChanged;

        public void AttachKeyboard(MikroLab.Core.Devices.MatrixKeyboard keyboard)
        {
            _keyboard?.Attach(keyboard);
        }

        private static void AddSection(StackPanel stack, string title)
        {
            TextBlock label = AppTheme.Label(title, AppTheme.TextDim, AppTheme.SmallSize, true);
            label.Margin = new Thickness(0, 8, 0, 4);
            stack.Children.Add(label);
        }

        private static TextBlock AddValueRow(StackPanel stack, string caption)
        {
            var grid = new Grid
            {
                ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"),
                Height = 22
            };
            var label = AppTheme.Label(caption, AppTheme.TextMuted, AppTheme.SmallSize);
            grid.Children.Add(label);

            var value = AppTheme.Label("00h", AppTheme.Text, AppTheme.MonoSmallSize);
            value.HorizontalAlignment = HorizontalAlignment.Right;
            grid.Children.Add(value);
            Grid.SetColumn(value, 1);

            stack.Children.Add(grid);
            return value;
        }

        private static string DescribePpi(MachineSnapshot snapshot)
        {
            string directionA = snapshot.PpiPortABidirectional
                ? "двунаправленный"
                : (snapshot.PpiPortAInput ? "вход" : "выход");
            string directionB = snapshot.PpiPortBInput ? "вход" : "выход";
            string upperC = snapshot.PpiPortCUpperInput ? "вход" : "выход";
            string lowerC = snapshot.PpiPortCLowerInput ? "вход" : "выход";
            return "Режим A" + (int)snapshot.PpiModeA + "/B" + (int)snapshot.PpiModeB +
                   " · A " + directionA + " · B " + directionB +
                   " · C " + upperC + "/" + lowerC;
        }

        public void UpdateSnapshot(MachineSnapshot snapshot)
        {
            if (snapshot == null) return;

            _syncing = true;
            try
            {
                _portA.Text = snapshot.PpiEffectiveA.ToString("X2") + "h";
                _portB.Text = snapshot.PpiEffectiveB.ToString("X2") + "h";
                _portC.Text = snapshot.PpiEffectiveC.ToString("X2") + "h";
                _control.Text = snapshot.PpiControlWord.ToString("X2") + "h";
                _ppiMode.Text = DescribePpi(snapshot);
                _display.SetDigits(snapshot.Digits);
                _lamps.SetValue(snapshot.PpiEffectiveB);

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
                _keyboard?.SyncFromModel();
            }
            finally
            {
                _syncing = false;
            }
        }
    }
}
