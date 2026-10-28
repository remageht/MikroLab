using System;
using MikroLab.Core.Memory;

namespace MikroLab.Core.Debugging
{
    /// <summary>
    /// A read and/or write watchpoint on a single 8080 address. Watchpoints
    /// stop the CPU after the instruction that touched the address completes.
    /// </summary>
    public readonly struct MemoryWatchpoint : IEquatable<MemoryWatchpoint>
    {
        public MemoryWatchpoint(ushort address, MemoryAccess access)
        {
            if (access == MemoryAccess.None ||
                (access & ~MemoryAccess.ReadWrite) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(access));
            }

            Address = address;
            Access = access;
        }

        public ushort Address { get; }
        public MemoryAccess Access { get; }

        public bool WatchesRead => (Access & MemoryAccess.Read) != 0;
        public bool WatchesWrite => (Access & MemoryAccess.Write) != 0;

        public bool Matches(ushort address, MemoryAccess access)
        {
            if (Address != address) return false;
            if ((access & MemoryAccess.Read) != 0 && WatchesRead) return true;
            return (access & MemoryAccess.Write) != 0 && WatchesWrite;
        }

        public string AccessText
        {
            get
            {
                if (Access == MemoryAccess.ReadWrite) return "R/W";
                return Access == MemoryAccess.Read ? "R" : "W";
            }
        }

        public override string ToString()
        {
            return Address.ToString("X4") + "h " + AccessText;
        }

        public bool Equals(MemoryWatchpoint other)
        {
            return Address == other.Address && Access == other.Access;
        }

        public override bool Equals(object obj)
        {
            return obj is MemoryWatchpoint other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (Address << 2) ^ (int)Access;
        }
    }
}
