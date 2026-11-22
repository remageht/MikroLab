using System;

namespace MikroLab.Core.Memory
{
    /// <summary>
    /// Flat byte memory of the IBM PC real mode: one megabyte addressed by a
    /// 20-bit linear address. Segment:offset pairs are resolved by the CPU,
    /// the memory itself only deals with linear addresses.
    /// </summary>
    public sealed class I8086Memory
    {
        public const int Size = 1024 * 1024;
        public const int AddressMask = Size - 1;

        private readonly byte[] _data = new byte[Size];

        public byte ReadByte(int linear)
        {
            return _data[linear & AddressMask];
        }

        public void WriteByte(int linear, byte value)
        {
            _data[linear & AddressMask] = value;
        }

        public ushort ReadWord(int linear)
        {
            int a = linear & AddressMask;
            int b = (a + 1) & AddressMask;
            return (ushort)(_data[a] | (_data[b] << 8));
        }

        public void WriteWord(int linear, ushort value)
        {
            int a = linear & AddressMask;
            int b = (a + 1) & AddressMask;
            _data[a] = (byte)(value & 0xFF);
            _data[b] = (byte)(value >> 8);
        }

        public uint ReadDword(int linear)
        {
            return (uint)(ReadWord(linear) | (ReadWord(linear + 2) << 16));
        }

        public void WriteDword(int linear, uint value)
        {
            WriteWord(linear, (ushort)(value & 0xFFFF));
            WriteWord(linear + 2, (ushort)(value >> 16));
        }

        /// <summary>Writes a block of bytes starting at a linear address.</summary>
        public void WriteBlock(int linear, byte[] data)
        {
            if (data == null)
                return;
            for (int i = 0; i < data.Length; i++)
                WriteByte(linear + i, data[i]);
        }

        public void Clear()
        {
            Array.Clear(_data, 0, _data.Length);
        }

        public byte[] Capture()
        {
            byte[] copy = new byte[_data.Length];
            Array.Copy(_data, copy, _data.Length);
            return copy;
        }

        public void Restore(byte[] dump)
        {
            if (dump == null)
                return;
            int count = Math.Min(dump.Length, _data.Length);
            Array.Copy(dump, _data, count);
        }
    }
}
