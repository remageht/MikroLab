using System;
using System.Collections.Generic;
using MikroLab.Core.Cpu;
using MikroLab.Core.Devices;
using MikroLab.Core.Machine;
using MikroLab.Core.Memory;

namespace MikroLab.Tests
{
    /// <summary>
    /// Stage 1 architecture tests: devices are reached only through the I/O
    /// and memory buses, never through hardcoded CPU addresses.
    /// </summary>
    public static class BusTests
    {
        private static int _passed;

        public static void RunAll()
        {
            Console.WriteLine("[TEST SUITE] BusTests (IIoBus, IPortDevice, memory-mapped devices)");
            _passed = 0;

            Test_IoBus_DispatchByRange();
            Test_IoBus_MultipleRangesAndUnregister();
            Test_IoBus_RejectsOverlapAndInvalidPorts();
            Test_MappedMemory_DirectionSpecificWindows();
            Test_MappedMemory_RejectsOverlap();
            Test_Machine_RegistersDevices();
            Test_ExtendedCpu_UsesTheBus();
            Test_LegacyCpu_UsesTheBus();

            Console.WriteLine($"  All BusTests PASSED ({_passed} tests).\n");
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

        public static void Test_IoBus_DispatchByRange()
        {
            var bus = new IoBus();
            var first = new RecordingPortDevice(0xA5);
            var second = new RecordingPortDevice(0x5A);
            bus.Register(first, 0x10, 0x1F);
            bus.Register(second, 0x20, 0x2F);

            bus.WritePort(0x15, 0x42);
            Assert(first.WriteCount == 1, "write should reach the first device");
            Assert(first.LastPort == 0x15 && first.LastValue == 0x42, "write port/value mismatch");
            Assert(second.WriteCount == 0, "write must not leak to another range");

            Assert(bus.ReadPort(0x15) == 0xA5, "read should be served by the first device");
            Assert(bus.ReadPort(0x25) == 0x5A, "read should be served by the second device");
            Assert(bus.ReadPort(0x35) == 0xFF, "unmapped reads must float high");
            bus.WritePort(0x35, 0x99); // must be silently ignored

            Assert(bus.IsMapped(0x10) && bus.IsMapped(0x1F), "range boundaries must be inclusive");
            Assert(!bus.IsMapped(0x35), "unmapped port reported as mapped");
            Assert(bus.Devices.Count == 2, "both devices should be listed");

            Pass("I/O bus dispatch by inclusive port range");
        }

        public static void Test_IoBus_MultipleRangesAndUnregister()
        {
            var bus = new IoBus();
            var device = new RecordingPortDevice(0x77);
            bus.Register(device, 0x01, 0x02);
            bus.Register(device, 0xF9, 0xFA);

            bus.WritePort(0x01, 0x11);
            bus.WritePort(0xF9, 0x22);
            Assert(device.WriteCount == 2, "a device may own several disjoint ranges");
            Assert(device.LastValue == 0x22, "second range write mismatch");

            bus.Unregister(device);
            Assert(bus.Devices.Count == 0, "unregister must remove all ranges");
            Assert(bus.ReadPort(0xFA) == 0xFF, "unregistered port must float high");
            bus.WritePort(0x01, 0x33);
            Assert(device.WriteCount == 2, "writes after unregister must be ignored");

            Pass("multiple ranges and unregister");
        }

        public static void Test_IoBus_RejectsOverlapAndInvalidPorts()
        {
            var bus = new IoBus();
            var first = new RecordingPortDevice(0x00);
            var second = new RecordingPortDevice(0x00);
            bus.Register(first, 0x10, 0x20);

            Expect<InvalidOperationException>(
                () => bus.Register(second, 0x20, 0x2F),
                "overlapping port ranges must be rejected");
            Expect<ArgumentOutOfRangeException>(
                () => bus.Register(second, -1, 0x10),
                "negative ports must be rejected");
            Expect<ArgumentOutOfRangeException>(
                () => bus.Register(second, 0x10, 0x100),
                "ports above FFh must be rejected");
            Expect<ArgumentException>(
                () => bus.Register(second, 0x20, 0x10),
                "reversed ranges must be rejected");

            Pass("overlap and invalid-range validation");
        }

        public static void Test_MappedMemory_DirectionSpecificWindows()
        {
            var raw = new NibbleMemory();
            var memory = new MappedMemory(raw);
            var device = new RecordingMemoryDevice(0x5A);
            memory.Register(device, 0x1000, 0x1000, MemoryAccess.Read);
            memory.Register(device, 0x2000, 0x2000, MemoryAccess.Write);

            memory.WriteByte(0x2000, 0x7E);
            Assert(device.LastWriteAddress == 0x2000 && device.LastWriteValue == 0x7E,
                "write window should reach the device");
            Assert(raw.ReadByte(0x2000) == 0x7E, "writes must stay visible in raw RAM");
            Assert(memory.ReadByte(0x2000) == 0x7E, "write-only window should read from RAM");

            memory.WriteByte(0x1000, 0x99);
            Assert(device.WriteCount == 1, "read-only window must not receive writes");
            Assert(raw.ReadByte(0x1000) == 0x99, "underlying RAM should still be updated");
            Assert(memory.ReadByte(0x1000) == 0x5A, "read window should be served by the device");

            byte unmapped = memory.ReadByte(0x3000);
            Assert(unmapped == raw.ReadByte(0x3000), "unmapped reads must use raw RAM");
            Assert(memory.IsMapped(0x1000, MemoryAccess.Read), "read window not reported");
            Assert(!memory.IsMapped(0x1000, MemoryAccess.Write), "read window reported as writable");

            Pass("memory-mapped windows with independent read/write directions");
        }

        public static void Test_MappedMemory_RejectsOverlap()
        {
            var memory = new MappedMemory(new NibbleMemory());
            var first = new RecordingMemoryDevice(0x00);
            var second = new RecordingMemoryDevice(0x00);

            memory.Register(first, 0x1000, 0x10FF, MemoryAccess.ReadWrite);
            Expect<InvalidOperationException>(
                () => memory.Register(second, 0x10F0, 0x1100, MemoryAccess.Read),
                "overlapping read windows must be rejected");
            Expect<InvalidOperationException>(
                () => memory.Register(second, 0x1000, 0x10FF, MemoryAccess.ReadWrite),
                "ReadWrite registration must check both directions");

            memory.Register(second, 0x2000, 0x20FF, MemoryAccess.Read);
            memory.Register(second, 0x2000, 0x20FF, MemoryAccess.Write);
            Assert(memory.IsMapped(0x2000, MemoryAccess.ReadWrite),
                "separate read/write registration should be allowed");

            Pass("memory window overlap validation");
        }

        public static void Test_Machine_RegistersDevices()
        {
            var machine = new Machine(profile: CpuProfile.Extended);

            Assert(machine.IoBus.Devices.Count >= 2, "machine should expose its registered port devices");
            Assert(machine.IoBus.IsMapped(0xFA), "PPI input port must be on the bus");
            Assert(machine.IoBus.IsMapped(0x01) && machine.IoBus.IsMapped(0xF9),
                "legacy lamp ports must be on the bus");
            Assert(machine.IoBus.IsMapped(0xFD) && machine.IoBus.IsMapped(0xFE),
                "analog ports must be on the bus");

            Assert(machine.AddressSpace.IsMapped(0xFA, MemoryAccess.Read),
                "legacy PPI input must be memory-mapped");
            Assert(machine.AddressSpace.IsMapped(0x01, MemoryAccess.Write),
                "legacy lamp write must be memory-mapped");
            Assert(machine.AddressSpace.IsMapped(MemoryMap.DisplayLatchStart, MemoryAccess.ReadWrite),
                "display latches must be memory-mapped");

            Pass("Machine registers PPI, analog and display devices on both buses");
        }

        public static void Test_ExtendedCpu_UsesTheBus()
        {
            var machine = new Machine(profile: CpuProfile.Extended);
            var device = new RecordingPortDevice(0xC3);
            machine.IoBus.Register(device, 0x42, 0x42);
            machine.Cpu.State.PC = 0x8000;
            machine.Cpu.State.A = 0x3C;

            machine.Memory.WriteByte(0x8000, 0xD3); // OUT 42h
            machine.Memory.WriteByte(0x8001, 0x42);
            Assert(machine.Step(), "OUT should continue execution");
            Assert(device.LastPort == 0x42 && device.LastValue == 0x3C,
                "ExtendedCpu OUT must go through IIoBus");

            machine.Memory.WriteByte(0x8002, 0xDB); // IN 42h
            machine.Memory.WriteByte(0x8003, 0x42);
            machine.Cpu.State.PC = 0x8002;
            Assert(machine.Step(), "IN should continue execution");
            Assert(machine.Cpu.State.A == 0xC3, "ExtendedCpu IN must read through IIoBus");

            machine.Memory.WriteByte(0x8004, 0xDB); // IN 43h (unmapped)
            machine.Memory.WriteByte(0x8005, 0x43);
            machine.Cpu.State.PC = 0x8004;
            Assert(machine.Step(), "unmapped IN should continue execution");
            Assert(machine.Cpu.State.A == 0xFF, "unmapped IN must float high");

            Pass("ExtendedCpu IN/OUT through the I/O bus");
        }

        public static void Test_LegacyCpu_UsesTheBus()
        {
            var machine = new Machine();
            var device = new RecordingPortDevice(0x81);
            machine.IoBus.Register(device, 0x42, 0x42);
            machine.Cpu.State.PC = 0x8000;
            machine.Cpu.State.A = 0x24;

            machine.Memory.WriteByte(0x8000, 0xD3); // OUT 42h
            machine.Memory.WriteByte(0x8001, 0x42);
            Assert(machine.Step(), "legacy OUT should continue execution");
            Assert(device.LastPort == 0x42 && device.LastValue == 0x24,
                "LegacyCpu OUT must go through IIoBus");

            machine.Memory.WriteByte(0x8002, 0xDB); // IN 42h
            machine.Memory.WriteByte(0x8003, 0x42);
            machine.Cpu.State.PC = 0x8002;
            Assert(machine.Step(), "legacy IN should continue execution");
            Assert(machine.Cpu.State.A == 0x81, "LegacyCpu IN must read through IIoBus");

            Pass("LegacyCpu IN/OUT through the I/O bus");
        }

        private sealed class RecordingPortDevice : IPortDevice
        {
            private readonly byte _readValue;

            public RecordingPortDevice(byte readValue)
            {
                _readValue = readValue;
            }

            public string Name => "Recording port device";
            public int WriteCount { get; private set; }
            public int LastPort { get; private set; }
            public byte LastValue { get; private set; }

            public byte ReadPort(int port)
            {
                return _readValue;
            }

            public void WritePort(int port, byte value)
            {
                WriteCount++;
                LastPort = port;
                LastValue = value;
            }
        }

        private sealed class RecordingMemoryDevice : IMemoryMappedDevice
        {
            private readonly byte _readValue;

            public RecordingMemoryDevice(byte readValue)
            {
                _readValue = readValue;
            }

            public string Name => "Recording memory device";
            public int WriteCount { get; private set; }
            public int LastWriteAddress { get; private set; }
            public byte LastWriteValue { get; private set; }

            public byte ReadMemory(int address)
            {
                return _readValue;
            }

            public void WriteMemory(int address, byte value)
            {
                WriteCount++;
                LastWriteAddress = address;
                LastWriteValue = value;
            }
        }
    }
}
