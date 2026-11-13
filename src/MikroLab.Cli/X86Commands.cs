using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using MikroLab.Core.Assembler;
using MikroLab.Core.Assembler.I8086;
using MikroLab.Core.Assembler.X64;
using MikroLab.Core.Debugging;
using MikroLab.Core.Formats;

namespace MikroLab.Cli
{
    /// <summary>
    /// Disassembler of the 8086 and the x86-64 on the command line: it turns
    /// machine code back into instructions, so the encoding of a program can
    /// be checked from the source listing.
    /// </summary>
    internal static class X86Commands
    {
        public static int Disassemble(string[] args, bool longMode)
        {
            ulong start = longMode ? 0x0040_0000UL : 0x0100UL;
            string startText = GetOption(args, "--start");
            if (startText != null) start = ParseAddress(startText);

            string hex = GetOption(args, "--hex");
            byte[] code;

            if (hex != null)
            {
                code = ParseHex(hex);
            }
            else
            {
                string path = SourceArgument(args);
                code = LoadCode(path, longMode, ref start);
            }

            int count = GetIntOption(args, "--count", 32);
            int offset = 0;

            while (offset < code.Length && count > 0)
            {
                DecodedInstruction instruction = longMode
                    ? X64Disassembler.Decode(code, offset, start + (ulong)offset)
                    : I8086Disassembler.Decode(code, offset, (ushort)(start + (ulong)offset));

                Console.WriteLine(instruction.Address.ToString(longMode ? "X8" : "X4") + "h  " +
                    instruction.BytesText.PadRight(24) + "  " + instruction.Text);

                offset += Math.Max(1, instruction.Length);
                count--;
            }

            return 0;
        }

        // ------------------------------------------------------------------
        // загрузка кода
        // ------------------------------------------------------------------

        private static byte[] LoadCode(string path, bool longMode, ref ulong start)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();

            if (extension == ".hex" || extension == ".ihx")
            {
                IntelHexImage image = IntelHex.Load(path);
                if (image.ByteCount == 0)
                    throw new InvalidDataException("Файл Intel HEX не содержит данных.");

                var code = new List<byte>();
                foreach (IntelHexSegment segment in image.Segments)
                {
                    for (int i = 0; i < segment.Length; i++)
                        code.Add(segment.Data[i]);
                }

                if (image.StartAddress.HasValue)
                    start = image.StartAddress.Value;

                return code.ToArray();
            }

            if (extension == ".asm" || extension == ".s" || extension == ".txt")
            {
                string source = File.ReadAllText(path);
                string directory = Path.GetDirectoryName(Path.GetFullPath(path));

                if (longMode)
                {
                    X64AssembleResult result = X64Assembler.Assemble(source, start, directory);
                    foreach (AssemblerError error in result.Errors)
                        Console.Error.WriteLine("Строка " + error.LineNumber + ": " + error.Message);
                    if (!result.Success)
                        return Array.Empty<byte>();
                    start = result.Origin;
                    return result.Image;
                }

                I8086AssembleResult i8086 = I8086Assembler.Assemble(source, (ushort)start, directory);
                foreach (AssemblerError error in i8086.Errors)
                    Console.Error.WriteLine("Строка " + error.LineNumber + ": " + error.Message);
                if (!i8086.Success)
                    return Array.Empty<byte>();
                start = i8086.Origin;
                return i8086.Image;
            }

            byte[] data = File.ReadAllBytes(path);
            if (data.Length == 0)
                throw new InvalidDataException("Файл пуст.");

            return data;
        }

        private static byte[] ParseHex(string text)
        {
            var bytes = new List<byte>();
            string cleaned = text.Replace(",", " ").Replace(";", " ");

            foreach (string token in cleaned.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string value = token.Trim();
                if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    value = value.Substring(2);

                if (value.EndsWith("h", StringComparison.OrdinalIgnoreCase))
                    value = value.Substring(0, value.Length - 1);

                if (value.Length == 0 || value.Length > 2)
                    throw new ArgumentException("Не разобрано значение: " + token);

                if (!byte.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte result))
                    throw new ArgumentException("Не разобрано значение: " + token);

                bytes.Add(result);
            }

            return bytes.ToArray();
        }

        // ------------------------------------------------------------------
        // разбор аргументов
        // ------------------------------------------------------------------

        private static string SourceArgument(string[] args)
        {
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i].StartsWith("--", StringComparison.Ordinal) ||
                    args[i].StartsWith("-", StringComparison.Ordinal))
                {
                    i++;
                    continue;
                }

                return args[i];
            }

            throw new ArgumentException("Укажите файл с машинным кодом или --hex.");
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

        private static int GetIntOption(string[] args, string name, int fallback)
        {
            string text = GetOption(args, name);
            if (text == null) return fallback;
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                throw new ArgumentException(name + " требует целое число.");
            return value;
        }

        private static ulong ParseAddress(string text)
        {
            string value = text.Trim();
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                value = value.Substring(2);
            if (value.EndsWith("h", StringComparison.OrdinalIgnoreCase))
                value = value.Substring(0, value.Length - 1);

            if (!ulong.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong result))
                throw new ArgumentException("Адрес должен быть шестнадцатеричным: " + text);

            return result;
        }
    }
}
