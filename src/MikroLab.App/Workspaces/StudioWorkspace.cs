using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using MikroLab.App.Controls;
using MikroLab.Core.Assembler;
using MikroLab.Core.Machine;

namespace MikroLab.App.Workspaces
{
    internal sealed class StudioWorkspace : UserControl
    {
        private readonly MachineHost _host;
        private readonly CodeEditor _editor;
        private readonly OutputPanel _output;
        private readonly RegisterPanel _registers;
        private readonly MemoryPanel _memory;
        private readonly DevicesPanel _devices;
        private readonly StackPanel _stack;
        private readonly Panel _inspectorContent;
        private readonly UiButton[] _inspectorTabs;
        private readonly Label _fileLabel;
        private readonly Label _dirtyLabel;
        private readonly Label _cursorLabel;
        private readonly Label _breakLabel;
        private readonly ThemedDropDown _sampleBox;
        private readonly UiButton _btnRun;
        private readonly UiButton _btnPause;
        private readonly UiButton _btnStep;
        private readonly UiButton _btnReset;
        private readonly UiButton _btnAssemble;

        private string _filePath;
        private bool _dirty;
        private bool _suppressDirty;
        private bool _loadingSample;
        private bool _programLoaded;
        private int _currentSampleIndex = -1;
        private string _lastAssembledText = string.Empty;

        public event Action DocumentTitleChanged;

        public StudioWorkspace(MachineHost host)
        {
            _host = host;
            BackColor = AppTheme.Window;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = AppTheme.Window,
                Padding = new Padding(14, 12, 14, 12),
                Margin = new Padding(0)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 400));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var left = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                BackColor = AppTheme.Window,
                Margin = new Padding(0, 0, 14, 0),
                Padding = new Padding(0)
            };
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, 166));

            _btnRun = MakeAction("Пуск", UiButtonKind.Primary, 82, () => RunProgram());
            _btnPause = MakeAction("Пауза", UiButtonKind.Secondary, 78, () => PauseProgram());
            _btnStep = MakeAction("Шаг", UiButtonKind.Secondary, 72, () => StepProgram());
            _btnReset = MakeAction("Сброс", UiButtonKind.Secondary, 86, () => ResetProgram());
            _btnAssemble = MakeAction("Собрать", UiButtonKind.Secondary, 84, () => AssembleProgram());

            var actionBar = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(8, 8, 8, 8) };
            var actionLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = AppTheme.Surface,
                Margin = new Padding(0)
            };
            actionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            actionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            actionLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var leftFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoScroll = false,
                BackColor = AppTheme.Surface,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            leftFlow.Controls.Add(_btnAssemble);
            leftFlow.Controls.Add(_btnRun);
            leftFlow.Controls.Add(_btnPause);
            leftFlow.Controls.Add(_btnStep);
            leftFlow.Controls.Add(_btnReset);
            leftFlow.Controls.Add(MakeSeparator());
            Label sampleLabel = AppTheme.Label("Пример", AppTheme.TextDim, AppTheme.SmallFont);
            sampleLabel.Margin = new Padding(4, 10, 6, 0);
            leftFlow.Controls.Add(sampleLabel);

            _sampleBox = new ThemedDropDown
            {
                Width = 214,
                Height = 30,
                Font = AppTheme.SmallFont,
                Margin = new Padding(0, 3, 0, 0),
                Placeholder = "Свой код"
            };
            foreach (SampleProgram sample in SampleLibrary.All)
            {
                _sampleBox.AddItem(sample.Name);
            }
            _sampleBox.SelectionChanged += OnSampleSelected;
            leftFlow.Controls.Add(_sampleBox);

            var rightFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                BackColor = AppTheme.Surface,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            rightFlow.Controls.Add(MakeSmallButton("Новый", () => NewDocument()));
            rightFlow.Controls.Add(MakeSmallButton("Открыть", () => OpenDocument()));
            rightFlow.Controls.Add(MakeSmallButton("Сохранить", () => SaveDocument()));

            actionLayout.Controls.Add(leftFlow, 0, 0);
            actionLayout.Controls.Add(rightFlow, 1, 0);
            actionBar.Controls.Add(actionLayout);

            var editorHeader = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(12, 0, 12, 0) };
            var editorHeaderLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = AppTheme.Surface,
                Margin = new Padding(0)
            };
            editorHeaderLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            editorHeaderLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            editorHeaderLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            _fileLabel = AppTheme.Label("main.asm", AppTheme.Text, AppTheme.SmallBoldFont);
            _fileLabel.AutoSize = true;
            _fileLabel.TextAlign = ContentAlignment.MiddleLeft;
            _fileLabel.Location = new Point(0, 8);

            _dirtyLabel = AppTheme.Label("● не сохранено", AppTheme.Amber, AppTheme.SmallFont);
            _dirtyLabel.AutoSize = true;
            _dirtyLabel.Visible = false;
            _dirtyLabel.Location = new Point(0, 9);

            _cursorLabel = AppTheme.Label("Строка 1, кол. 1", AppTheme.TextDim, AppTheme.MonoSmall);
            _cursorLabel.AutoSize = true;
            _cursorLabel.Margin = new Padding(0, 7, 0, 0);

            var fileFlow = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = AppTheme.Surface,
                Padding = new Padding(0),
                Margin = new Padding(0)
            };
            fileFlow.Controls.Add(_fileLabel);
            fileFlow.Controls.Add(_dirtyLabel);

            editorHeaderLayout.Controls.Add(fileFlow, 0, 0);
            editorHeaderLayout.Controls.Add(_cursorLabel, 1, 0);
            editorHeader.Controls.Add(editorHeaderLayout);

            var editorCard = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(0) };
            _editor = new CodeEditor { Dock = DockStyle.Fill };
            _editor.CodeChanged += OnCodeChanged;
            _editor.CaretMoved += UpdateCursorLabel;
            _editor.BreakpointToggled += ToggleBreakpoint;
            editorCard.Controls.Add(_editor);

            var outputCard = new CardPanel { Dock = DockStyle.Fill };
            _output = new OutputPanel { Dock = DockStyle.Fill };
            _output.ErrorActivated += line =>
            {
                _editor.GoToLine(line);
                _editor.FocusEditor();
            };
            outputCard.Controls.Add(_output);

            left.Controls.Add(actionBar, 0, 0);
            left.Controls.Add(editorHeader, 0, 1);
            left.Controls.Add(editorCard, 0, 2);
            left.Controls.Add(outputCard, 0, 3);

            // ---------------------------------------------------------- inspector

            var inspector = new CardPanel { Dock = DockStyle.Fill, Margin = new Padding(0) };
            var inspectorLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = AppTheme.Surface,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            inspectorLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            inspectorLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            inspectorLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var tabBar = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                BackColor = AppTheme.SurfaceRaised,
                Margin = new Padding(0),
                Padding = new Padding(3)
            };
            for (int i = 0; i < 4; i++) tabBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            tabBar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            _inspectorTabs = new UiButton[4];
            string[] labels = { "Регистры", "Память", "Устройства", "Стек" };
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                var tab = new UiButton
                {
                    Text = labels[i],
                    Kind = UiButtonKind.Ghost,
                    Dock = DockStyle.Fill,
                    Height = 32,
                    Font = AppTheme.SmallFont,
                    Margin = new Padding(2, 0, 2, 0),
                    CornerRadius = 6
                };
                tab.Click += (sender, args) => SelectInspector(index);
                _inspectorTabs[i] = tab;
                tabBar.Controls.Add(tab, i, 0);
            }

            var inspectorHeader = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = AppTheme.Surface,
                Padding = new Padding(12, 0, 12, 0)
            };
            Label inspectorTitle = AppTheme.Label("ИНСПЕКТОР", AppTheme.TextDim, AppTheme.SmallBoldFont);
            inspectorTitle.Location = new Point(12, 9);
            _breakLabel = AppTheme.Label("Точки останова: 0", AppTheme.TextDim, AppTheme.SmallFont);
            _breakLabel.AutoSize = false;
            _breakLabel.TextAlign = ContentAlignment.MiddleRight;
            _breakLabel.Dock = DockStyle.Right;
            _breakLabel.Width = 140;
            inspectorHeader.Controls.Add(inspectorTitle);
            inspectorHeader.Controls.Add(_breakLabel);

            _inspectorContent = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.Surface };
            _registers = new RegisterPanel { Dock = DockStyle.Fill };
            _memory = new MemoryPanel { Dock = DockStyle.Fill };
            _memory.ByteWriteRequested += OnMemoryWrite;
            _devices = new DevicesPanel { Dock = DockStyle.Fill };
            _devices.TumblerChanged += OnTumblerChanged;
            _devices.AnalogChanged += OnAnalogChanged;
            _stack = new StackPanel { Dock = DockStyle.Fill };

            _inspectorContent.Controls.Add(_registers);
            _inspectorContent.Controls.Add(_memory);
            _inspectorContent.Controls.Add(_devices);
            _inspectorContent.Controls.Add(_stack);

            inspectorLayout.Controls.Add(tabBar, 0, 0);
            inspectorLayout.Controls.Add(inspectorHeader, 0, 1);
            inspectorLayout.Controls.Add(_inspectorContent, 0, 2);
            inspector.Controls.Add(inspectorLayout);

            root.Controls.Add(left, 0, 0);
            root.Controls.Add(inspector, 1, 0);
            Controls.Add(root);

            _host.Output += SafeLog;
            SelectInspector(0);
            LoadSample(0, false);
            UpdateButtons();
        }

        public CodeEditor Editor => _editor;
        public OutputPanel Output => _output;
        public string DocumentName => _filePath != null ? Path.GetFileName(_filePath)
            : (_sampleBox.SelectedItem ?? "main.asm");

        private static UiButton MakeAction(string text, UiButtonKind kind, int width, Action action)
        {
            var button = new UiButton
            {
                Text = text,
                Kind = kind,
                Width = width,
                Height = 34,
                Margin = new Padding(0, 0, 6, 0),
                Font = AppTheme.SmallFont
            };
            button.Click += (sender, args) => action();
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
                Margin = new Padding(0, 4, 2, 0),
                Font = AppTheme.SmallFont
            };
            button.Click += (sender, args) => action();
            return button;
        }

        private static Control MakeSeparator()
        {
            return new Panel
            {
                Width = 1,
                Height = 22,
                BackColor = AppTheme.Border,
                Margin = new Padding(8, 6, 8, 0)
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
                    _output.Log("Первая ошибка: строка " + result.Errors[0].LineNumber + " · " + result.Errors[0].Message, LogLevel.Error);
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
                if (!_programLoaded || _lastAssembledText != _editor.Code)
                {
                    return;
                }
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
            if (added)
            {
                _output.Log("Точка останова · строка " + line);
            }
            else
            {
                _output.Log("Точка останова снята · строка " + line);
            }
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

        // ------------------------------------------------------------------ files

        private void NewDocument()
        {
            if (!ConfirmDiscard()) return;

            _filePath = null;
            _suppressDirty = true;
            _editor.Code = SamplePrograms.NewTemplate;
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

        private void OpenDocument()
        {
            if (!ConfirmDiscard()) return;

            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "Открыть программу";
                dialog.Filter = "Ассемблер 8080 (*.asm;*.a80)|*.asm;*.a80|Все файлы (*.*)|*.*";
                if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;

                try
                {
                    _suppressDirty = true;
                    _editor.Code = File.ReadAllText(dialog.FileName);
                    _suppressDirty = false;
                    _filePath = dialog.FileName;
                    _loadingSample = true;
                    _currentSampleIndex = -1;
                    _sampleBox.SelectIndex(-1);
                    _loadingSample = false;
                    _dirty = false;
                    _programLoaded = false;
                    _output.Log("Открыто · " + dialog.FileName);
                    UpdateDocumentHeader();
                    AssembleProgram();
                }
                catch (Exception ex)
                {
                    _suppressDirty = false;
                    _output.Log("Не удалось открыть файл: " + ex.Message, LogLevel.Error);
                }
            }
        }

        private void SaveDocument()
        {
            if (_filePath == null)
            {
                SaveDocumentAs();
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

        private void SaveDocumentAs()
        {
            using (var dialog = new SaveFileDialog())
            {
                dialog.Title = "Сохранить программу";
                dialog.Filter = "Ассемблер 8080 (*.asm)|*.asm|Все файлы (*.*)|*.*";
                dialog.FileName = Path.GetFileName(_filePath ?? "main.asm");
                if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;

                _filePath = dialog.FileName;
                SaveDocument();
                UpdateDocumentHeader();
            }
        }

        private bool ConfirmDiscard()
        {
            if (!_dirty) return true;

            DialogResult answer = MessageBox.Show(
                FindForm(),
                "В документе есть несохранённые изменения. Продолжить?",
                "MikroLab",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);
            return answer == DialogResult.Yes;
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

            if (announce)
            {
                _output.Log("Пример · " + sample.Name);
            }
            AssembleProgram();
        }

        private void OnSampleSelected(int index)
        {
            if (_loadingSample || index < 0) return;
            if (!ConfirmDiscard())
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
            _dirtyLabel.Visible = _dirty;
            _dirtyLabel.Location = new Point(_fileLabel.Right + 8, 9);
            DocumentTitleChanged?.Invoke();
        }

        private void UpdateBreakpointLabel()
        {
            int count = _host.ActiveBreakpointCount;
            _breakLabel.Text = "Точки останова: " + count;
            _breakLabel.ForeColor = count > 0 ? AppTheme.Amber : AppTheme.TextDim;
        }

        private void SelectInspector(int index)
        {
            for (int i = 0; i < _inspectorTabs.Length; i++)
            {
                _inspectorTabs[i].Active = i == index;
                _inspectorTabs[i].Invalidate();
            }

            _registers.Visible = index == 0;
            _memory.Visible = index == 1;
            _devices.Visible = index == 2;
            _stack.Visible = index == 3;
            if (index == 0) _registers.BringToFront();
            else if (index == 1) _memory.BringToFront();
            else if (index == 2) _devices.BringToFront();
            else _stack.BringToFront();
        }

        // ------------------------------------------------------------------ update loop

        public void UpdateFromSnapshot(MachineSnapshot snapshot)
        {
            if (snapshot == null) return;

            _registers.UpdateSnapshot(snapshot);
            _memory.UpdateSnapshot(snapshot);
            _devices.UpdateSnapshot(snapshot);

            if (_host.State != RunState.Running)
            {
                _stack.UpdateStack(snapshot, _host.CallStack());
            }

            UpdateExecutionLine(snapshot.PC);
            UpdateButtons();
            UpdateBreakpointLabel();
        }

        private void UpdateExecutionLine(ushort pc)
        {
            int? line = _host.FindLineForAddress(pc);
            _editor.SetExecutionLine(line);
        }

        private void UpdateButtons()
        {
            RunState state = _host.State;
            bool running = state == RunState.Running;
            bool halted = state == RunState.Halted;

            _btnRun.Enabled = !running && !halted;
            _btnPause.Enabled = running;
            _btnStep.Enabled = !halted;
            _btnReset.Enabled = true;
            _btnAssemble.Enabled = !running;
        }

        private void SafeLog(string message, LogLevel level)
        {
            if (IsDisposed || Disposing) return;

            if (IsHandleCreated && InvokeRequired)
            {
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        if (!IsDisposed && !Disposing) _output.Log(message, level);
                    }));
                }
                catch (InvalidOperationException)
                {
                    // The form is closing.
                }
            }
            else
            {
                _output.Log(message, level);
            }
        }

        // ------------------------------------------------------------------ host automation

        public void HostLoadSample(int index) => LoadSample(index, true);
        public void HostAssemble() => AssembleProgram();
        public void HostRun() => RunProgram();
        public void HostPause() => PauseProgram();
        public void HostStep() => StepProgram();
        public void HostReset() => ResetProgram();
        public void HostToggleBreakpoint(int line) => ToggleBreakpoint(line);
        public void HostSelectInspector(int index) => SelectInspector(index);
        public void HostNew() => NewDocument();
        public void HostOpen() => OpenDocument();
        public void HostSave() => SaveDocument();
        public string HostCode => _editor.Code;
        public bool IsRunning => _host.State == RunState.Running;
    }
}
