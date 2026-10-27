using System;
using System.Collections.Generic;
using MikroLab.Core.Assembler;

namespace MikroLab.App
{
    internal sealed class LabDefinition
    {
        public string Number { get; }
        public string Title { get; }
        public string Summary { get; }
        public string Description { get; }
        public string SampleName { get; }

        public LabDefinition(string number, string title, string summary, string description, string sampleName)
        {
            Number = number;
            Title = title;
            Summary = summary;
            Description = description;
            SampleName = sampleName;
        }

        public override string ToString() => Number + " · " + Title;
    }

    internal static class SampleLibrary
    {
        public static readonly SampleProgram[] All =
        {
            new SampleProgram("Бегущие огни", SamplePrograms.RunningLight),
            new SampleProgram("Сумма 1…5", SamplePrograms.SumOneToFive),
            new SampleProgram("Переключатели → лампы", SamplePrograms.TumblersToLamps),
            new SampleProgram("Регистры (лаб. 1)", SamplePrograms.RegisterLab),
            new SampleProgram("Индикатор: 1234", Display1234),
            new SampleProgram("Счётчик 0…F", DisplayCounter),
            new SampleProgram("Аналоговый датчик", AnalogSensor),
            new SampleProgram("Логические операции", LogicLab)
        };

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

        public const string Display1234 =
@"; MikroLab Studio · индикаторы
; 83F4h..83F7h — входные полубайты для монитора SEGGG (0100h).
; Каждый байт содержит две цифры: старшая → левее.

        ORG 8000h

        MVI A, 12h
        STA 83F4h
        MVI A, 34h
        STA 83F5h
        MVI A, 00h
        STA 83F6h
        STA 83F7h

        CALL 0100h        ; преобразовать цифры в сегменты
        HLT

        END
";

        public const string DisplayCounter =
@"; MikroLab Studio · счётчик на индикаторах
; Младшая пара цифр показывает 00, 01, … 0F и снова 00.

        ORG 8000h

        MVI B, 00h

LOOP:   MOV A, B
        STA 83F4h
        CALL 0100h
        INR B
        MOV A, B
        CPI 10h
        JNZ LOOP

        MVI B, 00h
        JMP LOOP

        END
";

        public const string AnalogSensor =
@"; MikroLab Studio · аналоговый датчик
; Канал 2 АЦП → аккумулятор → лампы порта B (F9h).
; Значение датчика меняется на вкладке «Устройства».

        ORG 8000h

        MVI A, 90h        ; PA — вход, PB/PC — выход
        OUT 0FBh          ; управляющее слово ВВ55
        MVI A, 02h
        OUT 0FDh          ; выбрать канал 2

LOOP:   IN 0FEh          ; прочитать АЦП
        OUT 0F9h          ; показать на лампах порта B
        JMP LOOP

        END
";

        public const string LogicLab =
@"; MikroLab Studio · логические операции
; A = ((0F0h AND 3Ch) OR 03h) XOR 0FFh
; Результат сохраняется в 8200h и выводится на лампы.

        ORG 8000h

        MVI A, 90h        ; PA — вход, PB/PC — выход
        OUT 0FBh          ; управляющее слово ВВ55

        MVI A, 0F0h
        ANI 3Ch
        ORI 03h
        XRI 0FFh

        STA 8200h
        OUT 0F9h
        HLT

        END
";
    }

    internal static class LabCatalog
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
                "Регистры (лаб. 1)"),
            new LabDefinition(
                "Лаб. 2",
                "Арифметика",
                "Сложение 1…5, флаги и результат в памяти.",
                "Программа складывает числа от 1 до 5. Результат (0Fh) записывается в 8200h.\n\n" +
                "1. Соберите и запустите программу.\n" +
                "2. Проверьте регистр A и флаги C, Z, S, P.\n" +
                "3. Объясните, почему после HLT лампы показывают 0Fh.\n" +
                "4. Измените начальное значение B и повторите опыт.",
                "Сумма 1…5"),
            new LabDefinition(
                "Лаб. 3",
                "Логические операции",
                "AND, OR, XOR и преобразование результата.",
                "Вычислите ((F0h AND 3Ch) OR 03h) XOR FFh пошагово.\n\n" +
                "1. Зафиксируйте A после каждой команды.\n" +
                "2. Проверьте флаги после логических операций.\n" +
                "3. Объясните значение на лампах.",
                "Логические операции"),
            new LabDefinition(
                "Лаб. 4",
                "Циклы и задержки",
                "Цикл, условный переход и счётчик.",
                "Программа выполняет цикл 10 раз и формирует результат в памяти.\n\n" +
                "1. Поставьте точку останова на строке LOOP.\n" +
                "2. Посчитайте количество проходов цикла.\n" +
                "3. Измените счётчик B и исследуйте влияние на результат.",
                "Сумма 1…5"),
            new LabDefinition(
                "Лаб. 5",
                "Переключатели и лампы",
                "ВВ55: IN FAh читает SA1…SA3, OUT F9h управляет лампами порта B.",
                "Подключите переключатели к порту A и наблюдайте за лампами порта B.\n\n" +
                "1. Включите SA1 — на лампах появится 01h.\n" +
                "2. Включите SA1 и SA3 — получите 05h.\n" +
                "3. Объясните формулу SA1 + 2·SA2 + 4·SA3.\n" +
                "4. Измените программу, чтобы инвертировать младшие биты.",
                "Переключатели → лампы"),
            new LabDefinition(
                "Лаб. 6",
                "Аналоговые датчики",
                "АЦП, выбор канала и измерение напряжения.",
                "Выберите канал 2 и подайте напряжение на датчик.\n\n" +
                "1. Перетащите ползунок «Д3» (канал 2).\n" +
                "2. Сравните напряжение со значением АЦП 0…255.\n" +
                "3. Объясните, как программа выводит код АЦП на лампы.\n" +
                "4. Измените канал в программе на 0 или 1.",
                "Аналоговый датчик")
        };
    }
}
