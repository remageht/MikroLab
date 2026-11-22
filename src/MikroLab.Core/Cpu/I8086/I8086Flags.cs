using System;

namespace MikroLab.Core.Cpu.I8086
{
    /// <summary>
    /// Flags register of the Intel 8086. Bit positions match the real FLAGS
    /// register so the debugger can show the raw word without translation.
    /// </summary>
    [Flags]
    public enum I8086Flag : ushort
    {
        None = 0,
        Carry = 1 << 0,          // CF
        Parity = 1 << 2,         // PF
        Auxiliary = 1 << 4,      // AF
        Zero = 1 << 6,           // ZF
        Sign = 1 << 7,           // SF
        Trap = 1 << 8,           // TF
        Interrupt = 1 << 9,      // IF
        Direction = 1 << 10,     // DF
        Overflow = 1 << 11       // OF
    }
}
