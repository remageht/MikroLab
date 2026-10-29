using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace MikroLab.Studio.Controls
{
    /// <summary>
    /// Compact dark drop-down. The closed control is drawn by the app and the
    /// item list is a themed popup, so the native ComboBox chrome never shows.
    /// Selection is reported only for user actions, matching the WinForms
    /// control.
    /// </summary>
    internal sealed class ThemedDropDown : Control
    {
        private readonly List<string> _items = new List<string>();
        private readonly Popup _popup;
        private readonly Border _popupBorder;
        private readonly StackPanel _popupPanel;
        private int _selectedIndex = -1;
        private bool _hover;
        private bool _pressed;

        public ThemedDropDown()
        {
            Focusable = true;
            Cursor = new Cursor(StandardCursorType.Hand);
            Height = 30;

            _popupPanel = new StackPanel { Spacing = 1 };
            _popupBorder = new Border
            {
                Background = AppTheme.Brush(AppTheme.SurfaceRaised),
                BorderBrush = AppTheme.Brush(AppTheme.Border),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(3),
                Child = _popupPanel
            };

            _popup = new Popup
            {
                PlacementTarget = this,
                Placement = PlacementMode.Bottom,
                IsLightDismissEnabled = true,
                Child = _popupBorder
            };

            AddHandler(PointerPressedEvent, OnPressed, Avalonia.Interactivity.RoutingStrategies.Bubble);
        }

        public event Action<int> SelectionChanged;

        public string Placeholder { get; set; } = "—";

        public int SelectedIndex => _selectedIndex;

        public string SelectedItem => _selectedIndex >= 0 && _selectedIndex < _items.Count
            ? _items[_selectedIndex]
            : null;

        public void SetItems(IEnumerable<string> items)
        {
            _items.Clear();
            if (items != null)
            {
                foreach (string item in items) _items.Add(item ?? string.Empty);
            }
            if (_selectedIndex >= _items.Count) _selectedIndex = -1;
            InvalidateVisual();
        }

        public void AddItem(string item)
        {
            _items.Add(item ?? string.Empty);
            InvalidateVisual();
        }

        public void SelectIndex(int index)
        {
            _selectedIndex = index >= 0 && index < _items.Count ? index : -1;
            InvalidateVisual();
        }

        public void SetToolTip(string text)
        {
            ToolTip.SetTip(this, text);
        }

        private void OnPressed(object sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

            Focus();
            _pressed = true;
            InvalidateVisual();
            OpenMenu();
            e.Handled = true;
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
            _pressed = false;
            InvalidateVisual();
            base.OnPointerExited(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Space || e.Key == Key.Down)
            {
                OpenMenu();
                e.Handled = true;
                return;
            }

            base.OnKeyDown(e);
        }

        private void OpenMenu()
        {
            _popupPanel.Children.Clear();
            _popupBorder.MinWidth = Math.Max(Bounds.Width, 180);

            for (int i = 0; i < _items.Count; i++)
            {
                int index = i;
                var label = AppTheme.Label(
                    _items[i],
                    i == _selectedIndex ? AppTheme.Text : AppTheme.TextMuted,
                    AppTheme.UiSize,
                    i == _selectedIndex);

                var row = new Border
                {
                    Background = AppTheme.Brush(i == _selectedIndex ? AppTheme.AccentSoft : AppTheme.SurfaceRaised),
                    Padding = new Thickness(9, 6),
                    Child = label,
                    Cursor = new Cursor(StandardCursorType.Hand)
                };
                row.PointerEntered += (sender, args) =>
                {
                    if (index != _selectedIndex) row.Background = AppTheme.Brush(AppTheme.SurfaceHover);
                };
                row.PointerExited += (sender, args) =>
                {
                    if (index != _selectedIndex) row.Background = AppTheme.Brush(AppTheme.SurfaceRaised);
                };
                row.PointerPressed += (sender, args) =>
                {
                    if (!args.GetCurrentPoint(row).Properties.IsLeftButtonPressed) return;
                    _popup.IsOpen = false;
                    SelectIndex(index);
                    SelectionChanged?.Invoke(index);
                    args.Handled = true;
                };
                _popupPanel.Children.Add(row);
            }

            _popup.IsOpen = true;
            _pressed = false;
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            var bounds = new Rect(0, 0, Math.Max(0, Bounds.Width - 1), Math.Max(0, Bounds.Height - 1));
            Color fill = _pressed
                ? AppTheme.SurfaceHover
                : _hover || IsFocused ? AppTheme.Mix(AppTheme.SurfaceRaised, AppTheme.SurfaceHover, 0.55) : AppTheme.SurfaceRaised;

            AppTheme.FillRounded(context, bounds, 7, AppTheme.Brush(fill));
            AppTheme.DrawRoundedBorder(context, bounds, 7,
                AppTheme.Pen(IsFocused ? AppTheme.Mix(AppTheme.Accent, AppTheme.Surface, 0.55) : AppTheme.Border));

            string text = _selectedIndex >= 0 && _selectedIndex < _items.Count
                ? _items[_selectedIndex]
                : Placeholder;

            FormattedText label = AppTheme.MakeText(text, AppTheme.SmallSize,
                AppTheme.Brush(IsEnabled ? AppTheme.Text : AppTheme.TextDim));
            using (context.PushClip(new Rect(8, 0, Math.Max(0, Bounds.Width - 38), Bounds.Height)))
            {
                context.DrawText(label, new Point(10, Math.Round((Bounds.Height - label.Height) / 2)));
            }

            double arrowX = Bounds.Width - 20;
            double arrowY = Bounds.Height / 2 - 1;
            IPen pen = AppTheme.Pen(_hover ? AppTheme.Text : AppTheme.TextMuted, 1.5);
            context.DrawLine(pen, new Point(arrowX - 4, arrowY - 2), new Point(arrowX, arrowY + 2));
            context.DrawLine(pen, new Point(arrowX, arrowY + 2), new Point(arrowX + 4, arrowY - 2));
        }
    }
}
