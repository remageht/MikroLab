using System;
using MikroLab.Core.Cpu;
using MikroLab.Core.Devices;

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

        /// <summary>Debug call stack (return addresses), top first.</summary>
        public ushort[] CallStack { get; init; } = Array.Empty<ushort>();

        /// <summary>
        /// CPU profile the snapshot was taken under. Null for older state files
        /// and .580 images, which do not record a profile; restoring those keeps
        /// the current CPU.
        /// </summary>
        public CpuProfile? Profile { get; init; }

        /// <summary>
        /// Full Legacy call frames (register snapshot + flags + return address),
        /// top first. Legacy keeps returns in a software stack rather than memory,
        /// so these are required to resume a Legacy program mid-CALL.
        /// </summary>
        public LegacyCallFrame[] LegacyCallStack { get; init; }

        /// <summary>True when the CPU is latched on HLT.</summary>
        public bool Halted { get; init; }

        // Devices
        public byte PpiPortA { get; init; }
        public byte PpiPortB { get; init; }
        public byte PpiPortC { get; init; }
        public byte PpiControlWord { get; init; } = Ppi8255.ResetControlWord;
        public byte PpiInputA { get; init; }
        public byte PpiInputB { get; init; }
        public byte PpiInputC { get; init; }
        public PpiMode PpiModeA { get; init; }
        public PpiMode PpiModeB { get; init; }
        public bool PpiPortAInput { get; init; }
        public bool PpiPortBInput { get; init; }
        public bool PpiPortCUpperInput { get; init; }
        public bool PpiPortCLowerInput { get; init; }
        public bool PpiPortABidirectional { get; init; }
        public bool Tumbler1 { get; init; }
        public bool Tumbler2 { get; init; }
        public bool Tumbler3 { get; init; }

        /// <summary>Effective value seen by an IN from port A.</summary>
        public byte PpiEffectiveA => (PpiPortAInput || PpiPortABidirectional) ? PpiInputA : PpiPortA;

        /// <summary>Effective value seen by an IN from port B.</summary>
        public byte PpiEffectiveB => PpiPortBInput ? PpiInputB : PpiPortB;

        /// <summary>Effective value seen by an IN from port C.</summary>
        public byte PpiEffectiveC
        {
            get
            {
                byte value = PpiPortCUpperInput ? (byte)(PpiInputC & 0xF0) : (byte)(PpiPortC & 0xF0);
                value |= PpiPortCLowerInput ? (byte)(PpiInputC & 0x0F) : (byte)(PpiPortC & 0x0F);
                return value;
            }
        }

        public byte[] Digits { get; init; }
        public double[] AnalogVoltages { get; init; }
        public byte SelectedAnalogChannel { get; init; }
        public byte AdcValue { get; init; }

        // Machine
        public RunState State { get; init; }
        public byte[] Memory { get; init; }
    }

    /// <summary>
    /// One Legacy software-stack frame. Mirrors <c>LegacyCpu.CallStackElement</c>
    /// in a serializable form so a saved Legacy program can resume inside a CALL.
    /// </summary>
    public sealed class LegacyCallFrame
    {
        public ushort[] Regs { get; set; } = new ushort[8];
        public FlagReg Flags { get; set; }
        public ushort RetAddress { get; set; }
    }
}
