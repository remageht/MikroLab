using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MikroLab.Core.Assembler.X64
{
    /// <summary>
    /// Assembler for the x86-64 long mode in Intel syntax (NASM dialect).
    /// The output is a flat image; labels are resolved relative to the
    /// instruction pointer by default (<c>default rel</c>).
    /// </summary>
    public static partial class X64Assembler
    {
        private static readonly string[] Register64Names =
        {
            "rax", "rcx", "rdx", "rbx", "rsp", "rbp", "rsi", "rdi",
            "r8", "r9", "r10", "r11", "r12", "r13", "r14", "r15"
        };

        private static readonly string[] Register32Names =
        {
            "eax", "ecx", "edx", "ebx", "esp", "ebp", "esi", "edi",
            "r8d", "r9d", "r10d", "r11d", "r12d", "r13d", "r14d", "r15d"
        };

        private static readonly string[] Register16Names =
        {
            "ax", "cx", "dx", "bx", "sp", "bp", "si", "di",
            "r8w", "r9w", "r10w", "r11w", "r12w", "r13w", "r14w", "r15w"
        };

        private static readonly string[] Register8Names =
        {
            "al", "cl", "dl", "bl", "spl", "bpl", "sil", "dil",
            "r8b", "r9b", "r10b", "r11b", "r12b", "r13b", "r14b", "r15b"
        };

        private static readonly string[] LegacyByteNames = { "ah", "ch", "dh", "bh" };
        private static readonly string[] SegmentNames = { "es", "cs", "ss", "ds", "fs", "gs" };

        internal sealed class SourceLine
        {
            public int Number;
            public string Raw = string.Empty;
            public string Label;
            public string Mnemonic = string.Empty;
            public string[] RawOperands = Array.Empty<string>();
            public X64Operand[] Operands = Array.Empty<X64Operand>();
            public long Offset;
            public int Size;
            public bool IsRelativeJump;
            public bool IsNearJump;
            public bool ForceShort;
            public bool ForceNear;
            public string JumpTarget;
            public byte[] Data = Array.Empty<byte>();
        }

        /// <summary>Assembles a source file; the origin defaults to 0100h.</summary>
        public static X64AssembleResult Assemble(string source)
        {
            return Assemble(source, 0x0100, null);
        }

        public static X64AssembleResult Assemble(string source, ulong origin, string baseDirectory)
        {
            List<AssemblerError> errors = new List<AssemblerError>();
            List<SourceLine> lines = ParseLines(source ?? string.Empty, errors, baseDirectory,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase));

            Dictionary<string, long> symbols = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            HashSet<int> nearJumps = new HashSet<int>();

            Layout(lines, symbols, nearJumps, origin, errors);

            byte[] image = new byte[0];
            ulong end = origin;
            foreach (SourceLine line in lines)
            {
                long lineEnd = line.Offset + line.Size;
                if (lineEnd > 0 && (ulong)lineEnd > end)
                    end = (ulong)lineEnd;
            }

            int length = (int)Math.Max(0UL, end - origin);
            image = new byte[length];
            foreach (SourceLine line in lines)
            {
                int index = (int)(line.Offset - (long)origin);
                if (index < 0)
                    continue;
                for (int i = 0; i < line.Size && i < line.Data.Length; i++)
                {
                    if (index + i < image.Length)
                        image[index + i] = line.Data[i];
                }
            }

            List<X64ListingLine> listing = new List<X64ListingLine>();
            foreach (SourceLine line in lines)
            {
                listing.Add(new X64ListingLine(
                    line.Number,
                    line.Raw,
                    !string.IsNullOrEmpty(line.Mnemonic),
                    (ulong)line.Offset,
                    line.Data));
            }

            errors.Sort(delegate(AssemblerError a, AssemblerError b)
            {
                return a.LineNumber.CompareTo(b.LineNumber);
            });

            Dictionary<string, long> frozen = new Dictionary<string, long>(symbols, StringComparer.OrdinalIgnoreCase);
            return new X64AssembleResult(
                errors.Count == 0,
                image,
                origin,
                end,
                listing,
                errors,
                frozen);
        }

        // ------------------------------------------------------------------
        // разбор исходного текста
        // ------------------------------------------------------------------

        private static List<SourceLine> ParseLines(
            string source,
            List<AssemblerError> errors,
            string baseDirectory,
            HashSet<string> included)
        {
            List<SourceLine> lines = new List<SourceLine>();
            string[] raw = source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            for (int i = 0; i < raw.Length; i++)
            {
                SourceLine line = new SourceLine { Number = i + 1, Raw = raw[i] };
                string text = StripComment(raw[i]).Trim();

                if (text.Length == 0)
                {
                    lines.Add(line);
                    continue;
                }

                if (text.StartsWith("%include", StringComparison.OrdinalIgnoreCase))
                {
                    string fileName = ExtractIncludeName(text);
                    string fullPath = fileName == null || string.IsNullOrEmpty(baseDirectory)
                        ? fileName
                        : Path.Combine(baseDirectory, fileName);

                    if (fullPath == null || !File.Exists(fullPath))
                    {
                        errors.Add(new AssemblerError(line.Number, "файл не найден: " + fileName));
                        lines.Add(line);
                        continue;
                    }

                    string key = Path.GetFullPath(fullPath);
                    if (!included.Add(key))
                    {
                        errors.Add(new AssemblerError(line.Number, "циклический %include: " + fileName));
                        lines.Add(line);
                        continue;
                    }

                    lines.AddRange(ParseLines(File.ReadAllText(fullPath), errors,
                        Path.GetDirectoryName(key), included));
                    continue;
                }

                int colon = FindLabelColon(text);
                if (colon > 0)
                {
                    line.Label = text.Substring(0, colon).Trim();
                    text = text.Substring(colon + 1).Trim();
                }
                else
                {
                    int separator = FindSeparator(text);
                    if (separator > 0)
                    {
                        string head = text.Substring(0, separator).Trim();
                        string tail = text.Substring(separator + 1).Trim();
                        string tailWord = FirstWord(tail).ToUpperInvariant();
                        if (IsIdentifier(head) && (tailWord == "EQU" || tail.StartsWith("=", StringComparison.Ordinal)))
                        {
                            line.Label = head;
                            text = tail;
                        }
                    }
                }

                if (text.Length == 0)
                {
                    lines.Add(line);
                    continue;
                }

                int space = FindSeparator(text);
                line.Mnemonic = FirstWord(text).ToUpperInvariant();
                string operands = space < 0 ? string.Empty : text.Substring(space + 1).Trim();

                if (line.Mnemonic == "TIMES")
                {
                    // "times 5 db 0" — между количеством и элементом нет запятой
                    int split = FindSeparator(operands);
                    if (split > 0)
                    {
                        line.RawOperands = new[]
                        {
                            operands.Substring(0, split).Trim(),
                            operands.Substring(split + 1).Trim()
                        };
                    }
                    else
                    {
                        line.RawOperands = Array.Empty<string>();
                    }

                    lines.Add(line);
                    continue;
                }

                line.RawOperands = SplitOperands(operands);
                lines.Add(line);
            }

            return lines;
        }

        private static string FirstWord(string text)
        {
            string[] parts = text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 0 ? parts[0] : string.Empty;
        }

        private static bool IsIdentifier(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;
            foreach (char c in text)
            {
                if (!char.IsLetterOrDigit(c) && c != '_' && c != '.' && c != '@' && c != '$')
                    return false;
            }

            return true;
        }

        private static string StripComment(string text)
        {
            bool inString = false;
            char quote = '\0';
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (inString)
                {
                    if (c == quote)
                        inString = false;
                    continue;
                }

                if (c == '\'' || c == '"')
                {
                    inString = true;
                    quote = c;
                    continue;
                }

                if (c == ';')
                    return text.Substring(0, i);
            }

            return text;
        }

        private static int FindLabelColon(string text)
        {
            bool inString = false;
            char quote = '\0';
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (inString)
                {
                    if (c == quote)
                        inString = false;
                    continue;
                }

                if (c == '\'' || c == '"')
                {
                    inString = true;
                    quote = c;
                    continue;
                }

                if (c == ':' && i > 0)
                {
                    string head = text.Substring(0, i).Trim();
                    return IsIdentifier(head) ? i : -1;
                }
            }

            return -1;
        }

        private static int FindSeparator(string text)
        {
            bool inString = false;
            char quote = '\0';
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (inString)
                {
                    if (c == quote)
                        inString = false;
                    continue;
                }

                if (c == '\'' || c == '"')
                {
                    inString = true;
                    quote = c;
                    continue;
                }

                if (c == ' ' || c == '\t')
                    return i;
            }

            return -1;
        }

        private static string[] SplitOperands(string text)
        {
            if (string.IsNullOrEmpty(text))
                return Array.Empty<string>();

            List<string> parts = new List<string>();
            int depth = 0;
            bool inString = false;
            char quote = '\0';
            StringBuilder current = new StringBuilder();

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (inString)
                {
                    current.Append(c);
                    if (c == quote)
                        inString = false;
                    continue;
                }

                if (c == '\'' || c == '"')
                {
                    inString = true;
                    quote = c;
                    current.Append(c);
                    continue;
                }

                if (c == '[')
                    depth++;
                else if (c == ']')
                    depth--;

                if (c == ',' && depth == 0)
                {
                    parts.Add(current.ToString().Trim());
                    current.Length = 0;
                    continue;
                }

                current.Append(c);
            }

            string last = current.ToString().Trim();
            if (last.Length > 0)
                parts.Add(last);
            return parts.ToArray();
        }

        private static string ExtractIncludeName(string text)
        {
            int start = text.IndexOf('"');
            if (start >= 0)
            {
                int end = text.IndexOf('"', start + 1);
                if (end > start)
                    return text.Substring(start + 1, end - start - 1);
            }

            string[] parts = text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 1 ? parts[1] : null;
        }
    }
}
