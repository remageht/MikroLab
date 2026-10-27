using System;

namespace MikroLab.Core.Assembler
{
    /// <summary>
    /// A named assembler example that can be loaded by any host.
    /// </summary>
    public sealed class SampleProgram
    {
        public string Name { get; }
        public string Code { get; }

        public SampleProgram(string name, string code)
        {
            Name = name ?? string.Empty;
            Code = code ?? string.Empty;
        }

        public override string ToString() => Name;
    }

    /// <summary>
    /// Ready-to-run programs shared by the Studio UI, tests and automation.
    /// All examples start at 8000h and only use documented 8080 instructions.
    /// </summary>
    public static class SamplePrograms
    {
        public static readonly SampleProgram[] All =
        {
            new SampleProgram("Бегущие огни", RunningLight),
            new SampleProgram("Сумма 1…5", SumOneToFive),
            new SampleProgram("Переключатели → лампы", TumblersToLamps),
            new SampleProgram("Регистры (лаб. 1)", RegisterLab)
        };

        public static string Default => All[0].Code;

        public const string NewTemplate =
@"; MikroLab Studio · новый проект
; ORG задаёт адрес загрузки программы.

        ORG 8000h

START:  MVI A, 00h
        OUT 01h
        HLT

        END
";

        public const string RunningLight =
@"; MikroLab Studio · пример: бегущие огни
; Лампы подключены к порту 01h (ВВ55, порт A)
;
; F7 — собрать, F5 — пуск, F6 — пауза, F8 — шаг,
; F9 — точка останова, Ctrl+R — сброс.

        ORG 8000h

START:  MVI A, 01h        ; начальный огонь
        MVI B, 08h        ; восемь позиций

SHIFT:  OUT 01h           ; вывести A на лампы
        CALL DELAY        ; небольшая задержка
        RLC               ; сдвинуть огонь влево
        DCR B
        JNZ SHIFT
        JMP START         ; бесконечный цикл

; Пустой цикл задержки, ~2–5 мс
DELAY:  MVI C, 0C0h
DLOOP:  DCR C
        JNZ DLOOP
        RET

        END
";

        public const string SumOneToFive =
@"; MikroLab Studio · пример: сумма 1+2+3+4+5
; Результат (0Fh) записывается в память 8200h
; и выводится на лампы порта 01h.

        ORG 8000h

        MVI A, 00h        ; аккумулятор = 0
        MVI B, 05h        ; слагаемое = 5

LOOP:   ADD B             ; A = A + B
        DCR B             ; B = B - 1
        JNZ LOOP          ; повторять, пока B != 0

        STA 8200h         ; сохранить сумму в память
        OUT 01h           ; показать сумму на лампах
        HLT               ; остановить процессор

        END
";

        public const string TumblersToLamps =
@"; MikroLab Studio · пример: переключатели → лампы
; IN 0FAh читает состояние трёх переключателей,
; OUT 01h выводит его на светодиоды.
;
; Переключатели управляются на вкладке «Устройства».

        ORG 8000h

LOOP:   IN 0FAh           ; SA1..SA3 → A
        OUT 01h           ; A → лампы
        JMP LOOP

        END
";

        public const string RegisterLab =
@"; MikroLab Studio · лабораторная 1: регистры
; Загрузка регистров и пересылка данных.

        ORG 8000h

        MVI A, 11h
        MVI B, 22h
        MVI C, 33h
        MVI D, 44h
        MVI E, 55h
        MVI H, 80h
        MVI L, 00h
        MOV M, A          ; 8000h ← A

        MVI A, 00h
        MOV A, B
        MOV B, C
        MOV C, D
        MOV D, E

        STA 8201h         ; сохранить A
        MOV A, M          ; A ← 8000h
        STA 8202h
        HLT

        END
";
    }
}
