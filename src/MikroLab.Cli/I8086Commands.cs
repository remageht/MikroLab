using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MikroLab.Core.Assembler;
using MikroLab.Core.Assembler.I8086;
using MikroLab.Core.Cpu.I8086;
using MikroLab.Core.Formats;
using MikroLab.Core.Memory;

namespace MikroLab.Cli
{
    /// <summary>
    /// Command line support for the Intel 8086 architecture: assembling an
    /// Intel syntax source file, running the produced program and dumping the
    /// state of the processor.
    /// </summary>
    internal static class I8086Commands
    {
        public const ushort DefaultOrigin = 0x0100;

        public static int Assemble(string[] args, string sourcePath, string output)
        {
            string source = File.ReadAllText(sourcePath);
            string directory = Path.GetDirectoryName(Path.GetFullPath(sourcePath));
            I8086AssembleResult result = I8086Assembler.Assemble(source, DefaultOrigin, directory);

            foreach (AssemblerError error in result.Errors)
            {
                Console.Error.WriteLine("Строка " + error.LineNumber + ": " + error.Message);
            }

            if (!result.Success)
                return 1;

            Console.WriteLine("Собрано: " + result.Image.Length + " байт, символов: " +
                result.Symbols.Count + ", начало: " + result.Origin.ToString("X4") + "h");

            if (HasFlag(args, "--listing"))
            {
                Console.WriteLine();
                Console.WriteLine("Листинг:");
                foreach (I8086ListingLine line in result.Listing)
                {
                    if (!line.HasAddress)
                        continue;
                    Console.WriteLine(line.Offset.ToString("X4") + "h  " +
                        BytesToText(line.Bytes) + "  " + line.Source.Trim());
                }
            }

            if (!string.IsNullOrEmpty(output))
            {
                string extension = Path.GetExtension(output).ToLowerInvariant();
                if (extension == ".hex" || extension == ".ihx")
                {
                    IntelHex.Save(output, result.Image, result.Origin, result.EndAddress);
                }
                else
                {
                    File.WriteAllBytes(output, result.Image);
                }

                Console.WriteLine("Записано: " + output);
            }

            return 0;
        }

        public static int Run(string[] args, string sourcePath)
        {
            string source = File.ReadAllText(sourcePath);
            string directory = Path.GetDirectoryName(Path.GetFullPath(sourcePath));
            I8086AssembleResult result = I8086Assembler.Assemble(source, DefaultOrigin, directory);

            foreach (AssemblerError error in result.Errors)
                Console.Error.WriteLine("Строка " + error.LineNumber + ": " + error.Message);

            if (!result.Success)
                return 1;

            int maxSteps = GetIntOption(args, "--steps", 1000000);
            bool dump = HasFlag(args, "--dump");
            bool trace = HasFlag(args, "--trace");

            I8086Memory memory = new I8086Memory();
            I8086Console console = new I8086Console();
            I8086Cpu cpu = new I8086Cpu(memory, console);
            cpu.LoadProgram(result.Image, 0x0000, result.Origin);

            int executed = 0;
            while (executed < maxSteps && cpu.Step())
                executed++;

            Console.WriteLine("Выполнено команд: " + executed +
                (cpu.Halted ? ", программа завершилась" : ", останов по лимиту шагов"));
            if (cpu.ExitCode != 0)
                Console.WriteLine("Код завершения: " + cpu.ExitCode);

            string text = console.Output;
            if (text.Length > 0)
            {
                Console.WriteLine();
                Console.WriteLine("Вывод программы:");
                Console.WriteLine(text);
            }

            if (dump || trace)
            {
                Console.WriteLine();
                Console.WriteLine("Состояние процессора:");
                Console.WriteLine(cpu.State.Describe());
                Console.WriteLine("Флаги: CF=" + (cpu.State.Carry ? 1 : 0) +
                    " PF=" + (cpu.State.Parity ? 1 : 0) +
                    " AF=" + (cpu.State.Auxiliary ? 1 : 0) +
                    " ZF=" + (cpu.State.Zero ? 1 : 0) +
                    " SF=" + (cpu.State.Sign ? 1 : 0) +
                    " DF=" + (cpu.State.Direction ? 1 : 0) +
                    " OF=" + (cpu.State.Overflow ? 1 : 0));
                DumpMemory(memory, result, 0x0100, 64);
            }

            return 0;
        }

        private static void DumpMemory(I8086Memory memory, I8086AssembleResult result, int start, int count)
        {
            Console.WriteLine();
            Console.WriteLine("Память (" + start.ToString("X4") + "h):");
            for (int row = 0; row < count / 16; row++)
            {
                int address = start + row * 16;
                StringBuilder builder = new StringBuilder();
                builder.Append(address.ToString("X4")).Append("h  ");
                for (int i = 0; i < 16; i++)
                    builder.Append(memory.ReadByte(address + i).ToString("X2")).Append(' ');
                Console.WriteLine(builder.ToString());
            }
        }

        private static string BytesToText(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return string.Empty;

            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < bytes.Length; i++)
            {
                if (i > 0)
                    builder.Append(' ');
                builder.Append(bytes[i].ToString("X2"));
            }

            return builder.ToString();
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
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (!string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    continue;
                int value;
                if (int.TryParse(args[i + 1], out value))
                    return value;
            }

            return fallback;
        }
    }
}
