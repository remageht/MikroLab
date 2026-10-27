using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MikroLab.App
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
    /// A flat, rounded button that follows the dark palette and does not rely
    /// on the platform theme. It keeps normal Button keyboard behaviour.
    /// </summary>
    internal sealed class UiButton : Button
    {
        private bool _hover;
        private bool _pressed;

        public UiButtonKind Kind { get; set; } = UiButtonKind.Secondary;
        public int CornerRadius { get; set; } = 8;
        public bool Active { get; set; }

        /// <summary>
        /// Colour used as the backdrop behind the rounded shape. Needed when
        /// the button sits on a transparent container: clearing with
        /// Color.Transparent would otherwise produce a black rectangle.
        /// </summary>
        public Color ClearColor { get; set; } = Color.Empty;

        public UiButton()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor,
                true);

            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            BackColor = Color.Transparent;
            ForeColor = AppTheme.Text;
            Font = AppTheme.UiFont;
            Cursor = Cursors.Hand;
            Height = 34;
            TabStop = false;
            TextAlign = ContentAlignment.MiddleCenter;
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
            _pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs mevent)
        {
            if (mevent.Button == MouseButtons.Left)
            {
                _pressed = true;
                Invalidate();
            }
            base.OnMouseDown(mevent);
        }

        protected override void OnMouseUp(MouseEventArgs mevent)
        {
            _pressed = false;
            Invalidate();
            base.OnMouseUp(mevent);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            Graphics graphics = pevent.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color backdrop = !ClearColor.IsEmpty
                ? ClearColor
                : (Parent != null && Parent.BackColor != Color.Transparent ? Parent.BackColor : AppTheme.Surface);
            graphics.Clear(backdrop);

            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill;
            Color border;
            Color text;

            if (!Enabled)
            {
                fill = AppTheme.Surface;
                border = AppTheme.BorderSoft;
                text = AppTheme.TextDim;
            }
            else
            {
                switch (Kind)
                {
                    case UiButtonKind.Primary:
                        fill = _pressed ? AppTheme.Mix(AppTheme.Accent, Color.Black, 0.15)
                            : _hover ? AppTheme.AccentHover : AppTheme.Accent;
                        border = fill;
                        text = Color.FromArgb(12, 17, 28);
                        break;
                    case UiButtonKind.Danger:
                        fill = _pressed ? AppTheme.Mix(AppTheme.Red, Color.Black, 0.18)
                            : _hover ? AppTheme.Mix(AppTheme.Red, Color.White, 0.08) : AppTheme.Mix(AppTheme.Red, AppTheme.Surface, 0.72);
                        border = AppTheme.Mix(AppTheme.Red, AppTheme.Surface, 0.25);
                        text = AppTheme.Red;
                        break;
                    case UiButtonKind.Ghost:
                        fill = _pressed ? AppTheme.SurfaceHover : _hover ? AppTheme.SurfaceRaised : Color.Transparent;
                        border = Color.Transparent;
                        text = Active ? AppTheme.Text : AppTheme.TextMuted;
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

            if (Active)
            {
                fill = AppTheme.AccentSoft;
                border = AppTheme.Mix(AppTheme.Accent, AppTheme.Surface, 0.45);
                text = AppTheme.Text;
            }

            if (fill.A > 0)
            {
                AppTheme.FillRounded(graphics, bounds, CornerRadius, fill);
            }
            if (border.A > 0)
            {
                AppTheme.DrawRoundedBorder(graphics, bounds, CornerRadius, border);
            }

            TextRenderer.DrawText(
                graphics,
                Text,
                Font,
                bounds,
                text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            if (Focused && ShowFocusCues)
            {
                Rectangle focus = Rectangle.Inflate(bounds, -3, -3);
                using (var pen = new Pen(AppTheme.Mix(text, Color.Transparent, 0.5)))
                {
                    graphics.DrawRectangle(pen, focus);
                }
            }
        }
    }

    /// <summary>A compact icon button with a text glyph, used by toolbars.</summary>
    internal sealed class IconButton : Button
    {
        private bool _hover;
        private bool _pressed;

        public Color GlyphColor { get; set; } = AppTheme.Text;
        public Color HoverColor { get; set; } = AppTheme.SurfaceRaised;
        public Color PressedColor { get; set; } = AppTheme.SurfaceHover;
        public string Glyph { get; set; }

        public IconButton()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Color.Transparent;
            Font = AppTheme.UiFont;
            Cursor = Cursors.Hand;
            TabStop = false;
            Size = new Size(34, 34);
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
            _pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs mevent)
        {
            _pressed = true;
            Invalidate();
            base.OnMouseDown(mevent);
        }

        protected override void OnMouseUp(MouseEventArgs mevent)
        {
            _pressed = false;
            Invalidate();
            base.OnMouseUp(mevent);
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            Graphics graphics = pevent.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Parent != null && Parent.BackColor != Color.Transparent ? Parent.BackColor : AppTheme.Surface);

            Color back = _pressed ? PressedColor : _hover ? HoverColor : Color.Transparent;
            if (back.A > 0)
            {
                AppTheme.FillRounded(graphics, new Rectangle(0, 0, Width - 1, Height - 1), 8, back);
            }

            string text = string.IsNullOrEmpty(Glyph) ? Text : Glyph;
            TextRenderer.DrawText(
                graphics,
                text,
                Font,
                new Rectangle(0, 0, Width, Height),
                Enabled ? GlyphColor : AppTheme.TextDim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }
}
