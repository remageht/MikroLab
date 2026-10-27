using System;
using System.Collections.Generic;
using MikroLab.Core.Cpu;
using MikroLab.Core.Devices;
using MikroLab.Core.Memory;

namespace MikroLab.Core.Machine
{
    public sealed class Machine
    {
        public NibbleMemory Memory { get; }
        public ICpu Cpu { get; }
        public PpiState Ppi { get; }
        public DisplayState Display { get; }
        public AnalogState Analog { get; }

        public RunState State { get; set; } = RunState.Stopped;

        public ISound Sound { get; set; }
        public IClock Clock { get; set; }
        public IDiagnostics Diagnostics { get; set; }

        public Machine(
            ISound sound = null,
            IClock clock = null,
            IDiagnostics diagnostics = null,
            CpuProfile profile = CpuProfile.Legacy)
        {
            Sound = sound;
            Clock = clock;
            Diagnostics = diagnostics;

            Memory = new NibbleMemory();
            Ppi = new PpiState();
            Display = new DisplayState();
            Analog = new AnalogState();

            if (profile == CpuProfile.Extended)
            {
                Cpu = new ExtendedCpu(
                    Memory,
                    Ppi,
                    Display,
                    Analog,
                    Sound,
                    Clock,
                    Diagnostics);
            }
            else
            {
                Cpu = new LegacyCpu(
                    Memory,
                    Ppi,
                    Display,
                    Analog,
                    Sound,
                    Clock,
                    Diagnostics);
            }
        }

        public bool Step()
        {
            bool ok = Cpu.Step();
            if (!ok)
            {
                State = RunState.Halted;
            }
            return ok;
        }

        public void Reset(ResetKind kind = ResetKind.Soft)
        {
            Cpu.Reset();
            Ppi.Reset();
            Display.Reset();

            if (kind == ResetKind.Hard)
            {
                Memory.Reset();
            }
            State = RunState.Stopped;
        }

        public MachineSnapshot Capture()
        {
            var cpuState = Cpu.State;
            IReadOnlyList<ushort> stack = Cpu.GetCallStack();
            var callStack = new ushort[stack.Count];
            for (int i = 0; i < stack.Count; i++)
            {
                callStack[i] = stack[i];
            }

            bool halted = State == RunState.Halted ||
                (Cpu is ExtendedCpu extended && extended.IsHalted);

            return new MachineSnapshot
            {
                A = cpuState.A,
                B = cpuState.B,
                C = cpuState.C,
                D = cpuState.D,
                E = cpuState.E,
                H = cpuState.H,
                L = cpuState.L,
                F = cpuState.F,
                M = cpuState.M,
                PC = cpuState.PC,
                SP = cpuState.SP,
                Cycles = cpuState.Cycles,
                Flags = cpuState.Flags,
                CallStackDepth = Cpu.CallStackDepth,
                CallStack = callStack,
                Halted = halted,

                PpiLamps = Ppi.Lamps,
                PpiControlWord = Ppi.ControlWord,
                PpiInputWord = Ppi.InputWord,
                Tumbler1 = Ppi.Tumbler1,
                Tumbler2 = Ppi.Tumbler2,
                Tumbler3 = Ppi.Tumbler3,

                Digits = Display.CaptureDigits(),
                AnalogVoltages = Analog.CaptureVoltages(),
                SelectedAnalogChannel = Analog.SelectedMuxChannel,
                AdcValue = Analog.ReadAdc(),

                State = this.State,
                Memory = Memory.Capture()
            };
        }

        public void Restore(MachineSnapshot snapshot)
        {
            if (snapshot == null) return;
            if (snapshot.Memory != null)
                Memory.Restore(snapshot.Memory);

            Cpu.State.A = snapshot.A;
            Cpu.State.B = snapshot.B;
            Cpu.State.C = snapshot.C;
            Cpu.State.D = snapshot.D;
            Cpu.State.E = snapshot.E;
            Cpu.State.H = snapshot.H;
            Cpu.State.L = snapshot.L;
            Cpu.State.F = snapshot.F;
            Cpu.State.PC = snapshot.PC;
            Cpu.State.SP = snapshot.SP;
            Cpu.State.Cycles = snapshot.Cycles;
            Cpu.State.Flags = snapshot.Flags;

            Ppi.Lamps = snapshot.PpiLamps;
            Ppi.ControlWord = snapshot.PpiControlWord;
            Ppi.InputWord = snapshot.PpiInputWord;
            Ppi.Tumbler1 = snapshot.Tumbler1;
            Ppi.Tumbler2 = snapshot.Tumbler2;
            Ppi.Tumbler3 = snapshot.Tumbler3;

            if (snapshot.Digits != null)
            {
                for (int i = 0; i < Math.Min(8, snapshot.Digits.Length); i++)
                    Display.WriteDigit(i, snapshot.Digits[i]);
            }

            if (snapshot.AnalogVoltages != null)
            {
                for (int i = 0; i < Math.Min(4, snapshot.AnalogVoltages.Length); i++)
                    Analog.SetChannel(i, snapshot.AnalogVoltages[i]);
            }
            Analog.SelectedMuxChannel = snapshot.SelectedAnalogChannel;

            if (Cpu is ExtendedCpu extended)
            {
                extended.RestoreDebugState(snapshot.CallStack, snapshot.Halted);
            }

            // A loaded snapshot has no execution thread behind it. Never report
            // Running for a machine that is not actually executing, and let HLT
            // win over any stale run state.
            if (snapshot.Halted)
            {
                this.State = RunState.Halted;
            }
            else if (snapshot.State == RunState.Running)
            {
                this.State = RunState.Paused;
            }
            else
            {
                this.State = snapshot.State;
            }
        }
    }
}
