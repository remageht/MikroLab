using System;
using System.Diagnostics;
using System.Threading;
using MikroLab.Core.Assembler;
using MikroLab.Core.Cpu;
using MikroLab.Core.Devices;
using MikroLab.Core.Machine;
using MikroLab.Core.Memory;
using MikroLab.Presentation;

namespace MikroLab.Tests
{
    /// <summary>
    /// Matrix keyboard model (rows PC0..PC3, columns PC4..PC7, active low) and
    /// its integration with the KR580IK55 port C input latch.
    /// </summary>
    public static class KeyboardTests
    {
        private static int _passed;

        public static void RunAll()
        {
            Console.WriteLine("[TEST SUITE] KeyboardTests (4×4/3×4 matrix, PPI port C scan)");
            _passed = 0;

            Test_Matrix_ActiveLowScan();
            Test_Matrix_MultipleRows();
            Test_Matrix_Validation();
            Test_Ppi_ReadsKeyboardColumns();
            Test_Host_KeyStateReachesPpiSnapshot();
            Test_KeyboardSample_ReportsKeyCode();

            Console.WriteLine($"  All KeyboardTests PASSED ({_passed} tests).\n");
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

        private static void Test_Matrix_ActiveLowScan()
        {
            var keyboard = new MatrixKeyboard(4, 4);
            Assert(keyboard.Rows == 4 && keyboard.Columns == 4, "keyboard must be 4×4");
            Assert(keyboard.KeyCount == 16, "keyboard must expose 16 keys");
            Assert(keyboard.ReadColumns(0xFE) == 0xF0, "no key must read F0h");

            keyboard.SetKey(0, 0, true);
            Assert(keyboard.IsKeyDown(0, 0), "key 1 must be down");
            Assert(keyboard.FindPressedKey() == 1, "key 1 must map to code 1");
            Assert(keyboard.ReadColumns(0xFE) == 0xE0, "row 0 + column 0 must pull PC4 low");
            Assert(keyboard.ReadColumns(0xFD) == 0xF0, "a different row must not see the key");

            keyboard.SetKey(0, 0, false);
            Assert(keyboard.FindPressedKey() == 0, "released keyboard must report code 0");
            Assert(keyboard.ReadColumns(0xFE) == 0xF0, "released key must read F0h");

            keyboard.SetKey(1, 2, true);
            Assert(keyboard.ReadColumns(0xFD) == 0xB0, "row 1 + column 2 must pull PC6 low");
            Assert(keyboard.FindPressedKey() == 7, "row 1/column 2 must map to code 7");

            Pass("active-low row/column scan");
        }

        private static void Test_Matrix_MultipleRows()
        {
            var keyboard = new MatrixKeyboard(4, 4);
            keyboard.SetKey(0, 1, true);
            keyboard.SetKey(2, 3, true);

            Assert(keyboard.ReadColumns(0xFC) == 0xD0,
                "selecting rows 0 and 1 must merge both column masks");
            Assert(keyboard.ReadColumns(0xFA) == 0x50,
                "selecting rows 0 and 2 must pull PC5 and PC7 low");

            keyboard.Clear();
            Assert(keyboard.FindPressedKey() == 0, "Clear must release every key");

            Pass("multiple selected rows merge column masks");
        }

        private static void Test_Matrix_Validation()
        {
            var small = new MatrixKeyboard(3, 4);
            Assert(small.Rows == 3 && small.Columns == 4, "3×4 keyboard must be supported");

            try
            {
                var invalid = new MatrixKeyboard(5, 4);
                Assert(false, "5 rows must be rejected");
            }
            catch (ArgumentOutOfRangeException)
            {
            }

            try
            {
                var invalid = new MatrixKeyboard(4, 0);
                Assert(false, "0 columns must be rejected");
            }
            catch (ArgumentOutOfRangeException)
            {
            }

            Pass("keyboard dimension validation");
        }

        private static void Test_Ppi_ReadsKeyboardColumns()
        {
            var ppi = new Ppi8255();
            var keyboard = new MatrixKeyboard(4, 4);
            ppi.Keyboard = keyboard;
            ppi.WriteControl(0x98); // PA input, PB output, PC upper input / lower output

            ppi.WritePort(2, 0x0E); // select row 0 (active low)
            ppi.RefreshKeyboard();
            Assert((ppi.ReadPort(2) & 0xF0) == 0xF0, "empty matrix must read FFh columns");

            keyboard.SetKey(0, 2, true);
            ppi.RefreshKeyboard();
            Assert((ppi.ReadPort(2) & 0xF0) == 0xB0, "pressed PC6 must be visible on port C");
            Assert((ppi.ReadPort(2) & 0x0F) == 0x0E, "row select latch must stay intact");

            ppi.WritePort(2, 0x0D); // select row 1
            ppi.RefreshKeyboard();
            Assert((ppi.ReadPort(2) & 0xF0) == 0xF0, "row 0 key must disappear when row 1 is selected");

            Pass("PPI reads keyboard columns on port C");
        }

        private static void Test_Host_KeyStateReachesPpiSnapshot()
        {
            using var host = new MachineHost();
            host.SetKeyState(1, 1, true);
            Assert(host.Keyboard.IsKeyDown(1, 1), "host must press the requested key");

            host.WithMachine(machine =>
            {
                machine.Ppi.WriteControl(0x98); // PC upper input, lower output
                machine.Ppi.WritePort(2, 0x0D); // select row 1 (active low)
            });

            MachineSnapshot snapshot = host.Snapshot();
            Assert((snapshot.PpiInputC & 0xF0) == 0xD0,
                "host key state must reach the PPI input latch (expected D0h)");

            host.ClearKeys();
            snapshot = host.Snapshot();
            Assert((snapshot.PpiInputC & 0xF0) == 0xF0,
                "releasing all keys must restore the idle column mask");

            Pass("host key events refresh the PPI snapshot");
        }

        private static void Test_KeyboardSample_ReportsKeyCode()
        {
            AssembleResult result = I8080Assembler.Assemble(SamplePrograms.MatrixKeyboard, 0x8000);
            Assert(result.Success, "matrix keyboard sample must assemble");

            var machine = new Machine(profile: CpuProfile.Extended);
            var keyboard = new MatrixKeyboard(4, 4);
            machine.Ppi.Keyboard = keyboard;
            keyboard.SetKey(1, 1, true); // row-major code 6

            for (int address = 0x8000; address <= 0x83FF; address++)
            {
                machine.Memory.WriteByte((ushort)address, 0);
            }
            foreach (AssembledChunk chunk in result.Chunks)
            {
                for (int i = 0; i < chunk.Data.Length; i++)
                {
                    machine.Memory.WriteByte((ushort)(chunk.Address + i), chunk.Data[i]);
                }
            }
            machine.Cpu.State.PC = result.StartAddress;
            machine.Cpu.State.SP = 0x83F0;

            using var session = new MachineSession(machine);
            session.ThrottleLikeOriginal = false;
            session.InstructionsPerSlice = 500;
            session.Run();

            var watch = Stopwatch.StartNew();
            while (session.State != RunState.Halted && watch.ElapsedMilliseconds < 3000)
            {
                Thread.Sleep(1);
            }

            Assert(session.State == RunState.Halted, "keyboard sample must halt after finding a key");
            Assert(machine.Memory.ReadByte(0x8200) == 6, "sample must store key code 6 at 8200h");
            Assert(machine.Ppi.PortB == 6, "sample must show key code 6 on port B lamps");

            Pass("keyboard sample scans the matrix and reports a key code");
        }
    }
}
