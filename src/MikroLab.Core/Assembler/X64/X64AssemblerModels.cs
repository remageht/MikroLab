using System;
using System.Collections.Generic;

namespace MikroLab.Core.Assembler.X64
{
    /// <summary>One line of the x86-64 listing.</summary>
    public sealed class X64ListingLine
    {
        public int LineNumber { get; }
        public string Source { get; }
        public bool HasAddress { get; }
        public ulong Offset { get; }
        public byte[] Bytes { get; }

        public X64ListingLine(int lineNumber, string source, bool hasAddress, ulong offset, byte[] bytes)
        {
            LineNumber = lineNumber;
            Source = source ?? string.Empty;
            HasAddress = hasAddress;
            Offset = offset;
            Bytes = bytes ?? Array.Empty<byte>();
        }
    }

    /// <summary>Result of assembling an x86-64 source file.</summary>
    public sealed class X64AssembleResult
    {
        public bool Success { get; }
        public byte[] Image { get; }
        public ulong Origin { get; }
        public ulong EndAddress { get; }
        public IReadOnlyList<X64ListingLine> Listing { get; }
        public IReadOnlyList<AssemblerError> Errors { get; }
        public IReadOnlyDictionary<string, long> Symbols { get; }

        internal X64AssembleResult(
            bool success,
            byte[] image,
            ulong origin,
            ulong endAddress,
            IReadOnlyList<X64ListingLine> listing,
            IReadOnlyList<AssemblerError> errors,
            IReadOnlyDictionary<string, long> symbols)
        {
            Success = success;
            Image = image ?? Array.Empty<byte>();
            Origin = origin;
            EndAddress = endAddress;
            Listing = listing ?? Array.Empty<X64ListingLine>();
            Errors = errors ?? Array.Empty<AssemblerError>();
            Symbols = symbols ?? new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
