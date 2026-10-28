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
        public MappedMemory AddressSpace { get; }
        public IoBus IoBus { get; }
        public ICpu Cpu { get; }
        public Ppi8255 Ppi { get; }
        public PpiPortMap PpiMap { get; }
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
            CpuProfile profile = CpuProfile.Legacy,
            PpiPortMap ppiMap = null)
        {
            Sound = sound;
            Clock = clock;
            Diagnostics = diagnostics;

            Memory = new NibbleMemory();
            AddressSpace = new MappedMemory(Memory);
            Ppi = new Ppi8255();
            PpiMap = (ppiMap ?? PpiPortMap.Default).Clone();
            PpiMap.Validate();
            Display = new DisplayState();
            Analog = new AnalogState();
            IoBus = new IoBus();

            RegisterPpiDevices();

            var analogDevice = new AnalogDevice(Analog);
            IoBus.Register(analogDevice, AnalogDevice.PortMux, AnalogDevice.PortAdc);
            AddressSpace.Register(
                analogDevice,
                AnalogDevice.PortAdc,
                AnalogDevice.PortAdc,
                MemoryAccess.Read);
            AddressSpace.Register(
                analogDevice,
                AnalogDevice.PortMux,
                AnalogDevice.PortMux,
                MemoryAccess.Write);

            var displayDevice = new DisplayMemoryDevice(
                Display,
                MemoryMap.DisplayLatchStart,
                MemoryMap.DisplayLatchEnd);
            AddressSpace.Register(
                displayDevice,
                MemoryMap.DisplayLatchStart,
                MemoryMap.DisplayLatchEnd,
                MemoryAccess.ReadWrite);

            if (profile == CpuProfile.Extended)
            {
                Cpu = new ExtendedCpu(AddressSpace, IoBus, Sound, Clock, Diagnostics);
            }
            else
            {
                Cpu = new LegacyCpu(AddressSpace, IoBus, Sound, Clock, Diagnostics);
            }
        }

        private void RegisterPpiDevices()
        {
            var device = new Ppi8255Device(Ppi, PpiMap);

            // IN/OUT bus: four primary ports plus optional VNTU aliases.
            IoBus.Register(device, PpiMap.PortA, PpiMap.PortA);
            IoBus.Register(device, PpiMap.PortB, PpiMap.PortB);
            IoBus.Register(device, PpiMap.PortC, PpiMap.PortC);
            IoBus.Register(device, PpiMap.Control, PpiMap.Control);
            if (PpiMap.LegacyPortB >= 0)
            {
                IoBus.Register(device, PpiMap.LegacyPortB, PpiMap.LegacyPortB);
            }
            if (PpiMap.LegacyControl >= 0)
            {
                IoBus.Register(device, PpiMap.LegacyControl, PpiMap.LegacyControl);
            }

            // Memory-mapped compatibility windows. Port A is readable at its
            // primary address; the other primary ports and legacy aliases are
            // write-only so program data reads are not hijacked unnecessarily.
            AddressSpace.Register(device, PpiMap.PortA, PpiMap.PortA, MemoryAccess.ReadWrite);
            AddressSpace.Register(device, PpiMap.PortB, PpiMap.PortB, MemoryAccess.Write);
            AddressSpace.Register(device, PpiMap.PortC, PpiMap.PortC, MemoryAccess.Write);
            AddressSpace.Register(device, PpiMap.Control, PpiMap.Control, MemoryAccess.Write);
            if (PpiMap.LegacyPortB >= 0)
            {
                AddressSpace.Register(device, PpiMap.LegacyPortB, PpiMap.LegacyPortB, MemoryAccess.Write);
            }
            if (PpiMap.LegacyControl >= 0)
            {
                AddressSpace.Register(device, PpiMap.LegacyControl, PpiMap.LegacyControl, MemoryAccess.Write);
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

            Ppi.RefreshKeyboard();

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

                PpiPortA = Ppi.PortA,
                PpiPortB = Ppi.PortB,
                PpiPortC = Ppi.PortC,
                PpiControlWord = Ppi.ControlWord,
                PpiInputA = Ppi.InputA,
                PpiInputB = Ppi.InputB,
                PpiInputC = Ppi.InputC,
                PpiModeA = Ppi.GroupAMode,
                PpiModeB = Ppi.GroupBMode,
                PpiPortAInput = Ppi.PortAInput,
                PpiPortBInput = Ppi.PortBInput,
                PpiPortCUpperInput = Ppi.PortCUpperInput,
                PpiPortCLowerInput = Ppi.PortCLowerInput,
                PpiPortABidirectional = Ppi.PortABidirectional,
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
            Cpu.State.M = snapshot.M;
            Cpu.State.F = snapshot.F;
            Cpu.State.PC = snapshot.PC;
            Cpu.State.SP = snapshot.SP;
            Cpu.State.Cycles = snapshot.Cycles;
            Cpu.State.Flags = snapshot.Flags;

            Ppi.RestoreState(
                snapshot.PpiPortA,
                snapshot.PpiPortB,
                snapshot.PpiPortC,
                snapshot.PpiControlWord,
                snapshot.PpiInputA,
                snapshot.PpiInputB,
                snapshot.PpiInputC);

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
