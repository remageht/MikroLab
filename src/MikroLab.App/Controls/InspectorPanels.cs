using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;
using MikroLab.Core.Machine;

namespace MikroLab.App.Controls
{
    internal sealed class RegisterPanel : Control
    {
        private readonly List<RegisterHit> _hits = new List<RegisterHit>();
        private MachineSnapshot _snapshot;

        public event Action<string, ushort> RegisterEdited;

        public RegisterPanel()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.StandardClick |
                ControlStyles.StandardDoubleClick,
                true);
            BackColor = AppTheme.Surface;
            Font = AppTheme.MonoSmall;
            Padding = new Padding(12);
            Cursor = Cursors.Default;
        }

        public void UpdateSnapshot(MachineSnapshot snapshot)
        {
            _snapshot = snapshot;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.Clear(AppTheme.Surface);
            _hits.Clear();

            if (_snapshot == null)
            {
                TextRenderer.DrawText(g, "Нет данных", AppTheme.UiFont, new Point(16, 16), AppTheme.TextDim);
                return;
            }

            string[] names = { "A", "B", "C", "D", "E", "H", "L", "M", "F" };
            ushort[] values =
            {
                _snapshot.A, _snapshot.B, _snapshot.C,
                _snapshot.D, _snapshot.E, _snapshot.H,
                _snapshot.L, _snapshot.M, _snapshot.Flags.ToByte()
            };

            int top = 12;
            int rowHeight = 25;
            int colWidth = Math.Max(150, Width / 2 - 8);
            int left = 14;

            TextRenderer.DrawText(g, "РЕГИСТРЫ", AppTheme.SmallBoldFont, new Point(left, top), AppTheme.TextDim);
            TextRenderer.DrawText(
                g,
                "двойной клик — изменить",
                AppTheme.SmallFont,
                new Rectangle(left, top - 1, Width - left - 14, 22),
                AppTheme.TextDim,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            top += 26;

            using (var line = new Pen(AppTheme.BorderSoft))
            {
                g.DrawLine(line, left, top - 6, Width - 14, top - 6);
            }

            for (int i = 0; i < names.Length; i++)
            {
                int column = i / 5;
                int row = i % 5;
                int x = left + column * colWidth;
                int y = top + row * rowHeight;
                bool highlighted = names[i] == "M" || names[i] == "F";
                string text = values[i].ToString("X2") + "h";

                TextRenderer.DrawText(g, names[i], AppTheme.SmallFont, new Point(x, y), AppTheme.TextMuted);
                TextRenderer.DrawText(g, text, AppTheme.MonoBold, new Point(x + 26, y - 1),
                    highlighted ? AppTheme.Amber : AppTheme.Text);

                _hits.Add(new RegisterHit(
                    names[i],
                    values[i],
                    2,
                    new Rectangle(x, y - 2, Math.Max(100, colWidth - 12), rowHeight)));
            }

            top += 5 * rowHeight + 12;
            using (var line = new Pen(AppTheme.BorderSoft))
            {
                g.DrawLine(line, left, top - 6, Width - 14, top - 6);
            }

            TextRenderer.DrawText(g, "PC", AppTheme.SmallFont, new Point(left, top), AppTheme.TextMuted);
            TextRenderer.DrawText(g, _snapshot.PC.ToString("X4") + "h", AppTheme.MonoBold,
                new Point(left + 26, top - 1), AppTheme.Cyan);
            TextRenderer.DrawText(g, "SP", AppTheme.SmallFont, new Point(left + colWidth, top), AppTheme.TextMuted);
            TextRenderer.DrawText(g, _snapshot.SP.ToString("X4") + "h", AppTheme.MonoBold,
                new Point(left + colWidth + 26, top - 1), AppTheme.Cyan);

            _hits.Add(new RegisterHit("PC", _snapshot.PC, 4,
                new Rectangle(left, top - 2, Math.Max(100, colWidth - 12), rowHeight)));
            _hits.Add(new RegisterHit("SP", _snapshot.SP, 4,
                new Rectangle(left + colWidth, top - 2, Math.Max(100, colWidth - 12), rowHeight)));

            top += rowHeight + 10;
            TextRenderer.DrawText(g, "ФЛАГИ", AppTheme.SmallBoldFont, new Point(left, top), AppTheme.TextDim);
            top += 24;

            DrawFlag(g, left, top, "C", _snapshot.Flags.C);
            DrawFlag(g, left + 52, top, "Z", _snapshot.Flags.Z);
            DrawFlag(g, left + 104, top, "S", _snapshot.Flags.S);
            DrawFlag(g, left + 156, top, "P", _snapshot.Flags.P);
            DrawFlag(g, left + 208, top, "AC", _snapshot.Flags.AC);

            top += 40;
            TextRenderer.DrawText(g, "ТАКТЫ", AppTheme.SmallBoldFont, new Point(left, top), AppTheme.TextDim);
            TextRenderer.DrawText(g, _snapshot.Cycles.ToString("N0"), AppTheme.MonoSmall,
                new Point(left + 66, top - 1), AppTheme.Text);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || e.Clicks < 2) return;

            foreach (RegisterHit hit in _hits)
            {
                if (!hit.Bounds.Contains(e.Location)) continue;

                using (var dialog = new RegisterEditDialog(hit.Name, hit.Value, hit.Digits))
                {
                    if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
                    {
                        RegisterEdited?.Invoke(hit.Name, dialog.Value);
                    }
                }
                return;
            }
        }

        private readonly struct RegisterHit
        {
            public string Name { get; }
            public ushort Value { get; }
            public int Digits { get; }
            public Rectangle Bounds { get; }

            public RegisterHit(string name, ushort value, int digits, Rectangle bounds)
            {
                Name = name;
                Value = value;
                Digits = digits;
                Bounds = bounds;
            }
        }

        private static void DrawFlag(Graphics g, int x, int y, string name, bool on)
        {
            Rectangle bounds = new Rectangle(x, y, 38, 26);
            AppTheme.FillRounded(g, bounds, 6, on ? AppTheme.AccentSoft : AppTheme.SurfaceRaised);
            AppTheme.DrawRoundedBorder(g, bounds, 6, on ? AppTheme.Accent : AppTheme.BorderSoft);
            TextRenderer.DrawText(g, name, AppTheme.SmallBoldFont, bounds, on ? AppTheme.Text : AppTheme.TextDim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    internal sealed class RegisterEditDialog : Form
    {
        private readonly TextBox _box;
        private readonly bool _word;

        public ushort Value { get; private set; }

        public RegisterEditDialog(string name, ushort current, int digits)
        {
            _word = digits >= 4;
            Text = "Изменить регистр " + name;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = AppTheme.Surface;
            ForeColor = AppTheme.Text;
            Font = AppTheme.UiFont;
            ClientSize = new Size(320, 132);

            Label label = AppTheme.Label(
                "Значение (hex, " + (_word ? "0000–FFFF" : "00–FF") + "):",
                AppTheme.TextMuted,
                AppTheme.SmallFont);
            label.Location = new Point(18, 18);

            _box = new TextBox
            {
                Text = current.ToString(_word ? "X4" : "X2", CultureInfo.InvariantCulture),
                Location = new Point(18, 42),
                Width = 130,
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
                Location = new Point(124, 88)
            };
            var cancel = new UiButton
            {
                Text = "Отмена",
                Kind = UiButtonKind.Secondary,
                Width = 90,
                Height = 30,
                Location = new Point(220, 88),
                DialogResult = DialogResult.Cancel
            };

            ok.Click += (sender, args) =>
            {
                string text = (_box.Text ?? string.Empty).Trim().TrimEnd('h', 'H');
                if (_word)
                {
                    if (ushort.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort word))
                    {
                        Value = word;
                        DialogResult = DialogResult.OK;
                        Close();
                        return;
                    }
                }
                else if (byte.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte value))
                {
                    Value = value;
                    DialogResult = DialogResult.OK;
                    Close();
                    return;
                }

                _box.BackColor = Color.FromArgb(76, 36, 42);
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

    internal sealed class StackPanel : UserControl
    {
        private readonly Label _depth;
        private readonly ListBox _list;

        public StackPanel()
        {
            BackColor = AppTheme.Surface;

            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 36,
                BackColor = AppTheme.Surface,
                Padding = new Padding(12, 8, 12, 4)
            };
            Label title = AppTheme.Label("СТЕК ВЫЗОВОВ", AppTheme.TextDim, AppTheme.SmallBoldFont);
            title.Location = new Point(12, 10);
            _depth = AppTheme.Label("Глубина: 0", AppTheme.TextMuted, AppTheme.SmallFont);
            _depth.AutoSize = false;
            _depth.TextAlign = ContentAlignment.MiddleRight;
            _depth.Dock = DockStyle.Right;
            _depth.Width = 110;
            header.Controls.Add(title);
            header.Controls.Add(_depth);

            _list = new ListBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                BackColor = AppTheme.Surface,
                ForeColor = AppTheme.Text,
                Font = AppTheme.MonoSmall,
                IntegralHeight = false,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 24
            };
            _list.DrawItem += (sender, args) =>
            {
                if (args.Index < 0) return;
                bool selected = (args.State & DrawItemState.Selected) == DrawItemState.Selected;
                using (var brush = new SolidBrush(selected ? AppTheme.AccentSoft : AppTheme.Surface))
                {
                    args.Graphics.FillRectangle(brush, args.Bounds);
                }
                string text = _list.Items[args.Index].ToString();
                TextRenderer.DrawText(args.Graphics, text, AppTheme.MonoSmall,
                    new Rectangle(args.Bounds.X + 8, args.Bounds.Y, args.Bounds.Width - 8, args.Bounds.Height),
                    selected ? AppTheme.Text : AppTheme.TextMuted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
            };

            Controls.Add(_list);
            Controls.Add(header);
        }

        public void UpdateStack(MachineSnapshot snapshot, ushort[] stack)
        {
            int depth = snapshot != null ? snapshot.CallStackDepth : 0;
            _depth.Text = "Глубина: " + depth;
            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                if (stack == null || stack.Length == 0)
                {
                    _list.Items.Add("— стек пуст —");
                }
                else
                {
                    for (int i = 0; i < stack.Length; i++)
                    {
                        _list.Items.Add((i + 1) + ".  RET → " + stack[i].ToString("X4") + "h");
                    }
                }
            }
            finally
            {
                _list.EndUpdate();
            }
        }
    }
}
