namespace MikroLab.Core.Devices
{
    public interface IDisplaySink
    {
        /// <summary>
        /// Writes 7-segment bitmask to indicator at index (0..7).
        /// 0..3 are address (IAR), 4..7 are data (IRD).
        /// </summary>
        void WriteDigit(int index, byte segments);

        byte GetDigit(int index);
        void Reset();
    }
}
