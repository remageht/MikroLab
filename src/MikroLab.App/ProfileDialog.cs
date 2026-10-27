using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using MikroLab.App.Controls;
using MikroLab.Core.Machine;

namespace MikroLab.App
{
    /// <summary>Editor for the board's default origin, initial PC and stack.</summary>
    internal sealed class ProfileDialog : Form
    {
        private readonly TextBox _name;
        private readonly TextBox _origin;
        private readonly TextBox _entry;
        private readonly TextBox _stack;

        public MachineProfile Result { get; private set; }

        public ProfileDialog(MachineProfile current)
        {
            current = current ?? MachineProfile.Vntu;
            Text = "Профиль памяти";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = AppTheme.Surface;
            ForeColor = AppTheme.Text;
            Font = AppTheme.UiFont;
            ClientSize = new Size(360, 236);

            Label intro = AppTheme.Label(
                "Профиль задаёт адрес по умолчанию для ORG, начальный PC и SP.",
                AppTheme.TextMuted,
                AppTheme.SmallFont);
            intro.Location = new Point(18, 14);

            _name = MakeBox(current.Name, 18, 46, 318);
            _origin = MakeBox(current.Origin.ToString("X4"), 18, 92, 96);
            _entry = MakeBox(current.EntryPoint.ToString("X4"), 130, 92, 96);
            _stack = MakeBox(current.StackPointer.ToString("X4"), 242, 92, 94);

            Label hint = AppTheme.Label(
                "Адреса вводятся в hex: 0800, 8000, 0FFF…",
                AppTheme.TextDim,
                AppTheme.SmallFont);
            hint.Location = new Point(18, 128);

            var vntu = new UiButton
            {
                Text = "ВНТУ",
                Kind = UiButtonKind.Ghost,
                Width = 84,
                Height = 28,
                Location = new Point(18, 154)
            };
            vntu.Click += (sender, args) => Fill(MachineProfile.Vntu);

            var umk = new UiButton
            {
                Text = "УМК",
                Kind = UiButtonKind.Ghost,
                Width = 84,
                Height = 28,
                Location = new Point(108, 154)
            };
            umk.Click += (sender, args) => Fill(MachineProfile.Umk);

            var ok = new UiButton
            {
                Text = "Применить",
                Kind = UiButtonKind.Primary,
                Width = 112,
                Height = 30,
                Location = new Point(126, 194)
            };
            var cancel = new UiButton
            {
                Text = "Отмена",
                Kind = UiButtonKind.Secondary,
                Width = 96,
                Height = 30,
                Location = new Point(246, 194),
                DialogResult = DialogResult.Cancel
            };

            ok.Click += (sender, args) =>
            {
                if (!TryReadHex(_origin, out ushort origin) ||
                    !TryReadHex(_entry, out ushort entry) ||
                    !TryReadHex(_stack, out ushort stack))
                {
                    return;
                }

                Result = new MachineProfile(_name.Text, origin, entry, stack);
                DialogResult = DialogResult.OK;
                Close();
            };

            AcceptButton = ok;
            CancelButton = cancel;
            Controls.Add(intro);
            Controls.Add(_name);
            Controls.Add(_origin);
            Controls.Add(_entry);
            Controls.Add(_stack);
            Controls.Add(hint);
            Controls.Add(vntu);
            Controls.Add(umk);
            Controls.Add(ok);
            Controls.Add(cancel);
            AddLabels();
        }

        private void AddLabels()
        {
            // Labels are added after the boxes so they paint above the surface.
            var name = AppTheme.Label("Название", AppTheme.TextMuted, AppTheme.SmallFont);
            name.Location = new Point(18, 28);
            var origin = AppTheme.Label("ORG", AppTheme.TextMuted, AppTheme.SmallFont);
            origin.Location = new Point(18, 76);
            var entry = AppTheme.Label("PC", AppTheme.TextMuted, AppTheme.SmallFont);
            entry.Location = new Point(130, 76);
            var stack = AppTheme.Label("SP", AppTheme.TextMuted, AppTheme.SmallFont);
            stack.Location = new Point(242, 76);
            Controls.Add(name);
            Controls.Add(origin);
            Controls.Add(entry);
            Controls.Add(stack);
        }

        private void Fill(MachineProfile profile)
        {
            _name.Text = profile.Name;
            _origin.Text = profile.Origin.ToString("X4");
            _entry.Text = profile.EntryPoint.ToString("X4");
            _stack.Text = profile.StackPointer.ToString("X4");
        }

        private static TextBox MakeBox(string text, int x, int y, int width)
        {
            return new TextBox
            {
                Text = text,
                Location = new Point(x, y),
                Width = width,
                BackColor = AppTheme.EditorBackground,
                ForeColor = AppTheme.Text,
                BorderStyle = BorderStyle.FixedSingle,
                Font = AppTheme.MonoSmall
            };
        }

        private static bool TryReadHex(TextBox box, out ushort value)
        {
            string text = (box.Text ?? string.Empty).Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                text = text.Substring(2);
            text = text.TrimEnd('h', 'H');
            if (ushort.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value))
            {
                box.BackColor = AppTheme.EditorBackground;
                return true;
            }

            box.BackColor = Color.FromArgb(76, 36, 42);
            return false;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            AppTheme.EnableDarkTitleBar(this);
        }
    }
}
