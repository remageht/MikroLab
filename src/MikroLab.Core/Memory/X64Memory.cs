using System;
using System.Collections.Generic;

namespace MikroLab.Core.Memory
{
    /// <summary>
    /// Memory of the x86-64 long mode: a sparse 64-bit address space built
    /// from 4 KB pages. Pages are allocated on the first write, so an
    /// emulator of a small program does not need gigabytes of RAM.
    /// </summary>
    public sealed class X64Memory
    {
        public const int PageSize = 4096;
        public const int PageShift = 12;

        private readonly Dictionary<int, byte[]> _pages = new Dictionary<int, byte[]>();
        private readonly object _lock = new object();

        public int PageCount
        {
            get
            {
                lock (_lock)
                    return _pages.Count;
            }
        }

        public byte ReadByte(ulong address)
        {
            int page = (int)(address >> PageShift);
            int offset = (int)(address & (PageSize - 1));

            lock (_lock)
            {
                byte[] data;
                if (_pages.TryGetValue(page, out data))
                    return data[offset];
            }

            return 0;
        }

        public void WriteByte(ulong address, byte value)
        {
            int page = (int)(address >> PageShift);
            int offset = (int)(address & (PageSize - 1));

            lock (_lock)
            {
                byte[] data;
                if (!_pages.TryGetValue(page, out data))
                {
                    data = new byte[PageSize];
                    _pages[page] = data;
                }

                data[offset] = value;
            }
        }

        public ushort ReadWord(ulong address)
        {
            return (ushort)(ReadByte(address) | (ReadByte(address + 1) << 8));
        }

        public void WriteWord(ulong address, ushort value)
        {
            WriteByte(address, (byte)(value & 0xFF));
            WriteByte(address + 1, (byte)(value >> 8));
        }

        public uint ReadDword(ulong address)
        {
            return (uint)(ReadWord(address) | (ReadWord(address + 2) << 16));
        }

        public void WriteDword(ulong address, uint value)
        {
            WriteWord(address, (ushort)(value & 0xFFFF));
            WriteWord(address + 2, (ushort)(value >> 16));
        }

        public ulong ReadQword(ulong address)
        {
            return ReadDword(address) | ((ulong)ReadDword(address + 4) << 32);
        }

        public void WriteQword(ulong address, ulong value)
        {
            WriteDword(address, (uint)(value & 0xFFFFFFFF));
            WriteDword(address + 4, (uint)(value >> 32));
        }

        public void WriteBlock(ulong address, byte[] data)
        {
            if (data == null)
                return;
            for (int i = 0; i < data.Length; i++)
                WriteByte(address + (ulong)i, data[i]);
        }

        public void Clear()
        {
            lock (_lock)
                _pages.Clear();
        }

        /// <summary>Snapshot of the allocated pages.</summary>
        public X64MemorySnapshot Capture()
        {
            lock (_lock)
            {
                int[] numbers = new int[_pages.Count];
                _pages.Keys.CopyTo(numbers, 0);
                byte[][] data = new byte[numbers.Length][];
                for (int i = 0; i < numbers.Length; i++)
                    data[i] = (byte[])_pages[numbers[i]].Clone();
                return new X64MemorySnapshot(numbers, data);
            }
        }

        public void Restore(X64MemorySnapshot snapshot)
        {
            if (snapshot == null)
                return;

            lock (_lock)
            {
                _pages.Clear();
                for (int i = 0; i < snapshot.PageNumbers.Length; i++)
                    _pages[snapshot.PageNumbers[i]] = (byte[])snapshot.PageData[i].Clone();
            }
        }
    }

    /// <summary>Snapshot of the sparse memory pages.</summary>
    public sealed class X64MemorySnapshot
    {
        public int[] PageNumbers { get; }
        public byte[][] PageData { get; }

        public X64MemorySnapshot(int[] pageNumbers, byte[][] pageData)
        {
            PageNumbers = pageNumbers ?? Array.Empty<int>();
            PageData = pageData ?? Array.Empty<byte[]>();
        }
    }
}
