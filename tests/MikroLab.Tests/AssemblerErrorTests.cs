using System;
using System.Collections.Generic;
using MikroLab.Core.Assembler.I8086;
using MikroLab.Core.Assembler.X64;

namespace MikroLab.Tests
{
    /// <summary>
    /// Tests for the error reporting of the assemblers: an undefined symbol
    /// must be reported for every use of it, including the short forms
    /// "mov ax, [mem]" and "mov [mem], ax", and nothing may be encoded
    /// silently as zero.
    /// </summary>
    public static class AssemblerErrorTests
    {
        public static void RunAll()
        {
            Test_I8086_UndefinedSymbol_IsReportedInEveryForm();
            Test_I8086_UndefinedSymbol_IsNotSwallowed();
            Test_X64_UndefinedSymbol_IsReported();
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
                throw new Exception(message);
        }

        private static void Test_I8086_UndefinedSymbol_IsReportedInEveryForm()
        {
            // все формы обращения к отсутствующей метке
            const string Source = "mov     ax, [A_IN]\n" +        // короткая форма moffs
                                  "mov     cx, [A_IN]\n" +        // обычная форма ModR/M
                                  "mov     [A_IN], ax\n" +        // короткая форма moffs
                                  "mov     al, [A_IN]\n" +        // короткая форма moffs
                                  "mov     dx, A_IN\n";           // непосредственный операнд

            I8086AssembleResult result = I8086Assembler.Assemble(Source, 0x0100, null);

            Assert(!result.Success, "ассемблер должен сообщить об ошибке");
            Assert(result.Errors.Count == 5,
                "ошибок должно быть 5, а не " + result.Errors.Count);
        }

        private static void Test_I8086_UndefinedSymbol_IsNotSwallowed()
        {
            // короткая форма: mov ax, [A_IN] — адрес не должен превратиться в ноль
            const string Source = "mov     ax, [A_IN]\n" +
                                  "mov     ax, 4C00h\n" +
                                  "int     21h\n";

            I8086AssembleResult result = I8086Assembler.Assemble(Source, 0x0100, null);

            Assert(!result.Success, "ассемблер должен сообщить об ошибке");
            Assert(result.Errors.Count == 1,
                "ошибок должно быть 1, а не " + result.Errors.Count);
            Assert(result.Errors[0].Message.Contains("A_IN"),
                "в сообщении должно быть имя символа: " + result.Errors[0].Message);
        }

        private static void Test_X64_UndefinedSymbol_IsReported()
        {
            const string Source = "mov     rax, [A_IN]\n" +
                                  "mov     rcx, [A_IN]\n" +
                                  "mov     rax, A_IN\n";

            X64AssembleResult result = X64Assembler.Assemble(Source, 0x00400000, null);

            Assert(!result.Success, "ассемблер x64 должен сообщить об ошибке");
            Assert(result.Errors.Count == 3,
                "ошибок должно быть 3, а не " + result.Errors.Count);
        }
    }
}
