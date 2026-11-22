using System;
using System.Collections.Generic;

namespace MikroLab.Presentation
{
    /// <summary>
    /// Breakpoints of the 16- and 64-bit architectures. The user marks a line
    /// in the editor, and after the assembly the line is mapped to the address
    /// the assembler produced for it.
    /// </summary>
    public sealed class BreakpointSet
    {
        private readonly HashSet<int> _lines = new HashSet<int>();
        private readonly HashSet<ulong> _addresses = new HashSet<ulong>();
        private readonly Dictionary<int, ulong> _lineAddresses = new Dictionary<int, ulong>();

        /// <summary>Editor lines marked by the user.</summary>
        public IReadOnlyCollection<int> Lines => _lines;

        /// <summary>Addresses of the marked lines after the last assembly.</summary>
        public IReadOnlyCollection<ulong> Addresses => _addresses;

        public int Count => _lines.Count;

        /// <summary>Marks or unmarks a line. Returns true when it was marked.</summary>
        public bool ToggleLine(int line)
        {
            if (line <= 0)
                return false;

            if (_lines.Remove(line))
                return false;

            _lines.Add(line);
            return true;
        }

        public bool HasLine(int line) => _lines.Contains(line);

        /// <summary>Removes every breakpoint.</summary>
        public void Clear()
        {
            _lines.Clear();
            _addresses.Clear();
            _lineAddresses.Clear();
        }

        /// <summary>
        /// True when the marked line has machine code. A line with a label or
        /// a comment cannot stop the program.
        /// </summary>
        public bool HasCode(int line) => _lineAddresses.ContainsKey(line);

        /// <summary>
        /// Recomputes the addresses of the marked lines. The resolver returns
        /// the address of a line or null when the line has no code.
        /// </summary>
        public void MapAddresses(Func<int, ulong?> resolver)
        {
            _addresses.Clear();
            _lineAddresses.Clear();
            if (resolver == null)
                return;

            foreach (int line in _lines)
            {
                ulong? address = resolver(line);
                if (!address.HasValue)
                    continue;

                _addresses.Add(address.Value);
                _lineAddresses[line] = address.Value;
            }
        }

        public bool Contains(ulong address) => _addresses.Contains(address);

        public bool Contains(ushort address) => _addresses.Contains(address);
    }
}
