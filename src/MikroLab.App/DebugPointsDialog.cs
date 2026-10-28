using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using MikroLab.Core.Debugging;
using MikroLab.Core.Memory;

namespace MikroLab.App
{
    internal sealed class DebugPointsDialog : Form
    {
        private readonly MachineHost _host;
        private readonly ListBox _breakList;
        private readonly TextBox _breakAddress;
        private readonly ListBox _watchList;
        private readonly TextBox _watchAddress;
        private readonly ComboBox _watchType;

        public DebugPointsDialog(MachineHost host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));

            Text = "Точки останова и наблюдения";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = AppTheme.Surface;
            ForeColor = AppTheme.Text;
            Font = AppTheme.UiFont;
            ClientSize = new Size(520, 560);

            Label title = AppTheme.Label("ОТЛАДКА", AppTheme.TextDim, AppTheme.SmallBoldFont);
            title.Location = new Point(18, 16);
            Controls.Add(title);

            // Breakpoints section.
            Label breakTitle = AppTheme.Label("Точки останова по адресу", AppTheme.Text, AppTheme.SmallBoldFont);
            breakTitle.Location = new Point(18, 46);
            Controls.Add(breakTitle);

            _breakList = new ListBox
            {
                Location = new Point(18, 70),
                Size = new Size(484, 150),
                BackColor = AppTheme.EditorBackground,
                ForeColor = AppTheme.Text,
                BorderStyle = BorderStyle.FixedSingle,
                Font = AppTheme.MonoSmall
            };
            Controls.Add(_breakList);

            _breakAddress = new TextBox
            {
                Location = new Point(18, 228),
                Width = 120,
                BackColor = AppTheme.EditorBackground,
                ForeColor = AppTheme.Text,
                BorderStyle = BorderStyle.FixedSingle,
                Font = AppTheme.MonoSmall
            };
            Controls.Add(_breakAddress);

            var breakAdd = new UiButton
            {
                Text = "Добавить",
                Kind = UiButtonKind.Secondary,
                Width = 90,
                Height = 28,
                Location = new Point(144, 226)
            };
            breakAdd.Click += (sender, args) => AddBreakpoint();
            Controls.Add(breakAdd);

            var breakRemove = new UiButton
            {
                Text = "Удалить",
                Kind = UiButtonKind.Danger,
                Width = 90,
                Height = 28,
                Location = new Point(240, 226)
            };
            breakRemove.Click += (sender, args) => RemoveBreakpoint();
            Controls.Add(breakRemove);

            var breakClear = new UiButton
            {
                Text = "Очистить всё",
                Kind = UiButtonKind.Ghost,
                Width = 110,
                Height = 28,
                Location = new Point(336, 226)
            };
            breakClear.Click += (sender, args) => { _host.ClearAllBreakpoints(); RefreshLists(); };
            Controls.Add(breakClear);

            // Watchpoints section.
            Label watchTitle = AppTheme.Label("Наблюдение памяти (чтение/запись)", AppTheme.Text, AppTheme.SmallBoldFont);
            watchTitle.Location = new Point(18, 270);
            Controls.Add(watchTitle);

            _watchList = new ListBox
            {
                Location = new Point(18, 294),
                Size = new Size(484, 150),
                BackColor = AppTheme.EditorBackground,
                ForeColor = AppTheme.Text,
                BorderStyle = BorderStyle.FixedSingle,
                Font = AppTheme.MonoSmall
            };
            Controls.Add(_watchList);

            _watchAddress = new TextBox
            {
                Location = new Point(18, 452),
                Width = 120,
                BackColor = AppTheme.EditorBackground,
                ForeColor = AppTheme.Text,
                BorderStyle = BorderStyle.FixedSingle,
                Font = AppTheme.MonoSmall
            };
            Controls.Add(_watchAddress);

            _watchType = new ComboBox
            {
                Location = new Point(144, 452),
                Width = 120,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = AppTheme.EditorBackground,
                ForeColor = AppTheme.Text,
                Font = AppTheme.SmallFont
            };
            _watchType.Items.Add("Чтение");
            _watchType.Items.Add("Запись");
            _watchType.Items.Add("Чтение+запись");
            _watchType.SelectedIndex = 1;
            Controls.Add(_watchType);

            var watchAdd = new UiButton
            {
                Text = "Добавить",
                Kind = UiButtonKind.Secondary,
                Width = 90,
                Height = 28,
                Location = new Point(270, 450)
            };
            watchAdd.Click += (sender, args) => AddWatchpoint();
            Controls.Add(watchAdd);

            var watchRemove = new UiButton
            {
                Text = "Удалить",
                Kind = UiButtonKind.Danger,
                Width = 90,
                Height = 28,
                Location = new Point(366, 450)
            };
            watchRemove.Click += (sender, args) => RemoveWatchpoint();
            Controls.Add(watchRemove);

            var close = new UiButton
            {
                Text = "Закрыть",
                Kind = UiButtonKind.Primary,
                Width = 100,
                Height = 30,
                Location = new Point(402, 512)
            };
            close.Click += (sender, args) => Close();
            Controls.Add(close);

            RefreshLists();
        }

        private void AddBreakpoint()
        {
            string text = (_breakAddress.Text ?? string.Empty).Trim().TrimEnd('h', 'H');
            if (!ushort.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort address)) return;
            _host.AddAddressBreakpoint(address);
            _breakAddress.Text = string.Empty;
            RefreshLists();
        }

        private void RemoveBreakpoint()
        {
            if (_breakList.SelectedItem is string text)
            {
                string addr = text.Split(' ')[0].TrimEnd('h', 'H');
                if (ushort.TryParse(addr, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort address))
                {
                    _host.RemoveAddressBreakpoint(address);
                    RefreshLists();
                }
            }
        }

        private void AddWatchpoint()
        {
            string text = (_watchAddress.Text ?? string.Empty).Trim().TrimEnd('h', 'H');
            if (!ushort.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort address)) return;

            MemoryAccess access;
            switch (_watchType.SelectedIndex)
            {
                case 0: access = MemoryAccess.Read; break;
                case 2: access = MemoryAccess.ReadWrite; break;
                default: access = MemoryAccess.Write; break;
            }
            _host.AddWatchpoint(address, access);
            _watchAddress.Text = string.Empty;
            RefreshLists();
        }

        private void RemoveWatchpoint()
        {
            if (_watchList.SelectedItem is MemoryWatchpoint watchpoint)
            {
                _host.RemoveWatchpoint(watchpoint);
                RefreshLists();
            }
        }

        private void RefreshLists()
        {
            _breakList.Items.Clear();
            foreach (ushort address in _host.AddressBreakpoints)
            {
                _breakList.Items.Add(address.ToString("X4") + "h");
            }

            _watchList.Items.Clear();
            foreach (MemoryWatchpoint watchpoint in _host.Watchpoints)
            {
                _watchList.Items.Add(watchpoint);
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            AppTheme.EnableDarkTitleBar(this);
        }
    }
}
