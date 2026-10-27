using System;
using System.Drawing;
using System.Windows.Forms;
using MikroLab.App.Controls;
using MikroLab.Core.Machine;

namespace MikroLab.App
{
    internal sealed partial class MainForm
    {
        private ContextMenuStrip _fileMenu;

        private static readonly string[] SpeedLabels =
        {
            "Оригинал",
            "Максимум",
            "100 000 /с",
            "50 000 /с",
            "10 000 /с",
            "1 000 /с",
            "100 /с",
            "10 /с"
        };

        private static readonly int[] SpeedValues =
        {
            0, 0, 100000, 50000, 10000, 1000, 100, 10
        };

        private void OnSpeedSelected(int index)
        {
            if (index < 0 || index >= SpeedValues.Length) return;

            if (index == 0)
            {
                _host.Session.ThrottleLikeOriginal = true;
                _host.Session.TargetInstructionsPerSecond = 0;
            }
            else if (index == 1)
            {
                _host.Session.ThrottleLikeOriginal = false;
                _host.Session.TargetInstructionsPerSecond = 0;
            }
            else
            {
                _host.Session.ThrottleLikeOriginal = false;
                _host.Session.TargetInstructionsPerSecond = SpeedValues[index];
            }

            _studio.Output.Log("Скорость выполнения · " + SpeedLabels[index]);
        }

        // ------------------------------------------------------------------ file menu

        private void ShowFileMenu()
        {
            if (_fileMenu == null)
            {
                _fileMenu = new ContextMenuStrip
                {
                    BackColor = AppTheme.SurfaceRaised,
                    ForeColor = AppTheme.Text,
                    Font = AppTheme.UiFont,
                    ShowImageMargin = false,
                    ShowCheckMargin = false,
                    Padding = new Padding(4),
                    MinimumSize = new Size(240, 0),
                    Renderer = new DarkMenuRenderer()
                };

                _fileMenu.Items.Add(MenuItem("Новый проект…", "Ctrl+N", () => _studio.HostNew()));
                _fileMenu.Items.Add(MenuItem("Открыть программу…", "Ctrl+O", () => _studio.HostOpen()));
                _fileMenu.Items.Add(MenuItem("Сохранить программу…", "Ctrl+S", () => _studio.HostSave()));
                _fileMenu.Items.Add(new ToolStripSeparator());
                _fileMenu.Items.Add(MenuItem("Импорт Intel HEX…", "Ctrl+I", ImportHex));
                _fileMenu.Items.Add(MenuItem("Экспорт Intel HEX…", "Ctrl+E", ExportHex));
                _fileMenu.Items.Add(new ToolStripSeparator());
                _fileMenu.Items.Add(MenuItem("Сохранить состояние…", "Ctrl+Shift+S", SaveState));
                _fileMenu.Items.Add(MenuItem("Загрузить состояние…", "Ctrl+Shift+O", LoadState));
                _fileMenu.Items.Add(new ToolStripSeparator());
                _fileMenu.Items.Add(MenuItem("Справка", "F1", ShowHelp));
            }

            _fileMenu.Show(_fileButton, new Point(0, _fileButton.Height + 2));
        }

        private static ToolStripMenuItem MenuItem(string text, string shortcut, Action action)
        {
            var item = new ToolStripMenuItem(text)
            {
                ShortcutKeyDisplayString = shortcut,
                ForeColor = AppTheme.TextMuted,
                Font = AppTheme.UiFont,
                Padding = new Padding(8, 5, 8, 5),
                BackColor = Color.Transparent
            };
            item.Click += (sender, args) => action();
            return item;
        }

        // ------------------------------------------------------------------ Intel HEX

        private void ImportHex()
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "Импорт Intel HEX";
                dialog.Filter = "Intel HEX (*.hex;*.ihx)|*.hex;*.ihx|Все файлы (*.*)|*.*";
                dialog.CheckFileExists = true;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    _host.ImportHex(dialog.FileName);
                    PumpUi();
                    _studio.UpdateFromSnapshot(_host.Snapshot());
                }
                catch (Exception ex)
                {
                    ShowFileError("Не удалось импортировать Intel HEX", ex);
                }
            }
        }

        private void ExportHex()
        {
            using (var dialog = new SaveFileDialog())
            {
                dialog.Title = "Экспорт Intel HEX";
                dialog.Filter = "Intel HEX (*.hex;*.ihx)|*.hex;*.ihx|Все файлы (*.*)|*.*";
                dialog.FileName = "program.hex";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    _host.ExportHex(dialog.FileName);
                }
                catch (Exception ex)
                {
                    ShowFileError("Не удалось экспортировать Intel HEX", ex);
                }
            }
        }

        // ------------------------------------------------------------------ machine state

        private void SaveState()
        {
            using (var dialog = new SaveFileDialog())
            {
                dialog.Title = "Сохранить состояние машины";
                dialog.Filter = "Состояние MikroLab (*.json)|*.json|Все файлы (*.*)|*.*";
                dialog.FileName = "mikrolab-state.json";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    _host.SaveState(dialog.FileName);
                }
                catch (Exception ex)
                {
                    ShowFileError("Не удалось сохранить состояние", ex);
                }
            }
        }

        private void LoadState()
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "Загрузить состояние машины";
                dialog.Filter = "Состояние MikroLab (*.json)|*.json|Все файлы (*.*)|*.*";
                dialog.CheckFileExists = true;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    _host.LoadState(dialog.FileName);
                    PumpUi();
                    _studio.UpdateFromSnapshot(_host.Snapshot());
                }
                catch (Exception ex)
                {
                    ShowFileError("Не удалось загрузить состояние", ex);
                }
            }
        }

        private void ShowFileError(string title, Exception exception)
        {
            _studio.Output.Log(title + ": " + exception.Message, LogLevel.Error);
            MessageBox.Show(this, exception.Message, title,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
