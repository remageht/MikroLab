using System;
using MikroLab.Core.Memory;

namespace MikroLab.Core.Cpu.X64
{
    /// <summary>
    /// x86-64 processor core (long mode). The decoder understands the REX
    /// prefix, 64-bit operands, RIP-relative addressing and the SIB byte.
    /// </summary>
    public sealed partial class X64Cpu
    {
        private readonly X64Memory _memory;
        private readonly I8086.I8086Console _console;

        private bool _halted;
        private bool _repeat;
        private bool _repeatNotEqual;
        private bool _lock;
        private int _segmentOverride = -1;

        private bool _rexPresent;
        private bool _rexW;
        private bool _rexR;
        private bool _rexX;
        private bool _rexB;
        private bool _operandSize16;
        private bool _addressSize32;

        private ulong _instructionStart;

        public X64CpuState State { get; } = new X64CpuState();
        public I8086.I8086Console Console => _console;
        public bool Halted => _halted;
        public int ExitCode { get; private set; }
        public string Fault { get; private set; }

        /// <summary>True when the last instruction carried a LOCK prefix.</summary>
        public bool LockPrefix => _lock;

        /// <summary>Segment register override of the last instruction, -1 when none.</summary>
        public int SegmentOverride => _segmentOverride;

        public event Action<int> Interrupt;

        public X64Cpu(X64Memory memory, I8086.I8086Console console)
        {
            _memory = memory ?? throw new ArgumentNullException(nameof(memory));
            _console = console ?? throw new ArgumentNullException(nameof(console));
            Reset();
        }

        public void Reset()
        {
            State.Reset();
            _halted = false;
            _repeat = false;
            _repeatNotEqual = false;
            _lock = false;
            _segmentOverride = -1;
            ExitCode = 0;
            Fault = null;
        }

        /// <summary>Loads a program image and points the instruction pointer at it.</summary>
        public void LoadProgram(byte[] image, ulong address)
        {
            _memory.WriteBlock(address, image);
            State.RIP = address;
        }

        public bool Step()
        {
            if (_halted)
                return false;

            try
            {
                ExecuteInstruction();
            }
            catch (X64Fault fault)
            {
                Fault = fault.Message;
                _halted = true;
            }

            return !_halted;
        }

        // ------------------------------------------------------------------
        // prefixes and fetch
        // ------------------------------------------------------------------

        private byte FetchByte()
        {
            byte value = _memory.ReadByte(State.RIP);
            State.RIP++;
            return value;
        }

        private ulong FetchUInt64()
        {
            ulong value = 0;
            for (int i = 0; i < 8; i++)
                value |= (ulong)FetchByte() << (8 * i);
            return value;
        }

        private uint FetchUInt32()
        {
            uint value = 0;
            for (int i = 0; i < 4; i++)
                value |= (uint)FetchByte() << (8 * i);
            return value;
        }

        private ushort FetchUInt16()
        {
            ushort low = FetchByte();
            ushort high = FetchByte();
            return (ushort)(low | (high << 8));
        }

        private int FetchInt32()
        {
            return (int)FetchUInt32();
        }

        private sbyte FetchInt8()
        {
            return (sbyte)FetchByte();
        }

        private void ReadPrefixes(ref byte opcode)
        {
            bool more = true;
            while (more)
            {
                switch (opcode)
                {
                    case 0x66:
                        _operandSize16 = true;
                        opcode = FetchByte();
                        break;
                    case 0x67:
                        _addressSize32 = true;
                        opcode = FetchByte();
                        break;
                    case 0x2E: _segmentOverride = 1; opcode = FetchByte(); break;   // CS
                    case 0x36: _segmentOverride = 2; opcode = FetchByte(); break;   // SS
                    case 0x3E: _segmentOverride = 3; opcode = FetchByte(); break;   // DS
                    case 0x26: _segmentOverride = 0; opcode = FetchByte(); break;   // ES
                    case 0x64: _segmentOverride = 4; opcode = FetchByte(); break;   // FS
                    case 0x65: _segmentOverride = 5; opcode = FetchByte(); break;   // GS
                    case 0xF0: _lock = true; opcode = FetchByte(); break;
                    case 0xF2: _repeatNotEqual = true; _repeat = false; opcode = FetchByte(); break;
                    case 0xF3: _repeat = true; _repeatNotEqual = false; opcode = FetchByte(); break;
                    case 0xF1: opcode = FetchByte(); break;                        // INT1
                    default:
                        if (opcode >= 0x40 && opcode <= 0x4F)
                        {
                            _rexPresent = true;
                            _rexW = (opcode & 0x08) != 0;
                            _rexR = (opcode & 0x04) != 0;
                            _rexX = (opcode & 0x02) != 0;
                            _rexB = (opcode & 0x01) != 0;
                            opcode = FetchByte();
                            break;
                        }

                        more = false;
                        break;
                }
            }
        }

        // ------------------------------------------------------------------
        // ModR/M and SIB
        // ------------------------------------------------------------------

        private struct Modrm
        {
            public byte Mod;
            public int Reg;          // index with REX.R applied
            public int Rm;           // index with REX.B applied
            public bool IsRegister;
            public ulong Address;
            public bool RipRelative;
        }

        private Modrm DecodeModrm()
        {
            byte raw = FetchByte();
            Modrm info = new Modrm
            {
                Mod = (byte)((raw >> 6) & 3),
                Reg = (raw >> 3) & 7,
                Rm = raw & 7
            };

            if (_rexR)
                info.Reg += 8;

            if (info.Mod == 3)
            {
                info.IsRegister = true;
                info.Rm += _rexB ? 8 : 0;
                return info;
            }

            ulong address = 0;
            bool ripRelative = false;

            if (info.Rm == 4)
            {
                byte sib = FetchByte();
                int scale = sib >> 6;
                int index = (sib >> 3) & 7;
                int baseIndex = sib & 7;

                if (index == 4 && !_rexX)
                    index = -1;
                if (baseIndex == 5 && info.Mod == 0)
                    baseIndex = -1;

                if (index >= 0)
                    address += (ulong)(index * (1 << scale)) & 0xFFFFFFFFUL;
                if (baseIndex >= 0)
                    address += State.GetGpr(baseIndex) & 0xFFFFFFFFUL;

                if (info.Mod == 1)
                    address += (ulong)(long)(sbyte)FetchByte();
                else if (info.Mod == 2)
                    address += (ulong)(long)FetchInt32();
                else if (baseIndex < 0)
                    address += (ulong)(long)FetchInt32();
            }
            else if (info.Rm == 5 && info.Mod == 0)
            {
                int displacement = FetchInt32();
                address = State.RIP + (ulong)(long)displacement;
                ripRelative = true;
            }
            else
            {
                address = State.GetGpr(info.Rm) & 0xFFFFFFFFUL;
                if (info.Mod == 1)
                    address += (ulong)(long)(sbyte)FetchByte();
                else if (info.Mod == 2)
                    address += (ulong)(long)FetchInt32();
            }

            if (_addressSize32)
                address &= 0xFFFFFFFFUL;

            info.Address = address;
            info.RipRelative = ripRelative;
            return info;
        }

        // ------------------------------------------------------------------
        // operand access
        // ------------------------------------------------------------------

        private int OperandSize()
        {
            if (_rexW)
                return 8;
            return _operandSize16 ? 2 : 4;
        }

        private static ulong Mask(int size)
        {
            return size >= 8 ? ulong.MaxValue : (1UL << (size * 8)) - 1;
        }

        private static long SignExtend(ulong value, int size)
        {
            switch (size)
            {
                case 1: return (sbyte)value;
                case 2: return (short)value;
                case 4: return (int)value;
                default: return (long)value;
            }
        }

        private ulong GetRm(in Modrm info, int size)
        {
            if (info.IsRegister)
            {
                ulong value = State.GetGpr(info.Rm);
                if (size == 1)
                    return State.GetByteRegister(info.Rm, _rexPresent);
                return value & Mask(size);
            }

            switch (size)
            {
                case 1: return _memory.ReadByte(info.Address);
                case 2: return _memory.ReadWord(info.Address);
                case 4: return _memory.ReadDword(info.Address);
                default: return _memory.ReadQword(info.Address);
            }
        }

        private void SetRm(in Modrm info, int size, ulong value)
        {
            if (info.IsRegister)
            {
                if (size == 1)
                {
                    State.SetByteRegister(info.Rm, _rexPresent, (byte)value);
                    return;
                }

                ulong current = State.GetGpr(info.Rm);
                State.SetGpr(info.Rm, size >= 8 ? value : (current & ~Mask(size)) | (value & Mask(size)));
                return;
            }

            switch (size)
            {
                case 1: _memory.WriteByte(info.Address, (byte)value); break;
                case 2: _memory.WriteWord(info.Address, (ushort)value); break;
                case 4: _memory.WriteDword(info.Address, (uint)value); break;
                default: _memory.WriteQword(info.Address, value); break;
            }
        }

        private ulong GetReg(int index, int size)
        {
            if (size == 1)
                return State.GetByteRegister(index, _rexPresent);
            return State.GetGpr(index) & Mask(size);
        }

        private void SetReg(int index, int size, ulong value)
        {
            if (size == 1)
            {
                State.SetByteRegister(index, _rexPresent, (byte)value);
                return;
            }

            ulong current = State.GetGpr(index);
            State.SetGpr(index, size >= 8 ? value : (current & ~Mask(size)) | (value & Mask(size)));
        }

        // ------------------------------------------------------------------
        // flags
        // ------------------------------------------------------------------

        internal static bool ParityEven(byte value)
        {
            int bits = 0;
            for (int i = 0; i < 8; i++)
                bits += (value >> i) & 1;
            return (bits & 1) == 0;
        }

        private void SetSizeFlags(ulong result, int size)
        {
            State.Zero = result == 0;
            State.Sign = (result & (1UL << (size * 8 - 1))) != 0;
            State.Parity = ParityEven((byte)result);
        }

        private ulong DoAdd(ulong left, ulong right, int size, bool withCarry)
        {
            ulong carry = withCarry && State.Carry ? 1UL : 0UL;
            ulong mask = Mask(size);
            ulong sum = left + right + carry;

            State.Carry = size < 8 ? sum > mask : (sum < left || (carry != 0 && sum == left));
            State.Auxiliary = ((left & 0x0F) + (right & 0x0F) + carry) > 0x0F;

            // промежуточное сложение считаем в 128 битах, иначе переполнение
            // самого long скрыло бы переполнение операнда
            Int128 signed = (Int128)SignExtend(left, size) + (Int128)SignExtend(right, size) + (Int128)carry;
            long min = -(1L << (size * 8 - 1));
            long max = (1L << (size * 8 - 1)) - 1;
            State.Overflow = signed > max || signed < min;

            ulong result = sum & mask;
            SetSizeFlags(result, size);
            return result;
        }

        private ulong DoSub(ulong left, ulong right, int size, bool withBorrow)
        {
            ulong borrow = withBorrow && State.Carry ? 1UL : 0UL;
            ulong mask = Mask(size);
            ulong difference = left - right - borrow;

            State.Carry = size < 8
                ? left < right + borrow
                : (left < right || (borrow != 0 && left == right));
            State.Auxiliary = ((long)(left & 0x0F) - (long)(right & 0x0F) - (long)borrow) < 0;

            Int128 signed = (Int128)SignExtend(left, size) - (Int128)SignExtend(right, size) - (Int128)borrow;
            long min = -(1L << (size * 8 - 1));
            long max = (1L << (size * 8 - 1)) - 1;
            State.Overflow = signed > max || signed < min;

            ulong result = difference & mask;
            SetSizeFlags(result, size);
            return result;
        }

        private ulong DoLogic(ulong left, ulong right, int size, int operation)
        {
            ulong result;
            switch (operation)
            {
                case 0: result = left & right; break;      // AND
                case 1: result = left | right; break;      // OR
                default: result = left ^ right; break;     // XOR
            }

            result &= Mask(size);
            SetSizeFlags(result, size);
            State.Carry = false;
            State.Overflow = false;
            State.Auxiliary = false;
            return result;
        }

        private void DoTest(ulong left, ulong right, int size)
        {
            SetSizeFlags(left & right & Mask(size), size);
            State.Carry = false;
            State.Overflow = false;
        }

        private ulong DoIncrement(ulong value, int size)
        {
            ulong mask = Mask(size);
            ulong result = (value + 1) & mask;
            SetSizeFlags(result, size);
            State.Auxiliary = (value & 0x0F) == 0x0F;
            State.Overflow = result == (1UL << (size * 8 - 1));
            return result;
        }

        private ulong DoDecrement(ulong value, int size)
        {
            ulong mask = Mask(size);
            ulong result = (value - 1) & mask;
            SetSizeFlags(result, size);
            State.Auxiliary = (value & 0x0F) == 0x00;
            State.Overflow = result == (1UL << (size * 8 - 2) - 1);
            return result;
        }

        /// <summary>Sign extends a value to the full 64-bit register.</summary>
        private static ulong ExtendTo64(ulong value, int size)
        {
            switch (size)
            {
                case 1: return (ulong)(long)(sbyte)value;
                case 2: return (ulong)(long)(short)value;
                case 4: return (ulong)(long)(int)value;
                default: return value;
            }
        }
    }

    /// <summary>Internal processor fault.</summary>
    public sealed class X64Fault : Exception
    {
        public X64Fault(string message) : base(message)
        {
        }
    }
}
