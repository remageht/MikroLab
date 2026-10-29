using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using MikroLab.Core.Machine;
using MikroLab.Studio.Dialogs;

namespace MikroLab.Studio.Controls
{
    /// <summary>
    /// Virtual hex viewer: only visible rows are painted, so the full 64K
    /// memory map costs nothing until it is scrolled into view. The row width
    /// adapts to the panel: 16 bytes when there is room, 8 in the narrow
    /// Studio inspector.
    /// </summary>
    internal sealed class HexViewer : Control
    {
        private const double HeaderHeight = 26;
        private const double RowHeight = 22;
        private const double ScrollWidth = 10;

        private byte[] _memory = new byte[65536];
        private ushort _pc;
        private int _topRow;
        private int _selectedAddress = -1;
        private bool _followPc = true;
        private int _columns = 16;
        private double _charWidth = 8;
        private int _nibbleHigh = -1;
        private bool _draggingScroll;
        private Rect _scrollTrack;
        private Rect _scrollThumb;

        public HexViewer()
        {
            Focusable = true;
            ClipToBounds = true;
            Cursor = new Cursor(StandardCursorType.Arrow);
            AddHandler(TextInputEvent, OnTextInputHandler, Avalonia.Interactivity.RoutingStrategies.Bubble);
        }

        public event Action<ushort, byte> ByteEditRequested;

        public bool FollowPc
        {
            get => _followPc;
            set
            {
                _followPc = value;
                if (value) ScrollToAddress(_pc);
            }
        }

        public ushort TopAddress => (ushort)(_topRow * _columns);
        public ushort SelectedAddress => _selectedAddress >= 0 ? (ushort)_selectedAddress : (ushort)0;
        public int TotalRows => 65536 / _columns;
        public int TopRow => _topRow;
        public int VisibleRows => Math.Max(1, (int)((Bounds.Height - HeaderHeight) / RowHeight));

        public void SetTopRow(int row)
        {
            _topRow = Math.Max(0, Math.Min(TotalRows - 1, row));
            _followPc = false;
            InvalidateVisual();
        }

        public void UpdateSnapshot(MachineSnapshot snapshot)
        {
            if (snapshot == null) return;
            _memory = snapshot.Memory ?? _memory;
            _pc = snapshot.PC;
            if (_followPc) ScrollToAddress(_pc);
            InvalidateVisual();
        }

        public void GoTo(ushort address)
        {
            _selectedAddress = address;
            _followPc = false;
            _topRow = AlignDown(address) / _columns;
            InvalidateVisual();
        }

        public void ScrollToAddress(ushort address)
        {
            _topRow = AlignDown(address) / _columns;
            InvalidateVisual();
        }

        private ushort AlignDown(ushort address)
        {
            return (ushort)(address - (address % _columns));
        }

        private void RecomputeColumns()
        {
            _charWidth = Math.Max(6, AppTheme.MakeText("00", AppTheme.MonoFace,
                AppTheme.MonoSmallSize, AppTheme.Brush(AppTheme.Text)).Width);

            int available = Math.Max(120, (int)(Bounds.Width - ScrollWidth - 16));
            int chosen = 4;
            foreach (int candidate in new[] { 16, 8, 4 })
            {
                int required = 10 + (int)(_charWidth * 5) + (int)(_charWidth * 3 * candidate) + 12 +
                               (int)(_charWidth * candidate);
                if (available >= required)
                {
                    chosen = candidate;
                    break;
                }
            }

            if (chosen == _columns) return;
            int oldAddress = _topRow * _columns;
            _columns = chosen;
            _topRow = (oldAddress / _columns);
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            _topRow -= (int)(e.Delta.Y * 3);
            _topRow = Math.Max(0, Math.Min(TotalRows - 1, _topRow));
            _followPc = false;
            InvalidateVisual();
            e.Handled = true;
            base.OnPointerWheelChanged(e);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            PointerPoint point = e.GetCurrentPoint(this);
            if (!point.Properties.IsLeftButtonPressed) return;

            Focus();
            Point position = e.GetPosition(this);

            if (_scrollThumb.Contains(position) || _scrollTrack.Contains(position))
            {
                _draggingScroll = true;
                e.Pointer.Capture(this);
                SetScrollFromY(position.Y);
                e.Handled = true;
                return;
            }

            if (!TryAddressAt(position, out ushort address)) return;

            _selectedAddress = address;
            InvalidateVisual();

            if (e.ClickCount >= 2)
            {
                _ = EditByteAsync(address);
            }

            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            if (!_draggingScroll) return;
            SetScrollFromY(e.GetPosition(this).Y);
            e.Handled = true;
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            if (!_draggingScroll) return;
            _draggingScroll = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }

        private void SetScrollFromY(double y)
        {
            double trackTop = _scrollTrack.Y;
            double travel = Math.Max(1, _scrollTrack.Height - _scrollThumb.Height);
            double ratio = (y - trackTop - _scrollThumb.Height / 2) / travel;
            ratio = Math.Max(0, Math.Min(1, ratio));
            _topRow = (int)Math.Round(ratio * Math.Max(0, TotalRows - VisibleRows));
            _followPc = false;
            InvalidateVisual();
        }

        private async System.Threading.Tasks.Task EditByteAsync(ushort address)
        {
            Window owner = TopLevel.GetTopLevel(this) as Window;
            byte? value = await ByteEditDialog.ShowAsync(owner, address, _memory[address]);
            if (value.HasValue) ByteEditRequested?.Invoke(address, value.Value);
        }

        private void OnTextInputHandler(object sender, TextInputEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Text) || _selectedAddress < 0) return;

            foreach (char raw in e.Text)
            {
                char c = char.ToUpperInvariant(raw);
                if (Uri.IsHexDigit(c))
                {
                    int digit = Convert.ToInt32(c.ToString(), 16);
                    if (_nibbleHigh < 0)
                    {
                        _nibbleHigh = digit;
                    }
                    else
                    {
                        int value = (_nibbleHigh << 4) | digit;
                        _nibbleHigh = -1;
                        ByteEditRequested?.Invoke((ushort)_selectedAddress, (byte)value);
                    }
                    e.Handled = true;
                }
                else
                {
                    _nibbleHigh = -1;
                }
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Enter && _selectedAddress >= 0)
            {
                _ = EditByteAsync((ushort)_selectedAddress);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Up) { SetTopRow(_topRow - 1); e.Handled = true; return; }
            if (e.Key == Key.Down) { SetTopRow(_topRow + 1); e.Handled = true; return; }
            if (e.Key == Key.PageUp) { SetTopRow(_topRow - VisibleRows); e.Handled = true; return; }
            if (e.Key == Key.PageDown) { SetTopRow(_topRow + VisibleRows); e.Handled = true; return; }
            base.OnKeyDown(e);
        }

        private bool TryAddressAt(Point point, out ushort address)
        {
            address = 0;
            if (point.Y < HeaderHeight) return false;

            int bytesX = 10 + (int)(_charWidth * 6);
            int asciiX = bytesX + (int)(_charWidth * 3 * _columns) + 12;
            int row = (int)((point.Y - HeaderHeight) / RowHeight);
            int top = (_topRow + row) * _columns;
            if (top >= 65536) return false;

            if (point.X < bytesX)
            {
                address = AlignDown((ushort)top);
                return true;
            }

            if (point.X < asciiX)
            {
                int column = (int)((point.X - bytesX) / (_charWidth * 3));
                if (column >= 0 && column < _columns)
                {
                    address = (ushort)(top + column);
                    return true;
                }
            }
            else
            {
                int column = (int)((point.X - asciiX) / _charWidth);
                if (column >= 0 && column < _columns &&
                    asciiX + column * _charWidth < Bounds.Width - ScrollWidth)
                {
                    address = (ushort)(top + column);
                    return true;
                }
            }

            return false;
        }

        public override void Render(DrawingContext context)
        {
            RecomputeColumns();

            double width = Bounds.Width;
            double height = Bounds.Height;
            context.DrawRectangle(AppTheme.Brush(AppTheme.Surface), null, new Rect(0, 0, width, height));

            int addressX = 10;
            int bytesX = addressX + (int)(_charWidth * 6);
            int asciiX = bytesX + (int)(_charWidth * 3 * _columns) + 12;

            context.DrawRectangle(AppTheme.Brush(AppTheme.SurfaceRaised), null,
                new Rect(0, 0, width, HeaderHeight));
            context.DrawLine(AppTheme.Pen(AppTheme.BorderSoft),
                new Point(0, HeaderHeight - 1), new Point(width, HeaderHeight - 1));

            AppTheme.DrawText(context, "АДРЕС", new Point(addressX, 5),
                AppTheme.MonoSmallSize, AppTheme.Brush(AppTheme.TextMuted));
            for (int i = 0; i < _columns; i++)
            {
                AppTheme.DrawText(context, i.ToString("X2"),
                    new Point(bytesX + i * _charWidth * 3, 5),
                    AppTheme.MonoSmallSize, AppTheme.Brush(AppTheme.TextDim));
            }
            AppTheme.DrawText(context, "ASCII", new Point(asciiX, 5),
                AppTheme.MonoSmallSize, AppTheme.Brush(AppTheme.TextDim));

            int rows = VisibleRows + 1;
            int pcRow = _pc / _columns;

            for (int r = 0; r < rows; r++)
            {
                int row = _topRow + r;
                if (row >= 65536 / _columns) break;

                double y = HeaderHeight + r * RowHeight;
                int baseAddress = row * _columns;

                if (row == pcRow)
                {
                    context.DrawRectangle(AppTheme.Brush(Color.FromRgb(31, 48, 83)), null,
                        new Rect(0, y, width - ScrollWidth, RowHeight));
                }
                else if ((row & 1) == 1)
                {
                    context.DrawRectangle(AppTheme.Brush(Color.FromRgb(27, 30, 38)), null,
                        new Rect(0, y, width - ScrollWidth, RowHeight));
                }

                AppTheme.DrawText(context, baseAddress.ToString("X4"),
                    new Point(addressX, y + 2), AppTheme.MonoSmallSize, AppTheme.Brush(AppTheme.Cyan));

                for (int i = 0; i < _columns; i++)
                {
                    int address = baseAddress + i;
                    byte value = _memory[address];
                    Color color = value == 0 ? AppTheme.TextDim : AppTheme.Text;
                    if (address == _pc) color = AppTheme.Amber;
                    if (address == _selectedAddress) color = AppTheme.Text;

                    AppTheme.DrawText(context, value.ToString("X2"),
                        new Point(bytesX + i * _charWidth * 3, y + 2),
                        AppTheme.MonoSmallSize, AppTheme.Brush(color));

                    if (address == _selectedAddress)
                    {
                        context.DrawRectangle(null, AppTheme.Pen(AppTheme.Accent),
                            new Rect(bytesX + i * _charWidth * 3 - 2, y + 1,
                                _charWidth * 2 + 3, RowHeight - 2));
                    }
                }

                char[] ascii = new char[_columns];
                for (int i = 0; i < _columns; i++)
                {
                    byte value = _memory[baseAddress + i];
                    ascii[i] = value >= 32 && value <= 126 ? (char)value : '.';
                }
                AppTheme.DrawText(context, new string(ascii),
                    new Point(asciiX, y + 2), AppTheme.MonoSmallSize,
                    AppTheme.Brush(AppTheme.TextMuted));
            }

            DrawScrollbar(context, width, height);
        }

        private void DrawScrollbar(DrawingContext context, double width, double height)
        {
            double trackTop = HeaderHeight + 2;
            double trackHeight = Math.Max(1, height - trackTop - 2);
            _scrollTrack = new Rect(width - ScrollWidth, trackTop, ScrollWidth, trackHeight);

            double thumbHeight = Math.Max(24, trackHeight * VisibleRows / TotalRows);
            double maxTop = Math.Max(1, TotalRows - VisibleRows);
            double thumbTop = trackTop + (trackHeight - thumbHeight) * (_topRow / maxTop);
            _scrollThumb = new Rect(width - ScrollWidth, thumbTop, ScrollWidth, thumbHeight);

            AppTheme.FillRounded(context, _scrollTrack, 3,
                AppTheme.Brush(Color.FromRgb(28, 31, 39)));
            AppTheme.FillRounded(context, _scrollThumb, 3,
                AppTheme.Brush(Color.FromRgb(60, 66, 80)));
        }
    }

    /// <summary>Memory inspector with address navigation and direct byte editing.</summary>
    internal sealed class MemoryPanel : UserControl
    {
        private readonly HexViewer _hex;
        private readonly TextBox _addressBox;
        private readonly ToggleSwitch _followPc;

        private MachineSnapshot _snapshot;

        public MemoryPanel()
        {
            var grid = new Grid
            {
                RowDefinitions = RowDefinitions.Parse("78,32,*"),
                Background = AppTheme.Brush(AppTheme.Surface)
            };

            var topBar = new Grid
            {
                ColumnDefinitions = ColumnDefinitions.Parse("Auto,Auto,Auto,Auto"),
                Margin = new Thickness(12, 8, 12, 6)
            };

            var addressLabel = AppTheme.Label("Адрес", AppTheme.TextMuted, AppTheme.SmallSize);
            addressLabel.VerticalAlignment = VerticalAlignment.Center;
            topBar.Children.Add(addressLabel);

            _addressBox = new TextBox
            {
                Text = "8000",
                Width = 76,
                Height = 28,
                Margin = new Thickness(8, 0, 0, 0),
                Background = AppTheme.Brush(AppTheme.EditorBackground),
                Foreground = AppTheme.Brush(AppTheme.Text),
                BorderBrush = AppTheme.Brush(AppTheme.Border),
                BorderThickness = new Thickness(1),
                FontFamily = AppTheme.MonoFamily,
                FontSize = AppTheme.MonoSmallSize,
                Padding = new Thickness(6, 3),
                VerticalAlignment = VerticalAlignment.Center
            };
            _addressBox.KeyDown += (sender, args) =>
            {
                if (args.Key == Key.Enter)
                {
                    GoToAddress();
                    args.Handled = true;
                }
            };
            topBar.Children.Add(_addressBox);
            Grid.SetColumn(_addressBox, 1);

            var go = new UiButton
            {
                Text = "Перейти",
                Kind = UiButtonKind.Subtle,
                Width = 76,
                Height = 28,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            go.Click += GoToAddress;
            topBar.Children.Add(go);
            Grid.SetColumn(go, 2);

            _followPc = new ToggleSwitch
            {
                Text = "Следить за PC",
                Checked = true,
                Width = 150,
                Height = 28,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            _followPc.CheckedChanged += value => _hex.FollowPc = value;
            topBar.Children.Add(_followPc);
            Grid.SetColumn(_followPc, 3);

            grid.Children.Add(topBar);

            var jumpBar = new Grid
            {
                ColumnDefinitions = ColumnDefinitions.Parse("Auto,Auto,Auto,Auto,Auto,Auto"),
                Margin = new Thickness(12, 2, 12, 4)
            };
            var jumpLabel = AppTheme.Label("Перейти к", AppTheme.TextMuted, AppTheme.SmallSize);
            jumpLabel.VerticalAlignment = VerticalAlignment.Center;
            jumpBar.Children.Add(jumpLabel);

            int column = 1;
            foreach (string target in new[] { "PC", "HL", "DE", "BC", "SP" })
            {
                string name = target;
                var button = new UiButton
                {
                    Text = name,
                    Kind = UiButtonKind.Ghost,
                    Width = 46,
                    Height = 24,
                    Margin = new Thickness(6, 0, 0, 0),
                    FontSize = AppTheme.SmallSize
                };
                button.Click += () => JumpToRegister(name);
                jumpBar.Children.Add(button);
                Grid.SetColumn(button, column++);
            }

            grid.Children.Add(jumpBar);
            Grid.SetRow(jumpBar, 1);

            _hex = new HexViewer();
            _hex.ByteEditRequested += (address, value) =>
            {
                if (_snapshot == null) return;
                ByteWriteRequested?.Invoke(address, value);
            };
            grid.Children.Add(_hex);
            Grid.SetRow(_hex, 2);

            Content = grid;
        }

        public event Action<ushort, byte> ByteWriteRequested;

        public void UpdateSnapshot(MachineSnapshot snapshot)
        {
            _snapshot = snapshot;
            _hex.UpdateSnapshot(snapshot);
        }

        private void JumpToRegister(string name)
        {
            if (_snapshot == null) return;
            ushort address = 0;
            switch (name)
            {
                case "PC": address = _snapshot.PC; break;
                case "HL": address = (ushort)((_snapshot.H << 8) | _snapshot.L); break;
                case "DE": address = (ushort)((_snapshot.D << 8) | _snapshot.E); break;
                case "BC": address = (ushort)((_snapshot.B << 8) | _snapshot.C); break;
                case "SP": address = _snapshot.SP; break;
            }
            SetAddress(address);
        }

        public void SetAddress(ushort address)
        {
            _addressBox.Text = address.ToString("X4");
            _hex.GoTo(address);
        }

        public void SetFollowPc(bool follow)
        {
            _followPc.Checked = follow;
        }

        private void GoToAddress()
        {
            string text = (_addressBox.Text ?? string.Empty).Trim().TrimEnd('h', 'H');
            if (ushort.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort address))
            {
                _hex.GoTo(address);
            }
        }
    }
}
