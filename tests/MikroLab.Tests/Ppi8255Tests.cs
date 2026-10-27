using System;
using MikroLab.Core.Cpu;
using MikroLab.Core.Devices;
using MikroLab.Core.Machine;
using MikroLab.Core.Memory;

namespace MikroLab.Tests
{
    /// <summary>
    /// Unit tests for the real KR580IK55 / i8255 model: reset state, mode
    /// words 0x81/0x90/0x98, modes 1 and 2, direction bits, bit set/reset and
    /// the primary/legacy port map.
    /// </summary>
    public static class Ppi8255Tests
    {
        private static int _passed;

        public static void RunAll()
        {
            Console.WriteLine("[TEST SUITE] Ppi8255Tests (KR580IK55 / i8255 modes and directions)");
            _passed = 0;

            Test_Reset_Defaults();
            Test_ModeWord_81();
            Test_ModeWord_90();
            Test_ModeWord_98();
            Test_Modes_1_And_2();
            Test_BitSetReset();
            Test_Device_MapsPrimaryAndLegacyPorts();
            Test_PortMap_Validation();
            Test_MachineSnapshot_RoundTripsPpi();

            Console.WriteLine($"  All Ppi8255Tests PASSED ({_passed} tests).\n");
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

        private static void Expect<TException>(Action action, string message)
            where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }
            throw new InvalidOperationException("Assertion failed: " + message);
        }

        public static void Test_Reset_Defaults()
        {
            var ppi = new Ppi8255();

            Assert(ppi.ControlWord == 0x9B, "reset control word must be 9Bh");
            Assert(ppi.GroupAMode == PpiMode.Mode0 && ppi.GroupBMode == PpiMode.Mode0,
                "reset must select mode 0 for both groups");
            Assert(ppi.PortAInput && ppi.PortBInput, "reset must make ports A and B inputs");
            Assert(ppi.PortCUpperInput && ppi.PortCLowerInput, "reset must make port C an input");
            Assert(ppi.ReadPort(0) == 0x00 && ppi.ReadPort(1) == 0x00 && ppi.ReadPort(2) == 0x00,
                "reset must clear the port latches");
            Assert(ppi.ReadPort(3) == 0x9B, "control register must read back the reset word");

            ppi.WritePort(0, 0xFF);
            ppi.WritePort(1, 0xFF);
            ppi.WritePort(2, 0xFF);
            Assert(ppi.PortA == 0 && ppi.PortB == 0 && ppi.PortC == 0,
                "writes to input ports must not change the output latches");

            Pass("reset defaults: 9Bh, mode 0, all ports input");
        }

        public static void Test_ModeWord_81()
        {
            var ppi = new Ppi8255();
            ppi.WriteControl(0x81);

            Assert(ppi.ControlWord == 0x81, "control word must read back 81h");
            Assert(ppi.GroupAMode == PpiMode.Mode0 && ppi.GroupBMode == PpiMode.Mode0,
                "81h must select mode 0");
            Assert(!ppi.PortAInput && !ppi.PortBInput && !ppi.PortCUpperInput,
                "81h must make A, B and C-upper outputs");
            Assert(ppi.PortCLowerInput, "81h must make C-lower an input");

            ppi.WritePort(0, 0xA5);
            ppi.WritePort(1, 0x5A);
            ppi.WritePort(2, 0xF5);
            Assert(ppi.ReadPort(0) == 0xA5, "PA output latch must read back");
            Assert(ppi.ReadPort(1) == 0x5A, "PB output latch must read back");
            Assert(ppi.ReadPort(2) == 0xF0, "only the C-upper half may be updated");

            ppi.InputC = 0x0A;
            Assert(ppi.ReadPort(2) == 0xFA, "C-upper must come from the latch, C-lower from the pins");

            Pass("control word 81h: A/B/C-upper output, C-lower input");
        }

        public static void Test_ModeWord_90()
        {
            var ppi = new Ppi8255();
            ppi.WriteControl(0x90);

            Assert(ppi.ControlWord == 0x90, "control word must read back 90h");
            Assert(ppi.PortAInput, "90h must make port A an input");
            Assert(!ppi.PortBInput && !ppi.PortCUpperInput && !ppi.PortCLowerInput,
                "90h must make B and C outputs");
            Assert(!ppi.PortABidirectional, "mode 0 must not be bidirectional");

            ppi.InputA = 0x3C;
            ppi.WritePort(0, 0xFF);
            Assert(ppi.PortA == 0, "writing PA in input mode must be ignored");
            Assert(ppi.ReadPort(0) == 0x3C, "PA must read the external pins");

            ppi.WritePort(1, 0x55);
            ppi.WritePort(2, 0xC3);
            Assert(ppi.ReadPort(1) == 0x55 && ppi.ReadPort(2) == 0xC3,
                "PB and PC must latch writes in output mode");

            Pass("control word 90h: PA input, PB/PC output");
        }

        public static void Test_ModeWord_98()
        {
            var ppi = new Ppi8255();
            ppi.WriteControl(0x98);

            Assert(ppi.PortAInput && ppi.PortCUpperInput,
                "98h must make PA and C-upper inputs");
            Assert(!ppi.PortBInput && !ppi.PortCLowerInput,
                "98h must make PB and C-lower outputs");

            ppi.InputA = 0x12;
            ppi.InputC = 0xA0;
            ppi.WritePort(2, 0x0F);
            Assert(ppi.ReadPort(0) == 0x12, "PA must read the pins");
            Assert(ppi.ReadPort(2) == 0xAF, "C-upper must read pins, C-lower must read the latch");

            ppi.WritePort(1, 0x7E);
            Assert(ppi.ReadPort(1) == 0x7E, "PB must remain an output");

            Pass("control word 98h: split port C directions");
        }

        public static void Test_Modes_1_And_2()
        {
            var ppi = new Ppi8255();

            ppi.WriteControl(0xB0); // group A mode 1, PA input; group B mode 0
            Assert(ppi.GroupAMode == PpiMode.Mode1, "B0h must select mode 1 for group A");
            Assert(ppi.GroupBMode == PpiMode.Mode0, "B0h must keep group B in mode 0");
            Assert(ppi.PortAInput, "B0h must make PA an input");
            Assert(!ppi.PortBInput, "B0h must make PB an output");

            ppi.WriteControl(0xC0); // group A mode 2, bidirectional PA
            Assert(ppi.GroupAMode == PpiMode.Mode2, "C0h must select mode 2 for group A");
            Assert(ppi.PortABidirectional, "mode 2 must mark PA bidirectional");
            Assert(ppi.ReadPort(0) == 0x00, "bidirectional PA must read the input pins in this model");

            ppi.InputA = 0x66;
            ppi.WritePort(0, 0x99);
            Assert(ppi.ReadPort(0) == 0x66, "bidirectional read must return the pins");
            Assert(ppi.PortA == 0x99, "bidirectional write must still update the output latch");

            Pass("modes 1 and 2, including bidirectional port A");
        }

        public static void Test_BitSetReset()
        {
            var ppi = new Ppi8255();
            ppi.WriteControl(0x80); // all outputs

            ppi.WriteControl(0x09); // set PC4
            Assert(ppi.PortC == 0x10, "BSR 09h must set PC4");
            ppi.WriteControl(0x08); // reset PC4
            Assert(ppi.PortC == 0x00, "BSR 08h must reset PC4");
            ppi.WriteControl(0x0F); // set PC7
            Assert(ppi.PortC == 0x80, "BSR 0Fh must set PC7");
            Assert(ppi.ControlWord == 0x80, "BSR words must not replace the mode word");

            Pass("port C bit set/reset words");
        }

        public static void Test_Device_MapsPrimaryAndLegacyPorts()
        {
            var ppi = new Ppi8255();
            var device = new Ppi8255Device(ppi);
            ppi.WriteControl(0x90);

            ppi.InputA = 0x05;
            Assert(device.ReadPort(0xFA) == 0x05, "IN FAh must read PA pins");
            Assert(device.ReadMemory(0xFA) == 0x05, "LDA 00FAh must read PA pins");
            Assert(device.ReadMemory(0xFA) == 0x05, "reading 00FAh must not consume the input");

            device.WritePort(0xF9, 0x3C);
            Assert(ppi.PortB == 0x3C, "OUT F9h must write PB");
            device.WritePort(0x01, 0x5A);
            Assert(ppi.PortB == 0x5A, "legacy OUT 01h must write PB");

            device.WritePort(0xFB, 0x98);
            Assert(ppi.ControlWord == 0x98, "OUT FBh must write the control register");
            Assert(ppi.PortAInput && ppi.PortCUpperInput, "98h direction bits must be applied");

            device.WriteMemory(0x02, 0x09); // legacy control alias, BSR set PC4
            Assert((ppi.PortC & 0x10) != 0, "legacy STA 0002h must reach the PPI control register");

            device.WriteMemory(0xF9, 0x21);
            Assert(ppi.PortB == 0x21, "STA 00F9h must write PB");

            Pass("primary FAh/F9h/F8h/FBh and legacy 01h/02h mapping");
        }

        public static void Test_PortMap_Validation()
        {
            var map = PpiPortMap.Default;
            Assert(map.PortA == 0xFA && map.PortB == 0xF9 && map.PortC == 0xF8 && map.Control == 0xFB,
                "default map must be FAh/F9h/F8h/FBh");
            map.Validate();

            var duplicate = new PpiPortMap { PortA = 0x10, PortB = 0x10 };
            Expect<ArgumentException>(() => duplicate.Validate(),
                "duplicate primary ports must be rejected");

            var alias = new PpiPortMap { LegacyPortB = 0xFA };
            Expect<ArgumentException>(() => alias.Validate(),
                "legacy alias must not collide with a primary port");

            var disabled = new PpiPortMap { LegacyPortB = -1, LegacyControl = -1 };
            disabled.Validate();

            var negative = new PpiPortMap { PortA = -1 };
            Expect<ArgumentOutOfRangeException>(() => negative.Validate(),
                "primary ports must be in 00h..FFh");

            Pass("port map defaults and validation");
        }

        public static void Test_MachineSnapshot_RoundTripsPpi()
        {
            var machine = new Machine(profile: CpuProfile.Extended);
            machine.Ppi.WriteControl(0x98);
            machine.Ppi.InputA = 0x11;
            machine.Ppi.InputB = 0x22;
            machine.Ppi.InputC = 0xA0;
            machine.Ppi.WritePort(1, 0x33);
            machine.Ppi.WritePort(2, 0x0F);

            MachineSnapshot snapshot = machine.Capture();
            Assert(snapshot.PpiControlWord == 0x98, "snapshot must store the control word");
            Assert(snapshot.PpiEffectiveA == 0x11, "snapshot effective PA must use the pins");
            Assert(snapshot.PpiEffectiveB == 0x33, "snapshot effective PB must use the latch");
            Assert(snapshot.PpiEffectiveC == 0xAF, "snapshot effective PC must mix pins and latch");

            machine.Ppi.Reset();
            machine.Ppi.InputA = 0;
            machine.Ppi.InputB = 0;
            machine.Ppi.InputC = 0;
            machine.Restore(snapshot);

            Assert(machine.Ppi.ControlWord == 0x98, "restore must keep the control word");
            Assert(machine.Ppi.PortAInput && machine.Ppi.PortCUpperInput,
                "restore must reapply direction bits");
            Assert(machine.Ppi.InputA == 0x11 && machine.Ppi.InputB == 0x22 && machine.Ppi.InputC == 0xA0,
                "restore must keep external input pins");
            Assert(machine.Ppi.PortB == 0x33 && machine.Ppi.PortC == 0x0F,
                "restore must keep output latches");
            Assert(machine.Ppi.ReadPort(2) == 0xAF, "restored port C must read pins + latch");

            Pass("machine snapshot captures and restores complete PPI state");
        }
    }
}
