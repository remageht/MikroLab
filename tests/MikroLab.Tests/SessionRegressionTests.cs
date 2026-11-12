using System;
using System.Diagnostics;
using System.Threading;
using MikroLab.Core.Cpu;
using MikroLab.Core.Devices;
using MikroLab.Core.Machine;
using MikroLab.Presentation;

namespace MikroLab.Tests
{
    public static class SessionRegressionTests
    {
        public static void RunAll()
        {
            Console.WriteLine("[TEST SUITE] SessionRegressionTests (execution control)");
            Test_PausePreservesHalted();
            Test_PausePreservesBreakpoint();
            Test_BreakpointAtStartIsNotSkipped();
            Test_InterruptibleClock();
            Test_TargetSpeed();
            Test_DisposedSessionThrows();
            Test_SwitchProfileResetsCallContext();
            Test_SwitchProfilePreservesMemoryAndRegisters();
            Test_SwitchProfileStopsRunningMachine();
            Test_SwitchProfileHostRestoresEntryPoint();
            Console.WriteLine("  All SessionRegressionTests PASSED.\n");
        }

        private static void Test_PausePreservesHalted()
        {
            var machine = new Machine(profile: CpuProfile.Extended);
            machine.Memory.WriteByte(0x8000, 0x76); // HLT
            machine.Cpu.State.PC = 0x8000;

            using var session = new MachineSession(machine);
            session.Run();
            Assert(WaitUntil(() => session.State == RunState.Halted, 2000),
                "Program should reach Halted");

            session.Pause();
            Assert(session.State == RunState.Halted, "Pause must not overwrite Halted");
            Console.WriteLine("  [PASS] Pause preserves Halted");
        }

        private static void Test_PausePreservesBreakpoint()
        {
            var machine = new Machine(profile: CpuProfile.Extended);
            machine.Memory.WriteByte(0x8000, 0xC3); // JMP 8000h
            machine.Memory.WriteByte(0x8001, 0x00);
            machine.Memory.WriteByte(0x8002, 0x80);
            machine.Cpu.State.PC = 0x8000;

            using var session = new MachineSession(machine);
            session.SetBreakpoints(new ushort[] { 0x8000 });
            session.Run();
            Assert(WaitUntil(() => session.IsPausedAtBreakpoint, 2000),
                "Breakpoint should stop execution");

            session.Pause();
            Assert(session.State == RunState.Paused, "Machine should stay paused");
            Assert(session.IsPausedAtBreakpoint, "Pause must not clear the breakpoint marker");
            Assert(session.BreakpointAddress == 0x8000, "Breakpoint address mismatch");
            Console.WriteLine("  [PASS] Pause preserves breakpoint pause");
        }

        private static void Test_BreakpointAtStartIsNotSkipped()
        {
            var machine = new Machine(profile: CpuProfile.Extended);
            machine.Memory.WriteByte(0x8000, 0x00); // NOP
            machine.Memory.WriteByte(0x8001, 0x76); // HLT
            machine.Cpu.State.PC = 0x8000;

            using var session = new MachineSession(machine);
            session.SetBreakpoints(new ushort[] { 0x8000 });
            session.Run();
            Assert(WaitUntil(() => session.IsPausedAtBreakpoint, 2000),
                "A breakpoint at the current PC must stop the first run");
            Assert(session.State == RunState.Paused, "Breakpoint must pause the machine");
            Assert(machine.Cpu.State.PC == 0x8000, "Machine must stop before the NOP");

            session.Run();
            Assert(WaitUntil(() => session.State == RunState.Halted, 2000),
                "Resuming must execute the instruction under PC and continue");
            Console.WriteLine("  [PASS] Breakpoint at current PC is not skipped on first run");
        }

        private static void Test_InterruptibleClock()
        {
            var clock = new StoppingClock();
            var machine = new Machine(clock: clock, profile: CpuProfile.Extended);
            machine.Memory.WriteByte(0x8000, 0xCD); // CALL 0400h
            machine.Memory.WriteByte(0x8001, 0x00);
            machine.Memory.WriteByte(0x8002, 0x04);
            machine.Memory.WriteByte(0x8003, 0x76); // HLT
            machine.Cpu.State.PC = 0x8000;

            using var session = new MachineSession(machine);
            session.Run();
            Assert(WaitUntil(() => clock.Entered, 2000), "Monitor delay should be entered");

            var watch = Stopwatch.StartNew();
            session.Pause();
            watch.Stop();

            Assert(watch.ElapsedMilliseconds < 500,
                "Pause must interrupt a monitor delay quickly (took " + watch.ElapsedMilliseconds + " ms)");
            Assert(session.State == RunState.Paused, "Machine should be paused after interrupting a delay");
            Console.WriteLine("  [PASS] Monitor delay is interruptible");
        }

        private static void Test_TargetSpeed()
        {
            var machine = new Machine(profile: CpuProfile.Extended);
            machine.Memory.WriteByte(0x8000, 0xC3); // JMP 8000h
            machine.Memory.WriteByte(0x8001, 0x00);
            machine.Memory.WriteByte(0x8002, 0x80);
            machine.Cpu.State.PC = 0x8000;

            using var session = new MachineSession(machine);
            session.ThrottleLikeOriginal = false;
            session.TargetInstructionsPerSecond = 1000;
            session.Run();
            Thread.Sleep(180);

            var watch = Stopwatch.StartNew();
            session.Pause();
            watch.Stop();

            Assert(session.State == RunState.Paused, "Rate-limited session should pause");
            Assert(watch.ElapsedMilliseconds < 500, "Rate-limited pause must stay responsive");
            Assert(machine.Cpu.State.Cycles > 0, "Rate-limited session must execute instructions");
            Assert(machine.Cpu.State.Cycles < 1000,
                "Rate limit should keep cycles bounded, got " + machine.Cpu.State.Cycles);
            Console.WriteLine("  [PASS] Target instructions per second is applied");
        }

        private static void Test_DisposedSessionThrows()
        {
            var session = new MachineSession(new Machine(profile: CpuProfile.Extended));
            session.Dispose();

            try
            {
                session.Run();
            }
            catch (ObjectDisposedException)
            {
                Console.WriteLine("  [PASS] Disposed session rejects Run");
                return;
            }

            throw new InvalidOperationException("Disposed MachineSession.Run should throw ObjectDisposedException");
        }

        private static void WriteCallProgram(Machine machine)
        {
            // 8000: CALL 8010h · 8003: HLT · 8010: MVI A,42h · 8012: RET
            machine.Memory.WriteByte(0x8000, 0xCD);
            machine.Memory.WriteByte(0x8001, 0x10);
            machine.Memory.WriteByte(0x8002, 0x80);
            machine.Memory.WriteByte(0x8003, 0x76);
            machine.Memory.WriteByte(0x8010, 0x3E);
            machine.Memory.WriteByte(0x8011, 0x42);
            machine.Memory.WriteByte(0x8012, 0xC9);
            machine.Cpu.State.PC = 0x8000;
            machine.Cpu.State.SP = 0x83F0;
        }

        private static void Test_SwitchProfileResetsCallContext()
        {
            foreach (CpuProfile from in new[] { CpuProfile.Extended, CpuProfile.Legacy })
            {
                CpuProfile to = from == CpuProfile.Extended ? CpuProfile.Legacy : CpuProfile.Extended;
                var machine = new Machine(profile: from);
                WriteCallProgram(machine);

                using var session = new MachineSession(machine);
                session.ThrottleLikeOriginal = false;

                session.Step(); // CALL -> inside the subroutine
                session.Step(); // MVI A,42h
                Assert(machine.Cpu.CallStackDepth == 1, "must be inside a CALL before switching");
                Assert(machine.Cpu.State.A == 0x42, "MVI must have executed");
                Assert(machine.Cpu.State.PC == 0x8012, "must be mid-subroutine before switching");

                session.SwitchProfile(to);
                Assert(machine.Cpu.Profile == to, "profile must change");
                Assert(machine.Cpu.CallStackDepth == 0, "switch must clear the incompatible call stack");
                Assert(session.State == RunState.Stopped, "switch must leave the machine stopped");
                Assert(machine.Cpu.State.PC == 0x8000,
                    "switch must reset PC to the start, not leave it mid-call with a mismatched stack");

                // Restore the profile stack (as MachineHost does) and run to HLT.
                machine.Cpu.State.SP = 0x83F0;
                session.Run();
                Assert(WaitUntil(() => session.State == RunState.Halted, 2000),
                    "re-run after switch must reach HLT (RET must work under the new profile)");
                Assert(machine.Cpu.State.A == 0x42, "subroutine must run again after the switch");
            }

            Console.WriteLine("  [PASS] Switching profile mid-CALL resets the call context and re-runs cleanly");
        }

        private static void Test_SwitchProfilePreservesMemoryAndRegisters()
        {
            var machine = new Machine(profile: CpuProfile.Extended);
            machine.Memory.WriteByte(0x8200, 0xAB);
            machine.Cpu.State.A = 0x55;
            machine.Cpu.State.B = 0x66;

            using var session = new MachineSession(machine);
            session.SwitchProfile(CpuProfile.Legacy);

            Assert(machine.Memory.ReadByte(0x8200) == 0xAB, "memory must survive a profile switch");
            Assert(machine.Cpu.State.A == 0x55, "register A must survive a profile switch");
            Assert(machine.Cpu.State.B == 0x66, "register B must survive a profile switch");
            Console.WriteLine("  [PASS] Profile switch preserves memory and the register file");
        }

        private static void Test_SwitchProfileStopsRunningMachine()
        {
            var machine = new Machine(profile: CpuProfile.Extended);
            machine.Memory.WriteByte(0x8000, 0xC3); // JMP 8000h (infinite loop)
            machine.Memory.WriteByte(0x8001, 0x00);
            machine.Memory.WriteByte(0x8002, 0x80);
            machine.Cpu.State.PC = 0x8000;

            using var session = new MachineSession(machine);
            session.ThrottleLikeOriginal = false;
            session.Run();
            Assert(WaitUntil(() => session.State == RunState.Running, 2000), "machine must be running");

            session.SwitchProfile(CpuProfile.Legacy);
            Assert(session.State == RunState.Stopped, "switch must stop the machine, not resume it");
            Thread.Sleep(50);
            Assert(session.State == RunState.Stopped, "machine must stay stopped after the switch");
            Console.WriteLine("  [PASS] Profile switch stops a running machine (no auto-resume)");
        }

        private static void Test_SwitchProfileHostRestoresEntryPoint()
        {
            using var host = new MachineHost();
            host.LoadSource("ORG 8000h\nMVI A,01h\nHLT\n");
            Assert(host.CpuProfile == CpuProfile.Extended, "host must default to Extended");

            host.SwitchCpuProfile(CpuProfile.Legacy);
            Assert(host.CpuProfile == CpuProfile.Legacy, "host must switch to Legacy");
            Assert(host.Machine.Cpu.State.PC == 0x8000, "PC must return to the entry point");
            Assert(host.Machine.Cpu.State.SP == 0x83F0, "SP must use the profile stack pointer");
            Assert(host.State == RunState.Stopped, "host must be stopped after the switch");
            Console.WriteLine("  [PASS] Host profile switch restores the entry point and profile stack");
        }

        private static bool WaitUntil(Func<bool> condition, int timeoutMilliseconds)
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeoutMilliseconds)
            {
                if (condition()) return true;
                Thread.Sleep(5);
            }
            return condition();
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException("Assertion failed: " + message);
        }

        private sealed class StoppingClock : IInterruptibleClock
        {
            private readonly ManualResetEventSlim _entered = new ManualResetEventSlim(false);
            private volatile bool _stop;

            public bool Entered => _entered.IsSet;

            public void Delay(int milliseconds)
            {
                _entered.Set();
                while (!_stop)
                {
                    Thread.Sleep(5);
                }
            }

            public void RequestStop()
            {
                _stop = true;
            }

            public void ClearStop()
            {
                _stop = false;
            }
        }
    }
}
