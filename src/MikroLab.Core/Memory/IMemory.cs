namespace MikroLab.Core.Memory
{
    public interface IMemory
    {
        byte ReadByte(ushort address);
        void WriteByte(ushort address, byte value);

        void GetNibbles(int address, out int signif, out int lower);
        void SetNibbles(int address, int signif, int lower);

        bool IsClientRegion(int address);
        void Reset();
        byte[] Capture();
        void Restore(byte[] memoryDump);
    }
}
