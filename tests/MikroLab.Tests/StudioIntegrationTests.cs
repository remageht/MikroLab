using System;
using System.Diagnostics;
using System.Threading;
using MikroLab.Core.Assembler;
using MikroLab.Core.Cpu;
using MikroLab.Core.Devices;
using MikroLab.Core.Machine;
using MikroLab.Core.Memory;

namespace MikroLab.Tests
{
    /// <summary>
    /// End-to-end checks for the Stage 2 Studio workflow: assemble source,
    /// load the image into a machine, execute it, inspect devices and stop
    /// on debugger breakpoints. These tests only use the headless Core API.
    /// </summary>
    public static class StudioIntegrationTests
    {
        private static int _passed;

        public static void RunAll()
        {
            Console.WriteLine("[TEST SUITE] StudioIntegrationTests (assemble → load → run → devices)");
            _passed = 0;

            Test_RunningLight_Assembles();
            Test_AllSamples_AssembleCleanly();
            Test_SumProgram_WritesMemoryAndLamps();
            Test_Tumblers_Reach_Lamps();
            Test_Breakpoint_StopsAtLabel_AndStepExecutesIt();
            Test_Breakpoint_CanBeResumed();
            Test_RunningSnapshot_IsNonBlocking();
            Test_CallStack_IsExposedWhilePaused();
            Test_AssemblerError_ReportsLine();
            Test_ProfileSelection();

            Console.WriteLine($"  StudioIntegrationTests: {_passed} passed");
        }

        // ---------------------------------------------------------------- helpers

        private static MachineSnapshot LoadAndReset(AssembleResult result, out MachineSession session, CpuProfile profile = CpuProfile.Extended)
        {
            var machine = new Machine(profile: profile);
            session = new MachineSession(machine);
            session.ThrottleLikeOriginal = false;
            session.InstructionsPerSlice = 500;

            LoadImage(machine, result);
            return session.CaptureSnapshot();
        }

        private static void LoadImage(Machine machine, AssembleResult result)
        {
            foreach (AssembledChunk chunk in result.Chunks)
            {
                for (int i = 0; i < chunk.Data.Length; i++)
                {
                    machine.Memory.WriteByte((ushort)(chunk.Address + i), chunk.Data[i]);
                }
            }

            machine.Cpu.State.PC = result.StartAddress;
            machine.Cpu.State.SP = 0x83FE;
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

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            _passed++;
        }

        // ---------------------------------------------------------------- tests

        private static void Test_RunningLight_Assembles()
        {
            AssembleResult result = I8080Assembler.Assemble(SamplePrograms.RunningLight, 0x8000);

            Check(result.Success, "Running light must assemble without errors");
            Check(result.Errors.Count == 0, "Running light must not report assembler errors");
            Check(result.StartAddress == 0x8000, "Running light must start at 8000h");
            Check(result.Symbols.ContainsKey("START"), "START symbol must be exported");
            Check(result.Symbols.ContainsKey("SHIFT"), "SHIFT symbol must be exported");
            Check(result.Symbols.ContainsKey("DELAY"), "DELAY symbol must be exported");
            Check(result.Chunks.Count > 0, "Running light must produce at least one chunk");
        }

        private static void Test_AllSamples_AssembleCleanly()
        {
            Check(SamplePrograms.All.Length >= 4, "Studio must ship at least four samples");

            for (int i = 0; i < SamplePrograms.All.Length; i++)
            {
                SampleProgram sample = SamplePrograms.All[i];
                AssembleResult result = I8080Assembler.Assemble(sample.Code, 0x8000);
                Check(result.Success, "Sample must assemble: " + sample.Name);
                Check(result.Errors.Count == 0, "Sample must not have errors: " + sample.Name);
                Check(result.StartAddress == 0x8000, "Sample must start at 8000h: " + sample.Name);
                Check(result.EndAddress > result.StartAddress, "Sample must emit bytes: " + sample.Name);
            }

            AssembleResult template = I8080Assembler.Assemble(SamplePrograms.NewTemplate, 0x8000);
            Check(template.Success, "New-project template must assemble");
        }

        private static void Test_SumProgram_WritesMemoryAndLamps()
        {
            AssembleResult result = I8080Assembler.Assemble(SamplePrograms.SumOneToFive, 0x8000);
            MachineSession session;
            LoadAndReset(result, out session);

            using (session)
            {
                session.Run();
                bool halted = WaitUntil(() => session.State == RunState.Halted, 3000);
                Check(halted, "Sum program must halt within 3 seconds");

                MachineSnapshot snapshot = session.CaptureSnapshot();
                Check(snapshot != null, "Snapshot must be available after halt");

                byte stored = 0;
                session.WithMachine(machine => stored = machine.Memory.ReadByte(0x8200));
                Check(stored == 0x0F, "Sum 1..5 must be stored at 8200h");
                Check(session.Machine.Ppi.PortB == 0x0F, "Sum must be shown on port B lamps");
            }
        }

        private static void Test_Tumblers_Reach_Lamps()
        {
            AssembleResult result = I8080Assembler.Assemble(SamplePrograms.TumblersToLamps, 0x8000);
            MachineSession session;
            LoadAndReset(result, out session);

            using (session)
            {
                session.WithMachine(machine =>
                {
                    machine.Ppi.Tumbler1 = true;
                    machine.Ppi.Tumbler2 = true;
                    machine.Ppi.Tumbler3 = true;
                });

                session.Step(); // MVI A, 90h
                session.Step(); // OUT FBh — configure PPI
                session.Step(); // IN FAh
                session.Step(); // OUT F9h

                Check(session.Machine.Ppi.PortB == 0x07,
                    "All tumblers must produce SA1 + 2*SA2 + 4*SA3 = 07h on port B");
            }
        }

        private static void Test_Breakpoint_StopsAtLabel_AndStepExecutesIt()
        {
            AssembleResult result = I8080Assembler.Assemble(SamplePrograms.RunningLight, 0x8000);
            ushort shift = result.Symbols["SHIFT"];

            MachineSession session;
            LoadAndReset(result, out session);

            using (session)
            {
                session.SetBreakpoints(new[] { shift });
                session.Run();

                bool stopped = WaitUntil(() => !IsRunning(session) && session.State == RunState.Paused, 3000);
                Check(stopped, "Breakpoint at SHIFT must stop the CPU");
                Check(session.IsPausedAtBreakpoint, "Session must report a breakpoint pause");
                Check(session.BreakpointAddress == shift, "Breakpoint address must equal SHIFT");
                Check(session.Machine.Cpu.State.PC == shift, "PC must point at the breakpoint instruction");

                session.Step();
                Check(session.Machine.Ppi.PortB == 0x01, "Stepping over OUT F9h must light the first lamp");
                Check(session.Machine.Cpu.State.PC != shift, "PC must leave the breakpoint after one step");
                Check(!session.IsPausedAtBreakpoint, "A manual step clears the breakpoint pause flag");
            }
        }

        private static void Test_Breakpoint_CanBeResumed()
        {
            AssembleResult result = I8080Assembler.Assemble(SamplePrograms.RunningLight, 0x8000);
            ushort shift = result.Symbols["SHIFT"];

            MachineSession session;
            LoadAndReset(result, out session);

            using (session)
            {
                session.SetBreakpoints(new[] { shift });
                session.Run();
                Check(WaitUntil(() => session.IsPausedAtBreakpoint, 3000), "First breakpoint hit");

                // Resuming must execute the instruction under PC and stop at
                // the next loop iteration instead of immediately re-triggering.
                session.Run();
                Check(WaitUntil(() => session.IsPausedAtBreakpoint, 3000), "Second breakpoint hit");
                Check(session.BreakpointAddress == shift, "Second stop must be at SHIFT again");
                Check(session.Machine.Cpu.State.PC == shift, "PC must be at SHIFT on the second stop");
            }
        }

        private static void Test_RunningSnapshot_IsNonBlocking()
        {
            AssembleResult result = I8080Assembler.Assemble(SamplePrograms.RunningLight, 0x8000);
            MachineSession session;
            LoadAndReset(result, out session);

            using (session)
            {
                session.InstructionsPerSlice = 50;
                session.Run();
                Check(WaitUntil(() => session.LastSnapshot != null, 2000),
                    "Execution loop must publish a snapshot");

                MachineSnapshot last = session.LastSnapshot;
                Check(last != null, "LastSnapshot must be available while running");

                var watch = Stopwatch.StartNew();
                for (int i = 0; i < 200; i++)
                {
                    MachineSnapshot snapshot = session.CaptureSnapshot();
                    Check(ReferenceEquals(snapshot, session.LastSnapshot),
                        "Running CaptureSnapshot must return the published snapshot");
                }
                watch.Stop();

                Check(watch.ElapsedMilliseconds < 500,
                    "CaptureSnapshot must not block the UI thread while running");

                Check(session.State == RunState.Running, "Infinite sample must still be running");
                session.Pause();
            }
        }

        private static void Test_CallStack_IsExposedWhilePaused()
        {
            AssembleResult result = I8080Assembler.Assemble(SamplePrograms.RunningLight, 0x8000);
            ushort dloop = result.Symbols["DLOOP"];

            MachineSession session;
            LoadAndReset(result, out session);

            using (session)
            {
                session.SetBreakpoints(new[] { dloop });
                session.Run();
                Check(WaitUntil(() => session.IsPausedAtBreakpoint, 3000),
                    "Breakpoint inside DELAY must stop the CPU");

                ushort[] stack = session.GetCallStack();
                Check(stack.Length == 1, "CALL DELAY must leave exactly one return address");
                Check(stack[0] == result.Symbols["SHIFT"] + 5,
                    "Return address must point to RLC after CALL DELAY (got " +
                    stack[0].ToString("X4") + ", SHIFT=" + result.Symbols["SHIFT"].ToString("X4") + ")");
            }
        }

        private static void Test_AssemblerError_ReportsLine()
        {
            string source = "        ORG 8000h\nBAD:    FOO A\n";
            AssembleResult result = I8080Assembler.Assemble(source, 0x8000);

            Check(!result.Success, "Unknown mnemonic must fail assembly");
            Check(result.Errors.Count > 0, "Assembler must report at least one error");
            Check(result.Errors[0].LineNumber == 2, "Error must point at the offending line");
        }

        private static void Test_ProfileSelection()
        {
            var legacy = new Machine();
            var extended = new Machine(profile: CpuProfile.Extended);

            Check(legacy.Cpu.Profile == CpuProfile.Legacy, "Default machine must keep legacy CPU");
            Check(extended.Cpu.Profile == CpuProfile.Extended, "Studio machine must use extended CPU");
        }

        private static bool IsRunning(MachineSession session)
        {
            return session.State == RunState.Running;
        }
    }
}
