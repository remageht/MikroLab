using System;

namespace MikroLab.Core.Memory
{
    public class NibbleMemory : IMemory
    {
        private const int RandomSeed = 54321;
        private readonly Cell[] _memory = new Cell[MemoryMap.MemSize];
        private Random _random;

        public NibbleMemory()
        {
            Reset();
        }

        public void Reset()
        {
            _random = new Random(RandomSeed);
            RandomiseMemory();
        }

        public void RandomiseMemory()
        {
            for (int i = 0; i < MemoryMap.MemSize; i++)
            {
                _memory[i].signifBit = (byte)_random.Next(15);
                _memory[i].lowerBit = (byte)_random.Next(15);
            }
        }

        public void RandomiseRegisters()
        {
            for (int r = MemoryMap.RegsStart; r <= MemoryMap.RegsEnd; r++)
            {
                _memory[r].signifBit = (byte)_random.Next(15);
                _memory[r].lowerBit = (byte)_random.Next(15);
            }
        }

        public byte ReadByte(ushort address)
        {
            return (byte)((_memory[address].signifBit << 4) | (_memory[address].lowerBit & 0x0F));
        }

        public void WriteByte(ushort address, byte value)
        {
            _memory[address].signifBit = (byte)((value >> 4) & 0x0F);
            _memory[address].lowerBit = (byte)(value & 0x0F);
        }

        public void GetNibbles(int address, out int signif, out int lower)
        {
            int addr = address & 0xFFFF;
            signif = _memory[addr].signifBit;
            lower = _memory[addr].lowerBit;
        }

        public void SetNibbles(int address, int signif, int lower)
        {
            int addr = address & 0xFFFF;
            _memory[addr].signifBit = (byte)(signif & 0x0F);
            _memory[addr].lowerBit = (byte)(lower & 0x0F);
        }

        public bool IsClientRegion(int address)
        {
            int addr = address & 0xFFFF;
            return (addr >= MemoryMap.UserMemStart && addr <= MemoryMap.UserMemEnd) ||
                   (addr >= MemoryMap.RegsStart && addr <= MemoryMap.RegsEnd);
        }

        public byte[] Capture()
        {
            byte[] dump = new byte[MemoryMap.MemSize];
            for (int i = 0; i < MemoryMap.MemSize; i++)
            {
                dump[i] = (byte)((_memory[i].signifBit << 4) | (_memory[i].lowerBit & 0x0F));
            }
            return dump;
        }

        public void Restore(byte[] memoryDump)
        {
            if (memoryDump == null) throw new ArgumentNullException(nameof(memoryDump));
            int len = Math.Min(memoryDump.Length, MemoryMap.MemSize);
            for (int i = 0; i < len; i++)
            {
                _memory[i].signifBit = (byte)((memoryDump[i] >> 4) & 0x0F);
                _memory[i].lowerBit = (byte)(memoryDump[i] & 0x0F);
            }
        }
    }
}
