using System;
using System.Collections.Generic;

namespace MikroLab.Core.Memory
{
    /// <summary>Directions in which a memory-mapped device responds.</summary>
    [Flags]
    public enum MemoryAccess
    {
        None = 0,
        Read = 1,
        Write = 2,
        ReadWrite = Read | Write
    }

    /// <summary>
    /// A device mapped into the 16-bit address space. Reads and writes are
    /// registered independently so write-only latches and read-only inputs
    /// do not have to mimic the opposite direction.
    /// </summary>
    public interface IMemoryMappedDevice
    {
        string Name { get; }

        byte ReadMemory(int address);
        void WriteMemory(int address, byte value);
    }

    /// <summary>
    /// Wraps raw <see cref="NibbleMemory"/> and routes registered address
    /// windows to devices. Writes always update the underlying RAM first, so
    /// snapshots, the memory panel and existing programs keep seeing the last
    /// byte that was written. Reads from a mapped window are served by the
    /// device.
    /// </summary>
    public sealed class MappedMemory : IMemory
    {
        private readonly NibbleMemory _raw;
        private readonly List<Registration> _readers = new List<Registration>();
        private readonly List<Registration> _writers = new List<Registration>();

        public MappedMemory(NibbleMemory raw)
        {
            _raw = raw ?? throw new ArgumentNullException(nameof(raw));
        }

        public NibbleMemory Raw => _raw;

        /// <summary>Maps an inclusive address window in the given direction(s).</summary>
        public void Register(
            IMemoryMappedDevice device,
            int startAddress,
            int endAddress,
            MemoryAccess access = MemoryAccess.ReadWrite)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            ValidateAddress(startAddress, nameof(startAddress));
            ValidateAddress(endAddress, nameof(endAddress));
            if (startAddress > endAddress)
            {
                throw new ArgumentException("The address range start must not exceed its end.");
            }
            if ((access & ~MemoryAccess.ReadWrite) != 0 || access == MemoryAccess.None)
            {
                throw new ArgumentOutOfRangeException(nameof(access));
            }

            if ((access & MemoryAccess.Read) != 0)
            {
                EnsureFree(_readers, device, startAddress, endAddress);
                _readers.Add(new Registration(device, startAddress, endAddress));
            }
            if ((access & MemoryAccess.Write) != 0)
            {
                EnsureFree(_writers, device, startAddress, endAddress);
                _writers.Add(new Registration(device, startAddress, endAddress));
            }
        }

        public void Unregister(IMemoryMappedDevice device)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            _readers.RemoveAll(registration => ReferenceEquals(registration.Device, device));
            _writers.RemoveAll(registration => ReferenceEquals(registration.Device, device));
        }

        public bool IsMapped(int address, MemoryAccess access)
        {
            ValidateAddress(address, nameof(address));
            if ((access & MemoryAccess.Read) != 0 && Find(_readers, address) != null)
            {
                return true;
            }
            return (access & MemoryAccess.Write) != 0 && Find(_writers, address) != null;
        }

        public byte ReadByte(ushort address)
        {
            Registration registration = Find(_readers, address);
            return registration != null
                ? registration.Device.ReadMemory(address)
                : _raw.ReadByte(address);
        }

        public void WriteByte(ushort address, byte value)
        {
            // Keep RAM coherent even for mapped windows: a snapshot restored
            // without device state still shows the bytes the program wrote.
            _raw.WriteByte(address, value);
            Find(_writers, address)?.Device.WriteMemory(address, value);
        }

        public void GetNibbles(int address, out int signif, out int lower)
        {
            _raw.GetNibbles(address, out signif, out lower);
        }

        public void SetNibbles(int address, int signif, int lower)
        {
            _raw.SetNibbles(address, signif, lower);
        }

        public bool IsClientRegion(int address)
        {
            return _raw.IsClientRegion(address);
        }

        public void Reset()
        {
            _raw.Reset();
        }

        public byte[] Capture()
        {
            return _raw.Capture();
        }

        public void Restore(byte[] memoryDump)
        {
            _raw.Restore(memoryDump);
        }

        private static Registration Find(List<Registration> registrations, int address)
        {
            foreach (Registration registration in registrations)
            {
                if (address >= registration.StartAddress && address <= registration.EndAddress)
                {
                    return registration;
                }
            }
            return null;
        }

        private static void EnsureFree(
            List<Registration> registrations,
            IMemoryMappedDevice device,
            int startAddress,
            int endAddress)
        {
            foreach (Registration existing in registrations)
            {
                if (startAddress <= existing.EndAddress && endAddress >= existing.StartAddress)
                {
                    throw new InvalidOperationException(
                        "Memory range " + FormatAddress(startAddress) + ".." + FormatAddress(endAddress) +
                        " overlaps " + existing.Device.Name + " (" +
                        FormatAddress(existing.StartAddress) + ".." + FormatAddress(existing.EndAddress) + ").");
                }
            }
        }

        private static void ValidateAddress(int address, string parameterName)
        {
            if (address < 0 || address > 0xFFFF)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName, address, "An 8080 address must be in the range 0000h..FFFFh.");
            }
        }

        private static string FormatAddress(int address)
        {
            return address.ToString("X4") + "h";
        }

        private sealed class Registration
        {
            public Registration(IMemoryMappedDevice device, int startAddress, int endAddress)
            {
                Device = device;
                StartAddress = startAddress;
                EndAddress = endAddress;
            }

            public IMemoryMappedDevice Device { get; }
            public int StartAddress { get; }
            public int EndAddress { get; }
        }
    }
}
