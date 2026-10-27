using System;
using System.IO;
using MikroLab.Core.Cpu;
using MikroLab.Core.Machine;

namespace MikroLab.Core.Formats
{
    /// <summary>
    /// Reader and writer for the original KP580 <c>.580</c> snapshot format.
    ///
    /// The file is a fixed 65,549-byte image: 64 KiB of RAM followed by nine
    /// register bytes (A, B, C, D, E, H, L, W, Z), a little-endian PC and a
    /// little-endian SP. There is no magic header and no flag register. The
    /// format is intentionally byte-exact so images can be exchanged with the
    /// reference KP580 emulator.
    /// </summary>
    public sealed class Kr580Image
    {
        public const int MemoryLength = 0x10000;
        public const int RegisterCount = 9;
        public const int FileLength = MemoryLength + RegisterCount + 2 + 2; // 65,549

        public byte[] Memory { get; }
        public byte A { get; set; }
        public byte B { get; set; }
        public byte C { get; set; }
        public byte D { get; set; }
        public byte E { get; set; }
        public byte H { get; set; }
        public byte L { get; set; }

        /// <summary>Internal 8080 W register, preserved for compatibility.</summary>
        public byte W { get; set; }

        /// <summary>Internal 8080 Z register, preserved for compatibility.</summary>
        public byte Z { get; set; }

        public ushort PC { get; set; }
        public ushort SP { get; set; }

        public Kr580Image()
        {
            Memory = new byte[MemoryLength];
        }

        /// <summary>Creates an image from a complete machine snapshot.</summary>
        public static Kr580Image FromSnapshot(MachineSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (snapshot.Memory == null || snapshot.Memory.Length != MemoryLength)
                throw new ArgumentException("Snapshot must contain a 64 KiB memory dump.", nameof(snapshot));

            var image = new Kr580Image
            {
                A = snapshot.A,
                B = snapshot.B,
                C = snapshot.C,
                D = snapshot.D,
                E = snapshot.E,
                H = snapshot.H,
                L = snapshot.L,
                PC = snapshot.PC,
                SP = snapshot.SP
            };
            Array.Copy(snapshot.Memory, image.Memory, MemoryLength);
            return image;
        }

        /// <summary>
        /// Converts the image to a snapshot. The .580 format has no flags, so
        /// the flag register starts in its reset state (the mandatory D1 bit
        /// is set, all condition flags are clear).
        /// </summary>
        public MachineSnapshot ToSnapshot()
        {
            var flags = new FlagReg();
            flags.Reset();

            return new MachineSnapshot
            {
                A = A,
                B = B,
                C = C,
                D = D,
                E = E,
                H = H,
                L = L,
                F = 0x02,
                Flags = flags,
                PC = PC,
                SP = SP,
                State = RunState.Stopped,
                Memory = (byte[])Memory.Clone()
            };
        }

        public static Kr580Image Load(string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                return Load(stream);
            }
        }

        public static Kr580Image Load(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead) throw new ArgumentException("Stream must be readable.", nameof(stream));

            var buffer = new byte[FileLength];
            int total = 0;
            while (total < FileLength)
            {
                int read = stream.Read(buffer, total, FileLength - total);
                if (read <= 0) break;
                total += read;
            }

            if (total != FileLength)
            {
                throw new InvalidDataException(
                    "A .580 image must be exactly " + FileLength + " bytes; got " + total + ".");
            }

            if (stream.ReadByte() >= 0)
            {
                throw new InvalidDataException(
                    "A .580 image must be exactly " + FileLength + " bytes; the file has trailing data.");
            }

            var image = new Kr580Image();
            Array.Copy(buffer, image.Memory, MemoryLength);

            int offset = MemoryLength;
            image.A = buffer[offset++];
            image.B = buffer[offset++];
            image.C = buffer[offset++];
            image.D = buffer[offset++];
            image.E = buffer[offset++];
            image.H = buffer[offset++];
            image.L = buffer[offset++];
            image.W = buffer[offset++];
            image.Z = buffer[offset++];
            image.PC = (ushort)(buffer[offset] | (buffer[offset + 1] << 8));
            offset += 2;
            image.SP = (ushort)(buffer[offset] | (buffer[offset + 1] << 8));
            return image;
        }

        public void Save(string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                Save(stream);
            }
        }

        public void Save(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanWrite) throw new ArgumentException("Stream must be writable.", nameof(stream));

            var buffer = new byte[FileLength];
            Array.Copy(Memory, buffer, MemoryLength);

            int offset = MemoryLength;
            buffer[offset++] = A;
            buffer[offset++] = B;
            buffer[offset++] = C;
            buffer[offset++] = D;
            buffer[offset++] = E;
            buffer[offset++] = H;
            buffer[offset++] = L;
            buffer[offset++] = W;
            buffer[offset++] = Z;
            buffer[offset++] = (byte)(PC & 0xFF);
            buffer[offset++] = (byte)(PC >> 8);
            buffer[offset++] = (byte)(SP & 0xFF);
            buffer[offset++] = (byte)(SP >> 8);

            stream.Write(buffer, 0, buffer.Length);
        }
    }
}
