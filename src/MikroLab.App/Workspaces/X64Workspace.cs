using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using MikroLab.App.Controls;
using MikroLab.Core.Assembler;
using MikroLab.Core.Assembler.X64;
using MikroLab.Core.Cpu.I8086;
using MikroLab.Core.Cpu.X64;
using MikroLab.Core.Debugging;
using MikroLab.Core.Memory;
using MikroLab.Presentation.Services;

namespace MikroLab.App.Workspaces
{
    /// <summary>
    /// Workspace for the x86-64 architecture: a source editor, the processor
    /// state, a memory dump and the output of the emulated DOS services.
    /// </summary>
    internal sealed class X64Workspace : UserControl
    {
        private const ulong ProgramOrigin = 0x0040_0000;
        private const int MaxSteps = 1000000;

        // порядок совпадает с индексами регистров процессора:
        // RAX, RCX, RDX, RBX, RSP, RBP, RSI, RDI, R8…R15
        private static readonly string[] RegisterNames =
        {
            "RAX", "RCX", "RDX", "RBX", "RSP", "RBP", "RSI", "RDI",
            "R8", "R9", "R10", "R11", "R12", "R13", "R14", "R15"
        };

        private static readonly string[] SegmentNames = { "CS", "DS", "ES", "FS", "GS", "SS" };

        private readonly IFileDialogService _files;
        private readonly CodeEditor _editor;
        private readonly TextBox _consoleView;
        private readonly Label[] _registerValues = new Label[RegisterNames.Length];
        private readonly Label[] _specialValues = new Label[2];      // RIP, RFLAGS
        private readonly Label[] _segmentValues = new Label[SegmentNames.Length];
        private readonly Label[] _flagValues = new Label[7];
        private readonly TextBox _memoryView;
        private readonly Label _statusLabel;
        private readonly SplitContainer _split;

        private X64Memory _memory = new X64Memory();
        private I8086Console _console = new I8086Console();
        private X64Cpu _cpu;
        private X64AssembleResult _assembly;
        private string _filePath;

        public X64Workspace(IFileDialogService files)
        {
            _files = files;
            _cpu = new X64Cpu(_memory, _console);

            Dock = DockStyle.Fill;
            BackColor = AppTheme.Window;

            _editor = new CodeEditor { Dock = DockStyle.Fill };
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

            Control toolbar = BuildToolbar();
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
            int right = 300;
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

            UiButton open = Make("Открыть", OpenFile, 92);
            UiButton save = Make("Сохранить", SaveFile, 100);
            UiButton assemble = Make("Собрать", AssembleProgram, 92);
            UiButton run = Make("Выполнить", RunProgram, 104);
            UiButton step = Make("Шаг", StepOnce, 74);
            UiButton reset = Make("Сброс", ResetMachine, 74);
            UiButton disasm = Make("Дизассемблировать", ShowDisassembly, 132);
            UiButton code = Make("Код → мнемоника", ShowMachineCode, 128);

            int x = 12;
            foreach (UiButton button in new[] { open, save, assemble, run, step, reset, disasm, code })
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
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 236));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));

            root.Controls.Add(BuildRegisterCard(), 0, 0);
            root.Controls.Add(BuildSegmentCard(), 0, 1);
            root.Controls.Add(MakeCard("Память (RIP)", _memoryView), 0, 2);
            root.Controls.Add(MakeCard("Вывод программы (DOS)", _consoleView), 0, 3);
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
                ColumnCount = 8,
                RowCount = 5,
                BackColor = Color.Transparent,
                Padding = new Padding(6, 0, 6, 4)
            };
            for (int i = 0; i < 8; i++)
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 12.5f));

            for (int i = 0; i < RegisterNames.Length; i++)
            {
                int row = i / 4;
                int column = (i % 4) * 2;

                var name = AppTheme.Label(RegisterNames[i], AppTheme.TextDim, AppTheme.SmallFont);
                name.AutoSize = false;
                name.Dock = DockStyle.Fill;
                name.TextAlign = ContentAlignment.MiddleLeft;

                var value = AppTheme.Label("0000000000000000h", AppTheme.Text, AppTheme.MonoSmall);
                value.AutoSize = false;
                value.Dock = DockStyle.Fill;
                value.TextAlign = ContentAlignment.MiddleLeft;
                _registerValues[i] = value;

                grid.Controls.Add(name, column, row);
                grid.Controls.Add(value, column + 1, row);
            }

            string[] special = { "RIP", "RFLAGS" };
            for (int i = 0; i < special.Length; i++)
            {
                var name = AppTheme.Label(special[i], AppTheme.TextDim, AppTheme.SmallFont);
                name.AutoSize = false;
                name.Dock = DockStyle.Fill;
                name.TextAlign = ContentAlignment.MiddleLeft;

                var value = AppTheme.Label("0000h", AppTheme.Text, AppTheme.MonoSmall);
                value.AutoSize = false;
                value.Dock = DockStyle.Fill;
                value.TextAlign = ContentAlignment.MiddleLeft;
                _specialValues[i] = value;

                grid.Controls.Add(name, i * 2, 4);
                grid.Controls.Add(value, i * 2 + 1, 4);
            }

            return MakeCard("Регистры Intel x86-64", grid);
        }

        private Control BuildSegmentCard()
        {
            var panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
                Padding = new Padding(6, 0, 0, 0)
            };

            for (int i = 0; i < SegmentNames.Length; i++)
            {
                var name = AppTheme.Label(SegmentNames[i], AppTheme.TextDim, AppTheme.SmallFont);
                name.AutoSize = true;
                name.Margin = new Padding(2, 6, 2, 2);

                var value = AppTheme.Label("0000h", AppTheme.Text, AppTheme.MonoSmall);
                value.AutoSize = true;
                value.Margin = new Padding(0, 6, 10, 2);
                _segmentValues[i] = value;

                panel.Controls.Add(name);
                panel.Controls.Add(value);
            }

            panel.Controls.Add(new Panel { Width = 16 });

            string[] flags = { "CF", "PF", "AF", "ZF", "SF", "DF", "OF" };
            for (int i = 0; i < flags.Length; i++)
            {
                var name = AppTheme.Label(flags[i], AppTheme.TextDim, AppTheme.SmallFont);
                name.AutoSize = true;
                name.Margin = new Padding(2, 6, 2, 2);

                var value = AppTheme.Label("0", AppTheme.TextMuted, AppTheme.MonoSmall);
                value.AutoSize = true;
                value.Margin = new Padding(0, 6, 8, 2);
                _flagValues[i] = value;

                panel.Controls.Add(name);
                panel.Controls.Add(value);
            }

            return MakeCard("Сегментные регистры · флаги", panel);
        }

        // ------------------------------------------------------------------

        private void LoadSample()
        {
            _editor.Code = SampleProgram;
            AssembleProgram();
        }

        private const string SampleProgram = @"; Пример для Intel x86-64 (long mode)
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

message: db      'RAX = ', '$'
";

        public void AssembleProgram()
        {
            _consoleView.Clear();
            _editor.SetErrorLine(null);

            string source = _editor.Code;
            string directory = _filePath != null ? Path.GetDirectoryName(Path.GetFullPath(_filePath)) : null;
            X64AssembleResult result = X64Assembler.Assemble(source, ProgramOrigin, directory);

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
            _memory = new X64Memory();
            _console = new I8086Console();
            _cpu = new X64Cpu(_memory, _console);
            _cpu.LoadProgram(result.Image, result.Origin);

            _consoleView.AppendText("Собрано: " + result.Image.Length + " байт, начало " +
                result.Origin.ToString("X") + "h" + Environment.NewLine);
            _statusLabel.Text = "Собрано: " + result.Image.Length + " байт";
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
            while (executed < MaxSteps && _cpu.Step())
                executed++;

            _consoleView.AppendText("Выполнено команд: " + executed + Environment.NewLine);
            string text = _cpu.Console.Output;
            if (!string.IsNullOrEmpty(text))
                _consoleView.AppendText(text + Environment.NewLine);
            if (!string.IsNullOrEmpty(_cpu.Fault))
                _consoleView.AppendText("Ошибка: " + _cpu.Fault + Environment.NewLine);

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

        private void ResetMachine()
        {
            if (_assembly != null)
            {
                _memory = new X64Memory();
                _console = new I8086Console();
                _cpu = new X64Cpu(_memory, _console);
                _cpu.LoadProgram(_assembly.Image, _assembly.Origin);
            }

            _consoleView.Clear();
            RefreshPanels();
        }

        private void OpenFile()
        {
            string path = _files.OpenFile(new FileDialogRequest
            {
                Title = "Открыть программу x86-64",
                Filter = "Исходный текст (*.asm)|*.asm;*.ASM;Все файлы (*.*)|*.*",
                CheckFileExists = true
            });

            if (string.IsNullOrEmpty(path))
                return;

            HostLoadFile(path);
        }

        private void SaveFile()
        {
            string path = _files.SaveFile(new FileDialogRequest
            {
                Title = "Сохранить программу x86-64",
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

        // ------------------------------------------------------------------

        /// <summary>
        /// Shows the machine code of the assembled program: the address, the
        /// bytes of every instruction and the source line.
        /// </summary>
        /// <summary>
        /// Decodes the machine code of the assembled program back into
        /// instructions.
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
                DecodedInstruction instruction = X64Disassembler.Decode(
                    image, offset, _assembly.Origin + (ulong)offset);
                builder.Append(instruction.Address.ToString("X8")).Append("h  ");
                builder.Append(instruction.BytesText).Append(' ');
                builder.Append(instruction.Text).AppendLine();
                offset += Math.Max(1, instruction.Length);
            }

            _consoleView.AppendText(builder.ToString());
        }

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
            foreach (X64ListingLine line in _assembly.Listing)
            {
                if (!line.HasAddress || line.Bytes.Length == 0)
                    continue;
                builder.Append(line.Offset.ToString("X8")).Append("h  ");
                foreach (byte value in line.Bytes)
                    builder.Append(value.ToString("X2")).Append(' ');
                builder.Append(' ').Append(line.Source.Trim()).AppendLine();
            }

            _consoleView.AppendText(builder.ToString());
        }

        private void RefreshPanels()
        {
            X64CpuState state = _cpu.State;
            for (int i = 0; i < RegisterNames.Length; i++)
                _registerValues[i].Text = state.GetGpr(i).ToString("X16") + "h";

            _specialValues[0].Text = state.RIP.ToString("X16") + "h";
            _specialValues[1].Text = state.Flags.ToString("X4") + "h";

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
                _flagValues[i].ForeColor = flags[i] ? AppTheme.Green : AppTheme.TextDim;
            }

            _memoryView.Text = BuildMemoryDump();
        }

        private string BuildMemoryDump()
        {
            int linear = (int)_cpu.State.RIP;
            int start = linear - 16;
            if (start < 0)
                start = 0;

            StringBuilder builder = new StringBuilder();
            for (int row = 0; row < 12; row++)
            {
                int address = start + row * 16;
                builder.Append(address.ToString("X8")).Append('h');
                for (int i = 0; i < 16; i++)
                    builder.Append(' ').Append(_memory.ReadByte((ulong)(address + i)).ToString("X2"));
                builder.Append("  ");
                for (int i = 0; i < 16; i++)
                {
                    byte value = _memory.ReadByte((ulong)(address + i));
                    builder.Append(value >= 32 && value < 127 ? (char)value : '.');
                }

                builder.Append("\r\n");
            }

            return builder.ToString();
        }
    }
}
