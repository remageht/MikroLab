using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using MikroLab.Core.Assembler;
using MikroLab.Core.Cpu;
using MikroLab.Core.Debugging;
using MikroLab.Core.Formats;
using MikroLab.Core.Machine;
using MikroLab.Core.Memory;
using MikroLab.Presentation;

namespace MikroLab.Cli
{
    /// <summary>
    /// Headless, cross-platform companion to the desktop application. It can
    /// assemble sources, run or step programs, inspect memory, disassemble
    /// code, check guided labs, convert Intel HEX and save/load machine state
    /// without any desktop dependencies.
    /// </summary>
    internal static class Program
    {
        private const ushort DefaultOrigin = 0x8000;
        private const ushort DefaultStack = 0x83F0;

        private static int Main(string[] args)
        {
            try
            {
                Console.OutputEncoding = Encoding.UTF8;
            }
            catch (Exception)
            {
                // Перенаправленный вывод может не поддерживать смену кодировки.
            }

            try
            {
                return Run(args);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Ошибка: " + ex.Message);
                return 1;
            }
        }

        private static int Run(string[] args)
        {
            if (args.Length == 0 || IsHelp(args[0]))
            {
                PrintHelp();
                return 0;
            }

            switch (args[0].ToLowerInvariant())
            {
                case "assemble":
                    return AssembleCommand(args);
                case "run":
                    return RunCommand(args);
                case "lab":
                    return LabCommand(args);
                case "disasm":
                case "disassemble":
                    return DisassembleCommand(args);
                case "info":
                    return InfoCommand();
                case "step":
                    return StepCommand(args);
                case "dump":
                    return DumpCommand(args);
                case "hex":
                    return HexCommand(args);
                case "state":
                    return StateCommand(args);
                case "version":
                case "--version":
                    Console.WriteLine("MikroLab.Cli " + ProductVersion());
                    return 0;
                default:
                    Console.Error.WriteLine("Неизвестная команда: " + args[0]);
                    PrintHelp();
                    return 1;
            }
        }

        // ------------------------------------------------------------------ commands

        private static int AssembleCommand(string[] args)
        {
            string sourcePath = RequireSource(args, 1);
            string output = GetOption(args, "-o") ?? GetOption(args, "--output");
            string source = File.ReadAllText(sourcePath);
            AssembleResult result = I8080Assembler.Assemble(source, DefaultOrigin);

            if (!result.Success)
            {
                PrintAssemblerErrors(result);
                return 1;
            }

            int bytes = CountBytes(result);
            Console.WriteLine("Собрано: " + bytes + " байт, символов: " + result.Symbols.Count +
                ", начало: " + result.StartAddress.ToString("X4") + "h");

            if (HasFlag(args, "--listing"))
            {
                Console.WriteLine();
                Console.WriteLine("Листинг:");
                foreach (ListingLine line in result.Listing)
                {
                    if (!line.HasAddress) continue;
                    Console.WriteLine(line.Address.ToString("X4") + "h  " +
                        BytesToText(line.Bytes) + "  " + line.Source);
                }
            }

            if (!string.IsNullOrEmpty(output))
            {
                ushort end = result.EndAddress > result.StartAddress
                    ? (ushort)(result.EndAddress - 1)
                    : result.StartAddress;
                IntelHex.Save(output, result.Image, result.StartAddress, end);
                Console.WriteLine("Записано: " + output);
            }

            return 0;
        }

        private static int RunCommand(string[] args)
        {
            CpuProfile profile = GetProfileOption(args);
            LoadedProgram program = LoadProgram(RequireSource(args, 1), profile);
            int maxSteps = GetIntOption(args, "--steps", 1000000);
            ulong maxCycles = GetULongOption(args, "--max-cycles", 0);
            bool trace = HasFlag(args, "--trace");
            ushort? runTo = GetAddressOption(args, "--run-to");
            string logPath = GetOption(args, "--log");
            ExecutionLog executionLog = string.IsNullOrEmpty(logPath)
                ? null
                : new ExecutionLog(Math.Max(1, Math.Min(maxSteps, 1000000)));

            bool halted = false;
            bool reachedTarget = !runTo.HasValue;
            int executed = 0;
            program.Machine.State = RunState.Running;

            while (executed < maxSteps)
            {
                ushort pc = program.Machine.Cpu.State.PC;
                if (runTo.HasValue && pc == runTo.Value)
                {
                    reachedTarget = true;
                    break;
                }

                byte opcode = program.Machine.Memory.ReadByte(pc);
                DisassembledInstruction decoded = executionLog != null
                    ? I8080Disassembler.Decode(program.Machine.Memory, pc)
                    : default;
                if (trace)
                {
                    Console.WriteLine(pc.ToString("X4") + "h  " + opcode.ToString("X2") + "h");
                }

                ulong cyclesBefore = program.Machine.Cpu.State.Cycles;
                bool ok = program.Machine.Step();
                executed++;

                if (executionLog != null)
                {
                    executionLog.Append(
                        pc,
                        decoded,
                        program.Machine.Cpu.State,
                        program.Machine.Cpu.State.Cycles - cyclesBefore);
                }

                if (runTo.HasValue && ok && program.Machine.Cpu.State.PC == runTo.Value)
                {
                    reachedTarget = true;
                    break;
                }

                if (!ok)
                {
                    halted = true;
                    break;
                }
                if (maxCycles > 0 && program.Machine.Cpu.State.Cycles >= maxCycles)
                {
                    break;
                }
            }

            if (!halted)
            {
                program.Machine.State = RunState.Paused;
            }

            PrintState(program.Machine, executed, halted);
            if (HasFlag(args, "--dump"))
            {
                PrintDump(program.Machine.Memory.Capture(), DefaultOrigin, 64);
            }

            if (executionLog != null)
            {
                ExecutionLogFormat format = string.Equals(
                    Path.GetExtension(logPath), ".md", StringComparison.OrdinalIgnoreCase)
                    ? ExecutionLogFormat.Markdown
                    : ExecutionLogFormat.Csv;
                executionLog.Save(logPath, format);
                Console.WriteLine("Записано: " + logPath + " (" + executionLog.Count + " записей)");
            }

            if (runTo.HasValue && !reachedTarget)
            {
                Console.Error.WriteLine("Адрес " + runTo.Value.ToString("X4") + "h не достигнут.");
                return 1;
            }
            return 0;
        }

        private static int LabCommand(string[] args)
        {
            if (args.Length < 2 || IsHelp(args[1]) ||
                string.Equals(args[1], "list", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(args[1], "список", StringComparison.OrdinalIgnoreCase))
            {
                PrintLabList();
                return 0;
            }

            if (!int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) ||
                number < 1 || number > LabCatalog.All.Length)
            {
                Console.Error.WriteLine("Укажите номер лабораторной работы от 1 до " + LabCatalog.All.Length + ".");
                return 1;
            }

            LabDefinition lab = LabCatalog.All[number - 1];
            SampleProgram sample = SampleLibrary.ByName(lab.SampleName);
            LoadedProgram program = LoadSource(sample.Code, sample.Name, CpuProfile.Extended);
            int maxSteps = GetIntOption(args, "--steps", 100000);

            int executed = RunUntilHalt(program.Machine, maxSteps);
            bool halted = program.Machine.State == RunState.Halted;

            Console.WriteLine();
            Console.WriteLine("Лабораторная работа " + number + ": " + lab.Title);
            Console.WriteLine(lab.Summary);
            Console.WriteLine();
            PrintState(program.Machine, executed, halted);

            MachineSnapshot snapshot = program.Machine.Capture();
            LabCheckResult check = LabCheck.Evaluate(lab, snapshot);

            Console.WriteLine();
            Console.WriteLine("Проверка: " + check.Summary);
            foreach (string detail in check.Details)
            {
                Console.WriteLine("  " + detail);
            }

            if (!check.IsDefined)
            {
                Console.WriteLine("  Интерактивная работа: запустите её в MikroLab Studio и проверьте результат вручную.");
                return 0;
            }

            if (!halted)
            {
                Console.Error.WriteLine("  Программа не остановилась за " + maxSteps + " шагов.");
                return 1;
            }

            return check.Passed ? 0 : 1;
        }

        private static int DisassembleCommand(string[] args)
        {
            string path = RequireSource(args, 1);
            byte[] memory = LoadMemoryImage(path, out ushort start);

            ushort? selectedStart = GetAddressOption(args, "--start");
            if (selectedStart.HasValue)
            {
                start = selectedStart.Value;
            }

            int? length = null;
            string lengthText = GetOption(args, "--length");
            if (lengthText != null)
            {
                if (!int.TryParse(lengthText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedLength) ||
                    parsedLength < 0)
                {
                    throw new ArgumentException("--length требует неотрицательное целое число.");
                }
                length = parsedLength;
            }

            int count = Math.Max(1, GetIntOption(args, "--count", 32));
            int address = start;
            int printed = 0;
            int end = length.HasValue ? Math.Min(0x10000, start + length.Value) : 0x10000;

            while (address < end && (length.HasValue || printed < count))
            {
                DisassembledInstruction instruction = I8080Disassembler.Decode(memory, (ushort)address);
                Console.WriteLine(address.ToString("X4") + "h  " +
                    instruction.BytesText.PadRight(8) + "  " + instruction.Text);
                address += instruction.Length;
                printed++;
            }

            return 0;
        }

        private static int InfoCommand()
        {
            Console.WriteLine("=================================================================");
            Console.WriteLine(" MikroLab CLI — эмулятор КР580ВМ80А / Intel 8080");
            Console.WriteLine(" Кроссплатформенная консольная среда (Windows, Linux, macOS)");
            Console.WriteLine("=================================================================");
            Console.WriteLine();
            Console.WriteLine("Карта адресного пространства памяти:");
            Console.WriteLine("  0000h - 07FFh  ПЗУ монитора стенда (2 КБ)");
            Console.WriteLine("  0100h          Точка входа SEGGG (вывод на 7-сегментные индикаторы)");
            Console.WriteLine("  0300h          Точка входа Sound (генерация звукового сигнала)");
            Console.WriteLine("  0400h          Точка входа Delay (программная задержка)");
            Console.WriteLine("  8000h - 83C6h  ОЗУ пользователя (базовый адрес загрузки программ 8000h)");
            Console.WriteLine("  83E4h - 83EBh  Буфер регистров монитора при останове:");
            Console.WriteLine("                 83E4h=L, 83E5h=H, 83E6h=E, 83E7h=D, 83E8h=C, 83E9h=B, 83EAh=PSW, 83EBh=A");
            Console.WriteLine("  83F8h - 83FFh  Регистры 8-разрядного 7-сегментного дисплея");
            Console.WriteLine();
            Console.WriteLine("Порты ввода-вывода (микросхема КР580ВВ55А / i8255 PPI):");
            Console.WriteLine("  Порт FBh       Регистр управляющего слова (РУС)");
            Console.WriteLine("  Порт FAh       Порт ввода состояния тумблеров (SA1..SA3)");
            Console.WriteLine("  Порт F9h       Порт вывода на светодиодные лампы (HL1..HL8)");
            Console.WriteLine("  Порт F8h       Порт C (матричная клавиатура 4x4)");
            Console.WriteLine();
            Console.WriteLine("Профили выполнения:");
            Console.WriteLine("  Legacy         Историческое поведение стенда: буфер регистров, особенности микрокода.");
            Console.WriteLine("  Extended       Полный набор инструкций i8080, аппаратный стек, точные флаги.");
            Console.WriteLine("                 В командах CLI профиль extended также называется newest.");
            Console.WriteLine();
            Console.WriteLine("Команды: run, lab 1.." + LabCatalog.All.Length + ", assemble, disasm, info, step, dump, hex, state.");
            Console.WriteLine("Подробная справка: MikroLab.Cli help");
            return 0;
        }

        private static int StepCommand(string[] args)
        {
            CpuProfile profile = GetProfileOption(args);
            LoadedProgram program = LoadProgram(RequireSource(args, 1), profile);
            int count = Math.Max(1, GetIntOption(args, "--count", 1));

            for (int i = 0; i < count; i++)
            {
                ushort pc = program.Machine.Cpu.State.PC;
                byte opcode = program.Machine.Memory.ReadByte(pc);
                bool ok = program.Machine.Step();
                program.Machine.State = ok ? RunState.Paused : RunState.Halted;
                Console.WriteLine(pc.ToString("X4") + "h  " + opcode.ToString("X2") +
                    "h  -> PC=" + program.Machine.Cpu.State.PC.ToString("X4") + "h" +
                    (ok ? string.Empty : "  HALT"));
                if (!ok) break;
            }

            PrintState(program.Machine, count, program.Machine.State == RunState.Halted);
            return 0;
        }

        private static int DumpCommand(string[] args)
        {
            CpuProfile profile = GetProfileOption(args);
            LoadedProgram program = LoadProgram(RequireSource(args, 1), profile);
            ushort start = ParseAddress(GetOption(args, "--start") ?? "8000");
            int length = GetIntOption(args, "--length", 256);
            if (length < 0) throw new ArgumentException("--length требует неотрицательное целое число.");
            length = Math.Min(length, 0x10000 - start);
            PrintDump(program.Machine.Memory.Capture(), start, length);
            return 0;
        }

        private static int HexCommand(string[] args)
        {
            string input = RequireSource(args, 1);
            string output = GetOption(args, "-o") ?? GetOption(args, "--output");
            if (string.IsNullOrEmpty(output))
                throw new ArgumentException("hex требует -o <файл>");

            string extension = Path.GetExtension(input).ToLowerInvariant();
            if (extension == ".hex" || extension == ".ihx")
            {
                IntelHexImage image = IntelHex.Load(input);
                if (image.ByteCount == 0)
                    throw new InvalidDataException("Файл Intel HEX не содержит данных.");

                int first = 0x10000;
                int last = -1;
                foreach (IntelHexSegment segment in image.Segments)
                {
                    first = Math.Min(first, segment.Address);
                    last = Math.Max(last, segment.Address + segment.Length - 1);
                }

                string selectedStart = GetOption(args, "--start");
                int baseAddress = selectedStart != null ? ParseAddress(selectedStart) : first;
                if (baseAddress > first)
                    throw new ArgumentException("--start " + baseAddress.ToString("X4") +
                        "h выше первого загруженного байта " + first.ToString("X4") + "h.");

                var binary = new byte[last - baseAddress + 1];
                foreach (IntelHexSegment segment in image.Segments)
                {
                    Array.Copy(segment.Data, 0, binary,
                        segment.Address - baseAddress, segment.Length);
                }

                File.WriteAllBytes(output, binary);
                Console.WriteLine("Записано: " + output + " (" + binary.Length + " байт, " +
                    image.ByteCount + " загружено, " + baseAddress.ToString("X4") + "h–" +
                    last.ToString("X4") + "h)");
                return 0;
            }

            byte[] data = File.ReadAllBytes(input);
            bool explicitStart = GetOption(args, "--start") != null;
            string startOption = GetOption(args, "--start") ?? "8000";
            ushort start = ParseAddress(startOption);
            if (!explicitStart && data.Length == 0x10000)
            {
                // Полный образ 64 КБ уже покрывает всё адресное пространство.
                start = 0;
            }
            if ((int)start + data.Length > 0x10000)
                throw new ArgumentException("Двоичный файл не помещается в 64 КБ по адресу " + start.ToString("X4") + "h.");
            if (data.Length == 0)
                throw new ArgumentException("Входной файл пуст.");

            var memory = new byte[0x10000];
            Array.Copy(data, 0, memory, start, data.Length);
            ushort end = (ushort)(start + data.Length - 1);
            IntelHex.Save(output, memory, start, end, HasFlag(args, "--start-record"));
            Console.WriteLine("Записано: " + output + " (" + data.Length + " байт по адресу " +
                start.ToString("X4") + "h)");
            return 0;
        }

        private static int StateCommand(string[] args)
        {
            if (args.Length < 2)
                throw new ArgumentException("state требует save или load.");

            string action = args[1].ToLowerInvariant();
            if (action == "save")
            {
                CpuProfile profile = GetProfileOption(args);
                LoadedProgram program = LoadProgram(RequireSource(args, 2), profile);
                string output = GetOption(args, "-o") ?? GetOption(args, "--output");
                if (string.IsNullOrEmpty(output))
                    throw new ArgumentException("state save требует -o <файл>.");

                int steps = GetIntOption(args, "--steps", 0);
                ExecuteSteps(program.Machine, steps);

                if (Is580(output))
                {
                    MachineSnapshot snapshot = program.Machine.Capture();
                    Kr580Image.FromSnapshot(snapshot).Save(output);
                    Console.WriteLine("Записано: " + output + " (.580, PC=" +
                        snapshot.PC.ToString("X4") + "h, SP=" + snapshot.SP.ToString("X4") + "h)");
                }
                else
                {
                    MachineStateSerializer.Save(output, program.Machine.Capture(),
                        program.ProgramLoaded, program.EntryPoint);
                    Console.WriteLine("Записано: " + output + " при PC=" +
                        program.Machine.Cpu.State.PC.ToString("X4") + "h");
                }
                return 0;
            }

            if (action == "load")
            {
                string input = RequireSource(args, 2);
                CpuProfile profile = GetProfileOption(args);
                var machine = CreateMachine(profile);
                if (Is580(input))
                {
                    machine.Restore(Kr580Image.Load(input).ToSnapshot());
                }
                else
                {
                    MachineStateFile state = MachineStateSerializer.Load(input);
                    machine.Restore(state.Snapshot);
                }

                int steps = GetIntOption(args, "--steps", 0);
                ExecuteSteps(machine, steps);
                PrintState(machine, steps, machine.State == RunState.Halted);
                if (HasFlag(args, "--dump"))
                {
                    PrintDump(machine.Memory.Capture(), DefaultOrigin, 64);
                }
                return 0;
            }

            throw new ArgumentException("state требует save или load.");
        }

        // ------------------------------------------------------------------ loading

        private sealed class LoadedProgram
        {
            public Machine Machine { get; }
            public bool ProgramLoaded { get; }
            public ushort EntryPoint { get; }

            public LoadedProgram(Machine machine, bool programLoaded, ushort entryPoint)
            {
                Machine = machine;
                ProgramLoaded = programLoaded;
                EntryPoint = entryPoint;
            }
        }

        private static LoadedProgram LoadProgram(string path, CpuProfile profile)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension == ".json" || extension == ".mstate")
            {
                MachineStateFile state = MachineStateSerializer.Load(path);
                var machine = CreateMachine(profile);
                machine.Restore(state.Snapshot);
                return new LoadedProgram(machine, state.ProgramLoaded, state.EntryPoint);
            }

            if (extension == ".580")
            {
                Kr580Image image = Kr580Image.Load(path);
                var machine = CreateMachine(profile);
                machine.Restore(image.ToSnapshot());
                machine.State = RunState.Stopped;
                return new LoadedProgram(machine, true, image.PC);
            }

            if (extension == ".bin")
            {
                byte[] data = File.ReadAllBytes(path);
                if (data.Length == 0)
                    throw new InvalidDataException("Двоичный файл пуст.");
                int start = data.Length == MemoryMap.MemSize ? 0 : DefaultOrigin;
                if (start + data.Length > MemoryMap.MemSize)
                    throw new InvalidDataException("Двоичный файл не помещается в 64 КБ по адресу " +
                        start.ToString("X4") + "h.");

                var machine = CreateMachine(profile);
                machine.Reset(ResetKind.Soft);
                ClearMemory(machine);
                for (int i = 0; i < data.Length; i++)
                {
                    machine.Memory.WriteByte((ushort)(start + i), data[i]);
                }
                machine.Cpu.State.PC = (ushort)start;
                machine.Cpu.State.SP = DefaultStack;
                machine.State = RunState.Stopped;
                return new LoadedProgram(machine, true, (ushort)start);
            }

            if (extension == ".hex" || extension == ".ihx")
            {
                IntelHexImage image = IntelHex.Load(path);
                if (image.ByteCount == 0)
                    throw new InvalidDataException("Файл Intel HEX не содержит данных.");
                var machine = CreateMachine(profile);
                machine.Reset(ResetKind.Soft);
                ClearMemory(machine);
                foreach (IntelHexSegment segment in image.Segments)
                {
                    for (int i = 0; i < segment.Data.Length; i++)
                    {
                        machine.Memory.WriteByte((ushort)(segment.Address + i), segment.Data[i]);
                    }
                }
                ushort start = image.StartAddress ?? DefaultOrigin;
                machine.Cpu.State.PC = start;
                machine.Cpu.State.SP = DefaultStack;
                machine.State = RunState.Stopped;
                return new LoadedProgram(machine, true, start);
            }

            string source = File.ReadAllText(path);
            return LoadSource(source, path, profile);
        }

        private static LoadedProgram LoadSource(string source, string displayName, CpuProfile profile)
        {
            AssembleResult result = I8080Assembler.Assemble(source, DefaultOrigin);
            if (!result.Success)
            {
                PrintAssemblerErrors(result);
                throw new InvalidOperationException("Не удалось собрать " + displayName +
                    ": ошибок " + result.Errors.Count + ".");
            }

            var assembled = CreateMachine(profile);
            assembled.Reset(ResetKind.Soft);
            for (int address = 0x8000; address <= 0x83FF; address++)
            {
                assembled.Memory.WriteByte((ushort)address, 0);
            }
            foreach (AssembledChunk chunk in result.Chunks)
            {
                for (int i = 0; i < chunk.Data.Length; i++)
                {
                    assembled.Memory.WriteByte((ushort)(chunk.Address + i), chunk.Data[i]);
                }
            }
            assembled.Cpu.State.PC = result.StartAddress;
            assembled.Cpu.State.SP = DefaultStack;
            assembled.State = RunState.Stopped;
            return new LoadedProgram(assembled, true, result.StartAddress);
        }

        private static byte[] LoadMemoryImage(string path, out ushort start)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            var memory = new byte[MemoryMap.MemSize];
            start = DefaultOrigin;

            if (extension == ".hex" || extension == ".ihx")
            {
                IntelHexImage image = IntelHex.Load(path);
                if (image.ByteCount == 0)
                    throw new InvalidDataException("Файл Intel HEX не содержит данных.");
                start = image.StartAddress ?? FirstSegmentAddress(image);
                foreach (IntelHexSegment segment in image.Segments)
                {
                    Array.Copy(segment.Data, 0, memory, segment.Address, segment.Length);
                }
                return memory;
            }

            if (extension == ".580")
            {
                MachineSnapshot snapshot = Kr580Image.Load(path).ToSnapshot();
                if (snapshot.Memory != null)
                    Array.Copy(snapshot.Memory, memory, Math.Min(snapshot.Memory.Length, memory.Length));
                start = snapshot.PC;
                return memory;
            }

            if (extension == ".json" || extension == ".mstate")
            {
                MachineStateFile state = MachineStateSerializer.Load(path);
                if (state.Snapshot.Memory != null)
                    Array.Copy(state.Snapshot.Memory, memory, Math.Min(state.Snapshot.Memory.Length, memory.Length));
                start = state.Snapshot.PC;
                return memory;
            }

            if (extension == ".bin")
            {
                byte[] data = File.ReadAllBytes(path);
                if (data.Length == 0)
                    throw new InvalidDataException("Двоичный файл пуст.");
                start = data.Length == MemoryMap.MemSize ? (ushort)0 : DefaultOrigin;
                if (start + data.Length > MemoryMap.MemSize)
                    throw new InvalidDataException("Двоичный файл не помещается в 64 КБ.");
                Array.Copy(data, 0, memory, start, data.Length);
                return memory;
            }

            string source = File.ReadAllText(path);
            AssembleResult result = I8080Assembler.Assemble(source, DefaultOrigin);
            if (!result.Success)
            {
                PrintAssemblerErrors(result);
                throw new InvalidOperationException("Не удалось собрать " + path +
                    ": ошибок " + result.Errors.Count + ".");
            }
            foreach (AssembledChunk chunk in result.Chunks)
            {
                Array.Copy(chunk.Data, 0, memory, chunk.Address, chunk.Data.Length);
            }
            start = result.StartAddress;
            return memory;
        }

        private static ushort FirstSegmentAddress(IntelHexImage image)
        {
            ushort first = 0xFFFF;
            foreach (IntelHexSegment segment in image.Segments)
            {
                if (segment.Address < first) first = (ushort)segment.Address;
            }
            return first == 0xFFFF ? DefaultOrigin : first;
        }

        private static Machine CreateMachine(CpuProfile profile)
        {
            return new Machine(profile: profile);
        }

        private static void ClearMemory(Machine machine)
        {
            for (int address = 0; address < MemoryMap.MemSize; address++)
            {
                machine.Memory.WriteByte((ushort)address, 0);
            }
        }

        private static void ExecuteSteps(Machine machine, int steps)
        {
            if (steps <= 0) return;

            machine.State = RunState.Running;
            for (int i = 0; i < steps; i++)
            {
                if (!machine.Step()) return;
            }
            machine.State = RunState.Paused;
        }

        private static int RunUntilHalt(Machine machine, int maxSteps)
        {
            int executed = 0;
            machine.State = RunState.Running;
            while (executed < maxSteps)
            {
                bool ok = machine.Step();
                executed++;
                if (!ok) break;
            }
            return executed;
        }

        // ------------------------------------------------------------------ output

        private static void PrintAssemblerErrors(AssembleResult result)
        {
            foreach (AssemblerError error in result.Errors)
            {
                Console.Error.WriteLine("строка " + error.LineNumber + ": " + error.Message);
            }
        }

        private static void PrintState(Machine machine, int executed, bool halted)
        {
            CpuState state = machine.Cpu.State;
            Console.WriteLine(
                "Состояние: " + StateText(machine.State) +
                "  останов: " + (halted ? "да" : "нет") +
                "  шагов: " + executed +
                "  PC=" + state.PC.ToString("X4") + "h" +
                "  SP=" + state.SP.ToString("X4") + "h" +
                "  A=" + state.A.ToString("X2") + "h" +
                "  F=" + state.Flags.ToByte().ToString("X2") + "h" +
                "  тактов: " + state.Cycles);
        }

        private static string StateText(RunState state)
        {
            switch (state)
            {
                case RunState.Running: return "выполняется";
                case RunState.Paused: return "пауза";
                case RunState.Halted: return "остановлен по HLT";
                default: return "остановлен";
            }
        }

        private static void PrintDump(byte[] memory, ushort start, int length)
        {
            for (int offset = 0; offset < length; offset += 16)
            {
                int address = start + offset;
                if (address >= 0x10000) break;
                int count = Math.Min(16, length - offset);
                var bytes = new StringBuilder();
                var ascii = new StringBuilder();
                for (int i = 0; i < 16; i++)
                {
                    if (i < count)
                    {
                        byte value = memory[address + i];
                        bytes.Append(value.ToString("X2")).Append(' ');
                        ascii.Append(value >= 32 && value <= 126 ? (char)value : '.');
                    }
                    else
                    {
                        bytes.Append("   ");
                    }
                }
                Console.WriteLine(address.ToString("X4") + "h  " + bytes + " |" + ascii + "|");
            }
        }

        private static string ProductVersion()
        {
            Version version = typeof(Program).Assembly.GetName().Version;
            return version != null ? version.ToString(3) : "0.0.0";
        }

        private static void PrintHelp()
        {
            Console.WriteLine("=================================================================");
            Console.WriteLine(" MikroLab CLI — КР580ВМ80А / Intel 8080 Эмулятор");
            Console.WriteLine(" Кроссплатформенная консольная среда (Windows, Linux, macOS)");
            Console.WriteLine("=================================================================");
            Console.WriteLine();
            Console.WriteLine("Использование: MikroLab.Cli <команда> [параметры]");
            Console.WriteLine();
            Console.WriteLine("Команды:");
            Console.WriteLine("  run <файл.asm|файл.hex|файл.bin|файл.580|state.json>");
            Console.WriteLine("                            Собрать и выполнить программу");
            Console.WriteLine("  lab <1.." + LabCatalog.All.Length + ">              Выполнить и проверить лабораторную работу");
            Console.WriteLine("  lab list                  Показать список лабораторных работ");
            Console.WriteLine("  assemble <файл.asm>       Собрать ассемблерный файл в Intel HEX");
            Console.WriteLine("                            Параметры: [-o out.hex] [--listing]");
            Console.WriteLine("  disasm <файл>             Дизассемблировать код");
            Console.WriteLine("                            Параметры: [--start XXXX] [--length N | --count N]");
            Console.WriteLine("  info                      Карта памяти, порты и профили процессора");
            Console.WriteLine("  step <файл>               Выполнить одну или несколько инструкций");
            Console.WriteLine("                            Параметры: [--count N]");
            Console.WriteLine("  dump <файл>               Вывести дамп памяти");
            Console.WriteLine("                            Параметры: [--start XXXX] [--length N]");
            Console.WriteLine("  hex <файл.hex|файл.bin> -o <файл>");
            Console.WriteLine("                            Конвертировать Intel HEX <-> BIN");
            Console.WriteLine("  state save <файл> -o <файл.json|файл.580> [--steps N]");
            Console.WriteLine("  state load <файл.json|файл.580> [--steps N] [--dump]");
            Console.WriteLine("  version                   Показать версию");
            Console.WriteLine("  help                      Вывести эту справку");
            Console.WriteLine();
            Console.WriteLine("Параметры команды run:");
            Console.WriteLine("  --profile legacy|newest   Профиль процессора (по умолчанию: newest)");
            Console.WriteLine("  --steps <N>               Максимальное число шагов (по умолчанию: 1000000)");
            Console.WriteLine("  --max-cycles <N>          Остановиться после N тактов");
            Console.WriteLine("  --run-to <XXXXh>          Остановиться на указанном адресе");
            Console.WriteLine("  --log <файл.csv|файл.md>  Сохранить журнал выполнения");
            Console.WriteLine("  --trace                   Пошаговый вывод инструкций");
            Console.WriteLine("  --dump                    Вывести дамп памяти после останова");
            Console.WriteLine();
            Console.WriteLine("Примеры:");
            Console.WriteLine("  MikroLab.Cli lab 1                 Проверить лабораторную работу №1");
            Console.WriteLine("  MikroLab.Cli lab list              Список всех лабораторных работ");
            Console.WriteLine("  MikroLab.Cli run program.asm --dump");
            Console.WriteLine("  MikroLab.Cli disasm program.hex --count 20");
            Console.WriteLine("  MikroLab.Cli info                  Справка по карте памяти и портам");
            Console.WriteLine();
            Console.WriteLine("Программы без ORG загружаются по адресу 8000h. Импорт Intel HEX");
            Console.WriteLine("очищает образ памяти 64 КБ; файлы состояния хранят машину целиком,");
            Console.WriteLine("включая память, устройства, флаги, стек вызовов и состояние HLT.");
            Console.WriteLine("Файлы .580 используют исходный формат КР580 объёмом 65 549 байт:");
            Console.WriteLine("64 КБ ОЗУ, A B C D E H L W Z, PC и SP (без флагов).");
        }

        private static void PrintLabList()
        {
            Console.WriteLine("Лабораторные работы MikroLab:");
            Console.WriteLine();
            foreach (LabDefinition lab in LabCatalog.All)
            {
                Console.WriteLine("  " + lab.Number + " — " + lab.Title);
                Console.WriteLine("      " + lab.Summary);
            }
            Console.WriteLine();
            Console.WriteLine("Запуск: MikroLab.Cli lab <номер>");
            Console.WriteLine("Работы 1–4 проверяются автоматически, работы 5–" + LabCatalog.All.Length +
                " интерактивные.");
        }

        // ------------------------------------------------------------------ options

        private static bool IsHelp(string value)
        {
            return value == "-h" || value == "--help" || value == "help" || value == "/?";
        }

        private static bool Is580(string path)
        {
            return string.Equals(Path.GetExtension(path), ".580", StringComparison.OrdinalIgnoreCase);
        }

        private static CpuProfile GetProfileOption(string[] args)
        {
            string text = GetOption(args, "--profile");
            if (string.IsNullOrEmpty(text)) return CpuProfile.Extended;

            if (string.Equals(text, "legacy", StringComparison.OrdinalIgnoreCase))
                return CpuProfile.Legacy;
            if (string.Equals(text, "extended", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "newest", StringComparison.OrdinalIgnoreCase))
                return CpuProfile.Extended;

            throw new ArgumentException("--profile принимает legacy или newest.");
        }

        private static string RequireSource(string[] args, int index)
        {
            if (args.Length <= index || string.IsNullOrWhiteSpace(args[index]))
                throw new ArgumentException("Не указан путь к файлу.");
            return args[index];
        }

        private static string GetOption(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }
            return null;
        }

        private static bool HasFlag(string[] args, string name)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static ushort? GetAddressOption(string[] args, string name)
        {
            string text = GetOption(args, name);
            return text == null ? (ushort?)null : ParseAddress(text);
        }

        private static int GetIntOption(string[] args, string name, int fallback)
        {
            string text = GetOption(args, name);
            if (text == null) return fallback;
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                throw new ArgumentException(name + " требует целое число.");
            return value;
        }

        private static ulong GetULongOption(string[] args, string name, ulong fallback)
        {
            string text = GetOption(args, name);
            if (text == null) return fallback;
            if (!ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong value))
                throw new ArgumentException(name + " требует неотрицательное целое число.");
            return value;
        }

        private static ushort ParseAddress(string text)
        {
            string value = (text ?? string.Empty).Trim();
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                value = value.Substring(2);
            value = value.TrimEnd('h', 'H');
            if (!ushort.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort address))
                throw new ArgumentException("Некорректный шестнадцатеричный адрес: " + text);
            return address;
        }

        private static int CountBytes(AssembleResult result)
        {
            int count = 0;
            foreach (AssembledChunk chunk in result.Chunks)
            {
                count += chunk.Data.Length;
            }
            return count;
        }

        private static string BytesToText(byte[] bytes)
        {
            var builder = new StringBuilder();
            for (int i = 0; i < bytes.Length; i++)
            {
                if (i > 0) builder.Append(' ');
                builder.Append(bytes[i].ToString("X2"));
            }
            return builder.ToString();
        }
    }
}
