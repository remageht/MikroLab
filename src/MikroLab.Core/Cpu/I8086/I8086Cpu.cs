using System;
using MikroLab.Core.Memory;

namespace MikroLab.Core.Cpu.I8086
{
    /// <summary>
    /// Intel 8086 compatible processor core (16-bit real mode).
    /// Implements the instruction set of the original 8086 including the
    /// overflow flag, string operations and the DOS services used by the
    /// laboratory programs.
    /// </summary>
    public sealed partial class I8086Cpu
    {
        private readonly I8086Memory _memory;
        private readonly I8086Console _console;

        private bool _halted;
        private bool _repeat;
        private bool _repeatNotEqual;
        private bool _lock;
        private int _segmentOverride = -1;

        public I8086CpuState State { get; } = new I8086CpuState();
        public I8086Console Console => _console;
        public bool Halted => _halted;
        public int ExitCode { get; private set; }

        /// <summary>True when the last instruction carried a LOCK prefix.</summary>
        public bool LockPrefix => _lock;

        /// <summary>Raised when the processor meets an opcode it cannot execute.</summary>
        public event Action<ushort, byte> UnsupportedCommand;

        /// <summary>Raised for interrupts that are not handled inside the core.</summary>
        public event Action<int> Interrupt;

        public I8086Cpu(I8086Memory memory, I8086Console console)
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

            InstallInterruptVectorTable();
        }

        /// <summary>
        /// Builds a minimal interrupt vector table: every vector points at a
        /// single IRET stub, so an unhandled interrupt returns immediately
        /// instead of jumping into random memory.
        /// </summary>
        private void InstallInterruptVectorTable()
        {
            const ushort stubOffset = 0x0080;
            _memory.WriteByte(stubOffset, 0xCF);            // IRET

            for (int vector = 0; vector < 256; vector++)
            {
                _memory.WriteWord(vector * 4, stubOffset);
                _memory.WriteWord(vector * 4 + 2, 0x0000);
            }
        }

        private void IncrementRegister16(byte reg, bool increment)
        {
            ushort value = GetReg16(reg);
            ushort result = increment ? (ushort)(value + 1) : (ushort)(value - 1);
            if (increment)
                SetIncFlags(result, value);
            else
                SetDecFlags(result, value);
            SetReg16(reg, result);
        }

        /// <summary>Loads a program image at a linear address and points CS:IP at it.</summary>
        public void LoadProgram(byte[] image, ushort segment, ushort offset)
        {
            _memory.WriteBlock((segment << 4) + offset, image);
            State.CS = segment;
            State.IP = offset;
        }

        public bool Step()
        {
            if (_halted)
                return false;

            try
            {
                ExecuteInstruction();
            }
            catch (DivideByZeroException)
            {
                State.Zero = true;
                _halted = true;
            }

            return !_halted;
        }

        // ------------------------------------------------------------------
        // fetch helpers
        // ------------------------------------------------------------------

        private byte FetchByte()
        {
            byte value = _memory.ReadByte(Linear(State.CS, State.IP));
            State.IP = (ushort)(State.IP + 1);
            return value;
        }

        private ushort FetchWord()
        {
            byte low = FetchByte();
            byte high = FetchByte();
            return (ushort)(low | (high << 8));
        }

        private static int Linear(ushort segment, ushort offset)
        {
            return (((segment << 4) + offset) & I8086Memory.AddressMask);
        }

        private byte ReadByte(ushort segment, ushort offset)
        {
            return _memory.ReadByte(Linear(segment, offset));
        }

        private void WriteByte(ushort segment, ushort offset, byte value)
        {
            _memory.WriteByte(Linear(segment, offset), value);
        }

        private ushort ReadWord(ushort segment, ushort offset)
        {
            return _memory.ReadWord(Linear(segment, offset));
        }

        private void WriteWord(ushort segment, ushort offset, ushort value)
        {
            _memory.WriteWord(Linear(segment, offset), value);
        }

        // ------------------------------------------------------------------
        // register access
        // ------------------------------------------------------------------

        private ushort GetReg16(int index)
        {
            switch (index)
            {
                case 0: return State.AX;
                case 1: return State.CX;
                case 2: return State.DX;
                case 3: return State.BX;
                case 4: return State.SP;
                case 5: return State.BP;
                case 6: return State.SI;
                default: return State.DI;
            }
        }

        private void SetReg16(int index, ushort value)
        {
            switch (index)
            {
                case 0: State.AX = value; break;
                case 1: State.CX = value; break;
                case 2: State.DX = value; break;
                case 3: State.BX = value; break;
                case 4: State.SP = value; break;
                case 5: State.BP = value; break;
                case 6: State.SI = value; break;
                default: State.DI = value; break;
            }
        }

        private byte GetReg8(int index)
        {
            switch (index)
            {
                case 0: return State.AL;
                case 1: return State.CL;
                case 2: return State.DL;
                case 3: return State.BL;
                case 4: return State.AH;
                case 5: return State.CH;
                case 6: return State.DH;
                default: return State.BH;
            }
        }

        private void SetReg8(int index, byte value)
        {
            switch (index)
            {
                case 0: State.AL = value; break;
                case 1: State.CL = value; break;
                case 2: State.DL = value; break;
                case 3: State.BL = value; break;
                case 4: State.AH = value; break;
                case 5: State.CH = value; break;
                case 6: State.DH = value; break;
                default: State.BH = value; break;
            }
        }

        private ushort GetSegment(int index)
        {
            switch (index)
            {
                case 0: return State.ES;
                case 1: return State.CS;
                case 2: return State.SS;
                default: return State.DS;
            }
        }

        private void SetSegment(int index, ushort value)
        {
            switch (index)
            {
                case 0: State.ES = value; break;
                case 1: State.CS = value; break;
                case 2: State.SS = value; break;
                default: State.DS = value; break;
            }
        }

        // ------------------------------------------------------------------
        // ModR/M decoding
        // ------------------------------------------------------------------

        private struct Modrm
        {
            public byte Mod;
            public byte Reg;
            public byte Rm;
            public bool IsRegister;
            public ushort Segment;
            public ushort Offset;
        }

        private Modrm DecodeModrm()
        {
            byte raw = FetchByte();
            Modrm info = new Modrm
            {
                Mod = (byte)((raw >> 6) & 3),
                Reg = (byte)((raw >> 3) & 7),
                Rm = (byte)(raw & 7),
                IsRegister = ((raw >> 6) & 3) == 3
            };

            if (info.IsRegister)
                return info;

            bool direct = info.Mod == 0 && info.Rm == 6;
            ushort segment = _segmentOverride >= 0
                ? GetSegment(_segmentOverride)
                : (direct ? State.DS : DefaultDataSegment(info.Rm));

            int offset;
            switch (info.Rm)
            {
                case 0: offset = State.BX + State.SI; break;
                case 1: offset = State.BX + State.DI; break;
                case 2: offset = State.BP + State.SI; break;
                case 3: offset = State.BP + State.DI; break;
                case 4: offset = State.SI; break;
                case 5: offset = State.DI; break;
                case 6: offset = State.BP; break;
                default: offset = State.BX; break;
            }

            if (direct)
            {
                offset = FetchWord();
            }
            else if (info.Mod == 1)
            {
                offset += (sbyte)FetchByte();
            }
            else if (info.Mod == 2)
            {
                offset += (short)FetchWord();
            }

            info.Segment = segment;
            info.Offset = (ushort)(offset & 0xFFFF);
            return info;
        }

        private ushort DefaultDataSegment(byte rm)
        {
            // BP-based addressing uses the stack segment.
            return (rm == 2 || rm == 3 || rm == 6) ? State.SS : State.DS;
        }

        private byte GetRm8(in Modrm info)
        {
            return info.IsRegister ? GetReg8(info.Rm) : ReadByte(info.Segment, info.Offset);
        }

        private void SetRm8(in Modrm info, byte value)
        {
            if (info.IsRegister)
                SetReg8(info.Rm, value);
            else
                WriteByte(info.Segment, info.Offset, value);
        }

        private ushort GetRm16(in Modrm info)
        {
            return info.IsRegister ? GetReg16(info.Rm) : ReadWord(info.Segment, info.Offset);
        }

        private void SetRm16(in Modrm info, ushort value)
        {
            if (info.IsRegister)
                SetReg16(info.Rm, value);
            else
                WriteWord(info.Segment, info.Offset, value);
        }

        // ------------------------------------------------------------------
        // flag helpers
        // ------------------------------------------------------------------

        private static bool ParityEven(byte value)
        {
            int bits = 0;
            for (int i = 0; i < 8; i++)
                bits += (value >> i) & 1;
            return (bits & 1) == 0;
        }

        private void SetSizeFlags(byte result)
        {
            State.Zero = result == 0;
            State.Sign = (result & 0x80) != 0;
            State.Parity = ParityEven(result);
        }

        private void SetAddFlags(byte result, byte left, byte right)
        {
            SetSizeFlags(result);
            State.Carry = (left + right) > 0xFF;
            State.Auxiliary = ((left & 0x0F) + (right & 0x0F)) > 0x0F;
            int signed = (sbyte)left + (sbyte)right;
            State.Overflow = signed > 127 || signed < -128;
        }

        private void SetSubFlags(byte result, byte left, byte right)
        {
            SetSizeFlags(result);
            State.Carry = left < right;
            State.Auxiliary = (left & 0x0F) < (right & 0x0F);
            int signed = (sbyte)left - (sbyte)right;
            State.Overflow = signed > 127 || signed < -128;
        }

        private void SetLogicFlags(byte result)
        {
            SetSizeFlags(result);
            State.Carry = false;
            State.Overflow = false;
            State.Auxiliary = false;
        }

        private void SetIncFlags(byte result, byte original)
        {
            State.Zero = result == 0;
            State.Sign = (result & 0x80) != 0;
            State.Parity = ParityEven(result);
            State.Auxiliary = (original & 0x0F) == 0x0F;
            State.Overflow = result == 0x80;
        }

        private void SetDecFlags(byte result, byte original)
        {
            State.Zero = result == 0;
            State.Sign = (result & 0x80) != 0;
            State.Parity = ParityEven(result);
            State.Auxiliary = (original & 0x0F) == 0x00;
            State.Overflow = result == 0x7F;
        }

        private void SetAddFlags(ushort result, ushort left, ushort right)
        {
            State.Zero = result == 0;
            State.Sign = (result & 0x8000) != 0;
            State.Parity = ParityEven((byte)result);
            State.Carry = (left + right) > 0xFFFF;
            State.Auxiliary = ((left & 0x0F) + (right & 0x0F)) > 0x0F;
            int signed = (short)left + (short)right;
            State.Overflow = signed > 32767 || signed < -32768;
        }

        private void SetSubFlags(ushort result, ushort left, ushort right)
        {
            State.Zero = result == 0;
            State.Sign = (result & 0x8000) != 0;
            State.Parity = ParityEven((byte)result);
            State.Carry = left < right;
            State.Auxiliary = (left & 0x0F) < (right & 0x0F);
            int signed = (short)left - (short)right;
            State.Overflow = signed > 32767 || signed < -32768;
        }

        private void SetLogicFlags(ushort result)
        {
            State.Zero = result == 0;
            State.Sign = (result & 0x8000) != 0;
            State.Parity = ParityEven((byte)result);
            State.Carry = false;
            State.Overflow = false;
            State.Auxiliary = false;
        }

        private void SetIncFlags(ushort result, ushort original)
        {
            State.Zero = result == 0;
            State.Sign = (result & 0x8000) != 0;
            State.Parity = ParityEven((byte)result);
            State.Auxiliary = (original & 0x0F) == 0x0F;
            State.Overflow = result == 0x8000;
        }

        private void SetDecFlags(ushort result, ushort original)
        {
            State.Zero = result == 0;
            State.Sign = (result & 0x8000) != 0;
            State.Parity = ParityEven((byte)result);
            State.Auxiliary = (original & 0x0F) == 0x0000;
            State.Overflow = result == 0x7FFF;
        }
    }
}
