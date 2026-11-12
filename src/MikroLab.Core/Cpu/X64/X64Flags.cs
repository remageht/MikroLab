using System;

namespace MikroLab.Core.Cpu.X64
{
    /// <summary>
    /// Flags of the x86-64 processor. The bit positions are the same as in the
    /// 8086 so the debugger shows a familiar word.
    /// </summary>
    [Flags]
    public enum X64Flag : uint
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
        Overflow = 1 << 11,      // OF
        Nested = 1 << 14,        // NT
        Resume = 1 << 16         // RF
    }
}
