using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MikroLab.Core.Formats
{
    /// <summary>A contiguous block of bytes loaded from an Intel HEX file.</summary>
    public sealed class IntelHexSegment
    {
        public ushort Address { get; }
        public byte[] Data { get; }

        public IntelHexSegment(ushort address, byte[] data)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
            if ((int)address + data.Length > 0x10000)
                throw new ArgumentException("Segment extends beyond 64K.", nameof(data));

            Address = address;
        }

        public int Length => Data.Length;
    }

    /// <summary>Parsed Intel HEX image with optional start address.</summary>
    public sealed class IntelHexImage
    {
        private readonly List<IntelHexSegment> _segments = new List<IntelHexSegment>();

        public IReadOnlyList<IntelHexSegment> Segments => _segments;
        public ushort? StartAddress { get; internal set; }

        public int ByteCount
        {
            get
            {
                int count = 0;
                foreach (IntelHexSegment segment in _segments)
                {
                    count += segment.Length;
                }
                return count;
            }
        }

        internal void Add(ushort address, byte[] data)
        {
            if (data == null || data.Length == 0) return;
            _segments.Add(new IntelHexSegment(address, data));
        }

        /// <summary>Flattens the image into a 64 KiB memory dump.</summary>
        public byte[] ToMemoryImage()
        {
            var memory = new byte[0x10000];
            foreach (IntelHexSegment segment in _segments)
            {
                Array.Copy(segment.Data, 0, memory, segment.Address, segment.Data.Length);
            }
            return memory;
        }
    }

    /// <summary>
    /// Intel HEX reader and writer. Supports data, end-of-file, extended
    /// segment/linear address and start address records. The emulated machine
    /// has a 16-bit address space, so data beyond 64 KiB is rejected.
    /// </summary>
    public static class IntelHex
    {
        public static IntelHexImage Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));

            var image = new IntelHexImage();
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            bool sawEof = false;
            int lineNumber = 0;
            int recordCount = 0;
            uint baseAddress = 0;

            foreach (string rawLine in lines)
            {
                lineNumber++;
                string line = rawLine.Trim();
                if (line.Length == 0) continue;

                if (sawEof)
                {
                    throw Error(lineNumber, "data after the end-of-file record");
                }
                if (line[0] != ':')
                {
                    throw Error(lineNumber, "record must start with ':'");
                }
                if (line.Length < 11)
                {
                    throw Error(lineNumber, "record is too short");
                }

                int count = ParseByte(line, 1, lineNumber);
                int expectedLength = 11 + count * 2;
                if (line.Length != expectedLength)
                {
                    throw Error(lineNumber, "record length " + line.Length +
                        " does not match byte count " + count);
                }

                int address = ParseWord(line, 3, lineNumber);
                int type = ParseByte(line, 7, lineNumber);

                int sum = 0;
                for (int i = 1; i < line.Length - 2; i += 2)
                {
                    sum += ParseByte(line, i, lineNumber);
                }
                int checksum = ParseByte(line, line.Length - 2, lineNumber);
                if (((sum + checksum) & 0xFF) != 0)
                {
                    throw Error(lineNumber, "checksum mismatch");
                }

                var data = new byte[count];
                for (int i = 0; i < count; i++)
                {
                    data[i] = (byte)ParseByte(line, 9 + i * 2, lineNumber);
                }

                switch (type)
                {
                    case 0x00: // data
                    {
                        long absolute = baseAddress + (uint)address;
                        if (absolute + data.Length > 0x10000)
                        {
                            throw Error(lineNumber, "data address exceeds 64K");
                        }
                        if (data.Length > 0)
                        {
                            image.Add((ushort)absolute, data);
                        }
                        break;
                    }

                    case 0x01: // end of file
                        if (count != 0)
                        {
                            throw Error(lineNumber, "end-of-file record must be empty");
                        }
                        sawEof = true;
                        break;

                    case 0x02: // extended segment address
                        if (count != 2)
                        {
                            throw Error(lineNumber, "extended segment address needs 2 data bytes");
                        }
                        baseAddress = (uint)((data[0] << 8) | data[1]) << 4;
                        break;

                    case 0x03: // start segment address
                        if (count != 4)
                        {
                            throw Error(lineNumber, "start segment address needs 4 data bytes");
                        }
                        image.StartAddress = ToStartAddress(
                            (uint)((data[0] << 8) | data[1]) << 4 |
                            (uint)((data[2] << 8) | data[3]),
                            lineNumber);
                        break;

                    case 0x04: // extended linear address
                        if (count != 2)
                        {
                            throw Error(lineNumber, "extended linear address needs 2 data bytes");
                        }
                        baseAddress = (uint)((data[0] << 8) | data[1]) << 16;
                        break;

                    case 0x05: // start linear address
                        if (count != 4)
                        {
                            throw Error(lineNumber, "start linear address needs 4 data bytes");
                        }
                        image.StartAddress = ToStartAddress(
                            (uint)((data[0] << 24) | (data[1] << 16) | (data[2] << 8) | data[3]),
                            lineNumber);
                        break;

                    default:
                        throw Error(lineNumber, "unknown record type " + type.ToString("X2"));
                }

                recordCount++;
            }

            if (recordCount == 0)
            {
                throw new FormatException("Intel HEX image contains no records.");
            }
            if (!sawEof)
            {
                throw new FormatException("Intel HEX image has no end-of-file record.");
            }

            return image;
        }

        public static IntelHexImage Load(string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            return Parse(File.ReadAllText(path));
        }

        /// <summary>
        /// Writes a memory range as Intel HEX. The start-address record is
        /// optional because some 8080 tools reject record type 05.
        /// </summary>
        public static string Write(
            byte[] memory,
            ushort startAddress,
            ushort endAddress,
            int recordSize = 16,
            bool includeStartAddress = false)
        {
            if (memory == null) throw new ArgumentNullException(nameof(memory));
            if (memory.Length < 0x10000)
                throw new ArgumentException("Memory dump must contain 64 KiB.", nameof(memory));
            if (recordSize < 1 || recordSize > 255)
                throw new ArgumentOutOfRangeException(nameof(recordSize));
            if (startAddress > endAddress)
                throw new ArgumentException("Start address must not exceed the end address.");

            var builder = new StringBuilder();
            int address = startAddress;
            int last = endAddress;

            while (address <= last)
            {
                int count = Math.Min(recordSize, last - address + 1);
                int sum = count + ((address >> 8) & 0xFF) + (address & 0xFF);

                builder.Append(':');
                AppendByte(builder, (byte)count);
                AppendWord(builder, (ushort)address);
                AppendByte(builder, 0x00);
                for (int i = 0; i < count; i++)
                {
                    byte value = memory[address + i];
                    AppendByte(builder, value);
                    sum += value;
                }
                AppendByte(builder, (byte)(-sum & 0xFF));
                builder.Append("\r\n");

                address += count;
            }

            if (includeStartAddress)
            {
                int high = (startAddress >> 8) & 0xFF;
                int low = startAddress & 0xFF;
                int sum = 4 + 0x05 + high + low;
                builder.Append(":04000005");
                builder.Append("0000");
                AppendWord(builder, startAddress);
                AppendByte(builder, (byte)(-sum & 0xFF));
                builder.Append("\r\n");
            }

            builder.Append(":00000001FF\r\n");
            return builder.ToString();
        }

        public static void Save(
            string path,
            byte[] memory,
            ushort startAddress,
            ushort endAddress,
            bool includeStartAddress = false)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            File.WriteAllText(path, Write(memory, startAddress, endAddress,
                includeStartAddress: includeStartAddress), new UTF8Encoding(false));
        }

        private static ushort ToStartAddress(uint address, int lineNumber)
        {
            if (address > 0xFFFF)
            {
                throw Error(lineNumber, "start address exceeds 64K");
            }
            return (ushort)address;
        }

        private static int ParseByte(string line, int offset, int lineNumber)
        {
            if (!byte.TryParse(line.Substring(offset, 2), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out byte value))
            {
                throw Error(lineNumber, "invalid hexadecimal byte");
            }
            return value;
        }

        private static int ParseWord(string line, int offset, int lineNumber)
        {
            if (!ushort.TryParse(line.Substring(offset, 4), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out ushort value))
            {
                throw Error(lineNumber, "invalid hexadecimal address");
            }
            return value;
        }

        private static void AppendByte(StringBuilder builder, byte value)
        {
            builder.Append(value.ToString("X2", CultureInfo.InvariantCulture));
        }

        private static void AppendWord(StringBuilder builder, ushort value)
        {
            builder.Append(value.ToString("X4", CultureInfo.InvariantCulture));
        }

        private static FormatException Error(int lineNumber, string message)
        {
            return new FormatException("Intel HEX, line " + lineNumber + ": " + message + ".");
        }
    }
}
