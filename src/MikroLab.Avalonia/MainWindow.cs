using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MikroLab.Core.Machine;
using MikroLab.Presentation;
using MikroLab.Presentation.Services;
using MikroLab.Studio.Controls;
using MikroLab.Studio.Dialogs;
using MikroLab.Studio.Services;
using MikroLab.Studio.Workspaces;
using ToggleSwitch = MikroLab.Studio.Controls.ToggleSwitch;

namespace MikroLab.Studio
{
    /// <summary>
    /// Product shell: one window, two workspaces. Studio gives the full
    /// editor/debugger, Laboratory gives the guided lab bench. Both share the
    /// same <see cref="MachineHost"/>, so a program assembled in Studio keeps
    /// running when the user switches to the bench.
    /// </summary>
    internal sealed class MainWindow : Window
    {
        private enum Workspace
        {
            Studio,
            Laboratory
        }

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

        private readonly MachineHost _host;
        private readonly IAvaloniaFileService _files;
        private readonly IScreenshotService _screenshots;
        private readonly StudioWorkspace _studio;
        private readonly LaboratoryWorkspace _laboratory;
        private readonly Grid _content;
        private readonly UiButton _studioTab;
        private readonly UiButton _labTab;
        private readonly UiButton _fileButton;
        private readonly ThemedDropDown _speedBox;
        private readonly DispatcherTimer _uiTimer;

        private readonly TextBlock _stateLabel;
        private readonly TextBlock _cyclesLabel;
        private readonly TextBlock _pcLabel;
        private readonly TextBlock _breakpointLabel;
        private readonly TextBlock _reasonLabel;
        private readonly TextBlock _workspaceLabel;

        private Workspace _workspace = Workspace.Studio;
        private MachineSnapshot _snapshot;

        private Popup _filePopup;

        // Screenshot harness state.
        private DispatcherTimer _captureTimer;
        private Timer _captureWatchdog;
        private string _captureDirectory;
        private int _captureStep;
        private int _captureSample;
        private string _captureWorkspace;

        public MainWindow()
        {
            Title = "MikroLab — КР580ВМ80А";
            Width = 1420;
            Height = 880;
            MinWidth = 1120;
            MinHeight = 720;
            Background = AppTheme.Brush(AppTheme.Window);
            Foreground = AppTheme.Brush(AppTheme.Text);
            FontFamily = AppTheme.UiFamily;
            FontSize = AppTheme.UiSize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            _host = new MachineHost();
            _files = new AvaloniaFileService(() => this);
            _screenshots = new AvaloniaScreenshotService(() => this);

            var root = new Grid
            {
                RowDefinitions = RowDefinitions.Parse("64,*,30"),
                Background = AppTheme.Brush(AppTheme.Window)
            };

            // ------------------------------------------------------------------ header

            var header = new Border
            {
                Background = AppTheme.Brush(AppTheme.Surface),
                BorderBrush = AppTheme.Brush(AppTheme.Border),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };

            var headerLayout = new Grid
            {
                ColumnDefinitions = ColumnDefinitions.Parse("Auto,Auto,*,Auto,Auto,Auto"),
                Margin = new Thickness(18, 0)
            };

            var brandPanel = new Grid
            {
                ColumnDefinitions = ColumnDefinitions.Parse("Auto,Auto"),
                VerticalAlignment = VerticalAlignment.Center
            };
            brandPanel.Children.Add(new BrandMark
            {
                Width = 42,
                Height = 42,
                VerticalAlignment = VerticalAlignment.Center
            });

            var brandText = new StackPanel
            {
                Spacing = 0,
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            brandText.Children.Add(AppTheme.Label("MikroLab", AppTheme.Text, AppTheme.BrandSize, true));
            brandText.Children.Add(AppTheme.Label(
                "КР580ВМ80А · Intel 8080 · учебный эмулятор",
                AppTheme.TextMuted, AppTheme.SmallSize));
            brandPanel.Children.Add(brandText);
            Grid.SetColumn(brandText, 1);
            headerLayout.Children.Add(brandPanel);

            _fileButton = new UiButton
            {
                Text = "Файл",
                Kind = UiButtonKind.Ghost,
                Width = 82,
                Height = 32,
                FontSize = AppTheme.SmallSize,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(26, 0, 0, 0)
            };
            _fileButton.Click += ShowFileMenu;
            ToolTip.SetTip(_fileButton, "Intel HEX, состояние машины и файлы программ");
            headerLayout.Children.Add(_fileButton);
            Grid.SetColumn(_fileButton, 1);

            var switchBar = new Border
            {
                Background = AppTheme.Brush(AppTheme.SurfaceRaised),
                BorderBrush = AppTheme.Brush(AppTheme.BorderSoft),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(4),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var switchLayout = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 0
            };
            _studioTab = new UiButton
            {
                Text = "Студия",
                Kind = UiButtonKind.Ghost,
                Width = 128,
                Height = 32,
                FontSize = AppTheme.SmallSize,
                CornerRadius = 7
            };
            _labTab = new UiButton
            {
                Text = "Лаборатория",
                Kind = UiButtonKind.Ghost,
                Width = 128,
                Height = 32,
                FontSize = AppTheme.SmallSize,
                CornerRadius = 7
            };
            _studioTab.Click += () => SwitchTo(Workspace.Studio);
            _labTab.Click += () => SwitchTo(Workspace.Laboratory);
            switchLayout.Children.Add(_studioTab);
            switchLayout.Children.Add(_labTab);
            switchBar.Child = switchLayout;
            headerLayout.Children.Add(switchBar);
            Grid.SetColumn(switchBar, 2);

            var speedLabel = AppTheme.Label("Скорость", AppTheme.TextDim, AppTheme.SmallSize);
            speedLabel.VerticalAlignment = VerticalAlignment.Center;
            speedLabel.Margin = new Thickness(0, 0, 8, 0);
            headerLayout.Children.Add(speedLabel);
            Grid.SetColumn(speedLabel, 3);

            _speedBox = new ThemedDropDown
            {
                Width = 148,
                Height = 30,
                Placeholder = "Оригинал",
                VerticalAlignment = VerticalAlignment.Center
            };
            _speedBox.SetItems(SpeedLabels);
            _speedBox.SelectIndex(0);
            _speedBox.SelectionChanged += OnSpeedSelected;
            _speedBox.SetToolTip("Скорость выполнения: от оригинала до максимума");
            headerLayout.Children.Add(_speedBox);
            Grid.SetColumn(_speedBox, 4);

            var about = new UiButton
            {
                Text = "Справка · F1",
                Kind = UiButtonKind.Ghost,
                Width = 104,
                Height = 32,
                FontSize = AppTheme.SmallSize,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0)
            };
            about.Click += ShowHelp;
            ToolTip.SetTip(about, "Горячие клавиши, возможности и лицензия");
            headerLayout.Children.Add(about);
            Grid.SetColumn(about, 5);

            header.Child = headerLayout;
            root.Children.Add(header);

            // ------------------------------------------------------------------ workspaces

            _content = new Grid
            {
                Background = AppTheme.Brush(AppTheme.Window)
            };

            _studio = new StudioWorkspace(_host, _files);
            _laboratory = new LaboratoryWorkspace(_host);
            _studio.DocumentTitleChanged += UpdateTitle;
            _laboratory.OpenInStudioRequested += OpenLabInStudio;

            _content.Children.Add(_studio);
            _content.Children.Add(_laboratory);
            root.Children.Add(_content);
            Grid.SetRow(_content, 1);

            // ------------------------------------------------------------------ status bar

            var status = new Border
            {
                Background = AppTheme.Brush(AppTheme.Surface),
                BorderBrush = AppTheme.Brush(AppTheme.BorderSoft),
                BorderThickness = new Thickness(0, 1, 0, 0)
            };

            var statusLayout = new Grid
            {
                ColumnDefinitions = ColumnDefinitions.Parse("Auto,1,Auto,Auto,Auto,*,Auto"),
                Margin = new Thickness(14, 0)
            };

            _stateLabel = MakeStatusLabel("● ОСТАНОВЛЕНО", AppTheme.TextMuted);
            _cyclesLabel = MakeStatusLabel("тактов: 0", AppTheme.TextMuted);
            _pcLabel = MakeStatusLabel("PC 8000h", AppTheme.Cyan);
            _breakpointLabel = MakeStatusLabel("точек останова: 0", AppTheme.TextMuted);
            _reasonLabel = MakeStatusLabel(string.Empty, AppTheme.TextMuted);
            _reasonLabel.HorizontalAlignment = HorizontalAlignment.Left;
            _workspaceLabel = MakeStatusLabel("Студия", AppTheme.TextDim);
            _workspaceLabel.HorizontalAlignment = HorizontalAlignment.Right;

            statusLayout.Children.Add(_stateLabel);

            var separator = new Border
            {
                Width = 1,
                Background = AppTheme.Brush(AppTheme.Border),
                Margin = new Thickness(12, 9)
            };
            statusLayout.Children.Add(separator);
            Grid.SetColumn(separator, 1);

            statusLayout.Children.Add(_pcLabel);
            Grid.SetColumn(_pcLabel, 2);
            statusLayout.Children.Add(_cyclesLabel);
            Grid.SetColumn(_cyclesLabel, 3);
            statusLayout.Children.Add(_breakpointLabel);
            Grid.SetColumn(_breakpointLabel, 4);
            statusLayout.Children.Add(_reasonLabel);
            Grid.SetColumn(_reasonLabel, 5);
            statusLayout.Children.Add(_workspaceLabel);
            Grid.SetColumn(_workspaceLabel, 6);

            status.Child = statusLayout;
            root.Children.Add(status);
            Grid.SetRow(status, 2);

            Content = root;

            AddHandler(KeyDownEvent, OnGlobalKeyDown, RoutingStrategies.Tunnel);

            _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _uiTimer.Tick += (sender, args) => PumpUi();
            _uiTimer.Start();

            _host.Session.PublishSnapshot();
            SwitchTo(Workspace.Studio);
            UpdateTitle();
            PumpUi();
        }

        public StudioWorkspace Studio => _studio;
        public LaboratoryWorkspace Laboratory => _laboratory;

        // ================================================================== shell

        private static TextBlock MakeStatusLabel(string text, Color color)
        {
            TextBlock label = AppTheme.Label(text, color, AppTheme.SmallSize);
            label.VerticalAlignment = VerticalAlignment.Center;
            label.Margin = new Thickness(0, 0, 14, 0);
            return label;
        }

        private void SwitchTo(Workspace workspace)
        {
            _workspace = workspace;
            bool studio = workspace == Workspace.Studio;

            _studio.IsVisible = studio;
            _laboratory.IsVisible = !studio;

            _studioTab.Active = studio;
            _labTab.Active = !studio;
            _workspaceLabel.Text = studio ? "Студия" : "Лаборатория";

            PumpUi();
            if (studio) _studio.Editor.FocusEditor();
        }

        private void OpenLabInStudio(string sampleName)
        {
            SampleLibraryLoad(sampleName);
            SwitchTo(Workspace.Studio);
            _studio.Output.Log("Открыта лабораторная программа · " + sampleName);
        }

        private void SampleLibraryLoad(string sampleName)
        {
            for (int i = 0; i < SampleLibrary.All.Length; i++)
            {
                if (string.Equals(SampleLibrary.All[i].Name, sampleName, StringComparison.OrdinalIgnoreCase))
                {
                    _studio.HostLoadSample(i);
                    return;
                }
            }
            _studio.HostLoadSample(0);
        }

        private void PumpUi()
        {
            MachineSnapshot snapshot = _host.Snapshot();
            if (snapshot != null) _snapshot = snapshot;
            if (_snapshot == null) return;

            if (_workspace == Workspace.Studio) _studio.UpdateFromSnapshot(_snapshot);
            else _laboratory.UpdateFromSnapshot(_snapshot);

            UpdateStatus(_snapshot);
        }

        private void UpdateStatus(MachineSnapshot snapshot)
        {
            RunState state = _host.State;
            string label;
            Color color;
            switch (state)
            {
                case RunState.Running:
                    label = "● ВЫПОЛНЯЕТСЯ";
                    color = AppTheme.Green;
                    break;
                case RunState.Paused:
                    label = "● ПАУЗА";
                    color = AppTheme.Amber;
                    break;
                case RunState.Halted:
                    label = "● HLT";
                    color = AppTheme.Red;
                    break;
                default:
                    label = "● ОСТАНОВЛЕНО";
                    color = AppTheme.TextMuted;
                    break;
            }

            if (_stateLabel.Text != label) _stateLabel.Text = label;
            _stateLabel.Foreground = AppTheme.Brush(color);

            _pcLabel.Text = "PC " + snapshot.PC.ToString("X4", CultureInfo.InvariantCulture) + "h";
            _cyclesLabel.Text = "тактов: " + snapshot.Cycles.ToString("N0", CultureInfo.InvariantCulture);
            _breakpointLabel.Text = "точек останова: " + _host.ActiveBreakpointCount;

            string reason = state == RunState.Paused || state == RunState.Halted
                ? _host.StopReason
                : string.Empty;
            if (_reasonLabel.Text != reason) _reasonLabel.Text = reason;
            _reasonLabel.Foreground = AppTheme.Brush(state == RunState.Halted ? AppTheme.Red : AppTheme.TextMuted);
        }

        private void UpdateTitle()
        {
            Title = "MikroLab — " + _studio.DocumentName;
        }

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

        // ================================================================== shortcuts

        private void OnGlobalKeyDown(object sender, KeyEventArgs e)
        {
            if (_captureTimer != null) return;

            KeyModifiers modifiers = e.KeyModifiers;
            bool control = (modifiers & KeyModifiers.Control) != 0;
            if (OperatingSystem.IsMacOS())
            {
                // macOS users expect Command where Windows uses Control.
                control |= (modifiers & KeyModifiers.Meta) != 0;
            }
            bool shift = (modifiers & KeyModifiers.Shift) != 0;
            bool alt = (modifiers & KeyModifiers.Alt) != 0;

            if (!control && !alt)
            {
                switch (e.Key)
                {
                    case Key.F1:
                        ShowHelp();
                        e.Handled = true;
                        return;
                    case Key.F5:
                        if (_workspace == Workspace.Studio) _studio.HostRun();
                        else _host.Run();
                        PumpUi();
                        e.Handled = true;
                        return;
                    case Key.F6:
                        _host.Pause();
                        PumpUi();
                        e.Handled = true;
                        return;
                    case Key.F7:
                        if (_workspace == Workspace.Studio) _studio.HostAssemble();
                        PumpUi();
                        e.Handled = true;
                        return;
                    case Key.F8:
                        if (_workspace == Workspace.Studio) _studio.HostStep();
                        else _host.Step();
                        PumpUi();
                        e.Handled = true;
                        return;
                    case Key.F9:
                        if (_workspace == Workspace.Studio)
                        {
                            _studio.HostToggleBreakpoint(_studio.Editor.CaretLine);
                            PumpUi();
                        }
                        e.Handled = true;
                        return;
                }
            }

            if (!control)
            {
                if (e.Key == Key.F10 && alt)
                {
                    StepBack();
                    e.Handled = true;
                }
                return;
            }

            if (e.Key == Key.R && !shift && !alt)
            {
                if (_workspace == Workspace.Studio) _studio.HostReset();
                else _host.Reset();
                PumpUi();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.N && !shift && !alt)
            {
                if (_workspace == Workspace.Studio) _ = _studio.HostNewAsync();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.O && !shift && !alt)
            {
                if (_workspace == Workspace.Studio) _ = _studio.HostOpenAsync();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.S && !shift && !alt)
            {
                if (_workspace == Workspace.Studio) _ = _studio.HostSaveAsync();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.I && !shift && !alt)
            {
                _ = ImportHexAsync();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.E && !shift && !alt)
            {
                _ = ExportHexAsync();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.I && shift && !alt)
            {
                _ = Import580Async();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.E && shift && !alt)
            {
                _ = Export580Async();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.I && alt)
            {
                _ = ImportBinaryAsync();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.E && alt)
            {
                _ = ExportBinaryAsync();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.P && shift && !alt)
            {
                _ = EditProfileAsync();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.F5 && !shift)
            {
                _ = RunToAddressAsync();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.B && !shift && !alt)
            {
                _ = ShowDebugPointsAsync();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.S && shift && !alt)
            {
                _ = SaveStateAsync();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.O && shift && !alt)
            {
                _ = LoadStateAsync();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.D1)
            {
                SwitchTo(Workspace.Studio);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.D2)
            {
                SwitchTo(Workspace.Laboratory);
                e.Handled = true;
            }
        }

        private async Task RunToAddressAsync()
        {
            ushort? address = await AddressDialog.ShowAsync(
                this, "Выполнить до адреса", "Адрес останова (hex):", _host.CurrentPc());
            if (!address.HasValue) return;

            if (_workspace == Workspace.Studio) _studio.HostRunTo(address.Value);
            else _host.RunTo(address.Value);
            PumpUi();
        }

        private async Task ShowDebugPointsAsync()
        {
            if (_workspace != Workspace.Studio) SwitchTo(Workspace.Studio);
            await _studio.HostShowDebugPointsAsync();
            PumpUi();
        }

        private void StepBack()
        {
            if (_workspace == Workspace.Studio) _studio.HostStepBack();
            else _host.StepBack();
            PumpUi();
        }

        private async void ShowHelp()
        {
            await HelpDialog.ShowAsync(this);
        }

        // ================================================================== file menu

        private void ShowFileMenu()
        {
            var stack = new StackPanel { Spacing = 1 };
            stack.Children.Add(MenuItem("Новый проект…", "Ctrl+N", () => _ = _studio.HostNewAsync()));
            stack.Children.Add(MenuItem("Открыть программу…", "Ctrl+O", () => _ = _studio.HostOpenAsync()));
            stack.Children.Add(MenuItem("Сохранить программу…", "Ctrl+S", () => _ = _studio.HostSaveAsync()));
            stack.Children.Add(MenuSeparator());
            stack.Children.Add(MenuItem("Импорт Intel HEX…", "Ctrl+I", () => _ = ImportHexAsync()));
            stack.Children.Add(MenuItem("Экспорт Intel HEX…", "Ctrl+E", () => _ = ExportHexAsync()));
            stack.Children.Add(MenuItem("Импорт BIN…", "Ctrl+Alt+I", () => _ = ImportBinaryAsync()));
            stack.Children.Add(MenuItem("Экспорт BIN…", "Ctrl+Alt+E", () => _ = ExportBinaryAsync()));
            stack.Children.Add(MenuItem("Импорт .580…", "Ctrl+Shift+I", () => _ = Import580Async()));
            stack.Children.Add(MenuItem("Экспорт .580…", "Ctrl+Shift+E", () => _ = Export580Async()));
            stack.Children.Add(MenuSeparator());
            stack.Children.Add(MenuItem("Профиль · " + _host.Profile.Name + "…", "Ctrl+Shift+P", () => _ = EditProfileAsync()));
            stack.Children.Add(MenuSeparator());
            stack.Children.Add(MenuItem("Сохранить состояние…", "Ctrl+Shift+S", () => _ = SaveStateAsync()));
            stack.Children.Add(MenuItem("Загрузить состояние…", "Ctrl+Shift+O", () => _ = LoadStateAsync()));
            stack.Children.Add(MenuSeparator());
            stack.Children.Add(MenuItem("Справка", "F1", ShowHelp));

            var border = new Border
            {
                Background = AppTheme.Brush(AppTheme.SurfaceRaised),
                BorderBrush = AppTheme.Brush(AppTheme.Border),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(5),
                MinWidth = 270,
                Child = stack
            };

            if (_filePopup == null)
            {
                _filePopup = new Popup
                {
                    PlacementTarget = _fileButton,
                    Placement = PlacementMode.Bottom,
                    IsLightDismissEnabled = true,
                    Child = border
                };
            }
            else
            {
                _filePopup.Child = border;
            }

            _filePopup.IsOpen = true;
        }

        private Control MenuItem(string text, string shortcut, Action action)
        {
            var row = new Grid
            {
                ColumnDefinitions = ColumnDefinitions.Parse("*,Auto")
            };

            var label = AppTheme.Label(text, AppTheme.TextMuted, AppTheme.UiSize);
            row.Children.Add(label);

            var hint = AppTheme.Label(shortcut, AppTheme.TextDim, AppTheme.SmallSize);
            hint.HorizontalAlignment = HorizontalAlignment.Right;
            row.Children.Add(hint);
            Grid.SetColumn(hint, 1);

            var border = new Border
            {
                Background = AppTheme.Brush(AppTheme.SurfaceRaised),
                Padding = new Thickness(10, 7),
                CornerRadius = new CornerRadius(6),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = row
            };

            border.PointerEntered += (sender, args) => border.Background = AppTheme.Brush(AppTheme.SurfaceHover);
            border.PointerExited += (sender, args) => border.Background = AppTheme.Brush(AppTheme.SurfaceRaised);
            border.PointerPressed += (sender, args) =>
            {
                if (!args.GetCurrentPoint(border).Properties.IsLeftButtonPressed) return;
                _filePopup.IsOpen = false;
                action();
                args.Handled = true;
            };
            return border;
        }

        private static Control MenuSeparator()
        {
            return new Border
            {
                Height = 1,
                Background = AppTheme.Brush(AppTheme.BorderSoft),
                Margin = new Thickness(6, 4)
            };
        }

        // ================================================================== Intel HEX

        private async Task ImportHexAsync()
        {
            string path = await _files.OpenFileAsync(new FileDialogRequest
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

        private async Task ExportHexAsync()
        {
            string path = await _files.SaveFileAsync(new FileDialogRequest
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

        // ================================================================== raw BIN

        private async Task ImportBinaryAsync()
        {
            string path = await _files.OpenFileAsync(new FileDialogRequest
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

        private async Task ExportBinaryAsync()
        {
            string path = await _files.SaveFileAsync(new FileDialogRequest
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

        // ================================================================== .580

        private async Task Import580Async()
        {
            string path = await _files.OpenFileAsync(new FileDialogRequest
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

        private async Task Export580Async()
        {
            string path = await _files.SaveFileAsync(new FileDialogRequest
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

        // ================================================================== profile / state

        private async Task EditProfileAsync()
        {
            MachineProfile profile = await ProfileDialog.ShowAsync(this, _host.Profile);
            if (profile == null) return;

            _studio.HostApplyProfile(profile);
            PumpUi();
        }

        private async Task SaveStateAsync()
        {
            string path = await _files.SaveFileAsync(new FileDialogRequest
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

        private async Task LoadStateAsync()
        {
            string path = await _files.OpenFileAsync(new FileDialogRequest
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

        private async void ShowFileError(string title, Exception exception)
        {
            _studio.Output.Log(title + ": " + exception.Message, LogLevel.Error);
            await ConfirmDialog.ShowAsync(this, exception.Message, title);
        }

        // ================================================================== screenshot harness

        /// <summary>
        /// Drives the app through a fixed set of scenes and writes PNG files.
        /// Used by docs/screens generation and by the visual regression check.
        /// A watchdog guarantees the process can never hang in CI.
        /// </summary>
        public void BeginCapture(string directory, int sample, string workspace)
        {
            _captureDirectory = directory;
            _captureSample = sample;
            _captureWorkspace = workspace ?? "studio";
            Directory.CreateDirectory(_captureDirectory);

            _captureWatchdog = new Timer(
                state => Environment.Exit(3),
                null,
                TimeSpan.FromSeconds(45),
                Timeout.InfiniteTimeSpan);

            _captureTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(260) };
            _captureTimer.Tick += (sender, args) => CaptureStep();
            _captureTimer.Start();
        }

        private void CaptureStep()
        {
            if (_captureTimer != null) _captureTimer.Stop();

            try
            {
                switch (_captureStep)
                {
                    case 0:
                        Width = 1440;
                        Height = 900;
                        Activate();
                        if (_captureWorkspace == "lab" || _captureWorkspace == "laboratory")
                        {
                            SwitchTo(Workspace.Laboratory);
                        }
                        else
                        {
                            SwitchTo(Workspace.Studio);
                            _studio.HostLoadSample(_captureSample);
                        }
                        break;

                    case 1:
                        CaptureScene("01-studio-editor.png");
                        break;

                    case 2:
                        _studio.HostSelectInspector(1);
                        break;

                    case 3:
                        CaptureScene("02-studio-memory.png");
                        break;

                    case 4:
                        _studio.HostSelectInspector(2);
                        break;

                    case 5:
                        CaptureScene("03-studio-devices.png");
                        break;

                    case 6:
                        _studio.HostSelectInspector(0);
                        _studio.HostLoadSample(4);
                        _studio.HostRun();
                        break;

                    case 7:
                        CaptureScene("04-studio-display.png");
                        break;

                    case 8:
                        _studio.HostLoadSample(1);
                        _studio.HostRun();
                        SwitchTo(Workspace.Laboratory);
                        _laboratory.SelectLab(1);
                        break;

                    case 9:
                        CaptureScene("05-laboratory.png");
                        break;

                    case 10:
                        _studio.HostLoadSample(2);
                        _host.WithMachine(machine =>
                        {
                            machine.Ppi.Tumbler1 = true;
                            machine.Ppi.Tumbler3 = true;
                        });
                        _studio.HostRun();
                        _laboratory.SelectLab(4);
                        break;

                    case 11:
                        CaptureScene("06-laboratory-switches.png");
                        break;

                    case 12:
                        _studio.HostLoadSample(9);
                        _laboratory.SelectLab(5);
                        _host.WithMachine(machine =>
                        {
                            machine.Analog.SetChannel(2, 6.4);
                            machine.Analog.SelectedMuxChannel = 2;
                        });
                        _studio.HostRun();
                        break;

                    case 13:
                        break;

                    case 14:
                        CaptureScene("07-laboratory-analog.png");
                        break;

                    case 15:
                        _host.Pause();
                        _captureTimer.Stop();
                        _captureWatchdog.Dispose();
                        _uiTimer.Stop();
                        _host.Dispose();
                        Close();
                        Environment.Exit(0);
                        break;
                }
            }
            catch (Exception exception)
            {
                try
                {
                    File.WriteAllText(
                        Path.Combine(_captureDirectory ?? ".", "capture-error.txt"),
                        exception.ToString());
                }
                catch
                {
                    // Ignore logging failures, exit status is enough.
                }
                Environment.Exit(4);
            }

            _captureStep++;
            if (_captureTimer != null) _captureTimer.Start();
        }

        private void CaptureScene(string fileName)
        {
            _uiTimer.Stop();
            PumpUi();
            InvalidateVisual();
            UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            _screenshots.Capture(Path.Combine(_captureDirectory, fileName));

            _uiTimer.Start();
        }

        // ================================================================== disposal

        protected override void OnClosed(EventArgs e)
        {
            _uiTimer?.Stop();
            _captureTimer?.Stop();
            _captureWatchdog?.Dispose();
            _host?.Dispose();
            base.OnClosed(e);
        }

        private sealed class BrandMark : Control
        {
            public BrandMark()
            {
                ClipToBounds = true;
            }

            public override void Render(DrawingContext context)
            {
                var bounds = new Rect(0, 0, Math.Max(0, Bounds.Width - 1), Math.Max(0, Bounds.Height - 1));
                AppTheme.FillRounded(context, bounds, 10, AppTheme.Brush(AppTheme.AccentSoft));
                AppTheme.DrawRoundedBorder(context, bounds, 10,
                    AppTheme.Pen(AppTheme.Mix(AppTheme.Accent, AppTheme.Surface, 0.45)));

                var chip = new Rect(11, 12, 20, 18);
                context.DrawRectangle(AppTheme.Brush(AppTheme.Accent), AppTheme.Pen(AppTheme.Text, 1.2), chip);

                IPen pin = AppTheme.Pen(AppTheme.Accent);
                for (int i = 0; i < 3; i++)
                {
                    double y = 15 + i * 6;
                    context.DrawLine(pin, new Point(7, y), new Point(11, y));
                    context.DrawLine(pin, new Point(31, y), new Point(35, y));
                }

                context.DrawEllipse(AppTheme.Brush(AppTheme.Text), null,
                    new Point(chip.X + 9.5, chip.Y + 9.5), 2.5, 2.5);
            }
        }
    }
}
