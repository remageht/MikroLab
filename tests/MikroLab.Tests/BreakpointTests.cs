using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using MikroLab.Core.Cpu;
using MikroLab.Core.Machine;
using MikroLab.Core.Memory;
using MikroLab.Presentation;

namespace MikroLab.Tests
{
    /// <summary>
    /// Breakpoint/watchpoint persistence and removal on the portable
    /// <see cref="MachineHost"/>, plus human-readable stop reasons.
    /// </summary>
    public static class BreakpointTests
    {
        private const string Source =
            "        ORG 8000h\n" +
            "START:  MVI A, 11h\n" +
            "        HLT\n" +
            "        END\n";

        private static int _passed;

        public static void RunAll()
        {
            Console.WriteLine("[TEST SUITE] BreakpointTests (persistence, removal, stop reasons)");
            _passed = 0;

            Test_Assemble_PreservesManualBreakpointsAndWatchpoints();
            Test_ClearAllBreakpoints_KeepsWatchpoints();
            Test_RemoveBreakpointAt_RemovesLineAndManual();
            Test_StopReason_BreakpointAndHalt();
            Test_StopReason_RunTo();

            Console.WriteLine($"  All BreakpointTests PASSED ({_passed} tests).\n");
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Assertion failed: " + message);
        }

        private static void Pass(string name)
        {
            _passed++;
            Console.WriteLine("  [PASS] " + name);
        }

        private static bool WaitUntil(Func<bool> condition, int timeoutMs = 3000)
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeoutMs)
            {
                if (condition()) return true;
                Thread.Sleep(1);
            }
            return condition();
        }

        private static void Test_Assemble_PreservesManualBreakpointsAndWatchpoints()
        {
            using var host = new MachineHost();
            host.Assemble(Source);
            host.ToggleBreakpointLine(2);
            host.AddAddressBreakpoint(0x8100);
            host.AddWatchpoint(0x8200, MemoryAccess.Write);

            host.Assemble(Source);

            Assert(host.BreakpointLines.Any(line => line == 2), "line breakpoint must survive reassembly");
            Assert(host.ManualBreakpoints.Any(address => address == 0x8100), "manual breakpoint must survive reassembly");
            Assert(host.Watchpoints.Length == 1, "watchpoint must survive reassembly");
            Assert(Array.IndexOf(host.AddressBreakpoints, (ushort)0x8000) >= 0,
                "line breakpoint must be mapped to 8000h after reassembly");
            Assert(Array.IndexOf(host.AddressBreakpoints, (ushort)0x8100) >= 0,
                "manual breakpoint must stay active after reassembly");

            Pass("assembly keeps manual breakpoints and watchpoints");
        }

        private static void Test_ClearAllBreakpoints_KeepsWatchpoints()
        {
            using var host = new MachineHost();
            host.Assemble(Source);
            host.ToggleBreakpointLine(2);
            host.AddAddressBreakpoint(0x8100);
            host.AddWatchpoint(0x8200, MemoryAccess.Write);

            host.ClearAllBreakpoints();

            Assert(host.BreakpointLines.Count == 0, "clear all must remove line breakpoints");
            Assert(host.ManualBreakpoints.Count == 0, "clear all must remove manual breakpoints");
            Assert(host.AddressBreakpoints.Length == 0, "clear all must leave no active breakpoints");
            Assert(host.Watchpoints.Length == 1, "clear all must not remove watchpoints");

            host.ClearWatchpoints();
            Assert(host.Watchpoints.Length == 0, "watchpoints must have a separate clear operation");

            Pass("clear all keeps watchpoints, separate watchpoint clear");
        }

        private static void Test_RemoveBreakpointAt_RemovesLineAndManual()
        {
            using var host = new MachineHost();
            host.Assemble(Source);
            host.ToggleBreakpointLine(2);
            host.AddAddressBreakpoint(0x8000);

            Assert(host.TryGetAddressForLine(2, out ushort lineAddress), "line 2 must map to an address");
            Assert(lineAddress == 0x8000, "line 2 must map to 8000h");

            host.RemoveBreakpointAt(0x8000);

            Assert(!host.BreakpointLines.Any(line => line == 2), "RemoveBreakpointAt must remove the line breakpoint");
            Assert(!host.ManualBreakpoints.Any(address => address == 0x8000), "RemoveBreakpointAt must remove the manual breakpoint");
            Assert(host.AddressBreakpoints.Length == 0, "no active breakpoint must remain");

            host.AddAddressBreakpoint(0x8100);
            host.RemoveAddressBreakpoint(0x8100);
            Assert(!host.ManualBreakpoints.Any(address => address == 0x8100), "manual breakpoint must be removable by address");

            Pass("breakpoints are removable by address and by line");
        }

        private static void Test_StopReason_BreakpointAndHalt()
        {
            var machine = new Machine(profile: CpuProfile.Extended);
            machine.Memory.WriteByte(0x8000, 0x00); // NOP
            machine.Memory.WriteByte(0x8001, 0x00); // NOP
            machine.Memory.WriteByte(0x8002, 0x76); // HLT
            machine.Cpu.State.PC = 0x8000;
            machine.Cpu.State.SP = 0x83F0;

            using var session = new MachineSession(machine);
            session.ThrottleLikeOriginal = false;
            session.TargetInstructionsPerSecond = 100000;
            session.SetBreakpoints(new[] { (ushort)0x8001 });

            session.Run();
            Assert(WaitUntil(() => session.State == RunState.Paused), "breakpoint must pause the CPU");
            string reason = StopReasonText.Describe(session);
            Assert(reason.Contains("точка останова") && reason.Contains("8001h"),
                "breakpoint reason must name the address, got: " + reason);

            session.SetBreakpoints(Array.Empty<ushort>());
            session.Run();
            Assert(WaitUntil(() => session.State == RunState.Halted), "program must reach HLT");
            reason = StopReasonText.Describe(session);
            Assert(reason.Contains("HLT"), "halt reason must mention HLT, got: " + reason);

            Pass("stop reason for breakpoint and HLT");
        }

        private static void Test_StopReason_RunTo()
        {
            var machine = new Machine(profile: CpuProfile.Extended);
            machine.Memory.WriteByte(0x8000, 0x00); // NOP
            machine.Memory.WriteByte(0x8001, 0x00); // NOP
            machine.Memory.WriteByte(0x8002, 0x76); // HLT
            machine.Cpu.State.PC = 0x8000;
            machine.Cpu.State.SP = 0x83F0;

            using var session = new MachineSession(machine);
            session.ThrottleLikeOriginal = false;
            session.TargetInstructionsPerSecond = 100000;
            session.RunTo(0x8002);

            Assert(WaitUntil(() => session.State == RunState.Paused), "run-to must pause");
            string reason = StopReasonText.Describe(session);
            Assert(reason.Contains("достигнут адрес") && reason.Contains("8002h"),
                "run-to reason must name the address, got: " + reason);

            Pass("stop reason for run-to");
        }
    }
}
