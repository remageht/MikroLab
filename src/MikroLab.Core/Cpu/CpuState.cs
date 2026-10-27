using MikroLab.Core.Memory;

namespace MikroLab.Core.Cpu
{
    public class CpuState
    {
        private readonly IMemory _memory;

        public CpuState(IMemory memory)
        {
            _memory = memory;
        }

        public byte A
        {
            get => _memory.ReadByte(MemoryMap.RegA);
            set => _memory.WriteByte(MemoryMap.RegA, value);
        }

        public byte F
        {
            get => _memory.ReadByte(MemoryMap.RegF);
            set => _memory.WriteByte(MemoryMap.RegF, value);
        }

        public byte B
        {
            get => _memory.ReadByte(MemoryMap.RegB);
            set => _memory.WriteByte(MemoryMap.RegB, value);
        }

        public byte C
        {
            get => _memory.ReadByte(MemoryMap.RegC);
            set => _memory.WriteByte(MemoryMap.RegC, value);
        }

        public byte D
        {
            get => _memory.ReadByte(MemoryMap.RegD);
            set => _memory.WriteByte(MemoryMap.RegD, value);
        }

        public byte E
        {
            get => _memory.ReadByte(MemoryMap.RegE);
            set => _memory.WriteByte(MemoryMap.RegE, value);
        }

        public byte H
        {
            get => _memory.ReadByte(MemoryMap.RegH);
            set => _memory.WriteByte(MemoryMap.RegH, value);
        }

        public byte L
        {
            get => _memory.ReadByte(MemoryMap.RegL);
            set => _memory.WriteByte(MemoryMap.RegL, value);
        }

        public byte M
        {
            get => _memory.ReadByte((ushort)((H << 8) | L));
            set => _memory.WriteByte((ushort)((H << 8) | L), value);
        }

        public ushort PC { get; set; } = MemoryMap.UserMemStart;
        public ushort SP { get; set; } = 0x83FE;

        public FlagReg Flags;
        public ulong Cycles { get; set; }
    }
}
