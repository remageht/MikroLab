using System;
using MikroLab.Core.Memory;

namespace MikroLab.Core.Devices
{
    /// <summary>
    /// Connects a <see cref="Ppi8255"/> to the I/O and memory buses. Primary
    /// ports default to PA=Fah, PB=F9h, PC=F8h, control=FBh; legacy 01h/02h
    /// aliases keep old VNTU programs and examples working.
    /// </summary>
    public sealed class Ppi8255Device : IPortDevice, IMemoryMappedDevice
    {
        private readonly Ppi8255 _ppi;

        public Ppi8255Device(Ppi8255 ppi, PpiPortMap map = null)
        {
            _ppi = ppi ?? throw new ArgumentNullException(nameof(ppi));
            Map = (map ?? PpiPortMap.Default).Clone();
            Map.Validate();
        }

        public string Name => "KR580IK55 / i8255";

        public PpiPortMap Map { get; }

        public byte ReadPort(int port)
        {
            if (port == Map.PortA) return _ppi.ReadPort(0);
            if (port == Map.PortB || port == Map.LegacyPortB) return _ppi.ReadPort(1);
            if (port == Map.PortC) return _ppi.ReadPort(2);
            if (port == Map.Control || port == Map.LegacyControl) return _ppi.ReadPort(3);
            return 0xFF;
        }

        public void WritePort(int port, byte value)
        {
            if (port == Map.PortA) _ppi.WritePort(0, value);
            else if (port == Map.PortB || port == Map.LegacyPortB) _ppi.WritePort(1, value);
            else if (port == Map.PortC) _ppi.WritePort(2, value);
            else if (port == Map.Control || port == Map.LegacyControl) _ppi.WritePort(3, value);
        }

        public byte ReadMemory(int address)
        {
            // Legacy LDA 00FAh reads port A directly; unlike the old PPI
            // state it must not consume or clear the value.
            if (address == Map.PortA) return _ppi.ReadPort(0);
            return 0xFF;
        }

        public void WriteMemory(int address, byte value)
        {
            // Writes to the legacy aliases and to the primary port addresses
            // are accepted so both STA 0001h and STA 00F9h keep working.
            WritePort(address, value);
        }
    }
}
