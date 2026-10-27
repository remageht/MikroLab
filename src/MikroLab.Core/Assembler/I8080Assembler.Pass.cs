using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MikroLab.Core.Assembler
{
    public static partial class I8080Assembler
    {
        private sealed class PendingEqu
        {
            public ParsedLine Line;
            public string Expression;
        }

        private sealed class ChunkBuilder
        {
            public int Address;
            public int LineNumber;
            public List<byte> Data = new List<byte>();
        }

        private static bool TryEvaluateRaw(
            string expression,
            IReadOnlyDictionary<string, ushort> symbols,
            int location,
            out long value,
            out string error)
        {
            return ExpressionEvaluator.TryEvaluate(expression, symbols, location, out value, out error);
        }

        private static void FirstPass(
            List<ParsedLine> lines,
            ushort origin,
            Dictionary<string, ushort> symbols,
            List<AssemblerError> errors)
        {
            int pc = origin;
            bool ended = false;
            HashSet<string> declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<PendingEqu> pending = new List<PendingEqu>();

            foreach (ParsedLine line in lines)
            {
                if (ended)
                    break;

                line.Address = pc;

                if (line.Kind != LineKind.Equ && line.Label != null)
                {
                    if (DeclareName(line.Label, line.LineNumber, declared, errors))
                        symbols[line.Label] = (ushort)(pc & 0xFFFF);
                }

                switch (line.Kind)
                {
                    case LineKind.Empty:
                        break;

                    case LineKind.Ignored:
                        break;

                    case LineKind.End:
                        ended = true;
                        break;

                    case LineKind.Org:
                        if (!line.Invalid)
                        {
                            long value;
                            string expressionError;
                            if (!TryEvaluateRaw(line.Expression, symbols, pc, out value, out expressionError))
                            {
                                AddError(errors, line.LineNumber, "Invalid expression: " + expressionError);
                                line.Invalid = true;
                            }
                            else if (value < 0 || value > 0xFFFF)
                            {
                                AddError(errors, line.LineNumber, "ORG address is out of range: " + FormatValue(value) + " (allowed 0..65535)");
                                line.Invalid = true;
                            }
                            else
                            {
                                pc = (int)value;
                            }
                        }
                        break;

                    case LineKind.Equ:
                    {
                        if (line.Invalid || line.Label == null)
                            break;

                        if (!DeclareName(line.Label, line.LineNumber, declared, errors))
                            break;

                        long value;
                        string expressionError;
                        if (TryEvaluateRaw(line.Expression, symbols, line.Address, out value, out expressionError))
                        {
                            if (value < 0 || value > 0xFFFF)
                            {
                                AddError(errors, line.LineNumber, "EQU value is out of range: " + FormatValue(value) + " (allowed 0..65535)");
                                line.Invalid = true;
                            }
                            else
                            {
                                symbols[line.Label] = (ushort)value;
                            }
                        }
                        else
                        {
                            PendingEqu item = new PendingEqu();
                            item.Line = line;
                            item.Expression = line.Expression;
                            pending.Add(item);
                        }
                        break;
                    }

                    case LineKind.Ds:
                        if (!line.Invalid)
                        {
                            long value;
                            string expressionError;
                            if (!TryEvaluateRaw(line.Expression, symbols, pc, out value, out expressionError))
                            {
                                AddError(errors, line.LineNumber, "Invalid expression: " + expressionError);
                                line.Invalid = true;
                            }
                            else if (value < 0 || value > 0xFFFF)
                            {
                                AddError(errors, line.LineNumber, "DS size is out of range: " + FormatValue(value) + " (allowed 0..65535)");
                                line.Invalid = true;
                            }
                            else if (pc + (int)value > 0x10000)
                            {
                                AddError(errors, line.LineNumber, "DS reserve overflows the 16-bit address space");
                                line.Invalid = true;
                            }
                            else
                            {
                                line.Size = (int)value;
                                pc += line.Size;
                            }
                        }
                        break;

                    case LineKind.Db:
                    case LineKind.Dw:
                    case LineKind.Instruction:
                        if (pc + line.Size > 0x10000)
                        {
                            AddError(errors, line.LineNumber, "Emitted bytes overflow the 16-bit address space");
                            line.Invalid = true;
                        }
                        else
                        {
                            pc += line.Size;
                        }
                        break;
                }
            }

            ResolvePendingEqu(pending, symbols, errors);
        }

        private static void ResolvePendingEqu(
            List<PendingEqu> pending,
            Dictionary<string, ushort> symbols,
            List<AssemblerError> errors)
        {
            bool progress = true;
            while (progress && pending.Count > 0)
            {
                progress = false;
                List<PendingEqu> remaining = new List<PendingEqu>();

                foreach (PendingEqu item in pending)
                {
                    long value;
                    string expressionError;
                    if (TryEvaluateRaw(item.Expression, symbols, item.Line.Address, out value, out expressionError))
                    {
                        if (value < 0 || value > 0xFFFF)
                        {
                            AddError(errors, item.Line.LineNumber, "EQU value is out of range: " + FormatValue(value) + " (allowed 0..65535)");
                            item.Line.Invalid = true;
                        }
                        else
                        {
                            symbols[item.Line.Label] = (ushort)value;
                        }

                        progress = true;
                    }
                    else
                    {
                        remaining.Add(item);
                    }
                }

                pending = remaining;
            }

            foreach (PendingEqu item in pending)
            {
                long value;
                string expressionError;
                TryEvaluateRaw(item.Expression, symbols, item.Line.Address, out value, out expressionError);
                AddError(errors, item.Line.LineNumber, "Unable to resolve EQU: " + (expressionError ?? "undefined symbol"));
                item.Line.Invalid = true;
            }
        }

        private static bool DeclareName(
            string name,
            int lineNumber,
            HashSet<string> declared,
            List<AssemblerError> errors)
        {
            if (declared.Contains(name))
            {
                AddError(errors, lineNumber, "Duplicate label '" + name + "'");
                return false;
            }

            declared.Add(name);
            return true;
        }

        private static void SecondPass(
            List<ParsedLine> lines,
            ushort origin,
            Dictionary<string, ushort> symbols,
            List<AssemblerError> errors,
            out byte[] image,
            out List<AssembledChunk> chunks,
            out List<ListingLine> listing,
            out ushort startAddress,
            out ushort endAddress)
        {
            image = new byte[65536];
            listing = new List<ListingLine>(lines.Count);
            List<ChunkBuilder> chunkBuilders = new List<ChunkBuilder>();
            List<byte>[] emissions = new List<byte>[lines.Count];
            bool ended = false;
            int firstEmitted = -1;

            for (int i = 0; i < lines.Count; i++)
            {
                ParsedLine line = lines[i];

                if (ended || line.Kind == LineKind.Ignored)
                {
                    listing.Add(new ListingLine(line.LineNumber, line.Source, false, 0, Array.Empty<byte>()));
                    continue;
                }

                if (line.Kind == LineKind.End)
                {
                    ended = true;
                    listing.Add(new ListingLine(line.LineNumber, line.Source, false, 0, Array.Empty<byte>()));
                    continue;
                }

                if (line.Kind == LineKind.Ds && line.Size > 0 && line.Address >= 0 && line.Address + line.Size <= 0x10000)
                    Array.Clear(image, line.Address, line.Size);

                List<byte> emitted = null;

                if (!line.Invalid)
                {
                    switch (line.Kind)
                    {
                        case LineKind.Db:
                            emitted = BuildDb(line, symbols, errors);
                            break;
                        case LineKind.Dw:
                            emitted = BuildDw(line, symbols, errors);
                            break;
                        case LineKind.Instruction:
                            emitted = BuildInstruction(line, symbols, errors);
                            break;
                    }
                }

                if (emitted != null && emitted.Count > 0)
                {
                    emissions[i] = emitted;
                    if (firstEmitted < 0)
                        firstEmitted = line.Address;
                }

                bool hasBytes = emitted != null && emitted.Count > 0;
                listing.Add(new ListingLine(
                    line.LineNumber,
                    line.Source,
                    hasBytes,
                    hasBytes ? (ushort)line.Address : (ushort)0,
                    emitted != null ? emitted.ToArray() : Array.Empty<byte>()));
            }

            ChunkBuilder lastChunk = null;

            for (int i = 0; i < lines.Count; i++)
            {
                List<byte> emitted = emissions[i];
                if (emitted == null)
                    continue;

                int address = lines[i].Address;
                foreach (byte value in emitted)
                {
                    if (address < 0 || address > 0xFFFF)
                    {
                        AddError(errors, lines[i].LineNumber, "Emitted byte is outside the 16-bit address space");
                        break;
                    }

                    image[address] = value;
                    if (lastChunk == null || lastChunk.Address + lastChunk.Data.Count != address)
                    {
                        lastChunk = new ChunkBuilder();
                        lastChunk.Address = address;
                        lastChunk.LineNumber = lines[i].LineNumber;
                        chunkBuilders.Add(lastChunk);
                    }

                    lastChunk.Data.Add(value);
                    address++;
                }
            }

            chunkBuilders.Sort(delegate(ChunkBuilder a, ChunkBuilder b)
            {
                int byAddress = a.Address.CompareTo(b.Address);
                if (byAddress != 0)
                    return byAddress;
                return a.LineNumber.CompareTo(b.LineNumber);
            });

            chunks = new List<AssembledChunk>(chunkBuilders.Count);
            foreach (ChunkBuilder builder in chunkBuilders)
                chunks.Add(new AssembledChunk((ushort)builder.Address, builder.Data.ToArray(), builder.LineNumber));

            startAddress = firstEmitted >= 0 ? (ushort)firstEmitted : origin;
            if (chunks.Count > 0)
            {
                AssembledChunk last = chunks[chunks.Count - 1];
                endAddress = unchecked((ushort)(last.Address + last.Data.Length));
            }
            else
            {
                endAddress = origin;
            }
        }

        private static List<byte> BuildDb(
            ParsedLine line,
            Dictionary<string, ushort> symbols,
            List<AssemblerError> errors)
        {
            List<byte> data = new List<byte>();
            int location = line.Address;

            foreach (string operand in line.Operands)
            {
                if (operand.Length > 0 && (operand[0] == '\'' || operand[0] == '"'))
                {
                    string textValue;
                    int end;
                    string stringError;
                    if (!AsmText.TryDecodeQuoted(operand, 0, out textValue, out end, out stringError))
                    {
                        AddError(errors, line.LineNumber, stringError);
                        return null;
                    }

                    if (end == operand.Length)
                    {
                        for (int i = 0; i < textValue.Length; i++)
                        {
                            if (textValue[i] > 0xFF)
                            {
                                AddError(errors, line.LineNumber, "String literal contains a non-ASCII character");
                                return null;
                            }

                            data.Add((byte)textValue[i]);
                            location++;
                        }

                        continue;
                    }
                }

                long value;
                string expressionError;
                if (!TryEvaluateRaw(operand, symbols, location, out value, out expressionError))
                {
                    AddError(errors, line.LineNumber, "Invalid expression: " + expressionError);
                    return null;
                }

                if (value < 0 || value > 0xFF)
                {
                    AddError(errors, line.LineNumber, "Byte value is out of range: " + FormatValue(value) + " (allowed 0..255)");
                    return null;
                }

                data.Add((byte)value);
                location++;
            }

            return data;
        }

        private static List<byte> BuildDw(
            ParsedLine line,
            Dictionary<string, ushort> symbols,
            List<AssemblerError> errors)
        {
            List<byte> data = new List<byte>(line.Operands.Count * 2);

            for (int i = 0; i < line.Operands.Count; i++)
            {
                long value;
                string expressionError;
                if (!TryEvaluateRaw(line.Operands[i], symbols, line.Address + i * 2, out value, out expressionError))
                {
                    AddError(errors, line.LineNumber, "Invalid expression: " + expressionError);
                    return null;
                }

                if (value < 0 || value > 0xFFFF)
                {
                    AddError(errors, line.LineNumber, "Word value is out of range: " + FormatValue(value) + " (allowed 0..65535)");
                    return null;
                }

                data.Add((byte)(value & 0xFF));
                data.Add((byte)((value >> 8) & 0xFF));
            }

            return data;
        }

        private static List<byte> BuildInstruction(
            ParsedLine line,
            Dictionary<string, ushort> symbols,
            List<AssemblerError> errors)
        {
            List<byte> data = new List<byte>(3);

            switch (line.Form)
            {
                case OperandForm.Implied:
                    data.Add(line.Opcode);
                    return data;

                case OperandForm.Mov:
                    data.Add((byte)(0x40 | (line.Reg1 << 3) | line.Reg2));
                    return data;

                case OperandForm.Imm8:
                {
                    int value;
                    string description = (line.Mnemonic == "IN" || line.Mnemonic == "OUT") ? "Port number" : "8-bit value";
                    if (!TryEvaluateOperand(line, symbols, 0, 255, description, errors, out value))
                        return null;
                    data.Add(line.Opcode);
                    data.Add((byte)value);
                    return data;
                }

                case OperandForm.Imm16:
                {
                    int value;
                    if (!TryEvaluateOperand(line, symbols, 0, 0xFFFF, "16-bit value", errors, out value))
                        return null;
                    data.Add(line.Opcode);
                    data.Add((byte)(value & 0xFF));
                    data.Add((byte)((value >> 8) & 0xFF));
                    return data;
                }

                case OperandForm.Rst:
                {
                    int value;
                    if (!TryEvaluateOperand(line, symbols, 0, 7, "RST vector", errors, out value))
                        return null;
                    data.Add((byte)(0xC7 | (value << 3)));
                    return data;
                }

                default:
                    return null;
            }
        }

        private static bool TryEvaluateOperand(
            ParsedLine line,
            Dictionary<string, ushort> symbols,
            int minimum,
            int maximum,
            string description,
            List<AssemblerError> errors,
            out int value)
        {
            value = 0;
            if (string.IsNullOrEmpty(line.Expression))
            {
                AddError(errors, line.LineNumber, "Missing operand expression");
                return false;
            }

            long raw;
            string expressionError;
            if (!TryEvaluateRaw(line.Expression, symbols, line.Address, out raw, out expressionError))
            {
                AddError(errors, line.LineNumber, "Invalid expression: " + expressionError);
                return false;
            }

            if (raw < minimum || raw > maximum)
            {
                AddError(errors, line.LineNumber, description + " is out of range: " +
                    FormatValue(raw) + " (allowed " + minimum + ".." + maximum + ")");
                return false;
            }

            value = (int)raw;
            return true;
        }

        private static string FormatValue(long value)
        {
            if (value < 0)
                return value.ToString();
            return "0x" + value.ToString("X");
        }

        private static IReadOnlyDictionary<string, ushort> FreezeSymbols(Dictionary<string, ushort> symbols)
        {
            return new ReadOnlyDictionary<string, ushort>(symbols);
        }
    }
}
