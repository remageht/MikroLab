using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace MikroLab.Studio.Controls
{
    internal enum UiButtonKind
    {
        Primary,
        Secondary,
        Ghost,
        Danger,
        Subtle
    }

    /// <summary>
    /// Flat rounded button drawn by the app instead of the platform theme.
    /// Mirrors the WinForms <c>UiButton</c>: hover, pressed, active and
    /// disabled states share the same palette.
    /// </summary>
    internal sealed class UiButton : Control
    {
        private bool _hover;
        private bool _pressed;
        private string _text = string.Empty;
        private UiButtonKind _kind = UiButtonKind.Secondary;
        private bool _active;
        private double _cornerRadius = 8;
        private double _fontSize = AppTheme.SmallSize;
        private bool _bold;

        public UiButton()
        {
            Focusable = false;
            Cursor = new Cursor(StandardCursorType.Hand);
            Height = 34;
        }

        public event Action Click;

        public string Text
        {
            get => _text;
            set
            {
                string text = value ?? string.Empty;
                if (_text == text) return;
                _text = text;
                InvalidateVisual();
            }
        }

        public UiButtonKind Kind
        {
            get => _kind;
            set { if (_kind == value) return; _kind = value; InvalidateVisual(); }
        }

        public bool Active
        {
            get => _active;
            set { if (_active == value) return; _active = value; InvalidateVisual(); }
        }

        public double CornerRadius
        {
            get => _cornerRadius;
            set { if (Math.Abs(_cornerRadius - value) < 0.01) return; _cornerRadius = value; InvalidateVisual(); }
        }

        public double FontSize
        {
            get => _fontSize;
            set { if (Math.Abs(_fontSize - value) < 0.01) return; _fontSize = value; InvalidateVisual(); }
        }

        public bool Bold
        {
            get => _bold;
            set { if (_bold == value) return; _bold = value; InvalidateVisual(); }
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
            if (!_pressed) InvalidateVisual();
            base.OnPointerExited(e);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            PointerPoint point = e.GetCurrentPoint(this);
            if (!point.Properties.IsLeftButtonPressed) return;

            _pressed = true;
            e.Pointer.Capture(this);
            InvalidateVisual();
            base.OnPointerPressed(e);
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            if (!_pressed) return;

            _pressed = false;
            e.Pointer.Capture(null);
            InvalidateVisual();

            var bounds = new Rect(0, 0, Bounds.Width, Bounds.Height);
            if (IsEnabled && bounds.Contains(e.GetPosition(this)))
            {
                Click?.Invoke();
            }

            base.OnPointerReleased(e);
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            _pressed = false;
            InvalidateVisual();
            base.OnPointerCaptureLost(e);
        }

        public override void Render(DrawingContext context)
        {
            var bounds = new Rect(0, 0, Math.Max(0, Bounds.Width - 1), Math.Max(0, Bounds.Height - 1));
            Color fill;
            Color border;
            Color text;

            if (!IsEnabled)
            {
                fill = AppTheme.Surface;
                border = AppTheme.BorderSoft;
                text = AppTheme.TextDim;
            }
            else
            {
                switch (_kind)
                {
                    case UiButtonKind.Primary:
                        fill = _pressed ? AppTheme.Mix(AppTheme.Accent, Colors.Black, 0.15)
                            : _hover ? AppTheme.AccentHover : AppTheme.Accent;
                        border = fill;
                        text = Color.FromRgb(12, 17, 28);
                        break;
                    case UiButtonKind.Danger:
                        fill = _pressed ? AppTheme.Mix(AppTheme.Red, Colors.Black, 0.18)
                            : _hover ? AppTheme.Mix(AppTheme.Red, Colors.White, 0.08) : AppTheme.Mix(AppTheme.Red, AppTheme.Surface, 0.72);
                        border = AppTheme.Mix(AppTheme.Red, AppTheme.Surface, 0.25);
                        text = AppTheme.Red;
                        break;
                    case UiButtonKind.Ghost:
                        fill = _pressed ? AppTheme.SurfaceHover : _hover ? AppTheme.SurfaceRaised : Colors.Transparent;
                        border = Colors.Transparent;
                        text = _active ? AppTheme.Text : AppTheme.TextMuted;
                        break;
                    case UiButtonKind.Subtle:
                        fill = _pressed ? AppTheme.SurfaceHover : _hover ? AppTheme.SurfaceRaised : AppTheme.Surface;
                        border = AppTheme.BorderSoft;
                        text = AppTheme.TextMuted;
                        break;
                    default:
                        fill = _pressed ? AppTheme.SurfaceHover : _hover ? AppTheme.SurfaceRaised : AppTheme.SurfaceRaised;
                        border = _hover ? AppTheme.Border : AppTheme.BorderSoft;
                        text = AppTheme.Text;
                        break;
                }
            }

            if (_active)
            {
                fill = AppTheme.AccentSoft;
                border = AppTheme.Mix(AppTheme.Accent, AppTheme.Surface, 0.45);
                text = AppTheme.Text;
            }

            if (fill.A > 0)
            {
                AppTheme.FillRounded(context, bounds, _cornerRadius, AppTheme.Brush(fill));
            }
            if (border.A > 0)
            {
                AppTheme.DrawRoundedBorder(context, bounds, _cornerRadius, AppTheme.Pen(border));
            }

            FormattedText label = AppTheme.MakeText(_text, _fontSize, AppTheme.Brush(text), _bold);
            context.DrawText(label, new Point(
                Math.Round((Bounds.Width - label.Width) / 2),
                Math.Round((Bounds.Height - label.Height) / 2)));
        }
    }
}
