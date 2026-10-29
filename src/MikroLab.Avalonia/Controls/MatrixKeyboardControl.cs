using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using MikroLab.Core.Devices;

namespace MikroLab.Studio.Controls
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

        public MatrixKeyboardControl()
        {
            ClipToBounds = true;
            Cursor = new Cursor(StandardCursorType.Hand);
        }

        public event Action<int, int, bool> KeyChanged;

        public MatrixKeyboard Keyboard => _keyboard;

        public void Attach(MatrixKeyboard keyboard)
        {
            _keyboard = keyboard;
            InvalidateVisual();
        }

        public void SyncFromModel()
        {
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            context.DrawRectangle(AppTheme.Brush(AppTheme.Surface), null,
                new Rect(0, 0, Bounds.Width, Bounds.Height));

            if (_keyboard == null)
            {
                AppTheme.DrawText(context, "Клавиатура не подключена", new Point(8, 8),
                    AppTheme.UiSize, AppTheme.Brush(AppTheme.TextDim));
                return;
            }

            int rows = _keyboard.Rows;
            int columns = _keyboard.Columns;
            const double gap = 6;
            const double left = 28;
            const double top = 8;
            double availableWidth = Math.Max(40, Bounds.Width - left - 10);
            double availableHeight = Math.Max(40, Bounds.Height - top - 22);
            double cellWidth = Math.Max(28, (availableWidth - (columns - 1) * gap) / columns);
            double cellHeight = Math.Max(26, (availableHeight - (rows - 1) * gap) / rows);

            for (int column = 0; column < columns; column++)
            {
                FormattedText label = AppTheme.MakeText("PC" + (4 + column), AppTheme.SmallSize,
                    AppTheme.Brush(AppTheme.TextDim));
                context.DrawText(label, new Point(
                    left + column * (cellWidth + gap) + (cellWidth - label.Width) / 2, top));
            }

            for (int row = 0; row < rows; row++)
            {
                FormattedText label = AppTheme.MakeText("PC" + row, AppTheme.SmallSize,
                    AppTheme.Brush(AppTheme.TextDim));
                context.DrawText(label, new Point(
                    left - 4 - label.Width,
                    top + 16 + row * (cellHeight + gap) + (cellHeight - label.Height) / 2));

                for (int column = 0; column < columns; column++)
                {
                    double x = left + column * (cellWidth + gap);
                    double y = top + 16 + row * (cellHeight + gap);
                    var bounds = new Rect(x, y, cellWidth, cellHeight);
                    bool pressed = _keyboard.IsKeyDown(row, column);
                    bool hover = row == _hoverRow && column == _hoverColumn;

                    Color fill = pressed
                        ? AppTheme.AccentSoft
                        : (hover ? AppTheme.SurfaceRaised : AppTheme.EditorBackground);
                    Color border = pressed
                        ? AppTheme.Accent
                        : (hover ? AppTheme.TextDim : AppTheme.BorderSoft);

                    AppTheme.FillRounded(context, bounds, 7, AppTheme.Brush(fill));
                    AppTheme.DrawRoundedBorder(context, bounds, 7, AppTheme.Pen(border));

                    FormattedText caption = AppTheme.MakeText(
                        (row * columns + column + 1).ToString(),
                        AppTheme.MonoBoldSize,
                        AppTheme.Brush(pressed ? AppTheme.Text : AppTheme.TextMuted), true, true);
                    context.DrawText(caption, new Point(
                        x + (cellWidth - caption.Width) / 2,
                        y + (cellHeight - caption.Height) / 2));
                }
            }

            AppTheme.DrawText(context,
                "строки — выходы, столбцы — входы, активный ноль",
                new Point(0, Bounds.Height - 18),
                AppTheme.SmallSize, AppTheme.Brush(AppTheme.TextDim));
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            if (HitTest(e.GetPosition(this), out int row, out int column))
            {
                if (row != _hoverRow || column != _hoverColumn)
                {
                    _hoverRow = row;
                    _hoverColumn = column;
                    InvalidateVisual();
                }
            }
            else if (_hoverRow != -1)
            {
                _hoverRow = -1;
                _hoverColumn = -1;
                InvalidateVisual();
            }

            base.OnPointerMoved(e);
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            _hoverRow = -1;
            _hoverColumn = -1;
            InvalidateVisual();
            base.OnPointerExited(e);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            if (_keyboard == null) return;
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

            if (!HitTest(e.GetPosition(this), out int row, out int column)) return;

            e.Pointer.Capture(this);
            Press(row, column);
            e.Handled = true;
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            if (_pressedRow < 0) return;
            e.Pointer.Capture(null);
            Release();
            e.Handled = true;
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            Release();
            base.OnPointerCaptureLost(e);
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
            InvalidateVisual();
        }

        private void Release()
        {
            if (_pressedRow < 0 || _keyboard == null) return;

            int row = _pressedRow;
            int column = _pressedColumn;
            _pressedRow = -1;
            _pressedColumn = -1;
            _keyboard.SetKey(row, column, false);
            KeyChanged?.Invoke(row, column, false);
            InvalidateVisual();
        }

        private bool HitTest(Point point, out int row, out int column)
        {
            row = -1;
            column = -1;
            if (_keyboard == null) return false;

            int rows = _keyboard.Rows;
            int columns = _keyboard.Columns;
            const double gap = 6;
            const double left = 28;
            const double top = 8;
            double availableWidth = Math.Max(40, Bounds.Width - left - 10);
            double availableHeight = Math.Max(40, Bounds.Height - top - 22);
            double cellWidth = Math.Max(28, (availableWidth - (columns - 1) * gap) / columns);
            double cellHeight = Math.Max(26, (availableHeight - (rows - 1) * gap) / rows);

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < columns; c++)
                {
                    var bounds = new Rect(
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
    }
}
