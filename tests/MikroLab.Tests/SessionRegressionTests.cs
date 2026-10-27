using System;
using System.Diagnostics;
using System.Threading;
using MikroLab.Core.Cpu;
using MikroLab.Core.Devices;
using MikroLab.Core.Machine;

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
