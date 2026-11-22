using System;
using System.Collections.Generic;
using System.Text;
using MikroLab.Core.Assembler;
using MikroLab.Core.Assembler.X64;
using MikroLab.Core.Cpu.I8086;
using MikroLab.Core.Cpu.X64;
using MikroLab.Core.Memory;

namespace MikroLab.Tests
{
    /// <summary>
    /// Tests for the x86-64 core: the assembler (the reference encodings were
    /// produced by NASM) and the processor itself.
    /// </summary>
    public static class X64Tests
    {
        public static void RunAll()
        {
            Test_Assembler_MatchesNasmEncoding();
            Test_Assembler_DataAndDirectives();
            Test_Cpu_ArithmeticAndFlags();
            Test_Cpu_OverflowFlag();
            Test_Cpu_MultiplyDivide64();
            Test_Cpu_RipRelative();
            Test_Cpu_CallsAndStack();
            Test_Cpu_StringOperations();
            Test_Cpu_DosOutput();
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
                throw new Exception(message);
        }

        private static string Describe(X64AssembleResult result)
        {
            StringBuilder builder = new StringBuilder();
            foreach (AssemblerError error in result.Errors)
                builder.Append("строка ").Append(error.LineNumber).Append(": ")
                    .Append(error.Message).Append("; ");
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
            public X64Cpu Cpu;
            public I8086Console Console;
            public X64AssembleResult Assembly;
            public X64Memory Memory;
        }

        private static RunResult Run(string source, int maxSteps = 100000)
        {
            X64AssembleResult assembly = X64Assembler.Assemble(source, 0x00400000, null);
            if (!assembly.Success)
            {
                StringBuilder message = new StringBuilder("ошибки ассемблера: ");
                foreach (AssemblerError error in assembly.Errors)
                    message.Append(error.LineNumber).Append(": ").Append(error.Message).Append("; ");
                throw new Exception(message.ToString());
            }

            X64Memory memory = new X64Memory();
            I8086Console console = new I8086Console();
            X64Cpu cpu = new X64Cpu(memory, console);
            cpu.LoadProgram(assembly.Image, assembly.Origin);

            int steps = 0;
            while (cpu.Step() && steps < maxSteps)
                steps++;

            return new RunResult { Cpu = cpu, Console = console, Assembly = assembly, Memory = memory };
        }

        private const string EncodingProbe = @"
        bits 64
        default rel
_start:
        mov     rax, 5
        mov     rbx, rax
        add     rax, rbx
        sub     rax, 3
        imul    rax, rbx
        mov     r8, 100
        mov     rcx, r8
        lea     rdx, [rel data]
        mov     rsi, rdx
        xor     r9, r9
        cmp     rax, 5
        jne     fail
        jmp     done
fail:
        mov     rax, 0
done:
        mov     eax, 4C00h
        int     21h
data:   dq      42
        db      'hello', 0
";

        private const string EncodingProbeExpected =
            "B8050000004889C34801D84883E803480FAFC341B8640000004C89C1488D151A0000004889D6" +
            "4D31C94883F8057502EB05B800000000B8004C0000CD212A0000000000000068656C6C6F00";

        private static void Test_Assembler_MatchesNasmEncoding()
        {
            X64AssembleResult result = X64Assembler.Assemble(EncodingProbe, 0, null);
            Assert(result.Success, "эталонный пример не собрался");
            Assert(ToHex(result.Image) == EncodingProbeExpected,
                "кодировка не совпадает с NASM:\n  ожидалось " + EncodingProbeExpected +
                "\n  получено  " + ToHex(result.Image));
        }

        private static void Test_Assembler_DataAndDirectives()
        {
            X64AssembleResult result = X64Assembler.Assemble(@"
        bits 64
        default rel
COUNT   equ 5
start:
        mov     rcx, COUNT
        mov     qword [data], 1234h
        mov     byte [data+8], 0Ah
        times   3 db 90h
        jmp     short done
data:   db      1, 2, 3
        times   5 dw 0
done:
        hlt
", 0, null);

            Assert(result.Success, "директивы не собрались: " + Describe(result));
            // эталон NASM: nasm -f bin (начало 0000h)
            const string expected = "B90500000048C7050C00000034120000C6050D0000000A909090EB0D" +
                "01020300000000000000000000F4";
            Assert(ToHex(result.Image) == expected,
                "кодировка директив не совпала:\n  ожидалось " + expected +
                "\n  получено  " + ToHex(result.Image));
        }

        private static void Test_Cpu_ArithmeticAndFlags()
        {
            RunResult result = Run(@"
        bits 64
start:
        mov     rax, 1000
        mov     rbx, 24
        add     rax, rbx
        mov     rcx, rax
        sub     rax, 10
        mov     rdx, rax
        xor     rax, rax
        test    rax, rax
        hlt
");
            Assert(result.Cpu.State.RAX == 0, "xor не обнулил регистр");
            Assert(result.Cpu.State.RCX == 1024, "сложение 64-битное");
            Assert(result.Cpu.State.RDX == 1014, "вычитание 64-битное");
            Assert(result.Cpu.State.Zero, "флаг нуля после test");
        }

        private static void Test_Cpu_OverflowFlag()
        {
            RunResult result = Run(@"
        bits 64
start:
        mov     rax, 0x7FFFFFFFFFFFFFFF
        add     rax, 1
        hlt
");
            Assert(result.Cpu.State.Overflow, "флаг переполнения при сложении");
            Assert(result.Cpu.State.Sign, "флаг знака после переполнения");
            Assert(result.Cpu.State.RAX == unchecked((ulong)0x8000000000000000), "результат сложения");
        }

        private static void Test_Cpu_MultiplyDivide64()
        {
            RunResult result = Run(@"
        bits 64
start:
        mov     rax, 1000000
        mov     rbx, 1000000
        mul     rbx
        mov     rcx, rax
        mov     rax, rcx
        xor     rdx, rdx
        mov     rbx, 1000
        div     rbx
        hlt
");
            Assert(result.Cpu.State.RCX == 1000000000000UL, "произведение 64-битное");
            Assert(result.Cpu.State.RAX == 1000000000UL, "частное от деления");
            Assert(result.Cpu.State.RDX == 0, "остаток от деления");
        }

        private static void Test_Cpu_RipRelative()
        {
            RunResult result = Run(@"
        bits 64
        default rel
start:
        lea     rax, [rel value]
        mov     rbx, [rel value]
        hlt
value:  dq      12345
");
            // символы в ассемблере — абсолютные адреса (как в NASM при org)
            long value = result.Assembly.Symbols["value"];
            Assert(result.Cpu.State.RAX == (ulong)value,
                "lea с относительным адресом: RAX=" + result.Cpu.State.RAX.ToString("X") +
                " ожидалось " + value.ToString("X"));
            Assert(result.Cpu.State.RBX == 12345, "чтение по относительному адресу");
        }

        private static void Test_Cpu_CallsAndStack()
        {
            RunResult result = Run(@"
        bits 64
start:
        call    sub1
        mov     rbx, rax
        hlt
sub1:
        mov     rax, 77
        ret
");
            Assert(result.Cpu.State.RAX == 77, "значение из подпрограммы");
            Assert(result.Cpu.State.RBX == 77, "значение после возврата");
        }

        private static void Test_Cpu_StringOperations()
        {
            RunResult result = Run(@"
        bits 64
        default rel
start:
        lea     rsi, [rel src]
        lea     rdi, [rel dst]
        mov     rcx, 4
        cld
        rep     movsb
        hlt
src:    db      1, 2, 3, 4
dst:    times   4 db 0
");
            long destination = result.Assembly.Symbols["dst"];
            Assert(result.Memory.ReadByte((ulong)destination) == 1, "первый байт копирован");
            Assert(result.Memory.ReadByte((ulong)destination + 3) == 4, "четвёртый байт копирован");
            Assert(result.Cpu.State.RCX == 0, "счётчик копирования обнулён");
        }

        private static void Test_Cpu_DosOutput()
        {
            RunResult result = Run(@"
        bits 64
        default rel
start:
        mov     rdx, text
        mov     ah, 09h
        int     21h
        mov     ax, 4C00h
        int     21h
text:   db      'OK', '$'
");
            Assert(result.Console.Output.Trim() == "OK", "вывод через INT 21h: " + result.Console.Output);
            Assert(result.Cpu.ExitCode == 0, "код завершения");
        }
    }
}
