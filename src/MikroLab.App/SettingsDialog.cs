using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using MikroLab.Presentation;

namespace MikroLab.App
{
    /// <summary>
    /// Settings dialog of the studio. The options mirror the ones of the
    /// laboratory bench emulator: the address the assembled program is loaded
    /// to, automatic assembly and disassembly of opened files, the tumblers of
    /// the bench and the execution speed.
    /// </summary>
    internal sealed class SettingsDialog : Form
    {
        private readonly MikroLabSettings _settings;

        private readonly ComboBox _speed = new ComboBox();
        private readonly CheckBox _pauseOnHalt = new CheckBox();
        private readonly CheckBox _stepToBranch = new CheckBox();
        private readonly CheckBox _trace = new CheckBox();
        private readonly CheckBox _autoAssemble = new CheckBox();
        private readonly CheckBox _autoDisassemble = new CheckBox();
        private readonly TextBox _loadAddress = new TextBox();
        private readonly CheckBox _tumbler1 = new CheckBox();
        private readonly CheckBox _tumbler2 = new CheckBox();
        private readonly CheckBox _tumbler3 = new CheckBox();
        private readonly ComboBox _fontScale = new ComboBox();
        private readonly CheckBox _showLog = new CheckBox();
        private readonly CheckBox _showDevices = new CheckBox();

        private static readonly string[] SpeedNames =
        {
            "Оригинал (2–5 мс на команду)",
            "Максимум",
            "100 000 команд в секунду",
            "50 000 команд в секунду",
            "10 000 команд в секунду",
            "1 000 команд в секунду",
            "100 команд в секунду",
            "10 команд в секунду"
        };

        private static readonly int[] FontScales = { 90, 100, 110, 125, 150 };

        public SettingsDialog(MikroLabSettings settings)
        {
            _settings = settings ?? new MikroLabSettings();

            Text = "Настройки MikroLab";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = AppTheme.Surface;
            ForeColor = AppTheme.Text;
            Font = AppTheme.UiFont;
            ClientSize = new Size(560, 470);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                Padding = new Padding(16, 14, 16, 10),
                BackColor = AppTheme.Surface,
                AutoSize = true
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            AddGroup(layout, "Выполнение");
            AddRow(layout, "Скорость", _speed);
            AddRow(layout, "Останавливаться при HLT", _pauseOnHalt);
            AddRow(layout, "Шаг до следующего перехода", _stepToBranch);
            AddRow(layout, "Трассировать все команды", _trace);

            AddGroup(layout, "Программа");
            AddRow(layout, "Адрес загрузки программы", _loadAddress);
            AddRow(layout, "Собирать мнемокод при открытии .asm", _autoAssemble);
            AddRow(layout, "Дизассемблировать .hex при открытии", _autoDisassemble);

            AddGroup(layout, "Приставки стенда");
            AddRow(layout, "Тумблер 1", _tumbler1);
            AddRow(layout, "Тумблер 2", _tumbler2);
            AddRow(layout, "Тумблер 3", _tumbler3);

            AddGroup(layout, "Интерфейс");
            AddRow(layout, "Масштаб шрифта редактора", _fontScale);
            AddRow(layout, "Показывать журнал выполнения", _showLog);
            AddRow(layout, "Показывать приставки", _showDevices);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                Height = 46,
                BackColor = AppTheme.Surface
            };

            var apply = MakeButton("Применить", 108);
            apply.Click += (sender, args) => { Apply(); DialogResult = DialogResult.OK; Close(); };

            var cancel = MakeButton("Отмена", 96);
            cancel.Click += (sender, args) => { DialogResult = DialogResult.Cancel; Close(); };

            var reset = MakeButton("По умолчанию", 128);
            reset.Click += (sender, args) => Load(new MikroLabSettings());

            buttons.Controls.Add(apply);
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(reset);

            Controls.Add(layout);
            Controls.Add(buttons);

            Load(_settings);
        }

        private static UiButton MakeButton(string text, int width)
        {
            return new UiButton
            {
                Text = text,
                Kind = UiButtonKind.Secondary,
                Width = width,
                Height = 32,
                Font = AppTheme.SmallFont,
                Margin = new Padding(8, 6, 0, 0)
            };
        }

        private static void AddGroup(TableLayoutPanel layout, string title)
        {
            int row = layout.RowStyles.Count;
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var label = AppTheme.Label(title, AppTheme.Accent, AppTheme.SmallBoldFont);
            label.AutoSize = true;
            label.Margin = new Padding(0, row == 0 ? 0 : 12, 0, 4);
            layout.Controls.Add(label, 0, row);
            layout.SetColumnSpan(label, 2);
        }

        private static void AddRow(TableLayoutPanel layout, string caption, Control control)
        {
            int row = layout.RowStyles.Count;
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var label = AppTheme.Label(caption, AppTheme.TextMuted, AppTheme.SmallFont);
            label.AutoSize = true;
            label.Margin = new Padding(0, 4, 8, 4);
            layout.Controls.Add(label, 0, row);

            control.Margin = new Padding(0, 2, 0, 2);
            layout.Controls.Add(control, 1, row);
        }

        private void Load(MikroLabSettings settings)
        {
            _speed.Items.Clear();
            _speed.Items.AddRange(SpeedNames);
            _speed.SelectedIndex = Math.Max(0, Math.Min(SpeedNames.Length - 1, settings.SpeedIndex));
            _speed.DropDownStyle = ComboBoxStyle.DropDownList;

            _pauseOnHalt.Checked = settings.PauseOnHalt;
            _stepToBranch.Checked = settings.StepToNextBranch;
            _trace.Checked = settings.TraceEveryInstruction;
            _autoAssemble.Checked = settings.AutoAssembleOnOpen;
            _autoDisassemble.Checked = settings.AutoDisassembleOnOpen;
            _loadAddress.Text = settings.LoadAddress ?? "8000h";
            _tumbler1.Checked = settings.Tumbler1;
            _tumbler2.Checked = settings.Tumbler2;
            _tumbler3.Checked = settings.Tumbler3;

            _fontScale.Items.Clear();
            foreach (int scale in FontScales)
                _fontScale.Items.Add(scale + " %");
            _fontScale.SelectedIndex = Math.Max(0, FontScales.IndexOf(
                settings.EditorFontScale <= 0 ? 100 : settings.EditorFontScale));
            _fontScale.DropDownStyle = ComboBoxStyle.DropDownList;

            _showLog.Checked = settings.ShowExecutionLog;
            _showDevices.Checked = settings.ShowDevices;
        }

        /// <summary>Applies the values chosen in the dialog to the settings.</summary>
        public void Apply()
        {
            _settings.SpeedIndex = Math.Max(0, _speed.SelectedIndex);
            _settings.PauseOnHalt = _pauseOnHalt.Checked;
            _settings.StepToNextBranch = _stepToBranch.Checked;
            _settings.TraceEveryInstruction = _trace.Checked;
            _settings.AutoAssembleOnOpen = _autoAssemble.Checked;
            _settings.AutoDisassembleOnOpen = _autoDisassemble.Checked;
            _settings.LoadAddress = _loadAddress.Text.Trim();
            _settings.Tumbler1 = _tumbler1.Checked;
            _settings.Tumbler2 = _tumbler2.Checked;
            _settings.Tumbler3 = _tumbler3.Checked;
            _settings.EditorFontScale = FontScales[Math.Max(0, _fontScale.SelectedIndex)];
            _settings.ShowExecutionLog = _showLog.Checked;
            _settings.ShowDevices = _showDevices.Checked;
            _settings.Save();
        }

        /// <summary>Reads the load address; returns false when the value is malformed.</summary>
        public bool TryGetLoadAddress(out ushort address)
        {
            address = 0;
            string text = (_loadAddress.Text ?? string.Empty).Trim();
            if (text.Length == 0)
                return false;

            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                text = text.Substring(2);

            if (text.EndsWith("h", StringComparison.OrdinalIgnoreCase))
                text = text.Substring(0, text.Length - 1);

            if (text.Length == 0)
                return false;

            return ushort.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out address);
        }
    }
}
