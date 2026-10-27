using System;
using MikroLab.Core.Cpu;

namespace MikroLab.Core.Machine
{
    public class MachineSnapshot
    {
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;

        // CPU
        public byte A { get; init; }
        public byte B { get; init; }
        public byte C { get; init; }
        public byte D { get; init; }
        public byte E { get; init; }
        public byte H { get; init; }
        public byte L { get; init; }
        public byte F { get; init; }
        public byte M { get; init; }
        public ushort PC { get; init; }
        public ushort SP { get; init; }
        public ulong Cycles { get; init; }
        public FlagReg Flags { get; init; }
        public int CallStackDepth { get; init; }

        // Devices
        public byte PpiLamps { get; init; }
        public byte PpiControlWord { get; init; }
        public byte PpiInputWord { get; init; }
        public bool Tumbler1 { get; init; }
        public bool Tumbler2 { get; init; }
        public bool Tumbler3 { get; init; }

        public byte[] Digits { get; init; }
        public double[] AnalogVoltages { get; init; }
        public byte SelectedAnalogChannel { get; init; }
        public byte AdcValue { get; init; }

        // Machine
        public RunState State { get; init; }
        public byte[] Memory { get; init; }
    }
}
