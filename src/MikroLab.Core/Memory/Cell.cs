namespace MikroLab.Core.Memory
{
    public struct Cell
    {
        public byte signifBit;
        public byte lowerBit;

        public Cell(byte signif, byte lower)
        {
            signifBit = signif;
            lowerBit = lower;
        }

        public byte ToByte() => (byte)((signifBit << 4) | (lowerBit & 0x0F));

        public static Cell FromByte(byte b) => new Cell((byte)((b >> 4) & 0x0F), (byte)(b & 0x0F));
    }
}
