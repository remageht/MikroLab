using System;
using System.Collections.Generic;

namespace MikroLab.Core.Assembler.X64
{
    /// <summary>Kind of an operand in the x86-64 assembler.</summary>
    public enum X64OperandKind
    {
        None,
        Register8,
        Register16,
        Register32,
        Register64,
        Memory,
        Immediate,
        SegmentRegister
    }

    /// <summary>One parsed operand.</summary>
    public sealed class X64Operand
    {
        public X64OperandKind Kind { get; set; }
        public int Register { get; set; } = -1;
        public int Size { get; set; }
        public string Expression { get; set; }
        public long Value { get; set; }
        public bool HasValue { get; set; }

        public int BaseRegister { get; set; } = -1;
        public int IndexRegister { get; set; } = -1;
        public int Scale { get; set; } = 1;
        public long Displacement { get; set; }
        public bool HasDisplacement { get; set; }
        public bool RipRelative { get; set; }

        public bool IsMemory => Kind == X64OperandKind.Memory;
        public bool IsRegister => Kind >= X64OperandKind.Register8 && Kind <= X64OperandKind.Register64;
    }
}
