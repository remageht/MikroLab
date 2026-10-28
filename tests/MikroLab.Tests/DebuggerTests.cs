using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using MikroLab.Core.Cpu;
using MikroLab.Core.Debugging;
using MikroLab.Core.Machine;
using MikroLab.Core.Memory;

namespace MikroLab.Tests
{
    public static class DebuggerTests
    {
        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        public static void RunAll()
        {
            Console.WriteLine("[TEST SUITE] DebuggerTests (disassembly, run-to, step-back, watchpoints)");
            Test_Disassembler_RepresentativeOpcodes();
            Test_Disassembler_AllOpcodes();
            Test_ExecutionLog_CsvAndMarkdown();
            Test_StepHistory_Ring();
            Test_Session_StepBack();
            Test_Session_RunTo();
            Test_Session_RunToImmediate();
            Test_Session_WatchpointWrite();
            Test_Session_WatchpointRead();
            Test_Session_ExecutionLog();
            Test_MemoryWatchpoint_Matches();
            Console.WriteLine("  All DebuggerTests PASSED.\n");
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

        private static void Test_Disassembler_RepresentativeOpcodes()
        {
            byte[] memory = new byte[0x10000];
            ushort[] addresses = new ushort[] { 0x8000, 0x8001, 0x8002, 0x8003, 0x8004 };
            // Write sequences:
            memory[0x8000] = 0x00; // NOP
            memory[0x8001] = 0x01; memory[0x8002] = 0x34; memory[0x8003] = 0x12; // LXI B,1234h
            memory[0x8004] = 0x76; // HLT
            memory[0x8005] = 0x3E; memory[0x8006] = 0x55; // MVI A,55h
            memory[0x8007] = 0x21; memory[0x8008] = 0x00; memory[0x8009] = 0x82; // LXI H,8200h
            memory[0x800A] = 0x22; memory[0x800B] = 0x00; memory[0x800C] = 0x82; // SHLD 8200h
            memory[0x800D] = 0x32; memory[0x800E] = 0x00; memory[0x800F] = 0x82; // STA 8200h
            memory[0x8010] = 0x3A; memory[0x8011] = 0x00; memory[0x8012] = 0x82; // LDA 8200h
            memory[0x8013] = 0x2A; memory[0x8014] = 0x00; memory[0x8015] = 0x82; // LHLD 8200h
            memory[0x8016] = 0xCD; memory[0x8017] = 0x00; memory[0x8018] = 0x04; // CALL 0400h
            memory[0x8019] = 0xC3; memory[0x801A] = 0x00; memory[0x801B] = 0x80; // JMP 8000h
            memory[0x801C] = 0xC6; memory[0x801D] = 0x33; // ADI 33h
            memory[0x801E] = 0xDB; memory[0x801F] = 0xFB; // IN FBh
            memory[0x8020] = 0xD3; memory[0x8021] = 0xF9; // OUT F9h
            memory[0x8022] = 0xCB; memory[0x8023] = 0x00; memory[0x8024] = 0x80; // JMP alias
            memory[0x8025] = 0xD9; // RET alias
            memory[0x8026] = 0xDD; memory[0x8027] = 0x00; memory[0x8028] = 0x80; // CALL alias
            memory[0x8029] = 0x08; // NOP alias

            var checks = new[]
            {
                new { Address = 0x8000, Text = "NOP", Length = 1 },
                new { Address = 0x8001, Text = "LXI B, 1234h", Length = 3 },
                new { Address = 0x8004, Text = "HLT", Length = 1 },
                new { Address = 0x8005, Text = "MVI A, 55h", Length = 2 },
                new { Address = 0x800A, Text = "SHLD 8200h", Length = 3 },
                new { Address = 0x800D, Text = "STA 8200h", Length = 3 },
                new { Address = 0x8010, Text = "LDA 8200h", Length = 3 },
                new { Address = 0x8013, Text = "LHLD 8200h", Length = 3 },
                new { Address = 0x8016, Text = "CALL 0400h", Length = 3 },
                new { Address = 0x8019, Text = "JMP 8000h", Length = 3 },
                new { Address = 0x801C, Text = "ADI 33h", Length = 2 },
                new { Address = 0x801E, Text = "IN FBh", Length = 2 },
                new { Address = 0x8020, Text = "OUT F9h", Length = 2 },
                new { Address = 0x8022, Text = "JMP 8000h", Length = 3 },
                new { Address = 0x8025, Text = "RET", Length = 1 },
                new { Address = 0x8026, Text = "CALL 8000h", Length = 3 },
                new { Address = 0x8029, Text = "NOP", Length = 1 },
            };

            foreach (var check in checks)
            {
                DisassembledInstruction decoded = I8080Disassembler.Decode(memory, (ushort)check.Address);
                Assert(decoded.Text == check.Text,
                    "Disassembler mismatch at " + check.Address.ToString("X4") + ": " + decoded.Text + " != " + check.Text);
                Assert(decoded.Length == check.Length,
                    "Length mismatch at " + check.Address.ToString("X4") + ": " + decoded.Length + " != " + check.Length);
            }
            Console.WriteLine("  [PASS] disassembler representative opcodes");
        }

        private static void Test_Disassembler_AllOpcodes()
        {
            var memory = new byte[0x10000];
            for (int opcode = 0; opcode < 256; opcode++)
            {
                memory[0x8000] = (byte)opcode;
                DisassembledInstruction decoded = I8080Disassembler.Decode(memory, 0x8000);
                Assert(decoded.Length >= 1 && decoded.Length <= 3, "Bad length for opcode " + opcode.ToString("X2"));
                Assert(!string.IsNullOrEmpty(decoded.Text), "Empty text for opcode " + opcode.ToString("X2"));
                Assert(decoded.Byte1 == (byte)opcode, "Byte1 mismatch for opcode " + opcode.ToString("X2"));
                if (decoded.Length >= 2)
                {
                    Assert(decoded.Byte2 == memory[0x8001], "Byte2 mismatch for opcode " + opcode.ToString("X2"));
                }
                if (decoded.Length >= 3)
                {
                    Assert(decoded.Byte3 == memory[0x8002], "Byte3 mismatch for opcode " + opcode.ToString("X2"));
                }
            }
            Console.WriteLine("  [PASS] disassembler all 256 opcodes");
        }

        private static void Test_ExecutionLog_CsvAndMarkdown()
        {
            var log = new ExecutionLog(4);
            var entry1 = log.Append(new ExecutionLogEntry
            {
                Address = 0x8000,
                Bytes = new byte[] { 0x3E, 0x55 },
                Instruction = "MVI A, 55h",
                A = 0x55,
                B = 0x00,
                C = 0x00,
                D = 0x00,
                E = 0x00,
                H = 0x00,
                L = 0x00,
                F = 0x02,
                PC = 0x8002,
                SP = 0x83F0,
                Cycles = 7,
                TotalCycles = 7
            });
            var entry2 = log.Append(new ExecutionLogEntry
            {
                Address = 0x8002,
                Bytes = new byte[] { 0x76 },
                Instruction = "HLT",
                A = 0x55,
                B = 0x00,
                C = 0x00,
                D = 0x00,
                E = 0x00,
                H = 0x00,
                L = 0x00,
                F = 0x02,
                PC = 0x8002,
                SP = 0x83F0,
                Cycles = 7,
                TotalCycles = 14
            });

            Assert(entry1.Step == 1, "First step must be 1");
            Assert(entry2.Step == 2, "Second step must be 2");
            Assert(log.Count == 2, "Log count must be 2");
            Assert(log.Revision > 0, "Revision must advance");

            string csv = log.ToCsv();
            Assert(csv.Contains("Step,Address,Bytes,Instruction"), "CSV header must exist");
            Assert(csv.Contains("MVI A, 55h"), "CSV must contain instruction");
            Assert(csv.Contains("HLT"), "CSV must contain HLT");
            Assert(csv.Contains("3E 55"), "CSV must contain bytes");

            string markdown = log.ToMarkdown();
            Assert(markdown.Contains("| Step | Address | Bytes | Instruction |"), "Markdown header must exist");
            Assert(markdown.Contains("| 1 | 8000 | 3E 55 |"), "Markdown row must exist");

            using (var stream = new MemoryStream())
            {
                log.Save(stream, ExecutionLogFormat.Csv);
                Assert(stream.Length > 0, "CSV save must write bytes");
            }

            // Ring eviction.
            log.Append(new ExecutionLogEntry { Address = 0x8003 });
            log.Append(new ExecutionLogEntry { Address = 0x8004 });
            log.Append(new ExecutionLogEntry { Address = 0x8005 });
            Assert(log.Count == 4, "Ring must retain 4 entries");
            Assert(log.GetLast(4)[0].Address == 0x8002, "Oldest retained entry must be 8002h");

            log.Clear();
            Assert(log.Count == 0, "Clear must empty the log");
            Assert(log.IsEmpty, "IsEmpty must be true");
            Console.WriteLine("  [PASS] execution log CSV/Markdown and ring");
        }

        private static void Test_StepHistory_Ring()
        {
            var history = new StepHistory(100);
            Assert(history.Capacity == 100, "Capacity must be 100");
            Assert(StepHistory.MaxCapacity == 500, "MaxCapacity must be 500");
            Assert(StepHistory.MinCapacity == 100, "MinCapacity must be 100");

            try
            {
                var invalid = new StepHistory(99);
                Assert(false, "StepHistory must reject capacity 99");
            }
            catch (ArgumentOutOfRangeException)
            {
            }

            for (int i = 0; i < 105; i++)
            {
                var snapshot = new MachineSnapshot { A = (byte)(i & 0xFF), PC = (ushort)(0x8000 + i), SP = 0x83F0 };
                history.Push(snapshot);
            }

            Assert(history.Count == 100, "History must retain 100 snapshots");
            MachineSnapshot last;
            Assert(history.TryPop(out last), "TryPop must succeed");
            Assert(last.PC == 0x8000 + 104, "TryPop must return the newest snapshot");
            Assert(history.Count == 99, "Count must decrement after pop");

            history.Clear();
            Assert(history.Count == 0, "Clear must empty the history");
            MachineSnapshot empty;
            Assert(!history.TryPop(out empty), "TryPop must fail on empty history");
            Console.WriteLine("  [PASS] step history ring");
        }

        private static void Test_Session_StepBack()
        {
            var machine = new Machine(profile: CpuProfile.Extended);
            machine.Memory.WriteByte(0x8000, 0x3E); // MVI A,55h
            machine.Memory.WriteByte(0x8001, 0x55);
            machine.Memory.WriteByte(0x8002, 0x06); // MVI B,66h
            machine.Memory.WriteByte(0x8003, 0x66);
            machine.Memory.WriteByte(0x8004, 0x76); // HLT
            machine.Cpu.State.PC = 0x8000;
            machine.Cpu.State.SP = 0x83F0;
            machine.Cpu.State.A = 0x00;
            machine.Cpu.State.B = 0x00;

            using var session = new MachineSession(machine);
            session.LogExecution = false;
            session.ThrottleLikeOriginal = false;
            session.InstructionsPerSlice = 10;

            session.Step();
            Assert(machine.Cpu.State.A == 0x55, "First step must set A");
            Assert(machine.Cpu.State.PC == 0x8002, "First step must advance PC");

            session.Step();
            Assert(machine.Cpu.State.B == 0x66, "Second step must set B");
            Assert(machine.Cpu.State.PC == 0x8004, "Second step must advance PC");

            Assert(session.StepBack(), "StepBack must succeed");
            Assert(machine.Cpu.State.PC == 0x8002, "StepBack must restore the previous PC");
            Assert(machine.Cpu.State.A == 0x55, "StepBack must preserve A from the previous state");
            Assert(machine.Cpu.State.B == 0x00, "StepBack must revert B");
            Assert(session.State == RunState.Paused, "StepBack must leave the machine paused");
            Assert(session.CanStepBack, "StepBack must leave earlier history intact");

            Assert(session.StepBack(), "Second StepBack must succeed");
            Assert(machine.Cpu.State.PC == 0x8000, "Second StepBack must restore the initial PC");
            Assert(machine.Cpu.State.A == 0x00, "Second StepBack must revert A");

            Assert(!session.StepBack(), "StepBack must fail when history is empty");
            Console.WriteLine("  [PASS] session step back");
        }

        private static void Test_Session_RunTo()
        {
            var machine = new Machine(profile: CpuProfile.Extended);
            machine.Memory.WriteByte(0x8000, 0x00); // NOP
            machine.Memory.WriteByte(0x8001, 0x00); // NOP
            machine.Memory.WriteByte(0x8002, 0x00); // NOP
            machine.Memory.WriteByte(0x8003, 0x76); // HLT
            machine.Cpu.State.PC = 0x8000;
            machine.Cpu.State.SP = 0x83F0;

            using var session = new MachineSession(machine);
            session.LogExecution = false;
            session.ThrottleLikeOriginal = false;
            session.TargetInstructionsPerSecond = 100000;

            session.RunTo(0x8002);
            Assert(WaitUntil(() => session.State == RunState.Paused, 3000), "RunTo must pause");
            Assert(machine.Cpu.State.PC == 0x8002, "RunTo must stop at 0x8002");
            Assert(session.PauseReason == PauseReason.RunToAddress, "PauseReason must be RunToAddress");
            Assert(session.StopAddress == 0x8002, "StopAddress must be 0x8002");

            session.Run();
            Assert(WaitUntil(() => session.State == RunState.Halted, 3000), "The program must reach HLT");
            Console.WriteLine("  [PASS] session run to address");
        }

        private static void Test_Session_RunToImmediate()
        {
            var machine = new Machine(profile: CpuProfile.Extended);
            machine.Memory.WriteByte(0x8000, 0x00); // NOP
            machine.Cpu.State.PC = 0x8000;
            machine.Cpu.State.SP = 0x83F0;

            using var session = new MachineSession(machine);
            session.RunTo(0x8000);
            Assert(session.State == RunState.Paused, "RunTo at the current PC must not execute");
            Assert(machine.Cpu.State.PC == 0x8000, "RunTo at the current PC must keep PC");
            Console.WriteLine("  [PASS] session run to current PC");
        }

        private static void Test_Session_WatchpointWrite()
        {
            var machine = new Machine(profile: CpuProfile.Extended);
            machine.Memory.WriteByte(0x8000, 0x3E); // MVI A,55h
            machine.Memory.WriteByte(0x8001, 0x55);
            machine.Memory.WriteByte(0x8002, 0x32); // STA 8200h
            machine.Memory.WriteByte(0x8003, 0x00);
            machine.Memory.WriteByte(0x8004, 0x82);
            machine.Memory.WriteByte(0x8005, 0x76); // HLT
            machine.Cpu.State.PC = 0x8000;
            machine.Cpu.State.SP = 0x83F0;

            using var session = new MachineSession(machine);
            session.LogExecution = false;
            session.ThrottleLikeOriginal = false;
            session.TargetInstructionsPerSecond = 100000;
            session.SetWatchpoints(new[] { new MemoryWatchpoint(0x8200, MemoryAccess.Write) });

            session.Run();
            Assert(WaitUntil(() => session.IsPausedAtWatchpoint, 3000), "Write watchpoint must stop the CPU");
            Assert(session.WatchpointAddress == 0x8200, "WatchpointAddress must be 8200h");
            Assert(session.WatchpointAccess == MemoryAccess.Write, "WatchpointAccess must be Write");
            Assert(session.PauseReason == PauseReason.Watchpoint, "PauseReason must be Watchpoint");
            Assert(machine.Cpu.State.PC == 0x8005, "STA must complete before the watchpoint stops");
            Assert(machine.Memory.ReadByte(0x8200) == 0x55, "STA must write the value before stopping");
            Console.WriteLine("  [PASS] write watchpoint");
        }

        private static void Test_Session_WatchpointRead()
        {
            var machine = new Machine(profile: CpuProfile.Extended);
            machine.Memory.WriteByte(0x8000, 0x3E); // MVI A,55h
            machine.Memory.WriteByte(0x8001, 0x55);
            machine.Memory.WriteByte(0x8002, 0x32); // STA 8200h
            machine.Memory.WriteByte(0x8003, 0x00);
            machine.Memory.WriteByte(0x8004, 0x82);
            machine.Memory.WriteByte(0x8005, 0x3A); // LDA 8200h
            machine.Memory.WriteByte(0x8006, 0x00);
            machine.Memory.WriteByte(0x8007, 0x82);
            machine.Memory.WriteByte(0x8008, 0x76); // HLT
            machine.Cpu.State.PC = 0x8000;
            machine.Cpu.State.SP = 0x83F0;

            using var session = new MachineSession(machine);
            session.LogExecution = false;
            session.ThrottleLikeOriginal = false;
            session.TargetInstructionsPerSecond = 100000;
            session.SetWatchpoints(new[] { new MemoryWatchpoint(0x8200, MemoryAccess.Read) });

            session.Run();
            Assert(WaitUntil(() => session.IsPausedAtWatchpoint, 3000), "Read watchpoint must stop the CPU");
            Assert(session.WatchpointAddress == 0x8200, "WatchpointAddress must be 8200h");
            Assert(session.WatchpointAccess == MemoryAccess.Read, "WatchpointAccess must be Read");
            Assert(machine.Cpu.State.PC == 0x8008, "LDA must complete before the watchpoint stops");
            Assert(machine.Cpu.State.A == 0x55, "LDA must load the watched value");
            Console.WriteLine("  [PASS] read watchpoint");
        }

        private static void Test_Session_ExecutionLog()
        {
            var machine = new Machine(profile: CpuProfile.Extended);
            machine.Memory.WriteByte(0x8000, 0x3E); // MVI A,55h
            machine.Memory.WriteByte(0x8001, 0x55);
            machine.Memory.WriteByte(0x8002, 0x76); // HLT
            machine.Cpu.State.PC = 0x8000;
            machine.Cpu.State.SP = 0x83F0;

            using var session = new MachineSession(machine);
            session.LogExecution = true;
            session.ThrottleLikeOriginal = false;
            session.TargetInstructionsPerSecond = 100000;

            session.Run();
            Assert(WaitUntil(() => session.State == RunState.Halted, 3000), "Program must reach HLT");
            Assert(session.ExecutionLog.Count >= 2, "Execution log must contain at least 2 entries");

            ExecutionLogEntry[] entries = session.ExecutionLog.GetLast(2);
            Assert(entries[0].Address == 0x8000, "First logged instruction must be at 8000h");
            Assert(entries[0].Instruction == "MVI A, 55h", "First instruction must be MVI A,55h");
            Assert(entries[0].A == 0x55, "First entry must capture A after execution");
            Assert(entries[0].Cycles >= 1, "First entry must capture cycles");
            Assert(entries[1].Address == 0x8002, "Second logged instruction must be at 8002h");
            Assert(entries[1].Instruction == "HLT", "Second instruction must be HLT");
            Console.WriteLine("  [PASS] session execution log");
        }

        private static void Test_MemoryWatchpoint_Matches()
        {
            var watchpoint = new MemoryWatchpoint(0x1234, MemoryAccess.ReadWrite);
            Assert(watchpoint.WatchesRead, "ReadWrite watchpoint must watch reads");
            Assert(watchpoint.WatchesWrite, "ReadWrite watchpoint must watch writes");
            Assert(watchpoint.Matches(0x1234, MemoryAccess.Read), "Must match a read at the watched address");
            Assert(watchpoint.Matches(0x1234, MemoryAccess.Write), "Must match a write at the watched address");
            Assert(!watchpoint.Matches(0x1235, MemoryAccess.Read), "Must not match a different address");
            Assert(!new MemoryWatchpoint(0x1234, MemoryAccess.Write).Matches(0x1234, MemoryAccess.Read),
                "Write-only watchpoint must not match a read");

            try
            {
                var invalid = new MemoryWatchpoint(0x1234, MemoryAccess.None);
                Assert(false, "MemoryWatchpoint must reject MemoryAccess.None");
            }
            catch (ArgumentOutOfRangeException)
            {
            }
            Console.WriteLine("  [PASS] memory watchpoint");
        }
    }
}
