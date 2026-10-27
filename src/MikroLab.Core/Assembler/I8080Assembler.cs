using System;
using System.Collections.Generic;

namespace MikroLab.Core.Assembler
{
    public static partial class I8080Assembler
    {
        public static AssembleResult Assemble(string source)
        {
            return Assemble(source, 0x0000);
        }

        public static AssembleResult Assemble(string source, ushort origin)
        {
            List<AssemblerError> errors = new List<AssemblerError>();
            List<ParsedLine> lines = ParseLines(source, errors);

            foreach (ParsedLine line in lines)
                ValidateLine(line, errors);

            Dictionary<string, ushort> symbols = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);
            FirstPass(lines, origin, symbols, errors);

            byte[] image;
            List<AssembledChunk> chunks;
            List<ListingLine> listing;
            ushort startAddress;
            ushort endAddress;
            SecondPass(lines, origin, symbols, errors, out image, out chunks, out listing, out startAddress, out endAddress);

            errors.Sort(delegate(AssemblerError a, AssemblerError b)
            {
                return a.LineNumber.CompareTo(b.LineNumber);
            });

            IReadOnlyDictionary<string, ushort> frozenSymbols = FreezeSymbols(symbols);
            return new AssembleResult(
                errors.Count == 0,
                image,
                chunks,
                frozenSymbols,
                listing,
                errors,
                startAddress,
                endAddress);
        }
    }
}
