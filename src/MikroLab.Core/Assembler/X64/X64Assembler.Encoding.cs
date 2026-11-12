using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MikroLab.Core.Assembler.X64
{
    public static partial class X64Assembler
    {
        private static readonly Dictionary<string, int> GroupOperations = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "ADD", 0 }, { "OR", 1 }, { "ADC", 2 }, { "SBB", 3 },
            { "AND", 4 }, { "SUB", 5 }, { "XOR", 6 }, { "CMP", 7 }
        };

        private static readonly Dictionary<string, int> ConditionCodes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "JO", 0x0 }, { "JNO", 0x1 }, { "JB", 0x2 }, { "JAE", 0x3 },
            { "JE", 0x4 }, { "JNE", 0x5 }, { "JBE", 0x6 }, { "JA", 0x7 },
            { "JS", 0x8 }, { "JNS", 0x9 }, { "JP", 0xA }, { "JNP", 0xB },
            { "JL", 0xC }, { "JGE", 0xD }, { "JLE", 0xE }, { "JG", 0xF },
            { "JZ", 0x4 }, { "JNZ", 0x5 }, { "JC", 0x2 }, { "JNC", 0x3 },
            { "JNAE", 0x2 }, { "JNB", 0x3 }, { "JNBE", 0x7 }, { "JNA", 0x6 },
            { "JNG", 0xE }, { "JNL", 0xD }, { "JPE", 0xA }, { "JPO", 0xB }
        };

        private static readonly Dictionary<string, int> ShiftOperations = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "ROL", 0 }, { "ROR", 1 }, { "RCL", 2 }, { "RCR", 3 },
            { "SHL", 4 }, { "SAL", 4 }, { "SHR", 5 }, { "SAR", 6 }
        };

        private static string EmitLine(
            SourceLine line,
            MemoryStreamHelper output,
            Dictionary<string, long> symbols,
            bool reportErrors)
        {
            string mnemonic = line.Mnemonic;
            if (mnemonic.Length == 0)
                return null;

            switch (mnemonic)
            {
                case "ORG":
                case "EQU":
                case "SECTION":
                case "SEGMENT":
                case "ENDS":
                case "ASSUME":
                case "BITS":
                case "DEFAULT":
                case "GLOBAL":
                case "ALIGN":
                    return null;

                case "DB":
                case "DW":
                case "DD":
                case "DQ":
                    return EmitData(line, output, symbols);

                case "TIMES":
                    return EmitTimes(line, output, symbols);

                case "RESB":
                case "RESW":
                case "RESD":
                case "RESQ":
                {
                    long count;
                    string error;
                    if (!TryEvaluate(line.RawOperands.Length > 0 ? line.RawOperands[0] : "0",
                            symbols, out count, out error))
                        return reportErrors ? error : null;
                    int size = mnemonic == "RESB" ? 1 : mnemonic == "RESW" ? 2 : mnemonic == "RESD" ? 4 : 8;
                    for (long i = 0; i < count; i++)
                        for (int b = 0; b < size; b++)
                            output.Write((byte)0);
                    return null;
                }
            }

            X64Operand[] operands = new X64Operand[line.RawOperands.Length];
            for (int i = 0; i < line.RawOperands.Length; i++)
            {
                string parseError;
                operands[i] = ParseOperand(line.RawOperands[i], out parseError);
                if (parseError != null)
                    return reportErrors ? parseError : null;
            }

            line.Operands = operands;
            PendingPatches().Clear();
            try
            {
                string error = EmitInstruction(line, output, symbols, operands, reportErrors);
                ApplyPendingPatches(output, line);
                return error;
            }
            catch (X64AssemblyError failure)
            {
                PendingPatches().Clear();
                return reportErrors ? failure.Message : null;
            }
        }

        private static string EmitData(
            SourceLine line,
            MemoryStreamHelper output,
            Dictionary<string, long> symbols)
        {
            int width = line.Mnemonic == "DW" ? 2 : line.Mnemonic == "DD" ? 4 : line.Mnemonic == "DQ" ? 8 : 1;

            foreach (string raw in line.RawOperands)
            {
                string item = raw.Trim();
                if (item.Length == 0)
                    continue;

                if (item.StartsWith("'", StringComparison.Ordinal) || item.StartsWith("\"", StringComparison.Ordinal))
                {
                    string value;
                    int endIndex;
                    string error;
                    if (!AsmText.TryDecodeQuoted(item, 0, out value, out endIndex, out error))
                        return error;

                    foreach (char c in value)
                    {
                        output.Write((byte)c);
                        for (int p = 1; p < width; p++)
                            output.Write((byte)0);
                    }

                    continue;
                }

                long number;
                string evalError;
                if (!TryEvaluate(item, symbols, out number, out evalError))
                    return evalError;

                for (int i = 0; i < width; i++)
                    output.Write((byte)((number >> (8 * i)) & 0xFF));
            }

            return null;
        }

        private static string EmitTimes(
            SourceLine line,
            MemoryStreamHelper output,
            Dictionary<string, long> symbols)
        {
            if (line.RawOperands.Length < 2)
                return "TIMES требует количество и элемент";

            long count;
            string error;
            if (!TryEvaluate(line.RawOperands[0], symbols, out count, out error))
                return error;

            string item = line.RawOperands[1].Trim();
            string kind = "DB";
            foreach (string candidate in new[] { "DB", "DW", "DD", "DQ" })
            {
                if (item.StartsWith(candidate, StringComparison.OrdinalIgnoreCase))
                {
                    kind = candidate;
                    item = item.Substring(2).Trim();
                    break;
                }
            }

            SourceLine fake = new SourceLine
            {
                Number = line.Number,
                Mnemonic = kind,
                RawOperands = new[] { item }
            };

            for (long i = 0; i < count; i++)
            {
                MemoryStreamHelper single = new MemoryStreamHelper();
                string emitError = EmitData(fake, single, symbols);
                if (emitError != null)
                    return emitError;
                output.Write(single.ToArray());
            }

            return null;
        }

        // ------------------------------------------------------------------
        // кодирование команд
        // ------------------------------------------------------------------

        [ThreadStatic]
        private static List<(int position, long target)> _pendingPatches;

        private static List<(int position, long target)> PendingPatches()
        {
            if (_pendingPatches == null)
                _pendingPatches = new List<(int position, long target)>();
            return _pendingPatches;
        }

        /// <summary>
        /// Writes the displacements of RIP-relative operands once the whole
        /// instruction is known.
        /// </summary>
        private static void ApplyPendingPatches(MemoryStreamHelper output, SourceLine line)
        {
            List<(int position, long target)> patches = _pendingPatches;
            if (patches == null || patches.Count == 0)
                return;

            long next = line.Offset + output.Length;
            foreach ((int position, long target) in patches)
            {
                long delta = target - next;
                for (int i = 0; i < 4; i++)
                    output.WriteAt(position + i, (byte)((delta >> (8 * i)) & 0xFF));
            }

            patches.Clear();
        }

        /// <summary>Removes the "short"/"near" prefix from a jump target.</summary>
        private static string JumpTargetText(X64Operand operand)
        {
            string text = (operand.Expression ?? string.Empty).Trim();
            string[] parts = text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
                return parts[1];
            return text;
        }

        private static string EmitInstruction(
            SourceLine line,
            MemoryStreamHelper output,
            Dictionary<string, long> symbols,
            X64Operand[] ops,
            bool reportErrors)
        {
            string mnemonic = line.Mnemonic;

            if (GroupOperations.TryGetValue(mnemonic, out int group))
                return EmitGroupOperation(group, line, output, symbols, ops, reportErrors);

            if (ConditionCodes.TryGetValue(mnemonic, out int condition) && mnemonic.StartsWith("J", StringComparison.Ordinal))
                return EmitConditionalJump(condition, line, output, symbols, ops, reportErrors);

            switch (mnemonic)
            {
                case "MOV":
                    return EmitMov(line, output, symbols, ops, reportErrors);

                case "LEA":
                {
                    if (ops.Length != 2 || !ops[1].IsMemory)
                        return reportErrors ? "LEA требует регистр и адрес" : null;
                    X64Operand destination = ops[0];
                    X64Operand source = ops[1];
                    WriteRex(output, destination.Size == 8, destination.Register >= 8, false, false);
                    output.Write(0x8D);
                    EmitModrm(output, source, destination.Register, line, symbols, reportErrors);
                    return null;
                }

                case "MOVZX":
                case "MOVSX":
                {
                    if (ops.Length != 2)
                        return reportErrors ? "MOVZX/MOVSX требует два операнда" : null;
                    X64Operand destination = ops[0];
                    X64Operand source = ops[1];
                    int sourceSize = source.Size == 0 ? (source.Kind == X64OperandKind.Register8 ? 1 : 2) : source.Size;
                    byte opcode = sourceSize == 1 ? (byte)(mnemonic == "MOVZX" ? 0xB6 : 0xBE)
                                                  : (byte)(mnemonic == "MOVZX" ? 0xB7 : 0xBF);
                    WriteRex(output, destination.Size == 8, destination.Register >= 8, false, false);
                    output.Write(0x0F);
                    output.Write(opcode);
                    EmitModrm(output, source, destination.Register, line, symbols, reportErrors);
                    return null;
                }

                case "MOVSXD":
                {
                    if (ops.Length != 2)
                        return reportErrors ? "MOVSXD требует два операнда" : null;
                    WriteRex(output, ops[0].Size == 8, ops[0].Register >= 8, false, false);
                    output.Write(0x63);
                    EmitModrm(output, ops[1], ops[0].Register, line, symbols, reportErrors);
                    return null;
                }

                case "INC":
                case "DEC":
                {
                    if (ops.Length != 1)
                        return reportErrors ? "Требуется один операнд" : null;
                    X64Operand operand = ops[0];
                    if (operand.Kind == X64OperandKind.Register16)
                    {
                        output.Write((byte)((mnemonic == "INC" ? 0x40 : 0x48) + (operand.Register & 7)));
                        return null;
                    }

                    WriteRex(output, operand.Size == 8, false, false, operand.IsRegister && operand.Register >= 8);
                    output.Write(operand.Size == 1 ? (byte)0xFE : (byte)0xFF);
                    EmitModrm(output, operand, mnemonic == "INC" ? 0 : 1, line, symbols, reportErrors);
                    return null;
                }

                case "NOT":
                case "NEG":
                {
                    if (ops.Length != 1)
                        return reportErrors ? "Требуется один операнд" : null;
                    X64Operand operand = ops[0];
                    WriteRex(output, operand.Size == 8, false, false, operand.IsRegister && operand.Register >= 8);
                    output.Write(operand.Size == 1 ? (byte)0xF6 : (byte)0xF7);
                    EmitModrm(output, operand, mnemonic == "NOT" ? 2 : 3, line, symbols, reportErrors);
                    return null;
                }

                case "MUL":
                case "IMUL":
                case "DIV":
                case "IDIV":
                {
                    if (ops.Length == 1)
                    {
                        X64Operand operand = ops[0];
                        int reg = mnemonic == "MUL" ? 4 : mnemonic == "IMUL" ? 5 : mnemonic == "DIV" ? 6 : 7;
                        WriteRex(output, operand.Size == 8, false, false, operand.IsRegister && operand.Register >= 8);
                        output.Write(operand.Size == 1 ? (byte)0xF6 : (byte)0xF7);
                        EmitModrm(output, operand, reg, line, symbols, reportErrors);
                        return null;
                    }

                    if (ops.Length == 3)
                    {
                        X64Operand destination = ops[0];
                        X64Operand source = ops[1];
                        long immediate;
                        string error;
                        if (!TryEvaluate(ops[2].Expression, symbols, out immediate, out error))
                            return reportErrors ? error : null;
                        bool byteForm = false;
                        if (ops[2].Expression != null && ops[2].Expression.Trim().Length <= 3
                            && immediate >= -128 && immediate <= 127)
                            byteForm = true;

                        WriteRex(output, destination.Size == 8, destination.Register >= 8, false, false);
                        output.Write(byteForm ? (byte)0x6B : (byte)0x69);
                        EmitModrm(output, source, destination.Register, line, symbols, reportErrors);
                        if (byteForm)
                            output.Write((byte)(immediate & 0xFF));
                        else
                            output.WriteInt32((int)immediate);
                        return null;
                    }

                    if (ops.Length == 2 && mnemonic == "IMUL")
                    {
                        // IMUL r, r/m — трёхбайтная форма 0F AF /r
                        X64Operand destination = ops[0];
                        X64Operand source = ops[1];
                        WriteRex(output, destination.Size == 8, destination.Register >= 8, false, false);
                        output.Write(0x0F);
                        output.Write(0xAF);
                        EmitModrm(output, source, destination.Register, line, symbols, reportErrors);
                        return null;
                    }

                    return reportErrors ? "неверное число операндов" : null;
                }

                case "SHL":
                case "SAL":
                case "SHR":
                case "SAR":
                case "ROL":
                case "ROR":
                case "RCL":
                case "RCR":
                {
                    if (ops.Length != 2)
                        return reportErrors ? "Требуется два операнда" : null;
                    X64Operand target = ops[0];
                    int type = ShiftOperations[mnemonic];
                    WriteRex(output, target.Size == 8, false, false, target.IsRegister && target.Register >= 8);

                    if (ops[1].Kind == X64OperandKind.Immediate)
                    {
                        long count;
                        string error;
                        if (!TryEvaluate(ops[1].Expression, symbols, out count, out error))
                            return reportErrors ? error : null;
                        output.Write(target.Size == 1 ? (byte)0xC0 : (byte)0xC1);
                        EmitModrm(output, target, type, line, symbols, reportErrors);
                        output.Write((byte)(count & 0xFF));
                        return null;
                    }

                    if (ops[1].Kind == X64OperandKind.Register8 && ops[1].Register == 1)
                    {
                        output.Write(target.Size == 1 ? (byte)0xD2 : (byte)0xD3);
                        EmitModrm(output, target, type, line, symbols, reportErrors);
                        return null;
                    }

                    output.Write(target.Size == 1 ? (byte)0xD0 : (byte)0xD1);
                    EmitModrm(output, target, type, line, symbols, reportErrors);
                    return null;
                }

                case "PUSH":
                {
                    if (ops.Length != 1)
                        return reportErrors ? "PUSH требует один операнд" : null;
                    X64Operand operand = ops[0];
                    if (operand.Kind == X64OperandKind.Immediate)
                    {
                        long value;
                        string error;
                        if (!TryEvaluate(operand.Expression, symbols, out value, out error))
                            return reportErrors ? error : null;
                        output.Write(0x68);
                        output.WriteInt32((int)value);
                        return null;
                    }

                    if (operand.Kind == X64OperandKind.SegmentRegister)
                    {
                        output.Write((byte)(0x06 + operand.Register * 8));
                        return null;
                    }

                    if (operand.IsRegister)
                    {
                        WriteRex(output, true, false, false, operand.Register >= 8);
                        output.Write((byte)(0x50 + (operand.Register & 7)));
                        return null;
                    }

                    WriteRex(output, true, false, false, false);
                    output.Write(0xFF);
                    EmitModrm(output, operand, 6, line, symbols, reportErrors);
                    return null;
                }

                case "POP":
                {
                    if (ops.Length != 1)
                        return reportErrors ? "POP требует один операнд" : null;
                    X64Operand operand = ops[0];
                    if (operand.Kind == X64OperandKind.SegmentRegister)
                    {
                        output.Write((byte)(0x07 + operand.Register * 8));
                        return null;
                    }

                    if (operand.IsRegister)
                    {
                        WriteRex(output, true, false, false, operand.Register >= 8);
                        output.Write((byte)(0x58 + (operand.Register & 7)));
                        return null;
                    }

                    WriteRex(output, true, false, false, false);
                    output.Write(0x8F);
                    EmitModrm(output, operand, 0, line, symbols, reportErrors);
                    return null;
                }

                case "TEST":
                {
                    if (ops.Length != 2)
                        return reportErrors ? "TEST требует два операнда" : null;
                    X64Operand left = ops[0];
                    X64Operand right = ops[1];
                    if (right.Kind == X64OperandKind.Immediate)
                    {
                        long value;
                        string error;
                        if (!TryEvaluate(right.Expression, symbols, out value, out error))
                            return reportErrors ? error : null;
                        int size = OperandSizeOf(left);
                        WriteRex(output, size == 8, false, false, left.IsRegister && left.Register >= 8);
                        if (size == 1)
                        {
                            output.Write(0xA8);
                            output.Write((byte)(value & 0xFF));
                        }
                        else
                        {
                            output.Write(0xA9);
                            output.WriteInt32((int)value);
                        }

                        return null;
                    }

                    int operandSize = OperandSizeOf(left);
                    WriteRex(output, operandSize == 8, right.Register >= 8, false,
                        left.IsRegister && left.Register >= 8);
                    output.Write(operandSize == 1 ? (byte)0x84 : (byte)0x85);
                    EmitModrm(output, left, right.Register, line, symbols, reportErrors);
                    return null;
                }

                case "XCHG":
                {
                    if (ops.Length != 2)
                        return reportErrors ? "XCHG требует два операнда" : null;
                    int size = OperandSizeOf(ops[0]);
                    WriteRex(output, size == 8, ops[1].Register >= 8, false,
                        ops[0].IsRegister && ops[0].Register >= 8);
                    output.Write(size == 1 ? (byte)0x86 : (byte)0x87);
                    EmitModrm(output, ops[0], ops[1].Register, line, symbols, reportErrors);
                    return null;
                }

                case "CALL":
                {
                    if (ops.Length != 1)
                        return reportErrors ? "CALL требует один операнд" : null;
                    X64Operand operand = ops[0];
                    if (operand.IsMemory)
                    {
                        WriteRex(output, true, false, false, false);
                        output.Write(0xFF);
                        EmitModrm(output, operand, 2, line, symbols, reportErrors);
                        return null;
                    }

                    long target;
                    string error;
                    if (!TryEvaluate(JumpTargetText(operand), symbols, out target, out error))
                        return reportErrors ? error : null;
                    output.Write(0xE8);
                    output.WriteInt32((int)(target - (line.Offset + 5)));
                    return null;
                }

                case "JMP":
                {
                    if (ops.Length != 1)
                        return reportErrors ? "JMP требует один операнд" : null;
                    X64Operand operand = ops[0];
                    if (operand.IsMemory)
                    {
                        WriteRex(output, true, false, false, false);
                        output.Write(0xFF);
                        EmitModrm(output, operand, 4, line, symbols, reportErrors);
                        return null;
                    }

                    long target;
                    string error;
                    if (!TryEvaluate(JumpTargetText(operand), symbols, out target, out error))
                    {
                        if (!reportErrors)
                        {
                            output.Write(0xE9);
                            output.WriteInt32(0);
                            return null;
                        }

                        return error;
                    }

                    if (!line.IsNearJump && !line.ForceNear)
                    {
                        long shortDistance = target - (line.Offset + 2);
                        output.Write(0xEB);
                        output.Write((byte)(shortDistance & 0xFF));
                        return null;
                    }

                    output.Write(0xE9);
                    output.WriteInt32((int)(target - (line.Offset + 5)));
                    return null;
                }

                case "RET":
                {
                    if (ops.Length == 0)
                    {
                        output.Write(0xC3);
                        return null;
                    }

                    long value;
                    string error;
                    if (!TryEvaluate(ops[0].Expression, symbols, out value, out error))
                        return reportErrors ? error : null;
                    output.Write(0xC2);
                    output.Write((byte)(value & 0xFF));
                    output.Write((byte)((value >> 8) & 0xFF));
                    return null;
                }

                case "SET":
                {
                    // SETcc: "sete al" и подобные
                    return reportErrors ? "неизвестная команда " + mnemonic : null;
                }

                case "NOP":
                    output.Write(0x90);
                    return null;

                case "HLT":
                    output.Write(0xF4);
                    return null;

                case "INT":
                {
                    if (ops.Length != 1)
                        return reportErrors ? "INT требует номер" : null;
                    long value;
                    string error;
                    if (!TryEvaluate(ops[0].Expression, symbols, out value, out error))
                        return reportErrors ? error : null;
                    output.Write(0xCD);
                    output.Write((byte)(value & 0xFF));
                    return null;
                }

                case "PUSHF":
                case "PUSHFD":
                case "PUSHFQ":
                    output.Write(0x9C);
                    return null;

                case "POPF":
                case "POPFD":
                case "POPFQ":
                    output.Write(0x9D);
                    return null;

                case "CLC": output.Write(0xF8); return null;
                case "STC": output.Write(0xF9); return null;
                case "CMC": output.Write(0xF5); return null;
                case "CLI": output.Write(0xFA); return null;
                case "STI": output.Write(0xFB); return null;
                case "CLD": output.Write(0xFC); return null;
                case "STD": output.Write(0xFD); return null;

                case "MOVSB": case "MOVSW":
                case "STOSB": case "STOSW":
                case "LODSB": case "LODSW":
                case "CMPSB": case "CMPSW":
                case "SCASB": case "SCASW":
                    output.Write(StringOperationOpcode(mnemonic));
                    return null;

                case "REP":
                case "REPE":
                case "REPZ":
                case "REPNE":
                case "REPNZ":
                {
                    output.Write(mnemonic == "REPNE" || mnemonic == "REPNZ" ? (byte)0xF2 : (byte)0xF3);
                    // "rep movsb" — префикс и команда в одной строке
                    if (ops.Length == 1)
                    {
                        string repeated = (ops[0].Expression ?? string.Empty).Trim().ToUpperInvariant();
                        if (IsStringInstruction(repeated))
                            output.Write(StringOperationOpcode(repeated));
                    }

                    return null;
                }

                default:
                    return reportErrors ? "неизвестная команда " + mnemonic : null;
            }
        }

        private static bool IsStringInstruction(string mnemonic)
        {
            switch (mnemonic)
            {
                case "MOVSB":
                case "MOVSW":
                case "STOSB":
                case "STOSW":
                case "LODSB":
                case "LODSW":
                case "CMPSB":
                case "CMPSW":
                case "SCASB":
                case "SCASW":
                    return true;
                default:
                    return false;
            }
        }

        private static byte StringOperationOpcode(string mnemonic)
        {
            switch (mnemonic)
            {
                case "MOVSB": return 0xA4;
                case "MOVSW": return 0xA5;
                case "STOSB": return 0xAA;
                case "STOSW": return 0xAB;
                case "LODSB": return 0xAC;
                case "LODSW": return 0xAD;
                case "CMPSB": return 0xA6;
                case "CMPSW": return 0xA7;
                case "SCASB": return 0xAE;
                default: return 0xAF;
            }
        }

        private static int OperandSizeOf(X64Operand operand)
        {
            if (operand.Size == 1 || operand.Size == 2 || operand.Size == 4 || operand.Size == 8)
                return operand.Size;
            if (operand.Kind == X64OperandKind.Register8)
                return 1;
            if (operand.Kind == X64OperandKind.Register16)
                return 2;
            if (operand.Kind == X64OperandKind.Register32)
                return 4;
            return 8;
        }

        /// <summary>
        /// The registers spl, bpl, sil and dil are only available with a REX
        /// prefix, so the prefix is required even when it adds no bits.
        /// </summary>
        private static bool NeedsRexForByte(int register)
        {
            return register >= 4 && register <= 7;
        }

        private static void WriteRex(MemoryStreamHelper output, bool w, bool r, bool x, bool b)
        {
            if (!w && !r && !x && !b)
                return;
            byte rex = 0x40;
            if (w) rex |= 0x08;
            if (r) rex |= 0x04;
            if (x) rex |= 0x02;
            if (b) rex |= 0x01;
            output.Write(rex);
        }

        /// <summary>Emits the ModR/M byte and the displacement for an operand.</summary>
        private static void EmitModrm(
            MemoryStreamHelper output,
            X64Operand operand,
            int reg,
            SourceLine line,
            Dictionary<string, long> symbols,
            bool reportErrors)
        {
            if (operand.IsRegister)
            {
                output.Write((byte)(0xC0 | ((reg & 7) << 3) | (operand.Register & 7)));
                return;
            }

            long displacement = 0;
            if (operand.HasDisplacement)
            {
                string error;
                if (!TryEvaluate(operand.Expression, symbols, out displacement, out error))
                {
                    if (reportErrors)
                        throw new X64AssemblyError(error);
                    displacement = 0;
                }
            }

            if (operand.RipRelative)
            {
                output.Write((byte)(0x00 | ((reg & 7) << 3) | 5));
                int patch = output.Length;
                output.WriteInt32(0);
                // смещение вычисляется только после того, как известна длина
                // всей команды, поэтому патч откладывается до конца строки
                PendingPatches().Add((patch, displacement));
                return;
            }

            int baseRegister = operand.BaseRegister;
            int indexRegister = operand.IndexRegister;
            bool needSib = indexRegister >= 0 || (baseRegister >= 0 && (baseRegister & 7) == 4);
            bool noBase = baseRegister < 0;

            int mod;
            if (noBase)
                mod = 0;
            else if (displacement == 0 && !needSib)
                mod = 0;
            else if (displacement >= -128 && displacement <= 127)
                mod = 1;
            else
                mod = 2;

            if (needSib)
            {
                output.Write((byte)((mod << 6) | ((reg & 7) << 3) | 4));
                int scale = operand.Scale == 8 ? 3 : operand.Scale == 4 ? 2 : operand.Scale == 2 ? 1 : 0;
                int index = indexRegister >= 0 ? (indexRegister & 7) : 4;
                int baseIndex = noBase ? 5 : (baseRegister & 7);
                output.Write((byte)((scale << 6) | (index << 3) | baseIndex));
            }
            else
            {
                output.Write((byte)((mod << 6) | ((reg & 7) << 3) | (baseRegister & 7)));
            }

            if (mod == 1)
            {
                output.Write((byte)(displacement & 0xFF));
            }
            else if (mod == 2)
            {
                output.Write((byte)(displacement & 0xFF));
                output.Write((byte)((displacement >> 8) & 0xFF));
            }
            else if (noBase)
            {
                output.Write((byte)(displacement & 0xFF));
                output.Write((byte)((displacement >> 8) & 0xFF));
            }
        }

        private static string EmitMov(
            SourceLine line,
            MemoryStreamHelper output,
            Dictionary<string, long> symbols,
            X64Operand[] ops,
            bool reportErrors)
        {
            if (ops.Length != 2)
                return reportErrors ? "MOV требует два операнда" : null;

            X64Operand destination = ops[0];
            X64Operand source = ops[1];

            if (source.Kind == X64OperandKind.Immediate)
            {
                long value;
                string error;
                if (!TryEvaluate(source.Expression, symbols, out value, out error))
                    return reportErrors ? error : null;

                if (destination.IsRegister)
                {
                    int registerSize = OperandSizeOf(destination);
                    if (registerSize == 1)
                    {
                        WriteRex(output, false, false, false, destination.Register >= 8);
                        output.Write((byte)(0xB0 + (destination.Register & 7)));
                        output.Write((byte)(value & 0xFF));
                        return null;
                    }

                    if (registerSize == 2)
                    {
                        output.Write(0x66);
                        WriteRex(output, false, false, false, destination.Register >= 8);
                        output.Write((byte)(0xB8 + (destination.Register & 7)));
                        output.Write((byte)(value & 0xFF));
                        output.Write((byte)((value >> 8) & 0xFF));
                        return null;
                    }

                    if (registerSize == 4)
                    {
                        WriteRex(output, false, false, false, destination.Register >= 8);
                        output.Write((byte)(0xB8 + (destination.Register & 7)));
                        output.WriteInt32((int)value);
                        return null;
                    }

                    // NASM по умолчанию кодирует "mov r64, imm32" как 32-битную
                    // команду с обнулением старших разрядов; 64-битная форма
                    // нужна только если значение не помещается в 32 бита.
                    if (value >= 0 && value <= 0xFFFFFFFFL)
                    {
                        WriteRex(output, false, false, false, destination.Register >= 8);
                        output.Write((byte)(0xB8 + (destination.Register & 7)));
                        output.WriteInt32((int)value);
                        return null;
                    }

                    WriteRex(output, true, false, false, destination.Register >= 8);
                    output.Write((byte)(0xB8 + (destination.Register & 7)));
                    output.WriteUInt64((ulong)value);
                    return null;
                }

                int memorySize = destination.Size == 0 ? 4 : destination.Size;
                WriteRex(output, memorySize == 8, false, false, false);
                output.Write(memorySize == 1 ? (byte)0xC6 : (byte)0xC7);
                EmitModrm(output, destination, 0, line, symbols, reportErrors);
                // в форме C6/C7 непосредственное значение всегда 32-битное
                // (для 64-битного операнда старшие разряды обнуляются)
                if (memorySize == 1)
                    output.Write((byte)(value & 0xFF));
                else if (memorySize == 2)
                {
                    output.Write((byte)(value & 0xFF));
                    output.Write((byte)((value >> 8) & 0xFF));
                }
                else
                    output.WriteInt32((int)value);
                return null;
            }

            int size = OperandSizeOf(destination);
            if (source.IsRegister && destination.IsRegister)
            {
                WriteRex(output, size == 8, source.Register >= 8, false, destination.Register >= 8);
                output.Write(size == 1 ? (byte)0x88 : (byte)0x89);
                output.Write((byte)(0xC0 | ((source.Register & 7) << 3) | (destination.Register & 7)));
                return null;
            }

            if (source.IsRegister)
            {
                WriteRex(output, size == 8, source.Register >= 8, false,
                    destination.IsRegister && destination.Register >= 8);
                output.Write(size == 1 ? (byte)0x88 : (byte)0x89);
                EmitModrm(output, destination, source.Register, line, symbols, reportErrors);
                return null;
            }

            // приёмник — регистр, источник — память: форма r, r/m
            WriteRex(output, size == 8, false, false,
                destination.IsRegister && destination.Register >= 8);
            output.Write(size == 1 ? (byte)0x8A : (byte)0x8B);
            EmitModrm(output, source, destination.Register, line, symbols, reportErrors);
            return null;
        }

        private static string EmitGroupOperation(
            int group,
            SourceLine line,
            MemoryStreamHelper output,
            Dictionary<string, long> symbols,
            X64Operand[] ops,
            bool reportErrors)
        {
            if (ops.Length != 2)
                return reportErrors ? "Требуется два операнда" : null;

            X64Operand destination = ops[0];
            X64Operand source = ops[1];
            int baseOpcode = group << 3;

            if (source.Kind == X64OperandKind.Immediate)
            {
                long value;
                string error;
                if (!TryEvaluate(source.Expression, symbols, out value, out error))
                    return reportErrors ? error : null;

                int size = OperandSizeOf(destination);
                if (size == 1)
                {
                    WriteRex(output, false, false, false, destination.IsRegister && destination.Register >= 8);
                    output.Write(0x80);
                    EmitModrm(output, destination, group, line, symbols, reportErrors);
                    output.Write((byte)(value & 0xFF));
                    return null;
                }

                // NASM выбирает форму 83 (imm8 со знаком), если значение
                // помещается в один байт со знаком.
                if (value >= -128 && value <= 127)
                {
                    WriteRex(output, size == 8, false, false,
                        destination.IsRegister && destination.Register >= 8);
                    output.Write(0x83);
                    EmitModrm(output, destination, group, line, symbols, reportErrors);
                    output.Write((byte)(value & 0xFF));
                    return null;
                }

                WriteRex(output, size == 8, false, false, destination.IsRegister && destination.Register >= 8);
                output.Write(0x81);
                EmitModrm(output, destination, group, line, symbols, reportErrors);
                output.WriteInt32((int)value);
                return null;
            }

            int operandSize = OperandSizeOf(destination);
            if (destination.IsRegister && source.IsRegister)
            {
                WriteRex(output, operandSize == 8, source.Register >= 8, false, destination.Register >= 8);
                output.Write((byte)(operandSize == 1 ? baseOpcode + 0 : baseOpcode + 1));
                output.Write((byte)(0xC0 | ((source.Register & 7) << 3) | (destination.Register & 7)));
                return null;
            }

            if (source.IsRegister)
            {
                WriteRex(output, operandSize == 8, source.Register >= 8, false,
                    destination.IsRegister && destination.Register >= 8);
                output.Write((byte)(operandSize == 1 ? baseOpcode + 2 : baseOpcode + 3));
                EmitModrm(output, destination, source.Register, line, symbols, reportErrors);
                return null;
            }

            WriteRex(output, operandSize == 8, false, false, destination.IsRegister && destination.Register >= 8);
            output.Write((byte)(operandSize == 1 ? baseOpcode + 0 : baseOpcode + 1));
            EmitModrm(output, destination, source.Register, line, symbols, reportErrors);
            return null;
        }

        private static string EmitConditionalJump(
            int condition,
            SourceLine line,
            MemoryStreamHelper output,
            Dictionary<string, long> symbols,
            X64Operand[] ops,
            bool reportErrors)
        {
            if (ops.Length != 1)
                return reportErrors ? "Условный переход требует один операнд" : null;

            long target;
            string error;
            if (!TryEvaluate(JumpTargetText(ops[0]), symbols, out target, out error))
            {
                if (!reportErrors)
                {
                    // на первом проходе метка ещё не известна — резервируем
                    // максимальную длину команды, чтобы разметка была стабильной
                    output.Write(0x0F);
                    output.Write((byte)(0x80 + condition));
                    output.WriteInt32(0);
                    return null;
                }

                return error;
            }

            if (!line.IsNearJump && !line.ForceNear)
            {
                long shortDistance = target - (line.Offset + 2);
                output.Write((byte)(0x70 + condition));
                output.Write((byte)(shortDistance & 0xFF));
                return null;
            }

            output.Write(0x0F);
            output.Write((byte)(0x80 + condition));
            output.WriteInt32((int)(target - (line.Offset + 6)));
            return null;
        }
    }

    /// <summary>Internal assembly error used to abort an emission.</summary>
    public sealed class X64AssemblyError : Exception
    {
        public X64AssemblyError(string message) : base(message)
        {
        }
    }
}
