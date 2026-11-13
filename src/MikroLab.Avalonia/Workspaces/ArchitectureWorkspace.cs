using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MikroLab.Core.Assembler;
using MikroLab.Core.Assembler.I8086;
using MikroLab.Core.Assembler.X64;
using MikroLab.Core.Cpu.I8086;
using MikroLab.Core.Cpu.X64;
using MikroLab.Core.Debugging;
using MikroLab.Core.Memory;
using MikroLab.Studio.Controls;
using MikroLab.Presentation.Services;
using MikroLab.Studio.Services;

namespace MikroLab.Studio.Workspaces
{
    /// <summary>Architecture served by the workspace.</summary>
    internal enum ArchitectureKind
    {
        I8086,
        X64
    }

    /// <summary>
    /// Workspace for the 16-bit architectures: the Intel 8086 and the
    /// x86-64 in long mode. The layout mirrors the WinForms studio: an action
    /// bar and the source editor on the left, the processor state, the memory
    /// dump and the program output on the right.
    /// </summary>
    internal sealed class ArchitectureWorkspace : UserControl
    {
        private const int MaxSteps = 1000000;

        private static readonly string[] Register8Names = { "AL", "CL", "DL", "BL" };
        private static readonly string[] Register16Names = { "AX", "CX", "DX", "BX", "SP", "BP", "SI", "DI" };
        private static readonly string[] Register32Names = { "EAX", "ECX", "EDX", "EBX" };
        private static readonly string[] Register64Names =
        {
            "RAX", "RCX", "RDX", "RBX", "RSP", "RBP", "RSI", "RDI",
            "R8", "R9", "R10", "R11", "R12", "R13", "R14", "R15"
        };

        private readonly ArchitectureKind _kind;
        private readonly IAvaloniaFileService _files;
        private readonly CodeEditor _editor;
        private readonly TextBox _consoleView;
        private readonly TextBox _consoleMain;
        private readonly TextBox _memoryView;
        private readonly TextBlock _status;
        private readonly List<TextBlock> _registerValues = new List<TextBlock>();
        private readonly List<TextBlock> _flagValues = new List<TextBlock>();
        private readonly List<TextBlock> _segmentValues = new List<TextBlock>();

        private I8086Memory _memory = new I8086Memory();
        private X64Memory _memory64 = new X64Memory();
        private I8086Console _console = new I8086Console();
        private I8086Cpu _cpu;
        private X64Cpu _cpu64;
        private I8086AssembleResult _assembly;
        private X64AssembleResult _assembly64;
        private string _filePath;

        public ArchitectureWorkspace(ArchitectureKind kind, IAvaloniaFileService files)
        {
            _kind = kind;
            _files = files ?? throw new ArgumentNullException(nameof(files));
            Background = AppTheme.Brush(AppTheme.Window);

            _cpu = new I8086Cpu(_memory, _console);
            _cpu64 = new X64Cpu(_memory64, _console);

            _editor = new CodeEditor();
            _consoleView = MakeReadOnlyBox();
            _consoleMain = MakeReadOnlyBox();
            _memoryView = MakeReadOnlyBox();
            _status = AppTheme.Label("Готово", AppTheme.TextDim, AppTheme.SmallSize);

            var root = new Grid
            {
                ColumnDefinitions = ColumnDefinitions.Parse("*,560"),
                ColumnSpacing = 12,
                Margin = new Thickness(12, 10, 12, 10)
            };

            var left = new Grid
            {
                RowDefinitions = RowDefinitions.Parse("44,*,150")
            };

            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6
            };
            actions.Children.Add(MakeButton("Открыть", OpenFileAsync));
            actions.Children.Add(MakeButton("Сохранить", SaveFileAsync));
            actions.Children.Add(MakeButton("Собрать", AssembleProgram));
            actions.Children.Add(MakeButton("Выполнить", RunProgram));
            actions.Children.Add(MakeButton("Шаг", StepOnce));
            actions.Children.Add(MakeButton("Сброс", ResetMachine));
            actions.Children.Add(MakeButton("Дизассемблировать", ShowDisassembly));
            actions.Children.Add(MakeButton("Код → мнемоника", ShowMachineCode));
            actions.Children.Add(_status);
            _status.VerticalAlignment = VerticalAlignment.Center;
            _status.Margin = new Thickness(12, 0, 0, 0);

            left.Children.Add(actions);
            Grid.SetRow(actions, 0);
            left.Children.Add(_editor);
            Grid.SetRow(_editor, 1);
            left.Children.Add(MakeCard("Вывод программы (DOS)", _consoleMain));
            Grid.SetRow(left.Children[^1], 2);

            root.Children.Add(left);
            Grid.SetColumn(left, 0);
            root.Children.Add(BuildInspector());
            Grid.SetColumn(root.Children[^1], 1);

            Content = root;

            _editor.Code = SampleProgram;
            AssembleProgram();
        }

        private void AppendConsole(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;
            _consoleView.Text += text;
            _consoleMain.Text += text;
        }

        private void ClearConsole()
        {
            _consoleView.Text = string.Empty;
            _consoleMain.Text = string.Empty;
        }

        private TextBox MakeReadOnlyBox()
        {
            return new TextBox
            {
                IsReadOnly = true,
                AcceptsReturn = true,
                Background = AppTheme.Brush(AppTheme.EditorBackground),
                Foreground = AppTheme.Brush(AppTheme.Text),
                BorderThickness = new Thickness(0),
                FontFamily = AppTheme.MonoFamily,
                FontSize = AppTheme.MonoSize,
                TextWrapping = TextWrapping.NoWrap
            };
        }

        private UiButton MakeButton(string text, Action action)
        {
            var button = new UiButton
            {
                Text = text,
                Kind = UiButtonKind.Ghost,
                Width = 92,
                Height = 30,
                FontSize = AppTheme.SmallSize,
                VerticalAlignment = VerticalAlignment.Center
            };
            button.Click += action;
            return button;
        }

        private UiButton MakeButton(string text, Func<Task> action)
        {
            var button = MakeButton(text, () => { });
            button.Click += () => _ = action();
            return button;
        }

        private static Control MakeCard(string title, Control content)
        {
            var panel = new StackPanel
            {
                Spacing = 4,
                Background = AppTheme.Brush(AppTheme.Surface)
            };
            panel.Children.Add(AppTheme.Label(title, AppTheme.TextMuted, AppTheme.SmallSize));
            panel.Children.Add(content);
            return panel;
        }

        private Control BuildInspector()
        {
            var root = new Grid
            {
                RowDefinitions = RowDefinitions.Parse("236,86,*,150"),
                RowSpacing = 10
            };

            var registerGrid = new Grid
            {
                ColumnDefinitions = ColumnDefinitions.Parse("34,100,34,100,34,100,34,100"),
                RowDefinitions = RowDefinitions.Parse("*,*,*,*,*")
            };

            string[] names = _kind == ArchitectureKind.X64 ? Register64Names : Register16Names;
            const int perRow = 4;                       // четыре пары «имя — значение» в строке
            for (int i = 0; i < names.Length; i++)
            {
                int row = i / perRow;
                int column = (i % perRow) * 2;

                var name = AppTheme.Label(names[i], AppTheme.TextDim, AppTheme.SmallSize);
                name.Width = 34;
                name.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
                var value = AppTheme.Label("0000h", AppTheme.Text, AppTheme.MonoSize - 1, mono: true);
                _registerValues.Add(value);

                registerGrid.Children.Add(name);
                Grid.SetColumn(name, column);
                Grid.SetRow(name, row);
                registerGrid.Children.Add(value);
                Grid.SetColumn(value, column + 1);
                Grid.SetRow(value, row);
            }

            string[] special = _kind == ArchitectureKind.X64 ? new[] { "RIP", "RFLAGS" } : new[] { "IP", "FLAGS" };
            for (int i = 0; i < special.Length; i++)
            {
                var name = AppTheme.Label(special[i], AppTheme.TextDim, AppTheme.SmallSize);
                var value = AppTheme.Label("0000h", AppTheme.Text, AppTheme.MonoSize - 1, mono: true);
                _registerValues.Add(value);

                registerGrid.Children.Add(name);
                Grid.SetColumn(name, i * 2);
                Grid.SetRow(name, 4);
                registerGrid.Children.Add(value);
                Grid.SetColumn(value, i * 2 + 1);
                Grid.SetRow(value, 4);
            }

            root.Children.Add(MakeCard(_kind == ArchitectureKind.X64
                ? "Регистры Intel x86-64" : "Регистры Intel 8086", registerGrid));
            Grid.SetRow(root.Children[^1], 0);

            var segmentPanel = new StackPanel
            {
                Spacing = 4,
                Orientation = Orientation.Horizontal
            };
            string[] segments = _kind == ArchitectureKind.X64
                ? new[] { "CS", "DS", "ES", "FS", "GS", "SS" }
                : new[] { "CS", "DS", "ES", "SS" };
            for (int i = 0; i < segments.Length; i++)
            {
                var name = AppTheme.Label(segments[i], AppTheme.TextDim, AppTheme.SmallSize);
                var value = AppTheme.Label("0000h", AppTheme.Text, AppTheme.MonoSize - 1, mono: true);
                _segmentValues.Add(value);
                segmentPanel.Children.Add(name);
                segmentPanel.Children.Add(value);
            }

            string[] flags = { "CF", "PF", "AF", "ZF", "SF", "DF", "OF" };
            for (int i = 0; i < flags.Length; i++)
            {
                var name = AppTheme.Label(flags[i], AppTheme.TextDim, AppTheme.SmallSize);
                var value = AppTheme.Label("0", AppTheme.TextMuted, AppTheme.MonoSize, mono: true);
                _flagValues.Add(value);
                segmentPanel.Children.Add(name);
                segmentPanel.Children.Add(value);
            }

            root.Children.Add(MakeCard("Сегментные регистры · флаги", segmentPanel));
            Grid.SetRow(root.Children[^1], 1);
            root.Children.Add(MakeCard("Память", _memoryView));
            Grid.SetRow(root.Children[^1], 2);
            root.Children.Add(MakeCard("Вывод программы (DOS)", _consoleView));
            Grid.SetRow(root.Children[^1], 3);
            return root;
        }

        // ------------------------------------------------------------------
        // sample program
        // ------------------------------------------------------------------

        private string SampleProgram
        {
            get
            {
                if (_kind == ArchitectureKind.X64)
                {
                    return @"; Пример для Intel x86-64 (long mode)
        bits 64
        default rel

start:
        mov     rax, 5
        mov     rbx, 7
        add     rax, rbx          ; RAX = 12
        imul    rax, rbx          ; RAX = 84
        mov     rcx, rax
        lea     rdx, [rel message]
        mov     ah, 09h
        int     21h               ; вывод строки через DOS
        mov     ax, 4C00h
        int     21h               ; завершение программы

message: db      'RAX = 84', '$'
";
                }

                return @"; Пример для Intel 8086
        org 100h

start:
        mov     ax, [A_IN]
        imul    word [B_IN]
        add     ax, 3
        adc     dx, 0
        idiv    word [D_IN]
        mov     cx, [A_IN]
        sub     ax, cx
        mov     [F_RES], ax
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
            }
        }

        // ------------------------------------------------------------------
        // commands
        // ------------------------------------------------------------------

        private void AssembleProgram()
        {
            ClearConsole();
            _editor.SetErrorLine(null);

            string source = _editor.Code;
            string directory = _filePath != null ? Path.GetDirectoryName(Path.GetFullPath(_filePath)) : null;

            if (_kind == ArchitectureKind.X64)
            {
                X64AssembleResult result = X64Assembler.Assemble(source, 0x00400000, directory);
                foreach (AssemblerError error in result.Errors)
                    AppendConsole("Строка " + error.LineNumber + ": " + error.Message + Environment.NewLine);
                if (!result.Success)
                {
                    if (result.Errors.Count > 0)
                        _editor.SetErrorLine(result.Errors[0].LineNumber);
                    _status.Text = "Ошибок: " + result.Errors.Count;
                    return;
                }

                _assembly64 = result;
                _memory64 = new X64Memory();
                _console = new I8086Console();
                _cpu64 = new X64Cpu(_memory64, _console);
                _cpu64.LoadProgram(result.Image, result.Origin);
                _status.Text = "Собрано: " + result.Image.Length + " байт";
            }
            else
            {
                I8086AssembleResult result = I8086Assembler.Assemble(source, 0x0100, directory);
                foreach (AssemblerError error in result.Errors)
                    AppendConsole("Строка " + error.LineNumber + ": " + error.Message + Environment.NewLine);
                if (!result.Success)
                {
                    if (result.Errors.Count > 0)
                        _editor.SetErrorLine(result.Errors[0].LineNumber);
                    _status.Text = "Ошибок: " + result.Errors.Count;
                    return;
                }

                _assembly = result;
                _memory = new I8086Memory();
                _console = new I8086Console();
                _cpu = new I8086Cpu(_memory, _console);
                _cpu.LoadProgram(result.Image, 0x0000, result.Origin);
                _status.Text = "Собрано: " + result.Image.Length + " байт";
            }

            RefreshPanels();
        }

        private void RunProgram()
        {
            AssembleProgram();

            int executed = 0;
            if (_kind == ArchitectureKind.X64)
            {
                while (executed < MaxSteps && _cpu64.Step())
                    executed++;
                string text = _cpu64.Console.Output;
                if (!string.IsNullOrEmpty(text))
                    AppendConsole(text + Environment.NewLine);
                if (!string.IsNullOrEmpty(_cpu64.Fault))
                    AppendConsole("Ошибка: " + _cpu64.Fault + Environment.NewLine);
            }
            else
            {
                while (executed < MaxSteps && _cpu.Step())
                    executed++;
                string text = _cpu.Console.Output;
                if (!string.IsNullOrEmpty(text))
                    AppendConsole(text + Environment.NewLine);
            }

            AppendConsole("Выполнено команд: " + executed + Environment.NewLine);
            RefreshPanels();
        }

        private void StepOnce()
        {
            AssembleProgram();
            if (_kind == ArchitectureKind.X64)
                _cpu64.Step();
            else
                _cpu.Step();
            RefreshPanels();
        }

        private void ResetMachine()
        {
            ClearConsole();
            AssembleProgram();
        }

        private async Task OpenFileAsync()
        {
            string path = await _files.OpenFileAsync(new FileDialogRequest
            {
                Title = "Открыть программу",
                Filter = "Исходный текст (*.asm)|*.asm;*.ASM;Все файлы (*.*)|*.*",
                CheckFileExists = true
            });

            if (string.IsNullOrEmpty(path))
                return;

            try
            {
                _editor.Code = File.ReadAllText(path);
                _filePath = path;
                AssembleProgram();
            }
            catch (Exception failure)
            {
                _consoleView.Text += "Не удалось открыть файл: " + failure.Message + Environment.NewLine;
            }
        }

        private async Task SaveFileAsync()
        {
            string path = await _files.SaveFileAsync(new FileDialogRequest
            {
                Title = "Сохранить программу",
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
                AppendConsole("Сохранено: " + path + Environment.NewLine);
            }
            catch (Exception failure)
            {
                _consoleView.Text += "Не удалось сохранить файл: " + failure.Message + Environment.NewLine;
            }
        }

        /// <summary>
        /// Decodes the machine code of the assembled program back into
        /// instructions.
        /// </summary>
        private void ShowMachineCode()
        {
            AssembleProgram();

            StringBuilder builder = new StringBuilder();
            builder.Append(Environment.NewLine).AppendLine("Машинный код (сплошной разбор):");

            if (_kind == ArchitectureKind.X64)
            {
                if (_assembly64 == null)
                    return;

                byte[] image = _assembly64.Image;
                int offset = 0;
                while (offset < image.Length)
                {
                    DecodedInstruction instruction = X64Disassembler.Decode(
                        image, offset, _assembly64.Origin + (ulong)offset);
                    builder.Append(instruction.Address.ToString("X8")).Append("h  ");
                    builder.Append(instruction.BytesText).Append(' ');
                    builder.Append(instruction.Text).Append(Environment.NewLine);
                    offset += Math.Max(1, instruction.Length);
                }
            }
            else
            {
                if (_assembly == null)
                    return;

                byte[] image = _assembly.Image;
                int offset = 0;
                while (offset < image.Length)
                {
                    DecodedInstruction instruction = I8086Disassembler.Decode(
                        image, offset, (ushort)(_assembly.Origin + offset));
                    builder.Append(instruction.Address.ToString("X4")).Append("h  ");
                    builder.Append(instruction.BytesText).Append(' ');
                    builder.Append(instruction.Text).Append(Environment.NewLine);
                    offset += Math.Max(1, instruction.Length);
                }
            }

            AppendConsole(builder.ToString());
        }

        private void ShowDisassembly()
        {
            AssembleProgram();

            StringBuilder builder = new StringBuilder();
            if (_kind == ArchitectureKind.X64)
            {
                if (_assembly64 == null)
                    return;
                foreach (X64ListingLine line in _assembly64.Listing)
                {
                    if (!line.HasAddress || line.Bytes.Length == 0)
                        continue;
                    builder.Append(line.Offset.ToString("X8")).Append("h  ");
                    foreach (byte b in line.Bytes)
                        builder.Append(b.ToString("X2")).Append(' ');
                    builder.Append(' ').Append(line.Source.Trim()).Append(Environment.NewLine);
                }
            }
            else
            {
                if (_assembly == null)
                    return;
                foreach (I8086ListingLine line in _assembly.Listing)
                {
                    if (!line.HasAddress || line.Bytes.Length == 0)
                        continue;
                    builder.Append(line.Offset.ToString("X4")).Append("h  ");
                    foreach (byte b in line.Bytes)
                        builder.Append(b.ToString("X2")).Append(' ');
                    builder.Append(' ').Append(line.Source.Trim()).Append(Environment.NewLine);
                }
            }

            AppendConsole(Environment.NewLine + "Дизассемблирование:" + Environment.NewLine + builder);
        }

        // ------------------------------------------------------------------

        private void RefreshPanels()
        {
            if (_kind == ArchitectureKind.X64)
            {
                X64CpuState state = _cpu64.State;
                for (int i = 0; i < 16; i++)
                    _registerValues[i].Text = state.GetGpr(i).ToString("X") + "h";
                _registerValues[16].Text = state.RIP.ToString("X") + "h";
                _registerValues[17].Text = state.Flags.ToString("X4") + "h";

                ushort[] segments = { state.CS, state.DS, state.ES, state.FS, state.GS, state.SS };
                for (int i = 0; i < segments.Length; i++)
                    _segmentValues[i].Text = segments[i].ToString("X4") + "h";

                bool[] flags =
                {
                    state.Carry, state.Parity, state.Auxiliary, state.Zero,
                    state.Sign, state.Direction, state.Overflow
                };
                for (int i = 0; i < flags.Length; i++)
                {
                    _flagValues[i].Text = flags[i] ? "1" : "0";
                    _flagValues[i].Foreground = AppTheme.Brush(flags[i] ? AppTheme.Green : AppTheme.TextDim);
                }

                _memoryView.Text = BuildMemoryDump64(state.RIP);
            }
            else
            {
                I8086CpuState state = _cpu.State;
                ushort[] registers =
                {
                    state.AX, state.CX, state.DX, state.BX,
                    state.SP, state.BP, state.SI, state.DI
                };
                for (int i = 0; i < 8; i++)
                    _registerValues[i].Text = registers[i].ToString("X") + "h";
                _registerValues[8].Text = state.IP.ToString("X") + "h";
                _registerValues[9].Text = state.Flags.ToString("X4") + "h";

                ushort[] segments = { state.CS, state.DS, state.ES, state.SS };
                for (int i = 0; i < segments.Length; i++)
                    _segmentValues[i].Text = segments[i].ToString("X4") + "h";

                bool[] flags =
                {
                    state.Carry, state.Parity, state.Auxiliary, state.Zero,
                    state.Sign, state.Direction, state.Overflow
                };
                for (int i = 0; i < flags.Length; i++)
                {
                    _flagValues[i].Text = flags[i] ? "1" : "0";
                    _flagValues[i].Foreground = AppTheme.Brush(flags[i] ? AppTheme.Green : AppTheme.TextDim);
                }

                _memoryView.Text = BuildMemoryDump16(state.IP);
            }
        }

        private string BuildMemoryDump16(int ip)
        {
            StringBuilder builder = new StringBuilder();
            int start = Math.Max(0, ip - 16);
            for (int row = 0; row < 12; row++)
            {
                int address = start + row * 16;
                builder.Append(address.ToString("X4")).Append('h');
                for (int i = 0; i < 16; i++)
                    builder.Append(' ').Append(_memory.ReadByte((ushort)(address + i)).ToString("X2"));
                builder.Append("  ");
                for (int i = 0; i < 16; i++)
                {
                    byte value = _memory.ReadByte((ushort)(address + i));
                    builder.Append(value >= 32 && value < 127 ? (char)value : '.');
                }

                builder.Append(Environment.NewLine);
            }

            return builder.ToString();
        }

        private string BuildMemoryDump64(ulong rip)
        {
            StringBuilder builder = new StringBuilder();
            int start = (int)Math.Max(0UL, rip - 16);
            for (int row = 0; row < 12; row++)
            {
                int address = start + row * 16;
                builder.Append(address.ToString("X8")).Append('h');
                for (int i = 0; i < 16; i++)
                    builder.Append(' ').Append(_memory64.ReadByte((ulong)(address + i)).ToString("X2"));
                builder.Append("  ");
                for (int i = 0; i < 16; i++)
                {
                    byte value = _memory64.ReadByte((ulong)(address + i));
                    builder.Append(value >= 32 && value < 127 ? (char)value : '.');
                }

                builder.Append(Environment.NewLine);
            }

            return builder.ToString();
        }
    }
}
