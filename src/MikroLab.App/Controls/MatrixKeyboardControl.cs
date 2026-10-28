using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using MikroLab.Core.Devices;

namespace MikroLab.App.Controls
{
    /// <summary>
    /// Clickable 4×4 (or 3×4) matrix keyboard. Rows are PC0..PC3, columns are
    /// PC4..PC7; the control only edits the portable <see cref="MatrixKeyboard"/>
    /// model and raises an event for the host to refresh the PPI latch.
    /// </summary>
    internal sealed class MatrixKeyboardControl : Control
    {
        private MatrixKeyboard _keyboard;
        private int _pressedRow = -1;
        private int _pressedColumn = -1;
        private int _hoverRow = -1;
        private int _hoverColumn = -1;

        public event Action<int, int, bool> KeyChanged;

        public MatrixKeyboardControl()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.StandardClick |
                ControlStyles.Selectable,
                true);
            BackColor = AppTheme.Surface;
            Cursor = Cursors.Hand;
            TabStop = false;
        }

        public MatrixKeyboard Keyboard => _keyboard;

        public void Attach(MatrixKeyboard keyboard)
        {
            _keyboard = keyboard;
            Invalidate();
        }

        public void SyncFromModel()
        {
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            if (_keyboard == null)
            {
                TextRenderer.DrawText(g, "Клавиатура не подключена", AppTheme.UiFont,
                    new Point(8, 8), AppTheme.TextDim);
                return;
            }

            int rows = _keyboard.Rows;
            int columns = _keyboard.Columns;
            int gap = 6;
            int left = 28;
            int top = 8;
            int availableWidth = Math.Max(40, Width - left - 10);
            int availableHeight = Math.Max(40, Height - top - 22);
            int cellWidth = Math.Max(28, (availableWidth - (columns - 1) * gap) / columns);
            int cellHeight = Math.Max(26, (availableHeight - (rows - 1) * gap) / rows);

            for (int column = 0; column < columns; column++)
            {
                string label = "PC" + (4 + column);
                TextRenderer.DrawText(g, label, AppTheme.SmallFont,
                    new Rectangle(left + column * (cellWidth + gap), top, cellWidth, 14),
                    AppTheme.TextDim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            }

            for (int row = 0; row < rows; row++)
            {
                TextRenderer.DrawText(g, "PC" + row, AppTheme.SmallFont,
                    new Rectangle(0, top + 16 + row * (cellHeight + gap), left - 4, cellHeight),
                    AppTheme.TextDim,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

                for (int column = 0; column < columns; column++)
                {
                    int x = left + column * (cellWidth + gap);
                    int y = top + 16 + row * (cellHeight + gap);
                    var bounds = new Rectangle(x, y, cellWidth, cellHeight);
                    bool pressed = _keyboard.IsKeyDown(row, column);
                    bool hover = row == _hoverRow && column == _hoverColumn;

                    Color fill = pressed
                        ? AppTheme.AccentSoft
                        : (hover ? AppTheme.SurfaceRaised : AppTheme.EditorBackground);
                    Color border = pressed
                        ? AppTheme.Accent
                        : (hover ? AppTheme.TextDim : AppTheme.BorderSoft);

                    AppTheme.FillRounded(g, bounds, 7, fill);
                    AppTheme.DrawRoundedBorder(g, bounds, 7, border);

                    string caption = (row * columns + column + 1).ToString();
                    TextRenderer.DrawText(g, caption, AppTheme.MonoBold, bounds,
                        pressed ? AppTheme.Text : AppTheme.TextMuted,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.NoPadding);
                }
            }

            TextRenderer.DrawText(
                g,
                "строки — выходы, столбцы — входы, активный ноль",
                AppTheme.SmallFont,
                new Rectangle(0, Height - 18, Width, 16),
                AppTheme.TextDim,
                TextFormatFlags.Left | TextFormatFlags.NoPadding);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int row, column;
            if (HitTest(e.Location, out row, out column))
            {
                if (row != _hoverRow || column != _hoverColumn)
                {
                    _hoverRow = row;
                    _hoverColumn = column;
                    Invalidate();
                }
            }
            else if (_hoverRow != -1)
            {
                _hoverRow = -1;
                _hoverColumn = -1;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hoverRow = -1;
            _hoverColumn = -1;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || _keyboard == null) return;

            int row, column;
            if (!HitTest(e.Location, out row, out column)) return;

            Capture = true;
            Press(row, column);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;

            Capture = false;
            Release();
        }

        private void Press(int row, int column)
        {
            if (_pressedRow >= 0 && (_pressedRow != row || _pressedColumn != column))
            {
                Release();
            }

            if (_keyboard.IsKeyDown(row, column)) return;

            _pressedRow = row;
            _pressedColumn = column;
            _keyboard.SetKey(row, column, true);
            KeyChanged?.Invoke(row, column, true);
            Invalidate();
        }

        private void Release()
        {
            if (_pressedRow < 0) return;

            int row = _pressedRow;
            int column = _pressedColumn;
            _pressedRow = -1;
            _pressedColumn = -1;
            _keyboard.SetKey(row, column, false);
            KeyChanged?.Invoke(row, column, false);
            Invalidate();
        }

        private bool HitTest(Point point, out int row, out int column)
        {
            row = -1;
            column = -1;
            if (_keyboard == null) return false;

            int rows = _keyboard.Rows;
            int columns = _keyboard.Columns;
            int gap = 6;
            int left = 28;
            int top = 8;
            int availableWidth = Math.Max(40, Width - left - 10);
            int availableHeight = Math.Max(40, Height - top - 22);
            int cellWidth = Math.Max(28, (availableWidth - (columns - 1) * gap) / columns);
            int cellHeight = Math.Max(26, (availableHeight - (rows - 1) * gap) / rows);

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < columns; c++)
                {
                    var bounds = new Rectangle(
                        left + c * (cellWidth + gap),
                        top + 16 + r * (cellHeight + gap),
                        cellWidth,
                        cellHeight);
                    if (bounds.Contains(point))
                    {
                        row = r;
                        column = c;
                        return true;
                    }
                }
            }
            return false;
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture) Release();
        }
    }
}
