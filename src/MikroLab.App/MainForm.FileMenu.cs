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
        private ToolStripMenuItem _profileMenuItem;

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
                _fileMenu.Items.Add(MenuItem("Импорт BIN…", "Ctrl+Alt+I", ImportBinary));
                _fileMenu.Items.Add(MenuItem("Экспорт BIN…", "Ctrl+Alt+E", ExportBinary));
                _fileMenu.Items.Add(MenuItem("Импорт .580…", "Ctrl+Shift+I", Import580));
                _fileMenu.Items.Add(MenuItem("Экспорт .580…", "Ctrl+Shift+E", Export580));
                _fileMenu.Items.Add(new ToolStripSeparator());
                _profileMenuItem = MenuItem("Профиль памяти…", "Ctrl+Shift+P", EditProfile);
                _fileMenu.Items.Add(_profileMenuItem);
                _fileMenu.Items.Add(new ToolStripSeparator());
                _fileMenu.Items.Add(MenuItem("Сохранить состояние…", "Ctrl+Shift+S", SaveState));
                _fileMenu.Items.Add(MenuItem("Загрузить состояние…", "Ctrl+Shift+O", LoadState));
                _fileMenu.Items.Add(new ToolStripSeparator());
                _fileMenu.Items.Add(MenuItem("Справка", "F1", ShowHelp));
            }

            _profileMenuItem.Text = "Профиль · " + _host.Profile.Name + "…";
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
            string path = _files.OpenFile(new FileDialogRequest
            {
                Title = "Импорт Intel HEX",
                Filter = "Intel HEX (*.hex;*.ihx)|*.hex;*.ihx|Все файлы (*.*)|*.*",
                CheckFileExists = true
            });
            if (path == null) return;

            try
            {
                _host.ImportHex(path);
                _studio.HostExternalProgramLoaded();
                PumpUi();
            }
            catch (Exception ex)
            {
                ShowFileError("Не удалось импортировать Intel HEX", ex);
            }
        }

        private void ExportHex()
        {
            string path = _files.SaveFile(new FileDialogRequest
            {
                Title = "Экспорт Intel HEX",
                Filter = "Intel HEX (*.hex;*.ihx)|*.hex;*.ihx|Все файлы (*.*)|*.*",
                DefaultFileName = "program.hex"
            });
            if (path == null) return;

            try
            {
                _host.ExportHex(path);
            }
            catch (Exception ex)
            {
                ShowFileError("Не удалось экспортировать Intel HEX", ex);
            }
        }

        // ------------------------------------------------------------------ raw BIN images

        private void ImportBinary()
        {
            string path = _files.OpenFile(new FileDialogRequest
            {
                Title = "Импорт бинарного образа",
                Filter = "Бинарный образ (*.bin)|*.bin|Все файлы (*.*)|*.*",
                CheckFileExists = true
            });
            if (path == null) return;

            try
            {
                _host.ImportBinary(path);
                _studio.HostExternalProgramLoaded();
                PumpUi();
            }
            catch (Exception ex)
            {
                ShowFileError("Не удалось импортировать BIN", ex);
            }
        }

        private void ExportBinary()
        {
            string path = _files.SaveFile(new FileDialogRequest
            {
                Title = "Экспорт бинарного образа",
                Filter = "Бинарный образ (*.bin)|*.bin|Все файлы (*.*)|*.*",
                DefaultFileName = "program.bin"
            });
            if (path == null) return;

            try
            {
                _host.ExportBinary(path);
            }
            catch (Exception ex)
            {
                ShowFileError("Не удалось экспортировать BIN", ex);
            }
        }

        // ------------------------------------------------------------------ .580 images

        private void Import580()
        {
            string path = _files.OpenFile(new FileDialogRequest
            {
                Title = "Импорт снимка КР580 (.580)",
                Filter = "Снимок КР580 (*.580)|*.580|Все файлы (*.*)|*.*",
                CheckFileExists = true
            });
            if (path == null) return;

            try
            {
                _host.Import580(path);
                _studio.HostExternalProgramLoaded();
                PumpUi();
            }
            catch (Exception ex)
            {
                ShowFileError("Не удалось импортировать .580", ex);
            }
        }

        private void Export580()
        {
            string path = _files.SaveFile(new FileDialogRequest
            {
                Title = "Экспорт снимка КР580 (.580)",
                Filter = "Снимок КР580 (*.580)|*.580|Все файлы (*.*)|*.*",
                DefaultFileName = "program.580"
            });
            if (path == null) return;

            try
            {
                _host.Export580(path);
            }
            catch (Exception ex)
            {
                ShowFileError("Не удалось экспортировать .580", ex);
            }
        }

        // ------------------------------------------------------------------ profile

        private void EditProfile()
        {
            using (var dialog = new ProfileDialog(_host.Profile))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result == null) return;

                _studio.HostApplyProfile(dialog.Result);
                PumpUi();
            }
        }

        // ------------------------------------------------------------------ machine state

        private void SaveState()
        {
            string path = _files.SaveFile(new FileDialogRequest
            {
                Title = "Сохранить состояние машины",
                Filter = "Состояние MikroLab (*.json)|*.json|Все файлы (*.*)|*.*",
                DefaultFileName = "mikrolab-state.json"
            });
            if (path == null) return;

            try
            {
                _host.SaveState(path);
            }
            catch (Exception ex)
            {
                ShowFileError("Не удалось сохранить состояние", ex);
            }
        }

        private void LoadState()
        {
            string path = _files.OpenFile(new FileDialogRequest
            {
                Title = "Загрузить состояние машины",
                Filter = "Состояние MikroLab (*.json)|*.json|Все файлы (*.*)|*.*",
                CheckFileExists = true
            });
            if (path == null) return;

            try
            {
                _host.LoadState(path);
                _studio.HostExternalProgramLoaded();
                PumpUi();
            }
            catch (Exception ex)
            {
                ShowFileError("Не удалось загрузить состояние", ex);
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
