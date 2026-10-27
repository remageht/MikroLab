using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using MikroLab.Core.Assembler;
using MikroLab.Core.Cpu;
using MikroLab.Core.Formats;
using MikroLab.Core.Machine;
using MikroLab.Core.Memory;

namespace MikroLab.Cli
{
    /// <summary>
    /// Headless, cross-platform companion to the WinForms application. It can
    /// assemble sources, run or step programs, inspect memory, convert Intel
    /// HEX and save/load machine state without any desktop dependencies.
    /// </summary>
    internal static class Program
    {
        private const ushort DefaultOrigin = 0x8000;
        private const ushort DefaultStack = 0x83F0;

        private static int Main(string[] args)
        {
            try
            {
                return Run(args);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("error: " + ex.Message);
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
                    Console.Error.WriteLine("unknown command: " + args[0]);
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
            Console.WriteLine("assembled " + bytes + " bytes, " + result.Symbols.Count +
                " symbols, start " + result.StartAddress.ToString("X4") + "h");

            if (HasFlag(args, "--listing"))
            {
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
                Console.WriteLine("wrote " + output);
            }

            return 0;
        }

        private static int RunCommand(string[] args)
        {
            LoadedProgram program = LoadProgram(RequireSource(args, 1));
            int maxSteps = GetIntOption(args, "--steps", 1000000);
            ulong maxCycles = GetULongOption(args, "--max-cycles", 0);
            bool trace = HasFlag(args, "--trace");
            bool halted = false;
            int executed = 0;
            program.Machine.State = RunState.Running;

            while (executed < maxSteps)
            {
                ushort pc = program.Machine.Cpu.State.PC;
                byte opcode = program.Machine.Memory.ReadByte(pc);
                if (trace)
                {
                    Console.WriteLine(pc.ToString("X4") + "h  " + opcode.ToString("X2") + "h");
                }

                bool ok = program.Machine.Step();
                executed++;
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
            return 0;
        }

        private static int StepCommand(string[] args)
        {
            LoadedProgram program = LoadProgram(RequireSource(args, 1));
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
            LoadedProgram program = LoadProgram(RequireSource(args, 1));
            ushort start = ParseAddress(GetOption(args, "--start") ?? "8000");
            int length = GetIntOption(args, "--length", 256);
            if (length < 0) throw new ArgumentException("--length must be non-negative");
            length = Math.Min(length, 0x10000 - start);
            PrintDump(program.Machine.Memory.Capture(), start, length);
            return 0;
        }

        private static int HexCommand(string[] args)
        {
            string input = RequireSource(args, 1);
            string output = GetOption(args, "-o") ?? GetOption(args, "--output");
            if (string.IsNullOrEmpty(output))
                throw new ArgumentException("hex requires -o <output>");

            string extension = Path.GetExtension(input).ToLowerInvariant();
            if (extension == ".hex" || extension == ".ihx")
            {
                IntelHexImage image = IntelHex.Load(input);
                if (image.ByteCount == 0)
                    throw new InvalidDataException("Intel HEX file contains no data");

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
                        "h is above the first loaded byte at " + first.ToString("X4") + "h");

                var binary = new byte[last - baseAddress + 1];
                foreach (IntelHexSegment segment in image.Segments)
                {
                    Array.Copy(segment.Data, 0, binary,
                        segment.Address - baseAddress, segment.Length);
                }

                File.WriteAllBytes(output, binary);
                Console.WriteLine("wrote " + output + " (" + binary.Length + " bytes, " +
                    image.ByteCount + " loaded, " + baseAddress.ToString("X4") + "h–" +
                    last.ToString("X4") + "h)");
                return 0;
            }

            byte[] data = File.ReadAllBytes(input);
            bool explicitStart = GetOption(args, "--start") != null;
            string startOption = GetOption(args, "--start") ?? "8000";
            ushort start = ParseAddress(startOption);
            if (!explicitStart && data.Length == 0x10000)
            {
                // A full 64 KiB image (for example, produced from Intel HEX)
                // already covers the whole address space.
                start = 0;
            }
            if ((int)start + data.Length > 0x10000)
                throw new ArgumentException("binary does not fit into 64K at " + start.ToString("X4") + "h");
            if (data.Length == 0)
                throw new ArgumentException("input file is empty");

            var memory = new byte[0x10000];
            Array.Copy(data, 0, memory, start, data.Length);
            ushort end = (ushort)(start + data.Length - 1);
            IntelHex.Save(output, memory, start, end, HasFlag(args, "--start-record"));
            Console.WriteLine("wrote " + output + " (" + data.Length + " bytes at " +
                start.ToString("X4") + "h)");
            return 0;
        }

        private static int StateCommand(string[] args)
        {
            if (args.Length < 2)
                throw new ArgumentException("state requires save or load");

            string action = args[1].ToLowerInvariant();
            if (action == "save")
            {
                LoadedProgram program = LoadProgram(RequireSource(args, 2));
                string output = GetOption(args, "-o") ?? GetOption(args, "--output");
                if (string.IsNullOrEmpty(output))
                    throw new ArgumentException("state save requires -o <output>");

                int steps = GetIntOption(args, "--steps", 0);
                ExecuteSteps(program.Machine, steps);
                MachineStateSerializer.Save(output, program.Machine.Capture(),
                    program.ProgramLoaded, program.EntryPoint);
                Console.WriteLine("wrote " + output + " at PC=" +
                    program.Machine.Cpu.State.PC.ToString("X4") + "h");
                return 0;
            }

            if (action == "load")
            {
                string input = RequireSource(args, 2);
                MachineStateFile state = MachineStateSerializer.Load(input);
                var machine = CreateMachine();
                machine.Restore(state.Snapshot);
                int steps = GetIntOption(args, "--steps", 0);
                ExecuteSteps(machine, steps);
                PrintState(machine, steps, machine.State == RunState.Halted);
                if (HasFlag(args, "--dump"))
                {
                    PrintDump(machine.Memory.Capture(), DefaultOrigin, 64);
                }
                return 0;
            }

            throw new ArgumentException("state requires save or load");
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

        private static LoadedProgram LoadProgram(string path)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension == ".json" || extension == ".mstate")
            {
                MachineStateFile state = MachineStateSerializer.Load(path);
                var machine = CreateMachine();
                machine.Restore(state.Snapshot);
                return new LoadedProgram(machine, state.ProgramLoaded, state.EntryPoint);
            }

            if (extension == ".hex" || extension == ".ihx")
            {
                IntelHexImage image = IntelHex.Load(path);
                if (image.ByteCount == 0)
                    throw new InvalidDataException("Intel HEX file contains no data");
                var machine = CreateMachine();
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
            AssembleResult result = I8080Assembler.Assemble(source, DefaultOrigin);
            if (!result.Success)
            {
                PrintAssemblerErrors(result);
                throw new InvalidOperationException("assembly failed with " + result.Errors.Count + " error(s)");
            }

            var assembled = CreateMachine();
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

        private static Machine CreateMachine()
        {
            return new Machine(profile: CpuProfile.Extended);
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

        // ------------------------------------------------------------------ output

        private static void PrintAssemblerErrors(AssembleResult result)
        {
            foreach (AssemblerError error in result.Errors)
            {
                Console.Error.WriteLine("line " + error.LineNumber + ": " + error.Message);
            }
        }

        private static void PrintState(Machine machine, int executed, bool halted)
        {
            CpuState state = machine.Cpu.State;
            Console.WriteLine(
                "state=" + machine.State +
                " halted=" + (halted ? "yes" : "no") +
                " steps=" + executed +
                " PC=" + state.PC.ToString("X4") + "h" +
                " SP=" + state.SP.ToString("X4") + "h" +
                " A=" + state.A.ToString("X2") + "h" +
                " F=" + state.Flags.ToByte().ToString("X2") + "h" +
                " cycles=" + state.Cycles);
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
            Console.WriteLine("MikroLab.Cli — headless KR580VM80A / Intel 8080 toolkit");
            Console.WriteLine();
            Console.WriteLine("usage:");
            Console.WriteLine("  MikroLab.Cli assemble <source.asm> [-o <out.hex>] [--listing]");
            Console.WriteLine("  MikroLab.Cli run <source.asm|file.hex|state.json> [--steps N] [--max-cycles N] [--trace] [--dump]");
            Console.WriteLine("  MikroLab.Cli step <source> [--count N]");
            Console.WriteLine("  MikroLab.Cli dump <source> [--start XXXX] [--length N]");
            Console.WriteLine("  MikroLab.Cli hex <file.hex|file.bin> -o <output> [--start XXXX] [--start-record]");
            Console.WriteLine("  MikroLab.Cli state save <source> -o <state.json> [--steps N]");
            Console.WriteLine("  MikroLab.Cli state load <state.json> [--steps N] [--dump]");
            Console.WriteLine();
            Console.WriteLine("Programs assembled without ORG start at 8000h. Intel HEX import");
            Console.WriteLine("clears the 64K memory image; state files store the full machine,");
            Console.WriteLine("including memory, devices, flags, call stack and HLT state.");
        }

        // ------------------------------------------------------------------ options

        private static bool IsHelp(string value)
        {
            return value == "-h" || value == "--help" || value == "help" || value == "/?";
        }

        private static string RequireSource(string[] args, int index)
        {
            if (args.Length <= index || string.IsNullOrWhiteSpace(args[index]))
                throw new ArgumentException("missing source path");
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

        private static int GetIntOption(string[] args, string name, int fallback)
        {
            string text = GetOption(args, name);
            if (text == null) return fallback;
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                throw new ArgumentException(name + " expects an integer");
            return value;
        }

        private static ulong GetULongOption(string[] args, string name, ulong fallback)
        {
            string text = GetOption(args, name);
            if (text == null) return fallback;
            if (!ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong value))
                throw new ArgumentException(name + " expects a non-negative integer");
            return value;
        }

        private static ushort ParseAddress(string text)
        {
            string value = (text ?? string.Empty).Trim();
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                value = value.Substring(2);
            value = value.TrimEnd('h', 'H');
            if (!ushort.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort address))
                throw new ArgumentException("invalid hexadecimal address: " + text);
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
