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
    /// Examples use explicit ORG directives; the UMK sample demonstrates the
    /// alternative 0800h profile.
    /// </summary>
    public static class SamplePrograms
    {
        public static readonly SampleProgram[] All =
        {
            new SampleProgram("Бегущие огни", RunningLight),
            new SampleProgram("Сумма 1…5", SumOneToFive),
            new SampleProgram("Переключатели → лампы", TumblersToLamps),
            new SampleProgram("Регистры (лаб. 1)", RegisterLab),
            new SampleProgram("Индикатор: 1234", Display1234),
            new SampleProgram("Счётчик 0…F", DisplayCounter),
            new SampleProgram("Логические операции", LogicLab),
            new SampleProgram("Циклы", LoopsLab),
            new SampleProgram("Порты ВВ55", PortsLab),
            new SampleProgram("Аналоговый датчик", AnalogSensor),
            new SampleProgram("Матричная клавиатура", MatrixKeyboard),
            new SampleProgram("УМК · огни с 0800h", UmkRunningLight)
        };

        public static string Default => All[0].Code;

        /// <summary>Generates the new-project template for a board profile.</summary>
        public static string NewTemplateFor(ushort origin)
        {
            return
@"; MikroLab Studio · новый проект
; ORG задаёт адрес загрузки программы.

        ORG " + origin.ToString("X4") + @"h

START:  MVI A, 90h        ; ВВ55: PA — вход, PB/PC — выход
        OUT 0FBh          ; записать управляющее слово
        MVI A, 00h
        OUT 0F9h          ; погасить лампы порта B
        HLT

        END
";
        }

        public static string NewTemplate => NewTemplateFor(0x8000);

        public const string RunningLight =
@"; MikroLab Studio · пример: бегущие огни
; Лампы подключены к порту B ВВ55 (OUT F9h).
;
; F7 — собрать, F5 — пуск, F6 — пауза, F8 — шаг,
; F9 — точка останова, Ctrl+R — сброс.

        ORG 8000h

START:  MVI A, 90h        ; PA — вход, PB/PC — выход
        OUT 0FBh          ; управляющее слово ВВ55
        MVI A, 01h        ; начальный огонь
        MVI B, 08h        ; восемь позиций

SHIFT:  OUT 0F9h          ; вывести A на лампы порта B
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
; и выводится на лампы порта B (OUT F9h).

        ORG 8000h

        MVI A, 90h        ; PA — вход, PB/PC — выход
        OUT 0FBh          ; управляющее слово ВВ55

        MVI A, 00h        ; аккумулятор = 0
        MVI B, 05h        ; слагаемое = 5

LOOP:   ADD B             ; A = A + B
        DCR B             ; B = B - 1
        JNZ LOOP          ; повторять, пока B != 0

        STA 8200h         ; сохранить сумму в память
        OUT 0F9h          ; показать сумму на лампах
        HLT               ; остановить процессор

        END
";

        public const string TumblersToLamps =
@"; MikroLab Studio · пример: переключатели → лампы
; IN 0FAh читает PA0..PA2 (SA1..SA3), OUT 0F9h выводит
; значение на светодиоды порта B.
;
; Переключатели управляются на вкладке «Устройства».

        ORG 8000h

        MVI A, 90h        ; PA — вход, PB/PC — выход
        OUT 0FBh          ; управляющее слово ВВ55

LOOP:   IN 0FAh           ; SA1..SA3 → A
        OUT 0F9h          ; A → лампы порта B
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

        public const string MatrixKeyboard =
@"; MikroLab Studio · матричная клавиатура 4×4
; PC0..PC3 — строки (выходы, активный ноль),
; PC4..PC7 — столбцы (входы, активный ноль).
; Код нажатой клавиши 1…16 выводится в 8200h и на лампы.

        ORG 8000h

        MVI A, 98h        ; PA — вход, PB — выход, PC: старший вход / младший выход
        OUT 0FBh

SCAN:   MVI B, 00h        ; код строки: 0, 4, 8, 12
        MVI C, 0FEh       ; активная строка 0

ROW:    MOV A, C
        OUT 0F8h          ; выбрать строку
        IN 0F8h           ; прочитать столбцы
        MOV E, A
        ANI 0F0h
        CPI 0F0h
        JNZ FOUND         ; есть нажатая клавиша

        INR B
        INR B
        INR B
        INR B
        MOV A, C
        RLC               ; следующая строка
        MOV C, A
        MOV A, B
        CPI 10h
        JNZ ROW
        JMP SCAN

FOUND:  MOV A, E
        RRC
        RRC
        RRC
        RRC               ; столбцы в младшем полубайте
        MOV E, A
        MVI D, 01h

COL:    MOV A, E
        RRC
        MOV E, A
        JNC KEY           ; ноль = нажатый столбец
        INR D
        MOV A, D
        CPI 05h
        JZ SCAN
        JMP COL

KEY:    MOV A, B
        ADD D
        STA 8200h
        OUT 0F9h
        HLT

        END
";

        public const string LogicLab =
@"; MikroLab Studio · логические операции
; A = ((F0h AND 3Ch) OR 03h) XOR FFh

        ORG 8000h

        MVI A, 90h
        OUT 0FBh

        MVI A, 0F0h
        ANI 3Ch
        ORI 03h
        XRI 0FFh

        STA 8200h
        OUT 0F9h
        HLT

        END
";

        public const string LoopsLab =
@"; MikroLab Studio · циклы
; Сумма 1..5, результат в 8200h и на лампах.

        ORG 8000h

        MVI B, 05h
        MVI A, 00h

LOOP:   ADD B
        DCR B
        JNZ LOOP

        STA 8200h
        OUT 0F9h
        HLT

        END
";

        public const string PortsLab =
@"; MikroLab Studio · порты ВВ55
; Чтение порта A и вывод на лампы порта B.

        ORG 8000h

        MVI A, 90h
        OUT 0FBh

        IN 0FAh
        STA 8200h
        OUT 0F9h
        HLT

        END
";

        public const string UmkRunningLight =
@"; MikroLab Studio · пример для профиля УМК (ORG 0800h)
; Программа загружается в область монитора 0800h.
; Лампы подключены к порту B ВВ55 (OUT F9h).

        ORG 0800h

START:  MVI A, 90h        ; PA — вход, PB/PC — выход
        OUT 0FBh          ; управляющее слово ВВ55
        MVI A, 01h        ; начальный огонь
        MVI B, 08h        ; восемь позиций

SHIFT:  OUT 0F9h          ; вывести A на лампы порта B
        CALL DELAY
        RLC
        DCR B
        JNZ SHIFT
        JMP START

DELAY:  MVI C, 0C0h
DLOOP:  DCR C
        JNZ DLOOP
        RET

        END
";
    }
}
