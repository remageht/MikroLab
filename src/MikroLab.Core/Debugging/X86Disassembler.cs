using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MikroLab.Core.Debugging
{
    /// <summary>
    /// One instruction decoded from machine code. Unlike the 8080 instruction
    /// the x86 instruction has no fixed length, so the bytes are kept as an
    /// array.
    /// </summary>
    public readonly struct DecodedInstruction
    {
        public DecodedInstruction(ulong address, byte[] bytes, string mnemonic, string operands)
        {
            Address = address;
            Bytes = bytes ?? Array.Empty<byte>();
            Mnemonic = mnemonic ?? string.Empty;
            Operands = operands ?? string.Empty;
        }

        public ulong Address { get; }
        public byte[] Bytes { get; }
        public int Length => Bytes.Length;
        public string Mnemonic { get; }
        public string Operands { get; }

        /// <summary>Mnemonic and operands, for example "mov ax, 1234h".</summary>
        public string Text => Operands.Length == 0 ? Mnemonic : Mnemonic + " " + Operands;

        public string BytesText
        {
            get
            {
                var text = new StringBuilder(Bytes.Length * 3);
                for (int i = 0; i < Bytes.Length; i++)
                {
                    if (i > 0) text.Append(' ');
                    text.Append(Bytes[i].ToString("X2"));
                }
                return text.ToString();
            }
        }

        public override string ToString()
        {
            return Address.ToString("X4") + "h  " + BytesText + "  " + Text;
        }
    }

    /// <summary>
    /// Disassembler of the Intel x86 family in the Intel syntax: it decodes
    /// machine code back into instructions. One engine serves both the 8086
    /// (real mode, 16-bit operands) and the x86-64 (long mode): the modes
    /// differ in the default operand size, the REX prefix and the
    /// RIP-relative addressing.
    /// </summary>
    public sealed class X86Disassembler
    {
        private static readonly string[] Reg8 = { "AL", "CL", "DL", "BL", "AH", "CH", "DH", "BH" };
        private static readonly string[] Reg8Rex = { "AL", "CL", "DL", "BL", "SPL", "BPL", "SIL", "DIL" };
        private static readonly string[] Reg16 = { "AX", "CX", "DX", "BX", "SP", "BP", "SI", "DI" };
        private static readonly string[] Reg32 = { "EAX", "ECX", "EDX", "EBX", "ESP", "EBP", "ESI", "EDI" };
        private static readonly string[] Reg64 = { "RAX", "RCX", "RDX", "RBX", "RSP", "RBP", "RSI", "RDI" };
        private static readonly string[] SegmentRegisters = { "es", "cs", "ss", "ds", "fs", "gs" };

        private static readonly string[] Conditions =
        {
            "o", "no", "b", "ae", "e", "ne", "be", "a", "s", "ns", "p", "np", "l", "ge", "le", "g"
        };

        private static readonly string[] AluOperations =
        {
            "add", "or", "adc", "sbb", "and", "sub", "xor", "cmp"
        };

        private static readonly string[] ShiftOperations =
        {
            "rol", "ror", "rcl", "rcr", "shl", "shr", "sal", "sar"
        };

        private readonly byte[] _code;
        private readonly int _start;
        private readonly ulong _address;
        private readonly bool _longMode;
        private int _position;

        // prefixes
        private bool _operand16;
        private bool _address32;
        private byte _rex;
        private bool _rexPresent;
        private string _segment;
        private bool _rep;
        private bool _repne;

        // ModRM
        private int _mod;
        private int _reg;
        private int _rm;
        private bool _registerOperand;
        private string _memory;

        public X86Disassembler(byte[] code, int offset, ulong address, bool longMode)
        {
            _code = code ?? Array.Empty<byte>();
            _start = offset;
            _position = offset;
            _address = address;
            _longMode = longMode;
        }

        /// <summary>Decodes one instruction at the given position.</summary>
        public DecodedInstruction Decode()
        {
            int start = _position;
            byte op = ReadOpcode();

            string mnemonic;
            string operands;

            if (op == 0x0F)
            {
                DecodeTwoByte(out mnemonic, out operands);
            }
            else
            {
                DecodeOneByte(op, out mnemonic, out operands);
            }

            byte[] bytes = new byte[Math.Min(_position - start, _code.Length - start)];
            if (bytes.Length > 0)
                Array.Copy(_code, start, bytes, 0, bytes.Length);
            return new DecodedInstruction(_address + (ulong)(start - _start), bytes, mnemonic, operands);
        }

        // ------------------------------------------------------------------
        // prefixes
        // ------------------------------------------------------------------

        private byte ReadOpcode()
        {
            while (true)
            {
                byte op = Next();

                if (_longMode && op >= 0x40 && op <= 0x4F)
                {
                    _rex = op;
                    _rexPresent = true;
                    continue;
                }

                switch (op)
                {
                    case 0x66: _operand16 = true; continue;
                    case 0x67: _address32 = true; continue;
                    case 0x26: _segment = SegmentRegisters[0]; continue;
                    case 0x2E: _segment = SegmentRegisters[1]; continue;
                    case 0x36: _segment = SegmentRegisters[2]; continue;
                    case 0x3E: _segment = SegmentRegisters[3]; continue;
                    case 0x64: _segment = SegmentRegisters[4]; continue;
                    case 0x65: _segment = SegmentRegisters[5]; continue;
                    case 0xF0: continue;                       // LOCK
                    case 0xF2: _repne = true; continue;
                    case 0xF3: _rep = true; continue;
                }

                return op;
            }
        }

        private byte Next()
        {
            byte value = _position < _code.Length ? _code[_position] : (byte)0;
            _position++;
            return value;
        }

        private ushort NextWord() => (ushort)(Next() | (Next() << 8));

        private uint NextDword()
        {
            return (uint)(Next() | (Next() << 8) | (Next() << 16) | (Next() << 24));
        }

        private ulong NextQword()
        {
            uint low = NextDword();
            uint high = NextDword();
            return low | ((ulong)high << 32);
        }

        // ------------------------------------------------------------------
        // helpers
        // ------------------------------------------------------------------

        private int OperandSize()
        {
            if (!_longMode) return 2;
            if (_rexPresent && (_rex & 0x08) != 0) return 8;
            return _operand16 ? 2 : 4;
        }

        private string RegisterName(int index, int size)
        {
            switch (size)
            {
                case 1:
                    if (index >= 8) return "R" + (index - 8).ToString(CultureInfo.InvariantCulture) + "B";
                    return _rexPresent ? Reg8Rex[index] : Reg8[index];
                case 2:
                    return index >= 8
                        ? "R" + (index - 8).ToString(CultureInfo.InvariantCulture) + "W"
                        : Reg16[index];
                case 4:
                    return index >= 8
                        ? "R" + (index - 8).ToString(CultureInfo.InvariantCulture) + "D"
                        : Reg32[index];
                default:
                    return index >= 8
                        ? "R" + (index - 8).ToString(CultureInfo.InvariantCulture)
                        : Reg64[index];
            }
        }

        private static string Hex(long value, int digits)
        {
            string text = ((ulong)value).ToString("X" + digits.ToString(CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture);
            return text + "h";
        }

        private static string Hex(long value)
        {
            return ((ulong)value).ToString("X", CultureInfo.InvariantCulture) + "h";
        }

        /// <summary>Hexadecimal value with the sign, for example "-2h".</summary>
        private static string HexSigned(long value)
        {
            if (value < 0)
                return "-" + ((ulong)(-value)).ToString("X", CultureInfo.InvariantCulture) + "h";

            return ((ulong)value).ToString("X", CultureInfo.InvariantCulture) + "h";
        }

        /// <summary>Hexadecimal byte with the sign, for example "-1h" or "03h".</summary>
        private static string HexByte(sbyte value)
        {
            if (value < 0)
                return "-" + ((ulong)(-value)).ToString("X", CultureInfo.InvariantCulture) + "h";

            return ((ulong)value).ToString("X2", CultureInfo.InvariantCulture) + "h";
        }

        private string SizePrefix(int size)
        {
            switch (size)
            {
                case 1: return "byte ";
                case 2: return "word ";
                case 4: return "dword ";
                default: return "qword ";
            }
        }

        private string MemoryOperand(int size)
        {
            string prefix = SizePrefix(size);
            if (_segment != null) return prefix + _segment + ":" + _memory;
            return prefix + _memory;
        }

        private string RegisterOperand(int size)
        {
            return RegisterName(_reg, size);
        }

        /// <summary>Operand of the r/m field: a register or a memory cell.</summary>
        private string RmOperand(int size)
        {
            if (_registerOperand) return RegisterName(_rm, size);
            return MemoryOperand(size);
        }

        private string RegRmOperand(int size)
        {
            return RegisterName(_reg, size);
        }

        // ------------------------------------------------------------------
        // ModRM
        // ------------------------------------------------------------------

        private void DecodeModrm()
        {
            byte modrm = Next();
            _mod = (modrm >> 6) & 3;
            _reg = ((modrm >> 3) & 7) + (_rexPresent && (_rex & 0x04) != 0 ? 8 : 0);
            _rm = modrm & 7;

            if (_mod == 3)
            {
                _registerOperand = true;
                if (_rexPresent && (_rex & 0x01) != 0) _rm += 8;
                return;
            }

            _registerOperand = false;
            if (_longMode || _address32)
                DecodeModrm32();
            else
                DecodeModrm16();
        }

        private void DecodeModrm16()
        {
            string baseReg;
            string indexReg = null;

            switch (_rm)
            {
                case 0: baseReg = "bx"; indexReg = "si"; break;
                case 1: baseReg = "bx"; indexReg = "di"; break;
                case 2: baseReg = "bp"; indexReg = "si"; break;
                case 3: baseReg = "bp"; indexReg = "di"; break;
                case 4: baseReg = "si"; break;
                case 5: baseReg = "di"; break;
                case 6: baseReg = "bp"; break;
                default: baseReg = "bx"; break;
            }

            if (_mod == 0 && _rm == 6)
            {
                // прямой адрес: смещение без базового регистра
                int offset = NextWord();
                _memory = "[" + Hex((ushort)offset) + "]";
                return;
            }

            long displacement = 0;
            if (_mod == 1) displacement = (sbyte)Next();
            else if (_mod == 2) displacement = (short)NextWord();

            _memory = BuildMemory(baseReg, indexReg, 0, displacement);
        }

        /// <summary>
        /// Собирает текст адреса памяти: база, индекс и смещение.
        /// </summary>
        private static string BuildMemory(string baseReg, string indexReg, int scale, long displacement)
        {
            var text = new StringBuilder("[");

            if (baseReg != null)
                text.Append(baseReg);

            if (indexReg != null)
            {
                if (baseReg != null) text.Append('+');
                text.Append(indexReg);
                if (scale > 1) text.Append('*').Append(scale.ToString(CultureInfo.InvariantCulture));
            }

            if (displacement != 0)
            {
                if (displacement < 0)
                {
                    text.Append('-').Append(((ulong)(-displacement)).ToString("X", CultureInfo.InvariantCulture)).Append('h');
                }
                else
                {
                    if (baseReg != null || indexReg != null) text.Append('+');
                    text.Append(((ulong)displacement).ToString("X", CultureInfo.InvariantCulture)).Append('h');
                }
            }

            text.Append(']');
            return text.ToString();
        }

        private void DecodeModrm32()
        {
            string baseReg = null;
            string indexReg = null;
            int scale = 1;
            long displacement = 0;
            bool ripRelative = false;

            // в длинном режиме адресные регистры 64-разрядные
            int addressSize = _longMode && !_address32 ? 8 : 4;

            if (_rm == 4)
            {
                byte sib = Next();
                scale = 1 << (sib >> 6);
                int index = (sib >> 3) & 7;
                int baseRegValue = sib & 7;
                bool indexPresent = index != 4 || (_rexPresent && (_rex & 0x02) != 0);
                bool basePresent = !(baseRegValue == 5 && _mod == 0);

                if (basePresent)
                {
                    int value = baseRegValue + (_rexPresent && (_rex & 0x01) != 0 ? 8 : 0);
                    baseReg = RegisterName(value, addressSize);
                }

                if (indexPresent)
                {
                    int value = index + (_rexPresent && (_rex & 0x02) != 0 ? 8 : 0);
                    indexReg = RegisterName(value, addressSize);
                }
            }
            else if (_rm == 5 && _mod == 0)
            {
                if (_longMode)
                {
                    ripRelative = true;
                    displacement = unchecked((int)NextDword());
                }
                else
                {
                    displacement = unchecked((int)NextDword());
                    _memory = "[" + Hex((uint)displacement) + "]";
                    return;
                }
            }
            else
            {
                int value = _rm + (_rexPresent && (_rex & 0x01) != 0 ? 8 : 0);
                baseReg = RegisterName(value, addressSize);
            }

            if (_mod == 1) displacement = (sbyte)Next();
            else if (_mod == 2) displacement = unchecked((int)NextDword());

            if (ripRelative)
            {
                _memory = "[rip" + (displacement < 0 ? "-" : "+") +
                    ((ulong)Math.Abs(displacement)).ToString("X", CultureInfo.InvariantCulture) + "h]";
                return;
            }

            _memory = BuildMemory(baseReg, indexReg, scale, displacement);
        }

        private ulong CurrentAddress()
        {
            return _address + (ulong)(_position - _start);
        }

        /// <summary>
        /// Address of a jump target. In the real mode the address wraps inside
        /// the 64 K segment, in the long mode it is a plain 64-bit value.
        /// </summary>
        private string Target(long relative)
        {
            ulong target = CurrentAddress() + (ulong)relative;
            if (!_longMode) target &= 0xFFFFUL;
            return target.ToString(_longMode ? "X8" : "X4", CultureInfo.InvariantCulture) + "h";
        }

        // ------------------------------------------------------------------
        // one byte opcodes
        // ------------------------------------------------------------------

        private void DecodeOneByte(byte op, out string mnemonic, out string operands)
        {
            mnemonic = "db";
            operands = Hex(op);

            // 00..3F: восемь групп арифметических и логических операций
            if (op < 0x40 && (op & 7) <= 5)
            {
                DecodeAluGroup(op);
                mnemonic = AluOperations[op >> 3];
                operands = _aluOperands;
                return;
            }

            switch (op)
            {
                case 0x06: mnemonic = "push"; operands = SegmentRegisters[0]; return;
                case 0x0E: mnemonic = "push"; operands = SegmentRegisters[1]; return;
                case 0x16: mnemonic = "push"; operands = SegmentRegisters[2]; return;
                case 0x1E: mnemonic = "push"; operands = SegmentRegisters[3]; return;
                case 0x07: mnemonic = "pop"; operands = SegmentRegisters[0]; return;
                case 0x0F: mnemonic = "pop"; operands = SegmentRegisters[1]; return;
                case 0x17: mnemonic = "pop"; operands = SegmentRegisters[2]; return;
                case 0x1F: mnemonic = "pop"; operands = SegmentRegisters[3]; return;
                case 0x27: mnemonic = "daa"; operands = string.Empty; return;
                case 0x2F: mnemonic = "das"; operands = string.Empty; return;
                case 0x37: mnemonic = "aaa"; operands = string.Empty; return;
                case 0x3F: mnemonic = "aas"; operands = string.Empty; return;
            }

            if (op >= 0x40 && op <= 0x47)
            {
                mnemonic = "inc";
                operands = RegisterName(op & 7, 2);
                return;
            }

            if (op >= 0x48 && op <= 0x4F)
            {
                mnemonic = "dec";
                operands = RegisterName(op & 7, 2);
                return;
            }

            if (op >= 0x50 && op <= 0x57)
            {
                mnemonic = "push";
                operands = RegisterName((op & 7) + (_rexPresent && (_rex & 0x01) != 0 ? 8 : 0),
                    _longMode ? 8 : 2);
                return;
            }

            if (op >= 0x58 && op <= 0x5F)
            {
                mnemonic = "pop";
                operands = RegisterName((op & 7) + (_rexPresent && (_rex & 0x01) != 0 ? 8 : 0),
                    _longMode ? 8 : 2);
                return;
            }

            switch (op)
            {
                case 0x60: mnemonic = "pusha"; operands = string.Empty; return;
                case 0x61: mnemonic = "popa"; operands = string.Empty; return;
                case 0x62: mnemonic = "bound"; return;
                case 0x63:
                {
                    if (_longMode && _rexPresent && (_rex & 0x08) != 0)
                    {
                        // MOVSXD r64, r/m32
                        DecodeModrm();
                        mnemonic = "movsxd";
                        operands = RegRmOperand(8) + ", " + RmOperand(4);
                        return;
                    }

                    DecodeModrm();
                    mnemonic = "arpl";
                    operands = RmOperand(2) + ", " + RegRmOperand(2);
                    return;
                }
                case 0x68:
                    mnemonic = "push";
                    operands = _longMode ? Hex(NextDword()) : Hex(NextWord());
                    return;
                case 0x69:
                {
                    DecodeModrm();
                    int size = OperandSize();
                    uint immediate = _longMode ? NextDword() : NextWord();
                    mnemonic = "imul";
                    operands = RegisterName(_reg, size) + ", " + RmOperand(size) + ", " + Hex(immediate);
                    return;
                }
                case 0x6A:
                {
                    sbyte disp = (sbyte)Next();
                    mnemonic = "push";
                    operands = HexByte(disp);
                    return;
                }
                case 0x6B:
                {
                    DecodeModrm();
                    int size = OperandSize();
                    sbyte disp = (sbyte)Next();
                    mnemonic = "imul";
                    operands = RegisterName(_reg, size) + ", " + RmOperand(size) + ", " + HexByte(disp);
                    return;
                }
                case 0x6C: mnemonic = "insb"; operands = string.Empty; return;
                case 0x6D: mnemonic = _longMode ? "insd" : "insw"; operands = string.Empty; return;
                case 0x6E: mnemonic = "outsb"; operands = string.Empty; return;
                case 0x6F: mnemonic = _longMode ? "outsd" : "outsw"; operands = string.Empty; return;
            }

            if (op >= 0x70 && op <= 0x7F)
            {
                sbyte disp = (sbyte)Next();
                mnemonic = "j" + Conditions[op & 0x0F];
                operands = Target(disp);
                return;
            }

            switch (op)
            {
                case 0x80:
                {
                    DecodeModrm();
                    byte immediate = Next();
                    mnemonic = AluOperations[_reg];
                    operands = RmOperand(1) + ", " + Hex(immediate);
                    return;
                }
                case 0x81:
                {
                    DecodeModrm();
                    int size = OperandSize();
                    uint immediate = ReadImmediate(size);
                    mnemonic = AluOperations[_reg];
                    operands = RmOperand(size) + ", " + Hex(immediate, size == 8 ? 16 : size * 2);
                    return;
                }
                case 0x82:
                {
                    DecodeModrm();
                    byte immediate = Next();
                    mnemonic = AluOperations[_reg];
                    operands = RmOperand(1) + ", " + Hex(immediate);
                    return;
                }
                case 0x83:
                {
                    DecodeModrm();
                    int size = OperandSize();
                    sbyte immediate = (sbyte)Next();
                    mnemonic = AluOperations[_reg];
                    operands = RmOperand(size) + ", " + HexByte(immediate);
                    return;
                }
                case 0x84:
                {
                    DecodeModrm();
                    mnemonic = "test";
                    operands = RmOperand(1) + ", " + RegRmOperand(1);
                    return;
                }
                case 0x85:
                {
                    DecodeModrm();
                    int size = OperandSize();
                    mnemonic = "test";
                    operands = RmOperand(size) + ", " + RegRmOperand(size);
                    return;
                }
                case 0x86:
                {
                    DecodeModrm();
                    mnemonic = "xchg";
                    operands = RmOperand(1) + ", " + RegRmOperand(1);
                    return;
                }
                case 0x87:
                {
                    DecodeModrm();
                    int size = OperandSize();
                    mnemonic = "xchg";
                    operands = RmOperand(size) + ", " + RegRmOperand(size);
                    return;
                }
                case 0x88:
                {
                    DecodeModrm();
                    mnemonic = "mov";
                    operands = RmOperand(1) + ", " + RegRmOperand(1);
                    return;
                }
                case 0x89:
                {
                    DecodeModrm();
                    int size = OperandSize();
                    mnemonic = "mov";
                    operands = RmOperand(size) + ", " + RegRmOperand(size);
                    return;
                }
                case 0x8A:
                {
                    DecodeModrm();
                    mnemonic = "mov";
                    operands = RegRmOperand(1) + ", " + RmOperand(1);
                    return;
                }
                case 0x8B:
                {
                    DecodeModrm();
                    int size = OperandSize();
                    mnemonic = "mov";
                    operands = RegRmOperand(size) + ", " + RmOperand(size);
                    return;
                }
                case 0x8C:
                {
                    DecodeModrm();
                    mnemonic = "mov";
                    operands = RmOperand(2) + ", " + SegmentRegisters[_reg & 3];
                    return;
                }
                case 0x8D:
                {
                    DecodeModrm();
                    int size = OperandSize();
                    mnemonic = "lea";
                    // у LEA нет размера операнда памяти
                    operands = RegRmOperand(size) + ", " + (_registerOperand
                        ? RmOperand(size)
                        : (string.IsNullOrEmpty(_segment) ? _memory : _segment + ":" + _memory));
                    return;
                }
                case 0x8E:
                {
                    DecodeModrm();
                    mnemonic = "mov";
                    operands = SegmentRegisters[_reg & 3] + ", " + RmOperand(2);
                    return;
                }
                case 0x8F:
                {
                    DecodeModrm();
                    mnemonic = "pop";
                    operands = RmOperand(2);
                    return;
                }
            }

            if (op >= 0x90 && op <= 0x97)
            {
                if (op == 0x90)
                {
                    mnemonic = "nop";
                    operands = string.Empty;
                    return;
                }

                int size = OperandSize();
                mnemonic = "xchg";
                operands = RegisterName(0, size) + ", " + RegisterName((op & 7) + (_rexPresent && (_rex & 0x01) != 0 ? 8 : 0), size);
                return;
            }

            switch (op)
            {
                case 0x98: mnemonic = _longMode ? "cdqe" : "cbw"; operands = string.Empty; return;
                case 0x99: mnemonic = _longMode ? "cqo" : "cwd"; operands = string.Empty; return;
                case 0x9A:
                {
                    int offset = NextWord();
                    int segment = NextWord();
                    mnemonic = "call";
                    operands = Hex((ushort)segment) + ":" + Hex((ushort)offset);
                    return;
                }
                case 0x9C: mnemonic = "pushf"; operands = string.Empty; return;
                case 0x9D: mnemonic = "popf"; operands = string.Empty; return;
                case 0x9E: mnemonic = "sahf"; operands = string.Empty; return;
                case 0x9F: mnemonic = "lahf"; operands = string.Empty; return;
            }

            switch (op)
            {
                case 0xA0:
                {
                    int offset = NextWord();
                    mnemonic = "mov";
                    operands = "AL, " + SizePrefix(1) + "[" + Hex((ushort)offset) + "]";
                    return;
                }
                case 0xA1:
                {
                    int size = OperandSize();
                    int offset = NextWord();
                    mnemonic = "mov";
                    operands = RegisterName(0, size) + ", " + SizePrefix(size) + "[" + Hex((ushort)offset) + "]";
                    return;
                }
                case 0xA2:
                {
                    int offset = NextWord();
                    mnemonic = "mov";
                    operands = SizePrefix(1) + "[" + Hex((ushort)offset) + "], AL";
                    return;
                }
                case 0xA3:
                {
                    int size = OperandSize();
                    int offset = NextWord();
                    mnemonic = "mov";
                    operands = SizePrefix(size) + "[" + Hex((ushort)offset) + "], " + RegisterName(0, size);
                    return;
                }
            }

            if (op >= 0xA4 && op <= 0xAF)
            {
                DecodeStringOperation(op, out mnemonic, out operands);
                return;
            }

            if (op >= 0xB0 && op <= 0xB7)
            {
                byte immediate = Next();
                mnemonic = "mov";
                operands = RegisterName(op & 7, 1) + ", " + Hex(immediate);
                return;
            }

            if (op >= 0xB8 && op <= 0xBF)
            {
                int size = OperandSize();
                if (size == 8)
                {
                    // mov r64, imm64 — непосредственное значение занимает 8 байт
                    ulong immediate = NextQword();
                    mnemonic = "mov";
                    operands = RegisterName(op & 7, size) + ", " + Hex((long)immediate, 16);
                    return;
                }

                uint value = ReadImmediate(size);
                mnemonic = "mov";
                operands = RegisterName(op & 7, size) + ", " + Hex(value, size * 2);
                return;
            }

            switch (op)
            {
                case 0xC0:
                case 0xC1:
                case 0xD0:
                case 0xD1:
                case 0xD2:
                case 0xD3:
                {
                    int size = (op == 0xC0 || op == 0xD0 || op == 0xD2) ? 1 : OperandSize();
                    DecodeModrm();
                    string shift = ShiftOperations[_reg & 7];
                    string amount;
                    if (op == 0xC0 || op == 0xC1)
                        amount = Hex(Next());
                    else if (op == 0xD0 || op == 0xD1)
                        amount = "1";
                    else
                        amount = "CL";

                    mnemonic = shift;
                    operands = RmOperand(size) + ", " + amount;
                    return;
                }
                case 0xC2:
                {
                    int bytes = NextWord();
                    mnemonic = "ret";
                    operands = Hex((ushort)bytes);
                    return;
                }
                case 0xC3: mnemonic = "ret"; operands = string.Empty; return;
                case 0xC4: mnemonic = "les"; return;
                case 0xC5: mnemonic = "lds"; return;
                case 0xC6:
                {
                    DecodeModrm();
                    byte immediate = Next();
                    mnemonic = "mov";
                    operands = RmOperand(1) + ", " + Hex(immediate);
                    return;
                }
                case 0xC7:
                {
                    DecodeModrm();
                    int size = OperandSize();
                    uint immediate = ReadImmediate(size);
                    mnemonic = "mov";
                    operands = RmOperand(size) + ", " + Hex(immediate, size == 8 ? 16 : size * 2);
                    return;
                }
                case 0xC8:
                {
                    int bytes = NextWord();
                    byte level = Next();
                    mnemonic = "enter";
                    operands = Hex((ushort)bytes) + ", " + Hex(level);
                    return;
                }
                case 0xC9: mnemonic = "leave"; operands = string.Empty; return;
                case 0xCA:
                {
                    int bytes = NextWord();
                    mnemonic = "retf";
                    operands = Hex((ushort)bytes);
                    return;
                }
                case 0xCB: mnemonic = "retf"; operands = string.Empty; return;
                case 0xCC: mnemonic = "int3"; operands = string.Empty; return;
                case 0xCD:
                {
                    byte vector = Next();
                    mnemonic = "int";
                    operands = Hex(vector);
                    return;
                }
                case 0xCE: mnemonic = "into"; operands = string.Empty; return;
                case 0xCF: mnemonic = "iret"; operands = string.Empty; return;
                case 0xD4:
                {
                    byte radix = Next();
                    mnemonic = "aam";
                    operands = Hex(radix);
                    return;
                }
                case 0xD5:
                {
                    byte radix = Next();
                    mnemonic = "aad";
                    operands = Hex(radix);
                    return;
                }
                case 0xD6: mnemonic = "xlat"; operands = string.Empty; return;
                case 0xD7: mnemonic = "xlat"; operands = string.Empty; return;
            }

            if (op >= 0xE0 && op <= 0xE3)
            {
                sbyte disp = (sbyte)Next();
                string name = op == 0xE0 ? "loopnz"
                    : op == 0xE1 ? "loopz"
                    : op == 0xE2 ? "loop" : "jcxz";
                mnemonic = name;
                operands = Target(disp);
                return;
            }

            switch (op)
            {
                case 0xE4:
                {
                    byte port = Next();
                    mnemonic = "in";
                    operands = "AL, " + Hex(port);
                    return;
                }
                case 0xE5:
                {
                    byte port = Next();
                    int size = OperandSize();
                    mnemonic = "in";
                    operands = RegisterName(0, size) + ", " + Hex(port);
                    return;
                }
                case 0xE6:
                {
                    byte port = Next();
                    mnemonic = "out";
                    operands = Hex(port) + ", AL";
                    return;
                }
                case 0xE7:
                {
                    byte port = Next();
                    int size = OperandSize();
                    mnemonic = "out";
                    operands = Hex(port) + ", " + RegisterName(0, size);
                    return;
                }
                case 0xE8:
                {
                    int rel = _longMode ? unchecked((int)NextDword()) : (short)NextWord();
                    mnemonic = "call";
                    operands = Target(rel);
                    return;
                }
                case 0xE9:
                {
                    int rel = _longMode ? unchecked((int)NextDword()) : (short)NextWord();
                    mnemonic = "jmp";
                    operands = Target(rel);
                    return;
                }
                case 0xEA:
                {
                    int offset = NextWord();
                    int segment = NextWord();
                    mnemonic = "jmp";
                    operands = Hex((ushort)segment) + ":" + Hex((ushort)offset);
                    return;
                }
                case 0xEB:
                {
                    sbyte disp = (sbyte)Next();
                    mnemonic = "jmp";
                    operands = Target(disp);
                    return;
                }
                case 0xEC:
                {
                    mnemonic = "in";
                    operands = "AL, DX";
                    return;
                }
                case 0xED:
                {
                    int size = OperandSize();
                    mnemonic = "in";
                    operands = RegisterName(0, size) + ", dx";
                    return;
                }
                case 0xEE:
                {
                    mnemonic = "out";
                    operands = "DX, AL";
                    return;
                }
                case 0xEF:
                {
                    int size = OperandSize();
                    mnemonic = "out";
                    operands = "dx, " + RegisterName(0, size);
                    return;
                }
            }

            switch (op)
            {
                case 0xF1: mnemonic = "int1"; operands = string.Empty; return;
                case 0xF4: mnemonic = "hlt"; operands = string.Empty; return;
                case 0xF5: mnemonic = "cmc"; operands = string.Empty; return;
                case 0xF6:
                {
                    DecodeModrm();
                    mnemonic = Group3(_reg, 1);
                    operands = RmOperand(1) + ImmediateGroup3(_reg, 1);
                    return;
                }
                case 0xF7:
                {
                    DecodeModrm();
                    int size = OperandSize();
                    mnemonic = Group3(_reg, size);
                    operands = RmOperand(size) + ImmediateGroup3(_reg, size);
                    return;
                }
                case 0xF8: mnemonic = "clc"; operands = string.Empty; return;
                case 0xF9: mnemonic = "stc"; operands = string.Empty; return;
                case 0xFA: mnemonic = "cli"; operands = string.Empty; return;
                case 0xFB: mnemonic = "sti"; operands = string.Empty; return;
                case 0xFC: mnemonic = "cld"; operands = string.Empty; return;
                case 0xFD: mnemonic = "std"; operands = string.Empty; return;
                case 0xFE:
                {
                    DecodeModrm();
                    mnemonic = _reg == 0 ? "inc" : "dec";
                    operands = RmOperand(1);
                    return;
                }
                case 0xFF:
                {
                    DecodeModrm();
                    mnemonic = Group5(_reg);
                    operands = RmOperand(OperandSize());
                    return;
                }
            }

            mnemonic = "db";
            operands = Hex(op);
        }

        private string _aluOperands;

        private void DecodeAluGroup(byte op)
        {
            int form = op & 7;
            switch (form)
            {
                case 0:
                {
                    DecodeModrm();
                    _aluOperands = RmOperand(1) + ", " + RegRmOperand(1);
                    return;
                }
                case 1:
                {
                    DecodeModrm();
                    int size = OperandSize();
                    _aluOperands = RmOperand(size) + ", " + RegRmOperand(size);
                    return;
                }
                case 2:
                {
                    DecodeModrm();
                    _aluOperands = RegRmOperand(1) + ", " + RmOperand(1);
                    return;
                }
                case 3:
                {
                    DecodeModrm();
                    int size = OperandSize();
                    _aluOperands = RegRmOperand(size) + ", " + RmOperand(size);
                    return;
                }
                case 4:
                {
                    byte immediate = Next();
                    _aluOperands = "al, " + Hex(immediate);
                    return;
                }
                default:
                {
                    int size = OperandSize();
                    uint immediate = ReadImmediate(size);
                    _aluOperands = RegisterName(0, size) + ", " + Hex(immediate, size == 8 ? 16 : size * 2);
                    return;
                }
            }
        }

        private void DecodeStringOperation(byte op, out string mnemonic, out string operands)
        {
            string name;
            switch (op)
            {
                case 0xA4: name = "movsb"; break;
                case 0xA5: name = _longMode ? "movsq" : (OperandSize() == 4 ? "movsd" : "movsw"); break;
                case 0xA6: name = "cmpsb"; break;
                case 0xA7: name = _longMode ? "cmpsq" : (OperandSize() == 4 ? "cmpsd" : "cmpsw"); break;
                case 0xA8: name = "test"; break;
                case 0xA9: name = "test"; break;
                case 0xAA: name = "stosb"; break;
                case 0xAB: name = _longMode ? "stosq" : (OperandSize() == 4 ? "stosd" : "stosw"); break;
                case 0xAC: name = "lodsb"; break;
                case 0xAD: name = _longMode ? "lodsq" : (OperandSize() == 4 ? "lodsd" : "lodsw"); break;
                case 0xAE: name = "scasb"; break;
                default: name = _longMode ? "scasq" : (OperandSize() == 4 ? "scasd" : "scasw"); break;
            }

            string prefix = string.Empty;
            if (_rep) prefix = "rep ";
            else if (_repne) prefix = "repne ";

            if (op == 0xA8)
            {
                byte immediate = Next();
                mnemonic = prefix + "test";
                operands = "al, " + Hex(immediate);
                return;
            }

            if (op == 0xA9)
            {
                int size = OperandSize();
                uint immediate = ReadImmediate(size);
                mnemonic = prefix + "test";
                operands = RegisterName(0, size) + ", " + Hex(immediate, size == 8 ? 16 : size * 2);
                return;
            }

            mnemonic = prefix + name;
            operands = string.Empty;
        }

        private static string Group3(int reg, int size)
        {
            switch (reg)
            {
                case 0: return "test";
                case 2: return "not";
                case 3: return "neg";
                case 4: return "mul";
                case 5: return "imul";
                case 6: return "div";
                default: return "idiv";
            }
        }

        private string ImmediateGroup3(int reg, int size)
        {
            if (reg != 0) return string.Empty;
            if (size == 1) return ", " + Hex(Next());
            return ", " + Hex(ReadImmediate(size), size == 8 ? 16 : size * 2);
        }

        private static string Group5(int reg)
        {
            switch (reg)
            {
                case 0: return "inc";
                case 1: return "dec";
                case 2: return "call";
                case 3: return "callf";
                case 4: return "jmp";
                case 5: return "jmpf";
                default: return "push";
            }
        }

        private uint ReadImmediate(int size)
        {
            switch (size)
            {
                case 1: return Next();
                case 2: return NextWord();
                case 8: return NextDword();     // в 64-битном режиме непосредственное значение 32-битное
                default: return NextDword();
            }
        }

        // ------------------------------------------------------------------
        // two byte opcodes (0F)
        // ------------------------------------------------------------------

        private void DecodeTwoByte(out string mnemonic, out string operands)
        {
            byte op = Next();
            mnemonic = "db";
            operands = Hex(0x0F) + " " + Hex(op);

            if (op >= 0x40 && op <= 0x4F)
            {
                DecodeModrm();
                int size = OperandSize();
                mnemonic = "cmov" + Conditions[op & 0x0F];
                operands = RegRmOperand(size) + ", " + RmOperand(size);
                return;
            }

            if (op >= 0x80 && op <= 0x8F)
            {
                int rel = _longMode ? unchecked((int)NextDword()) : (short)NextWord();
                mnemonic = "j" + Conditions[op & 0x0F];
                operands = Target(rel);
                return;
            }

            if (op >= 0x90 && op <= 0x9F)
            {
                DecodeModrm();
                mnemonic = "set" + Conditions[op & 0x0F];
                operands = RmOperand(1);
                return;
            }

            switch (op)
            {
                case 0x1F:
                {
                    DecodeModrm();
                    mnemonic = "nop";
                    operands = RmOperand(OperandSize());
                    return;
                }
                case 0x31: mnemonic = "rdtsc"; operands = string.Empty; return;
                case 0xA2: mnemonic = "cpuid"; operands = string.Empty; return;
                case 0xAF:
                {
                    DecodeModrm();
                    int size = OperandSize();
                    mnemonic = "imul";
                    operands = RegRmOperand(size) + ", " + RmOperand(size);
                    return;
                }
                case 0xB6:
                {
                    DecodeModrm();
                    int size = OperandSize();
                    mnemonic = "movzx";
                    operands = RegRmOperand(size) + ", " + RmOperand(1);
                    return;
                }
                case 0xBE:
                {
                    DecodeModrm();
                    int size = OperandSize();
                    mnemonic = "movsx";
                    operands = RegRmOperand(size) + ", " + RmOperand(1);
                    return;
                }
                case 0xB7:
                case 0xBF:
                {
                    DecodeModrm();
                    int size = OperandSize();
                    if (op == 0xB7 && size == 8)
                    {
                        mnemonic = "movsxd";
                        operands = RegRmOperand(size) + ", " + RmOperand(4);
                        return;
                    }

                    mnemonic = "movsx";
                    operands = RegRmOperand(size) + ", " + RmOperand(1);
                    return;
                }
            }

            mnemonic = "db";
            operands = Hex(0x0F) + " " + Hex(op);
        }
    }

    /// <summary>
    /// Disassembler of the Intel 8086 in the real mode.
    /// </summary>
    public static class I8086Disassembler
    {
        public static DecodedInstruction Decode(byte[] code, int offset, ushort address)
        {
            var disassembler = new X86Disassembler(code, offset, address, false);
            return disassembler.Decode();
        }

        public static DecodedInstruction Decode(byte[] code, ushort address)
        {
            return Decode(code, 0, address);
        }

        /// <summary>Decodes one instruction stored in the machine memory.</summary>
        public static DecodedInstruction Decode(Memory.IMemory memory, ushort address)
        {
            if (memory == null) throw new ArgumentNullException(nameof(memory));

            var code = new byte[16];
            for (int i = 0; i < code.Length; i++)
                code[i] = memory.ReadByte((ushort)(address + i));

            return Decode(code, 0, address);
        }
    }

    /// <summary>
    /// Disassembler of the x86-64 in the long mode.
    /// </summary>
    public static class X64Disassembler
    {
        public static DecodedInstruction Decode(byte[] code, int offset, ulong address)
        {
            var disassembler = new X86Disassembler(code, offset, address, true);
            return disassembler.Decode();
        }

        public static DecodedInstruction Decode(byte[] code, ulong address)
        {
            return Decode(code, 0, address);
        }

        /// <summary>Decodes one instruction stored in the machine memory.</summary>
        public static DecodedInstruction Decode(Memory.X64Memory memory, ulong address)
        {
            if (memory == null) throw new ArgumentNullException(nameof(memory));

            var code = new byte[16];
            for (int i = 0; i < code.Length; i++)
                code[i] = memory.ReadByte(address + (ulong)i);

            return Decode(code, 0, address);
        }
    }
}
