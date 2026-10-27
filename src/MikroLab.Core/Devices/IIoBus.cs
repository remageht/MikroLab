using System;
using System.Collections.Generic;

namespace MikroLab.Core.Devices
{
    /// <summary>
    /// A device reachable through the CPU's 256-port I/O space. Devices are
    /// registered on an <see cref="IIoBus"/> by port range; the CPU never
    /// knows the concrete port numbers.
    /// </summary>
    public interface IPortDevice
    {
        /// <summary>Human-readable name used by diagnostics and device panels.</summary>
        string Name { get; }

        byte ReadPort(int port);
        void WritePort(int port, byte value);
    }

    /// <summary>
    /// 8080 IN/OUT bus. Unmapped ports read as FFh (the bus floats high) and
    /// writes to them are ignored.
    /// </summary>
    public interface IIoBus
    {
        IReadOnlyList<IPortDevice> Devices { get; }

        /// <summary>Maps <paramref name="device"/> to an inclusive port range.</summary>
        void Register(IPortDevice device, int startPort, int endPort);

        /// <summary>Removes every range that belongs to <paramref name="device"/>.</summary>
        void Unregister(IPortDevice device);

        bool IsMapped(int port);
        byte ReadPort(int port);
        void WritePort(int port, byte value);
    }

    /// <summary>
    /// Default <see cref="IIoBus"/> implementation. Overlapping registrations
    /// are rejected so two devices can never silently drive the same port.
    /// </summary>
    public sealed class IoBus : IIoBus
    {
        private readonly List<Registration> _devices = new List<Registration>();

        public IReadOnlyList<IPortDevice> Devices
        {
            get
            {
                var result = new List<IPortDevice>();
                foreach (Registration registration in _devices)
                {
                    if (!result.Contains(registration.Device))
                    {
                        result.Add(registration.Device);
                    }
                }
                return result;
            }
        }

        public void Register(IPortDevice device, int startPort, int endPort)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            ValidatePort(startPort, nameof(startPort));
            ValidatePort(endPort, nameof(endPort));
            if (startPort > endPort)
            {
                throw new ArgumentException("The port range start must not exceed its end.");
            }

            foreach (Registration existing in _devices)
            {
                if (startPort <= existing.EndPort && endPort >= existing.StartPort)
                {
                    throw new InvalidOperationException(
                        "Port range " + FormatPort(startPort) + ".." + FormatPort(endPort) +
                        " overlaps " + existing.Device.Name + " (" +
                        FormatPort(existing.StartPort) + ".." + FormatPort(existing.EndPort) + ").");
                }
            }

            _devices.Add(new Registration(device, startPort, endPort));
        }

        public void Unregister(IPortDevice device)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            _devices.RemoveAll(registration => ReferenceEquals(registration.Device, device));
        }

        public bool IsMapped(int port)
        {
            ValidatePort(port, nameof(port));
            return Find(port) != null;
        }

        public byte ReadPort(int port)
        {
            ValidatePort(port, nameof(port));
            Registration registration = Find(port);
            return registration != null ? registration.Device.ReadPort(port) : (byte)0xFF;
        }

        public void WritePort(int port, byte value)
        {
            ValidatePort(port, nameof(port));
            Find(port)?.Device.WritePort(port, value);
        }

        private Registration Find(int port)
        {
            foreach (Registration registration in _devices)
            {
                if (port >= registration.StartPort && port <= registration.EndPort)
                {
                    return registration;
                }
            }
            return null;
        }

        private static void ValidatePort(int port, string parameterName)
        {
            if (port < 0 || port > 0xFF)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName, port, "An 8080 I/O port must be in the range 00h..FFh.");
            }
        }

        private static string FormatPort(int port)
        {
            return port.ToString("X2") + "h";
        }

        private sealed class Registration
        {
            public Registration(IPortDevice device, int startPort, int endPort)
            {
                Device = device;
                StartPort = startPort;
                EndPort = endPort;
            }

            public IPortDevice Device { get; }
            public int StartPort { get; }
            public int EndPort { get; }
        }
    }
}
