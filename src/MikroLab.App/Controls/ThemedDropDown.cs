using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MikroLab.App.Controls
{
    /// <summary>
    /// A compact dark drop-down used instead of the native ComboBox, whose
    /// closed edit area ignores the dark palette. Items are shown in a themed
    /// ContextMenuStrip; selection is reported only for user actions.
    /// </summary>
    internal sealed class ThemedDropDown : Control
    {
        private readonly List<string> _items = new List<string>();
        private readonly ToolTip _toolTip;
        private int _selectedIndex = -1;
        private bool _hover;
        private bool _pressed;
        private ContextMenuStrip _menu;

        public event Action<int> SelectionChanged;

        public string Placeholder { get; set; } = "—";

        public ThemedDropDown()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.Selectable,
                true);

            BackColor = AppTheme.Surface;
            ForeColor = AppTheme.Text;
            Font = AppTheme.SmallFont;
            Height = 30;
            TabStop = true;
            Cursor = Cursors.Hand;
            _toolTip = new ToolTip();
        }

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
            Invalidate();
        }

        public void AddItem(string item)
        {
            _items.Add(item ?? string.Empty);
            Invalidate();
        }

        public void SelectIndex(int index)
        {
            _selectedIndex = index >= 0 && index < _items.Count ? index : -1;
            Invalidate();
        }

        public void SetToolTip(string text)
        {
            _toolTip.SetToolTip(this, text);
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

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _pressed = true;
                Invalidate();
                Focus();
                OpenMenu();
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _pressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space || e.KeyCode == Keys.Down)
            {
                OpenMenu();
                e.Handled = true;
                return;
            }

            base.OnKeyDown(e);
        }

        private void OpenMenu()
        {
            if (_menu != null && _menu.Visible)
            {
                _menu.Close();
            }

            if (_menu == null)
            {
                _menu = new ContextMenuStrip
                {
                    BackColor = AppTheme.SurfaceRaised,
                    ForeColor = AppTheme.Text,
                    Font = AppTheme.UiFont,
                    ShowImageMargin = false,
                    ShowCheckMargin = false,
                    Padding = new Padding(4),
                    Renderer = new DarkMenuRenderer()
                };
                _menu.Closed += (sender, args) => Invalidate();
            }

            // Rebuild the items in place. Disposing the menu inside its own
            // Closed handler breaks WinForms key processing (the next Escape
            // or arrow key hits a disposed ContextMenuStrip).
            while (_menu.Items.Count > 0)
            {
                _menu.Items[0].Dispose();
            }

            _menu.MinimumSize = new Size(Math.Max(Width, 180), 0);

            for (int i = 0; i < _items.Count; i++)
            {
                int index = i;
                var item = new ToolStripMenuItem(_items[i])
                {
                    ForeColor = i == _selectedIndex ? AppTheme.Text : AppTheme.TextMuted,
                    Font = i == _selectedIndex ? AppTheme.UiFontMedium : AppTheme.UiFont,
                    Padding = new Padding(8, 5, 8, 5),
                    BackColor = Color.Transparent
                };
                item.Click += (sender, args) =>
                {
                    SelectIndex(index);
                    SelectionChanged?.Invoke(index);
                };
                _menu.Items.Add(item);
            }

            _menu.Show(this, new Point(0, Height + 2));
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Parent != null && Parent.BackColor != Color.Transparent ? Parent.BackColor : AppTheme.Surface);

            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill = _pressed
                ? AppTheme.SurfaceHover
                : _hover || Focused ? AppTheme.Mix(AppTheme.SurfaceRaised, AppTheme.SurfaceHover, 0.55) : AppTheme.SurfaceRaised;
            AppTheme.FillRounded(graphics, bounds, 7, fill);
            AppTheme.DrawRoundedBorder(graphics, bounds, 7, Focused ? AppTheme.Mix(AppTheme.Accent, AppTheme.Surface, 0.55) : AppTheme.Border);

            string text = _selectedIndex >= 0 && _selectedIndex < _items.Count
                ? _items[_selectedIndex]
                : Placeholder;

            TextRenderer.DrawText(
                graphics,
                text,
                Font,
                new Rectangle(10, 0, Width - 40, Height),
                Enabled ? AppTheme.Text : AppTheme.TextDim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

            int arrowX = Width - 20;
            int arrowY = Height / 2 - 1;
            using (var pen = new Pen(_hover ? AppTheme.Text : AppTheme.TextMuted, 1.5f))
            {
                graphics.DrawLine(pen, arrowX - 4, arrowY - 2, arrowX, arrowY + 2);
                graphics.DrawLine(pen, arrowX, arrowY + 2, arrowX + 4, arrowY - 2);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _menu?.Dispose();
                _toolTip?.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer()
            : base(new DarkMenuColorTable())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Selected ? AppTheme.Text : AppTheme.TextMuted;
            base.OnRenderItemText(e);
        }
    }

    internal sealed class DarkMenuColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => AppTheme.SurfaceRaised;
        public override Color ImageMarginGradientBegin => AppTheme.SurfaceRaised;
        public override Color ImageMarginGradientMiddle => AppTheme.SurfaceRaised;
        public override Color ImageMarginGradientEnd => AppTheme.SurfaceRaised;
        public override Color MenuBorder => AppTheme.Border;
        public override Color MenuItemBorder => AppTheme.Accent;
        public override Color MenuItemSelected => AppTheme.AccentSoft;
        public override Color MenuItemSelectedGradientBegin => AppTheme.AccentSoft;
        public override Color MenuItemSelectedGradientEnd => AppTheme.AccentSoft;
        public override Color MenuItemPressedGradientBegin => AppTheme.SurfaceHover;
        public override Color MenuItemPressedGradientEnd => AppTheme.SurfaceHover;
        public override Color CheckBackground => AppTheme.AccentSoft;
        public override Color CheckSelectedBackground => AppTheme.AccentSoft;
        public override Color SeparatorDark => AppTheme.BorderSoft;
        public override Color SeparatorLight => AppTheme.BorderSoft;
    }
}
