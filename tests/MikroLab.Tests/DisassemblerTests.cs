using System;
using System.Collections.Generic;
using System.Text;
using MikroLab.Core.Assembler.I8086;
using MikroLab.Core.Assembler.X64;
using MikroLab.Core.Debugging;

namespace MikroLab.Tests
{
    /// <summary>
    /// Tests for the disassembler of the 8086 and the x86-64: it turns machine
    /// code back into instructions. The reference texts were checked against
    /// NASM — the encodings and the decoded instructions match byte for byte.
    /// </summary>
    public static class DisassemblerTests
    {
        public static void RunAll()
        {
            Test_I8086_SimpleInstructions();
            Test_I8086_MemoryAddressing();
            Test_I8086_Jumps();
            Test_X64_RegistersAndImmediates();
            Test_X64_RipRelative();
            Test_Assembler_AndDisassembler_Agree();
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
                throw new Exception(message);
        }

        private static void AssertText(string expected, string actual, string message)
        {
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
                throw new Exception(message + ": ожидалось \"" + expected + "\", получено \"" + actual + "\"");
        }

        private static DecodedInstruction Decode16(params byte[] code)
        {
            return I8086Disassembler.Decode(code, 0, 0x0100);
        }

        private static DecodedInstruction Decode64(params byte[] code)
        {
            return X64Disassembler.Decode(code, 0, 0x00400000);
        }

        // ------------------------------------------------------------------
        // Intel 8086
        // ------------------------------------------------------------------

        private static void Test_I8086_SimpleInstructions()
        {
            AssertText("mov AX, 1234h", Decode16(0xB8, 0x34, 0x12).Text, "mov r16, imm16");
            AssertText("mov AL, 34h", Decode16(0xB0, 0x34).Text, "mov r8, imm8");
            AssertText("add AX, BX", Decode16(0x01, 0xD8).Text, "add r/m16, r16");
            AssertText("add AX, 0010h", Decode16(0x05, 0x10, 0x00).Text, "add r16, imm16");
            AssertText("sub AX, -1h", Decode16(0x83, 0xE8, 0xFF).Text, "sub r16, imm8 со знаком");
            AssertText("cmp AX, 0000h", Decode16(0x3D, 0x00, 0x00).Text, "cmp r16, imm16");
            AssertText("inc AX", Decode16(0x40).Text, "inc r16");
            AssertText("dec BX", Decode16(0x4B).Text, "dec r16");
            AssertText("push AX", Decode16(0x50).Text, "push r16");
            AssertText("pop DX", Decode16(0x5A).Text, "pop r16");
            AssertText("nop", Decode16(0x90).Text, "nop");
            AssertText("hlt", Decode16(0xF4).Text, "hlt");
            AssertText("ret", Decode16(0xC3).Text, "ret");
            AssertText("int 21h", Decode16(0xCD, 0x21).Text, "int imm8");
            AssertText("cbw", Decode16(0x98).Text, "cbw");
            AssertText("cwd", Decode16(0x99).Text, "cwd");
            AssertText("rep movsb", Decode16(0xF3, 0xA4).Text, "rep movsb");
            AssertText("repne scasb", Decode16(0xF2, 0xAE).Text, "repne scasb");
            AssertText("shl AX, 1", Decode16(0xD1, 0xE0).Text, "сдвиг на 1");
            AssertText("shr AX, CL", Decode16(0xD3, 0xE8).Text, "сдвиг на CL");
            AssertText("mul BX", Decode16(0xF7, 0xE3).Text, "mul r/m16");
            AssertText("imul AX, BX, 03h", Decode16(0x6B, 0xC3, 0x03).Text, "imul с непосредственным");
            AssertText("neg AX", Decode16(0xF7, 0xD8).Text, "neg r/m16");
            AssertText("not AX", Decode16(0xF7, 0xD0).Text, "not r/m16");
        }

        private static void Test_I8086_MemoryAddressing()
        {
            AssertText("mov word [bx], AX", Decode16(0x89, 0x07).Text, "mov в память");
            AssertText("mov word [bp+di+4h], 1234h", Decode16(0xC7, 0x43, 0x04, 0x34, 0x12).Text,
                "mov в память с непосредственным");
            AssertText("mov word [bp+di+4h], 5678h", Decode16(0xC7, 0x43, 0x04, 0x78, 0x56).Text,
                "mov в память со смещением");
            AssertText("mov AL, byte [200h]", Decode16(0xA0, 0x00, 0x02).Text, "mov AL из памяти");
            AssertText("mov byte [300h], AL", Decode16(0xA2, 0x00, 0x03).Text, "mov в память из AL");
            AssertText("lea SI, [bx+4h]", Decode16(0x8D, 0x77, 0x04).Text, "lea");
            AssertText("lea SI, [bp+di+4h]", Decode16(0x8D, 0x73, 0x04).Text, "lea с bp+di");
            AssertText("lea SI, [bx+si+4h]", Decode16(0x8D, 0x70, 0x04).Text, "lea с bx+si");
            AssertText("xchg AX, BX", Decode16(0x93).Text, "xchg");
            AssertText("movzx AX, BL", Decode16(0x0F, 0xB6, 0xC3).Text, "movzx");
            AssertText("movsx AX, BL", Decode16(0x0F, 0xBE, 0xC3).Text, "movsx");
        }

        private static void Test_I8086_Jumps()
        {
            // EB 10 — jmp +16 относительно следующей команды
            AssertText("jmp 0112h", Decode16(0xEB, 0x10).Text, "jmp rel8");
            // 74 10 — je +16
            AssertText("je 0112h", Decode16(0x74, 0x10).Text, "je rel8");
            // E8 00 00 — call +0
            AssertText("call 0103h", Decode16(0xE8, 0x00, 0x00).Text, "call rel16");
            // E2 FE — loop -2
            AssertText("loop 0100h", Decode16(0xE2, 0xFE).Text, "loop rel8");
        }

        // ------------------------------------------------------------------
        // x86-64
        // ------------------------------------------------------------------

        private static void Test_X64_RegistersAndImmediates()
        {
            AssertText("mov RAX, 1122334455667788h",
                Decode64(0x48, 0xB8, 0x88, 0x77, 0x66, 0x55, 0x44, 0x33, 0x22, 0x11).Text,
                "mov r64, imm64");
            AssertText("mov RBX, RAX", Decode64(0x48, 0x89, 0xC3).Text, "mov r64, r64");
            AssertText("add RAX, RBX", Decode64(0x48, 0x01, 0xD8).Text, "add r64, r64");
            AssertText("push RAX", Decode64(0x50).Text, "push r64");
            AssertText("pop RBX", Decode64(0x5B).Text, "pop r64");
            AssertText("movsxd RAX, EBX", Decode64(0x48, 0x63, 0xC3).Text, "movsxd");
            AssertText("movzx EAX, BL", Decode64(0x0F, 0xB6, 0xC3).Text, "movzx");
            AssertText("movsx RAX, BL", Decode64(0x48, 0x0F, 0xBE, 0xC3).Text, "movsx");
            AssertText("sete AL", Decode64(0x0F, 0x94, 0xC0).Text, "setcc");
            AssertText("cmove RAX, RBX", Decode64(0x48, 0x0F, 0x44, 0xC3).Text, "cmovcc");
            AssertText("cpuid", Decode64(0x0F, 0xA2).Text, "cpuid");
            AssertText("rdtsc", Decode64(0x0F, 0x31).Text, "rdtsc");
            AssertText("mov qword [RBX], RAX", Decode64(0x48, 0x89, 0x03).Text, "mov в память");
            AssertText("mov byte [RBX], 1h", Decode64(0xC6, 0x03, 0x01).Text, "mov в память");
            AssertText("mov RAX, qword [rip+5Fh]",
                Decode64(0x48, 0x8B, 0x05, 0x5F, 0x00, 0x00, 0x00).Text, "mov из памяти по RIP");
        }

        private static void Test_X64_RipRelative()
        {
            // 48 8B 05 5F 00 00 00 — mov rax, [rip+0x5F] при адресе 00400000h
            DecodedInstruction instruction = Decode64(0x48, 0x8B, 0x05, 0x5F, 0x00, 0x00, 0x00);
            AssertText("mov RAX, qword [rip+5Fh]", instruction.Text, "адрес по RIP");
            Assert(instruction.Length == 7, "длина команды с RIP неверна: " + instruction.Length);
        }

        // ------------------------------------------------------------------
        // согласие ассемблера и дизассемблера
        // ------------------------------------------------------------------

        private static void Test_Assembler_AndDisassembler_Agree()
        {
            const string Source16 = "mov ax, 1234h\n" +
                                    "add ax, bx\n" +
                                    "mov word [bx+si], 5678h\n" +
                                    "jmp done\n" +
                                    "done:\n" +
                                    "ret\n";

            I8086AssembleResult result16 = I8086Assembler.Assemble(Source16, 0x0100, null);
            Assert(result16.Success, "ассемблер 8086 ошибся: " + result16.Errors.Count);

            int offset = 0;
            foreach (I8086ListingLine line in result16.Listing)
            {
                if (!line.HasAddress || line.Bytes.Length == 0)
                    continue;

                DecodedInstruction instruction = I8086Disassembler.Decode(result16.Image, offset, 0x0100);
                Assert(instruction.Length == line.Bytes.Length,
                    "длина команды 8086 не совпала: " + instruction.Length + " и " + line.Bytes.Length);
                offset += line.Bytes.Length;
            }

            const string Source64 = "mov rax, 1122334455667788h\n" +
                                    "mov qword [rbx], rax\n" +
                                    "jmp done\n" +
                                    "done:\n" +
                                    "ret\n";

            X64AssembleResult result64 = X64Assembler.Assemble(Source64, 0x00400000, null);
            Assert(result64.Success, "ассемблер x64 ошибся: " + result64.Errors.Count);

            offset = 0;
            foreach (X64ListingLine line in result64.Listing)
            {
                if (!line.HasAddress || line.Bytes.Length == 0)
                    continue;

                DecodedInstruction instruction = X64Disassembler.Decode(result64.Image, offset, 0x00400000);
                Assert(instruction.Length == line.Bytes.Length,
                    "длина команды x64 не совпала: " + instruction.Length + " и " + line.Bytes.Length);
                offset += line.Bytes.Length;
            }
        }
    }
}
