using System;
using System.Collections.Generic;

namespace MikroLab.Core.Assembler.I8086
{
    /// <summary>Kind of a single instruction operand.</summary>
    public enum I8086OperandType
    {
        None,
        Register8,
        Register16,
        SegmentRegister,
        Memory,
        Immediate,
        FarPointer
    }

    /// <summary>One parsed operand of an Intel syntax instruction.</summary>
    public sealed class I8086Operand
    {
        public I8086OperandType Type { get; set; }
        public string Text { get; set; }

        /// <summary>Register number 0..7 (AX=0, CX=1, DX=2, BX=3, SP=4, BP=5, SI=6, DI=7).</summary>
        public int Register { get; set; } = -1;

        /// <summary>Segment register number 0..3 (ES=0, CS=1, SS=2, DS=3).</summary>
        public int SegmentRegister { get; set; } = -1;

        /// <summary>Operand size in bytes for memory operands.</summary>
        public int Size { get; set; }

        /// <summary>Segment override of the memory operand, -1 when none.</summary>
        public int SegmentOverride { get; set; } = -1;

        /// <summary>Base register of the effective address, -1 when none.</summary>
        public int Base { get; set; } = -1;

        /// <summary>Index register of the effective address, -1 when none.</summary>
        public int Index { get; set; } = -1;

        /// <summary>Displacement expression of the effective address.</summary>
        public string Displacement { get; set; }

        /// <summary>Resolved displacement value.</summary>
        public int DisplacementValue { get; set; }

        /// <summary>True when the displacement is known.</summary>
        public bool HasResolvedDisplacement { get; set; }

        /// <summary>True for a memory operand written without any register.</summary>
        public bool IsDirect => Base < 0 && Index < 0;

        public bool IsMemory => Type == I8086OperandType.Memory;

        public bool IsRegister16 => Type == I8086OperandType.Register16;
    }

    /// <summary>One line of the listing produced by the assembler.</summary>
    public sealed class I8086ListingLine
    {
        public int LineNumber { get; }
        public string Source { get; }
        public bool HasAddress { get; }
        public ushort Offset { get; }
        public byte[] Bytes { get; }

        public I8086ListingLine(int lineNumber, string source, bool hasAddress, ushort offset, byte[] bytes)
        {
            LineNumber = lineNumber;
            Source = source ?? string.Empty;
            HasAddress = hasAddress;
            Offset = offset;
            Bytes = bytes ?? Array.Empty<byte>();
        }
    }

    /// <summary>Result of assembling an Intel syntax source file.</summary>
    public sealed class I8086AssembleResult
    {
        public bool Success { get; }
        public byte[] Image { get; }
        public ushort Origin { get; }
        public ushort EndAddress { get; }
        public IReadOnlyList<I8086ListingLine> Listing { get; }
        public IReadOnlyList<AssemblerError> Errors { get; }
        public IReadOnlyDictionary<string, ushort> Symbols { get; }

        internal I8086AssembleResult(
            bool success,
            byte[] image,
            ushort origin,
            ushort endAddress,
            IReadOnlyList<I8086ListingLine> listing,
            IReadOnlyList<AssemblerError> errors,
            IReadOnlyDictionary<string, ushort> symbols)
        {
            Success = success;
            Image = image ?? Array.Empty<byte>();
            Origin = origin;
            EndAddress = endAddress;
            Listing = listing ?? Array.Empty<I8086ListingLine>();
            Errors = errors ?? Array.Empty<AssemblerError>();
            Symbols = symbols ?? new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
