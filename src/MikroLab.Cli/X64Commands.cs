using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MikroLab.Core.Assembler;
using MikroLab.Core.Assembler.X64;
using MikroLab.Core.Cpu.I8086;
using MikroLab.Core.Cpu.X64;
using MikroLab.Core.Formats;
using MikroLab.Core.Memory;

namespace MikroLab.Cli
{
    /// <summary>
    /// Command line support for the x86-64 architecture: assembling a source
    /// file in Intel syntax, running it and dumping the processor state.
    /// </summary>
    internal static class X64Commands
    {
        public const ulong DefaultOrigin = 0x0040_0000;

        public static int Assemble(string[] args, string sourcePath, string output)
        {
            string source = File.ReadAllText(sourcePath);
            string directory = Path.GetDirectoryName(Path.GetFullPath(sourcePath));
            ulong origin = OriginOption(args);
            X64AssembleResult result = X64Assembler.Assemble(source, origin, directory);

            foreach (AssemblerError error in result.Errors)
                Console.Error.WriteLine("Строка " + error.LineNumber + ": " + error.Message);

            if (!result.Success)
                return 1;

            Console.WriteLine("Собрано: " + result.Image.Length + " байт, символов: " +
                result.Symbols.Count + ", начало: " + result.Origin.ToString("X") + "h");

            if (HasFlag(args, "--listing"))
            {
                Console.WriteLine();
                Console.WriteLine("Листинг:");
                foreach (X64ListingLine line in result.Listing)
                {
                    if (!line.HasAddress)
                        continue;
                    Console.WriteLine(line.Offset.ToString("X8") + "h  " +
                        BytesToText(line.Bytes) + "  " + line.Source.Trim());
                }
            }

            if (!string.IsNullOrEmpty(output))
            {
                string extension = Path.GetExtension(output).ToLowerInvariant();
                if (extension == ".hex" || extension == ".ihx")
                    IntelHex.Save(output, result.Image, (ushort)result.Origin, (ushort)result.EndAddress);
                else
                    File.WriteAllBytes(output, result.Image);
                Console.WriteLine("Записано: " + output);
            }

            return 0;
        }

        public static int Run(string[] args, string sourcePath)
        {
            string source = File.ReadAllText(sourcePath);
            string directory = Path.GetDirectoryName(Path.GetFullPath(sourcePath));
            ulong origin = OriginOption(args);
            X64AssembleResult result = X64Assembler.Assemble(source, origin, directory);

            foreach (AssemblerError error in result.Errors)
                Console.Error.WriteLine("Строка " + error.LineNumber + ": " + error.Message);

            if (!result.Success)
                return 1;

            int maxSteps = GetIntOption(args, "--steps", 1000000);
            bool dump = HasFlag(args, "--dump");

            X64Memory memory = new X64Memory();
            I8086Console console = new I8086Console();
            X64Cpu cpu = new X64Cpu(memory, console);
            cpu.LoadProgram(result.Image, result.Origin);

            int executed = 0;
            while (executed < maxSteps && cpu.Step())
                executed++;

            Console.WriteLine("Выполнено команд: " + executed +
                (cpu.Halted ? ", программа завершилась" : ", останов по лимиту шагов"));
            if (!string.IsNullOrEmpty(cpu.Fault))
                Console.WriteLine("Ошибка: " + cpu.Fault);

            string text = console.Output;
            if (text.Length > 0)
            {
                Console.WriteLine();
                Console.WriteLine("Вывод программы:");
                Console.WriteLine(text);
            }

            if (dump)
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
            }

            return 0;
        }

        private static ulong OriginOption(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (!string.Equals(args[i], "--origin", StringComparison.OrdinalIgnoreCase))
                    continue;
                string text = args[i + 1];
                if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    text = text.Substring(2);
                ulong value;
                if (ulong.TryParse(text, System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture, out value))
                    return value;
            }

            return DefaultOrigin;
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
