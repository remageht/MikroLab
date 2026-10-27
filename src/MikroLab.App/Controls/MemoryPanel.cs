using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using MikroLab.Core.Machine;

namespace MikroLab.App.Controls
{
    /// <summary>
    /// Virtual hex viewer: only visible rows are painted, so the full 64K
    /// memory map costs nothing until it is scrolled into view. The row width
    /// adapts to the panel: 16 bytes when there is room, 8 in the narrow
    /// Studio inspector.
    /// </summary>
    internal sealed class HexViewer : Control
    {
        private const int HeaderHeight = 26;
        private const int RowHeight = 22;

        private readonly VScrollBar _scroll;
        private byte[] _memory = new byte[65536];
        private ushort _pc;
        private ushort _topAddress;
        private int _selectedAddress = -1;
        private bool _followPc = true;
        private int _columns = 16;
        private int _charWidth = 8;
        private bool _updatingScroll;
        private int _nibbleHigh = -1;

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

        public ushort TopAddress => _topAddress;
        public ushort SelectedAddress => _selectedAddress >= 0 ? (ushort)_selectedAddress : (ushort)0;

        public HexViewer()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.Selectable,
                true);
            BackColor = AppTheme.Surface;
            Font = AppTheme.MonoSmall;
            TabStop = true;

            _scroll = new VScrollBar
            {
                Dock = DockStyle.Right,
                Width = 14,
                SmallChange = 1,
                LargeChange = 10
            };
            _scroll.ValueChanged += (sender, args) =>
            {
                if (_updatingScroll) return;
                _topAddress = (ushort)(_scroll.Value * _columns);
                _followPc = false;
                Invalidate();
            };
            Controls.Add(_scroll);
            RecomputeColumns();
            UpdateScrollRange();
        }

        public void UpdateSnapshot(MachineSnapshot snapshot)
        {
            if (snapshot == null) return;
            _memory = snapshot.Memory ?? _memory;
            _pc = snapshot.PC;
            if (_followPc)
            {
                ScrollToAddress(_pc);
            }
            Invalidate();
        }

        public void GoTo(ushort address)
        {
            _selectedAddress = address;
            _followPc = false;
            _topAddress = AlignDown(address);
            SyncScroll();
            Invalidate();
        }

        public void ScrollToAddress(ushort address)
        {
            _topAddress = AlignDown(address);
            SyncScroll();
            Invalidate();
        }

        private ushort AlignDown(ushort address)
        {
            return (ushort)(address - (address % _columns));
        }

        private int VisibleRows => Math.Max(1, (Height - HeaderHeight) / RowHeight);

        private void RecomputeColumns()
        {
            if (Font == null) return;

            _charWidth = Math.Max(6, TextRenderer.MeasureText("00", Font).Width);
            int available = Math.Max(120, Width - _scroll.Width - 16);

            int chosen = 4;
            foreach (int candidate in new[] { 16, 8, 4 })
            {
                int required = 10 + _charWidth * 5 + _charWidth * 3 * candidate + 12 + _charWidth * candidate;
                if (available >= required)
                {
                    chosen = candidate;
                    break;
                }
            }

            if (chosen == _columns) return;
            _columns = chosen;
            _topAddress = AlignDown(_topAddress);
            UpdateScrollRange();
        }

        private void UpdateScrollRange()
        {
            int totalRows = 65536 / _columns;
            _updatingScroll = true;
            try
            {
                _scroll.Minimum = 0;
                _scroll.Maximum = Math.Max(0, totalRows - 1);
                _scroll.LargeChange = Math.Max(1, Math.Min(VisibleRows, totalRows));
                _scroll.SmallChange = 1;

                int row = _topAddress / _columns;
                if (row < _scroll.Minimum) row = _scroll.Minimum;
                if (row > _scroll.Maximum) row = _scroll.Maximum;
                if (_scroll.Value != row) _scroll.Value = row;
            }
            finally
            {
                _updatingScroll = false;
            }
        }

        private void SyncScroll()
        {
            int row = _topAddress / _columns;
            if (row < _scroll.Minimum) row = _scroll.Minimum;
            if (row > _scroll.Maximum) row = _scroll.Maximum;
            if (_scroll.Value == row) return;

            _updatingScroll = true;
            try
            {
                _scroll.Value = row;
            }
            finally
            {
                _updatingScroll = false;
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            RecomputeColumns();
            UpdateScrollRange();
            SyncScroll();
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            int delta = e.Delta > 0 ? -3 : 3;
            int value = Math.Max(_scroll.Minimum, Math.Min(_scroll.Maximum, _scroll.Value + delta));
            _scroll.Value = value;
            _followPc = false;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (!TryAddressAt(e.X, e.Y, out ushort address)) return;

            _selectedAddress = address;
            Invalidate();

            if (e.Clicks >= 2)
            {
                ByteEditRequested?.Invoke(address, _memory[address]);
            }
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e);
            if (_selectedAddress < 0) return;

            char c = char.ToUpperInvariant(e.KeyChar);
            if (Uri.IsHexDigit(c))
            {
                // Type two hex digits directly over a selected byte.
                if (_nibbleHigh < 0)
                {
                    _nibbleHigh = Convert.ToInt32(c.ToString(), 16);
                }
                else
                {
                    int value = (_nibbleHigh << 4) | Convert.ToInt32(c.ToString(), 16);
                    _nibbleHigh = -1;
                    ByteEditRequested?.Invoke((ushort)_selectedAddress, (byte)value);
                }
                e.Handled = true;
            }
            else if (e.KeyChar == (char)Keys.Enter)
            {
                ByteEditRequested?.Invoke((ushort)_selectedAddress, _memory[_selectedAddress]);
                e.Handled = true;
            }
            else
            {
                _nibbleHigh = -1;
            }
        }

        private bool TryAddressAt(int x, int y, out ushort address)
        {
            address = 0;
            if (y < HeaderHeight) return false;

            int bytesX = 10 + _charWidth * 6;
            int asciiX = bytesX + _charWidth * 3 * _columns + 12;
            int row = (y - HeaderHeight) / RowHeight;
            int top = _topAddress + row * _columns;
            if (top >= 65536) return false;

            if (x < bytesX)
            {
                address = AlignDown((ushort)top);
                return true;
            }

            if (x < asciiX)
            {
                int column = (x - bytesX) / (_charWidth * 3);
                if (column >= 0 && column < _columns)
                {
                    address = (ushort)(top + column);
                    return true;
                }
            }
            else
            {
                int column = (x - asciiX) / _charWidth;
                if (column >= 0 && column < _columns && asciiX + column * _charWidth < Width - _scroll.Width)
                {
                    address = (ushort)(top + column);
                    return true;
                }
            }

            return false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics graphics = e.Graphics;
            graphics.Clear(AppTheme.Surface);

            int addressX = 10;
            int bytesX = addressX + _charWidth * 6;
            int asciiX = bytesX + _charWidth * 3 * _columns + 12;
            int lineHeight = RowHeight;

            using (var headerBrush = new SolidBrush(AppTheme.SurfaceRaised))
            {
                graphics.FillRectangle(headerBrush, 0, 0, Width, HeaderHeight);
            }
            using (var borderPen = new Pen(AppTheme.BorderSoft))
            {
                graphics.DrawLine(borderPen, 0, HeaderHeight - 1, Width, HeaderHeight - 1);
            }

            TextRenderer.DrawText(graphics, "АДРЕС", Font, new Point(addressX, 5), AppTheme.TextMuted);
            for (int i = 0; i < _columns; i++)
            {
                TextRenderer.DrawText(
                    graphics,
                    i.ToString("X2"),
                    Font,
                    new Point(bytesX + i * _charWidth * 3, 5),
                    AppTheme.TextDim);
            }
            TextRenderer.DrawText(graphics, "ASCII", Font, new Point(asciiX, 5), AppTheme.TextDim);

            int rows = VisibleRows + 1;
            int pcRow = _pc / _columns;
            int topRow = _topAddress / _columns;

            for (int r = 0; r < rows; r++)
            {
                int row = topRow + r;
                if (row >= 65536 / _columns) break;

                int y = HeaderHeight + r * lineHeight;
                int baseAddress = row * _columns;

                if (row == pcRow)
                {
                    using (var pcBrush = new SolidBrush(Color.FromArgb(31, 48, 83)))
                    {
                        graphics.FillRectangle(pcBrush, 0, y, Width - _scroll.Width, lineHeight);
                    }
                }
                else if ((row & 1) == 1)
                {
                    using (var stripe = new SolidBrush(Color.FromArgb(27, 30, 38)))
                    {
                        graphics.FillRectangle(stripe, 0, y, Width - _scroll.Width, lineHeight);
                    }
                }

                TextRenderer.DrawText(
                    graphics,
                    baseAddress.ToString("X4"),
                    Font,
                    new Point(addressX, y + 2),
                    AppTheme.Cyan);

                for (int i = 0; i < _columns; i++)
                {
                    int address = baseAddress + i;
                    byte value = _memory[address];
                    Color color = value == 0 ? AppTheme.TextDim : AppTheme.Text;
                    if (address == _pc) color = AppTheme.Amber;
                    if (address == _selectedAddress) color = AppTheme.Text;

                    TextRenderer.DrawText(
                        graphics,
                        value.ToString("X2"),
                        Font,
                        new Point(bytesX + i * _charWidth * 3, y + 2),
                        color);

                    if (address == _selectedAddress)
                    {
                        using (var pen = new Pen(AppTheme.Accent))
                        {
                            graphics.DrawRectangle(pen,
                                bytesX + i * _charWidth * 3 - 2,
                                y + 1,
                                _charWidth * 2 + 3,
                                lineHeight - 2);
                        }
                    }
                }

                char[] ascii = new char[_columns];
                for (int i = 0; i < _columns; i++)
                {
                    byte value = _memory[baseAddress + i];
                    ascii[i] = value >= 32 && value <= 126 ? (char)value : '.';
                }
                TextRenderer.DrawText(
                    graphics,
                    new string(ascii),
                    Font,
                    new Point(asciiX, y + 2),
                    AppTheme.TextMuted);
            }
        }
    }

    /// <summary>Memory inspector with address navigation and direct byte editing.</summary>
    internal sealed class MemoryPanel : UserControl
    {
        private readonly HexViewer _hex;
        private readonly TextBox _addressBox;
        private readonly ToggleSwitch _followPc;

        public event Action<ushort, byte> ByteWriteRequested;

        public MemoryPanel()
        {
            BackColor = AppTheme.Surface;

            var topBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 42,
                BackColor = AppTheme.Surface,
                Padding = new Padding(12, 8, 12, 6)
            };

            Label addressLabel = AppTheme.Label("Адрес", AppTheme.TextMuted, AppTheme.SmallFont);
            addressLabel.Location = new Point(12, 14);

            _addressBox = new TextBox
            {
                Text = "8000",
                Width = 76,
                BackColor = AppTheme.EditorBackground,
                ForeColor = AppTheme.Text,
                BorderStyle = BorderStyle.FixedSingle,
                Font = AppTheme.MonoSmall,
                Location = new Point(62, 10)
            };
            _addressBox.KeyDown += (sender, args) =>
            {
                if (args.KeyCode == Keys.Enter)
                {
                    args.SuppressKeyPress = true;
                    GoToAddress();
                }
            };

            var go = new UiButton
            {
                Text = "Перейти",
                Kind = UiButtonKind.Subtle,
                Width = 76,
                Height = 28,
                Location = new Point(144, 7)
            };
            go.Click += (sender, args) => GoToAddress();

            _followPc = new ToggleSwitch
            {
                Text = "Следить за PC",
                Checked = true,
                Width = 150,
                Height = 28,
                ForeColor = AppTheme.TextMuted,
                Font = AppTheme.SmallFont,
                Location = new Point(226, 7)
            };
            _followPc.CheckedChanged += (sender, args) => _hex.FollowPc = _followPc.Checked;

            topBar.Controls.Add(addressLabel);
            topBar.Controls.Add(_addressBox);
            topBar.Controls.Add(go);
            topBar.Controls.Add(_followPc);

            _hex = new HexViewer { Dock = DockStyle.Fill };
            _hex.ByteEditRequested += (address, value) =>
            {
                if (TryPromptByte(address, value, out byte result))
                {
                    ByteWriteRequested?.Invoke(address, result);
                }
            };

            Controls.Add(_hex);
            Controls.Add(topBar);
        }

        public void UpdateSnapshot(MachineSnapshot snapshot)
        {
            _hex.UpdateSnapshot(snapshot);
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
            string text = (_addressBox.Text ?? string.Empty).Trim();
            text = text.TrimEnd('h', 'H');
            if (ushort.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort address))
            {
                _hex.GoTo(address);
            }
        }

        private bool TryPromptByte(ushort address, byte current, out byte result)
        {
            result = current;
            using (var dialog = new ByteEditDialog(address, current))
            {
                if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return false;
                result = dialog.Value;
                return true;
            }
        }
    }

    internal sealed class ByteEditDialog : Form
    {
        private readonly TextBox _box;

        public byte Value { get; private set; }

        public ByteEditDialog(ushort address, byte current)
        {
            Text = "Изменить байт " + address.ToString("X4") + "h";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = AppTheme.Surface;
            ForeColor = AppTheme.Text;
            Font = AppTheme.UiFont;
            ClientSize = new Size(300, 130);

            Label label = AppTheme.Label("Значение (hex, 00–FF):", AppTheme.TextMuted, AppTheme.SmallFont);
            label.Location = new Point(18, 18);

            _box = new TextBox
            {
                Text = current.ToString("X2"),
                Location = new Point(18, 42),
                Width = 120,
                BackColor = AppTheme.EditorBackground,
                ForeColor = AppTheme.Text,
                BorderStyle = BorderStyle.FixedSingle,
                Font = AppTheme.MonoFont
            };
            _box.SelectAll();

            var ok = new UiButton
            {
                Text = "OK",
                Kind = UiButtonKind.Primary,
                Width = 90,
                Height = 30,
                Location = new Point(104, 88),
                DialogResult = DialogResult.None
            };
            var cancel = new UiButton
            {
                Text = "Отмена",
                Kind = UiButtonKind.Secondary,
                Width = 90,
                Height = 30,
                Location = new Point(200, 88),
                DialogResult = DialogResult.Cancel
            };

            ok.Click += (sender, args) =>
            {
                string text = (_box.Text ?? string.Empty).Trim().TrimEnd('h', 'H');
                if (byte.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte value))
                {
                    Value = value;
                    DialogResult = DialogResult.OK;
                    Close();
                }
                else
                {
                    _box.BackColor = Color.FromArgb(76, 36, 42);
                }
            };

            AcceptButton = ok;
            CancelButton = cancel;
            Controls.Add(label);
            Controls.Add(_box);
            Controls.Add(ok);
            Controls.Add(cancel);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            AppTheme.EnableDarkTitleBar(this);
        }
    }
}
