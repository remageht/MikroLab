using System;
using System.Collections.Generic;

namespace MikroLab.Core.Assembler
{
    public sealed class AssemblerError
    {
        public int LineNumber { get; }
        public string Message { get; }

        public AssemblerError(int lineNumber, string message)
        {
            LineNumber = lineNumber;
            Message = message ?? string.Empty;
        }
    }

    public sealed class AssembledChunk
    {
        public ushort Address { get; }
        public byte[] Data { get; }
        public int LineNumber { get; }

        public AssembledChunk(ushort address, byte[] data, int lineNumber)
        {
            Address = address;
            Data = data ?? Array.Empty<byte>();
            LineNumber = lineNumber;
        }
    }

    public sealed class ListingLine
    {
        public int LineNumber { get; }
        public string Source { get; }
        public bool HasAddress { get; }
        public ushort Address { get; }
        public byte[] Bytes { get; }

        public ListingLine(int lineNumber, string source, bool hasAddress, ushort address, byte[] bytes)
        {
            LineNumber = lineNumber;
            Source = source ?? string.Empty;
            HasAddress = hasAddress;
            Address = address;
            Bytes = bytes ?? Array.Empty<byte>();
        }
    }

    public sealed class AssembleResult
    {
        public bool Success { get; }
        public byte[] Image { get; }
        public IReadOnlyList<AssembledChunk> Chunks { get; }
        public IReadOnlyDictionary<string, ushort> Symbols { get; }
        public IReadOnlyList<ListingLine> Listing { get; }
        public IReadOnlyList<AssemblerError> Errors { get; }
        public ushort StartAddress { get; }
        public ushort EndAddress { get; }

        internal AssembleResult(
            bool success,
            byte[] image,
            IReadOnlyList<AssembledChunk> chunks,
            IReadOnlyDictionary<string, ushort> symbols,
            IReadOnlyList<ListingLine> listing,
            IReadOnlyList<AssemblerError> errors,
            ushort startAddress,
            ushort endAddress)
        {
            Success = success;
            Image = image ?? new byte[65536];
            Chunks = chunks ?? Array.Empty<AssembledChunk>();
            Symbols = symbols ?? new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);
            Listing = listing ?? Array.Empty<ListingLine>();
            Errors = errors ?? Array.Empty<AssemblerError>();
            StartAddress = startAddress;
            EndAddress = endAddress;
        }
    }
}
