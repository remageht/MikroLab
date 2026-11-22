using System;
using System.Collections.Generic;
using System.Text;
using MikroLab.Core.Assembler;
using MikroLab.Core.Assembler.I8086;
using MikroLab.Core.Cpu.I8086;
using MikroLab.Core.Memory;

namespace MikroLab.Tests
{
    /// <summary>
    /// Tests for the Intel 8086 core: the assembler, the processor and the
    /// DOS services. The reference encodings were produced by NASM.
    /// </summary>
    public static class I8086Tests
    {
        public static void RunAll()
        {
            Test_Assembler_MatchesNasmEncoding();
            Test_Assembler_DirectivesAndSymbols();
            Test_Cpu_MovAndAdd();
            Test_Cpu_OverflowFlag();
            Test_Cpu_MultiplyAndDivide();
            Test_Cpu_JumpsAndLoop();
            Test_Cpu_StackAndCalls();
            Test_Cpu_StringOperations();
            Test_Cpu_DosOutput();
            Test_LabProgram_Formula();
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
                throw new Exception(message);
        }

        private static string Describe(I8086AssembleResult result)
        {
            StringBuilder builder = new StringBuilder();
            foreach (AssemblerError error in result.Errors)
                builder.Append("строка ").Append(error.LineNumber).Append(": ").Append(error.Message).Append("; ");
            return builder.ToString();
        }

        private static string ToHex(byte[] data)
        {
            StringBuilder builder = new StringBuilder();
            foreach (byte b in data)
                builder.Append(b.ToString("X2"));
            return builder.ToString();
        }

        private sealed class RunResult
        {
            public I8086Cpu Cpu;
            public I8086Console Console;
            public I8086AssembleResult Assembly;
            public I8086Memory Memory;
        }

        private static RunResult Run(string source, int maxSteps = 200000)
        {
            I8086AssembleResult assembly = I8086Assembler.Assemble(source);
            if (!assembly.Success)
            {
                StringBuilder message = new StringBuilder("ошибки ассемблера: ");
                foreach (AssemblerError error in assembly.Errors)
                    message.Append(error.LineNumber).Append(": ").Append(error.Message).Append("; ");
                throw new Exception(message.ToString());
            }

            I8086Memory memory = new I8086Memory();
            I8086Console console = new I8086Console();
            I8086Cpu cpu = new I8086Cpu(memory, console);
            cpu.LoadProgram(assembly.Image, 0x0000, assembly.Origin);

            int steps = 0;
            while (cpu.Step() && steps < maxSteps)
                steps++;

            return new RunResult { Cpu = cpu, Console = console, Assembly = assembly, Memory = memory };
        }

        private const string EncodingProbe = @"
        org 100h
start:
        mov     ax, [A_IN]
        mov     al, [A_IN]
        mov     [A_IN], ax
        mov     [A_IN], al
        mov     ax, [bx]
        mov     ax, [bx+si]
        mov     ax, [bp]
        mov     ax, [bp+4]
        mov     ax, [si]
        add     ax, 5
        add     ax, 1000
        cmp     ax, 5
        cmp     al, 5
        add     [bx], 5
        imul    word [B_IN]
        idiv    word [D_IN]
        jne     bad
        jmp     short good
        jmp     near good
        call    sub1
        mov     ax, 5
        mov     al, 5
        push    5
        push    ax
        lea     si, [bx+si]
        movzx   ax, bl
        movsx   ax, bl
        neg     ax
        mul     bx
        div     bx
        xor     ax, ax
        test    ax, ax
        shl     ax, 1
        sar     ax, 1
        inc     ax
        dec     bx
A_IN:   dw      5
B_IN:   dw      7
D_IN:   dw      3
bad:    nop
good:   nop
sub1:   ret
";

        private const string EncodingProbeExpected =
            "A15801A05801A35801A258018B078B008B46008B46048B0483C00505E80383F8053C05800705"
            + "F72E5A01F73E5C01752EEB2DE92A00E82800B80500B0056A05508D300FB6C30FBEC3F7D8F7E3"
            + "F7F331C085C0D1E0D1F8404B0500070003009090C3";

        private static void Test_Assembler_MatchesNasmEncoding()
        {
            I8086AssembleResult result = I8086Assembler.Assemble(EncodingProbe);
            Assert(result.Success, "сборка эталонного примера не удалась: " + Describe(result));
            Assert(ToHex(result.Image) == EncodingProbeExpected,
                "кодировка не совпадает с NASM:\n  ожидалось " + EncodingProbeExpected
                + "\n  получено  " + ToHex(result.Image));
        }

        private static void Test_Assembler_DirectivesAndSymbols()
        {
            string source = @"
        org 100h
COUNT   equ 5
start:
        mov     cx, COUNT
        mov     word [data], 1234h
        mov     byte [data+2], 0Ah
        times   3 db 90h
        jmp     short done
data:   db      1, 2, 3
        dw      0
done:
        hlt
";
            I8086AssembleResult result = I8086Assembler.Assemble(source);
            Assert(result.Success, "директивы не собрались: " + Describe(result));
            Assert(result.Image[0] == 0xB9 && result.Image[1] == 0x05,
                "MOV CX, COUNT: " + ToHex(result.Image));
            Assert(result.Image[3] == 0xC7 && result.Image[4] == 0x06,
                "MOV word [data], imm16: " + ToHex(result.Image));
            Assert(result.Image[7] == 0x34 && result.Image[8] == 0x12, "значение 1234h");
            Assert(result.Image[9] == 0xC6 && result.Image[10] == 0x06
                   && result.Image[13] == 0x0A, "MOV byte [data+2], 0Ah");
            Assert(result.Image[14] == 0x90 && result.Image[16] == 0x90, "times 3 db 90h");
            Assert(result.Image[17] == 0xEB, "короткий переход");
        }

        private static void Test_Cpu_MovAndAdd()
        {
            RunResult result = Run(@"
        org 100h
start:
        mov     ax, 1234h
        mov     bx, ax
        add     ax, bx
        mov     cx, ax
        hlt
");
            Assert(result.Cpu.State.AX == 0x2468, "AX после сложения");
            Assert(result.Cpu.State.BX == 0x1234, "BX после копирования");
            Assert(result.Cpu.State.CX == 0x2468, "CX после копирования");
        }

        private static void Test_Cpu_OverflowFlag()
        {
            RunResult result = Run(@"
        org 100h
start:
        mov     ax, 7FFFh
        add     ax, 1
        mov     bx, ax
        hlt
");
            Assert(!result.Cpu.State.Zero, "флаг нуля после переполнения");
            Assert(result.Cpu.State.Sign, "флаг знака после переполнения");
            Assert(result.Cpu.State.Overflow, "флаг переполнения после 7FFFh + 1");
            Assert(!result.Cpu.State.Carry, "флаг переноса после 7FFFh + 1");
            Assert(result.Cpu.State.BX == 0x8000, "результат сложения");
        }

        private static void Test_Cpu_MultiplyAndDivide()
        {
            RunResult result = Run(@"
        org 100h
start:
        mov     ax, 300
        mov     bx, 7
        mul     bx
        mov     cx, ax
        mov     ax, cx
        xor     dx, dx
        mov     bx, 5
        div     bx
        hlt
");
            Assert(result.Cpu.State.BX == 0x05, "делитель после DIV");
            Assert(result.Cpu.State.AX == 420, "частное 2100 / 5");
            Assert(result.Cpu.State.DX == 0, "остаток от деления");
        }

        private static void Test_Cpu_JumpsAndLoop()
        {
            RunResult result = Run(@"
        org 100h
        mov     cx, 5
        xor     ax, ax
loop1:
        add     ax, 2
        loop    loop1
        cmp     ax, 10
        jne     fail
        mov     bx, 1
        jmp     done
fail:
        mov     bx, 0
done:
        hlt
");
            Assert(result.Cpu.State.AX == 10, "сумма в цикле");
            Assert(result.Cpu.State.BX == 1, "условный переход");
            Assert(result.Cpu.State.CX == 0, "счётчик цикла обнулён");
        }

        private static void Test_Cpu_StackAndCalls()
        {
            RunResult result = Run(@"
        org 100h
        call    sub1
        mov     bx, ax
        hlt
sub1:
        mov     ax, 77
        ret
");
            Assert(result.Cpu.State.AX == 77, "значение из подпрограммы");
            Assert(result.Cpu.State.BX == 77, "значение после возврата");
        }

        private static void Test_Cpu_StringOperations()
        {
            RunResult result = Run(@"
        org 100h
        mov     si, src
        mov     di, dst
        mov     cx, 4
        cld
        rep     movsb
        jmp     done
src:    db      1, 2, 3, 4
dst:    times   4 db 0
done:
        hlt
");
            // the copy is done at run time, so the memory is checked
            int destination = result.Assembly.Symbols["dst"];
            Assert(result.Memory.ReadByte(destination) == 1, "первый байт копирован");
            Assert(result.Memory.ReadByte(destination + 3) == 4,
                "четвёртый байт копирован: SI=" + result.Cpu.State.SI + " DI=" + result.Cpu.State.DI
                + " CX=" + result.Cpu.State.CX + " dst=" + destination);
            Assert(result.Cpu.State.SI == result.Assembly.Symbols["src"] + 4, "SI после копирования");
            Assert(result.Cpu.State.CX == 0, "счётчик копирования обнулён");
        }

        private static void Test_Cpu_DosOutput()
        {
            RunResult result = Run(@"
        org 100h
        mov     dx, text
        mov     ah, 09h
        int     21h
        mov     ax, 4C00h
        int     21h
text:   db      'OK', '$'
");
            Assert(result.Console.Output.Trim() == "OK", "вывод через INT 21h");
            Assert(result.Cpu.ExitCode == 0, "код завершения");
        }

        private static void Test_LabProgram_Formula()
        {
            string source = @"
        org 100h
start:
        mov     ax, [A_IN]
        imul    word [B_IN]
        add     ax, 3
        adc     dx, 0
        idiv    word [D_IN]
        mov     [Q_RES], ax
        mov     [R_RES], dx
        mov     cx, [A_IN]
        sub     ax, cx
        mov     [F_RES], ax
        mov     dx, msg
        mov     ah, 09h
        int     21h
        mov     ax, [F_RES]
        mov     bx, 7
        cmp     ax, bx
        jne     bad
        mov     dx, ok
        mov     ah, 09h
        int     21h
        jmp     short fin
bad:
        mov     dx, fail
        mov     ah, 09h
        int     21h
fin:
        mov     ax, 4C00h
        int     21h
A_IN:   dw      5
B_IN:   dw      7
D_IN:   dw      3
Q_RES:  dw      0
R_RES:  dw      0
F_RES:  dw      0
msg:    db      'F = ', '$'
ok:     db      'OK', '$'
fail:   db      'FAIL', '$'
";
            RunResult result = Run(source);
            Assert(result.Console.Output.Contains("F = "), "вывод формулы: " + result.Console.Output);
            Assert(result.Console.Output.Contains("OK"), "признак проверки: " + result.Console.Output);
            Assert(!result.Console.Output.Contains("FAIL"), "проверка не пройдена");
            int fResult = result.Memory.ReadWord(result.Assembly.Symbols["F_RES"]);
            Assert(fResult == 7, "значение F в памяти: " + fResult);
        }
    }
}
