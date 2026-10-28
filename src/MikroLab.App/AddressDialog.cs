using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using MikroLab.Core.Machine;

namespace MikroLab.App
{
    /// <summary>Prompts for a single 16-bit address.</summary>
    internal sealed class AddressDialog : Form
    {
        private readonly TextBox _box;
        private readonly string _title;

        public ushort Value { get; private set; }

        public AddressDialog(string title, string prompt, ushort current)
        {
            _title = title ?? "Адрес";
            Text = _title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = AppTheme.Surface;
            ForeColor = AppTheme.Text;
            Font = AppTheme.UiFont;
            ClientSize = new Size(340, 144);

            Label label = AppTheme.Label(prompt ?? "Адрес (hex, 0000–FFFF):", AppTheme.TextMuted, AppTheme.SmallFont);
            label.Location = new Point(18, 18);

            _box = new TextBox
            {
                Text = current.ToString("X4", CultureInfo.InvariantCulture),
                Location = new Point(18, 42),
                Width = 150,
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
                Location = new Point(144, 96)
            };
            var cancel = new UiButton
            {
                Text = "Отмена",
                Kind = UiButtonKind.Secondary,
                Width = 90,
                Height = 30,
                Location = new Point(240, 96),
                DialogResult = DialogResult.Cancel
            };

            ok.Click += (sender, args) =>
            {
                string text = (_box.Text ?? string.Empty).Trim().TrimEnd('h', 'H');
                if (ushort.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort value))
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
}
