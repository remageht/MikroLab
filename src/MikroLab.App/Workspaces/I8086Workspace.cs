using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using MikroLab.App.Controls;
using MikroLab.Core.Assembler;
using MikroLab.Core.Assembler.I8086;
using MikroLab.Core.Cpu.I8086;
using MikroLab.Core.Debugging;
using MikroLab.Core.Memory;
using MikroLab.Presentation;
using MikroLab.Presentation.Services;

namespace MikroLab.App.Workspaces
{
    /// <summary>
    /// Workspace for the Intel 8086 architecture: a source editor, the
    /// processor state, a memory dump and the output of the emulated DOS
    /// services.
    /// </summary>
    internal sealed class I8086Workspace : UserControl
    {
        private const ushort ProgramOrigin = 0x0100;
        private const int MaxSteps = 1000000;

        private static readonly string[] RegisterNames =
        {
            "AX", "BX", "CX", "DX", "SI", "DI", "BP", "SP",
            "CS", "DS", "ES", "SS", "IP", "FLAGS"
        };

        private readonly IFileDialogService _files;
        private readonly CodeEditor _editor;
        private readonly TextBox _consoleView;
        private readonly Label[] _registerValues = new Label[RegisterNames.Length];
        private readonly Label[] _flagValues = new Label[7];
        private readonly TextBox _memoryView;
        private readonly Label _statusLabel;

        private I8086Memory _memory = new I8086Memory();
        private I8086Console _console = new I8086Console();
        private I8086Cpu _cpu;
        private I8086AssembleResult _assembly;
        private string _filePath;
        private readonly BreakpointSet _breakpoints = new BreakpointSet();
        private int _skipBreakpoint = -1;

        public I8086Workspace(IFileDialogService files)
        {
            _files = files;
            _cpu = new I8086Cpu(_memory, _console);

            Dock = DockStyle.Fill;
            BackColor = AppTheme.Window;

            _editor = new CodeEditor { Dock = DockStyle.Fill };
            _editor.BreakpointToggled += ToggleBreakpoint;
            _consoleView = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                Dock = DockStyle.Fill,
                BackColor = AppTheme.EditorBackground,
                ForeColor = AppTheme.Text,
                BorderStyle = BorderStyle.None,
                Font = AppTheme.MonoSmall,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = false
            };
            _memoryView = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                Dock = DockStyle.Fill,
                BackColor = AppTheme.EditorBackground,
                ForeColor = AppTheme.Text,
                BorderStyle = BorderStyle.None,
                Font = AppTheme.MonoSmall,
                ScrollBars = ScrollBars.None,
                WordWrap = false
            };

            _statusLabel = AppTheme.Label("Готово", AppTheme.TextDim, AppTheme.SmallFont);
            _statusLabel.AutoSize = true;
            _statusLabel.BackColor = Color.Transparent;

            var toolbar = BuildToolbar();
            _split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                BackColor = AppTheme.Border
            };
            _split.Panel1.Controls.Add(_editor);
            _split.Panel2.Controls.Add(BuildInspector());

            Controls.Add(_split);
            Controls.Add(toolbar);

            LoadSample();
            RefreshPanels();
        }

        private readonly SplitContainer _split;

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            ApplySplitterDistance();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ApplySplitterDistance();
        }

        private void ApplySplitterDistance()
        {
            if (_split == null || _split.Width < 700)
                return;

            int left = 200;
            int right = 260;
            if (_split.Panel1MinSize != left)
                _split.Panel1MinSize = left;
            if (_split.Panel2MinSize != right)
                _split.Panel2MinSize = right;

            int desired = Math.Min(680, _split.Width - right);
            if (desired < left)
                desired = left;
            if (_split.Width - desired < right)
                return;
            if (Math.Abs(_split.SplitterDistance - desired) > 8)
                _split.SplitterDistance = desired;
        }

        // ------------------------------------------------------------------

        private Control BuildToolbar()
        {
            var panel = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = AppTheme.Surface };

            UiButton Make(string text, Action action, int width = 96)
            {
                var button = new UiButton
                {
                    Text = text,
                    Kind = UiButtonKind.Ghost,
                    Width = width,
                    Height = 30,
                    Font = AppTheme.SmallFont,
                    Location = new Point(0, 0)
                };
                button.Click += (sender, args) => action();
                return button;
            }

            var open = Make("Открыть", OpenFile, 92);
            var save = Make("Сохранить", SaveFile, 100);
            var assemble = Make("Собрать", AssembleProgram, 92);
            var run = Make("Выполнить", RunProgram, 104);
            var step = Make("Шаг", StepOnce, 74);
            var reset = Make("Сброс", ResetMachine, 74);
            var disasm = Make("Дизассемблировать", ShowDisassembly, 132);
            var code = Make("Код → мнемоника", ShowMachineCode, 128);
            var point = Make("Точка", ToggleCaretBreakpoint, 68);

            int x = 12;
            foreach (UiButton button in new[] { open, save, assemble, run, step, reset, disasm, code, point })
            {
                button.Location = new Point(x, 9);
                x += button.Width + 8;
            }

            _statusLabel.Location = new Point(x + 8, 16);

            panel.Controls.Add(open);
            panel.Controls.Add(save);
            panel.Controls.Add(assemble);
            panel.Controls.Add(run);
            panel.Controls.Add(step);
            panel.Controls.Add(reset);
            panel.Controls.Add(disasm);
            panel.Controls.Add(code);
            panel.Controls.Add(point);
            panel.Controls.Add(_statusLabel);
            return panel;
        }

        private Control BuildInspector()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                BackColor = AppTheme.Window,
                Padding = new Padding(10)
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 208));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));

            root.Controls.Add(BuildRegisterCard(), 0, 0);
            root.Controls.Add(BuildFlagsCard(), 0, 1);
            root.Controls.Add(BuildMemoryCard(), 0, 2);
            root.Controls.Add(BuildOutputCard(), 0, 3);
            return root;
        }

        private static Control MakeCard(string title, Control content)
        {
            var card = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.Surface };
            card.Paint += (sender, args) =>
            {
                using (var pen = new Pen(AppTheme.Border))
                    args.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
            };

            var label = AppTheme.Label(title, AppTheme.TextMuted, AppTheme.SmallFont);
            label.Dock = DockStyle.Top;
            label.Height = 26;
            label.Padding = new Padding(8, 5, 0, 0);
            label.BackColor = Color.Transparent;

            content.Dock = DockStyle.Fill;
            card.Controls.Add(content);
            card.Controls.Add(label);
            return card;
        }

        private Control BuildRegisterCard()
        {
            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 4,
                BackColor = Color.Transparent,
                Padding = new Padding(6, 0, 6, 4)
            };
            for (int i = 0; i < 4; i++)
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));

            for (int i = 0; i < RegisterNames.Length; i++)
            {
                int row = i / 4;
                int column = i % 4;

                var name = AppTheme.Label(RegisterNames[i], AppTheme.TextDim, AppTheme.SmallFont);
                name.AutoSize = false;
                name.Dock = DockStyle.Fill;
                name.TextAlign = ContentAlignment.MiddleLeft;

                var value = AppTheme.Label("0000h", AppTheme.Text, AppTheme.MonoSmall);
                value.AutoSize = false;
                value.Dock = DockStyle.Fill;
                value.TextAlign = ContentAlignment.MiddleLeft;
                _registerValues[i] = value;

                grid.Controls.Add(name, column * 2, row);
                grid.Controls.Add(value, column * 2 + 1, row);
            }

            return MakeCard("Регистры Intel 8086", grid);
        }

        private Control BuildFlagsCard()
        {
            var panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
                Padding = new Padding(6, 0, 0, 0)
            };

            string[] names = { "CF", "PF", "AF", "ZF", "SF", "DF", "OF" };
            for (int i = 0; i < names.Length; i++)
            {
                var name = AppTheme.Label(names[i], AppTheme.TextDim, AppTheme.SmallFont);
                name.AutoSize = true;
                name.Margin = new Padding(2, 6, 2, 2);

                var value = AppTheme.Label("0", AppTheme.TextMuted, AppTheme.MonoSmall);
                value.AutoSize = true;
                value.Margin = new Padding(0, 6, 8, 2);
                _flagValues[i] = value;

                panel.Controls.Add(name);
                panel.Controls.Add(value);
            }

            return MakeCard("Флаги", panel);
        }

        private Control BuildMemoryCard()
        {
            return MakeCard("Память (CS:IP)", _memoryView);
        }

        private Control BuildOutputCard()
        {
            return MakeCard("Вывод программы (DOS)", _consoleView);
        }

        // ------------------------------------------------------------------

        private void LoadSample()
        {
            _editor.Code = SampleProgram;
            AssembleProgram();
        }

        private const string SampleProgram = @"; Пример для Intel 8086
        org 100h

start:
        mov     ax, [A_IN]       ; AX = A
        imul    word [B_IN]      ; DX:AX = A * B
        add     ax, 3            ; DX:AX = A*B + 3
        adc     dx, 0            ;   с учётом переноса
        idiv    word [D_IN]       ; AX = частное, DX = остаток
        mov     cx, [A_IN]
        sub     ax, cx           ; F = частное - A
        mov     [F_RES], ax      ; ответ в AX

        mov     dx, T1
        mov     ah, 09h
        int     21h
        mov     ax, [F_RES]
        add     ax, 30h
        mov     [T2], ax
        mov     dx, T2
        mov     ah, 09h
        int     21h

        mov     ax, 4C00h
        int     21h

A_IN:   dw      5
B_IN:   dw      7
D_IN:   dw      3
F_RES:  dw      0
T1:     db      'F = ', '$'
T2:     db      0, 0, '$'
";

        public void AssembleProgram()
        {
            _consoleView.Clear();
            _editor.SetErrorLine(null);

            string source = _editor.Code;
            string directory = _filePath != null ? Path.GetDirectoryName(Path.GetFullPath(_filePath)) : null;
            I8086AssembleResult result = I8086Assembler.Assemble(source, ProgramOrigin, directory);

            if (!result.Success)
            {
                foreach (AssemblerError error in result.Errors)
                    _consoleView.AppendText("Строка " + error.LineNumber + ": " + error.Message + Environment.NewLine);
                if (result.Errors.Count > 0)
                    _editor.SetErrorLine(result.Errors[0].LineNumber);
                _statusLabel.Text = "Ошибок: " + result.Errors.Count;
                return;
            }

            _assembly = result;
            _memory = new I8086Memory();
            _console = new I8086Console();
            _cpu = new I8086Cpu(_memory, _console);
            _cpu.LoadProgram(result.Image, 0x0000, result.Origin);

            _consoleView.AppendText("Собрано: " + result.Image.Length + " байт, начало " +
                result.Origin.ToString("X4") + "h" + Environment.NewLine);
            UpdateBreakpointAddresses();
            _statusLabel.Text = "Собрано: " + result.Image.Length + " байт · точек: " + _breakpoints.Count;
            RefreshPanels();
        }

        private void RunProgram()
        {
            if (_assembly == null)
            {
                AssembleProgram();
                if (_assembly == null)
                    return;
            }

            int executed = 0;
            bool stopped = false;
            ushort stopAddress = 0;

            while (executed < MaxSteps)
            {
                ushort pc = _cpu.State.IP;
                if (pc == _skipBreakpoint)
                {
                    // продолжение с места останова: текущую точку пропускаем один раз
                    _skipBreakpoint = -1;
                }
                else if (_breakpoints.Contains(pc))
                {
                    stopped = true;
                    stopAddress = pc;
                    break;
                }

                if (!_cpu.Step())
                    break;
                executed++;
            }

            if (stopped)
            {
                _skipBreakpoint = stopAddress;
                _consoleView.AppendText("Останов на точке останова: " +
                    stopAddress.ToString("X4") + "h" + Environment.NewLine);
            }

            _consoleView.AppendText("Выполнено команд: " + executed + Environment.NewLine);
            string text = _cpu.Console.Output;
            if (!string.IsNullOrEmpty(text))
                _consoleView.AppendText(text + Environment.NewLine);

            RefreshPanels();
        }

        private void StepOnce()
        {
            if (_assembly == null)
            {
                AssembleProgram();
                return;
            }

            _cpu.Step();
            RefreshPanels();
        }

        /// <summary>Marks or unmarks a breakpoint on the line of the caret.</summary>
        private void ToggleCaretBreakpoint()
        {
            ToggleBreakpoint(_editor.CaretLine);
        }

        /// <summary>Marks or unmarks a breakpoint on an editor line.</summary>
        private void ToggleBreakpoint(int line)
        {
            bool added = _breakpoints.ToggleLine(line);
            _editor.SetBreakpointLines(_breakpoints.Lines);
            UpdateBreakpointAddresses();
            _statusLabel.Text = "Собрано: " + (_assembly != null ? _assembly.Image.Length : 0) +
                " байт · точек: " + _breakpoints.Count;
            string note = added ? "Точка останова · строка " : "Точка останова снята · строка ";
            if (added && !_breakpoints.HasCode(line))
                note = "Строка без машинного кода · точка останова не сработает · строка ";

            _consoleView.AppendText(note + line + Environment.NewLine);
        }

        /// <summary>Maps the marked lines to the addresses of the last assembly.</summary>
        private void UpdateBreakpointAddresses()
        {
            _breakpoints.MapAddresses(line =>
            {
                if (_assembly == null)
                    return null;

                foreach (I8086ListingLine item in _assembly.Listing)
                {
                    if (item.HasAddress && item.LineNumber == line)
                        return (ulong)item.Offset;
                }

                return null;
            });
        }

        private void ResetMachine()
        {
            _skipBreakpoint = -1;
            if (_assembly != null)
            {
                _memory = new I8086Memory();
                _console = new I8086Console();
                _cpu = new I8086Cpu(_memory, _console);
                _cpu.LoadProgram(_assembly.Image, 0x0000, _assembly.Origin);
            }

            _consoleView.Clear();
            RefreshPanels();
        }

        private void OpenFile()
        {
            string path = _files.OpenFile(new FileDialogRequest
            {
                Title = "Открыть программу i8086",
                Filter = "Исходный текст (*.asm)|*.asm;*.ASM;Все файлы (*.*)|*.*",
                CheckFileExists = true
            });

            if (string.IsNullOrEmpty(path))
                return;

            HostLoadFile(path);
        }

        /// <summary>Loads a source file into the editor and assembles it.</summary>
        public void HostLoadFile(string path)
        {
            try
            {
                _editor.Code = File.ReadAllText(path);
                _filePath = path;
                AssembleProgram();
            }
            catch (Exception ex)
            {
                _consoleView.AppendText("Не удалось открыть файл: " + ex.Message + Environment.NewLine);
            }
        }

        /// <summary>Runs the assembled program to completion.</summary>
        public void HostRun()
        {
            RunProgram();
        }

        private void SaveFile()
        {
            string path = _files.SaveFile(new FileDialogRequest
            {
                Title = "Сохранить программу i8086",
                Filter = "Исходный текст (*.asm)|*.asm",
                DefaultFileName = _filePath != null ? Path.GetFileName(_filePath) : "program.asm",
                CheckFileExists = false
            });

            if (string.IsNullOrEmpty(path))
                return;

            try
            {
                File.WriteAllText(path, _editor.Code);
                _filePath = path;
                _consoleView.AppendText("Сохранено: " + path + Environment.NewLine);
            }
            catch (Exception ex)
            {
                _consoleView.AppendText("Не удалось сохранить файл: " + ex.Message + Environment.NewLine);
            }
        }

        // ------------------------------------------------------------------

        /// <summary>
        /// Shows the machine code of the assembled program: the address, the
        /// bytes of every instruction and the source line.
        /// </summary>
        private void ShowDisassembly()
        {
            if (_assembly == null)
            {
                AssembleProgram();
                if (_assembly == null)
                    return;
            }

            StringBuilder builder = new StringBuilder();
            builder.AppendLine();
            builder.AppendLine("Дизассемблирование:");
            foreach (I8086ListingLine line in _assembly.Listing)
            {
                if (!line.HasAddress || line.Bytes.Length == 0)
                    continue;
                builder.Append(line.Offset.ToString("X4")).Append("h  ");
                foreach (byte value in line.Bytes)
                    builder.Append(value.ToString("X2")).Append(' ');
                builder.Append(' ').Append(line.Source.Trim()).AppendLine();
            }

            _consoleView.AppendText(builder.ToString());
        }

        /// <summary>
        /// Decodes the machine code of the assembled program back into
        /// instructions — the same job the disassembler of the laboratory bench
        /// does.
        /// </summary>
        private void ShowMachineCode()
        {
            if (_assembly == null)
            {
                AssembleProgram();
                if (_assembly == null)
                    return;
            }

            StringBuilder builder = new StringBuilder();
            builder.AppendLine();
            builder.AppendLine("Машинный код (сплошной разбор):");
            byte[] image = _assembly.Image;
            int offset = 0;
            while (offset < image.Length)
            {
                DecodedInstruction instruction = I8086Disassembler.Decode(
                    image, offset, (ushort)(_assembly.Origin + offset));
                builder.Append(instruction.Address.ToString("X4")).Append("h  ");
                builder.Append(instruction.BytesText).Append(' ');
                builder.Append(instruction.Text).AppendLine();
                offset += Math.Max(1, instruction.Length);
            }

            _consoleView.AppendText(builder.ToString());
        }

        private void RefreshPanels()
        {
            I8086CpuState state = _cpu.State;
            ushort[] values =
            {
                state.AX, state.BX, state.CX, state.DX, state.SI, state.DI, state.BP, state.SP,
                state.CS, state.DS, state.ES, state.SS, state.IP, state.Flags
            };

            for (int i = 0; i < values.Length; i++)
                _registerValues[i].Text = values[i].ToString("X4") + "h";

            bool[] flags =
            {
                state.Carry, state.Parity, state.Auxiliary, state.Zero,
                state.Sign, state.Direction, state.Overflow
            };

            for (int i = 0; i < flags.Length; i++)
            {
                _flagValues[i].Text = flags[i] ? "1" : "0";
                _flagValues[i].ForeColor = flags[i] ? AppTheme.Green : AppTheme.TextDim;
            }

            _memoryView.Text = BuildMemoryDump();
        }

        private string BuildMemoryDump()
        {
            I8086CpuState state = _cpu.State;
            int linear = (state.CS << 4) + state.IP;
            int start = linear - 16;
            if (start < 0)
                start = 0;

            StringBuilder builder = new StringBuilder();
            for (int row = 0; row < 12; row++)
            {
                int address = start + row * 16;
                builder.Append((address & 0xFFFF).ToString("X4")).Append('h');
                for (int i = 0; i < 16; i++)
                    builder.Append(' ').Append(_memory.ReadByte(address + i).ToString("X2"));
                builder.Append("  ");
                for (int i = 0; i < 16; i++)
                {
                    byte value = _memory.ReadByte(address + i);
                    builder.Append(value >= 32 && value < 127 ? (char)value : '.');
                }

                builder.Append("\r\n");
            }

            return builder.ToString();
        }
    }
}
