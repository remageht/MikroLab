using System;
using System.Collections.Generic;
using MikroLab.Core.Cpu;
using MikroLab.Core.Devices;
using MikroLab.Core.Machine;
using MikroLab.Core.Memory;
using CpuFlagReg = MikroLab.Core.Cpu.FlagReg;

namespace MikroLab.Tests
{
    public static class ExtendedCpuTests
    {
        private static int _passed;

        public static void RunAll()
        {
            Console.WriteLine("[TEST SUITE] ExtendedCpuTests (Complete Intel 8080 / KR580VM80A)");
            _passed = 0;

            Test_ProfileSelection();
            Test_Nop_And_UndocumentedNops();
            Test_Mvi_Mov_Memory();
            Test_Hlt_Stops_Until_Reset();
            Test_Add_Adc_Flags();
            Test_Sub_Sbb_Cmp_Flags();
            Test_Inr_Dcr_Flags();
            Test_Logical_Flags();
            Test_Daa();
            Test_Rotates_And_CarryOps();
            Test_Lxi_Lda_Sta_Lhld_Shld();
            Test_Ldax_Stax();
            Test_Xchg_Xthl_Sphl_Pchl();
            Test_Jmp_And_ConditionalJumps();
            Test_Call_Ret_Rst_HardwareStack();
            Test_ConditionalCall_And_Rcc();
            Test_Push_Pop_Pairs_And_Psw();
            Test_Ports_In_Out();
            Test_MemoryMappedDevices();
            Test_MonitorRoutines();
            Test_Program_Sum1To5();
            Test_EiDi_And_UndocumentedDuplicates();
            Test_AllOpcodes_NoUnsupported();

            Console.WriteLine($"  All ExtendedCpuTests PASSED ({_passed} tests).\n");
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException("Assertion failed: " + message);
        }

        private static void Pass(string name)
        {
            _passed++;
            Console.WriteLine("  [PASS] " + name);
        }

        private static Machine NewMachine(
            ISound sound = null,
            IClock clock = null,
            IDiagnostics diagnostics = null)
        {
            var machine = new Machine(sound, clock, diagnostics, CpuProfile.Extended);
            ResetMachine(machine);
            return machine;
        }

        private static void ResetMachine(Machine machine)
        {
            machine.Reset();
            machine.Cpu.State.A = 0;
            machine.Cpu.State.B = 0;
            machine.Cpu.State.C = 0;
            machine.Cpu.State.D = 0;
            machine.Cpu.State.E = 0;
            machine.Cpu.State.H = 0;
            machine.Cpu.State.L = 0;
            machine.Cpu.State.F = 0;
            machine.Cpu.State.Flags.Reset();
        }

        private static void Load(Machine machine, int address, params byte[] bytes)
        {
            for (int i = 0; i < bytes.Length; i++)
                machine.Memory.WriteByte((ushort)(address + i), bytes[i]);
        }

        private static void StepOk(Machine machine)
        {
            ushort pc = machine.Cpu.State.PC;
            Assert(machine.Cpu.Step(), $"instruction at {pc:X4} unexpectedly stopped");
        }

        private static bool RunUntilHalt(Machine machine, int maxSteps)
        {
            for (int i = 0; i < maxSteps; i++)
            {
                if (!machine.Cpu.Step())
                    return true;
            }
            return false;
        }

        private static void SetFlags(
            Machine machine,
            bool s,
            bool z,
            bool ac,
            bool p,
            bool c)
        {
            CpuFlagReg flags = new CpuFlagReg();
            flags.S = s;
            flags.Z = z;
            flags.AC = ac;
            flags.P = p;
            flags.C = c;
            machine.Cpu.State.Flags = flags;
        }

        private static void AssertFlags(
            Machine machine,
            bool s,
            bool z,
            bool ac,
            bool p,
            bool c,
            string context)
        {
            CpuFlagReg flags = machine.Cpu.State.Flags;
            Assert(
                flags.S == s && flags.Z == z && flags.AC == ac && flags.P == p && flags.C == c,
                $"{context}: expected S={s} Z={z} AC={ac} P={p} C={c}, " +
                $"got S={flags.S} Z={flags.Z} AC={flags.AC} P={flags.P} C={flags.C}");
        }

        private static void SetConditionFlags(Machine machine, int condition, bool take)
        {
            CpuFlagReg flags = new CpuFlagReg();
            switch (condition)
            {
                case 0: flags.Z = !take; break;
                case 1: flags.Z = take; break;
                case 2: flags.C = !take; break;
                case 3: flags.C = take; break;
                case 4: flags.P = !take; break;
                case 5: flags.P = take; break;
                case 6: flags.S = !take; break;
                case 7: flags.S = take; break;
            }
            machine.Cpu.State.Flags = flags;
        }

        public static void Test_ProfileSelection()
        {
            var legacy = new Machine();
            Assert(legacy.Cpu.Profile == CpuProfile.Legacy, "default Machine must use the legacy profile");
            Assert(legacy.Cpu is LegacyCpu, "default Machine must expose LegacyCpu");
            Load(legacy, 0x8000, 0x3E, 0x42); // MVI A,42h (supported by the legacy subset)
            legacy.Cpu.State.PC = 0x8000;
            Assert(legacy.Cpu.Step(), "legacy MVI should execute");
            Assert(legacy.Cpu.State.PC == 0x8002, "legacy MVI should advance PC");
            Assert(legacy.Cpu.State.A == 0x42, "legacy MVI should load A");

            var extended = new Machine(profile: CpuProfile.Extended);
            Assert(extended.Cpu.Profile == CpuProfile.Extended, "profile parameter must select ExtendedCpu");
            Assert(extended.Cpu is ExtendedCpu, "Extended profile must expose ExtendedCpu");

            Machine machine = NewMachine();
            Load(machine, 0x8000, 0xCD, 0x00, 0x81); // CALL 8100h
            StepOk(machine);
            Assert(machine.Cpu.CallStackDepth == 1, "extended call stack should contain the return address");
            Assert(machine.Cpu.GetCallStack()[0] == 0x8003, "extended call stack top should be 8003h");

            MachineSnapshot snapshot = machine.Capture();
            Assert(snapshot.CallStackDepth == 1, "Capture must read CallStackDepth through ICpu");
            Assert(snapshot.PC == 0x8100 && snapshot.SP == 0x83FC, "Capture should expose extended PC/SP");

            var legacyCall = new Machine();
            Load(legacyCall, 0x8000, 0xCD, 0x00, 0x81);
            legacyCall.Cpu.State.PC = 0x8000;
            Assert(legacyCall.Cpu.Step(), "legacy CALL should execute");
            Assert(legacyCall.Capture().CallStackDepth == 1, "legacy Capture should use ICpu.CallStackDepth");

            Pass("Profile selection and Capture call-stack depth");
        }

        public static void Test_Nop_And_UndocumentedNops()
        {
            Machine machine = NewMachine();
            byte[] nops = { 0x00, 0x08, 0x10, 0x18, 0x20, 0x28, 0x30, 0x38 };
            Load(machine, 0x8000, nops);

            for (int i = 0; i < nops.Length; i++)
            {
                StepOk(machine);
                Assert(machine.Cpu.State.PC == 0x8001 + i, $"NOP {nops[i]:X2} should advance one byte");
                Assert(machine.Cpu.State.Cycles == (ulong)(i + 1), "NOP should count one cycle");
            }

            AssertFlags(machine, false, false, false, false, false, "NOPs must not change flags");
            Assert(machine.Cpu.State.A == 0, "NOPs must not change A");
            Pass("NOP and undocumented NOPs (08,10,18,20,28,30,38)");
        }

        public static void Test_Mvi_Mov_Memory()
        {
            Machine machine = NewMachine();
            Load(
                machine,
                0x8000,
                0x3E, 0x12, // MVI A,12h
                0x06, 0x34, // MVI B,34h
                0x26, 0x82, // MVI H,82h
                0x2E, 0x00, // MVI L,00h
                0x36, 0x56, // MVI M,56h
                0x4E,       // MOV C,M
                0x50,       // MOV D,B
                0x77,       // MOV M,A
                0x5E,       // MOV E,M
                0x76);      // HLT

            for (int i = 0; i < 9; i++)
                StepOk(machine);

            CpuState state = machine.Cpu.State;
            Assert(state.A == 0x12, $"MOV/MVI A mismatch: {state.A:X2}");
            Assert(state.B == 0x34, $"MOV/MVI B mismatch: {state.B:X2}");
            Assert(state.C == 0x56, $"MOV C,M mismatch: {state.C:X2}");
            Assert(state.D == 0x34, $"MOV D,B mismatch: {state.D:X2}");
            Assert(state.E == 0x12, $"MOV E,M mismatch: {state.E:X2}");
            Assert(state.H == 0x82 && state.L == 0x00, "HL should address 8200h");
            Assert(machine.Memory.ReadByte(0x8200) == 0x12, "MOV M,A must write [HL]");
            Assert(state.PC == 0x800E, $"PC before HLT should be 800Eh, got {state.PC:X4}");

            Pass("MVI/MOV including M");
        }

        public static void Test_Hlt_Stops_Until_Reset()
        {
            Machine machine = NewMachine();
            Load(machine, 0x8000, 0x76, 0x00);
            Assert(!machine.Step(), "HLT should stop the machine");
            Assert(machine.State == RunState.Halted, "Machine.Step should mark the run state halted");
            Assert(machine.Cpu.State.PC == 0x8000, "HLT must leave PC at the HLT address");

            ulong cycles = machine.Cpu.State.Cycles;
            Assert(!machine.Cpu.Step(), "HLT must keep returning false");
            Assert(machine.Cpu.State.Cycles == cycles, "repeated Step on halted CPU must not count instructions");

            machine.Reset();
            Load(machine, 0x8000, 0x00);
            Assert(machine.Cpu.Step(), "Reset must clear the halted latch");
            Assert(machine.Cpu.State.PC == 0x8001, "execution should continue after Reset");

            Pass("HLT stops until Reset");
        }

        public static void Test_Add_Adc_Flags()
        {
            Machine machine = NewMachine();
            machine.Cpu.State.A = 0x0F;
            Load(machine, 0x8000, 0xC6, 0x01); // ADI 01h
            StepOk(machine);
            Assert(machine.Cpu.State.A == 0x10, "0Fh + 01h = 10h");
            AssertFlags(machine, false, false, true, false, false, "ADD 0Fh+01h");

            machine = NewMachine();
            machine.Cpu.State.A = 0x7F;
            Load(machine, 0x8000, 0xC6, 0x01);
            StepOk(machine);
            Assert(machine.Cpu.State.A == 0x80, "7Fh + 01h = 80h");
            AssertFlags(machine, true, false, true, false, false, "ADD 7Fh+01h");

            machine = NewMachine();
            machine.Cpu.State.A = 0xFF;
            Load(machine, 0x8000, 0xC6, 0x01);
            StepOk(machine);
            Assert(machine.Cpu.State.A == 0x00, "FFh + 01h = 00h");
            AssertFlags(machine, false, true, true, true, true, "ADD FFh+01h");

            machine = NewMachine();
            machine.Cpu.State.A = 0x0A;
            Load(machine, 0x8000, 0xC6, 0x05);
            StepOk(machine);
            Assert(machine.Cpu.State.A == 0x0F, "0Ah + 05h = 0Fh");
            AssertFlags(machine, false, false, false, true, false, "ADD 0Ah+05h");

            machine = NewMachine();
            machine.Cpu.State.A = 0x0F;
            machine.Cpu.State.Flags.C = true;
            Load(machine, 0x8000, 0xCE, 0x00); // ACI 00h
            StepOk(machine);
            Assert(machine.Cpu.State.A == 0x10, "0Fh + 00h + carry = 10h");
            AssertFlags(machine, false, false, true, false, false, "ADC 0Fh+00h+C");

            machine = NewMachine();
            machine.Cpu.State.A = 0xFF;
            machine.Cpu.State.Flags.C = true;
            Load(machine, 0x8000, 0xCE, 0x00);
            StepOk(machine);
            Assert(machine.Cpu.State.A == 0x00, "FFh + 00h + carry = 00h");
            AssertFlags(machine, false, true, true, true, true, "ADC FFh+00h+C");

            machine = NewMachine();
            machine.Cpu.State.A = 0x01;
            machine.Cpu.State.Flags.C = true;
            Load(machine, 0x8000, 0xCE, 0x01);
            StepOk(machine);
            Assert(machine.Cpu.State.A == 0x03, "01h + 01h + carry = 03h");
            AssertFlags(machine, false, false, false, true, false, "ADC 01h+01h+C");

            Pass("ADD/ADC flags including AC and parity");
        }

        public static void Test_Sub_Sbb_Cmp_Flags()
        {
            Machine machine = NewMachine();
            machine.Cpu.State.A = 0x10;
            Load(machine, 0x8000, 0xD6, 0x01); // SUI 01h
            StepOk(machine);
            Assert(machine.Cpu.State.A == 0x0F, "10h - 01h = 0Fh");
            AssertFlags(machine, false, false, true, true, false, "SUB 10h-01h");

            machine = NewMachine();
            machine.Cpu.State.A = 0x00;
            Load(machine, 0x8000, 0xD6, 0x01);
            StepOk(machine);
            Assert(machine.Cpu.State.A == 0xFF, "00h - 01h = FFh");
            AssertFlags(machine, true, false, true, true, true, "SUB 00h-01h");

            machine = NewMachine();
            machine.Cpu.State.A = 0x80;
            Load(machine, 0x8000, 0xD6, 0x01);
            StepOk(machine);
            Assert(machine.Cpu.State.A == 0x7F, "80h - 01h = 7Fh");
            AssertFlags(machine, false, false, true, false, false, "SUB 80h-01h");

            machine = NewMachine();
            machine.Cpu.State.A = 0x00;
            machine.Cpu.State.Flags.C = true;
            Load(machine, 0x8000, 0xDE, 0x00); // SBI 00h
            StepOk(machine);
            Assert(machine.Cpu.State.A == 0xFF, "00h - 00h - borrow = FFh");
            AssertFlags(machine, true, false, true, true, true, "SBB 00h-00h-C");

            machine = NewMachine();
            machine.Cpu.State.A = 0x05;
            machine.Cpu.State.Flags.C = true;
            Load(machine, 0x8000, 0xDE, 0x02);
            StepOk(machine);
            Assert(machine.Cpu.State.A == 0x02, "05h - 02h - borrow = 02h");
            AssertFlags(machine, false, false, false, false, false, "SBB 05h-02h-C");

            machine = NewMachine();
            machine.Cpu.State.A = 0x10;
            Load(machine, 0x8000, 0xFE, 0x20); // CPI 20h
            StepOk(machine);
            Assert(machine.Cpu.State.A == 0x10, "CMP must not modify A");
            AssertFlags(machine, true, false, false, true, true, "CMP 10h-20h");

            machine = NewMachine();
            machine.Cpu.State.A = 0x42;
            Load(machine, 0x8000, 0xFE, 0x42);
            StepOk(machine);
            Assert(machine.Cpu.State.A == 0x42, "CMP equal must not modify A");
            AssertFlags(machine, false, true, false, true, false, "CMP 42h-42h");

            Pass("SUB/SBB/CMP flags, borrow and A preservation");
        }

        public static void Test_Inr_Dcr_Flags()
        {
            Machine machine = NewMachine();
            machine.Cpu.State.A = 0x0F;
            machine.Cpu.State.Flags.C = true;
            Load(machine, 0x8000, 0x3C); // INR A
            StepOk(machine);
            Assert(machine.Cpu.State.A == 0x10, "INR A: 0Fh -> 10h");
            AssertFlags(machine, false, false, true, false, true, "INR 0Fh");

            machine = NewMachine();
            machine.Cpu.State.B = 0x41;
            machine.Cpu.State.Flags.C = true;
            Load(machine, 0x8000, 0x04); // INR B
            StepOk(machine);
            Assert(machine.Cpu.State.B == 0x42, "INR B: 41h -> 42h");
            AssertFlags(machine, false, false, false, true, true, "INR 41h");

            machine = NewMachine();
            machine.Cpu.State.A = 0xFF;
            machine.Cpu.State.Flags.C = true;
            Load(machine, 0x8000, 0x3C);
            StepOk(machine);
            Assert(machine.Cpu.State.A == 0x00, "INR A: FFh -> 00h");
            AssertFlags(machine, false, true, true, true, true, "INR FFh");

            machine = NewMachine();
            machine.Cpu.State.A = 0x10;
            machine.Cpu.State.Flags.C = true;
            Load(machine, 0x8000, 0x3D); // DCR A
            StepOk(machine);
            Assert(machine.Cpu.State.A == 0x0F, "DCR A: 10h -> 0Fh");
            AssertFlags(machine, false, false, true, true, true, "DCR 10h");

            machine = NewMachine();
            machine.Cpu.State.A = 0x00;
            machine.Cpu.State.Flags.C = true;
            Load(machine, 0x8000, 0x3D);
            StepOk(machine);
            Assert(machine.Cpu.State.A == 0xFF, "DCR A: 00h -> FFh");
            AssertFlags(machine, true, false, true, true, true, "DCR 00h");

            machine = NewMachine();
            machine.Cpu.State.B = 0x42;
            machine.Cpu.State.Flags.C = true;
            Load(machine, 0x8000, 0x05); // DCR B
            StepOk(machine);
            Assert(machine.Cpu.State.B == 0x41, "DCR B: 42h -> 41h");
            AssertFlags(machine, false, false, false, true, true, "DCR 42h");

            machine = NewMachine();
            machine.Cpu.State.H = 0x82;
            machine.Cpu.State.L = 0x00;
            machine.Memory.WriteByte(0x8200, 0x0F);
            Load(machine, 0x8000, 0x34, 0x35); // INR M, DCR M
            StepOk(machine);
            Assert(machine.Memory.ReadByte(0x8200) == 0x10, "INR M should increment [HL]");
            AssertFlags(machine, false, false, true, false, false, "INR M 0Fh");
            StepOk(machine);
            Assert(machine.Memory.ReadByte(0x8200) == 0x0F, "DCR M should decrement [HL]");
            AssertFlags(machine, false, false, true, true, false, "DCR M 10h");

            Pass("INR/DCR preserve carry and update AC/SZP");
        }

        public static void Test_Logical_Flags()
        {
            Machine machine = NewMachine();
            machine.Cpu.State.A = 0xF0;
            machine.Cpu.State.B = 0x0F;
            SetFlags(machine, true, true, true, false, true);
            Load(machine, 0x8000, 0xA0); // ANA B
            StepOk(machine);
            Assert(machine.Cpu.State.A == 0x00, "ANA F0h,0Fh = 00h");
            AssertFlags(machine, false, true, false, true, false, "ANA");

            machine = NewMachine();
            machine.Cpu.State.A = 0x00;
            machine.Cpu.State.B = 0x80;
            SetFlags(machine, false, false, true, true, true);
            Load(machine, 0x8000, 0xB0); // ORA B
            StepOk(machine);
            Assert(machine.Cpu.State.A == 0x80, "ORA 00h,80h = 80h");
            AssertFlags(machine, true, false, false, false, false, "ORA");

            machine = NewMachine();
            machine.Cpu.State.A = 0xFF;
            machine.Cpu.State.B = 0xFF;
            SetFlags(machine, true, true, true, false, true);
            Load(machine, 0x8000, 0xA8); // XRA B
            StepOk(machine);
            Assert(machine.Cpu.State.A == 0x00, "XRA FFh,FFh = 00h");
            AssertFlags(machine, false, true, false, true, false, "XRA");

            machine = NewMachine();
            machine.Cpu.State.A = 0xAA;
            SetFlags(machine, true, true, true, false, true);
            Load(machine, 0x8000, 0xE6, 0x0F); // ANI 0Fh
            StepOk(machine);
            Assert(machine.Cpu.State.A == 0x0A, "ANI AAh,0Fh = 0Ah");
            AssertFlags(machine, false, false, false, true, false, "ANI");

            machine = NewMachine();
            machine.Cpu.State.A = 0x00;
            SetFlags(machine, false, true, true, true, true);
            Load(machine, 0x8000, 0xF6, 0x80); // ORI 80h
            StepOk(machine);
            Assert(machine.Cpu.State.A == 0x80, "ORI 00h,80h = 80h");
            AssertFlags(machine, true, false, false, false, false, "ORI");

            machine = NewMachine();
            machine.Cpu.State.A = 0x03;
            SetFlags(machine, true, true, true, true, true);
            Load(machine, 0x8000, 0xEE, 0x01); // XRI 01h
            StepOk(machine);
            Assert(machine.Cpu.State.A == 0x02, "XRI 03h,01h = 02h");
            AssertFlags(machine, false, false, false, false, false, "XRI");

            Pass("Logical ops clear C/AC and update SZP");
        }

        public static void Test_Daa()
        {
            Machine machine = NewMachine();
            machine.Cpu.State.A = 0x09;
            Load(machine, 0x8000, 0xC6, 0x09, 0x27); // ADI 09h, DAA
            StepOk(machine);
            AssertFlags(machine, false, false, true, true, false, "ADD 09h+09h before DAA");
            StepOk(machine);
            Assert(machine.Cpu.State.A == 0x18, $"DAA 09h+09h should be 18h, got {machine.Cpu.State.A:X2}");
            AssertFlags(machine, false, false, false, true, false, "DAA 09h+09h");

            machine = NewMachine();
            machine.Cpu.State.A = 0x99;
            Load(machine, 0x8000, 0xC6, 0x99, 0x27); // ADI 99h, DAA
            StepOk(machine);
            Assert(machine.Cpu.State.A == 0x32, "99h + 99h = 32h with carry before DAA");
            AssertFlags(machine, false, false, true, false, true, "ADD 99h+99h before DAA");
            StepOk(machine);
            Assert(machine.Cpu.State.A == 0x98, $"DAA 99h+99h should be 98h, got {machine.Cpu.State.A:X2}");
            AssertFlags(machine, true, false, false, false, true, "DAA 99h+99h");

            machine = NewMachine();
            machine.Cpu.State.A = 0x99;
            Load(machine, 0x8000, 0xD6, 0x01, 0x27); // SUI 01h, DAA
            StepOk(machine);
            AssertFlags(machine, true, false, false, false, false, "SUB 99h-01h before DAA");
            StepOk(machine);
            Assert(machine.Cpu.State.A == 0x98, "DAA after 99h-01h should stay 98h");
            AssertFlags(machine, true, false, false, false, false, "DAA after subtraction");

            machine = NewMachine();
            machine.Cpu.State.A = 0x15;
            Load(machine, 0x8000, 0xD6, 0x06, 0x27); // SUI 06h, DAA
            StepOk(machine);
            Assert(machine.Cpu.State.A == 0x0F, "15h - 06h = 0Fh");
            AssertFlags(machine, false, false, true, true, false, "SUB 15h-06h before DAA");
            StepOk(machine);
            Assert(machine.Cpu.State.A == 0x15, "DAA AC correction after subtraction should add 06h");
            AssertFlags(machine, false, false, true, false, false, "DAA after subtraction with AC");

            machine = NewMachine();
            machine.Cpu.State.A = 0x50;
            machine.Cpu.State.Flags.C = true;
            Load(machine, 0x8000, 0x27);
            StepOk(machine);
            Assert(machine.Cpu.State.A == 0xB0, "DAA with carry should add 60h");
            AssertFlags(machine, true, false, false, false, true, "DAA with carry");

            Pass("DAA vectors including AC correction and subtraction");
        }

        public static void Test_Rotates_And_CarryOps()
        {
            Machine machine = NewMachine();
            machine.Cpu.State.A = 0x85;
            SetFlags(machine, true, true, true, true, false);
            Load(machine, 0x8000, 0x07); // RLC
            StepOk(machine);
            Assert(machine.Cpu.State.A == 0x0B, "RLC 85h = 0Bh");
            AssertFlags(machine, true, true, true, true, true, "RLC changes only C");

            machine = NewMachine();
            machine.Cpu.State.A = 0x01;
            Load(machine, 0x8000, 0x0F); // RRC
            StepOk(machine);
            Assert(machine.Cpu.State.A == 0x80, "RRC 01h = 80h");
            AssertFlags(machine, false, false, false, false, true, "RRC");

            machine = NewMachine();
            machine.Cpu.State.A = 0x80;
            Load(machine, 0x8000, 0x17); // RAL
            StepOk(machine);
            Assert(machine.Cpu.State.A == 0x00, "RAL 80h with C=0 = 00h");
            AssertFlags(machine, false, false, false, false, true, "RAL 80h");

            machine = NewMachine();
            machine.Cpu.State.A = 0x01;
            machine.Cpu.State.Flags.C = true;
            Load(machine, 0x8000, 0x17); // RAL
            StepOk(machine);
            Assert(machine.Cpu.State.A == 0x03, "RAL 01h with C=1 = 03h");
            AssertFlags(machine, false, false, false, false, false, "RAL 01h");

            machine = NewMachine();
            machine.Cpu.State.A = 0x01;
            Load(machine, 0x8000, 0x1F); // RAR
            StepOk(machine);
            Assert(machine.Cpu.State.A == 0x00, "RAR 01h with C=0 = 00h");
            AssertFlags(machine, false, false, false, false, true, "RAR 01h");

            machine = NewMachine();
            machine.Cpu.State.A = 0x80;
            machine.Cpu.State.Flags.C = true;
            Load(machine, 0x8000, 0x1F); // RAR
            StepOk(machine);
            Assert(machine.Cpu.State.A == 0xC0, "RAR 80h with C=1 = C0h");
            AssertFlags(machine, false, false, false, false, false, "RAR 80h");

            machine = NewMachine();
            machine.Cpu.State.A = 0x0F;
            SetFlags(machine, true, true, true, true, true);
            Load(machine, 0x8000, 0x2F); // CMA
            StepOk(machine);
            Assert(machine.Cpu.State.A == 0xF0, "CMA 0Fh = F0h");
            AssertFlags(machine, true, true, true, true, true, "CMA must not change flags");

            machine = NewMachine();
            SetFlags(machine, true, false, true, true, false);
            Load(machine, 0x8000, 0x37); // STC
            StepOk(machine);
            AssertFlags(machine, true, false, true, true, true, "STC sets only C");

            machine = NewMachine();
            machine.Cpu.State.Flags.C = true;
            Load(machine, 0x8000, 0x3F); // CMC
            StepOk(machine);
            AssertFlags(machine, false, false, false, false, false, "CMC clears C");
            Load(machine, 0x8001, 0x3F);
            StepOk(machine);
            AssertFlags(machine, false, false, false, false, true, "CMC sets C");

            Pass("RLC/RRC/RAL/RAR/CMA/STC/CMC");
        }

        public static void Test_Lxi_Lda_Sta_Lhld_Shld()
        {
            Machine machine = NewMachine();
            machine.Memory.WriteByte(0x8220, 0xA5);
            machine.Memory.WriteByte(0x8302, 0x11);
            machine.Memory.WriteByte(0x8303, 0x22);

            Load(
                machine,
                0x8000,
                0x01, 0x34, 0x12, // LXI B,1234h
                0x11, 0x78, 0x56, // LXI D,5678h
                0x21, 0x00, 0x82, // LXI H,8200h
                0x31, 0xFE, 0x82, // LXI SP,82FEh
                0x3E, 0x5A,       // MVI A,5Ah
                0x32, 0x10, 0x82, // STA 8210h
                0x3A, 0x20, 0x82, // LDA 8220h
                0x22, 0x30, 0x83, // SHLD 8330h
                0x2A, 0x02, 0x83, // LHLD 8302h
                0x76);            // HLT

            Assert(RunUntilHalt(machine, 50), "LXI/LDA/STA program should halt");

            CpuState state = machine.Cpu.State;
            Assert(state.B == 0x12 && state.C == 0x34, "LXI B should load 1234h");
            Assert(state.D == 0x56 && state.E == 0x78, "LXI D should load 5678h");
            Assert(state.SP == 0x82FE, $"LXI SP mismatch: {state.SP:X4}");
            Assert(state.A == 0xA5, $"LDA 8220h mismatch: {state.A:X2}");
            Assert(machine.Memory.ReadByte(0x8210) == 0x5A, "STA 8210h should store A");
            Assert(machine.Memory.ReadByte(0x8330) == 0x00, "SHLD should store L first");
            Assert(machine.Memory.ReadByte(0x8331) == 0x82, "SHLD should store H second");
            Assert(state.H == 0x22 && state.L == 0x11, "LHLD 8302h should load H=22h, L=11h");
            Assert(state.PC == 0x801A, "program should stop at HLT");

            Pass("LXI/LDA/STA/LHLD/SHLD");
        }

        public static void Test_Ldax_Stax()
        {
            Machine machine = NewMachine();
            machine.Cpu.State.B = 0x83;
            machine.Cpu.State.C = 0x00;
            machine.Cpu.State.D = 0x83;
            machine.Cpu.State.E = 0x10;
            machine.Memory.WriteByte(0x8300, 0x77);
            machine.Memory.WriteByte(0x8310, 0x88);

            Load(
                machine,
                0x8000,
                0x0A,       // LDAX B
                0x12,       // STAX D
                0x3E, 0x99, // MVI A,99h
                0x02,       // STAX B
                0x1A);      // LDAX D

            StepOk(machine);
            Assert(machine.Cpu.State.A == 0x77, "LDAX B should load 77h");
            StepOk(machine);
            Assert(machine.Memory.ReadByte(0x8310) == 0x77, "STAX D should store A at DE");
            StepOk(machine);
            StepOk(machine);
            Assert(machine.Memory.ReadByte(0x8300) == 0x99, "STAX B should store A at BC");
            StepOk(machine);
            Assert(machine.Cpu.State.A == 0x77, "LDAX D should load 77h");

            Pass("LDAX/STAX B and D");
        }

        public static void Test_Xchg_Xthl_Sphl_Pchl()
        {
            Machine machine = NewMachine();
            machine.Cpu.State.H = 0x12;
            machine.Cpu.State.L = 0x34;
            machine.Cpu.State.D = 0x56;
            machine.Cpu.State.E = 0x78;
            Load(machine, 0x8000, 0xEB); // XCHG
            StepOk(machine);
            Assert(machine.Cpu.State.H == 0x56 && machine.Cpu.State.L == 0x78, "XCHG should put DE into HL");
            Assert(machine.Cpu.State.D == 0x12 && machine.Cpu.State.E == 0x34, "XCHG should put HL into DE");
            Assert(machine.Cpu.State.PC == 0x8001, "XCHG should advance PC");

            machine = NewMachine();
            machine.Cpu.State.H = 0x12;
            machine.Cpu.State.L = 0x34;
            machine.Cpu.State.SP = 0x8200;
            machine.Memory.WriteByte(0x8200, 0xAA);
            machine.Memory.WriteByte(0x8201, 0xBB);
            Load(machine, 0x8000, 0xE3); // XTHL
            StepOk(machine);
            Assert(machine.Cpu.State.H == 0xBB && machine.Cpu.State.L == 0xAA, "XTHL should swap HL with stack top");
            Assert(machine.Memory.ReadByte(0x8200) == 0x34, "XTHL should write L to [SP]");
            Assert(machine.Memory.ReadByte(0x8201) == 0x12, "XTHL should write H to [SP+1]");
            Assert(machine.Cpu.State.SP == 0x8200, "XTHL must not move SP");

            machine = NewMachine();
            machine.Cpu.State.H = 0x82;
            machine.Cpu.State.L = 0x00;
            Load(machine, 0x8000, 0xF9); // SPHL
            StepOk(machine);
            Assert(machine.Cpu.State.SP == 0x8200, "SPHL should copy HL to SP");

            machine = NewMachine();
            machine.Cpu.State.H = 0x81;
            machine.Cpu.State.L = 0x23;
            Load(machine, 0x8000, 0xE9); // PCHL
            StepOk(machine);
            Assert(machine.Cpu.State.PC == 0x8123, "PCHL should copy HL to PC");

            Pass("XCHG/XTHL/SPHL/PCHL");
        }

        public static void Test_Jmp_And_ConditionalJumps()
        {
            Machine machine = NewMachine();
            Load(machine, 0x8000, 0xC3, 0x00, 0x81); // JMP 8100h
            StepOk(machine);
            Assert(machine.Cpu.State.PC == 0x8100, "JMP should load PC");

            machine = NewMachine();
            Load(machine, 0x8000, 0xCB, 0x00, 0x82); // undocumented JMP 8200h
            StepOk(machine);
            Assert(machine.Cpu.State.PC == 0x8200, "CBh should behave as JMP");

            for (int condition = 0; condition < 8; condition++)
            {
                byte opcode = (byte)(0xC2 + condition * 8);

                machine = NewMachine();
                Load(machine, 0x8000, opcode, 0x34, 0x82);
                SetConditionFlags(machine, condition, true);
                CpuFlagReg before = machine.Cpu.State.Flags;
                StepOk(machine);
                Assert(machine.Cpu.State.PC == 0x8234, $"Jcc cond {condition} taken should jump");
                CpuFlagReg after = machine.Cpu.State.Flags;
                Assert(
                    before.S == after.S && before.Z == after.Z && before.AC == after.AC &&
                    before.P == after.P && before.C == after.C,
                    $"conditional jump {condition} must not change flags");

                machine = NewMachine();
                Load(machine, 0x8000, opcode, 0x34, 0x82);
                SetConditionFlags(machine, condition, false);
                StepOk(machine);
                Assert(machine.Cpu.State.PC == 0x8003, $"Jcc cond {condition} not taken should advance");
            }

            Pass("JMP and all eight conditional jumps");
        }

        public static void Test_Call_Ret_Rst_HardwareStack()
        {
            Machine machine = NewMachine();
            Load(machine, 0x8000, 0xCD, 0x00, 0x81); // CALL 8100h
            Load(machine, 0x8100, 0xC9);             // RET
            StepOk(machine);

            Assert(machine.Cpu.State.PC == 0x8100, "CALL should jump to the target");
            Assert(machine.Cpu.State.SP == 0x83FC, "CALL should decrement SP by two");
            Assert(machine.Memory.ReadByte(0x83FC) == 0x03, "CALL should push the low return byte at SP");
            Assert(machine.Memory.ReadByte(0x83FD) == 0x80, "CALL should push the high return byte at SP+1");
            Assert(machine.Cpu.CallStackDepth == 1, "CALL should push the debug call stack");
            IReadOnlyList<ushort> calls = machine.Cpu.GetCallStack();
            Assert(calls.Count == 1 && calls[0] == 0x8003, "debug call stack top should be the return address");

            StepOk(machine);
            Assert(machine.Cpu.State.PC == 0x8003, "RET should restore PC");
            Assert(machine.Cpu.State.SP == 0x83FE, "RET should increment SP by two");
            Assert(machine.Cpu.CallStackDepth == 0, "RET should pop the debug call stack");

            machine = NewMachine();
            Load(machine, 0x8000, 0xDF); // RST 3
            Load(machine, 0x0018, 0xC9); // RET
            StepOk(machine);
            Assert(machine.Cpu.State.PC == 0x0018, "RST 3 should jump to 0018h");
            Assert(machine.Cpu.State.SP == 0x83FC, "RST should push a return address");
            Assert(machine.Memory.ReadByte(0x83FC) == 0x01, "RST should push the low return byte");
            Assert(machine.Memory.ReadByte(0x83FD) == 0x80, "RST should push the high return byte");
            Assert(machine.Cpu.CallStackDepth == 1, "RST should push the debug call stack");
            Assert(machine.Cpu.GetCallStack()[0] == 0x8001, "RST debug return address should be 8001h");
            StepOk(machine);
            Assert(machine.Cpu.State.PC == 0x8001, "RET after RST should return to 8001h");
            Assert(machine.Cpu.State.SP == 0x83FE, "RET after RST should restore SP");
            Assert(machine.Cpu.CallStackDepth == 0, "RET after RST should pop the debug call stack");

            machine = NewMachine();
            Load(machine, 0x8000, 0xCD, 0x00, 0x81);
            Load(machine, 0x8100, 0xD9); // undocumented RET
            StepOk(machine);
            StepOk(machine);
            Assert(machine.Cpu.State.PC == 0x8003, "D9h should behave as RET");
            Assert(machine.Cpu.CallStackDepth == 0, "D9h RET should pop the debug stack");

            Pass("CALL/RET/RST hardware stack and debug stack");
        }

        public static void Test_ConditionalCall_And_Rcc()
        {
            Machine machine = NewMachine();
            Load(machine, 0x8000, 0xC4, 0x00, 0x81); // CNZ 8100h
            machine.Cpu.State.Flags.Z = false;
            StepOk(machine);
            Assert(machine.Cpu.State.PC == 0x8100, "taken CNZ should jump");
            Assert(machine.Cpu.State.SP == 0x83FC, "taken CNZ should push a return address");
            Assert(machine.Cpu.CallStackDepth == 1, "taken CNZ should push the debug stack");
            Assert(machine.Memory.ReadByte(0x83FC) == 0x03 &&
                   machine.Memory.ReadByte(0x83FD) == 0x80,
                   "taken CNZ should push 8003h");

            machine = NewMachine();
            Load(machine, 0x8000, 0xC4, 0x00, 0x81);
            machine.Cpu.State.Flags.Z = true;
            StepOk(machine);
            Assert(machine.Cpu.State.PC == 0x8003, "not taken CNZ should advance");
            Assert(machine.Cpu.State.SP == 0x83FE, "not taken CNZ must not push");
            Assert(machine.Cpu.CallStackDepth == 0, "not taken CNZ must not touch the debug stack");

            machine = NewMachine();
            Load(machine, 0x8000, 0xCD, 0x00, 0x81); // CALL 8100h
            Load(machine, 0x8100, 0xC8);             // RZ
            StepOk(machine);
            machine.Cpu.State.Flags.Z = true;
            StepOk(machine);
            Assert(machine.Cpu.State.PC == 0x8003, "taken RZ should return");
            Assert(machine.Cpu.State.SP == 0x83FE, "taken RZ should pop");
            Assert(machine.Cpu.CallStackDepth == 0, "taken RZ should pop the debug stack");

            machine = NewMachine();
            Load(machine, 0x8000, 0xCD, 0x00, 0x81);
            Load(machine, 0x8100, 0xC8);
            StepOk(machine);
            machine.Cpu.State.Flags.Z = false;
            StepOk(machine);
            Assert(machine.Cpu.State.PC == 0x8101, "not taken RZ should advance");
            Assert(machine.Cpu.State.SP == 0x83FC, "not taken RZ must not pop");
            Assert(machine.Cpu.CallStackDepth == 1, "not taken RZ should keep the debug stack");

            Pass("Conditional CALL and conditional RET");
        }

        public static void Test_Push_Pop_Pairs_And_Psw()
        {
            Machine machine = NewMachine();
            machine.Cpu.State.B = 0x12;
            machine.Cpu.State.C = 0x34;
            Load(machine, 0x8000, 0xC5, 0xD1); // PUSH B, POP D
            StepOk(machine);
            Assert(machine.Cpu.State.SP == 0x83FC, "PUSH B should decrement SP");
            Assert(machine.Memory.ReadByte(0x83FC) == 0x34, "PUSH B should store C at SP");
            Assert(machine.Memory.ReadByte(0x83FD) == 0x12, "PUSH B should store B at SP+1");
            Assert(machine.Cpu.CallStackDepth == 0, "PUSH must not change the debug stack");
            StepOk(machine);
            Assert(machine.Cpu.State.D == 0x12 && machine.Cpu.State.E == 0x34, "POP D should restore 1234h");
            Assert(machine.Cpu.State.SP == 0x83FE, "POP D should restore SP");

            byte[] pushOpcodes = { 0xC5, 0xD5, 0xE5 };
            byte[] popOpcodes = { 0xC1, 0xD1, 0xE1 };
            for (int pair = 0; pair < 3; pair++)
            {
                machine = NewMachine();
                ushort value = (ushort)(0x1200 + pair);
                machine.Cpu.State.B = (byte)(value >> 8);
                machine.Cpu.State.C = (byte)value;
                machine.Cpu.State.D = (byte)(value >> 8);
                machine.Cpu.State.E = (byte)value;
                machine.Cpu.State.H = (byte)(value >> 8);
                machine.Cpu.State.L = (byte)value;
                Load(machine, 0x8000, pushOpcodes[pair], popOpcodes[pair]);
                StepOk(machine);
                machine.Cpu.State.B = 0;
                machine.Cpu.State.C = 0;
                machine.Cpu.State.D = 0;
                machine.Cpu.State.E = 0;
                machine.Cpu.State.H = 0;
                machine.Cpu.State.L = 0;
                StepOk(machine);
                if (pair == 0)
                {
                    Assert(machine.Cpu.State.B == (byte)(value >> 8) && machine.Cpu.State.C == (byte)value,
                           "PUSH/POP pair 0 should restore BC");
                }
                else if (pair == 1)
                {
                    Assert(machine.Cpu.State.D == (byte)(value >> 8) && machine.Cpu.State.E == (byte)value,
                           "PUSH/POP pair 1 should restore DE");
                }
                else
                {
                    Assert(machine.Cpu.State.H == (byte)(value >> 8) && machine.Cpu.State.L == (byte)value,
                           "PUSH/POP pair 2 should restore HL");
                }
                Assert(machine.Cpu.CallStackDepth == 0, "PUSH/POP pairs must not change the debug stack");
            }

            machine = NewMachine();
            machine.Cpu.State.A = 0x12;
            SetFlags(machine, true, false, true, true, true);
            Load(machine, 0x8000, 0xF5, 0xF1); // PUSH PSW, POP PSW
            StepOk(machine);
            Assert(machine.Cpu.State.SP == 0x83FC, "PUSH PSW should decrement SP");
            Assert(machine.Memory.ReadByte(0x83FC) == 0x97, "PUSH PSW should store flags at SP");
            Assert(machine.Memory.ReadByte(0x83FD) == 0x12, "PUSH PSW should store A at SP+1");
            Assert((machine.Memory.ReadByte(0x83FC) & 0x02) != 0, "PSW flag byte should keep bit 1 set");
            Assert(machine.Cpu.CallStackDepth == 0, "PUSH PSW must not change the debug stack");

            machine.Cpu.State.A = 0;
            machine.Cpu.State.Flags.Reset();
            StepOk(machine);
            Assert(machine.Cpu.State.A == 0x12, "POP PSW should restore A");
            AssertFlags(machine, true, false, true, true, true, "POP PSW flags");
            Assert(machine.Cpu.State.SP == 0x83FE, "POP PSW should restore SP");

            machine = NewMachine();
            Load(machine, 0x8000, 0xF5);
            StepOk(machine);
            Assert(machine.Memory.ReadByte(0x83FC) == 0x02, "PUSH PSW with clear flags should store 02h");

            Pass("PUSH/POP pairs and PSW");
        }

        public static void Test_Ports_In_Out()
        {
            Machine machine = NewMachine();
            machine.Ppi.Tumbler1 = true;
            machine.Ppi.Tumbler2 = false;
            machine.Ppi.Tumbler3 = true;
            SetFlags(machine, false, false, false, false, true);
            Load(machine, 0x8000, 0xDB, 0xFA); // IN FAh
            StepOk(machine);
            Assert(machine.Cpu.State.A == 0x05, $"IN FAh should read PA0..PA2, got {machine.Cpu.State.A:X2}");
            Assert(machine.Cpu.State.PC == 0x8002, "IN should be two bytes long");
            AssertFlags(machine, false, false, false, false, true, "IN must not change flags");

            machine.Cpu.State.A = 0xAA;
            Load(machine, 0x8002, 0xDB, 0x00); // IN 00h
            StepOk(machine);
            Assert(machine.Cpu.State.A == 0xFF, "unmapped IN port should return FFh");
            Assert(machine.Cpu.State.PC == 0x8004, "unmapped IN should still advance two bytes");

            machine.Analog.SetChannel(1, 4.0);
            machine.Analog.SelectedMuxChannel = 1;
            Load(machine, 0x8004, 0xDB, 0xFE); // IN FEh
            StepOk(machine);
            Assert(machine.Cpu.State.A == 0x66, $"IN FEh should read ADC 102, got {machine.Cpu.State.A}");
            Assert(machine.Cpu.State.PC == 0x8006, "IN FEh should be two bytes long");

            // Configure PA=input, PB/PC=output before driving the lamps.
            machine.Ppi.WriteControl(0x90);
            machine.Memory.WriteByte(0x0001, 0x00);
            machine.Cpu.State.A = 0x55;
            Load(machine, 0x8006, 0xD3, 0x01); // OUT 01h (legacy PB alias)
            StepOk(machine);
            Assert(machine.Ppi.PortB == 0x55, "OUT 01h should write port B");
            Assert(machine.Memory.ReadByte(0x0001) == 0x00,
                "OUT must use the I/O bus and leave the memory cell untouched");
            Assert(machine.Cpu.State.PC == 0x8008, "OUT should be two bytes long");

            machine.Cpu.State.A = 0x66;
            Load(machine, 0x8008, 0xD3, 0xF9); // OUT F9h
            StepOk(machine);
            Assert(machine.Ppi.PortB == 0x66, "OUT F9h should write port B");

            machine.Cpu.State.A = 0x90;
            Load(machine, 0x800A, 0xD3, 0xFB); // OUT FBh
            StepOk(machine);
            Assert(machine.Ppi.ControlWord == 0x90, "OUT FBh should set the PPI control word");
            Assert(machine.Ppi.PortAInput, "control word 90h must make port A an input");
            Assert(!machine.Ppi.PortBInput, "control word 90h must make port B an output");

            machine.Memory.WriteByte(0x00FD, 0x00);
            machine.Cpu.State.A = 0x07;
            Load(machine, 0x800C, 0xD3, 0xFD); // OUT FDh
            StepOk(machine);
            Assert(machine.Analog.SelectedMuxChannel == 3, "OUT FDh should select channel 3 (b & 3)");
            Assert(machine.Memory.ReadByte(0x00FD) == 0x00,
                "OUT FDh must not write the memory cell");

            Pass("IN/OUT ports, PPI control word and legacy device mapping");
        }

        public static void Test_MemoryMappedDevices()
        {
            Machine machine = NewMachine();
            machine.Ppi.InputA = 0x5A;
            Load(machine, 0x8000, 0x3A, 0xFA, 0x00); // LDA 00FAh
            StepOk(machine);
            Assert(machine.Cpu.State.A == 0x5A, "LDA 00FAh should read port A");
            Assert(machine.Ppi.InputA == 0x5A, "reading 00FAh must not consume the port A pins");

            Load(machine, 0x8003, 0x3A, 0xFA, 0x00); // LDA 00FAh again
            StepOk(machine);
            Assert(machine.Cpu.State.A == 0x5A, "a second read must return the same port A value");

            machine.Analog.SetChannel(2, 1.0);
            machine.Analog.SelectedMuxChannel = 2;
            Load(machine, 0x8006, 0x3A, 0xFE, 0x00); // LDA 00FEh
            StepOk(machine);
            Assert(machine.Cpu.State.A == 26, $"LDA 00FEh should return ADC 26, got {machine.Cpu.State.A}");

            machine.Cpu.State.A = 0x7E;
            Load(machine, 0x8009, 0x32, 0xF8, 0x83); // STA 83F8h
            StepOk(machine);
            Assert(machine.Memory.ReadByte(0x83F8) == 0x7E, "STA 83F8h should write memory");
            Assert(machine.Display.GetDigit(0) == 0x7E, "STA 83F8h should latch digit 0");

            machine.Cpu.State.A = 0x3F;
            Load(machine, 0x800C, 0x32, 0xFF, 0x83); // STA 83FFh
            StepOk(machine);
            Assert(machine.Display.GetDigit(7) == 0x3F, "STA 83FFh should latch digit 7");

            machine.Ppi.WriteControl(0x90); // port B must be an output
            machine.Cpu.State.A = 0x5A;
            Load(machine, 0x800F, 0x32, 0x01, 0x00); // STA 0001h
            StepOk(machine);
            Assert(machine.Ppi.PortB == 0x5A, "STA 0001h should write port B (legacy lamps)");

            machine.Cpu.State.A = 0x02;
            Load(machine, 0x8012, 0x32, 0xFD, 0x00); // STA 00FDh
            StepOk(machine);
            Assert(machine.Analog.SelectedMuxChannel == 2, "STA 00FDh should select the analog channel");

            Pass("Memory-mapped devices (00FAh, 00FEh, display latch)");
        }

        public static void Test_MonitorRoutines()
        {
            var sound = new RecordingSound();
            Machine machine = NewMachine(sound: sound);
            machine.Memory.WriteByte(0x83F4, 0x12);
            machine.Memory.WriteByte(0x83F5, 0x34);
            machine.Memory.WriteByte(0x83F6, 0x56);
            machine.Memory.WriteByte(0x83F7, 0x78);
            Load(machine, 0x8000, 0xCD, 0x00, 0x01); // CALL 0100h - Seggg
            StepOk(machine);

            Assert(machine.Cpu.State.PC == 0x8003, "monitor CALL 0100h should advance PC by 3");
            Assert(machine.Cpu.State.SP == 0x83FE, "monitor CALL 0100h must not push a return address");
            Assert(machine.Cpu.CallStackDepth == 0, "monitor CALL 0100h must not push the debug stack");
            Assert(machine.Display.GetDigit(0) == 6, "seg7 digit 0 should be 6");
            Assert(machine.Display.GetDigit(1) == 91, "seg7 digit 1 should be 91");
            Assert(machine.Display.GetDigit(2) == 79, "seg7 digit 2 should be 79");
            Assert(machine.Display.GetDigit(3) == 102, "seg7 digit 3 should be 102");
            Assert(machine.Display.GetDigit(4) == 109, "seg7 digit 4 should be 109");
            Assert(machine.Display.GetDigit(5) == 125, "seg7 digit 5 should be 125");
            Assert(machine.Display.GetDigit(6) == 7, "seg7 digit 6 should be 7");
            Assert(machine.Display.GetDigit(7) == 128, "seg7 digit 7 should be 128");
            Assert(machine.Memory.ReadByte(0x83F8) == 6 && machine.Memory.ReadByte(0x83FF) == 128,
                   "Seggg must also write the display latch memory");
            Assert(sound.TestToneCount == 0, "Seggg should not play sound");

            var soundCall = new RecordingSound();
            machine = NewMachine(sound: soundCall);
            Load(machine, 0x8000, 0xCD, 0x00, 0x03); // CALL 0300h
            StepOk(machine);
            Assert(soundCall.TestToneCount == 1, "monitor CALL 0300h should play the test tone");
            Assert(machine.Cpu.State.PC == 0x8003, "monitor CALL 0300h should advance PC by 3");
            Assert(machine.Cpu.State.SP == 0x83FE, "monitor CALL 0300h must not push");
            Assert(machine.Cpu.CallStackDepth == 0, "monitor CALL 0300h must not push the debug stack");

            var clock = new RecordingClock();
            machine = NewMachine(clock: clock);
            machine.Memory.WriteByte(0x8000, 0x12);
            machine.Memory.WriteByte(0x8001, 0x34);
            Load(machine, 0x8100, 0xCD, 0x00, 0x04); // CALL 0400h
            machine.Cpu.State.PC = 0x8100;
            StepOk(machine);
            Assert(clock.DelayCount == 1, "monitor CALL 0400h should call IClock.Delay once");
            Assert(clock.LastDelay == 0x1234, $"monitor delay should be 1234h ms, got {clock.LastDelay:X}");
            Assert(machine.Cpu.State.PC == 0x8103, "monitor CALL 0400h should advance PC by 3");
            Assert(machine.Cpu.State.SP == 0x83FE, "monitor CALL 0400h must not push");
            Assert(machine.Cpu.CallStackDepth == 0, "monitor CALL 0400h must not push the debug stack");

            Pass("Monitor routines 0100h/0300h/0400h without stack pushes");
        }

        public static void Test_Program_Sum1To5()
        {
            Machine machine = NewMachine();
            Load(
                machine,
                0x8000,
                0x21, 0x00, 0x00, // LXI H,0000h (sum)
                0x06, 0x05,       // MVI B,05h (counter)
                0x16, 0x00,       // MVI D,00h
                0x1E, 0x01,       // MVI E,01h (addend)
                0x19,             // DAD D
                0x13,             // INX D
                0x05,             // DCR B
                0xC2, 0x09, 0x80, // JNZ 8009h
                0x22, 0x00, 0x83, // SHLD 8300h
                0x76);            // HLT

            Assert(RunUntilHalt(machine, 100), "sum program should reach HLT");
            Assert(machine.Cpu.State.H == 0x00 && machine.Cpu.State.L == 0x0F,
                   $"sum 1..5 should be 000Fh, got {machine.Cpu.State.H:X2}{machine.Cpu.State.L:X2}");
            Assert(machine.Memory.ReadByte(0x8300) == 0x0F, "low sum byte should be stored at 8300h");
            Assert(machine.Memory.ReadByte(0x8301) == 0x00, "high sum byte should be stored at 8301h");
            Assert(machine.Cpu.State.B == 0, "loop counter should end at zero");
            Assert(machine.Cpu.State.D == 0x00 && machine.Cpu.State.E == 0x06, "addend should end at 0006h");
            Assert(machine.Cpu.State.PC == 0x8012, $"HLT should be at 8012h, got {machine.Cpu.State.PC:X4}");
            Assert(machine.Cpu.State.Cycles == 26, $"program should execute 26 instructions, got {machine.Cpu.State.Cycles}");
            AssertFlags(machine, false, true, false, true, false, "flags after final DCR B");
            Assert(machine.Cpu.CallStackDepth == 0, "program should not use the debug call stack");

            Pass("End-to-end assembled-style program (sum 1..5)");
        }

        public static void Test_EiDi_And_UndocumentedDuplicates()
        {
            Machine machine = NewMachine();
            SetFlags(machine, true, true, true, true, true);
            Load(machine, 0x8000, 0xFB, 0xF3); // EI, DI
            StepOk(machine);
            Assert(machine.Cpu.State.PC == 0x8001, "EI should advance one byte");
            AssertFlags(machine, true, true, true, true, true, "EI should be a no-op");
            StepOk(machine);
            Assert(machine.Cpu.State.PC == 0x8002, "DI should advance one byte");
            AssertFlags(machine, true, true, true, true, true, "DI should be a no-op");

            byte[] callDuplicates = { 0xDD, 0xED, 0xFD };
            for (int i = 0; i < callDuplicates.Length; i++)
            {
                machine = NewMachine();
                Load(machine, 0x8000, callDuplicates[i], 0x00, 0x81);
                StepOk(machine);
                Assert(machine.Cpu.State.PC == 0x8100,
                       $"undocumented CALL {callDuplicates[i]:X2} should jump to 8100h");
                Assert(machine.Cpu.State.SP == 0x83FC,
                       $"undocumented CALL {callDuplicates[i]:X2} should push a return address");
                Assert(machine.Cpu.CallStackDepth == 1,
                       $"undocumented CALL {callDuplicates[i]:X2} should push the debug stack");
                Assert(machine.Memory.ReadByte(0x83FC) == 0x03 &&
                       machine.Memory.ReadByte(0x83FD) == 0x80,
                       $"undocumented CALL {callDuplicates[i]:X2} should push 8003h");

                Load(machine, 0x8100, 0xD9);
                StepOk(machine);
                Assert(machine.Cpu.State.PC == 0x8003,
                       $"undocumented RET after CALL {callDuplicates[i]:X2} should return");
                Assert(machine.Cpu.CallStackDepth == 0,
                       $"undocumented RET after CALL {callDuplicates[i]:X2} should pop the debug stack");
            }

            Pass("EI/DI no-ops and undocumented CALL/RET/JMP duplicates");
        }

        public static void Test_AllOpcodes_NoUnsupported()
        {
            var diagnostics = new RecordingDiagnostics();
            Machine machine = NewMachine(diagnostics: diagnostics);

            for (int opcode = 0; opcode < 256; opcode++)
            {
                ResetMachine(machine);
                Load(machine, 0x8000, (byte)opcode, 0x00, 0x00);

                bool ok = machine.Cpu.Step();
                if (opcode == 0x76)
                {
                    Assert(!ok, "opcode 76h (HLT) should return false");
                    Assert(machine.Cpu.State.PC == 0x8000, "HLT must leave PC unchanged");
                }
                else
                {
                    Assert(ok, $"opcode {opcode:X2} should be recognized and executable");
                }

                Assert(diagnostics.UnsupportedCount == 0,
                       $"opcode {opcode:X2} must not be reported as unsupported");
                Assert(diagnostics.ErrorCount == 0,
                       $"opcode {opcode:X2} must not report a diagnostic error");
                Assert(machine.Cpu.State.Cycles == 1,
                       $"opcode {opcode:X2} should count exactly one instruction");
            }

            Pass("All 256 opcodes recognized (no unsupported fall-through)");
        }

        private sealed class RecordingSound : ISound
        {
            public int ToneCount;
            public int TestToneCount;
            public uint LastFrequency;
            public uint LastDuration;

            public void PlayTone(uint frequency, uint duration)
            {
                ToneCount++;
                LastFrequency = frequency;
                LastDuration = duration;
            }

            public void PlayTestTone()
            {
                TestToneCount++;
            }
        }

        private sealed class RecordingClock : IClock
        {
            public int DelayCount;
            public int LastDelay;

            public void Delay(int milliseconds)
            {
                DelayCount++;
                LastDelay = milliseconds;
            }
        }

        private sealed class RecordingDiagnostics : IDiagnostics
        {
            public int ErrorCount;
            public int UnsupportedCount;
            public int LastPc;
            public byte LastOpcode;

            public void ReportError(string message)
            {
                ErrorCount++;
            }

            public void ReportUnsupported(int pc, byte opcode)
            {
                UnsupportedCount++;
                LastPc = pc;
                LastOpcode = opcode;
            }
        }

    }
}
