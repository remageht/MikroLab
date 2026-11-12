using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MikroLab.Core.Assembler.X64
{
    public static partial class X64Assembler
    {
        // ------------------------------------------------------------------
        // разбор операндов
        // ------------------------------------------------------------------

        private static X64Operand ParseOperand(string text, out string error)
        {
            error = null;
            X64Operand operand = new X64Operand();
            string work = (text ?? string.Empty).Trim();
            if (work.Length == 0)
            {
                error = "пустой операнд";
                return operand;
            }

            string lower = work.ToLowerInvariant();

            int legacy = Array.IndexOf(LegacyByteNames, lower);
            if (legacy >= 0)
            {
                operand.Kind = X64OperandKind.Register8;
                operand.Register = 4 + legacy;             // ah/ch/dh/bh
                operand.Size = 1;
                return operand;
            }

            int index = Array.IndexOf(Register64Names, lower);
            if (index >= 0)
            {
                operand.Kind = X64OperandKind.Register64;
                operand.Register = index;
                operand.Size = 8;
                return operand;
            }

            index = Array.IndexOf(Register32Names, lower);
            if (index >= 0)
            {
                operand.Kind = X64OperandKind.Register32;
                operand.Register = index;
                operand.Size = 4;
                return operand;
            }

            index = Array.IndexOf(Register16Names, lower);
            if (index >= 0)
            {
                operand.Kind = X64OperandKind.Register16;
                operand.Register = index;
                operand.Size = 2;
                return operand;
            }

            index = Array.IndexOf(Register8Names, lower);
            if (index >= 0)
            {
                operand.Kind = X64OperandKind.Register8;
                operand.Register = index;
                operand.Size = 1;
                return operand;
            }

            index = Array.IndexOf(SegmentNames, lower);
            if (index >= 0)
            {
                operand.Kind = X64OperandKind.SegmentRegister;
                operand.Register = index;
                operand.Size = 2;
                return operand;
            }

            string lowerWork = work.ToLowerInvariant();
            foreach (string sizePrefix in new[] { "byte ", "word ", "dword ", "qword " })
            {
                if (!lowerWork.StartsWith(sizePrefix, StringComparison.Ordinal))
                    continue;
                operand.Size = sizePrefix == "byte " ? 1 : sizePrefix == "word " ? 2
                    : sizePrefix == "dword " ? 4 : 8;
                work = work.Substring(sizePrefix.Length).Trim();
                lowerWork = work.ToLowerInvariant();
                break;
            }

            if (work.StartsWith("[", StringComparison.Ordinal) && work.EndsWith("]", StringComparison.Ordinal))
            {
                operand.Kind = X64OperandKind.Memory;
                ParseMemory(work.Substring(1, work.Length - 2), operand, out error);
                return operand;
            }

            operand.Kind = X64OperandKind.Immediate;
            operand.Expression = work;
            operand.Size = 0;
            return operand;
        }

        private static void ParseMemory(string content, X64Operand operand, out string error)
        {
            error = null;
            string work = content.Trim();

            // "rel label" и "qword label"
            string lower = work.ToLowerInvariant();
            if (lower.StartsWith("rel ", StringComparison.Ordinal))
            {
                operand.RipRelative = true;
                work = work.Substring(4).Trim();
                lower = work.ToLowerInvariant();
            }

            foreach (string size in new[] { "byte ", "word ", "dword ", "qword " })
            {
                if (lower.StartsWith(size, StringComparison.Ordinal))
                {
                    operand.Size = size == "byte " ? 1 : size == "word " ? 2 : size == "dword " ? 4 : 8;
                    work = work.Substring(size.Length).Trim();
                    lower = work.ToLowerInvariant();
                }
            }

            if (lower.StartsWith("rip+", StringComparison.Ordinal) || lower.StartsWith("eip+", StringComparison.Ordinal))
            {
                operand.RipRelative = true;
                work = work.Substring(4).Trim();
                lower = work.ToLowerInvariant();
            }

            // разбор слагаемых: [base + index*scale + displacement]
            string[] parts = SplitMemoryTerms(work);
            List<string> displacementParts = new List<string>();

            foreach (string part in parts)
            {
                string term = part.Trim();
                if (term.Length == 0)
                    continue;

                int star = term.IndexOf('*');
                if (star > 0)
                {
                    string registerName = term.Substring(0, star).Trim().ToLowerInvariant();
                    string scaleText = term.Substring(star + 1).Trim();
                    int scale = 1;
                    if (!int.TryParse(scaleText, NumberStyles.Integer, CultureInfo.InvariantCulture, out scale))
                        scale = 1;
                    operand.IndexRegister = IndexOfRegister64(registerName);
                    operand.Scale = scale;
                    if (operand.IndexRegister < 0)
                    {
                        error = "неизвестный регистр индекса: " + registerName;
                        return;
                    }

                    continue;
                }

                string name = term.ToLowerInvariant();
                int register = IndexOfRegister64(name);
                if (register >= 0)
                {
                    if (operand.BaseRegister >= 0)
                    {
                        error = "слишком много регистров в адресе";
                        return;
                    }

                    operand.BaseRegister = register;
                    continue;
                }

                displacementParts.Add(term);
            }

            if (displacementParts.Count > 0)
            {
                operand.Expression = string.Join(" + ", displacementParts.ToArray());
                operand.HasDisplacement = true;

                // метка без базовых регистров — относительный адрес
                if (operand.BaseRegister < 0 && operand.IndexRegister < 0 && !IsNumber(operand.Expression))
                    operand.RipRelative = true;
            }
        }

        /// <summary>True when the expression is a plain number.</summary>
        private static bool IsNumber(string expression)
        {
            string work = (expression ?? string.Empty).Trim();
            if (work.Length == 0)
                return false;
            if (work.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return true;
            if (work.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
                return true;
            if (work.EndsWith("h", StringComparison.OrdinalIgnoreCase))
                return true;

            foreach (char c in work)
            {
                if (!char.IsDigit(c) && c != '+' && c != '-' && c != ' ')
                    return false;
            }

            return true;
        }

        private static int IndexOfRegister64(string name)
        {
            int index = Array.IndexOf(Register64Names, name);
            if (index >= 0)
                return index;
            return -1;
        }

        private static string[] SplitMemoryTerms(string text)
        {
            List<string> parts = new List<string>();
            int depth = 0;
            int start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '(')
                    depth++;
                else if (c == ')')
                    depth--;
                else if ((c == '+' || c == '-') && depth == 0 && i > 0 && text[i - 1] != '+' && text[i - 1] != '-')
                {
                    parts.Add(text.Substring(start, i - start));
                    start = i;
                }
            }

            parts.Add(text.Substring(start));
            return parts.ToArray();
        }

        // ------------------------------------------------------------------
        // размещение и кодирование
        // ------------------------------------------------------------------

        private static void Layout(
            List<SourceLine> lines,
            Dictionary<string, long> symbols,
            HashSet<int> nearJumps,
            ulong origin,
            List<AssemblerError> errors)
        {
            foreach (SourceLine line in lines)
            {
                if (line.Mnemonic != "EQU")
                    continue;
                if (string.IsNullOrEmpty(line.Label))
                {
                    errors.Add(new AssemblerError(line.Number, "EQU требует метку"));
                    continue;
                }

                string expression = line.RawOperands.Length > 0 ? line.RawOperands[0] : string.Empty;
                long value;
                string error;
                if (TryEvaluate(expression, symbols, out value, out error))
                    symbols[line.Label] = value;
                else
                    errors.Add(new AssemblerError(line.Number, error));
            }

            foreach (SourceLine line in lines)
                PrepareJump(line);

            // начинаем с длинных форм переходов и укорачиваем их по мере
            // того, как выясняются расстояния
            foreach (SourceLine line in lines)
            {
                if (line.IsRelativeJump)
                    nearJumps.Add(line.Number);
            }

            for (int iteration = 0; iteration < 24; iteration++)
            {
                bool changed = false;
                long location = (long)origin;

                foreach (SourceLine line in lines)
                {
                    if (line.Mnemonic == "ORG")
                    {
                        line.Offset = location;
                        line.Size = 0;
                        long value;
                        string error;
                        if (TryEvaluate(line.RawOperands.Length > 0 ? line.RawOperands[0] : "0",
                                symbols, out value, out error))
                        {
                            if (value != location)
                            {
                                location = value;
                                changed = true;
                            }
                        }

                        continue;
                    }

                    line.Offset = location;
                    line.IsNearJump = nearJumps.Contains(line.Number);

                    MemoryStreamHelper buffer = new MemoryStreamHelper();
                    string emitError = EmitLine(line, buffer, symbols, false);
                    if (emitError == null && line.Size != buffer.Length)
                    {
                        line.Size = buffer.Length;
                        changed = true;
                    }

                    location += line.Size;
                }

                foreach (SourceLine line in lines)
                {
                    if (!string.IsNullOrEmpty(line.Label) && line.Mnemonic != "EQU")
                        symbols[line.Label] = line.Offset;
                }

                foreach (SourceLine line in lines)
                {
                    if (!line.IsRelativeJump || line.JumpTarget == null)
                        continue;

                    long target;
                    if (!symbols.TryGetValue(line.JumpTarget, out target))
                        continue;

                    long distance = target - (line.Offset + 2);
                    bool fits = distance >= -128 && distance <= 127;
                    bool wantNear = !fits || line.ForceNear;
                    if (line.ForceShort)
                        wantNear = false;

                    if (wantNear && !nearJumps.Contains(line.Number))
                    {
                        nearJumps.Add(line.Number);
                        changed = true;
                    }
                    else if (!wantNear && nearJumps.Contains(line.Number) && !line.ForceNear)
                    {
                        nearJumps.Remove(line.Number);
                        changed = true;
                    }
                }

                if (!changed)
                    break;
            }

            foreach (SourceLine line in lines)
            {
                line.IsNearJump = nearJumps.Contains(line.Number);
                MemoryStreamHelper buffer = new MemoryStreamHelper();
                string error = EmitLine(line, buffer, symbols, true);
                line.Data = buffer.ToArray();
                if (error != null)
                {
                    errors.Add(new AssemblerError(line.Number, error));
                    continue;
                }

                if (line.Data.Length != line.Size)
                    errors.Add(new AssemblerError(line.Number, "размер инструкции не совпадает"));
            }
        }

        private static void PrepareJump(SourceLine line)
        {
            if (!IsRelativeJumpMnemonic(line.Mnemonic))
                return;
            if (line.RawOperands.Length != 1)
                return;

            string text = line.RawOperands[0].Trim();
            if (text.Length == 0 || text.StartsWith("[", StringComparison.Ordinal))
                return;

            string[] parts = text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
            {
                if (parts[0].Equals("short", StringComparison.OrdinalIgnoreCase))
                {
                    line.ForceNear = false;
                    line.ForceShort = true;
                    line.JumpTarget = parts[1];
                    line.IsRelativeJump = true;
                    return;
                }

                if (parts[0].Equals("near", StringComparison.OrdinalIgnoreCase))
                {
                    line.ForceNear = true;
                    line.JumpTarget = parts[1];
                    line.IsRelativeJump = true;
                    return;
                }
            }

            if (parts.Length != 1)
                return;

            string lower = parts[0].ToLowerInvariant();
            if (IndexOfRegister64(lower) >= 0 || Array.IndexOf(SegmentNames, lower) >= 0)
                return;

            line.JumpTarget = parts[0];
            line.IsRelativeJump = true;
        }

        private static bool IsRelativeJumpMnemonic(string mnemonic)
        {
            if (mnemonic == "JMP" || mnemonic == "CALL" || mnemonic == "LOOP" || mnemonic == "JCXZ")
                return true;
            if (mnemonic.StartsWith("J", StringComparison.Ordinal) && mnemonic.Length == 3)
                return true;
            return false;
        }

        private static bool TryEvaluate(
            string expression,
            Dictionary<string, long> symbols,
            out long value,
            out string error)
        {
            value = 0;
            error = null;
            if (string.IsNullOrEmpty(expression))
            {
                error = "пустое выражение";
                return false;
            }

            string work = expression.Trim();

            // двоичные, восьмеричные и шестнадцатеричные литералы
            if (work.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                if (long.TryParse(work.Substring(2), NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture, out value))
                    return true;
            }

            if (work.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
            {
                if (long.TryParse(work.Substring(2), NumberStyles.BinaryNumber,
                        CultureInfo.InvariantCulture, out value))
                    return true;
            }

            if (work.EndsWith("h", StringComparison.OrdinalIgnoreCase) && work.Length > 1)
            {
                if (long.TryParse(work.Substring(0, work.Length - 1), NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture, out value))
                    return true;
            }

            long result;
            string evaluated;
            if (X64Expression.TryEvaluate(work, symbols, out result, out evaluated))
            {
                value = result;
                return true;
            }

            error = evaluated;
            return false;
        }

        /// <summary>Simple growable byte buffer.</summary>
        private sealed class MemoryStreamHelper
        {
            private byte[] _buffer = new byte[256];
            private int _length;

            public void Reset() { _length = 0; }

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

            public void WriteUInt32(uint value)
            {
                Write((byte)(value & 0xFF));
                Write((byte)((value >> 8) & 0xFF));
                Write((byte)((value >> 16) & 0xFF));
                Write((byte)((value >> 24) & 0xFF));
            }

            public void WriteUInt64(ulong value)
            {
                for (int i = 0; i < 8; i++)
                    Write((byte)((value >> (8 * i)) & 0xFF));
            }

            public void WriteInt32(int value) => WriteUInt32((uint)value);

            public int Length => _length;

            /// <summary>Overwrites a previously written byte (RIP-relative displacement).</summary>
            public void WriteAt(int index, byte value)
            {
                if (index >= 0 && index < _length)
                    _buffer[index] = value;
            }

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
    }
}
