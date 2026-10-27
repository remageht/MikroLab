using System;
using MikroLab.Core.Memory;

namespace MikroLab.Core.Devices
{
    /// <summary>
    /// Eight seven-segment latches mapped to 83F8h..83FFh in the legacy
    /// address space. The latch bytes remain visible in raw memory as well.
    /// </summary>
    public sealed class DisplayMemoryDevice : IMemoryMappedDevice
    {
        private readonly IDisplaySink _display;
        private readonly int _startAddress;
        private readonly int _endAddress;

        public DisplayMemoryDevice(IDisplaySink display, int startAddress, int endAddress)
        {
            _display = display ?? throw new ArgumentNullException(nameof(display));
            _startAddress = startAddress;
            _endAddress = endAddress;
        }

        public string Name => "Seven-segment display latches";

        public byte ReadMemory(int address)
        {
            int index = address - _startAddress;
            if (index < 0 || index > _endAddress - _startAddress)
            {
                return 0xFF;
            }
            return _display.GetDigit(index);
        }

        public void WriteMemory(int address, byte value)
        {
            int index = address - _startAddress;
            if (index >= 0 && index <= _endAddress - _startAddress)
            {
                _display.WriteDigit(index, value);
            }
        }
    }
}
