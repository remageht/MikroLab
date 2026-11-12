using System;

namespace MikroLab.Core.Cpu.I8086
{
    /// <summary>
    /// Programmer visible state of the Intel 8086: eight 16-bit general
    /// registers, four segment registers, the instruction pointer and the
    /// flags word.
    /// </summary>
    public sealed class I8086CpuState
    {
        public ushort AX { get; set; }
        public ushort BX { get; set; }
        public ushort CX { get; set; }
        public ushort DX { get; set; }
        public ushort SI { get; set; }
        public ushort DI { get; set; }
        public ushort BP { get; set; }
        public ushort SP { get; set; }

        public ushort CS { get; set; }
        public ushort DS { get; set; }
        public ushort ES { get; set; }
        public ushort SS { get; set; }

        public ushort IP { get; set; }
        public ushort Flags { get; set; }

        public ulong Cycles { get; set; }

        public byte AL { get => (byte)(AX & 0xFF); set => AX = (ushort)((AX & 0xFF00) | value); }
        public byte AH { get => (byte)(AX >> 8); set => AX = (ushort)((AX & 0x00FF) | (value << 8)); }
        public byte BL { get => (byte)(BX & 0xFF); set => BX = (ushort)((BX & 0xFF00) | value); }
        public byte BH { get => (byte)(BX >> 8); set => BX = (ushort)((BX & 0x00FF) | (value << 8)); }
        public byte CL { get => (byte)(CX & 0xFF); set => CX = (ushort)((CX & 0xFF00) | value); }
        public byte CH { get => (byte)(CX >> 8); set => CX = (ushort)((CX & 0x00FF) | (value << 8)); }
        public byte DL { get => (byte)(DX & 0xFF); set => DX = (ushort)((DX & 0xFF00) | value); }
        public byte DH { get => (byte)(DX >> 8); set => DX = (ushort)((DX & 0x00FF) | (value << 8)); }

        public bool HasFlag(I8086Flag flag) => (Flags & (ushort)flag) != 0;

        public void SetFlag(I8086Flag flag, bool value)
        {
            if (value)
                Flags |= (ushort)flag;
            else
                Flags &= (ushort)~(ushort)flag;
        }

        public bool Carry { get => HasFlag(I8086Flag.Carry); set => SetFlag(I8086Flag.Carry, value); }
        public bool Parity { get => HasFlag(I8086Flag.Parity); set => SetFlag(I8086Flag.Parity, value); }
        public bool Auxiliary { get => HasFlag(I8086Flag.Auxiliary); set => SetFlag(I8086Flag.Auxiliary, value); }
        public bool Zero { get => HasFlag(I8086Flag.Zero); set => SetFlag(I8086Flag.Zero, value); }
        public bool Sign { get => HasFlag(I8086Flag.Sign); set => SetFlag(I8086Flag.Sign, value); }
        public bool Trap { get => HasFlag(I8086Flag.Trap); set => SetFlag(I8086Flag.Trap, value); }
        public bool Interrupt { get => HasFlag(I8086Flag.Interrupt); set => SetFlag(I8086Flag.Interrupt, value); }
        public bool Direction { get => HasFlag(I8086Flag.Direction); set => SetFlag(I8086Flag.Direction, value); }
        public bool Overflow { get => HasFlag(I8086Flag.Overflow); set => SetFlag(I8086Flag.Overflow, value); }

        public void Reset()
        {
            AX = BX = CX = DX = 0;
            SI = DI = BP = SP = 0;
            CS = DS = ES = SS = 0;
            IP = 0;
            Flags = 0xF002;
            Cycles = 0;
        }

        /// <summary>Readable register view used by the debugger panels.</summary>
        public string Describe()
        {
            return string.Format(
                "AX={0:X4} BX={1:X4} CX={2:X4} DX={3:X4} SI={4:X4} DI={5:X4} BP={6:X4} SP={7:X4} "
                + "CS={8:X4} DS={9:X4} ES={10:X4} SS={11:X4} IP={12:X4} FLAGS={13:X4}",
                AX, BX, CX, DX, SI, DI, BP, SP, CS, DS, ES, SS, IP, Flags);
        }
    }
}
