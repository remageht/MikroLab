using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MikroLab.Core.Assembler;
using MikroLab.Core.Debugging;
using MikroLab.Core.Machine;
using MikroLab.Presentation;
using MikroLab.Presentation.Services;
using MikroLab.Studio.Controls;
using MikroLab.Studio.Dialogs;
using MikroLab.Studio.Services;

namespace MikroLab.Studio.Workspaces
{
    /// <summary>
    /// Full editor/debugger workspace. The layout mirrors the WinForms Studio:
    /// action bar, editor, build log on the left and a tabbed inspector on the
    /// right.
    /// </summary>
    internal sealed class StudioWorkspace : UserControl
    {
        private readonly MachineHost _host;
        private readonly IAvaloniaFileService _files;
        private readonly CodeEditor _editor;
        private readonly OutputPanel _output;
        private readonly RegisterPanel _registers;
        private readonly MemoryPanel _memory;
        private readonly DevicesPanel _devices;
        private readonly CallStackPanel _stack;
        private readonly ExecutionLogPanel _logPanel;
        private readonly Grid _inspectorContent;
        private readonly UiButton[] _inspectorTabs;
        private readonly TextBlock _fileLabel;
        private readonly TextBlock _dirtyLabel;
        private readonly TextBlock _cursorLabel;
        private readonly TextBlock _breakLabel;
        private readonly ThemedDropDown _sampleBox;
        private readonly UiButton _btnRun;
        private readonly UiButton _btnPause;
        private readonly UiButton _btnStep;
        private readonly UiButton _btnReset;
        private readonly UiButton _btnAssemble;
        private readonly UiButton _btnStepBack;

        private string _filePath;
        private bool _dirty;
        private bool _suppressDirty;
        private bool _loadingSample;
        private bool _programLoaded;
        private int _currentSampleIndex = -1;
        private string _lastAssembledText = string.Empty;

        public StudioWorkspace(MachineHost host, IAvaloniaFileService files)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _files = files ?? throw new ArgumentNullException(nameof(files));
            Background = AppTheme.Brush(AppTheme.Window);

            var root = new Grid
            {
                ColumnDefinitions = ColumnDefinitions.Parse("*,400"),
                ColumnSpacing = 14,
                Margin = new Thickness(14, 12, 14, 12)
            };

            var left = new Grid
            {
                RowDefinitions = RowDefinitions.Parse("88,34,*,166")
            };

            _btnRun = MakeAction("Пуск", UiButtonKind.Primary, 82, RunProgram);
            _btnPause = MakeAction("Пауза", UiButtonKind.Secondary, 78, PauseProgram);
            _btnStep = MakeAction("Шаг", UiButtonKind.Secondary, 72, StepProgram);
            _btnReset = MakeAction("Сброс", UiButtonKind.Secondary, 86, ResetProgram);
            _btnAssemble = MakeAction("Собрать", UiButtonKind.Secondary, 84, AssembleProgram);
            _btnStepBack = MakeAction("Шаг назад", UiButtonKind.Secondary, 96, StepBackProgram);

            var actionLayout = new Grid
            {
                RowDefinitions = RowDefinitions.Parse("42,34"),
                ColumnDefinitions = ColumnDefinitions.Parse("*,Auto")
            };

            var leftFlow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6
            };
            leftFlow.Children.Add(_btnAssemble);
            leftFlow.Children.Add(_btnRun);
            leftFlow.Children.Add(_btnPause);
            leftFlow.Children.Add(_btnStep);
            leftFlow.Children.Add(_btnStepBack);
            leftFlow.Children.Add(_btnReset);
            leftFlow.Children.Add(MakeSeparator());

            var sampleLabel = AppTheme.Label("Пример", AppTheme.TextDim, AppTheme.SmallSize);
            sampleLabel.VerticalAlignment = VerticalAlignment.Center;
            sampleLabel.Margin = new Thickness(4, 0, 0, 0);
            leftFlow.Children.Add(sampleLabel);

            _sampleBox = new ThemedDropDown
            {
                Width = 214,
                Height = 30,
                Placeholder = "Свой код",
                VerticalAlignment = VerticalAlignment.Center
            };
            foreach (SampleProgram sample in SampleLibrary.All)
            {
                _sampleBox.AddItem(sample.Name);
            }
            _sampleBox.SelectionChanged += OnSampleSelected;
            leftFlow.Children.Add(_sampleBox);

            actionLayout.Children.Add(leftFlow);

            var rightFlow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 2,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            rightFlow.Children.Add(MakeSmallButton("Новый", NewDocumentAsync));
            rightFlow.Children.Add(MakeSmallButton("Открыть", OpenDocumentAsync));
            rightFlow.Children.Add(MakeSmallButton("Сохранить", () => _ = SaveDocumentAsync()));
            actionLayout.Children.Add(rightFlow);
            Grid.SetColumn(rightFlow, 1);

            var debugFlow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 2
            };
            debugFlow.Children.Add(MakeSmallButton("В точку…", RunToAddressAsync));
            debugFlow.Children.Add(MakeSmallButton("Точки…", ShowDebugPointsAsync));
            debugFlow.Children.Add(MakeSmallButton("Журнал", () => SelectInspector(4)));
            debugFlow.Children.Add(MakeSmallButton("Очистить", () =>
            {
                _host.ClearExecutionLog();
                _logPanel?.RefreshLog(_host.ExecutionLog, true);
            }));
            actionLayout.Children.Add(debugFlow);
            Grid.SetRow(debugFlow, 1);
            Grid.SetColumnSpan(debugFlow, 2);

            var actionBar = Card(actionLayout, new Thickness(8));
            left.Children.Add(actionBar);

            var editorHeaderLayout = new Grid
            {
                ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"),
                Margin = new Thickness(12, 0)
            };
            var fileFlow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                VerticalAlignment = VerticalAlignment.Center
            };
            _fileLabel = AppTheme.Label("main.asm", AppTheme.Text, AppTheme.SmallSize, true);
            _dirtyLabel = AppTheme.Label("● не сохранено", AppTheme.Amber, AppTheme.SmallSize);
            _dirtyLabel.IsVisible = false;
            fileFlow.Children.Add(_fileLabel);
            fileFlow.Children.Add(_dirtyLabel);
            editorHeaderLayout.Children.Add(fileFlow);

            _cursorLabel = AppTheme.Label("Строка 1, кол. 1", AppTheme.TextDim, AppTheme.MonoSmallSize);
            _cursorLabel.VerticalAlignment = VerticalAlignment.Center;
            editorHeaderLayout.Children.Add(_cursorLabel);
            Grid.SetColumn(_cursorLabel, 1);

            var editorHeader = Card(editorHeaderLayout);
            left.Children.Add(editorHeader);
            Grid.SetRow(editorHeader, 1);

            _editor = new CodeEditor();
            _editor.CodeChanged += OnCodeChanged;
            _editor.CaretMoved += UpdateCursorLabel;
            _editor.BreakpointToggled += ToggleBreakpoint;
            var editorCard = Card(_editor);
            left.Children.Add(editorCard);
            Grid.SetRow(editorCard, 2);

            _output = new OutputPanel();
            _output.ErrorActivated += line =>
            {
                _editor.GoToLine(line);
                _editor.FocusEditor();
            };
            var outputCard = Card(_output);
            left.Children.Add(outputCard);
            Grid.SetRow(outputCard, 3);

            // ---------------------------------------------------------- inspector

            var inspectorLayout = new Grid
            {
                RowDefinitions = RowDefinitions.Parse("38,30,*")
            };

            var tabBar = new Grid
            {
                ColumnDefinitions = ColumnDefinitions.Parse("*,*,*,*,*"),
                Background = AppTheme.Brush(AppTheme.SurfaceRaised),
                Margin = new Thickness(0)
            };
            _inspectorTabs = new UiButton[5];
            string[] labels = { "Регистры", "Память", "Устройства", "Стек", "Журнал" };
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                var tab = new UiButton
                {
                    Text = labels[i],
                    Kind = UiButtonKind.Ghost,
                    Height = 32,
                    Margin = new Thickness(2, 3, 2, 3),
                    FontSize = AppTheme.SmallSize,
                    CornerRadius = 6
                };
                tab.Click += () => SelectInspector(index);
                _inspectorTabs[i] = tab;
                tabBar.Children.Add(tab);
                Grid.SetColumn(tab, i);
            }
            inspectorLayout.Children.Add(tabBar);

            var inspectorHeader = new Grid
            {
                ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"),
                Margin = new Thickness(12, 0)
            };
            var inspectorTitle = AppTheme.Label("ИНСПЕКТОР", AppTheme.TextDim, AppTheme.SmallSize, true);
            inspectorHeader.Children.Add(inspectorTitle);

            _breakLabel = AppTheme.Label("Точки останова: 0", AppTheme.TextDim, AppTheme.SmallSize);
            _breakLabel.HorizontalAlignment = HorizontalAlignment.Right;
            inspectorHeader.Children.Add(_breakLabel);
            Grid.SetColumn(_breakLabel, 1);
            inspectorLayout.Children.Add(inspectorHeader);
            Grid.SetRow(inspectorHeader, 1);

            _inspectorContent = new Grid { Background = AppTheme.Brush(AppTheme.Surface) };
            _registers = new RegisterPanel();
            _registers.RegisterEdited += OnRegisterEdited;
            _registers.FlagToggled += OnFlagToggled;
            _memory = new MemoryPanel();
            _memory.ByteWriteRequested += OnMemoryWrite;
            _devices = new DevicesPanel();
            _devices.AttachKeyboard(_host.Keyboard);
            _devices.TumblerChanged += OnTumblerChanged;
            _devices.AnalogChanged += OnAnalogChanged;
            _devices.KeyChanged += OnKeyChanged;
            _stack = new CallStackPanel();
            _logPanel = new ExecutionLogPanel();
            _logPanel.ClearRequested += () =>
            {
                _host.ClearExecutionLog();
                _logPanel.RefreshLog(_host.ExecutionLog, true);
            };
            _logPanel.ExportRequested += ExportLogAsync;

            _inspectorContent.Children.Add(_registers);
            _inspectorContent.Children.Add(_memory);
            _inspectorContent.Children.Add(_devices);
            _inspectorContent.Children.Add(_stack);
            _inspectorContent.Children.Add(_logPanel);
            inspectorLayout.Children.Add(_inspectorContent);
            Grid.SetRow(_inspectorContent, 2);

            var inspectorCard = Card(inspectorLayout);
            root.Children.Add(left);
            root.Children.Add(inspectorCard);
            Grid.SetColumn(inspectorCard, 1);
            Content = root;

            _host.Output += SafeLog;
            SelectInspector(0);
            LoadSample(0, false);
            UpdateButtons();
        }

        public event Action DocumentTitleChanged;

        public CodeEditor Editor => _editor;
        public OutputPanel Output => _output;
        public string DocumentName => _filePath != null
            ? Path.GetFileName(_filePath)
            : (_sampleBox.SelectedItem ?? "main.asm");

        private Window Owner => TopLevel.GetTopLevel(this) as Window;

        private static Border Card(Control child, Thickness? padding = null)
        {
            return new Border
            {
                Background = AppTheme.Brush(AppTheme.Surface),
                BorderBrush = AppTheme.Brush(AppTheme.Border),
                BorderThickness = new Thickness(1),
                Padding = padding ?? new Thickness(0),
                Child = child
            };
        }

        private static UiButton MakeAction(string text, UiButtonKind kind, double width, Action action)
        {
            var button = new UiButton
            {
                Text = text,
                Kind = kind,
                Width = width,
                Height = 34,
                FontSize = AppTheme.SmallSize
            };
            button.Click += action;
            return button;
        }

        private static UiButton MakeSmallButton(string text, Action action)
        {
            var button = new UiButton
            {
                Text = text,
                Kind = UiButtonKind.Ghost,
                Width = 70,
                Height = 30,
                Margin = new Thickness(0, 4, 2, 0),
                FontSize = AppTheme.SmallSize
            };
            button.Click += action;
            return button;
        }

        private static Control MakeSeparator()
        {
            return new Border
            {
                Width = 1,
                Height = 22,
                Background = AppTheme.Brush(AppTheme.Border),
                Margin = new Thickness(8, 6, 8, 0)
            };
        }

        // ------------------------------------------------------------------ actions

        private void AssembleProgram()
        {
            AssembleResult result = _host.Assemble(_editor.Code);
            _output.SetErrors(result.Errors);
            _editor.SetErrorLine(result.Errors.Count > 0 ? result.Errors[0].LineNumber : (int?)null);

            if (!result.Success)
            {
                _programLoaded = false;
                if (result.Errors.Count > 0)
                {
                    _editor.GoToLine(result.Errors[0].LineNumber);
                    _output.Log("Первая ошибка: строка " + result.Errors[0].LineNumber +
                                " · " + result.Errors[0].Message, LogLevel.Error);
                }
                UpdateButtons();
                return;
            }

            _programLoaded = true;
            _lastAssembledText = _editor.Code;
            _editor.SetBreakpointLines(_host.BreakpointLines);
            UpdateBreakpointLabel();
            UpdateExecutionLine(_host.CurrentPc());
            UpdateButtons();
        }

        private void RunProgram()
        {
            if (!_programLoaded || _lastAssembledText != _editor.Code)
            {
                AssembleProgram();
                if (!_programLoaded || _lastAssembledText != _editor.Code) return;
            }

            if (_host.Run())
            {
                _output.Log("Пуск · PC=" + _host.CurrentPc().ToString("X4") + "h");
                UpdateButtons();
            }
        }

        private void PauseProgram()
        {
            if (_host.State != RunState.Running) return;
            _host.Pause();
            _output.Log("Пауза · PC=" + _host.CurrentPc().ToString("X4") + "h");
            UpdateButtons();
        }

        private void StepProgram()
        {
            _host.Step();
            UpdateExecutionLine(_host.CurrentPc());
            UpdateButtons();
        }

        private void StepBackProgram()
        {
            _host.StepBack();
            UpdateExecutionLine(_host.CurrentPc());
            _logPanel.RefreshLog(_host.ExecutionLog, true);
            UpdateButtons();
        }

        private async void RunToAddressAsync()
        {
            ushort? address = await AddressDialog.ShowAsync(
                Owner, "Выполнить до адреса", "Адрес останова (hex):", _host.CurrentPc());
            if (!address.HasValue) return;
            _host.RunTo(address.Value);
        }

        private async void ShowDebugPointsAsync()
        {
            await DebugPointsDialog.ShowAsync(Owner, _host);
            _logPanel.RefreshLog(_host.ExecutionLog, true);
            UpdateButtons();
        }

        private void ResetProgram()
        {
            _host.Reset();
            _output.Log("Сброс · PC=" + _host.EntryPoint.ToString("X4") + "h");
            UpdateExecutionLine(_host.EntryPoint);
            UpdateButtons();
        }

        private void ToggleBreakpoint(int line)
        {
            bool added = _host.ToggleBreakpointLine(line);
            _editor.SetBreakpointLines(_host.BreakpointLines);
            UpdateBreakpointLabel();
            _output.Log(added
                ? "Точка останова · строка " + line
                : "Точка останова снята · строка " + line);
        }

        private void OnMemoryWrite(ushort address, byte value)
        {
            if (_host.State == RunState.Running)
            {
                _output.Log("Остановите выполнение, чтобы редактировать память.", LogLevel.Warning);
                return;
            }

            _host.WithMachine(machine => machine.Memory.WriteByte(address, value));
            _output.Log("Память · " + address.ToString("X4") + "h ← " + value.ToString("X2") + "h");
        }

        private void OnRegisterEdited(string name, ushort value)
        {
            if (!_host.SetRegister(name, value)) return;
            UpdateExecutionLine(_host.CurrentPc());
            _registers.UpdateSnapshot(_host.Snapshot());
            UpdateButtons();
        }

        private void OnFlagToggled(string name)
        {
            _host.ToggleFlag(name);
            _registers.UpdateSnapshot(_host.Snapshot());
            UpdateButtons();
        }

        private void OnTumblerChanged(int index, bool value)
        {
            _host.WithMachine(machine =>
            {
                if (index == 0) machine.Ppi.Tumbler1 = value;
                else if (index == 1) machine.Ppi.Tumbler2 = value;
                else if (index == 2) machine.Ppi.Tumbler3 = value;
            });
        }

        private void OnAnalogChanged(int channel, double value)
        {
            _host.WithMachine(machine => machine.Analog.SetChannel(channel, value));
        }

        private void OnKeyChanged(int row, int column, bool pressed)
        {
            _host.SetKeyState(row, column, pressed);
        }

        // ------------------------------------------------------------------ files

        private async void NewDocumentAsync()
        {
            if (!await ConfirmDiscardAsync()) return;

            _filePath = null;
            _suppressDirty = true;
            _editor.Code = SamplePrograms.NewTemplateFor(_host.Origin);
            _suppressDirty = false;
            _loadingSample = true;
            _currentSampleIndex = -1;
            _sampleBox.SelectIndex(-1);
            _loadingSample = false;
            _dirty = false;
            _programLoaded = false;
            _output.Log("Новый проект");
            UpdateDocumentHeader();
            AssembleProgram();
        }

        private async void OpenDocumentAsync()
        {
            if (!await ConfirmDiscardAsync()) return;

            string path = await _files.OpenFileAsync(new FileDialogRequest
            {
                Title = "Открыть программу",
                Filter = "Ассемблер 8080 (*.asm;*.a80)|*.asm;*.a80|Все файлы (*.*)|*.*",
                CheckFileExists = true
            });
            if (path == null) return;

            try
            {
                _suppressDirty = true;
                _editor.Code = File.ReadAllText(path);
                _suppressDirty = false;
                _filePath = path;
                _loadingSample = true;
                _currentSampleIndex = -1;
                _sampleBox.SelectIndex(-1);
                _loadingSample = false;
                _dirty = false;
                _programLoaded = false;
                _output.Log("Открыто · " + path);
                UpdateDocumentHeader();
                AssembleProgram();
            }
            catch (Exception ex)
            {
                _suppressDirty = false;
                _output.Log("Не удалось открыть файл: " + ex.Message, LogLevel.Error);
            }
        }

        private async Task SaveDocumentAsync()
        {
            if (_filePath == null)
            {
                await SaveDocumentAsAsync();
                return;
            }

            try
            {
                File.WriteAllText(_filePath, _editor.Code, new UTF8Encoding(false));
                _dirty = false;
                UpdateDocumentHeader();
                _output.Log("Сохранено · " + _filePath, LogLevel.Success);
            }
            catch (Exception ex)
            {
                _output.Log("Не удалось сохранить файл: " + ex.Message, LogLevel.Error);
            }
        }

        private async Task SaveDocumentAsAsync()
        {
            string path = await _files.SaveFileAsync(new FileDialogRequest
            {
                Title = "Сохранить программу",
                Filter = "Ассемблер 8080 (*.asm)|*.asm|Все файлы (*.*)|*.*",
                DefaultFileName = Path.GetFileName(_filePath ?? "main.asm")
            });
            if (path == null) return;

            _filePath = path;
            await SaveDocumentAsync();
            UpdateDocumentHeader();
        }

        private async Task<bool> ConfirmDiscardAsync()
        {
            if (!_dirty) return true;
            bool? answer = await ConfirmDialog.ShowAsync(
                Owner, "В документе есть несохранённые изменения. Продолжить?", "MikroLab");
            return answer == true;
        }

        private async void ExportLogAsync(ExecutionLogFormat format)
        {
            string path = await _files.SaveFileAsync(new FileDialogRequest
            {
                Title = "Экспорт журнала",
                Filter = format == ExecutionLogFormat.Csv
                    ? "CSV (*.csv)|*.csv|Все файлы (*.*)|*.*"
                    : "Markdown (*.md)|*.md|Все файлы (*.*)|*.*",
                DefaultFileName = format == ExecutionLogFormat.Csv
                    ? "execution-log.csv"
                    : "execution-log.md"
            });
            if (path == null) return;

            try
            {
                _host.ExportExecutionLog(path, format);
            }
            catch (Exception ex)
            {
                _output.Log("Не удалось экспортировать журнал: " + ex.Message, LogLevel.Error);
            }
        }

        // ------------------------------------------------------------------ samples/docs

        public void LoadSample(int index, bool announce = true)
        {
            if (index < 0 || index >= SampleLibrary.All.Length) return;

            SampleProgram sample = SampleLibrary.All[index];
            _suppressDirty = true;
            _editor.Code = sample.Code;
            _suppressDirty = false;
            _filePath = null;
            _loadingSample = true;
            _currentSampleIndex = index;
            _sampleBox.SelectIndex(index);
            _loadingSample = false;
            _dirty = false;
            _programLoaded = false;
            UpdateDocumentHeader();

            if (announce) _output.Log("Пример · " + sample.Name);
            AssembleProgram();
        }

        private async void OnSampleSelected(int index)
        {
            if (_loadingSample || index < 0) return;
            if (!await ConfirmDiscardAsync())
            {
                _sampleBox.SelectIndex(_currentSampleIndex);
                return;
            }
            LoadSample(index);
        }

        private void OnCodeChanged()
        {
            if (_suppressDirty) return;
            _dirty = true;
            UpdateDocumentHeader();
            UpdateButtons();
        }

        private void UpdateCursorLabel()
        {
            _cursorLabel.Text = "Строка " + _editor.CaretLine + ", кол. " + _editor.CaretColumn;
        }

        private void UpdateDocumentHeader()
        {
            _fileLabel.Text = DocumentName;
            _dirtyLabel.IsVisible = _dirty;
            DocumentTitleChanged?.Invoke();
        }

        private void UpdateBreakpointLabel()
        {
            int count = _host.ActiveBreakpointCount;
            _breakLabel.Text = "Точки останова: " + count;
            _breakLabel.Foreground = AppTheme.Brush(count > 0 ? AppTheme.Amber : AppTheme.TextDim);
        }

        private void SelectInspector(int index)
        {
            for (int i = 0; i < _inspectorTabs.Length; i++)
            {
                _inspectorTabs[i].Active = i == index;
                _inspectorTabs[i].InvalidateVisual();
            }

            _registers.IsVisible = index == 0;
            _memory.IsVisible = index == 1;
            _devices.IsVisible = index == 2;
            _stack.IsVisible = index == 3;
            _logPanel.IsVisible = index == 4;
        }

        // ------------------------------------------------------------------ update loop

        public void UpdateFromSnapshot(MachineSnapshot snapshot)
        {
            if (snapshot == null) return;

            _registers.UpdateSnapshot(snapshot, _host.LastLogEntry);
            _memory.UpdateSnapshot(snapshot);
            _devices.UpdateSnapshot(snapshot);

            if (_host.State != RunState.Running)
            {
                _stack.UpdateStack(snapshot, _host.CallStack());
            }

            _logPanel.RefreshLog(_host.ExecutionLog);
            _btnStepBack.IsEnabled = _host.CanStepBack && _host.State != RunState.Running;

            UpdateExecutionLine(snapshot.PC);
            UpdateButtons();
            UpdateBreakpointLabel();
        }

        private void UpdateExecutionLine(ushort pc)
        {
            _editor.SetExecutionLine(_host.FindLineForAddress(pc));
        }

        private void UpdateButtons()
        {
            RunState state = _host.State;
            bool running = state == RunState.Running;
            bool halted = state == RunState.Halted;

            _btnRun.IsEnabled = !running && !halted;
            _btnPause.IsEnabled = running;
            _btnStep.IsEnabled = !halted;
            _btnStepBack.IsEnabled = _host.CanStepBack && !running;
            _btnReset.IsEnabled = true;
            _btnAssemble.IsEnabled = !running;
        }

        private void SafeLog(string message, LogLevel level)
        {
            Dispatcher.UIThread.Post(() => _output.Log(message, level));
        }

        // ------------------------------------------------------------------ host automation

        public void HostLoadSample(int index) => LoadSample(index, true);
        public void HostAssemble() => AssembleProgram();
        public void HostRun() => RunProgram();
        public void HostPause() => PauseProgram();
        public void HostStep() => StepProgram();
        public void HostReset() => ResetProgram();
        public void HostToggleBreakpoint(int line) => ToggleBreakpoint(line);
        public void HostStepBack() => StepBackProgram();
        public void HostRunTo(ushort address) => _host.RunTo(address);
        public Task HostShowDebugPointsAsync() => DebugPointsDialog.ShowAsync(Owner, _host);
        public void HostSelectInspector(int index) => SelectInspector(index);
        public Task HostNewAsync() { NewDocumentAsync(); return Task.CompletedTask; }
        public Task HostOpenAsync() { OpenDocumentAsync(); return Task.CompletedTask; }
        public Task HostSaveAsync() => SaveDocumentAsync();
        public Task HostRunToAsync() { RunToAddressAsync(); return Task.CompletedTask; }

        /// <summary>
        /// Applies a memory/stack profile and rebuilds the current source with
        /// the profile's default ORG so the editor, listing and machine agree.
        /// </summary>
        public void HostApplyProfile(MachineProfile profile)
        {
            _host.ApplyProfile(profile);
            AssembleProgram();
            UpdateDocumentHeader();
        }

        /// <summary>
        /// Synchronises the Studio after a non-assembler image was loaded
        /// (.580, Intel HEX or a saved state) so "Run" executes the imported
        /// bytes instead of reassembling the editor buffer.
        /// </summary>
        public void HostExternalProgramLoaded()
        {
            _programLoaded = _host.IsProgramLoaded;
            if (_programLoaded) _lastAssembledText = _editor.Code;
            UpdateFromSnapshot(_host.Snapshot());
            UpdateButtons();
        }

        public string HostCode => _editor.Code;
        public bool IsRunning => _host.State == RunState.Running;
    }
}
