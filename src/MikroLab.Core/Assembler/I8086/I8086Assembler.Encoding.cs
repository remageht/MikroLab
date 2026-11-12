using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MikroLab.Core.Assembler.I8086
{
    public static partial class I8086Assembler
    {
        private static readonly Dictionary<string, int> ConditionCodes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "JO", 0x0 }, { "JNO", 0x1 }, { "JB", 0x2 }, { "JAE", 0x3 },
            { "JE", 0x4 }, { "JNE", 0x5 }, { "JBE", 0x6 }, { "JA", 0x7 },
            { "JS", 0x8 }, { "JNS", 0x9 }, { "JP", 0xA }, { "JNP", 0xB },
            { "JL", 0xC }, { "JGE", 0xD }, { "JLE", 0xE }, { "JG", 0xF },
            // aliases
            { "JZ", 0x4 }, { "JNZ", 0x5 }, { "JC", 0x2 }, { "JNC", 0x3 },
            { "JNAE", 0x2 }, { "JNB", 0x3 }, { "JNBE", 0x7 }, { "JNA", 0x6 },
            { "JNG", 0xE }, { "JNL", 0xD }, { "JPE", 0xA }, { "JPO", 0xB }
        };

        private static readonly Dictionary<string, int> ShiftTypes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "ROL", 0 }, { "ROR", 1 }, { "RCL", 2 }, { "RCR", 3 },
            { "SHL", 4 }, { "SAL", 4 }, { "SHR", 5 }, { "SAR", 7 }
        };

        private static readonly Dictionary<string, int> GroupTypes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "ADD", 0 }, { "OR", 1 }, { "ADC", 2 }, { "SBB", 3 },
            { "AND", 4 }, { "SUB", 5 }, { "XOR", 6 }, { "CMP", 7 }
        };

        // ------------------------------------------------------------------
        // line emission
        // ------------------------------------------------------------------

        private static string EmitLine(
            SourceLine line,
            MemoryStreamHelper output,
            Dictionary<string, ushort> symbols,
            bool reportErrors)
        {
            string mnemonic = line.Mnemonic;
            if (mnemonic.Length == 0)
                return null;

            if (mnemonic == "ORG" || mnemonic == "EQU" || mnemonic == "SEGMENT" || mnemonic == "ENDS"
                || mnemonic == "ASSUME" || mnemonic == "BITS" || mnemonic == "ALIGN" || mnemonic == "INCLUDE")
            {
                return null;
            }

            if (mnemonic == "DB" || mnemonic == "DW" || mnemonic == "DD" || mnemonic == "DT")
                return EmitData(line, output, symbols);

            if (mnemonic == "TIMES")
                return EmitTimes(line, output, symbols);

            if (mnemonic == "RESB" || mnemonic == "RESW" || mnemonic == "RESD")
            {
                long count;
                string error;
                if (!ExpressionEvaluator.TryEvaluate(
                        line.RawOperands.Length > 0 ? line.RawOperands[0] : "0",
                        symbols, line.Number, out count, out error))
                {
                    return reportErrors ? error : null;
                }

                int size = mnemonic == "RESB" ? 1 : mnemonic == "RESW" ? 2 : 4;
                for (long i = 0; i < count; i++)
                    for (int b = 0; b < size; b++)
                        output.Write((byte)0);
                return null;
            }

            // instruction
            I8086Operand[] operands = new I8086Operand[line.RawOperands.Length];
            for (int i = 0; i < line.RawOperands.Length; i++)
            {
                string parseError;
                operands[i] = ParseOperand(line.RawOperands[i], symbols, out parseError);
                if (parseError != null)
                    return reportErrors ? parseError : null;
            }

            line.Operands = operands;
            return EmitInstruction(line, output, symbols, operands, reportErrors);
        }

        private static string EmitData(
            SourceLine line,
            MemoryStreamHelper output,
            Dictionary<string, ushort> symbols)
        {
            int width = line.Mnemonic == "DW" ? 2 : line.Mnemonic == "DD" || line.Mnemonic == "DT" ? 4 : 1;

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

                    byte[] bytes = Encoding.ASCII.GetBytes(value);
                    if (width == 1)
                    {
                        foreach (byte b in bytes)
                            output.Write(b);
                    }
                    else
                    {
                        foreach (byte b in bytes)
                        {
                            output.Write(b);
                            for (int p = 1; p < width; p++)
                                output.Write((byte)0);
                        }
                    }

                    continue;
                }

                long number;
                string evalError;
                if (!ExpressionEvaluator.TryEvaluate(item, symbols, line.Number, out number, out evalError))
                    return evalError;

                for (int i = 0; i < width; i++)
                    output.Write((byte)((number >> (8 * i)) & 0xFF));
            }

            return null;
        }

        private static string EmitTimes(
            SourceLine line,
            MemoryStreamHelper output,
            Dictionary<string, ushort> symbols)
        {
            if (line.RawOperands.Length < 2)
                return "TIMES требует количество и элемент";

            long count;
            string error;
            if (!ExpressionEvaluator.TryEvaluate(line.RawOperands[0], symbols, line.Number, out count, out error))
                return error;

            string item = line.RawOperands[1].Trim();
            string kind = null;
            foreach (string candidate in new[] { "DB", "DW", "DD" })
            {
                if (item.StartsWith(candidate, StringComparison.OrdinalIgnoreCase))
                {
                    kind = candidate;
                    item = item.Substring(2).Trim();
                    break;
                }
            }

            int width = kind == "DW" ? 2 : kind == "DD" ? 4 : 1;
            SourceLine fake = new SourceLine
            {
                Number = line.Number,
                Mnemonic = width == 1 ? "DB" : width == 2 ? "DW" : "DD",
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
        // instruction encoding
        // ------------------------------------------------------------------

        private static string EmitInstruction(
            SourceLine line,
            MemoryStreamHelper output,
            Dictionary<string, ushort> symbols,
            I8086Operand[] ops,
            bool reportErrors)
        {
            string mnemonic = line.Mnemonic;

            if (GroupTypes.TryGetValue(mnemonic, out int group))
                return EmitGroupInstruction(mnemonic, group, output, symbols, ops, reportErrors);

            if (ConditionCodes.TryGetValue(mnemonic, out int condition))
                return EmitConditionalJump(condition, line, output, symbols, ops, reportErrors);

            // "rep movsb" — the prefix and the instruction share one line
            if ((mnemonic == "REP" || mnemonic == "REPE" || mnemonic == "REPNE"
                 || mnemonic == "REPZ" || mnemonic == "REPNZ") && ops.Length == 1)
            {
                byte prefix = mnemonic == "REPE" || mnemonic == "REPZ" ? (byte)0xF3 : (byte)0xF2;
                if (mnemonic == "REP")
                    prefix = 0xF3;

                output.Write(prefix);
                string repeated = ops[0].Text.Trim().ToUpperInvariant();
                if (IsStringInstruction(repeated))
                    return EmitString(repeated, output);
                return reportErrors ? "неизвестная команда " + repeated : null;
            }

            switch (mnemonic)
            {
                case "MOV": return EmitMov(output, symbols, ops, reportErrors);
                case "LEA": return EmitLea(output, symbols, ops, reportErrors);
                case "XCHG": return EmitXchg(output, symbols, ops, reportErrors);
                case "TEST": return EmitTest(output, symbols, ops, reportErrors);
                case "INC":
                case "DEC": return EmitIncDec(mnemonic == "INC", output, symbols, ops, reportErrors);
                case "NOT":
                case "NEG": return EmitNotNeg(mnemonic == "NOT", output, symbols, ops, reportErrors);
                case "MUL":
                case "IMUL":
                case "DIV":
                case "IDIV": return EmitMultiplyDivide(mnemonic, output, symbols, ops, reportErrors);
                case "SHL":
                case "SAL":
                case "SHR":
                case "SAR":
                case "ROL":
                case "ROR":
                case "RCL":
                case "RCR": return EmitShift(ShiftTypes[mnemonic], output, symbols, ops, reportErrors);
                case "PUSH": return EmitPush(output, symbols, ops, reportErrors);
                case "POP": return EmitPop(output, symbols, ops, reportErrors);
                case "JMP": return EmitJump(line, output, symbols, ops, reportErrors);
                case "CALL": return EmitCall(line, output, symbols, ops, reportErrors);
                case "RET": return EmitRet(output, symbols, ops, reportErrors);
                case "LOOP":
                case "LOOPE":
                case "LOOPZ":
                case "LOOPNE":
                case "LOOPNZ": return EmitLoop(mnemonic, line, output, symbols, ops, reportErrors);
                case "JCXZ": return EmitJcxz(line, output, symbols, ops, reportErrors);
                case "MOVZX":
                case "MOVSX": return EmitMoveExtend(mnemonic == "MOVSX", output, symbols, ops, reportErrors);
                case "CBW": output.Write(0x98); return null;
                case "CWD": output.Write(0x99); return null;
                case "CLC": output.Write(0xF8); return null;
                case "STC": output.Write(0xF9); return null;
                case "CMC": output.Write(0xF5); return null;
                case "CLI": output.Write(0xFA); return null;
                case "STI": output.Write(0xFB); return null;
                case "CLD": output.Write(0xFC); return null;
                case "STD": output.Write(0xFD); return null;
                case "NOP": output.Write(0x90); return null;
                case "HLT": output.Write(0xF4); return null;
                case "PUSHF": output.Write(0x9C); return null;
                case "POPF": output.Write(0x9D); return null;
                case "LAHF": output.Write(0x9F); return null;
                case "SAHF": output.Write(0x9E); return null;
                case "INT3": output.Write(0xCC); return null;
                case "INT": return EmitInterrupt(output, symbols, ops, reportErrors);
                case "MOVSB":
                case "MOVSW":
                case "STOSB":
                case "STOSW":
                case "LODSB":
                case "LODSW":
                case "CMPSB":
                case "CMPSW":
                case "SCASB":
                case "SCASW": return EmitString(mnemonic, output);
                case "REPE":
                case "REPNE":
                case "REPZ":
                case "REPNZ": return EmitRepeat(mnemonic, output);
                case "IN":
                case "OUT": return EmitInOut(mnemonic, output, symbols, ops, reportErrors);
                default:
                    return reportErrors ? "неизвестная команда " + mnemonic : null;
            }
        }

        private static void EmitSegmentPrefix(I8086Operand operand, MemoryStreamHelper output)
        {
            if (operand == null || operand.SegmentOverride < 0)
                return;

            switch (operand.SegmentOverride)
            {
                case 0: output.Write(0x26); break;
                case 1: output.Write(0x2E); break;
                case 2: output.Write(0x36); break;
                default: output.Write(0x3E); break;
            }
        }

        private static bool EmitModRm(
            MemoryStreamHelper output,
            I8086Operand memory,
            int regField,
            Dictionary<string, ushort> symbols,
            bool reportErrors,
            out string error)
        {
            error = null;

            if (memory.Base < 0 && memory.Index < 0)
            {
                // direct addressing: mod = 00, r/m = 110, displacement 16
                long disp = 0;
                if (!memory.HasResolvedDisplacement)
                {
                    string evalError;
                    if (!ExpressionEvaluator.TryEvaluate(memory.Displacement ?? "0", symbols, 0, out disp, out evalError))
                    {
                        if (reportErrors)
                        {
                            error = evalError;
                            return false;
                        }
                    }
                }
                else
                {
                    disp = memory.DisplacementValue;
                }

                output.Write((byte)((regField << 3) | 0x06));
                output.Write((byte)(disp & 0xFF));
                output.Write((byte)((disp >> 8) & 0xFF));
                return true;
            }

            int rm = EffectiveAddressCode(memory.Base, memory.Index);
            long displacement = 0;
            bool resolved = memory.HasResolvedDisplacement;
            if (resolved)
            {
                displacement = memory.DisplacementValue;
            }
            else
            {
                string evalError;
                if (!ExpressionEvaluator.TryEvaluate(memory.Displacement ?? "0", symbols, 0, out displacement, out evalError))
                {
                    if (reportErrors)
                    {
                        error = evalError;
                        return false;
                    }
                }
            }

            bool useDisp8 = resolved && FitsInt8(displacement);
            bool isBp = rm == 6;

            if (!resolved)
            {
                // assume the widest form while the layout is not settled
                output.Write((byte)((2 << 6) | (regField << 3) | rm));
                output.Write((byte)(displacement & 0xFF));
                output.Write((byte)((displacement >> 8) & 0xFF));
                return true;
            }

            if (displacement == 0 && !isBp)
            {
                output.Write((byte)((0 << 6) | (regField << 3) | rm));
                return true;
            }

            if (useDisp8)
            {
                output.Write((byte)((1 << 6) | (regField << 3) | rm));
                output.Write((byte)(displacement & 0xFF));
                return true;
            }

            output.Write((byte)((2 << 6) | (regField << 3) | rm));
            output.Write((byte)(displacement & 0xFF));
            output.Write((byte)((displacement >> 8) & 0xFF));
            return true;
        }

        private static string EmitGroupInstruction(
            string mnemonic,
            int group,
            MemoryStreamHelper output,
            Dictionary<string, ushort> symbols,
            I8086Operand[] ops,
            bool reportErrors)
        {
            if (ops.Length != 2)
                return reportErrors ? mnemonic + " требует два операнда" : null;

            I8086Operand destination = ops[0];
            I8086Operand source = ops[1];
            int baseOpcode = group << 3;

            bool destinationIsByte = destination.Type == I8086OperandType.Register8
                || (destination.Type == I8086OperandType.Memory && OperandSize(destination) == 1);

            if (destination.Type == I8086OperandType.Register8 && source.Type == I8086OperandType.Register8)
            {
                output.Write((byte)(baseOpcode + 0));
                output.Write((byte)(0xC0 | (source.Register << 3) | destination.Register));
                return null;
            }

            if (destination.Type == I8086OperandType.Register16 && source.Type == I8086OperandType.Register16)
            {
                output.Write((byte)(baseOpcode + 1));
                output.Write((byte)(0xC0 | (source.Register << 3) | destination.Register));
                return null;
            }

            if (destination.Type == I8086OperandType.Register8 && source.Type == I8086OperandType.Memory)
            {
                EmitSegmentPrefix(source, output);
                output.Write((byte)(baseOpcode + 2));
                string error;
                if (!EmitModRm(output, source, destination.Register, symbols, reportErrors, out error))
                    return reportErrors ? error : null;
                return null;
            }

            if (destination.Type == I8086OperandType.Register16 && source.Type == I8086OperandType.Memory)
            {
                EmitSegmentPrefix(source, output);
                output.Write((byte)(baseOpcode + 3));
                string error;
                if (!EmitModRm(output, source, destination.Register, symbols, reportErrors, out error))
                    return reportErrors ? error : null;
                return null;
            }

            if (destination.Type == I8086OperandType.Memory && source.Type == I8086OperandType.Register8)
            {
                EmitSegmentPrefix(destination, output);
                output.Write((byte)(baseOpcode + 0));
                string error;
                if (!EmitModRm(output, destination, source.Register, symbols, reportErrors, out error))
                    return reportErrors ? error : null;
                return null;
            }

            if (destination.Type == I8086OperandType.Memory && source.Type == I8086OperandType.Register16)
            {
                EmitSegmentPrefix(destination, output);
                output.Write((byte)(baseOpcode + 1));
                string error;
                if (!EmitModRm(output, destination, source.Register, symbols, reportErrors, out error))
                    return reportErrors ? error : null;
                return null;
            }

            if (source.Type != I8086OperandType.Immediate)
                return reportErrors ? "неподдерживаемая комбинация операндов " + mnemonic : null;

            long value;
            string evalError;
            if (!ExpressionEvaluator.TryEvaluate(source.Text.Trim(), symbols, 0, out value, out evalError))
                return reportErrors ? evalError : null;

            if (destination.Type == I8086OperandType.Register8 && destination.Register == 0)
            {
                output.Write((byte)(baseOpcode + 4));
                output.Write((byte)(value & 0xFF));
                return null;
            }

            // a general register with an immediate value (the accumulator has its own forms)
            if (destination.Type == I8086OperandType.Register8 && destination.Register != 0)
            {
                output.Write(0x80);
                output.Write((byte)(0xC0 | (group << 3) | destination.Register));
                output.Write((byte)(value & 0xFF));
                return null;
            }

            if (destination.Type == I8086OperandType.Register16 && destination.Register != 0)
            {
                if (FitsInt8(value))
                {
                    output.Write(0x83);
                    output.Write((byte)(0xC0 | (group << 3) | destination.Register));
                    output.Write((byte)(value & 0xFF));
                }
                else
                {
                    output.Write(0x81);
                    output.Write((byte)(0xC0 | (group << 3) | destination.Register));
                    output.Write((byte)(value & 0xFF));
                    output.Write((byte)((value >> 8) & 0xFF));
                }

                return null;
            }

            if (destination.Type == I8086OperandType.Register16 && destination.Register == 0)
            {
                if (FitsInt8(value))
                {
                    output.Write(0x83);
                    output.Write((byte)(0xC0 | (group << 3)));
                    output.Write((byte)(value & 0xFF));
                }
                else
                {
                    output.Write((byte)(baseOpcode + 5));
                    output.Write((byte)(value & 0xFF));
                    output.Write((byte)((value >> 8) & 0xFF));
                }

                return null;
            }

            if (destination.Type == I8086OperandType.Memory)
            {
                EmitSegmentPrefix(destination, output);
                if (OperandSize(destination) == 1)
                {
                    output.Write(0x80);
                    string error;
                    if (!EmitModRm(output, destination, group, symbols, reportErrors, out error))
                        return reportErrors ? error : null;
                    output.Write((byte)(value & 0xFF));
                    return null;
                }

                if (FitsInt8(value))
                {
                    output.Write(0x83);
                    string error;
                    if (!EmitModRm(output, destination, group, symbols, reportErrors, out error))
                        return reportErrors ? error : null;
                    output.Write((byte)(value & 0xFF));
                    return null;
                }

                output.Write(0x81);
                string error2;
                if (!EmitModRm(output, destination, group, symbols, reportErrors, out error2))
                    return reportErrors ? error2 : null;
                output.Write((byte)(value & 0xFF));
                output.Write((byte)((value >> 8) & 0xFF));
                return null;
            }

            return reportErrors ? "неподдерживаемая комбинация операндов " + mnemonic : null;
        }

        private static string EmitConditionalJump(
            int condition,
            SourceLine line,
            MemoryStreamHelper output,
            Dictionary<string, ushort> symbols,
            I8086Operand[] ops,
            bool reportErrors)
        {
            if (ops.Length != 1)
                return reportErrors ? "Условный переход требует один операнд" : null;

            string targetName = line.JumpTarget ?? ops[0].Text.Trim();
            ushort target;
            if (!symbols.TryGetValue(targetName, out target))
                return reportErrors ? "метка не найдена: " + targetName : null;

            if (line.IsNearJump)
            {
                output.Write(0x0F);
                output.Write((byte)(0x80 + condition));
                int distance = target - (line.Offset + 4);
                output.Write((byte)(distance & 0xFF));
                output.Write((byte)((distance >> 8) & 0xFF));
                return null;
            }

            output.Write((byte)(0x70 + condition));
            int shortDistance = target - (line.Offset + 2);
            output.Write((byte)(shortDistance & 0xFF));
            return null;
        }

        private static string EmitMov(
            MemoryStreamHelper output,
            Dictionary<string, ushort> symbols,
            I8086Operand[] ops,
            bool reportErrors)
        {
            if (ops.Length != 2)
                return reportErrors ? "MOV требует два операнда" : null;

            I8086Operand destination = ops[0];
            I8086Operand source = ops[1];

            // segment registers
            if (destination.Type == I8086OperandType.SegmentRegister)
            {
                if (source.Type == I8086OperandType.Register16)
                {
                    output.Write(0x8E);
                    output.Write((byte)((destination.SegmentRegister << 3) | source.Register));
                    return null;
                }

                if (source.Type == I8086OperandType.Memory)
                {
                    EmitSegmentPrefix(source, output);
                    output.Write(0x8E);
                    string error;
                    if (!EmitModRm(output, source, destination.SegmentRegister, symbols, reportErrors, out error))
                        return reportErrors ? error : null;
                    return null;
                }
            }

            if (source.Type == I8086OperandType.SegmentRegister)
            {
                if (destination.Type == I8086OperandType.Register16)
                {
                    output.Write(0x8C);
                    output.Write((byte)((source.SegmentRegister << 3) | destination.Register));
                    return null;
                }

                if (destination.Type == I8086OperandType.Memory)
                {
                    EmitSegmentPrefix(destination, output);
                    output.Write(0x8C);
                    string error;
                    if (!EmitModRm(output, destination, source.SegmentRegister, symbols, reportErrors, out error))
                        return reportErrors ? error : null;
                    return null;
                }
            }

            // accumulator with a direct address uses the short moffs form
            if (destination.Type == I8086OperandType.Register16 && source.Type == I8086OperandType.Memory
                && source.IsDirect && destination.Register == 0)
            {
                EmitSegmentPrefix(source, output);
                output.Write(0xA1);
                WriteDisplacement16(output, source, symbols, reportErrors);
                return null;
            }

            if (destination.Type == I8086OperandType.Register8 && source.Type == I8086OperandType.Memory
                && source.IsDirect && destination.Register == 0)
            {
                EmitSegmentPrefix(source, output);
                output.Write(0xA0);
                WriteDisplacement16(output, source, symbols, reportErrors);
                return null;
            }

            if (destination.Type == I8086OperandType.Memory && source.Type == I8086OperandType.Register16
                && destination.IsDirect && source.Register == 0)
            {
                EmitSegmentPrefix(destination, output);
                output.Write(0xA3);
                WriteDisplacement16(output, destination, symbols, reportErrors);
                return null;
            }

            if (destination.Type == I8086OperandType.Memory && source.Type == I8086OperandType.Register8
                && destination.IsDirect && source.Register == 0)
            {
                EmitSegmentPrefix(destination, output);
                output.Write(0xA2);
                WriteDisplacement16(output, destination, symbols, reportErrors);
                return null;
            }

            if (destination.Type == I8086OperandType.Register8 && source.Type == I8086OperandType.Register8)
            {
                output.Write(0x88);
                output.Write((byte)(0xC0 | (source.Register << 3) | destination.Register));
                return null;
            }

            if (destination.Type == I8086OperandType.Register16 && source.Type == I8086OperandType.Register16)
            {
                output.Write(0x89);
                output.Write((byte)(0xC0 | (source.Register << 3) | destination.Register));
                return null;
            }

            if (destination.Type == I8086OperandType.Register8 && source.Type == I8086OperandType.Memory)
            {
                EmitSegmentPrefix(source, output);
                output.Write(0x8A);
                string error;
                if (!EmitModRm(output, source, destination.Register, symbols, reportErrors, out error))
                    return reportErrors ? error : null;
                return null;
            }

            if (destination.Type == I8086OperandType.Register16 && source.Type == I8086OperandType.Memory)
            {
                EmitSegmentPrefix(source, output);
                output.Write(0x8B);
                string error;
                if (!EmitModRm(output, source, destination.Register, symbols, reportErrors, out error))
                    return reportErrors ? error : null;
                return null;
            }

            if (destination.Type == I8086OperandType.Memory && source.Type == I8086OperandType.Register8)
            {
                EmitSegmentPrefix(destination, output);
                output.Write(0x88);
                string error;
                if (!EmitModRm(output, destination, source.Register, symbols, reportErrors, out error))
                    return reportErrors ? error : null;
                return null;
            }

            if (destination.Type == I8086OperandType.Memory && source.Type == I8086OperandType.Register16)
            {
                EmitSegmentPrefix(destination, output);
                output.Write(0x89);
                string error;
                if (!EmitModRm(output, destination, source.Register, symbols, reportErrors, out error))
                    return reportErrors ? error : null;
                return null;
            }

            if (source.Type == I8086OperandType.Immediate)
            {
                long value;
                string evalError;
                if (!ExpressionEvaluator.TryEvaluate(source.Text.Trim(), symbols, 0, out value, out evalError))
                    return reportErrors ? evalError : null;

                if (destination.Type == I8086OperandType.Register8)
                {
                    output.Write((byte)(0xB0 + destination.Register));
                    output.Write((byte)(value & 0xFF));
                    return null;
                }

                if (destination.Type == I8086OperandType.Register16)
                {
                    output.Write((byte)(0xB8 + destination.Register));
                    output.Write((byte)(value & 0xFF));
                    output.Write((byte)((value >> 8) & 0xFF));
                    return null;
                }

                if (destination.Type == I8086OperandType.Memory)
                {
                    EmitSegmentPrefix(destination, output);
                    if (OperandSize(destination) == 1)
                    {
                        output.Write(0xC6);
                        string error;
                        if (!EmitModRm(output, destination, 0, symbols, reportErrors, out error))
                            return reportErrors ? error : null;
                        output.Write((byte)(value & 0xFF));
                        return null;
                    }

                    output.Write(0xC7);
                    string error2;
                    if (!EmitModRm(output, destination, 0, symbols, reportErrors, out error2))
                        return reportErrors ? error2 : null;
                    output.Write((byte)(value & 0xFF));
                    output.Write((byte)((value >> 8) & 0xFF));
                    return null;
                }
            }

            return reportErrors ? "неподдерживаемая комбинация операндов MOV" : null;
        }

        private static void WriteDisplacement16(
            MemoryStreamHelper output,
            I8086Operand operand,
            Dictionary<string, ushort> symbols,
            bool reportErrors)
        {
            long disp = 0;
            if (operand.HasResolvedDisplacement)
            {
                disp = operand.DisplacementValue;
            }
            else
            {
                string evalError;
                if (!ExpressionEvaluator.TryEvaluate(operand.Displacement ?? "0", symbols, 0, out disp, out evalError)
                    && reportErrors)
                {
                    // the layout pass reports the error later
                }
            }

            output.Write((byte)(disp & 0xFF));
            output.Write((byte)((disp >> 8) & 0xFF));
        }

        private static string EmitLea(
            MemoryStreamHelper output,
            Dictionary<string, ushort> symbols,
            I8086Operand[] ops,
            bool reportErrors)
        {
            if (ops.Length != 2 || ops[0].Type != I8086OperandType.Register16 || ops[1].Type != I8086OperandType.Memory)
                return reportErrors ? "LEA требует регистр и адрес памяти" : null;

            EmitSegmentPrefix(ops[1], output);
            output.Write(0x8D);
            string error;
            if (!EmitModRm(output, ops[1], ops[0].Register, symbols, reportErrors, out error))
                return reportErrors ? error : null;
            return null;
        }

        private static string EmitXchg(
            MemoryStreamHelper output,
            Dictionary<string, ushort> symbols,
            I8086Operand[] ops,
            bool reportErrors)
        {
            if (ops.Length != 2)
                return reportErrors ? "XCHG требует два операнда" : null;

            if (ops[0].Type == I8086OperandType.Register16 && ops[1].Type == I8086OperandType.Register16)
            {
                if (ops[0].Register == 0)
                {
                    output.Write((byte)(0x90 + ops[1].Register));
                    return null;
                }

                if (ops[1].Register == 0)
                {
                    output.Write((byte)(0x90 + ops[0].Register));
                    return null;
                }
            }

            bool wide = ops[0].Type == I8086OperandType.Register16 || ops[1].Type == I8086OperandType.Register16;
            if (wide)
            {
                output.Write(0x87);
                if (ops[0].Type == I8086OperandType.Memory)
                {
                    EmitSegmentPrefix(ops[0], output);
                    string error;
                    if (!EmitModRm(output, ops[0], ops[1].Register, symbols, reportErrors, out error))
                        return reportErrors ? error : null;
                    return null;
                }

                output.Write((byte)(0xC0 | (ops[1].Register << 3) | ops[0].Register));
                return null;
            }

            output.Write(0x86);
            if (ops[0].Type == I8086OperandType.Memory)
            {
                EmitSegmentPrefix(ops[0], output);
                string error;
                if (!EmitModRm(output, ops[0], ops[1].Register, symbols, reportErrors, out error))
                    return reportErrors ? error : null;
                return null;
            }

            output.Write((byte)(0xC0 | (ops[1].Register << 3) | ops[0].Register));
            return null;
        }

        private static string EmitTest(
            MemoryStreamHelper output,
            Dictionary<string, ushort> symbols,
            I8086Operand[] ops,
            bool reportErrors)
        {
            if (ops.Length != 2)
                return reportErrors ? "TEST требует два операнда" : null;

            I8086Operand destination = ops[0];
            I8086Operand source = ops[1];

            if (destination.Type == I8086OperandType.Register8 && source.Type == I8086OperandType.Register8)
            {
                output.Write(0x84);
                output.Write((byte)(0xC0 | (source.Register << 3) | destination.Register));
                return null;
            }

            if (destination.Type == I8086OperandType.Register16 && source.Type == I8086OperandType.Register16)
            {
                output.Write(0x85);
                output.Write((byte)(0xC0 | (source.Register << 3) | destination.Register));
                return null;
            }

            if (destination.Type == I8086OperandType.Memory && source.Type == I8086OperandType.Register8)
            {
                EmitSegmentPrefix(destination, output);
                output.Write(0x84);
                string error;
                if (!EmitModRm(output, destination, source.Register, symbols, reportErrors, out error))
                    return reportErrors ? error : null;
                return null;
            }

            if (destination.Type == I8086OperandType.Memory && source.Type == I8086OperandType.Register16)
            {
                EmitSegmentPrefix(destination, output);
                output.Write(0x85);
                string error;
                if (!EmitModRm(output, destination, source.Register, symbols, reportErrors, out error))
                    return reportErrors ? error : null;
                return null;
            }

            if (source.Type == I8086OperandType.Immediate)
            {
                long value;
                string evalError;
                if (!ExpressionEvaluator.TryEvaluate(source.Text.Trim(), symbols, 0, out value, out evalError))
                    return reportErrors ? evalError : null;

                if (destination.Type == I8086OperandType.Register8 && destination.Register == 0)
                {
                    output.Write(0xA8);
                    output.Write((byte)(value & 0xFF));
                    return null;
                }

                if (destination.Type == I8086OperandType.Register16 && destination.Register == 0)
                {
                    output.Write(0xA9);
                    output.Write((byte)(value & 0xFF));
                    output.Write((byte)((value >> 8) & 0xFF));
                    return null;
                }

                if (destination.Type == I8086OperandType.Memory)
                {
                    EmitSegmentPrefix(destination, output);
                    if (OperandSize(destination) == 1)
                    {
                        output.Write(0xF6);
                        string error;
                        if (!EmitModRm(output, destination, 0, symbols, reportErrors, out error))
                            return reportErrors ? error : null;
                        output.Write((byte)(value & 0xFF));
                        return null;
                    }

                    output.Write(0xF7);
                    string error2;
                    if (!EmitModRm(output, destination, 0, symbols, reportErrors, out error2))
                        return reportErrors ? error2 : null;
                    output.Write((byte)(value & 0xFF));
                    output.Write((byte)((value >> 8) & 0xFF));
                    return null;
                }
            }

            return reportErrors ? "неподдерживаемая комбинация операндов TEST" : null;
        }

        private static string EmitIncDec(
            bool increment,
            MemoryStreamHelper output,
            Dictionary<string, ushort> symbols,
            I8086Operand[] ops,
            bool reportErrors)
        {
            if (ops.Length != 1)
                return reportErrors ? "Требуется один операнд" : null;

            I8086Operand operand = ops[0];
            if (operand.Type == I8086OperandType.Register16)
            {
                output.Write((byte)((increment ? 0x40 : 0x48) + operand.Register));
                return null;
            }

            if (operand.Type == I8086OperandType.Register8)
            {
                output.Write(0xFE);
                output.Write((byte)(0xC0 | ((increment ? 0 : 1) << 3) | operand.Register));
                return null;
            }

            if (operand.Type == I8086OperandType.Memory)
            {
                EmitSegmentPrefix(operand, output);
                if (OperandSize(operand) == 1)
                {
                    output.Write(0xFE);
                    string error;
                    if (!EmitModRm(output, operand, increment ? 0 : 1, symbols, reportErrors, out error))
                        return reportErrors ? error : null;
                    return null;
                }

                output.Write(0xFF);
                string error2;
                if (!EmitModRm(output, operand, increment ? 0 : 1, symbols, reportErrors, out error2))
                    return reportErrors ? error2 : null;
                return null;
            }

            return reportErrors ? "неподдерживаемый операнд" : null;
        }

        private static string EmitNotNeg(
            bool not,
            MemoryStreamHelper output,
            Dictionary<string, ushort> symbols,
            I8086Operand[] ops,
            bool reportErrors)
        {
            if (ops.Length != 1)
                return reportErrors ? "Требуется один операнд" : null;

            I8086Operand operand = ops[0];
            if (operand.Type == I8086OperandType.Memory)
                EmitSegmentPrefix(operand, output);

            bool wide = operand.Type == I8086OperandType.Register16
                || (operand.Type == I8086OperandType.Memory && OperandSize(operand) == 2);
            output.Write((byte)(wide ? 0xF7 : 0xF6));

            if (operand.Type == I8086OperandType.Memory)
            {
                string error;
                if (!EmitModRm(output, operand, not ? 2 : 3, symbols, reportErrors, out error))
                    return reportErrors ? error : null;
                return null;
            }

            if (operand.Type == I8086OperandType.Register8)
            {
                output.Write((byte)(0xC0 | ((not ? 2 : 3) << 3) | operand.Register));
                return null;
            }

            if (operand.Type == I8086OperandType.Register16)
            {
                output.Write((byte)(0xC0 | ((not ? 2 : 3) << 3) | operand.Register));
                return null;
            }

            return reportErrors ? "неподдерживаемый операнд" : null;
        }

        private static string EmitMultiplyDivide(
            string mnemonic,
            MemoryStreamHelper output,
            Dictionary<string, ushort> symbols,
            I8086Operand[] ops,
            bool reportErrors)
        {
            if (ops.Length != 1)
                return reportErrors ? "Требуется один операнд" : null;

            I8086Operand operand = ops[0];
            int reg;
            switch (mnemonic)
            {
                case "MUL": reg = 4; break;
                case "IMUL": reg = 5; break;
                case "DIV": reg = 6; break;
                default: reg = 7; break;
            }

            if (operand.Type == I8086OperandType.Memory)
                EmitSegmentPrefix(operand, output);

            bool wide = operand.Type == I8086OperandType.Register16
                || (operand.Type == I8086OperandType.Memory && OperandSize(operand) == 2);
            output.Write((byte)(wide ? 0xF7 : 0xF6));

            if (operand.Type == I8086OperandType.Memory)
            {
                string error;
                if (!EmitModRm(output, operand, reg, symbols, reportErrors, out error))
                    return reportErrors ? error : null;
                return null;
            }

            output.Write((byte)(0xC0 | (reg << 3) | operand.Register));
            return null;
        }

        private static string EmitShift(
            int type,
            MemoryStreamHelper output,
            Dictionary<string, ushort> symbols,
            I8086Operand[] ops,
            bool reportErrors)
        {
            if (ops.Length != 2)
                return reportErrors ? "Требуется два операнда" : null;

            I8086Operand target = ops[0];
            I8086Operand count = ops[1];

            if (target.Type == I8086OperandType.Memory)
                EmitSegmentPrefix(target, output);

            bool wide = target.Type == I8086OperandType.Register16
                || (target.Type == I8086OperandType.Memory && OperandSize(target) == 2);

            if (count.Type == I8086OperandType.Immediate)
            {
                long value;
                string evalError;
                if (!ExpressionEvaluator.TryEvaluate(count.Text.Trim(), symbols, 0, out value, out evalError))
                    return reportErrors ? evalError : null;

                string modrmError;
                if (value == 1)
                {
                    output.Write((byte)(wide ? 0xD1 : 0xD0));
                }
                else
                {
                    output.Write((byte)(wide ? 0xC1 : 0xC0));
                }

                if (target.Type == I8086OperandType.Memory)
                {
                    if (!EmitModRm(output, target, type, symbols, reportErrors, out modrmError))
                        return reportErrors ? modrmError : null;
                }
                else
                {
                    output.Write((byte)(0xC0 | (type << 3) | target.Register));
                }

                if (value != 1)
                    output.Write((byte)(value & 0xFF));
                return null;
            }

            bool byCl = count.Type == I8086OperandType.Register8 && count.Register == 1;
            output.Write((byte)(byCl ? (wide ? 0xD3 : 0xD2) : (wide ? 0xD1 : 0xD0)));

            string error;
            if (target.Type == I8086OperandType.Memory)
            {
                if (!EmitModRm(output, target, type, symbols, reportErrors, out error))
                    return reportErrors ? error : null;
            }
            else
            {
                output.Write((byte)(0xC0 | (type << 3) | target.Register));
            }

            return null;
        }

        private static string EmitPush(
            MemoryStreamHelper output,
            Dictionary<string, ushort> symbols,
            I8086Operand[] ops,
            bool reportErrors)
        {
            if (ops.Length != 1)
                return reportErrors ? "PUSH требует один операнд" : null;

            I8086Operand operand = ops[0];
            if (operand.Type == I8086OperandType.Register16)
            {
                output.Write((byte)(0x50 + operand.Register));
                return null;
            }

            if (operand.Type == I8086OperandType.SegmentRegister)
            {
                output.Write((byte)(0x06 + operand.SegmentRegister * 8));
                return null;
            }

            if (operand.Type == I8086OperandType.Memory)
            {
                EmitSegmentPrefix(operand, output);
                output.Write(0xFF);
                string error;
                if (!EmitModRm(output, operand, 6, symbols, reportErrors, out error))
                    return reportErrors ? error : null;
                return null;
            }

            if (operand.Type == I8086OperandType.Immediate)
            {
                long value;
                string evalError;
                if (!ExpressionEvaluator.TryEvaluate(operand.Text.Trim(), symbols, 0, out value, out evalError))
                    return reportErrors ? evalError : null;

                if (FitsInt8(value))
                {
                    output.Write(0x6A);
                    output.Write((byte)(value & 0xFF));
                }
                else
                {
                    output.Write(0x68);
                    output.Write((byte)(value & 0xFF));
                    output.Write((byte)((value >> 8) & 0xFF));
                }

                return null;
            }

            return reportErrors ? "неподдерживаемый операнд PUSH" : null;
        }

        private static string EmitPop(
            MemoryStreamHelper output,
            Dictionary<string, ushort> symbols,
            I8086Operand[] ops,
            bool reportErrors)
        {
            if (ops.Length != 1)
                return reportErrors ? "POP требует один операнд" : null;

            I8086Operand operand = ops[0];
            if (operand.Type == I8086OperandType.Register16)
            {
                output.Write((byte)(0x58 + operand.Register));
                return null;
            }

            if (operand.Type == I8086OperandType.SegmentRegister)
            {
                output.Write((byte)(0x07 + operand.SegmentRegister * 8));
                return null;
            }

            if (operand.Type == I8086OperandType.Memory)
            {
                EmitSegmentPrefix(operand, output);
                output.Write(0x8F);
                string error;
                if (!EmitModRm(output, operand, 0, symbols, reportErrors, out error))
                    return reportErrors ? error : null;
                return null;
            }

            return reportErrors ? "неподдерживаемый операнд POP" : null;
        }

        private static string EmitJump(
            SourceLine line,
            MemoryStreamHelper output,
            Dictionary<string, ushort> symbols,
            I8086Operand[] ops,
            bool reportErrors)
        {
            if (ops.Length != 1)
                return reportErrors ? "JMP требует один операнд" : null;

            I8086Operand operand = ops[0];
            if (operand.Type == I8086OperandType.Register16 || operand.Type == I8086OperandType.Memory)
            {
                if (operand.Type == I8086OperandType.Memory)
                    EmitSegmentPrefix(operand, output);
                output.Write(0xFF);
                string error;
                if (!EmitModRm(output, operand, 4, symbols, reportErrors, out error))
                    return reportErrors ? error : null;
                return null;
            }

            // relative jump
            ushort target;
            string targetName = line.JumpTarget ?? operand.Text.Trim();
            if (!symbols.TryGetValue(targetName, out target))
                return reportErrors ? "метка не найдена: " + targetName : null;

            int distance = target - (line.Offset + 2);
            if (line.IsNearJump)
            {
                output.Write(0xE9);
                int nearDistance = target - (line.Offset + 3);
                output.Write((byte)(nearDistance & 0xFF));
                output.Write((byte)((nearDistance >> 8) & 0xFF));
                return null;
            }

            output.Write(0xEB);
            output.Write((byte)(distance & 0xFF));
            return null;
        }

        private static string EmitCall(
            SourceLine line,
            MemoryStreamHelper output,
            Dictionary<string, ushort> symbols,
            I8086Operand[] ops,
            bool reportErrors)
        {
            if (ops.Length != 1)
                return reportErrors ? "CALL требует один операнд" : null;

            I8086Operand operand = ops[0];
            if (operand.Type == I8086OperandType.Register16 || operand.Type == I8086OperandType.Memory)
            {
                if (operand.Type == I8086OperandType.Memory)
                    EmitSegmentPrefix(operand, output);
                output.Write(0xFF);
                string error;
                if (!EmitModRm(output, operand, 2, symbols, reportErrors, out error))
                    return reportErrors ? error : null;
                return null;
            }

            ushort target;
            string targetName = line.JumpTarget ?? operand.Text.Trim();
            if (!symbols.TryGetValue(targetName, out target))
                return reportErrors ? "метка не найдена: " + targetName : null;

            output.Write(0xE8);
            int distance = target - (line.Offset + 3);
            output.Write((byte)(distance & 0xFF));
            output.Write((byte)((distance >> 8) & 0xFF));
            return null;
        }

        private static string EmitRet(
            MemoryStreamHelper output,
            Dictionary<string, ushort> symbols,
            I8086Operand[] ops,
            bool reportErrors)
        {
            if (ops.Length == 0)
            {
                output.Write(0xC3);
                return null;
            }

            long value;
            string evalError;
            if (!ExpressionEvaluator.TryEvaluate(ops[0].Text.Trim(), symbols, 0, out value, out evalError))
                return reportErrors ? evalError : null;

            output.Write(0xC2);
            output.Write((byte)(value & 0xFF));
            output.Write((byte)((value >> 8) & 0xFF));
            return null;
        }

        private static string EmitLoop(
            string mnemonic,
            SourceLine line,
            MemoryStreamHelper output,
            Dictionary<string, ushort> symbols,
            I8086Operand[] ops,
            bool reportErrors)
        {
            if (ops.Length != 1)
                return reportErrors ? "Требуется один операнд" : null;

            ushort target;
            string targetName = line.JumpTarget ?? ops[0].Text.Trim();
            if (!symbols.TryGetValue(targetName, out target))
                return reportErrors ? "метка не найдена: " + targetName : null;

            byte opcode = 0xE2;
            if (mnemonic == "LOOPE" || mnemonic == "LOOPZ")
                opcode = 0xE1;
            else if (mnemonic == "LOOPNE" || mnemonic == "LOOPNZ")
                opcode = 0xE0;

            output.Write(opcode);
            output.Write((byte)((target - (line.Offset + 2)) & 0xFF));
            return null;
        }

        private static string EmitJcxz(
            SourceLine line,
            MemoryStreamHelper output,
            Dictionary<string, ushort> symbols,
            I8086Operand[] ops,
            bool reportErrors)
        {
            if (ops.Length != 1)
                return reportErrors ? "JCXZ требует один операнд" : null;

            string targetName = line.JumpTarget ?? ops[0].Text.Trim();
            ushort target;
            if (!symbols.TryGetValue(targetName, out target))
                return reportErrors ? "метка не найдена: " + targetName : null;

            output.Write(0xE3);
            output.Write((byte)((target - (line.Offset + 2)) & 0xFF));
            return null;
        }

        private static string EmitMoveExtend(
            bool signed,
            MemoryStreamHelper output,
            Dictionary<string, ushort> symbols,
            I8086Operand[] ops,
            bool reportErrors)
        {
            if (ops.Length != 2 || ops[0].Type != I8086OperandType.Register16)
                return reportErrors ? "MOVZX/MOVSX требует регистр и операнд" : null;

            bool sourceIsByte = ops[1].Type == I8086OperandType.Register8
                || (ops[1].Type == I8086OperandType.Memory && OperandSize(ops[1]) == 1);

            output.Write(0x0F);
            output.Write((byte)(signed ? (sourceIsByte ? 0xBE : 0xBF) : (sourceIsByte ? 0xB6 : 0xB7)));

            if (ops[1].Type == I8086OperandType.Memory)
            {
                EmitSegmentPrefix(ops[1], output);
                string error;
                if (!EmitModRm(output, ops[1], ops[0].Register, symbols, reportErrors, out error))
                    return reportErrors ? error : null;
                return null;
            }

            output.Write((byte)(0xC0 | (ops[0].Register << 3) | ops[1].Register));
            return null;
        }

        private static string EmitInterrupt(
            MemoryStreamHelper output,
            Dictionary<string, ushort> symbols,
            I8086Operand[] ops,
            bool reportErrors)
        {
            if (ops.Length != 1)
                return reportErrors ? "INT требует номер прерывания" : null;

            long value;
            string evalError;
            if (!ExpressionEvaluator.TryEvaluate(ops[0].Text.Trim(), symbols, 0, out value, out evalError))
                return reportErrors ? evalError : null;

            output.Write(0xCD);
            output.Write((byte)(value & 0xFF));
            return null;
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

        private static string EmitString(string mnemonic, MemoryStreamHelper output)
        {
            switch (mnemonic)
            {
                case "MOVSB": output.Write(0xA4); return null;
                case "MOVSW": output.Write(0xA5); return null;
                case "STOSB": output.Write(0xAA); return null;
                case "STOSW": output.Write(0xAB); return null;
                case "LODSB": output.Write(0xAC); return null;
                case "LODSW": output.Write(0xAD); return null;
                case "CMPSB": output.Write(0xA6); return null;
                case "CMPSW": output.Write(0xA7); return null;
                case "SCASB": output.Write(0xAE); return null;
                default: output.Write(0xAF); return null;
            }
        }

        private static string EmitRepeat(string mnemonic, MemoryStreamHelper output)
        {
            if (mnemonic == "REPE" || mnemonic == "REPZ")
                output.Write(0xF3);
            else
                output.Write(0xF2);
            return null;
        }

        private static string EmitInOut(
            string mnemonic,
            MemoryStreamHelper output,
            Dictionary<string, ushort> symbols,
            I8086Operand[] ops,
            bool reportErrors)
        {
            // The laboratory programs do not use the I/O ports; the encodings
            // are provided for completeness.
            if (ops.Length == 0)
                return reportErrors ? "Требуется операнд" : null;

            long value;
            string evalError;
            if (!ExpressionEvaluator.TryEvaluate(ops[0].Text.Trim(), symbols, 0, out value, out evalError))
                return reportErrors ? evalError : null;

            bool wide = ops.Length > 1 && ops[1].Type == I8086OperandType.Register16;
            if (mnemonic == "IN")
            {
                output.Write((byte)(wide ? 0xE5 : 0xE4));
                output.Write((byte)(value & 0xFF));
                return null;
            }

            output.Write((byte)(wide ? 0xE7 : 0xE6));
            output.Write((byte)(value & 0xFF));
            return null;
        }
    }
}
