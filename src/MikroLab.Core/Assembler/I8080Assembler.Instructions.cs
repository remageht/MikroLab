using System;
using System.Collections.Generic;

namespace MikroLab.Core.Assembler
{
    public static partial class I8080Assembler
    {
        private static readonly byte[] JumpConditionOpcodes = { 0xC2, 0xCA, 0xD2, 0xDA, 0xE2, 0xEA, 0xF2, 0xFA };
        private static readonly byte[] CallConditionOpcodes = { 0xC4, 0xCC, 0xD4, 0xDC, 0xE4, 0xEC, 0xF4, 0xFC };
        private static readonly byte[] ReturnConditionOpcodes = { 0xC0, 0xC8, 0xD0, 0xD8, 0xE0, 0xE8, 0xF0, 0xF8 };

        private static void ValidateLine(ParsedLine line, List<AssemblerError> errors)
        {
            switch (line.Kind)
            {
                case LineKind.Empty:
                case LineKind.Ignored:
                    return;
                case LineKind.Org:
                    ValidateSingleExpression(line, errors, "ORG");
                    return;
                case LineKind.Equ:
                    ValidateEqu(line, errors);
                    return;
                case LineKind.Db:
                    ValidateDb(line, errors);
                    return;
                case LineKind.Dw:
                    ValidateDw(line, errors);
                    return;
                case LineKind.Ds:
                    ValidateSingleExpression(line, errors, "DS");
                    return;
                case LineKind.End:
                    if (line.Operands.Count > 1)
                    {
                        AddError(errors, line.LineNumber, "END accepts at most one operand");
                        line.Invalid = true;
                    }
                    return;
                case LineKind.Instruction:
                    ValidateInstruction(line, errors);
                    return;
            }
        }

        private static void ValidateSingleExpression(ParsedLine line, List<AssemblerError> errors, string name)
        {
            line.Form = OperandForm.None;
            line.Size = 0;
            if (line.Operands.Count != 1)
            {
                AddError(errors, line.LineNumber, name + " requires exactly one expression");
                line.Invalid = true;
                return;
            }

            line.Expression = line.Operands[0];
        }

        private static void ValidateEqu(ParsedLine line, List<AssemblerError> errors)
        {
            line.Form = OperandForm.None;
            line.Size = 0;
            if (line.Label == null)
            {
                AddError(errors, line.LineNumber, "EQU requires a label");
                line.Invalid = true;
            }

            if (line.Operands.Count != 1)
            {
                AddError(errors, line.LineNumber, "EQU requires exactly one expression");
                line.Invalid = true;
                return;
            }

            line.Expression = line.Operands[0];
        }

        private static void ValidateDb(ParsedLine line, List<AssemblerError> errors)
        {
            line.Form = OperandForm.None;
            line.Size = 0;

            if (line.Operands.Count == 0)
            {
                AddError(errors, line.LineNumber, "DB requires at least one operand");
                line.Invalid = true;
                return;
            }

            foreach (string operand in line.Operands)
            {
                if (operand.Length > 0 && (operand[0] == '\'' || operand[0] == '"'))
                {
                    string value;
                    int end;
                    string stringError;
                    if (AsmText.TryDecodeQuoted(operand, 0, out value, out end, out stringError))
                    {
                        if (end == operand.Length)
                        {
                            line.Size += value.Length;
                            continue;
                        }
                    }
                    else if (!line.Invalid)
                    {
                        AddError(errors, line.LineNumber, stringError);
                        line.Invalid = true;
                    }
                    else
                    {
                        line.Invalid = true;
                    }
                }

                line.Size += 1;
            }
        }

        private static void ValidateDw(ParsedLine line, List<AssemblerError> errors)
        {
            line.Form = OperandForm.None;
            line.Size = 0;
            if (line.Operands.Count == 0)
            {
                AddError(errors, line.LineNumber, "DW requires at least one operand");
                line.Invalid = true;
                return;
            }

            line.Size = line.Operands.Count * 2;
        }

        private static void ValidateInstruction(ParsedLine line, List<AssemblerError> errors)
        {
            line.Form = OperandForm.None;
            line.Size = 0;
            string mnemonic = line.Mnemonic ?? string.Empty;

            switch (mnemonic)
            {
                case "NOP": SetImplied(line, 0x00, errors); return;
                case "HLT": SetImplied(line, 0x76, errors); return;
                case "EI": SetImplied(line, 0xFB, errors); return;
                case "DI": SetImplied(line, 0xF3, errors); return;
                case "XCHG": SetImplied(line, 0xEB, errors); return;
                case "XTHL": SetImplied(line, 0xE3, errors); return;
                case "SPHL": SetImplied(line, 0xF9, errors); return;
                case "PCHL": SetImplied(line, 0xE9, errors); return;
                case "RLC": SetImplied(line, 0x07, errors); return;
                case "RRC": SetImplied(line, 0x0F, errors); return;
                case "RAL": SetImplied(line, 0x17, errors); return;
                case "RAR": SetImplied(line, 0x1F, errors); return;
                case "DAA": SetImplied(line, 0x27, errors); return;
                case "CMA": SetImplied(line, 0x2F, errors); return;
                case "STC": SetImplied(line, 0x37, errors); return;
                case "CMC": SetImplied(line, 0x3F, errors); return;
                case "RET": SetImplied(line, 0xC9, errors); return;

                case "MOV": ValidateMov(line, errors); return;
                case "MVI": ValidateMvi(line, errors); return;
                case "LXI": ValidateLxi(line, errors); return;

                case "LDA": SetImm16(line, 0x3A, errors); return;
                case "STA": SetImm16(line, 0x32, errors); return;
                case "LHLD": SetImm16(line, 0x2A, errors); return;
                case "SHLD": SetImm16(line, 0x22, errors); return;

                case "LDAX": ValidateLdaxStax(line, 0x0A, errors); return;
                case "STAX": ValidateLdaxStax(line, 0x02, errors); return;

                case "ADD": ValidateAluRegister(line, 0x80, errors); return;
                case "ADC": ValidateAluRegister(line, 0x88, errors); return;
                case "SUB": ValidateAluRegister(line, 0x90, errors); return;
                case "SBB": ValidateAluRegister(line, 0x98, errors); return;
                case "ANA": ValidateAluRegister(line, 0xA0, errors); return;
                case "XRA": ValidateAluRegister(line, 0xA8, errors); return;
                case "ORA": ValidateAluRegister(line, 0xB0, errors); return;
                case "CMP": ValidateAluRegister(line, 0xB8, errors); return;

                case "ADI": SetImm8(line, 0xC6, errors); return;
                case "ACI": SetImm8(line, 0xCE, errors); return;
                case "SUI": SetImm8(line, 0xD6, errors); return;
                case "SBI": SetImm8(line, 0xDE, errors); return;
                case "ANI": SetImm8(line, 0xE6, errors); return;
                case "XRI": SetImm8(line, 0xEE, errors); return;
                case "ORI": SetImm8(line, 0xF6, errors); return;
                case "CPI": SetImm8(line, 0xFE, errors); return;

                case "INR": ValidateInrDcr(line, 0x04, errors); return;
                case "DCR": ValidateInrDcr(line, 0x05, errors); return;
                case "INX": ValidateInxDcxDad(line, 0x03, errors); return;
                case "DCX": ValidateInxDcxDad(line, 0x0B, errors); return;
                case "DAD": ValidateInxDcxDad(line, 0x09, errors); return;

                case "JMP": SetImm16(line, 0xC3, errors); return;
                case "CALL": SetImm16(line, 0xCD, errors); return;
                case "RST": ValidateRst(line, errors); return;
                case "PUSH": ValidatePushPop(line, 0xC5, errors); return;
                case "POP": ValidatePushPop(line, 0xC1, errors); return;
                case "IN": SetImm8(line, 0xDB, errors); return;
                case "OUT": SetImm8(line, 0xD3, errors); return;
            }

            if (mnemonic.Length > 0 && mnemonic[0] == 'J')
            {
                string condition = mnemonic.Substring(1);
                int index;
                if (TryConditionCode(condition, out index))
                {
                    line.Form = OperandForm.Imm16;
                    line.Opcode = JumpConditionOpcodes[index];
                    line.Size = 3;
                    ExpectCount(line, 1, errors);
                    if (line.Operands.Count == 1)
                        line.Expression = line.Operands[0];
                    return;
                }

                AddError(errors, line.LineNumber, "Invalid condition code '" + condition + "'");
                line.Form = OperandForm.Imm16;
                line.Size = 3;
                line.Invalid = true;
                return;
            }

            if (mnemonic.Length > 0 && mnemonic[0] == 'C')
            {
                string condition = mnemonic.Substring(1);
                int index;
                if (TryConditionCode(condition, out index))
                {
                    line.Form = OperandForm.Imm16;
                    line.Opcode = CallConditionOpcodes[index];
                    line.Size = 3;
                    ExpectCount(line, 1, errors);
                    if (line.Operands.Count == 1)
                        line.Expression = line.Operands[0];
                    return;
                }

                AddError(errors, line.LineNumber, "Invalid condition code '" + condition + "'");
                line.Form = OperandForm.Imm16;
                line.Size = 3;
                line.Invalid = true;
                return;
            }

            if (mnemonic.Length > 0 && mnemonic[0] == 'R')
            {
                string condition = mnemonic.Substring(1);
                int index;
                if (TryConditionCode(condition, out index))
                {
                    line.Form = OperandForm.Implied;
                    line.Opcode = ReturnConditionOpcodes[index];
                    line.Size = 1;
                    ExpectCount(line, 0, errors);
                    return;
                }

                AddError(errors, line.LineNumber, "Invalid condition code '" + condition + "'");
                line.Form = OperandForm.Implied;
                line.Size = 1;
                line.Invalid = true;
                return;
            }

            AddError(errors, line.LineNumber, "Unknown mnemonic '" + mnemonic + "'");
            line.Invalid = true;
        }

        private static void SetImplied(ParsedLine line, byte opcode, List<AssemblerError> errors)
        {
            line.Form = OperandForm.Implied;
            line.Opcode = opcode;
            line.Size = 1;
            ExpectCount(line, 0, errors);
        }

        private static void SetImm8(ParsedLine line, byte opcode, List<AssemblerError> errors)
        {
            line.Form = OperandForm.Imm8;
            line.Opcode = opcode;
            line.Size = 2;
            ExpectCount(line, 1, errors);
            if (line.Operands.Count == 1)
                line.Expression = line.Operands[0];
        }

        private static void SetImm16(ParsedLine line, byte opcode, List<AssemblerError> errors)
        {
            line.Form = OperandForm.Imm16;
            line.Opcode = opcode;
            line.Size = 3;
            ExpectCount(line, 1, errors);
            if (line.Operands.Count == 1)
                line.Expression = line.Operands[0];
        }

        private static void ValidateMov(ParsedLine line, List<AssemblerError> errors)
        {
            line.Form = OperandForm.Mov;
            line.Size = 1;
            if (!ExpectCount(line, 2, errors))
                return;

            int destination;
            int source;
            if (!TryRegister(line.Operands[0], out destination))
            {
                AddError(errors, line.LineNumber, "Invalid register '" + line.Operands[0] + "'");
                line.Invalid = true;
            }

            if (!TryRegister(line.Operands[1], out source))
            {
                AddError(errors, line.LineNumber, "Invalid register '" + line.Operands[1] + "'");
                line.Invalid = true;
            }

            line.Reg1 = destination;
            line.Reg2 = source;
        }

        private static void ValidateMvi(ParsedLine line, List<AssemblerError> errors)
        {
            line.Form = OperandForm.Imm8;
            line.Size = 2;
            if (!ExpectCount(line, 2, errors))
                return;

            int destination;
            if (!TryRegister(line.Operands[0], out destination))
            {
                AddError(errors, line.LineNumber, "Invalid register '" + line.Operands[0] + "'");
                line.Invalid = true;
                return;
            }

            line.Opcode = (byte)(0x06 | (destination << 3));
            line.Expression = line.Operands[1];
        }

        private static void ValidateLxi(ParsedLine line, List<AssemblerError> errors)
        {
            line.Form = OperandForm.Imm16;
            line.Size = 3;
            if (!ExpectCount(line, 2, errors))
                return;

            int pair;
            if (!TryLxiPair(line.Operands[0], out pair))
            {
                AddError(errors, line.LineNumber, "Invalid register pair '" + line.Operands[0] + "' for LXI");
                line.Invalid = true;
                return;
            }

            line.Opcode = (byte)(0x01 | (pair << 4));
            line.Expression = line.Operands[1];
        }

        private static void ValidateLdaxStax(ParsedLine line, byte opcode, List<AssemblerError> errors)
        {
            line.Form = OperandForm.Implied;
            line.Size = 1;
            if (!ExpectCount(line, 1, errors))
                return;

            int pair;
            if (!TryBdPair(line.Operands[0], out pair))
            {
                AddError(errors, line.LineNumber, "Invalid register pair '" + line.Operands[0] + "' (expected B/BC or D/DE)");
                line.Invalid = true;
                return;
            }

            line.Opcode = (byte)(opcode | (pair << 4));
        }

        private static void ValidateAluRegister(ParsedLine line, byte opcode, List<AssemblerError> errors)
        {
            line.Form = OperandForm.Implied;
            line.Size = 1;
            if (!ExpectCount(line, 1, errors))
                return;

            int register;
            if (!TryRegister(line.Operands[0], out register))
            {
                AddError(errors, line.LineNumber, "Invalid register '" + line.Operands[0] + "'");
                line.Invalid = true;
                return;
            }

            line.Opcode = (byte)(opcode | register);
        }

        private static void ValidateInrDcr(ParsedLine line, byte opcode, List<AssemblerError> errors)
        {
            line.Form = OperandForm.Implied;
            line.Size = 1;
            if (!ExpectCount(line, 1, errors))
                return;

            int register;
            if (!TryRegister(line.Operands[0], out register))
            {
                AddError(errors, line.LineNumber, "Invalid register '" + line.Operands[0] + "'");
                line.Invalid = true;
                return;
            }

            line.Opcode = (byte)(opcode | (register << 3));
        }

        private static void ValidateInxDcxDad(ParsedLine line, byte opcode, List<AssemblerError> errors)
        {
            line.Form = OperandForm.Implied;
            line.Size = 1;
            if (!ExpectCount(line, 1, errors))
                return;

            int pair;
            if (!TryLxiPair(line.Operands[0], out pair))
            {
                AddError(errors, line.LineNumber, "Invalid register pair '" + line.Operands[0] + "'");
                line.Invalid = true;
                return;
            }

            line.Opcode = (byte)(opcode | (pair << 4));
        }

        private static void ValidateRst(ParsedLine line, List<AssemblerError> errors)
        {
            line.Form = OperandForm.Rst;
            line.Size = 1;
            ExpectCount(line, 1, errors);
            if (line.Operands.Count == 1)
                line.Expression = line.Operands[0];
        }

        private static void ValidatePushPop(ParsedLine line, byte opcode, List<AssemblerError> errors)
        {
            line.Form = OperandForm.Implied;
            line.Size = 1;
            if (!ExpectCount(line, 1, errors))
                return;

            int pair;
            if (!TryPushPopPair(line.Operands[0], out pair))
            {
                AddError(errors, line.LineNumber, "Invalid register pair '" + line.Operands[0] + "' (expected B/BC, D/DE, H/HL or PSW)");
                line.Invalid = true;
                return;
            }

            line.Opcode = (byte)(opcode | (pair << 4));
        }

        private static bool ExpectCount(ParsedLine line, int expected, List<AssemblerError> errors)
        {
            if (line.Operands.Count == expected)
                return true;

            if (expected == 0)
                AddError(errors, line.LineNumber, line.Mnemonic + " does not take operands");
            else
                AddError(errors, line.LineNumber, line.Mnemonic + " requires " + expected +
                    (expected == 1 ? " operand" : " operands") +
                    " (found " + line.Operands.Count + ")");
            line.Invalid = true;
            return false;
        }

        private static bool TryRegister(string text, out int code)
        {
            switch ((text ?? string.Empty).ToUpperInvariant())
            {
                case "B": code = 0; return true;
                case "C": code = 1; return true;
                case "D": code = 2; return true;
                case "E": code = 3; return true;
                case "H": code = 4; return true;
                case "L": code = 5; return true;
                case "M": code = 6; return true;
                case "A": code = 7; return true;
                default: code = 0; return false;
            }
        }

        private static bool TryLxiPair(string text, out int code)
        {
            switch ((text ?? string.Empty).ToUpperInvariant())
            {
                case "B":
                case "BC": code = 0; return true;
                case "D":
                case "DE": code = 1; return true;
                case "H":
                case "HL": code = 2; return true;
                case "SP": code = 3; return true;
                default: code = 0; return false;
            }
        }

        private static bool TryBdPair(string text, out int code)
        {
            switch ((text ?? string.Empty).ToUpperInvariant())
            {
                case "B":
                case "BC": code = 0; return true;
                case "D":
                case "DE": code = 1; return true;
                default: code = 0; return false;
            }
        }

        private static bool TryPushPopPair(string text, out int code)
        {
            switch ((text ?? string.Empty).ToUpperInvariant())
            {
                case "B":
                case "BC": code = 0; return true;
                case "D":
                case "DE": code = 1; return true;
                case "H":
                case "HL": code = 2; return true;
                case "PSW": code = 3; return true;
                default: code = 0; return false;
            }
        }

        private static bool TryConditionCode(string text, out int index)
        {
            switch ((text ?? string.Empty).ToUpperInvariant())
            {
                case "NZ": index = 0; return true;
                case "Z": index = 1; return true;
                case "NC": index = 2; return true;
                case "C": index = 3; return true;
                case "PO": index = 4; return true;
                case "PE": index = 5; return true;
                case "P": index = 6; return true;
                case "M": index = 7; return true;
                default: index = 0; return false;
            }
        }
    }
}
