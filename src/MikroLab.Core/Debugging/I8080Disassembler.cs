using System;
using System.Text;
using MikroLab.Core.Memory;

namespace MikroLab.Core.Debugging
{
    /// <summary>
    /// One decoded 8080 instruction. The struct keeps the raw bytes so the
    /// execution log can show exactly what the CPU fetched without another
    /// memory read.
    /// </summary>
    public readonly struct DisassembledInstruction
    {
        public DisassembledInstruction(
            ushort address,
            byte byte1,
            byte byte2,
            byte byte3,
            int length,
            string mnemonic,
            string operands)
        {
            Address = address;
            Byte1 = byte1;
            Byte2 = byte2;
            Byte3 = byte3;
            Length = Math.Max(1, Math.Min(3, length));
            Mnemonic = mnemonic ?? "NOP";
            Operands = operands ?? string.Empty;
        }

        public ushort Address { get; }
        public byte Byte1 { get; }
        public byte Byte2 { get; }
        public byte Byte3 { get; }
        public int Length { get; }
        public string Mnemonic { get; }
        public string Operands { get; }

        /// <summary>Mnemonic and operands, for example "MVI A, 55h".</summary>
        public string Text => Operands.Length == 0 ? Mnemonic : Mnemonic + " " + Operands;

        public string BytesText
        {
            get
            {
                var text = new StringBuilder(8);
                text.Append(Byte1.ToString("X2"));
                if (Length >= 2) text.Append(' ').Append(Byte2.ToString("X2"));
                if (Length >= 3) text.Append(' ').Append(Byte3.ToString("X2"));
                return text.ToString();
            }
        }

        public byte[] GetBytes()
        {
            var bytes = new byte[Length];
            bytes[0] = Byte1;
            if (Length >= 2) bytes[1] = Byte2;
            if (Length >= 3) bytes[2] = Byte3;
            return bytes;
        }

        public override string ToString()
        {
            return Address.ToString("X4") + "h  " + BytesText + "  " + Text;
        }
    }

    /// <summary>
    /// Table-free Intel 8080 disassembler. Undocumented but universally
    /// accepted aliases (CBh = JMP, D9h = RET, DDh/EDh/FDh = CALL) are decoded;
    /// genuinely undefined bytes are rendered as data (DB xxh).
    /// </summary>
    public static class I8080Disassembler
    {
        private static readonly string[] Registers = { "B", "C", "D", "E", "H", "L", "M", "A" };
        private static readonly string[] RegisterPairs = { "B", "D", "H", "SP" };
        private static readonly string[] PushPairs = { "B", "D", "H", "PSW" };
        private static readonly string[] Conditions = { "NZ", "Z", "NC", "C", "PO", "PE", "P", "M" };
        private static readonly string[] Alu = { "ADD", "ADC", "SUB", "SBB", "ANA", "XRA", "ORA", "CMP" };
        private static readonly string[] AluImmediate = { "ADI", "ACI", "SUI", "SBI", "ANI", "XRI", "ORI", "CPI" };

        public static DisassembledInstruction Decode(IMemory memory, ushort address)
        {
            if (memory == null) throw new ArgumentNullException(nameof(memory));

            byte b1 = memory.ReadByte(address);
            byte b2 = memory.ReadByte((ushort)(address + 1));
            byte b3 = memory.ReadByte((ushort)(address + 2));
            return Decode(address, b1, b2, b3);
        }

        public static DisassembledInstruction Decode(byte[] memory, ushort address)
        {
            if (memory == null) throw new ArgumentNullException(nameof(memory));

            byte b1 = Read(memory, address);
            byte b2 = Read(memory, (ushort)(address + 1));
            byte b3 = Read(memory, (ushort)(address + 2));
            return Decode(address, b1, b2, b3);
        }

        private static byte Read(byte[] memory, ushort address)
        {
            return address < memory.Length ? memory[address] : (byte)0;
        }

        public static DisassembledInstruction Decode(
            ushort address,
            byte byte1,
            byte byte2,
            byte byte3)
        {
            if (byte1 <= 0x3F)
            {
                return DecodeBlockZero(address, byte1, byte2, byte3);
            }

            if (byte1 <= 0x7F)
            {
                if (byte1 == 0x76)
                {
                    return Make(address, 1, byte1, byte2, byte3, "HLT", string.Empty);
                }

                string destination = Registers[(byte1 >> 3) & 0x07];
                string source = Registers[byte1 & 0x07];
                return Make(address, 1, byte1, byte2, byte3, "MOV", destination + ", " + source);
            }

            if (byte1 <= 0xBF)
            {
                string operation = Alu[(byte1 >> 3) & 0x07];
                string operand = Registers[byte1 & 0x07];
                return Make(address, 1, byte1, byte2, byte3, operation, operand);
            }

            return DecodeBlockThree(address, byte1, byte2, byte3);
        }

        private static DisassembledInstruction DecodeBlockZero(
            ushort address,
            byte opcode,
            byte byte2,
            byte byte3)
        {
            int block = opcode >> 4;
            int low = opcode & 0x0F;
            int register = block * 2 + (low >= 0x0C ? 1 : 0);

            switch (low)
            {
                case 0x00:
                    return Make(address, 1, opcode, byte2, byte3, "NOP", string.Empty);

                case 0x01:
                    return Make(address, 3, opcode, byte2, byte3, "LXI",
                        RegisterPairs[block] + ", " + Word(byte2, byte3));

                case 0x02:
                    if (block == 0) return Make(address, 1, opcode, byte2, byte3, "STAX", "B");
                    if (block == 1) return Make(address, 1, opcode, byte2, byte3, "STAX", "D");
                    if (block == 2) return Make(address, 3, opcode, byte2, byte3, "SHLD", Word(byte2, byte3));
                    return Make(address, 3, opcode, byte2, byte3, "STA", Word(byte2, byte3));

                case 0x03:
                    return Make(address, 1, opcode, byte2, byte3, "INX", RegisterPairs[block]);

                case 0x04:
                    return Make(address, 1, opcode, byte2, byte3, "INR", Registers[register]);

                case 0x05:
                    return Make(address, 1, opcode, byte2, byte3, "DCR", Registers[register]);

                case 0x06:
                    return Make(address, 2, opcode, byte2, byte3, "MVI",
                        Registers[register] + ", " + Byte(byte2));

                case 0x07:
                    switch (block)
                    {
                        case 0: return Make(address, 1, opcode, byte2, byte3, "RLC", string.Empty);
                        case 1: return Make(address, 1, opcode, byte2, byte3, "RAL", string.Empty);
                        case 2: return Make(address, 1, opcode, byte2, byte3, "DAA", string.Empty);
                        default: return Make(address, 1, opcode, byte2, byte3, "STC", string.Empty);
                    }

                case 0x08:
                    return Make(address, 1, opcode, byte2, byte3, "NOP", string.Empty);

                case 0x09:
                    return Make(address, 1, opcode, byte2, byte3, "DAD", RegisterPairs[block]);

                case 0x0A:
                    if (block == 0) return Make(address, 1, opcode, byte2, byte3, "LDAX", "B");
                    if (block == 1) return Make(address, 1, opcode, byte2, byte3, "LDAX", "D");
                    if (block == 2) return Make(address, 3, opcode, byte2, byte3, "LHLD", Word(byte2, byte3));
                    return Make(address, 3, opcode, byte2, byte3, "LDA", Word(byte2, byte3));

                case 0x0B:
                    return Make(address, 1, opcode, byte2, byte3, "DCX", RegisterPairs[block]);

                case 0x0C:
                    return Make(address, 1, opcode, byte2, byte3, "INR", Registers[register]);

                case 0x0D:
                    return Make(address, 1, opcode, byte2, byte3, "DCR", Registers[register]);

                case 0x0E:
                    return Make(address, 2, opcode, byte2, byte3, "MVI",
                        Registers[register] + ", " + Byte(byte2));

                case 0x0F:
                    switch (block)
                    {
                        case 0: return Make(address, 1, opcode, byte2, byte3, "RRC", string.Empty);
                        case 1: return Make(address, 1, opcode, byte2, byte3, "RAR", string.Empty);
                        case 2: return Make(address, 1, opcode, byte2, byte3, "CMA", string.Empty);
                        default: return Make(address, 1, opcode, byte2, byte3, "CMC", string.Empty);
                    }
            }

            return Data(address, opcode, byte2, byte3);
        }

        private static DisassembledInstruction DecodeBlockThree(
            ushort address,
            byte opcode,
            byte byte2,
            byte byte3)
        {
            int condition = (opcode >> 3) & 0x07;
            int low = opcode & 0x0F;

            switch (low)
            {
                case 0x00:
                case 0x08:
                    return Make(address, 1, opcode, byte2, byte3, "R" + Conditions[condition], string.Empty);

                case 0x01:
                    return Make(address, 1, opcode, byte2, byte3, "POP", PushPairs[(opcode >> 4) & 0x03]);

                case 0x05:
                    return Make(address, 1, opcode, byte2, byte3, "PUSH", PushPairs[(opcode >> 4) & 0x03]);

                case 0x02:
                case 0x0A:
                    return Make(address, 3, opcode, byte2, byte3, "J" + Conditions[condition],
                        Word(byte2, byte3));

                case 0x04:
                case 0x0C:
                    return Make(address, 3, opcode, byte2, byte3, "C" + Conditions[condition],
                        Word(byte2, byte3));

                case 0x06:
                case 0x0E:
                    return Make(address, 2, opcode, byte2, byte3, AluImmediate[condition], Byte(byte2));

                case 0x07:
                case 0x0F:
                    return Make(address, 1, opcode, byte2, byte3, "RST",
                        condition.ToString()); // RST 0 ... RST 7

                case 0x03:
                    switch (opcode)
                    {
                        case 0xC3: return Make(address, 3, opcode, byte2, byte3, "JMP", Word(byte2, byte3));
                        case 0xD3: return Make(address, 2, opcode, byte2, byte3, "OUT", Byte(byte2));
                        case 0xE3: return Make(address, 1, opcode, byte2, byte3, "XTHL", string.Empty);
                        case 0xF3: return Make(address, 1, opcode, byte2, byte3, "DI", string.Empty);
                    }
                    break;

                case 0x09:
                    switch (opcode)
                    {
                        case 0xC9:
                        case 0xD9:
                            return Make(address, 1, opcode, byte2, byte3, "RET", string.Empty);
                        case 0xE9: return Make(address, 1, opcode, byte2, byte3, "PCHL", string.Empty);
                        case 0xF9: return Make(address, 1, opcode, byte2, byte3, "SPHL", string.Empty);
                    }
                    break;

                case 0x0B:
                    switch (opcode)
                    {
                        case 0xCB: return Make(address, 3, opcode, byte2, byte3, "JMP", Word(byte2, byte3));
                        case 0xDB: return Make(address, 2, opcode, byte2, byte3, "IN", Byte(byte2));
                        case 0xEB: return Make(address, 1, opcode, byte2, byte3, "XCHG", string.Empty);
                        case 0xFB: return Make(address, 1, opcode, byte2, byte3, "EI", string.Empty);
                    }
                    break;

                case 0x0D:
                    // CDh is CALL; DDh/EDh/FDh are the accepted duplicate CALL opcodes.
                    return Make(address, 3, opcode, byte2, byte3, "CALL", Word(byte2, byte3));
            }

            return Data(address, opcode, byte2, byte3);
        }

        private static DisassembledInstruction Make(
            ushort address,
            int length,
            byte byte1,
            byte byte2,
            byte byte3,
            string mnemonic,
            string operands)
        {
            return new DisassembledInstruction(address, byte1, byte2, byte3, length, mnemonic, operands);
        }

        private static DisassembledInstruction Data(
            ushort address,
            byte opcode,
            byte byte2,
            byte byte3)
        {
            return Make(address, 1, opcode, byte2, byte3, "DB", Byte(opcode));
        }

        private static string Byte(byte value)
        {
            return value.ToString("X2") + "h";
        }

        private static string Word(byte low, byte high)
        {
            return ((high << 8) | low).ToString("X4") + "h";
        }
    }
}
