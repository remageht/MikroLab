using System;
using System.Collections.Generic;
using MikroLab.Core.Assembler;

namespace MikroLab.Presentation
{
    /// <summary>One memory cell expected after a laboratory program finishes.</summary>
    public readonly struct MemoryExpectation
    {
        public ushort Address { get; }
        public byte Value { get; }

        public MemoryExpectation(ushort address, byte value)
        {
            Address = address;
            Value = value;
        }
    }

    /// <summary>
    /// Reference result of a laboratory work. Any member may be omitted when
    /// the result depends on user-controlled devices.
    /// </summary>
    public sealed class LabExpectation
    {
        private static readonly MemoryExpectation[] NoMemory = Array.Empty<MemoryExpectation>();

        public byte? A { get; }
        public ushort? DE { get; }
        public IReadOnlyList<MemoryExpectation> Memory { get; }

        public LabExpectation(byte? a = null, ushort? de = null, params MemoryExpectation[] memory)
        {
            A = a;
            DE = de;
            Memory = memory != null && memory.Length > 0 ? memory : NoMemory;
        }

        public bool IsEmpty => A == null && DE == null && Memory.Count == 0;
    }

    /// <summary>Description of one guided laboratory work.</summary>
    public sealed class LabDefinition
    {
        public string Number { get; }
        public string Title { get; }
        public string Summary { get; }
        public string Description { get; }
        public string SampleName { get; }
        public LabExpectation Expectation { get; }

        public LabDefinition(
            string number,
            string title,
            string summary,
            string description,
            string sampleName,
            LabExpectation expectation = null)
        {
            Number = number;
            Title = title;
            Summary = summary;
            Description = description;
            SampleName = sampleName;
            Expectation = expectation ?? new LabExpectation();
        }

        public override string ToString() => Number + " · " + Title;
    }

    /// <summary>Named programs exposed by the Studio sample picker.</summary>
    public static class SampleLibrary
    {
        public static readonly SampleProgram[] All = SamplePrograms.All;

        public static SampleProgram Default => All[0];

        public static SampleProgram ByName(string name)
        {
            foreach (SampleProgram sample in All)
            {
                if (string.Equals(sample.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return sample;
                }
            }
            return Default;
        }
    }

    /// <summary>
    /// Six programme works plus a dedicated matrix-keyboard work. Numbering is
    /// contiguous; no lab repeats another lab's sample or title.
    /// </summary>
    public static class LabCatalog
    {
        public static readonly LabDefinition[] All =
        {
            new LabDefinition(
                "Лаб. 1",
                "Память и регистры",
                "Загрузка, пошаговое выполнение, просмотр 64 КБ памяти.",
                "Исследуйте, как программа записывает данные в память и изменяет регистры.\n\n" +
                "1. Нажмите «Собрать», затем «Шаг».\n" +
                "2. Откройте вкладки «Регистры» и «Память».\n" +
                "3. Найдите адрес 8201h и объясните, откуда там появилось значение.\n" +
                "4. Измените байт в памяти двойным кликом.",
                "Регистры (лаб. 1)",
                new LabExpectation(
                    0x11,
                    0x5555,
                    new MemoryExpectation(0x8201, 0x22),
                    new MemoryExpectation(0x8202, 0x11))),
            new LabDefinition(
                "Лаб. 2",
                "Арифметика",
                "Сложение 1…5, флаги и результат в памяти.",
                "Программа складывает числа от 1 до 5. Результат (0Fh) записывается в 8200h.\n\n" +
                "1. Соберите и запустите программу.\n" +
                "2. Проверьте регистр A и флаги C, Z, S, P.\n" +
                "3. Объясните, почему после HLT лампы показывают 0Fh.\n" +
                "4. Измените начальное значение B и повторите опыт.",
                "Сумма 1…5",
                new LabExpectation(0x0F, 0x0000, new MemoryExpectation(0x8200, 0x0F))),
            new LabDefinition(
                "Лаб. 3",
                "Логические операции",
                "AND, OR, XOR и преобразование результата.",
                "Вычислите ((F0h AND 3Ch) OR 03h) XOR FFh пошагово.\n\n" +
                "1. Зафиксируйте A после каждой команды.\n" +
                "2. Проверьте флаги после логических операций.\n" +
                "3. Объясните значение на лампах.",
                "Логические операции",
                new LabExpectation(0xCC, 0x0000, new MemoryExpectation(0x8200, 0xCC))),
            new LabDefinition(
                "Лаб. 4",
                "Циклы",
                "Цикл, условный переход и счётчик.",
                "Программа выполняет цикл 10 раз и формирует результат в памяти.\n\n" +
                "1. Поставьте точку останова на строке LOOP.\n" +
                "2. Посчитайте количество проходов цикла.\n" +
                "3. Измените счётчик B и исследуйте влияние на результат.",
                "Циклы",
                new LabExpectation(0x0F, 0x0000, new MemoryExpectation(0x8200, 0x0F))),
            new LabDefinition(
                "Лаб. 5",
                "Порты ВВ55",
                "ВВ55: IN FAh читает SA1…SA3, OUT F9h управляет лампами порта B.",
                "Подключите переключатели к порту A и наблюдайте за лампами порта B.\n\n" +
                "1. Включите SA1 — на лампах появится 01h.\n" +
                "2. Включите SA1 и SA3 — получите 05h.\n" +
                "3. Объясните формулу SA1 + 2·SA2 + 4·SA3.\n" +
                "4. Измените программу, чтобы инвертировать младшие биты.",
                "Переключатели → лампы"),
            new LabDefinition(
                "Лаб. 6",
                "Аналоговый датчик",
                "АЦП: канал 2, напряжение 0…10 В и лампы порта B.",
                "Программа выбирает канал 2 АЦП, читает код 0…255 и выводит его на лампы.\n\n" +
                "1. Откройте пример «Аналоговый датчик» в студии.\n" +
                "2. На стенде переместите ползунок канала 2.\n" +
                "3. Объясните связь напряжения, кода АЦП и двоичного числа на лампах.\n" +
                "4. Сравните показания при 0 В, 5 В и 10 В.",
                "Аналоговый датчик"),
            new LabDefinition(
                "Лаб. 7",
                "Матричная клавиатура",
                "Сканирование матрицы 4×4 на порту C ВВ55.",
                "Младший полубайт порта C — строки (выходы, активный ноль), " +
                "старший — столбцы (входы, активный ноль).\n\n" +
                "1. Соберите и запустите пример «Матричная клавиатура».\n" +
                "2. Нажмите клавишу на стенде и наблюдайте код 1…16 в 8200h.\n" +
                "3. Поставьте точку останова на метке SCAN и проследите выбор строк.\n" +
                "4. Объясните, почему строки сканируются по одной.",
                "Матричная клавиатура")
        };
    }
}
