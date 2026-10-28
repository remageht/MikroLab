using System;
using System.Collections.Generic;
using System.Globalization;
using MikroLab.Core.Machine;

namespace MikroLab.Presentation
{
    /// <summary>Outcome of comparing a machine snapshot with a lab expectation.</summary>
    public sealed class LabCheckResult
    {
        public bool IsDefined { get; }
        public bool Passed { get; }
        public IReadOnlyList<string> Details { get; }

        public LabCheckResult(bool isDefined, bool passed, IReadOnlyList<string> details)
        {
            IsDefined = isDefined;
            Passed = passed;
            Details = details ?? Array.Empty<string>();
        }

        public string Summary
        {
            get
            {
                if (!IsDefined) return "Для этой работы эталон не задан";
                return Passed ? "Проверка пройдена" : "Есть расхождения";
            }
        }
    }

    /// <summary>
    /// UI-independent laboratory checker. Compares A, DE and selected memory
    /// cells with the reference values declared by <see cref="LabDefinition"/>.
    /// </summary>
    public static class LabCheck
    {
        public static LabCheckResult Evaluate(LabDefinition lab, MachineSnapshot snapshot)
        {
            if (lab == null) throw new ArgumentNullException(nameof(lab));
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            LabExpectation expectation = lab.Expectation ?? new LabExpectation();
            ushort de = (ushort)((snapshot.D << 8) | snapshot.E);

            if (expectation.IsEmpty)
            {
                return new LabCheckResult(
                    false,
                    false,
                    new[]
                    {
                        "A=" + Hex8(snapshot.A) + "  DE=" + Hex16(de) + "  PC=" + Hex16(snapshot.PC),
                        "Эталон задан только для работ с детерминированным результатом."
                    });
            }

            var details = new List<string>();
            bool passed = true;

            if (expectation.A.HasValue)
            {
                bool ok = snapshot.A == expectation.A.Value;
                passed &= ok;
                details.Add(Describe("A", Hex8(snapshot.A), Hex8(expectation.A.Value), ok));
            }

            if (expectation.DE.HasValue)
            {
                bool ok = de == expectation.DE.Value;
                passed &= ok;
                details.Add(Describe("DE", Hex16(de), Hex16(expectation.DE.Value), ok));
            }

            foreach (MemoryExpectation cell in expectation.Memory)
            {
                byte actual = snapshot.Memory != null && cell.Address < snapshot.Memory.Length
                    ? snapshot.Memory[cell.Address]
                    : (byte)0;
                bool ok = snapshot.Memory != null &&
                          cell.Address < snapshot.Memory.Length &&
                          actual == cell.Value;
                passed &= ok;
                details.Add(Describe(
                    "[" + Hex16(cell.Address) + "]",
                    snapshot.Memory == null ? "нет памяти" : Hex8(actual),
                    Hex8(cell.Value),
                    ok));
            }

            return new LabCheckResult(true, passed, details);
        }

        private static string Describe(string name, string actual, string expected, bool ok)
        {
            return (ok ? "OK  " : "ОШИБКА  ") + name + ": " + actual +
                   (ok ? string.Empty : " · ожидалось " + expected);
        }

        private static string Hex8(byte value) => value.ToString("X2", CultureInfo.InvariantCulture) + "h";

        private static string Hex16(ushort value) => value.ToString("X4", CultureInfo.InvariantCulture) + "h";
    }
}
