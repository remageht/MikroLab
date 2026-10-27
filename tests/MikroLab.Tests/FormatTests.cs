using System;
using System.IO;
using System.Text;
using MikroLab.Core.Cpu;
using MikroLab.Core.Formats;
using MikroLab.Core.Machine;

namespace MikroLab.Tests
{
    public static class FormatTests
    {
        public static void RunAll()
        {
            Console.WriteLine("[TEST SUITE] FormatTests (Intel HEX & machine state)");
            Test_IntelHex_RoundTrip();
            Test_IntelHex_ExtendedAndStartAddresses();
            Test_IntelHex_RejectsMalformedInput();
            Test_MachineState_RoundTrip_CallStack();
            Test_MachineState_RoundTrip_Halted();
            Test_MachineState_RejectsMalformedInput();
            Console.WriteLine("  All FormatTests PASSED.\n");
        }

        private static void Test_IntelHex_RoundTrip()
        {
            var memory = new byte[0x10000];
            for (int i = 0; i < 256; i++)
            {
                memory[0x8000 + i] = (byte)(i ^ 0x5A);
            }

            string text = IntelHex.Write(memory, 0x8000, 0x80FF);
            Assert(text.Contains(":00000001FF"), "HEX output must end with an EOF record");

            IntelHexImage image = IntelHex.Parse(text);
            Assert(image.ByteCount == 256, "Round-trip byte count mismatch");
            Assert(image.StartAddress == null, "Writer must not emit a start record by default");

            byte[] flat = image.ToMemoryImage();
            for (int i = 0; i < 256; i++)
            {
                Assert(flat[0x8000 + i] == memory[0x8000 + i],
                    "Round-trip byte mismatch at " + (0x8000 + i).ToString("X4"));
            }
            Assert(flat[0x8100] == 0, "Bytes outside the exported range must stay zero");
            Console.WriteLine("  [PASS] Intel HEX write/parse round trip");
        }

        private static void Test_IntelHex_ExtendedAndStartAddresses()
        {
            string segmentImage = Join(
                Record(0x02, 0, 0x08, 0x00),
                Record(0x00, 0x0010, 0xAA, 0xBB),
                Record(0x05, 0, 0x00, 0x00, 0x80, 0x20),
                Record(0x01, 0));

            IntelHexImage image = IntelHex.Parse(segmentImage);
            Assert(image.StartAddress == 0x8020, "Type 05 start address mismatch");
            byte[] flat = image.ToMemoryImage();
            Assert(flat[0x8010] == 0xAA && flat[0x8011] == 0xBB,
                "Extended segment address was not applied");

            string linearImage = Join(
                Record(0x04, 0, 0x00, 0x00),
                Record(0x00, 0x8000, 0x11),
                Record(0x03, 0, 0x00, 0x00, 0x80, 0x00),
                Record(0x01, 0));

            IntelHexImage linear = IntelHex.Parse(linearImage);
            Assert(linear.StartAddress == 0x8000, "Type 03 start address mismatch");
            Assert(linear.ToMemoryImage()[0x8000] == 0x11, "Extended linear address was not applied");

            string withStart = IntelHex.Write(new byte[0x10000], 0x8000, 0x800F,
                includeStartAddress: true);
            Assert(IntelHex.Parse(withStart).StartAddress == 0x8000,
                "Optional start record must survive a round trip");
            Console.WriteLine("  [PASS] Intel HEX extended and start address records");
        }

        private static void Test_IntelHex_RejectsMalformedInput()
        {
            string good = Record(0x00, 0, 1, 2, 3, 4);
            char last = good[good.Length - 1];
            string badChecksum = good.Substring(0, good.Length - 1) + (last == '0' ? "1" : "0");

            ExpectFormatError(badChecksum + "\r\n" + Record(0x01, 0), "checksum");
            ExpectFormatError(good, "missing EOF");
            ExpectFormatError("00000001FF\r\n", "missing colon");
            ExpectFormatError(Record(0x01, 0) + "\r\n" + good, "data after EOF");
            ExpectFormatError(Join(
                Record(0x04, 0, 0x00, 0x01),
                Record(0x00, 0x0000, 1),
                Record(0x01, 0)), "address beyond 64K");
            ExpectFormatError(Record(0x01, 0) + "\r\n" + Record(0x07, 0), "unknown record");
            Console.WriteLine("  [PASS] Intel HEX rejects malformed input");
        }

        private static void Test_MachineState_RoundTrip_CallStack()
        {
            var machine = new Machine(profile: CpuProfile.Extended);
            machine.Memory.WriteByte(0x8000, 0xCD); // CALL 8005h
            machine.Memory.WriteByte(0x8001, 0x05);
            machine.Memory.WriteByte(0x8002, 0x80);
            machine.Memory.WriteByte(0x8005, 0xC9); // RET
            machine.Cpu.State.PC = 0x8000;
            machine.Cpu.State.SP = 0x83F0;

            Assert(machine.Cpu.Step(), "CALL must execute");
            Assert(machine.Cpu.CallStackDepth == 1, "CALL must push the debug stack");

            MachineSnapshot snapshot = machine.Capture();
            Assert(snapshot.CallStack.Length == 1 && snapshot.CallStack[0] == 0x8003,
                "Capture must persist the debug call stack");

            string json = MachineStateSerializer.Serialize(snapshot, true, 0x8000);
            MachineStateFile file = MachineStateSerializer.Deserialize(json);
            Assert(file.ProgramLoaded, "ProgramLoaded flag must survive serialization");
            Assert(file.EntryPoint == 0x8000, "Entry point must survive serialization");

            var restored = new Machine(profile: CpuProfile.Extended);
            restored.Restore(file.Snapshot);
            Assert(restored.Cpu.CallStackDepth == 1, "Restore must rebuild the debug stack");
            Assert(restored.Cpu.GetCallStack()[0] == 0x8003, "Restored return address mismatch");
            Assert(restored.Cpu.State.PC == 0x8005, "Restored PC mismatch");

            Assert(restored.Cpu.Step(), "RET must execute after restore");
            Assert(restored.Cpu.State.PC == 0x8003, "RET must return to the saved address");
            Assert(restored.Cpu.CallStackDepth == 0, "RET must pop the restored stack");
            Console.WriteLine("  [PASS] Machine state round trip preserves call stack");
        }

        private static void Test_MachineState_RoundTrip_Halted()
        {
            var machine = new Machine(profile: CpuProfile.Extended);
            machine.Memory.WriteByte(0x8000, 0x76); // HLT
            machine.Cpu.State.PC = 0x8000;
            Assert(!machine.Step(), "HLT must stop the machine");
            Assert(machine.State == RunState.Halted, "Machine state must be Halted");

            MachineSnapshot snapshot = machine.Capture();
            Assert(snapshot.Halted, "Snapshot must record the HLT latch");

            var restored = new Machine(profile: CpuProfile.Extended);
            restored.Restore(MachineStateSerializer.Deserialize(
                MachineStateSerializer.Serialize(snapshot)).Snapshot);

            Assert(restored.State == RunState.Halted, "Restore must preserve Halted");
            Assert(!restored.Cpu.Step(), "Restored HLT latch must block execution");

            restored.Reset();
            restored.Memory.WriteByte(0x8000, 0x00); // NOP
            restored.Cpu.State.PC = 0x8000;
            Assert(restored.Cpu.Step(), "Reset must clear the restored HLT latch");
            Console.WriteLine("  [PASS] Machine state round trip preserves HLT");
        }

        private static void Test_MachineState_RejectsMalformedInput()
        {
            ExpectStateError("{}", "empty root");
            ExpectStateError(
                "{\"format\":\"Other\",\"version\":1,\"snapshot\":{}}",
                "wrong format");
            ExpectStateError(
                "{\"format\":\"MikroLab.MachineState\",\"version\":99,\"snapshot\":{}}",
                "wrong version");
            ExpectStateError(
                "{\"format\":\"MikroLab.MachineState\",\"version\":1,\"snapshot\":{\"memory\":\"AAAA\"}}",
                "short memory");
            ExpectStateError(
                "{\"format\":\"MikroLab.MachineState\",\"version\":1}",
                "missing snapshot");
            ExpectStateError("not json", "invalid JSON");
            Console.WriteLine("  [PASS] Machine state rejects malformed input");
        }

        private static string Join(params string[] records)
        {
            return string.Join("\r\n", records);
        }

        private static string Record(byte type, ushort address, params byte[] data)
        {
            var builder = new StringBuilder();
            int sum = data.Length + ((address >> 8) & 0xFF) + (address & 0xFF) + type;
            builder.Append(':');
            builder.Append(data.Length.ToString("X2"));
            builder.Append(address.ToString("X4"));
            builder.Append(type.ToString("X2"));
            foreach (byte value in data)
            {
                builder.Append(value.ToString("X2"));
                sum += value;
            }
            builder.Append(((byte)(-sum & 0xFF)).ToString("X2"));
            return builder.ToString();
        }

        private static void ExpectFormatError(string text, string what)
        {
            try
            {
                IntelHex.Parse(text);
            }
            catch (FormatException)
            {
                return;
            }
            throw new InvalidOperationException("Expected Intel HEX failure: " + what);
        }

        private static void ExpectStateError(string json, string what)
        {
            try
            {
                MachineStateSerializer.Deserialize(json);
            }
            catch (InvalidDataException)
            {
                return;
            }
            throw new InvalidOperationException("Expected state failure: " + what);
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException("Assertion failed: " + message);
        }
    }
}
