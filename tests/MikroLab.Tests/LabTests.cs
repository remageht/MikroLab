using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using MikroLab.Core.Assembler;
using MikroLab.Core.Cpu;
using MikroLab.Core.Machine;
using MikroLab.Presentation;

namespace MikroLab.Tests
{
    /// <summary>
    /// Laboratory catalogue integrity and the UI-independent checker: every
    /// deterministic lab must reproduce its reference A, DE and memory cells.
    /// </summary>
    public static class LabTests
    {
        private static int _passed;

        public static void RunAll()
        {
            Console.WriteLine("[TEST SUITE] LabTests (catalogue, expected results, auto-check)");
            _passed = 0;

            Test_Catalogue_IsContiguousAndUnique();
            Test_DeterministicLabs_MatchExpectations();
            Test_InteractiveLabs_HaveNoReference();
            Test_Check_ReportsMismatch();
            Test_Lab1_RegisterExpectation();

            Console.WriteLine($"  All LabTests PASSED ({_passed} tests).\n");
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

        private static void Test_Catalogue_IsContiguousAndUnique()
        {
            LabDefinition[] labs = LabCatalog.All;
            Assert(labs.Length == 7, "catalogue must contain seven works");

            var titles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var samples = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < labs.Length; i++)
            {
                Assert(labs[i].Number == "Лаб. " + (i + 1), "lab numbers must be contiguous: " + labs[i].Number);
                Assert(!string.IsNullOrWhiteSpace(labs[i].Title), "lab title must not be empty");
                Assert(!string.IsNullOrWhiteSpace(labs[i].Summary), "lab summary must not be empty");
                Assert(titles.Add(labs[i].Title), "lab titles must be unique: " + labs[i].Title);
                Assert(samples.Add(labs[i].SampleName), "lab samples must be unique: " + labs[i].SampleName);
                Assert(SampleLibrary.All.Length > 0, "sample library must not be empty");
                Assert(FindSample(labs[i].SampleName) != null,
                    "lab sample must exist in the library: " + labs[i].SampleName);
            }

            Assert(labs[2].Title == "Логические операции", "lab 3 must be «Логические операции»");
            Assert(labs[3].Title == "Циклы", "lab 4 must be «Циклы»");
            Assert(labs[4].Title == "Порты ВВ55", "lab 5 must be «Порты ВВ55»");
            Assert(labs[5].Title == "Аналоговый датчик", "lab 6 must be the analog lab");
            Assert(labs[6].Title == "Матричная клавиатура", "lab 7 must be the keyboard lab");

            Pass("seven unique labs with clean numbering and sample links");
        }

        private static void Test_DeterministicLabs_MatchExpectations()
        {
            for (int i = 0; i < 4; i++)
            {
                LabDefinition lab = LabCatalog.All[i];
                MachineSnapshot snapshot = RunSample(lab.SampleName);

                LabCheckResult result = LabCheck.Evaluate(lab, snapshot);
                Assert(result.IsDefined, "deterministic lab must define a reference: " + lab.Title);
                Assert(result.Passed,
                    "lab reference must match the sample result: " + lab.Title +
                    " · " + string.Join(" | ", result.Details));
            }

            Pass("labs 1–4 reproduce their A, DE and memory references");
        }

        private static void Test_InteractiveLabs_HaveNoReference()
        {
            for (int i = 4; i < LabCatalog.All.Length; i++)
            {
                LabDefinition lab = LabCatalog.All[i];
                Assert(lab.Expectation.IsEmpty, "interactive lab must not claim a fixed reference: " + lab.Title);

                LabCheckResult result = LabCheck.Evaluate(lab, new MachineSnapshot());
                Assert(!result.IsDefined, "interactive lab check must report an undefined reference");
                Assert(!result.Passed, "interactive lab check must not report success");
                Assert(result.Summary.Contains("эталон не задан"), "undefined reference must be explained");
            }

            Pass("interactive labs 5–7 report «эталон не задан»");
        }

        private static void Test_Check_ReportsMismatch()
        {
            LabDefinition lab = LabCatalog.All[0];
            var memory = new byte[0x10000];
            memory[0x8201] = 0x22;
            memory[0x8202] = 0x11;

            var snapshot = new MachineSnapshot
            {
                A = 0x00,
                D = 0x00,
                E = 0x00,
                Memory = memory
            };

            LabCheckResult result = LabCheck.Evaluate(lab, snapshot);
            Assert(result.IsDefined, "lab 1 must have a reference");
            Assert(!result.Passed, "wrong A/DE must fail the check");
            Assert(result.Details.Any(detail => detail.Contains("ОШИБКА") && detail.Contains("A")),
                "mismatch details must name the failing register");
            Assert(result.Details.Any(detail => detail.Contains("ОШИБКА") && detail.Contains("DE")),
                "mismatch details must name DE");

            Pass("checker reports register mismatches");
        }

        private static void Test_Lab1_RegisterExpectation()
        {
            LabDefinition lab = LabCatalog.All[0];
            Assert(lab.Expectation.A == 0x11, "lab 1 must expect A=11h");
            Assert(lab.Expectation.DE == 0x5555, "lab 1 must expect DE=5555h");
            Assert(lab.Expectation.Memory.Count == 2, "lab 1 must check two memory cells");

            MachineSnapshot snapshot = RunSample(lab.SampleName);
            Assert(snapshot.A == 0x11, "sample must leave A=11h");
            Assert((ushort)((snapshot.D << 8) | snapshot.E) == 0x5555, "sample must leave DE=5555h");
            Assert(snapshot.Memory[0x8201] == 0x22, "sample must store 22h at 8201h");
            Assert(snapshot.Memory[0x8202] == 0x11, "sample must store 11h at 8202h");

            Pass("lab 1 reference matches the register sample");
        }

        private static SampleProgram FindSample(string name)
        {
            foreach (SampleProgram sample in SampleLibrary.All)
            {
                if (string.Equals(sample.Name, name, StringComparison.OrdinalIgnoreCase)) return sample;
            }
            return null;
        }

        private static MachineSnapshot RunSample(string sampleName)
        {
            SampleProgram sample = FindSample(sampleName);
            Assert(sample != null, "sample not found: " + sampleName);

            AssembleResult result = I8080Assembler.Assemble(sample.Code, 0x8000);
            Assert(result.Success, "sample must assemble: " + sampleName);

            var machine = new Machine(profile: CpuProfile.Extended);
            // Match MachineHost.Assemble: a fresh program starts with a clean
            // register bank, so DE is zero for labs that never touch it.
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

            Assert(session.State == RunState.Halted, "sample must halt: " + sampleName);
            return session.CaptureSnapshot();
        }
    }
}
