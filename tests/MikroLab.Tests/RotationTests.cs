using System;
using MikroLab.Core.Cpu;
using MikroLab.Core.Machine;
using MikroLab.Core.Memory;

namespace MikroLab.Tests
{
    /// <summary>
    /// Pure rotation formulas used by the slow RLC/RRC/RAL/RAR animation,
    /// verified against the extended CPU itself.
    /// </summary>
    public static class RotationTests
    {
        private static int _passed;

        public static void RunAll()
        {
            Console.WriteLine("[TEST SUITE] RotationTests (RLC/RRC/RAL/RAR math and CPU parity)");
            _passed = 0;

            Test_Mnemonics();
            Test_LeftCircular();
            Test_RightCircular();
            Test_LeftThroughCarry();
            Test_RightThroughCarry();
            Test_CpuParity();

            Console.WriteLine($"  All RotationTests PASSED ({_passed} tests).\n");
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

        private static void Test_Mnemonics()
        {
            Assert(RotationMath.FromMnemonic("RLC") == RotationKind.LeftCircular, "RLC must map to LeftCircular");
            Assert(RotationMath.FromMnemonic(" rrc ") == RotationKind.RightCircular, "RRC must map to RightCircular");
            Assert(RotationMath.FromMnemonic("RAL") == RotationKind.LeftThroughCarry, "RAL must map to LeftThroughCarry");
            Assert(RotationMath.FromMnemonic("RAR") == RotationKind.RightThroughCarry, "RAR must map to RightThroughCarry");
            Assert(RotationMath.FromMnemonic("ADD") == null, "non-rotation mnemonics must not animate");
            Assert(RotationMath.FromMnemonic(null) == null, "null mnemonic must not animate");

            Pass("mnemonic mapping");
        }

        private static void Test_LeftCircular()
        {
            RotationResult result = RotationMath.Apply(RotationKind.LeftCircular, 0x81, false);
            Assert(result.A == 0x03, "RLC 81h must produce 03h");
            Assert(result.Carry, "RLC 81h must set carry");

            result = RotationMath.Apply(RotationKind.LeftCircular, 0x80, false);
            Assert(result.A == 0x01 && result.Carry, "RLC 80h must wrap bit 7 into bit 0");

            result = RotationMath.Apply(RotationKind.LeftCircular, 0x00, true);
            Assert(result.A == 0x00 && !result.Carry, "RLC 00h must clear carry");

            Pass("RLC formula");
        }

        private static void Test_RightCircular()
        {
            RotationResult result = RotationMath.Apply(RotationKind.RightCircular, 0x81, false);
            Assert(result.A == 0xC0 && result.Carry, "RRC 81h must wrap bit 0 into bit 7");

            result = RotationMath.Apply(RotationKind.RightCircular, 0x02, true);
            Assert(result.A == 0x01 && !result.Carry, "RRC 02h must clear carry");

            Pass("RRC formula");
        }

        private static void Test_LeftThroughCarry()
        {
            RotationResult result = RotationMath.Apply(RotationKind.LeftThroughCarry, 0x01, true);
            Assert(result.A == 0x03, "RAL 01h with carry must insert the old carry into bit 0");
            Assert(!result.Carry, "RAL 01h must clear carry");

            result = RotationMath.Apply(RotationKind.LeftThroughCarry, 0x80, false);
            Assert(result.A == 0x00 && result.Carry, "RAL 80h must move bit 7 into carry");

            Pass("RAL formula");
        }

        private static void Test_RightThroughCarry()
        {
            RotationResult result = RotationMath.Apply(RotationKind.RightThroughCarry, 0x02, true);
            Assert(result.A == 0x81, "RAR 02h with carry must insert the old carry into bit 7");
            Assert(!result.Carry, "RAR 02h must clear carry");

            result = RotationMath.Apply(RotationKind.RightThroughCarry, 0x01, false);
            Assert(result.A == 0x00 && result.Carry, "RAR 01h must move bit 0 into carry");

            Pass("RAR formula");
        }

        private static void Test_CpuParity()
        {
            var cases = new[]
            {
                new { Opcode = (byte)0x07, Kind = RotationKind.LeftCircular, A = (byte)0x81, Carry = false },
                new { Opcode = (byte)0x0F, Kind = RotationKind.RightCircular, A = (byte)0x81, Carry = false },
                new { Opcode = (byte)0x17, Kind = RotationKind.LeftThroughCarry, A = (byte)0x01, Carry = true },
                new { Opcode = (byte)0x1F, Kind = RotationKind.RightThroughCarry, A = (byte)0x02, Carry = true },
                new { Opcode = (byte)0x07, Kind = RotationKind.LeftCircular, A = (byte)0x00, Carry = true },
                new { Opcode = (byte)0x1F, Kind = RotationKind.RightThroughCarry, A = (byte)0x00, Carry = true }
            };

            foreach (var item in cases)
            {
                var machine = new Machine(profile: CpuProfile.Extended);
                machine.Memory.WriteByte(0x8000, item.Opcode);
                machine.Memory.WriteByte(0x8001, 0x76); // HLT
                machine.Cpu.State.PC = 0x8000;
                machine.Cpu.State.A = item.A;
                machine.Cpu.State.Flags.C = item.Carry;

                RotationResult expected = RotationMath.Apply(item.Kind, item.A, item.Carry);
                bool running = machine.Step();

                Assert(running, "rotation must not halt the CPU");
                Assert(machine.Cpu.State.A == expected.A,
                    "CPU " + item.Kind + " A mismatch: " + machine.Cpu.State.A.ToString("X2") +
                    " != " + expected.A.ToString("X2"));
                Assert(machine.Cpu.State.Flags.C == expected.Carry,
                    "CPU " + item.Kind + " carry mismatch");
                Assert(machine.Cpu.State.PC == 0x8001, "CPU must advance PC after a rotation");
            }

            Pass("CPU executes the same rotation as the animation");
        }
    }
}
