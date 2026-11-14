using System;
using System.Collections.Generic;
using MikroLab.Core.Assembler.I8086;
using MikroLab.Core.Assembler.X64;
using MikroLab.Presentation;

namespace MikroLab.Tests
{
    /// <summary>
    /// Tests for the breakpoints of the 16- and 64-bit architectures: the line
    /// marked in the editor is mapped to the address produced by the assembler.
    /// </summary>
    public static class ArchitectureBreakpointTests
    {
        public static void RunAll()
        {
            Test_Set_TogglesLines();
            Test_Lines_AreMappedToAddresses();
            Test_LineWithoutCode_HasNoAddress();
            Test_X64_Lines_AreMappedToAddresses();
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
                throw new Exception(message);
        }

        private static void Test_Set_TogglesLines()
        {
            var breakpoints = new BreakpointSet();

            Assert(breakpoints.ToggleLine(5), "строка должна отмечаться");
            Assert(breakpoints.HasLine(5), "строка 5 должна быть отмечена");
            Assert(!breakpoints.ToggleLine(5), "повторное нажатие должно снимать точку");
            Assert(!breakpoints.HasLine(5), "строка 5 должна быть снята");
            Assert(!breakpoints.ToggleLine(0), "нулевая строка не отмечается");
        }

        private static void Test_Lines_AreMappedToAddresses()
        {
            const string Source = "mov ax, 1234h\n" +
                                  "add ax, bx\n" +
                                  "ret\n";

            I8086AssembleResult result = I8086Assembler.Assemble(Source, 0x0100, null);
            Assert(result.Success, "ассемблер 8086 ошибся");

            var breakpoints = new BreakpointSet();
            breakpoints.ToggleLine(2);

            breakpoints.MapAddresses(line =>
            {
                foreach (I8086ListingLine item in result.Listing)
                {
                    if (item.HasAddress && item.LineNumber == line)
                        return (ulong)item.Offset;
                }

                return null;
            });

            Assert(breakpoints.HasCode(2), "строка с командой должна иметь адрес");
            Assert(breakpoints.Contains((ushort)0x0103), "вторая команда начинается по адресу 0103h");
            Assert(!breakpoints.Contains((ushort)0x0100), "адрес первой команды не должен быть отмечен");
            Assert(breakpoints.Addresses.Count == 1, "адресов должно быть одно");
        }

        private static void Test_LineWithoutCode_HasNoAddress()
        {
            const string Source = "start:\n" +
                                  "mov ax, 1234h\n";

            I8086AssembleResult result = I8086Assembler.Assemble(Source, 0x0100, null);
            Assert(result.Success, "ассемблер 8086 ошибся");

            var breakpoints = new BreakpointSet();
            breakpoints.ToggleLine(1);

            breakpoints.MapAddresses(line =>
            {
                foreach (I8086ListingLine item in result.Listing)
                {
                    if (item.HasAddress && item.LineNumber == line && item.Bytes.Length > 0)
                        return (ulong)item.Offset;
                }

                return null;
            });

            Assert(!breakpoints.HasCode(1), "у строки с меткой нет машинного кода");
            Assert(breakpoints.Addresses.Count == 0, "адресов быть не должно");
        }

        private static void Test_X64_Lines_AreMappedToAddresses()
        {
            const string Source = "mov rax, 1122334455667788h\n" +
                                  "add rax, rbx\n";

            X64AssembleResult result = X64Assembler.Assemble(Source, 0x00400000, null);
            Assert(result.Success, "ассемблер x64 ошибся");

            var breakpoints = new BreakpointSet();
            breakpoints.ToggleLine(2);

            breakpoints.MapAddresses(line =>
            {
                foreach (X64ListingLine item in result.Listing)
                {
                    if (item.HasAddress && item.LineNumber == line)
                        return item.Offset;
                }

                return null;
            });

            Assert(breakpoints.HasCode(2), "строка с командой должна иметь адрес");
            Assert(breakpoints.Contains(0x0040000AUL), "вторая команда начинается по адресу 0040000Ah");
        }
    }
}
