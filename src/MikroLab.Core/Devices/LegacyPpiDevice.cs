using System;
using MikroLab.Core.Memory;

namespace MikroLab.Core.Devices
{
    /// <summary>
    /// Compatibility mapping of the old memory-mapped PPI onto the IN/OUT bus.
    /// The 8255 itself will be modelled separately; this adapter only keeps
    /// existing VNTU-style programs (OUT 01h, OUT F9h, OUT 02h, IN FAh) alive.
    /// </summary>
    public sealed class LegacyPpiDevice : IPortDevice, IMemoryMappedDevice
    {
        public const int PortLamps = 0x01;
        public const int PortControl = 0x02;
        public const int PortLampsAlias = 0xF9;
        public const int PortInput = 0xFA;

        private readonly IPpiDevice _ppi;
        private readonly IMemory _legacyMemory;

        public LegacyPpiDevice(IPpiDevice ppi, IMemory legacyMemory = null)
        {
            _ppi = ppi ?? throw new ArgumentNullException(nameof(ppi));
            _legacyMemory = legacyMemory;
        }

        public string Name => "PPI (legacy VNTU mapping)";

        public byte ReadPort(int port)
        {
            if (port == PortInput)
            {
                return _ppi.ReadTumblers();
            }
            return 0xFF;
        }

        public void WritePort(int port, byte value)
        {
            if (port == PortLamps || port == PortLampsAlias)
            {
                _ppi.Lamps = value;
                _legacyMemory?.WriteByte((ushort)port, value);
            }
            else if (port == PortControl)
            {
                _ppi.ControlWord = value;
                _legacyMemory?.WriteByte((ushort)port, value);
            }
        }

        public byte ReadMemory(int address)
        {
            if (address == PortInput)
            {
                byte value = _ppi.InputWord;
                _ppi.InputWord = 0xFF;
                return value;
            }
            return 0xFF;
        }

        public void WriteMemory(int address, byte value)
        {
            if (address == PortLamps || address == PortLampsAlias)
            {
                _ppi.Lamps = value;
            }
            else if (address == PortControl)
            {
                _ppi.ControlWord = value;
            }
        }
    }
}
