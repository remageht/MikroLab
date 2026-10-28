using System;
using System.IO;
using System.Text;
using MikroLab.Core.Cpu;
using MikroLab.Core.Machine;

namespace MikroLab.Core.Debugging
{
    /// <summary>Supported execution-log export formats.</summary>
    public enum ExecutionLogFormat
    {
        Csv,
        Markdown
    }

    /// <summary>
    /// One executed instruction. Registers and flags are captured after the
    /// instruction, <see cref="Cycles"/> is the cost of this instruction and
    /// <see cref="TotalCycles"/> is the machine cycle counter after it.
    /// </summary>
    public sealed class ExecutionLogEntry
    {
        public long Step { get; set; }
        public ushort Address { get; init; }
        public byte[] Bytes { get; init; } = Array.Empty<byte>();
        public string Instruction { get; init; } = string.Empty;

        public byte A { get; init; }
        public byte B { get; init; }
        public byte C { get; init; }
        public byte D { get; init; }
        public byte E { get; init; }
        public byte H { get; init; }
        public byte L { get; init; }
        public byte F { get; init; }
        public ushort PC { get; init; }
        public ushort SP { get; init; }

        public ulong Cycles { get; init; }
        public ulong TotalCycles { get; init; }

        public string BytesText
        {
            get
            {
                if (Bytes == null || Bytes.Length == 0) return string.Empty;
                var text = new StringBuilder(Bytes.Length * 3);
                for (int i = 0; i < Bytes.Length; i++)
                {
                    if (i > 0) text.Append(' ');
                    text.Append(Bytes[i].ToString("X2"));
                }
                return text.ToString();
            }
        }

        public string FlagsText
        {
            get
            {
                var text = new StringBuilder(8);
                AppendFlag(text, "S", (F & 0x80) != 0);
                AppendFlag(text, "Z", (F & 0x40) != 0);
                AppendFlag(text, "AC", (F & 0x10) != 0);
                AppendFlag(text, "P", (F & 0x04) != 0);
                AppendFlag(text, "C", (F & 0x01) != 0);
                return text.Length == 0 ? "-" : text.ToString();
            }
        }

        public string RegistersText =>
            "A=" + A.ToString("X2") + " B=" + B.ToString("X2") +
            " C=" + C.ToString("X2") + " D=" + D.ToString("X2") +
            " E=" + E.ToString("X2") + " H=" + H.ToString("X2") +
            " L=" + L.ToString("X2") + " F=" + F.ToString("X2") +
            " PC=" + PC.ToString("X4") + " SP=" + SP.ToString("X4");

        public string ShortText =>
            Step.ToString("D5") + "  " + Address.ToString("X4") + "  " +
            BytesText + "  " + Instruction;

        public override string ToString() => ShortText;

        private static void AppendFlag(StringBuilder text, string name, bool set)
        {
            if (!set) return;
            if (text.Length > 0) text.Append(' ');
            text.Append(name);
        }
    }

    /// <summary>
    /// Thread-safe bounded ring of executed instructions. The newest entry wins
    /// when the capacity is exceeded; CSV and Markdown exports always contain
    /// the entries that are currently retained.
    /// </summary>
    public sealed class ExecutionLog
    {
        public const int DefaultCapacity = 10000;

        private readonly object _sync = new object();
        private readonly ExecutionLogEntry[] _items;
        private int _start;
        private int _count;
        private long _step;
        private long _revision;

        public ExecutionLog(int capacity = DefaultCapacity)
        {
            if (capacity < 1)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            _items = new ExecutionLogEntry[capacity];
        }

        public int Capacity => _items.Length;

        public int Count
        {
            get
            {
                lock (_sync) return _count;
            }
        }

        public bool IsEmpty => Count == 0;

        /// <summary>Increments on every append and clear; lets UI refresh lazily.</summary>
        public long Revision
        {
            get
            {
                lock (_sync) return _revision;
            }
        }

        public ExecutionLogEntry Append(ExecutionLogEntry entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));

            lock (_sync)
            {
                entry.Step = ++_step;
                if (_count < _items.Length)
                {
                    _items[(_start + _count) % _items.Length] = entry;
                    _count++;
                }
                else
                {
                    _items[_start] = entry;
                    _start = (_start + 1) % _items.Length;
                }
                _revision++;
            }
            return entry;
        }

        public ExecutionLogEntry Append(
            ushort address,
            DisassembledInstruction instruction,
            MachineSnapshot snapshot,
            ulong instructionCycles)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            return Append(new ExecutionLogEntry
            {
                Address = address,
                Bytes = instruction.GetBytes(),
                Instruction = instruction.Text,
                A = snapshot.A,
                B = snapshot.B,
                C = snapshot.C,
                D = snapshot.D,
                E = snapshot.E,
                H = snapshot.H,
                L = snapshot.L,
                F = snapshot.Flags.ToByte(),
                PC = snapshot.PC,
                SP = snapshot.SP,
                Cycles = instructionCycles,
                TotalCycles = snapshot.Cycles
            });
        }

        /// <summary>
        /// Lightweight append used by the execution loop: reads registers from
        /// the live CPU state instead of cloning the 64 KiB memory snapshot.
        /// </summary>
        public ExecutionLogEntry Append(
            ushort address,
            DisassembledInstruction instruction,
            CpuState state,
            ulong instructionCycles)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));

            return Append(new ExecutionLogEntry
            {
                Address = address,
                Bytes = instruction.GetBytes(),
                Instruction = instruction.Text,
                A = state.A,
                B = state.B,
                C = state.C,
                D = state.D,
                E = state.E,
                H = state.H,
                L = state.L,
                F = state.Flags.ToByte(),
                PC = state.PC,
                SP = state.SP,
                Cycles = instructionCycles,
                TotalCycles = state.Cycles
            });
        }

        public ExecutionLogEntry[] GetLast(int maximum)
        {
            lock (_sync)
            {
                int take = Math.Max(0, Math.Min(maximum, _count));
                var result = new ExecutionLogEntry[take];
                int first = _count - take;
                for (int i = 0; i < take; i++)
                {
                    result[i] = _items[(_start + first + i) % _items.Length];
                }
                return result;
            }
        }

        public ExecutionLogEntry[] ToArray()
        {
            return GetLast(int.MaxValue);
        }

        public void Clear()
        {
            lock (_sync)
            {
                Array.Clear(_items, 0, _items.Length);
                _start = 0;
                _count = 0;
                _step = 0;
                _revision++;
            }
        }

        public string ToCsv()
        {
            return BuildCsv(ToArray());
        }

        public string ToMarkdown()
        {
            return BuildMarkdown(ToArray());
        }

        public void Save(Stream stream, ExecutionLogFormat format)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanWrite) throw new ArgumentException("The stream must be writable.", nameof(stream));

            string text = format == ExecutionLogFormat.Csv ? ToCsv() : ToMarkdown();
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, leaveOpen: true))
            {
                writer.Write(text);
            }
        }

        public void Save(string path, ExecutionLogFormat format)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            using (FileStream stream = File.Create(path))
            {
                Save(stream, format);
            }
        }

        private static string BuildCsv(ExecutionLogEntry[] entries)
        {
            var text = new StringBuilder(entries.Length * 96 + 128);
            text.Append("Step,Address,Bytes,Instruction,A,B,C,D,E,H,L,F,PC,SP,Flags,Cycles,TotalCycles");
            text.Append(Environment.NewLine);

            foreach (ExecutionLogEntry entry in entries)
            {
                text.Append(entry.Step.ToString());
                text.Append(',');
                text.Append(entry.Address.ToString("X4"));
                text.Append(',');
                text.Append(Csv(entry.BytesText));
                text.Append(',');
                text.Append(Csv(entry.Instruction));
                text.Append(',');
                text.Append(entry.A.ToString("X2"));
                text.Append(',');
                text.Append(entry.B.ToString("X2"));
                text.Append(',');
                text.Append(entry.C.ToString("X2"));
                text.Append(',');
                text.Append(entry.D.ToString("X2"));
                text.Append(',');
                text.Append(entry.E.ToString("X2"));
                text.Append(',');
                text.Append(entry.H.ToString("X2"));
                text.Append(',');
                text.Append(entry.L.ToString("X2"));
                text.Append(',');
                text.Append(entry.F.ToString("X2"));
                text.Append(',');
                text.Append(entry.PC.ToString("X4"));
                text.Append(',');
                text.Append(entry.SP.ToString("X4"));
                text.Append(',');
                text.Append(Csv(entry.FlagsText));
                text.Append(',');
                text.Append(entry.Cycles.ToString());
                text.Append(',');
                text.Append(entry.TotalCycles.ToString());
                text.Append(Environment.NewLine);
            }

            return text.ToString();
        }

        private static string BuildMarkdown(ExecutionLogEntry[] entries)
        {
            var text = new StringBuilder(entries.Length * 120 + 256);
            text.Append("| Step | Address | Bytes | Instruction | A | B | C | D | E | H | L | F | PC | SP | Flags | Cycles | Total |");
            text.Append(Environment.NewLine);
            text.Append("|---:|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---|---|---|---:|---:|");
            text.Append(Environment.NewLine);

            foreach (ExecutionLogEntry entry in entries)
            {
                text.Append("| ");
                text.Append(entry.Step.ToString());
                text.Append(" | ");
                text.Append(entry.Address.ToString("X4"));
                text.Append(" | ");
                text.Append(Markdown(entry.BytesText));
                text.Append(" | ");
                text.Append(Markdown(entry.Instruction));
                text.Append(" | ");
                text.Append(entry.A.ToString("X2"));
                text.Append(" | ");
                text.Append(entry.B.ToString("X2"));
                text.Append(" | ");
                text.Append(entry.C.ToString("X2"));
                text.Append(" | ");
                text.Append(entry.D.ToString("X2"));
                text.Append(" | ");
                text.Append(entry.E.ToString("X2"));
                text.Append(" | ");
                text.Append(entry.H.ToString("X2"));
                text.Append(" | ");
                text.Append(entry.L.ToString("X2"));
                text.Append(" | ");
                text.Append(entry.F.ToString("X2"));
                text.Append(" | ");
                text.Append(entry.PC.ToString("X4"));
                text.Append(" | ");
                text.Append(entry.SP.ToString("X4"));
                text.Append(" | ");
                text.Append(Markdown(entry.FlagsText));
                text.Append(" | ");
                text.Append(entry.Cycles.ToString());
                text.Append(" | ");
                text.Append(entry.TotalCycles.ToString());
                text.Append(" |");
                text.Append(Environment.NewLine);
            }

            return text.ToString();
        }

        private static string Csv(string value)
        {
            value = value ?? string.Empty;
            if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static string Markdown(string value)
        {
            return (value ?? string.Empty).Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
        }
    }
}
