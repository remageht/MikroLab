using System;
using MikroLab.Core.Memory;

namespace MikroLab.Core.Devices
{
    /// <summary>
    /// Analog multiplexer and ADC. FDh selects the channel on the I/O bus or
    /// in the legacy memory map; FEh returns the current ADC value.
    /// </summary>
    public sealed class AnalogDevice : IPortDevice, IMemoryMappedDevice
    {
        public const int PortMux = 0xFD;
        public const int PortAdc = 0xFE;

        private readonly IAnalogSource _analog;
        private readonly IMemory _legacyMemory;

        public AnalogDevice(IAnalogSource analog, IMemory legacyMemory = null)
        {
            _analog = analog ?? throw new ArgumentNullException(nameof(analog));
            _legacyMemory = legacyMemory;
        }

        public string Name => "Analog mux / ADC";

        public byte ReadPort(int port)
        {
            return port == PortAdc ? _analog.ReadAdc() : (byte)0xFF;
        }

        public void WritePort(int port, byte value)
        {
            if (port == PortMux)
            {
                _analog.SelectedMuxChannel = (byte)(value & 0x03);
                _legacyMemory?.WriteByte((ushort)port, value);
            }
        }

        public byte ReadMemory(int address)
        {
            return address == PortAdc ? _analog.ReadAdc() : (byte)0xFF;
        }

        public void WriteMemory(int address, byte value)
        {
            if (address == PortMux)
            {
                _analog.SelectedMuxChannel = (byte)(value & 0x03);
            }
        }
    }
}
