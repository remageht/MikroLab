using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MikroLab.Core.Assembler.I8086
{
    /// <summary>
    /// Assembler for the Intel 8086 instruction set in Intel syntax (the
    /// dialect used by NASM). The output is a flat image that starts at the
    /// address given by the ORG directive, which is exactly the layout of a
    /// DOS .COM program.
    /// </summary>
    public static partial class I8086Assembler
    {
        internal sealed class SourceLine
        {
            public int Number;
            public string Raw = string.Empty;
            public string Label;
            public string Mnemonic = string.Empty;
            public string[] RawOperands = Array.Empty<string>();
            public I8086Operand[] Operands = Array.Empty<I8086Operand>();
            public int Offset;
            public int Size;
            public bool IsRelativeJump;
            public bool IsNearJump;
            public bool ForceNear;
            public bool ForceShort;
            public string JumpTarget;
            public byte[] Data = Array.Empty<byte>();
        }

        private static readonly HashSet<string> Directives = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ORG", "DB", "DW", "DD", "DT", "TIMES", "EQU", "RESB", "RESW", "RESD",
            "SEGMENT", "ENDS", "ASSUME", "BITS", "ALIGN", "INCBIN", "INCLUDE"
        };

        private static readonly string[] Register8Names = { "al", "cl", "dl", "bl", "ah", "ch", "dh", "bh" };
        private static readonly string[] Register16Names = { "ax", "cx", "dx", "bx", "sp", "bp", "si", "di" };
        private static readonly string[] SegmentNames = { "es", "cs", "ss", "ds" };

        /// <summary>Assembles a source file. The program origin defaults to 0100h.</summary>
        public static I8086AssembleResult Assemble(string source)
        {
            return Assemble(source, 0x0100, null);
        }

        /// <summary>Assembles a source file with an explicit program origin.</summary>
        public static I8086AssembleResult Assemble(string source, ushort origin)
        {
            return Assemble(source, origin, null);
        }

        /// <summary>
        /// Assembles a source file. <paramref name="baseDirectory"/> is used to
        /// resolve the files referenced by %include.
        /// </summary>
        public static I8086AssembleResult Assemble(string source, ushort origin, string baseDirectory)
        {
            List<AssemblerError> errors = new List<AssemblerError>();
            List<SourceLine> lines = ParseLines(source ?? string.Empty, errors, baseDirectory, new HashSet<string>());

            Dictionary<string, ushort> symbols = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);
            HashSet<int> nearJumps = new HashSet<int>();

            Layout(lines, symbols, nearJumps, origin, errors);

            List<I8086ListingLine> listing = new List<I8086ListingLine>();
            foreach (SourceLine line in lines)
            {
                listing.Add(new I8086ListingLine(
                    line.Number,
                    line.Raw,
                    !string.IsNullOrEmpty(line.Mnemonic),
                    (ushort)line.Offset,
                    line.Data));
            }

            errors.Sort(delegate(AssemblerError a, AssemblerError b)
            {
                return a.LineNumber.CompareTo(b.LineNumber);
            });

            ushort end = origin;
            foreach (SourceLine line in lines)
            {
                int endOfLine = line.Offset + line.Size;
                if (endOfLine > end)
                    end = (ushort)endOfLine;
            }

            int imageLength = Math.Max(0, end - origin);
            byte[] flat = new byte[imageLength];
            foreach (SourceLine line in lines)
            {
                int index = line.Offset - origin;
                if (index < 0)
                    continue;
                for (int i = 0; i < line.Size && i < line.Data.Length; i++)
                {
                    if (index + i < flat.Length)
                        flat[index + i] = line.Data[i];
                }
            }

            Dictionary<string, ushort> frozen = new Dictionary<string, ushort>(symbols, StringComparer.OrdinalIgnoreCase);
            return new I8086AssembleResult(
                errors.Count == 0,
                flat,
                origin,
                end,
                listing,
                errors,
                frozen);
        }

        /// <summary>Simple growable byte buffer used while emitting.</summary>
        private sealed class MemoryStreamHelper
        {
            private byte[] _buffer = new byte[256];
            private int _length;

            public void Reset()
            {
                _length = 0;
            }

            public void Write(byte value)
            {
                Ensure(_length + 1);
                _buffer[_length++] = value;
            }

            public void Write(byte[] values)
            {
                if (values == null)
                    return;
                Ensure(_length + values.Length);
                Array.Copy(values, 0, _buffer, _length, values.Length);
                _length += values.Length;
            }

            public void WriteWord(ushort value)
            {
                Write((byte)(value & 0xFF));
                Write((byte)(value >> 8));
            }

            public void WriteDword(uint value)
            {
                Write((byte)(value & 0xFF));
                Write((byte)((value >> 8) & 0xFF));
                Write((byte)((value >> 16) & 0xFF));
                Write((byte)((value >> 24) & 0xFF));
            }

            public int Length => _length;

            public byte[] ToArray()
            {
                byte[] result = new byte[_length];
                Array.Copy(_buffer, result, _length);
                return result;
            }

            private void Ensure(int capacity)
            {
                if (capacity <= _buffer.Length)
                    return;
                int size = _buffer.Length;
                while (size < capacity)
                    size *= 2;
                byte[] grown = new byte[size];
                Array.Copy(_buffer, grown, _length);
                _buffer = grown;
            }
        }

        // ------------------------------------------------------------------
        // parsing
        // ------------------------------------------------------------------

        private static List<SourceLine> ParseLines(
            string source,
            List<AssemblerError> errors,
            string baseDirectory,
            HashSet<string> includedFiles)
        {
            List<SourceLine> lines = new List<SourceLine>();
            string[] raw = source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            for (int i = 0; i < raw.Length; i++)
            {
                SourceLine line = new SourceLine
                {
                    Number = i + 1,
                    Raw = raw[i]
                };

                string text = StripComment(raw[i]).Trim();
                if (text.Length == 0)
                {
                    lines.Add(line);
                    continue;
                }

                // %include "file.asm"
                if (text.StartsWith("%include", StringComparison.OrdinalIgnoreCase))
                {
                    string fileName = ExtractIncludeName(text);
                    if (fileName == null)
                    {
                        errors.Add(new AssemblerError(line.Number, "%include требует имя файла"));
                        lines.Add(line);
                        continue;
                    }

                    string fullPath = ResolveInclude(fileName, baseDirectory);
                    if (fullPath == null || !File.Exists(fullPath))
                    {
                        errors.Add(new AssemblerError(line.Number, "файл не найден: " + fileName));
                        lines.Add(line);
                        continue;
                    }

                    string key = Path.GetFullPath(fullPath);
                    if (includedFiles.Contains(key))
                    {
                        errors.Add(new AssemblerError(line.Number, "циклический %include: " + fileName));
                        lines.Add(line);
                        continue;
                    }

                    includedFiles.Add(key);
                    string directory = Path.GetDirectoryName(key);
                    List<SourceLine> included = ParseLines(
                        File.ReadAllText(fullPath),
                        errors,
                        directory,
                        includedFiles);
                    lines.AddRange(included);
                    continue;
                }

                // label
                int colon = FindLabelColon(text);
                if (colon > 0)
                {
                    line.Label = text.Substring(0, colon).Trim();
                    text = text.Substring(colon + 1).Trim();
                }
                else
                {
                    // "NAME equ value" defines a constant without a colon
                    int separator = FindOperandSeparator(text);
                    if (separator > 0)
                    {
                        string head = text.Substring(0, separator).Trim();
                        string tail = text.Substring(separator + 1).Trim();
                        string tailWord = tail.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)[0]
                            .ToUpperInvariant();
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

                int space = FindOperandSeparator(text);
                line.Mnemonic = (space < 0 ? text : text.Substring(0, space)).Trim().ToUpperInvariant();
                string operands = space < 0 ? string.Empty : text.Substring(space + 1).Trim();

                if (line.Mnemonic == "TIMES")
                {
                    // "times 5 db 0" has no comma between the count and the item
                    int split = FindOperandSeparator(operands);
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

        private static string ExtractIncludeName(string text)
        {
            int start = text.IndexOf('"');
            if (start >= 0)
            {
                int end = text.IndexOf('"', start + 1);
                if (end > start)
                    return text.Substring(start + 1, end - start - 1);
            }

            start = text.IndexOf('\'');
            if (start >= 0)
            {
                int end = text.IndexOf('\'', start + 1);
                if (end > start)
                    return text.Substring(start + 1, end - start - 1);
            }

            string[] parts = text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 1 ? parts[1] : null;
        }

        private static string ResolveInclude(string fileName, string baseDirectory)
        {
            if (Path.IsPathRooted(fileName))
                return fileName;

            if (string.IsNullOrEmpty(baseDirectory))
                return fileName;

            return Path.Combine(baseDirectory, fileName);
        }

        private static bool IsIdentifier(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;
            for (int i = 0; i < text.Length; i++)
            {
                if (!AsmText.IsIdentifierPart(text[i]) && text[i] != '.')
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

                if (c == ':')
                {
                    // a label must be an identifier in front of the colon
                    string head = text.Substring(0, i).Trim();
                    if (head.Length == 0)
                        return -1;
                    for (int j = 0; j < head.Length; j++)
                    {
                        if (!AsmText.IsIdentifierPart(head[j]) && head[j] != '.')
                            return -1;
                    }

                    return i;
                }
            }

            return -1;
        }

        private static int FindOperandSeparator(string text)
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
    }
}
