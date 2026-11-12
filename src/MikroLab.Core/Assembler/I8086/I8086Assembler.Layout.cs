using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MikroLab.Core.Assembler.I8086
{
    public static partial class I8086Assembler
    {
        private static readonly Regex IdentifierPattern = new Regex(@"[A-Za-z_][A-Za-z0-9_]*", RegexOptions.Compiled);

        // ------------------------------------------------------------------
        // layout
        // ------------------------------------------------------------------

        private static void Layout(
            List<SourceLine> lines,
            Dictionary<string, ushort> symbols,
            HashSet<int> nearJumps,
            ushort origin,
            List<AssemblerError> errors)
        {
            // EQU constants are known before the layout starts.
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
                if (ExpressionEvaluator.TryEvaluate(expression, symbols, line.Number, out value, out error))
                    symbols[line.Label] = (ushort)value;
                else
                    errors.Add(new AssemblerError(line.Number, error));
            }

            // Relative jumps need their target before the layout starts.
            foreach (SourceLine line in lines)
                PrepareJump(line);

            for (int iteration = 0; iteration < 24; iteration++)
            {
                bool changed = false;
                int location = origin;

                foreach (SourceLine line in lines)
                {
                    if (line.Mnemonic == "ORG")
                    {
                        line.Offset = location;
                        line.Size = 0;
                        long value;
                        string error;
                        if (ExpressionEvaluator.TryEvaluate(
                                line.RawOperands.Length > 0 ? line.RawOperands[0] : "0",
                                symbols, line.Number, out value, out error))
                        {
                            int newLocation = (int)value;
                            if (newLocation != location)
                            {
                                location = newLocation;
                                changed = true;
                            }
                        }

                        continue;
                    }

                    line.Offset = location;
                    line.IsNearJump = nearJumps.Contains(line.Number);
                    MemoryStreamHelper buffer = new MemoryStreamHelper();
                    string emitError = EmitLine(line, buffer, symbols, false);
                    if (emitError == null)
                    {
                        if (line.Size != buffer.Length)
                        {
                            line.Size = buffer.Length;
                            changed = true;
                        }
                    }

                    location += line.Size;
                }

                foreach (SourceLine line in lines)
                {
                    if (!string.IsNullOrEmpty(line.Label) && line.Mnemonic != "EQU")
                        symbols[line.Label] = (ushort)line.Offset;
                }

                foreach (SourceLine line in lines)
                {
                    if (!line.IsRelativeJump || line.JumpTarget == null)
                        continue;

                    ushort target;
                    if (!symbols.TryGetValue(line.JumpTarget, out target))
                        continue;

                    int distance = target - (line.Offset + 2);
                    bool fits = distance >= -128 && distance <= 127;
                    bool wantNear = !fits || line.ForceNear;

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

            // Final emission with resolved symbols.
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
                {
                    errors.Add(new AssemblerError(line.Number, "размер инструкции не совпадает"));
                }
            }
        }

        /// <summary>
        /// Detects a relative jump and remembers its target label so the layout
        /// can relax the instruction between the short and the near form.
        /// </summary>
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
            if (Array.IndexOf(Register8Names, lower) >= 0 || Array.IndexOf(Register16Names, lower) >= 0)
                return;
            if (Array.IndexOf(SegmentNames, lower) >= 0)
                return;

            line.JumpTarget = parts[0];
            line.IsRelativeJump = true;
        }

        private static bool IsRelativeJumpMnemonic(string mnemonic)
        {
            if (mnemonic == "JMP" || mnemonic == "CALL")
                return true;
            if (mnemonic.StartsWith("J", StringComparison.Ordinal) && mnemonic.Length == 3)
                return ConditionCodes.ContainsKey(mnemonic);
            if (mnemonic.StartsWith("LOOP", StringComparison.Ordinal))
                return true;
            return false;
        }

        // ------------------------------------------------------------------
        // operand parsing
        // ------------------------------------------------------------------

        private static I8086Operand ParseOperand(string text, Dictionary<string, ushort> symbols, out string error)
        {
            error = null;
            I8086Operand operand = new I8086Operand { Text = text };
            string work = (text ?? string.Empty).Trim();
            if (work.Length == 0)
            {
                error = "пустой операнд";
                return operand;
            }

            // size prefix
            string lower = work.ToLowerInvariant();
            if (lower.StartsWith("byte ptr", StringComparison.Ordinal))
            {
                operand.Size = 1;
                work = work.Substring(9).Trim();
            }
            else if (lower.StartsWith("word ptr", StringComparison.Ordinal))
            {
                operand.Size = 2;
                work = work.Substring(9).Trim();
            }
            else if (lower.StartsWith("byte ", StringComparison.Ordinal) || lower == "byte")
            {
                operand.Size = 1;
                work = lower == "byte" ? string.Empty : work.Substring(5).Trim();
            }
            else if (lower.StartsWith("word ", StringComparison.Ordinal) || lower == "word")
            {
                operand.Size = 2;
                work = lower == "word" ? string.Empty : work.Substring(5).Trim();
            }

            // segment override
            int colon = work.IndexOf(':');
            if (colon > 0)
            {
                string prefix = work.Substring(0, colon).Trim().ToLowerInvariant();
                int segment = Array.IndexOf(SegmentNames, prefix);
                if (segment >= 0)
                {
                    operand.SegmentOverride = segment;
                    work = work.Substring(colon + 1).Trim();
                }
            }

            if (work.Length == 0)
            {
                error = "пустой операнд";
                return operand;
            }

            if (work.StartsWith("[", StringComparison.Ordinal) && work.EndsWith("]", StringComparison.Ordinal))
            {
                operand.Type = I8086OperandType.Memory;
                string content = work.Substring(1, work.Length - 2);
                ParseEffectiveAddress(content, operand, symbols, out error);
                return operand;
            }

            int register8 = Array.IndexOf(Register8Names, work.ToLowerInvariant());
            if (register8 >= 0)
            {
                operand.Type = I8086OperandType.Register8;
                operand.Register = register8;
                return operand;
            }

            int register16 = Array.IndexOf(Register16Names, work.ToLowerInvariant());
            if (register16 >= 0)
            {
                operand.Type = I8086OperandType.Register16;
                operand.Register = register16;
                return operand;
            }

            int segmentIndex = Array.IndexOf(SegmentNames, work.ToLowerInvariant());
            if (segmentIndex >= 0)
            {
                operand.Type = I8086OperandType.SegmentRegister;
                operand.SegmentRegister = segmentIndex;
                return operand;
            }

            operand.Type = I8086OperandType.Immediate;
            return operand;
        }

        private static void ParseEffectiveAddress(
            string content,
            I8086Operand operand,
            Dictionary<string, ushort> symbols,
            out string error)
        {
            error = null;
            string work = content.Trim();
            int baseRegister = -1;
            int indexRegister = -1;

            MatchCollection matches = IdentifierPattern.Matches(work);
            foreach (Match match in matches)
            {
                string name = match.Value.ToLowerInvariant();
                int register;
                switch (name)
                {
                    case "bx": register = 3; break;
                    case "bp": register = 5; break;
                    case "si": register = 6; break;
                    case "di": register = 7; break;
                    default:
                        continue;
                }

                if (baseRegister < 0)
                    baseRegister = register;
                else if (indexRegister < 0)
                    indexRegister = register;
                else
                {
                    error = "недопустимое сочетание регистров в адресе";
                    return;
                }
            }

            bool valid = (baseRegister < 0 && indexRegister < 0)
                || ((baseRegister == 3 || baseRegister == 5 || baseRegister == 6 || baseRegister == 7)
                    && (indexRegister < 0
                        || ((baseRegister == 3 || baseRegister == 5)
                            && (indexRegister == 6 || indexRegister == 7))));
            if (!valid)
            {
                error = "недопустимое сочетание регистров в адресе";
                return;
            }

            operand.Base = baseRegister;
            operand.Index = indexRegister;

            // Replace the registers with zero and evaluate the rest: "bx+si+4" -> "0+0+4".
            string expression = IdentifierPattern.Replace(work, match =>
            {
                string name = match.Value.ToLowerInvariant();
                return (name == "bx" || name == "bp" || name == "si" || name == "di") ? "0" : match.Value;
            });

            operand.Displacement = expression;
            long value;
            string evalError;
            if (ExpressionEvaluator.TryEvaluate(expression, symbols, 0, out value, out evalError))
            {
                operand.DisplacementValue = (int)value;
                operand.HasResolvedDisplacement = true;
            }
        }

        // ------------------------------------------------------------------
        // helpers
        // ------------------------------------------------------------------

        private static bool TryValue(
            I8086Operand operand,
            Dictionary<string, ushort> symbols,
            out long value,
            out string error)
        {
            value = 0;
            error = null;
            string expression = operand.Text;
            if (expression == null)
            {
                error = "пустой операнд";
                return false;
            }

            if (!ExpressionEvaluator.TryEvaluate(expression.Trim(), symbols, 0, out value, out error))
                return false;

            return true;
        }

        private static bool FitsInt8(long value)
        {
            return value >= -128 && value <= 127;
        }

        private static bool FitsUInt8(long value)
        {
            return value >= 0 && value <= 255;
        }

        private static bool FitsInt16(long value)
        {
            return value >= -32768 && value <= 32767;
        }

        /// <summary>Effective address register combination to ModR/M r/m field.</summary>
        private static int EffectiveAddressCode(int baseRegister, int indexRegister)
        {
            if (baseRegister == 3)
            {
                if (indexRegister == 6) return 0;          // bx+si
                if (indexRegister == 7) return 1;          // bx+di
                return 7;                                  // bx
            }

            if (baseRegister == 5)
            {
                if (indexRegister == 6) return 2;          // bp+si
                if (indexRegister == 7) return 3;          // bp+di
                return 6;                                  // bp
            }

            if (baseRegister == 6)
                return 4;                                  // si
            if (baseRegister == 7)
                return 5;                                  // di
            return 7;
        }

        /// <summary>
        /// Size of a memory operand in bytes. NASM defaults to a byte when the
        /// size is not stated, so "add [bx], 5" is a byte operation.
        /// </summary>
        private static int OperandSize(I8086Operand operand)
        {
            if (operand == null)
                return 0;
            if (operand.Size == 1 || operand.Size == 2)
                return operand.Size;
            return 1;
        }
    }
}
