using System;
using System.Text;

namespace MikroLab.Core.Cpu.X64
{
    /// <summary>
    /// Programmer visible state of an x86-64 processor in long mode:
    /// sixteen 64-bit general purpose registers, the instruction pointer,
    /// the flags word, the segment registers and the control registers.
    /// </summary>
    public sealed class X64CpuState
    {
        public ulong RAX { get; set; }
        public ulong RBX { get; set; }
        public ulong RCX { get; set; }
        public ulong RDX { get; set; }
        public ulong RSI { get; set; }
        public ulong RDI { get; set; }
        public ulong RBP { get; set; }
        public ulong RSP { get; set; }
        public ulong R8 { get; set; }
        public ulong R9 { get; set; }
        public ulong R10 { get; set; }
        public ulong R11 { get; set; }
        public ulong R12 { get; set; }
        public ulong R13 { get; set; }
        public ulong R14 { get; set; }
        public ulong R15 { get; set; }

        public ulong RIP { get; set; }
        public ushort Flags { get; set; }

        public ushort CS { get; set; }
        public ushort DS { get; set; }
        public ushort ES { get; set; }
        public ushort FS { get; set; }
        public ushort GS { get; set; }
        public ushort SS { get; set; }

        public ulong FSBase { get; set; }
        public ulong GSBase { get; set; }

        public ulong CR0 { get; set; }
        public ulong CR2 { get; set; }
        public ulong CR3 { get; set; }
        public ulong CR4 { get; set; }

        public ulong Cycles { get; set; }

        /// <summary>Low 32 bits of RAX (EAX).</summary>
        public uint EAX32
        {
            get => (uint)RAX;
            set => RAX = (RAX & 0xFFFFFFFF00000000UL) | value;
        }

        /// <summary>Second byte of RAX (AH).</summary>
        public byte RAH
        {
            get => (byte)(RAX >> 8);
            set => RAX = (RAX & 0xFFFF00FFUL) | ((ulong)value << 8);
        }

        public byte AL { get => (byte)RAX; set => RAX = (RAX & 0xFFFFFFFFFFFFFF00UL) | value; }
        public byte AH { get => (byte)(RAX >> 8); set => RAX = (RAX & 0xFFFF00FFUL) | ((ulong)value << 8); }
        public ushort AX { get => (ushort)RAX; set => RAX = (RAX & 0xFFFFFFFFFFFF0000UL) | value; }
        public uint EAX { get => (uint)RAX; set => RAX = (RAX & 0xFFFFFFFF00000000UL) | value; }

        public byte BL { get => (byte)RBX; set => RBX = (RBX & 0xFFFFFFFFFFFFFF00UL) | value; }
        public byte BH { get => (byte)(RBX >> 8); set => RBX = (RBX & 0xFFFF00FFUL) | ((ulong)value << 8); }
        public ushort BX { get => (ushort)RBX; set => RBX = (RBX & 0xFFFFFFFFFFFF0000UL) | value; }
        public uint EBX { get => (uint)RBX; set => RBX = (RBX & 0xFFFFFFFF00000000UL) | value; }

        public byte CL { get => (byte)RCX; set => RCX = (RCX & 0xFFFFFFFFFFFFFF00UL) | value; }
        public byte CH { get => (byte)(RCX >> 8); set => RCX = (RCX & 0xFFFF00FFUL) | ((ulong)value << 8); }
        public ushort CX { get => (ushort)RCX; set => RCX = (RCX & 0xFFFFFFFFFFFF0000UL) | value; }
        public uint ECX { get => (uint)RCX; set => RCX = (RCX & 0xFFFFFFFF00000000UL) | value; }

        /// <summary>Low byte of RDX (DL).</summary>
        public byte DL
        {
            get => (byte)RDX;
            set => RDX = (RDX & 0xFFFFFFFFFFFFFF00UL) | value;
        }

        public byte DH { get => (byte)(RDX >> 8); set => RDX = (RDX & 0xFFFF00FFUL) | ((ulong)value << 8); }
        public ushort DX { get => (ushort)RDX; set => RDX = (RDX & 0xFFFFFFFFFFFF0000UL) | value; }
        public uint EDX { get => (uint)RDX; set => RDX = (RDX & 0xFFFFFFFF00000000UL) | value; }

        public byte SIL { get => (byte)RSI; set => RSI = (RSI & 0xFFFFFFFFFFFFFF00UL) | value; }
        public byte DIL { get => (byte)RDI; set => RDI = (RDI & 0xFFFFFFFFFFFFFF00UL) | value; }
        public byte SPL { get => (byte)RSP; set => RSP = (RSP & 0xFFFFFFFFFFFFFF00UL) | value; }
        public byte BPL { get => (byte)RBP; set => RBP = (RBP & 0xFFFFFFFFFFFFFF00UL) | value; }
        public byte R8B { get => (byte)R8; set => R8 = (R8 & 0xFFFFFFFFFFFFFF00UL) | value; }
        public byte R9B { get => (byte)R9; set => R9 = (R9 & 0xFFFFFFFFFFFFFF00UL) | value; }

        /// <summary>Reads a general purpose register by index (0 = RAX ... 15 = R15).</summary>
        public ulong GetGpr(int index)
        {
            switch (index)
            {
                case 0: return RAX;
                case 1: return RCX;
                case 2: return RDX;
                case 3: return RBX;
                case 4: return RSP;
                case 5: return RBP;
                case 6: return RSI;
                case 7: return RDI;
                case 8: return R8;
                case 9: return R9;
                case 10: return R10;
                case 11: return R11;
                case 12: return R12;
                case 13: return R13;
                case 14: return R14;
                default: return R15;
            }
        }

        public void SetGpr(int index, ulong value)
        {
            switch (index)
            {
                case 0: RAX = value; break;
                case 1: RCX = value; break;
                case 2: RDX = value; break;
                case 3: RBX = value; break;
                case 4: RSP = value; break;
                case 5: RBP = value; break;
                case 6: RSI = value; break;
                case 7: RDI = value; break;
                case 8: R8 = value; break;
                case 9: R9 = value; break;
                case 10: R10 = value; break;
                case 11: R11 = value; break;
                case 12: R12 = value; break;
                case 13: R13 = value; break;
                case 14: R14 = value; break;
                default: R15 = value; break;
            }
        }

        /// <summary>
        /// Reads an 8-bit register. Without REX the indices 4..7 mean AH, CH,
        /// DH, BH; with REX they mean SPL, BPL, SIL, DIL.
        /// </summary>
        public byte GetByteRegister(int index, bool rexPresent)
        {
            if (!rexPresent && index >= 4)
            {
                switch (index)
                {
                    case 4: return (byte)(RAX >> 8);
                    case 5: return (byte)(RCX >> 8);
                    case 6: return (byte)(RDX >> 8);
                    default: return (byte)(RBX >> 8);
                }
            }

            return (byte)GetGpr(index & 7);
        }

        public void SetByteRegister(int index, bool rexPresent, byte value)
        {
            if (!rexPresent && index >= 4)
            {
                switch (index)
                {
                    case 4: RAX = (RAX & 0xFFFF00FFUL) | ((ulong)value << 8); return;
                    case 5: RCX = (RCX & 0xFFFF00FFUL) | ((ulong)value << 8); return;
                    case 6: RDX = (RDX & 0xFFFF00FFUL) | ((ulong)value << 8); return;
                    default: RBX = (RBX & 0xFFFF00FFUL) | ((ulong)value << 8); return;
                }
            }

            int gpr = index & 7;
            ulong current = GetGpr(gpr);
            SetGpr(gpr, (current & 0xFFFFFFFFFFFFFF00UL) | value);
        }

        public bool HasFlag(X64Flag flag) => (Flags & (ushort)flag) != 0;

        public void SetFlag(X64Flag flag, bool value)
        {
            if (value)
                Flags |= (ushort)flag;
            else
                Flags &= (ushort)~(ushort)flag;
        }

        public bool Carry { get => HasFlag(X64Flag.Carry); set => SetFlag(X64Flag.Carry, value); }
        public bool Parity { get => HasFlag(X64Flag.Parity); set => SetFlag(X64Flag.Parity, value); }
        public bool Auxiliary { get => HasFlag(X64Flag.Auxiliary); set => SetFlag(X64Flag.Auxiliary, value); }
        public bool Zero { get => HasFlag(X64Flag.Zero); set => SetFlag(X64Flag.Zero, value); }
        public bool Sign { get => HasFlag(X64Flag.Sign); set => SetFlag(X64Flag.Sign, value); }
        public bool Trap { get => HasFlag(X64Flag.Trap); set => SetFlag(X64Flag.Trap, value); }
        public bool Interrupt { get => HasFlag(X64Flag.Interrupt); set => SetFlag(X64Flag.Interrupt, value); }
        public bool Direction { get => HasFlag(X64Flag.Direction); set => SetFlag(X64Flag.Direction, value); }
        public bool Overflow { get => HasFlag(X64Flag.Overflow); set => SetFlag(X64Flag.Overflow, value); }

        public void Reset()
        {
            RAX = RBX = RCX = RDX = 0;
            RSI = RDI = RBP = 0;
            RSP = 0x0000_7FFF_FFFF_FFC0UL;
            R8 = R9 = R10 = R11 = 0;
            R12 = R13 = R14 = R15 = 0;
            RIP = 0x0000_0000_0010_0000UL;
            Flags = 0x0002;
            CS = DS = ES = FS = GS = SS = 0;
            FSBase = GSBase = 0;
            CR0 = 0x8000_0000_0000_0000UL | 0x0000_0000_0000_0001UL;   // PE + ET
            CR2 = 0;
            CR3 = 0;
            CR4 = 0;
            Cycles = 0;
        }

        /// <summary>Readable register view for the debugger.</summary>
        public string Describe()
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("RAX=").Append(RAX.ToString("X16"))
                .Append(" RBX=").Append(RBX.ToString("X16"))
                .Append(" RCX=").Append(RCX.ToString("X16"))
                .Append(" RDX=").Append(RDX.ToString("X16"))
                .Append(" RSI=").Append(RSI.ToString("X16"))
                .Append(" RDI=").Append(RDI.ToString("X16"))
                .Append(" RBP=").Append(RBP.ToString("X16"))
                .Append(" RSP=").Append(RSP.ToString("X16"))
                .Append(" R8=").Append(R8.ToString("X16"))
                .Append(" R9=").Append(R9.ToString("X16"))
                .Append(" R10=").Append(R10.ToString("X16"))
                .Append(" R11=").Append(R11.ToString("X16"))
                .Append(" R12=").Append(R12.ToString("X16"))
                .Append(" R13=").Append(R13.ToString("X16"))
                .Append(" R14=").Append(R14.ToString("X16"))
                .Append(" R15=").Append(R15.ToString("X16"))
                .Append(" RIP=").Append(RIP.ToString("X16"))
                .Append(" RFLAGS=").Append(Flags.ToString("X4"));
            return builder.ToString();
        }
    }
}
