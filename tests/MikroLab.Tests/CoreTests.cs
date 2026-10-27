using System;
using System.Reflection;
using System.Threading;
using MikroLab.Core.Cpu;
using MikroLab.Core.Devices;
using MikroLab.Core.Machine;
using MikroLab.Core.Memory;

namespace MikroLab.Tests
{
    public static class CoreTests
    {
        public static void RunAll()
        {
            Console.WriteLine("[TEST SUITE] CoreTests (MikroLab.Core Decoupling & Parity)");
            Test_Core_HasNoWinFormsReference();
            Test_MachineSession_Run_Pause_Step_Reset();
            Test_LegacyCpu_QuirkParity_Xchg();
            Test_LegacyCpu_QuirkParity_DadD();
            Test_LegacyCpu_QuirkParity_LdaxCd();
            Test_LegacyCpu_QuirkParity_LxiSp();
            Test_LegacyCpu_QuirkParity_InxDcx_ClearFlags();
            Test_LegacyCpu_QuirkParity_InFa_Tumblers();
            Console.WriteLine("  All CoreTests PASSED.\n");
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException($"Assertion failed: {message}");
        }

        public static void Test_Core_HasNoWinFormsReference()
        {
            Assembly coreAsm = typeof(Machine).Assembly;
            AssemblyName[] referenced = coreAsm.GetReferencedAssemblies();
            foreach (var r in referenced)
            {
                string name = r.Name.ToLowerInvariant();
                Assert(!name.Contains("forms"), $"MikroLab.Core must not reference Windows Forms, found: {r.FullName}");
                Assert(!name.Contains("drawing"), $"MikroLab.Core must not reference System.Drawing, found: {r.FullName}");
                Assert(!name.Contains("windowsbase"), $"MikroLab.Core must not reference WindowsBase, found: {r.FullName}");
            }
            Console.WriteLine("  [PASS] Test_Core_HasNoWinFormsReference (MikroLab.Core is 100% headless)");
        }

        public static void Test_MachineSession_Run_Pause_Step_Reset()
        {
            var machine = new Machine();
            using var session = new MachineSession(machine);
            session.InstructionsPerSlice = 50;

            // Load infinite loop at 0x8000: JMP 0x8000 (C3 00 80)
            machine.Memory.WriteByte(0x8000, 0xC3);
            machine.Memory.WriteByte(0x8001, 0x00);
            machine.Memory.WriteByte(0x8002, 0x80);
            machine.Cpu.State.PC = 0x8000;

            bool snapshotReceived = false;
            session.SnapshotUpdated += snap => { snapshotReceived = true; };

            session.Run();
            Assert(session.State == RunState.Running, $"Expected session to be Running, got {session.State}");

            // Let it run for 40ms in background
            Thread.Sleep(40);

            session.Pause();
            Assert(session.State == RunState.Paused, $"Expected session to be Paused, got {session.State}");
            Assert(machine.Cpu.State.PC == 0x8000, $"Expected PC to remain at 0x8000 in loop, got {machine.Cpu.State.PC:X4}");
            Assert(snapshotReceived, "Expected to receive snapshot update event");

            // Step once
            session.Step();
            Assert(session.State == RunState.Paused, $"Expected session to be Paused after step, got {session.State}");
            Assert(machine.Cpu.State.PC == 0x8000, "Expected PC to be 0x8000 after stepping JMP 8000h");

            // Reset
            session.Reset(ResetKind.Soft);
            Assert(session.State == RunState.Stopped, $"Expected session to be Stopped after reset, got {session.State}");
            Assert(machine.Cpu.State.PC == MemoryMap.UserMemStart, $"Expected PC to reset to {MemoryMap.UserMemStart:X4}, got {machine.Cpu.State.PC:X4}");

            Console.WriteLine("  [PASS] Test_MachineSession_Run_Pause_Step_Reset (Threaded execution & control)");
        }

        public static void Test_LegacyCpu_QuirkParity_Xchg()
        {
            var machine = new Machine();
            machine.Memory.WriteByte(0x8000, 0xEB); // XCHG
            machine.Cpu.State.PC = 0x8000;
            machine.Cpu.State.C = 0x11;
            machine.Cpu.State.D = 0x12;
            machine.Cpu.State.E = 0x34;
            machine.Cpu.State.H = 0x56;
            machine.Cpu.State.L = 0x78;

            machine.Step();

            // Legacy XCHG quirk: setRegPair shifts val >> 4 and & 15 instead of >> 8, regPair 1 = C:D, regPair 2 = D:E
            Assert(machine.Cpu.State.H == 0x11 && machine.Cpu.State.L == 0x02 &&
                   machine.Cpu.State.D == 0x23 && machine.Cpu.State.E == 0x04,
                   $"Legacy XCHG quirk mismatch: H={machine.Cpu.State.H:X2}, L={machine.Cpu.State.L:X2}, D={machine.Cpu.State.D:X2}, E={machine.Cpu.State.E:X2}");

            Console.WriteLine("  [PASS] Test_LegacyCpu_QuirkParity_Xchg (Exact legacy C:D register pair bug)");
        }

        public static void Test_LegacyCpu_QuirkParity_DadD()
        {
            var machine = new Machine();
            machine.Memory.WriteByte(0x8000, 0x19); // DAD D
            machine.Cpu.State.PC = 0x8000;
            machine.Cpu.State.H = 0x01;
            machine.Cpu.State.L = 0x00;
            machine.Cpu.State.B = 0x00;
            machine.Cpu.State.C = 0x00;
            machine.Cpu.State.D = 0x00;
            machine.Cpu.State.E = 0x20;

            machine.Step();

            // Legacy DAD D quirk: modifies DE (actually CD/DE mix via setRegPair) instead of HL!
            Assert(machine.Cpu.State.H == 0x01 && machine.Cpu.State.L == 0x00, "HL should remain untouched in legacy DAD D");
            Console.WriteLine("  [PASS] Test_LegacyCpu_QuirkParity_DadD (DAD D modifies pair, not HL)");
        }

        public static void Test_LegacyCpu_QuirkParity_LdaxCd()
        {
            var machine = new Machine();
            machine.Memory.WriteByte(0x8100, 0x77);
            machine.Memory.WriteByte(0x8000, 0x1A); // LDAX D
            machine.Cpu.State.PC = 0x8000;
            machine.Cpu.State.C = 0x81;
            machine.Cpu.State.D = 0x00;
            machine.Cpu.State.E = 0xFF; // E would point to 00FFh if correct, but C:D points to 8100h!

            machine.Step();

            Assert(machine.Cpu.State.A == 0x77, $"Expected A=77 from address 8100h (C:D), got {machine.Cpu.State.A:X2}");
            Console.WriteLine("  [PASS] Test_LegacyCpu_QuirkParity_LdaxCd (LDAX D uses C:D pair)");
        }

        public static void Test_LegacyCpu_QuirkParity_LxiSp()
        {
            var machine = new Machine();
            // LXI SP, 1234h (31 34 12)
            machine.Memory.WriteByte(0x8000, 0x31);
            machine.Memory.WriteByte(0x8001, 0x34);
            machine.Memory.WriteByte(0x8002, 0x12);
            machine.Cpu.State.PC = 0x8000;
            machine.Cpu.State.H = 0x81;
            machine.Cpu.State.L = 0x50;

            machine.Step();

            // Legacy LXI SP quirk: writes high byte to [HL] (M) and low byte to A
            byte memVal = machine.Memory.ReadByte(0x8150);
            Assert(memVal == 0x12, $"Expected M ([8150h]) = 12h, got {memVal:X2}");
            Assert(machine.Cpu.State.A == 0x34, $"Expected A = 34h, got {machine.Cpu.State.A:X2}");

            Console.WriteLine("  [PASS] Test_LegacyCpu_QuirkParity_LxiSp (LXI SP writes to M and A)");
        }

        public static void Test_LegacyCpu_QuirkParity_InxDcx_ClearFlags()
        {
            var machine = new Machine();
            machine.Memory.WriteByte(0x8000, 0x03); // INX B
            machine.Cpu.State.PC = 0x8000;
            machine.Cpu.State.B = 0x00;
            machine.Cpu.State.C = 0x05;
            machine.Cpu.State.Flags.AC = true;
            machine.Cpu.State.Flags.C = true;
            machine.Cpu.State.Flags.P = true;
            machine.Cpu.State.Flags.S = true;
            machine.Cpu.State.Flags.Z = true;

            machine.Step();

            Assert(machine.Cpu.State.C == 0x06, "C should increment");
            Assert(!machine.Cpu.State.Flags.AC && !machine.Cpu.State.Flags.C &&
                   !machine.Cpu.State.Flags.P && !machine.Cpu.State.Flags.S &&
                   !machine.Cpu.State.Flags.Z, "INX should clear all flags via clearFags quirk");

            Console.WriteLine("  [PASS] Test_LegacyCpu_QuirkParity_InxDcx_ClearFlags (INX clears flags)");
        }

        public static void Test_LegacyCpu_QuirkParity_InFa_Tumblers()
        {
            var machine = new Machine();
            machine.Memory.WriteByte(0x8000, 0xDB); // IN FAh
            machine.Memory.WriteByte(0x8001, 0xFA);
            machine.Cpu.State.PC = 0x8000;

            machine.Ppi.Tumbler1 = true; // 9
            machine.Ppi.Tumbler2 = false; // 0
            machine.Ppi.Tumbler3 = true; // 3

            machine.Step();

            // Formula: 1 | (9*1) | (5*0) | (3*1) = 1 | 9 | 3 = 11 (0x0B)
            Assert(machine.Cpu.State.A == 11, $"Expected A=11 (1|9|3), got {machine.Cpu.State.A}");

            Console.WriteLine("  [PASS] Test_LegacyCpu_QuirkParity_InFa_Tumblers (IN FAh bitwise OR formula)");
        }
    }
}
