using System;
using System.Collections.Generic;
using MikroLab.Core.Assembler;

namespace MikroLab.Tests
{
    public static class AssemblerTests
    {
        public static void RunAll()
        {
            Console.WriteLine("[TEST SUITE] AssemblerTests (Intel 8080 two-pass assembler)");
            Test_Implied_Control_Instructions();
            Test_Mov_AllRegisterPairs();
            Test_Mvi_AllRegisters();
            Test_Register_Pair_Utility_Instructions();
            Test_Lxi_AllPairs_And_ByteOrder();
            Test_Direct_Memory_Instructions();
            Test_Ldax_Stax_Pairs();
            Test_Alu_Register_Forms();
            Test_Alu_Immediate_Forms();
            Test_Inr_Dcr_AllRegisters();
            Test_Inx_Dcx_Dad_Pairs();
            Test_Rotate_And_Flag_Instructions();
            Test_Jmp_And_All_Jcc();
            Test_Call_And_All_Ccc();
            Test_Ret_And_All_Rcc();
            Test_Rst_Vectors();
            Test_Push_Pop_Pairs();
            Test_In_Out_Ports();
            Test_Mov_M_M_Encodes_Hlt();
            Test_Forward_And_Backward_Labels();
            Test_Equ_And_Forward_Equ();
            Test_Equ_Location_Counter_And_Symbol_Case();
            Test_Org_And_Chunks();
            Test_Db_Strings_Characters_And_Commas();
            Test_Dw_LittleEndian_And_Location_Counter();
            Test_Ds_Reserves_Without_Chunk();
            Test_Expressions_Precedence_And_Operators();
            Test_Number_Literal_Formats();
            Test_Comments_And_Quoted_Semicolons();
            Test_Case_Insensitivity_And_Directive_Dots();
            Test_Listing_Reflects_Source_Lines();
            Test_End_Stops_Processing();
            Test_Symbols_Preserve_Case_And_Are_CaseInsensitive();
            Test_Complete_Program_At_8000();
            Test_Assemble_Default_Origin();
            Test_Empty_And_Only_Equ_Source();
            Test_Error_Unknown_Mnemonic();
            Test_Error_Duplicate_Label();
            Test_Error_Invalid_Register_And_Pair();
            Test_Error_Immediate_Out_Of_Range();
            Test_Error_Undefined_Symbol();
            Test_Error_Invalid_Condition_And_Operands();
            Test_Error_Division_By_Zero_And_Address_Overflow();
            Test_Error_Listing_Still_Returned_On_Failure();
            Console.WriteLine("  All AssemblerTests PASSED.\n");
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException($"Assertion failed: {message}");
        }

        private static AssembleResult AssembleOk(string source, ushort origin)
        {
            AssembleResult result = I8080Assembler.Assemble(source, origin);
            Assert(result.Success, "Expected assembly to succeed. Errors: " + FormatErrors(result));
            return result;
        }

        private static AssembleResult AssembleOk(string source)
        {
            return AssembleOk(source, 0x8000);
        }

        private static void AssertBytesAt(string source, ushort expectedAddress, params byte[] expected)
        {
            AssembleResult result = AssembleOk(source);
            Assert(result.Chunks.Count == 1, "Expected one chunk for '" + source + "', got " + result.Chunks.Count);
            Assert(result.Chunks[0].Address == expectedAddress,
                $"Expected chunk at 0x{expectedAddress:X4}, got 0x{result.Chunks[0].Address:X4} for '{source}'");
            Assert(BytesEqual(result.Chunks[0].Data, expected),
                "Unexpected bytes for '" + source + "': expected " + HexBytes(expected) +
                ", got " + HexBytes(result.Chunks[0].Data));
        }

        private static void AssertBytes(string source, params byte[] expected)
        {
            AssertBytesAt(source, 0x8000, expected);
        }

        private static void AssertFails(string source, int lineNumber, string messagePart)
        {
            AssembleResult result = I8080Assembler.Assemble(source, 0x8000);
            Assert(!result.Success, "Expected assembly failure for '" + source + "'");
            foreach (AssemblerError error in result.Errors)
            {
                if (error.LineNumber == lineNumber &&
                    error.Message.IndexOf(messagePart, StringComparison.OrdinalIgnoreCase) >= 0)
                    return;
            }

            throw new InvalidOperationException(
                "Expected error containing '" + messagePart + "' on line " + lineNumber +
                " for '" + source + "'. Got: " + FormatErrors(result));
        }

        private static bool BytesEqual(byte[] actual, byte[] expected)
        {
            if (actual == null || expected == null || actual.Length != expected.Length)
                return false;
            for (int i = 0; i < actual.Length; i++)
            {
                if (actual[i] != expected[i])
                    return false;
            }
            return true;
        }

        private static string HexBytes(byte[] data)
        {
            if (data == null)
                return "<null>";
            string text = "";
            for (int i = 0; i < data.Length; i++)
            {
                if (i > 0)
                    text += " ";
                text += data[i].ToString("X2");
            }
            return text;
        }

        private static string FormatErrors(AssembleResult result)
        {
            string text = "";
            foreach (AssemblerError error in result.Errors)
            {
                if (text.Length > 0)
                    text += " | ";
                text += "line " + error.LineNumber + ": " + error.Message;
            }
            return text.Length == 0 ? "<none>" : text;
        }

        public static void Test_Implied_Control_Instructions()
        {
            AssertBytes("NOP\nHLT\nEI\nDI", 0x00, 0x76, 0xFB, 0xF3);
            Console.WriteLine("  [PASS] Test_Implied_Control_Instructions");
        }

        public static void Test_Mov_AllRegisterPairs()
        {
            string[] registers = { "B", "C", "D", "E", "H", "L", "M", "A" };
            for (int destination = 0; destination < 8; destination++)
            {
                for (int source = 0; source < 8; source++)
                {
                    string program = "MOV " + registers[destination] + "," + registers[source];
                    byte expected = (byte)(0x40 | (destination << 3) | source);
                    AssertBytes(program, expected);
                }
            }
            Console.WriteLine("  [PASS] Test_Mov_AllRegisterPairs (all 64 combinations)");
        }

        public static void Test_Mvi_AllRegisters()
        {
            string[] registers = { "B", "C", "D", "E", "H", "L", "M", "A" };
            for (int register = 0; register < 8; register++)
            {
                string program = "MVI " + registers[register] + ",0" + (register + 1) + "h";
                AssertBytes(program, (byte)(0x06 | (register << 3)), (byte)(register + 1));
            }
            Console.WriteLine("  [PASS] Test_Mvi_AllRegisters");
        }

        public static void Test_Register_Pair_Utility_Instructions()
        {
            AssertBytes("XCHG\nXTHL\nSPHL\nPCHL", 0xEB, 0xE3, 0xF9, 0xE9);
            Console.WriteLine("  [PASS] Test_Register_Pair_Utility_Instructions");
        }

        public static void Test_Lxi_AllPairs_And_ByteOrder()
        {
            AssertBytes("LXI B,1234h\nLXI D,5678h\nLXI H,9ABCh\nLXI SP,0DEF0h",
                0x01, 0x34, 0x12,
                0x11, 0x78, 0x56,
                0x21, 0xBC, 0x9A,
                0x31, 0xF0, 0xDE);

            AssertBytes("LXI BC,1\nLXI DE,2\nLXI HL,3\nlxi sp,4",
                0x01, 0x01, 0x00,
                0x11, 0x02, 0x00,
                0x21, 0x03, 0x00,
                0x31, 0x04, 0x00);
            Console.WriteLine("  [PASS] Test_Lxi_AllPairs_And_ByteOrder");
        }

        public static void Test_Direct_Memory_Instructions()
        {
            AssertBytes("LDA 1234h\nSTA 5678h\nLHLD 9ABCh\nSHLD 0DEF0h",
                0x3A, 0x34, 0x12,
                0x32, 0x78, 0x56,
                0x2A, 0xBC, 0x9A,
                0x22, 0xF0, 0xDE);
            Console.WriteLine("  [PASS] Test_Direct_Memory_Instructions");
        }

        public static void Test_Ldax_Stax_Pairs()
        {
            AssertBytes("LDAX B\nLDAX BC\nLDAX D\nLDAX DE\nSTAX B\nSTAX BC\nSTAX D\nSTAX DE",
                0x0A, 0x0A, 0x1A, 0x1A,
                0x02, 0x02, 0x12, 0x12);
            Console.WriteLine("  [PASS] Test_Ldax_Stax_Pairs");
        }

        public static void Test_Alu_Register_Forms()
        {
            string[] mnemonics = { "ADD", "ADC", "SUB", "SBB", "ANA", "XRA", "ORA", "CMP" };
            byte[] bases = { 0x80, 0x88, 0x90, 0x98, 0xA0, 0xA8, 0xB0, 0xB8 };
            string[] registers = { "B", "C", "D", "E", "H", "L", "M", "A" };

            for (int mnemonic = 0; mnemonic < mnemonics.Length; mnemonic++)
            {
                for (int register = 0; register < registers.Length; register++)
                {
                    string program = mnemonics[mnemonic] + " " + registers[register];
                    AssertBytes(program, (byte)(bases[mnemonic] | register));
                }
            }
            Console.WriteLine("  [PASS] Test_Alu_Register_Forms (all 64 combinations)");
        }

        public static void Test_Alu_Immediate_Forms()
        {
            AssertBytes("ADI 5Ah\nACI 5Ah\nSUI 5Ah\nSBI 5Ah\nANI 5Ah\nXRI 5Ah\nORI 5Ah\nCPI 5Ah",
                0xC6, 0x5A,
                0xCE, 0x5A,
                0xD6, 0x5A,
                0xDE, 0x5A,
                0xE6, 0x5A,
                0xEE, 0x5A,
                0xF6, 0x5A,
                0xFE, 0x5A);
            Console.WriteLine("  [PASS] Test_Alu_Immediate_Forms");
        }

        public static void Test_Inr_Dcr_AllRegisters()
        {
            string[] registers = { "B", "C", "D", "E", "H", "L", "M", "A" };
            for (int register = 0; register < 8; register++)
            {
                AssertBytes("INR " + registers[register], (byte)(0x04 | (register << 3)));
                AssertBytes("DCR " + registers[register], (byte)(0x05 | (register << 3)));
            }
            Console.WriteLine("  [PASS] Test_Inr_Dcr_AllRegisters");
        }

        public static void Test_Inx_Dcx_Dad_Pairs()
        {
            string[] pairs = { "B", "BC", "D", "DE", "H", "HL", "SP" };
            int[] codes = { 0, 0, 1, 1, 2, 2, 3 };

            for (int i = 0; i < pairs.Length; i++)
            {
                AssertBytes("INX " + pairs[i], (byte)(0x03 | (codes[i] << 4)));
                AssertBytes("DCX " + pairs[i], (byte)(0x0B | (codes[i] << 4)));
                AssertBytes("DAD " + pairs[i], (byte)(0x09 | (codes[i] << 4)));
            }
            Console.WriteLine("  [PASS] Test_Inx_Dcx_Dad_Pairs");
        }

        public static void Test_Rotate_And_Flag_Instructions()
        {
            AssertBytes("RLC\nRRC\nRAL\nRAR\nDAA\nCMA\nSTC\nCMC",
                0x07, 0x0F, 0x17, 0x1F, 0x27, 0x2F, 0x37, 0x3F);
            Console.WriteLine("  [PASS] Test_Rotate_And_Flag_Instructions");
        }

        public static void Test_Jmp_And_All_Jcc()
        {
            AssertBytes("JMP 8123h", 0xC3, 0x23, 0x81);
            AssertBytes("JNZ 8123h\nJZ 8123h\nJNC 8123h\nJC 8123h\nJPO 8123h\nJPE 8123h\nJP 8123h\nJM 8123h",
                0xC2, 0x23, 0x81,
                0xCA, 0x23, 0x81,
                0xD2, 0x23, 0x81,
                0xDA, 0x23, 0x81,
                0xE2, 0x23, 0x81,
                0xEA, 0x23, 0x81,
                0xF2, 0x23, 0x81,
                0xFA, 0x23, 0x81);
            Console.WriteLine("  [PASS] Test_Jmp_And_All_Jcc");
        }

        public static void Test_Call_And_All_Ccc()
        {
            AssertBytes("CALL 8123h", 0xCD, 0x23, 0x81);
            AssertBytes("CNZ 8123h\nCZ 8123h\nCNC 8123h\nCC 8123h\nCPO 8123h\nCPE 8123h\nCP 8123h\nCM 8123h",
                0xC4, 0x23, 0x81,
                0xCC, 0x23, 0x81,
                0xD4, 0x23, 0x81,
                0xDC, 0x23, 0x81,
                0xE4, 0x23, 0x81,
                0xEC, 0x23, 0x81,
                0xF4, 0x23, 0x81,
                0xFC, 0x23, 0x81);
            Console.WriteLine("  [PASS] Test_Call_And_All_Ccc");
        }

        public static void Test_Ret_And_All_Rcc()
        {
            AssertBytes("RET", 0xC9);
            AssertBytes("RNZ\nRZ\nRNC\nRC\nRPO\nRPE\nRP\nRM",
                0xC0, 0xC8, 0xD0, 0xD8, 0xE0, 0xE8, 0xF0, 0xF8);
            Console.WriteLine("  [PASS] Test_Ret_And_All_Rcc");
        }

        public static void Test_Rst_Vectors()
        {
            AssertBytes("RST 0\nRST 1\nRST 2\nRST 3\nRST 4\nRST 5\nRST 6\nRST 7",
                0xC7, 0xCF, 0xD7, 0xDF, 0xE7, 0xEF, 0xF7, 0xFF);
            AssertBytes("RST 0h", 0xC7);
            Console.WriteLine("  [PASS] Test_Rst_Vectors");
        }

        public static void Test_Push_Pop_Pairs()
        {
            AssertBytes("PUSH B\nPUSH BC\nPUSH D\nPUSH DE\nPUSH H\nPUSH HL\nPUSH PSW",
                0xC5, 0xC5, 0xD5, 0xD5, 0xE5, 0xE5, 0xF5);
            AssertBytes("POP B\nPOP BC\nPOP D\nPOP DE\nPOP H\nPOP HL\nPOP PSW",
                0xC1, 0xC1, 0xD1, 0xD1, 0xE1, 0xE1, 0xF1);
            Console.WriteLine("  [PASS] Test_Push_Pop_Pairs");
        }

        public static void Test_In_Out_Ports()
        {
            AssertBytes("IN 12h\nOUT 34h\nIN 0\nOUT 0FFh",
                0xDB, 0x12,
                0xD3, 0x34,
                0xDB, 0x00,
                0xD3, 0xFF);
            Console.WriteLine("  [PASS] Test_In_Out_Ports");
        }

        public static void Test_Mov_M_M_Encodes_Hlt()
        {
            AssertBytes("MOV M,M", 0x76);
            AssertBytes("HLT", 0x76);
            Console.WriteLine("  [PASS] Test_Mov_M_M_Encodes_Hlt");
        }

        public static void Test_Forward_And_Backward_Labels()
        {
            AssertBytes("ORG 8000h\nstart: MVI A,0\nloop: INR A\nJNZ loop\nJMP start",
                0x3E, 0x00,
                0x3C,
                0xC2, 0x02, 0x80,
                0xC3, 0x00, 0x80);

            AssertBytes("JMP done\nDB 0AAh\ndone: HLT",
                0xC3, 0x04, 0x80,
                0xAA,
                0x76);
            Console.WriteLine("  [PASS] Test_Forward_And_Backward_Labels");
        }

        public static void Test_Equ_And_Forward_Equ()
        {
            AssertBytes("BASE EQU 8000h\nVALUE EQU BASE+10h\nORG BASE\nMVI A,VALUE & 0FFh\nDB VALUE >> 8",
                0x3E, 0x10, 0x80);

            AssertBytes("X EQU Y+1\nY EQU 5\nDB X", 0x06);

            AssertBytes("VALUE EQU later\nDB VALUE & 0FFh\nlater: DB 0AAh", 0x01, 0xAA);
            Console.WriteLine("  [PASS] Test_Equ_And_Forward_Equ");
        }

        public static void Test_Equ_Location_Counter_And_Symbol_Case()
        {
            AssembleResult result = AssembleOk("ORG 8000h\nStart EQU $\nDB Start & 0FFh\nDB Start >> 8");
            Assert(result.Success, "EQU using $ should succeed");
            Assert(result.Chunks.Count == 1, "Expected one chunk for EQU/$ program");
            Assert(BytesEqual(result.Chunks[0].Data, new byte[] { 0x00, 0x80 }),
                "Unexpected bytes for EQU/$: " + HexBytes(result.Chunks[0].Data));
            Assert(result.Symbols.ContainsKey("start"), "Symbol lookup should be case-insensitive");
            Assert(result.Symbols["START"] == 0x8000, "Symbol Start should equal 0x8000");
            Console.WriteLine("  [PASS] Test_Equ_Location_Counter_And_Symbol_Case");
        }

        public static void Test_Org_And_Chunks()
        {
            AssembleResult result = AssembleOk("ORG 8000h\nNOP\nORG 8010h\nHLT\nORG 8004h\nEI");
            Assert(result.Success, "ORG program should assemble");
            Assert(result.Chunks.Count == 3, "Expected three chunks, got " + result.Chunks.Count);
            Assert(result.Chunks[0].Address == 0x8000 && BytesEqual(result.Chunks[0].Data, new byte[] { 0x00 }),
                "First chunk should be NOP at 8000h");
            Assert(result.Chunks[1].Address == 0x8004 && BytesEqual(result.Chunks[1].Data, new byte[] { 0xFB }),
                "Second chunk should be EI at 8004h");
            Assert(result.Chunks[2].Address == 0x8010 && BytesEqual(result.Chunks[2].Data, new byte[] { 0x76 }),
                "Third chunk should be HLT at 8010h");
            Assert(result.StartAddress == 0x8000, "Start address should be 8000h");
            Assert(result.EndAddress == 0x8011, "End address should be 8011h");
            Assert(result.Image[0x8000] == 0x00 && result.Image[0x8004] == 0xFB && result.Image[0x8010] == 0x76,
                "Image should contain all emitted bytes");
            Console.WriteLine("  [PASS] Test_Org_And_Chunks");
        }

        public static void Test_Db_Strings_Characters_And_Commas()
        {
            byte[] expected = new byte[]
            {
                0x01, 0xFF, 0x2A, 0x2A, 0x41, 0x0A,
                0x48, 0x69, 0x2C, 0x3B, 0x20, 0x74, 0x68, 0x65, 0x72, 0x65,
                0x41, 0x42, 0x41, 0x42,
                0x41, 0x5C, 0x27, 0x22, 0x00, 0x0D, 0x09
            };
            AssertBytes(
                "DB 1, 0FFh, 101010b, 52o, 'A', '\\n', \"Hi,; there\", 'AB', \"A\", 'A' + 1, \"\", " +
                "'\\x41', '\\\\', '\\'', '\\\"', '\\0', '\\r', '\\t'",
                expected);
            Console.WriteLine("  [PASS] Test_Db_Strings_Characters_And_Commas");
        }

        public static void Test_Dw_LittleEndian_And_Location_Counter()
        {
            AssertBytes("DW 1234h, 5678h, $", 0x34, 0x12, 0x78, 0x56, 0x04, 0x80);
            AssertBytes("DW $", 0x00, 0x80);
            Console.WriteLine("  [PASS] Test_Dw_LittleEndian_And_Location_Counter");
        }

        public static void Test_Ds_Reserves_Without_Chunk()
        {
            AssembleResult result = AssembleOk("ORG 8000h\nDB 1,2\nDS 3\nDB 4");
            Assert(result.Chunks.Count == 2, "DS should split emission into two chunks, got " + result.Chunks.Count);
            Assert(result.Chunks[0].Address == 0x8000 && BytesEqual(result.Chunks[0].Data, new byte[] { 0x01, 0x02 }),
                "First DS chunk mismatch");
            Assert(result.Chunks[1].Address == 0x8005 && BytesEqual(result.Chunks[1].Data, new byte[] { 0x04 }),
                "Second DS chunk mismatch");
            Assert(result.Image[0x8002] == 0 && result.Image[0x8003] == 0 && result.Image[0x8004] == 0,
                "DS should zero-fill the reserved image region");
            Assert(result.EndAddress == 0x8006, "End address should include the final DB");

            AssembleResult tail = AssembleOk("DB 0AAh\nDS 4");
            Assert(tail.Chunks.Count == 1, "A trailing DS must not create a chunk");
            Assert(tail.EndAddress == 0x8001, "A trailing DS must not extend EndAddress");

            AssembleResult expression = AssembleOk("DB 1\nDS 2+2\nDB 2");
            Assert(expression.Chunks.Count == 2 && expression.Chunks[1].Address == 0x8005,
                "DS should accept an expression size");
            Console.WriteLine("  [PASS] Test_Ds_Reserves_Without_Chunk");
        }

        public static void Test_Expressions_Precedence_And_Operators()
        {
            AssertBytes("DB 1+2*3, (1+2)*3, 1<<2+1, 0FFh & 0Fh, 0Fh | 30h, 0FFh ^ 0Fh, " +
                        "~0FFh & 0FFh, 100/7, 100%7, -(-5), +5, 'A'+1",
                0x07, 0x09, 0x08, 0x0F, 0x3F, 0xF0, 0x00, 0x0E, 0x02, 0x05, 0x05, 0x42);

            AssertBytes("ORG 8000h\nDB ($ >> 8), $ & 0FFh", 0x80, 0x01);
            Console.WriteLine("  [PASS] Test_Expressions_Precedence_And_Operators");
        }

        public static void Test_Number_Literal_Formats()
        {
            byte[] expected = new byte[]
            {
                0x2A, 0x2A, 0x2A, 0x2A, 0x2A, 0x2A, 0x2A, 0x2A, 0x2A, 0x2A, 0x2A,
                0xFF, 0xFF
            };
            AssertBytes("DB 42, 0x2A, 2Ah, 2AH, $2A, 101010b, 101010B, 52o, 52O, 42d, 42D, 0FFh, 0FFH",
                expected);
            Console.WriteLine("  [PASS] Test_Number_Literal_Formats");
        }

        public static void Test_Comments_And_Quoted_Semicolons()
        {
            AssertBytes("; full line comment\nNOP ; trailing comment\nDB ';', \";\", 'A' ; after",
                0x00, 0x3B, 0x3B, 0x41);
            AssertBytes("DB \"a;b\"", 0x61, 0x3B, 0x62);
            AssertBytes("NOP;no space before comment", 0x00);
            Console.WriteLine("  [PASS] Test_Comments_And_Quoted_Semicolons");
        }

        public static void Test_Case_Insensitivity_And_Directive_Dots()
        {
            AssertBytes("mOv a,b", 0x78);
            AssertBytes(".org 8000h\n.db 1\n.dw 2\nmVi A,3\n.end",
                0x01, 0x02, 0x00, 0x3E, 0x03);
            AssertBytes("X .EQU 5\nDB X", 0x05);

            AssembleResult dottedDs = AssembleOk(".org 8000h\n.db 1\n.ds 2\n.db 2");
            Assert(dottedDs.Chunks.Count == 2 && dottedDs.Chunks[1].Address == 0x8003,
                "'.ds' should reserve space and split chunks");
            Console.WriteLine("  [PASS] Test_Case_Insensitivity_And_Directive_Dots");
        }

        public static void Test_Listing_Reflects_Source_Lines()
        {
            AssembleResult result = AssembleOk("; comment\n\nORG 8000h\nstart: MVI A,1\nHLT\nX EQU 5");
            Assert(result.Listing.Count == 6, "Expected six listing lines, got " + result.Listing.Count);
            Assert(!result.Listing[0].HasAddress && !result.Listing[1].HasAddress && !result.Listing[2].HasAddress,
                "Comments, blanks and ORG must not have addresses");
            Assert(!result.Listing[5].HasAddress, "EQU must not have an address");
            Assert(result.Listing[3].HasAddress && result.Listing[3].Address == 0x8000,
                "MVI listing address mismatch");
            Assert(BytesEqual(result.Listing[3].Bytes, new byte[] { 0x3E, 0x01 }),
                "MVI listing bytes mismatch");
            Assert(result.Listing[4].HasAddress && result.Listing[4].Address == 0x8002,
                "HLT listing address mismatch");
            Assert(result.Listing[4].Source == "HLT", "Listing source text should be preserved");
            Assert(result.Listing[0].Bytes != null && result.Listing[0].Bytes.Length == 0,
                "Listing bytes must be non-null and empty for non-emitting lines");
            Console.WriteLine("  [PASS] Test_Listing_Reflects_Source_Lines");
        }

        public static void Test_End_Stops_Processing()
        {
            AssembleResult result = AssembleOk("NOP\nEND\nHLT\nFOO");
            Assert(result.Chunks.Count == 1 && BytesEqual(result.Chunks[0].Data, new byte[] { 0x00 }),
                "END should stop emission");
            Assert(result.Image[0x8001] == 0, "HLT after END must not be emitted");
            Assert(result.Listing.Count == 4, "Listing should still contain lines after END");
            Assert(!result.Listing[2].HasAddress && !result.Listing[3].HasAddress,
                "Lines after END must not have addresses");
            AssertBytes("NOP\nEND 8000h", 0x00);
            Console.WriteLine("  [PASS] Test_End_Stops_Processing");
        }

        public static void Test_Symbols_Preserve_Case_And_Are_CaseInsensitive()
        {
            AssembleResult result = AssembleOk("MixedCase EQU 1234h\nlower: NOP");
            Assert(result.Symbols.ContainsKey("MIXEDCASE"), "MixedCase lookup should be case-insensitive");
            Assert(result.Symbols.ContainsKey("LOWER"), "lower lookup should be case-insensitive");
            Assert(result.Symbols["mixedcase"] == 0x1234, "MixedCase value mismatch");

            bool sawMixedCase = false;
            bool sawLower = false;
            foreach (string key in result.Symbols.Keys)
            {
                if (key == "MixedCase")
                    sawMixedCase = true;
                if (key == "lower")
                    sawLower = true;
            }
            Assert(sawMixedCase && sawLower, "Symbol keys should preserve original spelling");
            Console.WriteLine("  [PASS] Test_Symbols_Preserve_Case_And_Are_CaseInsensitive");
        }

        public static void Test_Complete_Program_At_8000()
        {
            string source =
                "; print a zero-terminated string to port 1\n" +
                "        ORG 8000h\n" +
                "start:  LXI H, message\n" +
                "        MVI C, 0\n" +
                "loop:   MOV A, M\n" +
                "        ORA A\n" +
                "        JZ done\n" +
                "        CALL putc\n" +
                "        INX H\n" +
                "        INR C\n" +
                "        JMP loop\n" +
                "putc:   MOV E, A\n" +
                "        MVI A, 1\n" +
                "        OUT 1\n" +
                "        MOV A, E\n" +
                "        RET\n" +
                "done:   HLT\n" +
                "message: DB 'HI', 0\n" +
                "        END\n";

            byte[] expected = new byte[]
            {
                0x21, 0x1A, 0x80,
                0x0E, 0x00,
                0x7E,
                0xB7,
                0xCA, 0x19, 0x80,
                0xCD, 0x12, 0x80,
                0x23,
                0x0C,
                0xC3, 0x05, 0x80,
                0x5F,
                0x3E, 0x01,
                0xD3, 0x01,
                0x7B,
                0xC9,
                0x76,
                0x48, 0x49, 0x00
            };

            AssembleResult result = AssembleOk(source, 0x8000);
            Assert(result.Chunks.Count == 1, "Realistic program should be one chunk");
            Assert(BytesEqual(result.Chunks[0].Data, expected),
                "Realistic program bytes mismatch: " + HexBytes(result.Chunks[0].Data));
            Assert(result.StartAddress == 0x8000, "Realistic program start mismatch");
            Assert(result.EndAddress == 0x801D, "Realistic program end mismatch");
            Assert(result.Symbols["start"] == 0x8000, "start symbol mismatch");
            Assert(result.Symbols["loop"] == 0x8005, "loop symbol mismatch");
            Assert(result.Symbols["putc"] == 0x8012, "putc symbol mismatch");
            Assert(result.Symbols["done"] == 0x8019, "done symbol mismatch");
            Assert(result.Symbols["message"] == 0x801A, "message symbol mismatch");
            Console.WriteLine("  [PASS] Test_Complete_Program_At_8000");
        }

        public static void Test_Assemble_Default_Origin()
        {
            AssembleResult result = I8080Assembler.Assemble("NOP");
            Assert(result.Success, "Default-origin assembly should succeed");
            Assert(result.StartAddress == 0x0000 && result.EndAddress == 0x0001,
                "Default origin should be 0000h");
            Assert(result.Chunks.Count == 1 && result.Chunks[0].Address == 0x0000 && result.Image[0] == 0x00,
                "Default-origin chunk mismatch");

            AssembleResult at9000 = I8080Assembler.Assemble("JMP start\nstart: HLT", 0x9000);
            Assert(at9000.Success, "Origin-9000 assembly should succeed");
            Assert(BytesEqual(at9000.Chunks[0].Data, new byte[] { 0xC3, 0x03, 0x90, 0x76 }),
                "Origin-9000 bytes mismatch: " + HexBytes(at9000.Chunks[0].Data));
            Console.WriteLine("  [PASS] Test_Assemble_Default_Origin");
        }

        public static void Test_Empty_And_Only_Equ_Source()
        {
            AssembleResult empty = I8080Assembler.Assemble("");
            Assert(empty.Success, "Empty source should succeed");
            Assert(empty.Chunks.Count == 0 && empty.Listing.Count == 0, "Empty source should have no chunks/listings");
            Assert(empty.StartAddress == 0 && empty.EndAddress == 0, "Empty source should have zero start/end");
            Assert(empty.Image.Length == 65536, "Image should always be 65536 bytes");

            AssembleResult equ = I8080Assembler.Assemble("X EQU 5", 0x8000);
            Assert(equ.Success && equ.Chunks.Count == 0, "Only-EQU source should succeed without chunks");
            Assert(equ.StartAddress == 0x8000 && equ.EndAddress == 0x8000, "Only-EQU source should use origin");
            Assert(equ.Symbols["x"] == 5, "Only-EQU source symbol mismatch");

            AssembleResult ds = I8080Assembler.Assemble("DS 10", 0x8000);
            Assert(ds.Success && ds.Chunks.Count == 0, "Only-DS source should succeed without chunks");
            Assert(ds.StartAddress == 0x8000 && ds.EndAddress == 0x8000, "Only-DS source should use origin");
            Console.WriteLine("  [PASS] Test_Empty_And_Only_Equ_Source");
        }

        public static void Test_Error_Unknown_Mnemonic()
        {
            AssertFails("FOO A,B", 1, "Unknown mnemonic");
            AssertFails("ORGX 8000h", 1, "Unknown mnemonic");
            Console.WriteLine("  [PASS] Test_Error_Unknown_Mnemonic");
        }

        public static void Test_Error_Duplicate_Label()
        {
            AssertFails("start: NOP\nstart: HLT", 2, "Duplicate label");
            AssertFails("start: NOP\nSTART: HLT", 2, "Duplicate label");
            AssertFails("X EQU 1\nx EQU 2", 2, "Duplicate label");
            Console.WriteLine("  [PASS] Test_Error_Duplicate_Label");
        }

        public static void Test_Error_Invalid_Register_And_Pair()
        {
            AssertFails("MOV A,SP", 1, "Invalid register");
            AssertFails("MVI SP,1", 1, "Invalid register");
            AssertFails("ADD PSW", 1, "Invalid register");
            AssertFails("LXI PSW,0", 1, "Invalid register pair");
            AssertFails("INX PSW", 1, "Invalid register pair");
            AssertFails("PUSH SP", 1, "Invalid register pair");
            AssertFails("POP SP", 1, "Invalid register pair");
            AssertFails("LDAX H", 1, "Invalid register pair");
            AssertFails("STAX SP", 1, "Invalid register pair");
            AssertFails("DAD PSW", 1, "Invalid register pair");
            Console.WriteLine("  [PASS] Test_Error_Invalid_Register_And_Pair");
        }

        public static void Test_Error_Immediate_Out_Of_Range()
        {
            AssertFails("MVI A,100h", 1, "out of range");
            AssertFails("MVI A,-1", 1, "out of range");
            AssertFails("ADI 256", 1, "out of range");
            AssertFails("DB 256", 1, "out of range");
            AssertFails("DB 1 << 20", 1, "out of range");
            AssertFails("DW 10000h", 1, "out of range");
            AssertFails("LXI H,10000h", 1, "out of range");
            AssertFails("IN 100h", 1, "out of range");
            AssertFails("OUT -1", 1, "out of range");
            AssertFails("RST 8", 1, "out of range");
            AssertFails("ORG 10000h", 1, "out of range");

            AssembleResult noWrap = I8080Assembler.Assemble("MVI A,100h\nHLT", 0x8000);
            Assert(!noWrap.Success, "Out-of-range MVI should fail");
            Assert(noWrap.Image[0x8000] == 0x00 && noWrap.Image[0x8002] == 0x76,
                "Out-of-range immediate must not be wrapped or shift following code");
            Console.WriteLine("  [PASS] Test_Error_Immediate_Out_Of_Range");
        }

        public static void Test_Error_Undefined_Symbol()
        {
            AssertFails("JMP nowhere", 1, "Undefined symbol");
            AssertFails("DB missing", 1, "Undefined symbol");
            AssertFails("MVI A,unknown", 1, "Undefined symbol");
            AssertFails("X EQU Y\nDB X", 1, "Unable to resolve EQU");
            Console.WriteLine("  [PASS] Test_Error_Undefined_Symbol");
        }

        public static void Test_Error_Invalid_Condition_And_Operands()
        {
            AssertFails("JZZ 0", 1, "Invalid condition code");
            AssertFails("CZZ 0", 1, "Invalid condition code");
            AssertFails("RZZ", 1, "Invalid condition code");
            AssertFails("MVI A", 1, "requires 2 operands");
            AssertFails("MOV A,B,C", 1, "requires 2 operands");
            AssertFails("NOP 1", 1, "does not take operands");
            AssertFails("RET 1", 1, "does not take operands");
            AssertFails("IN", 1, "requires 1 operand");
            AssertFails("LXI H", 1, "requires 2 operands");
            AssertFails("CALL", 1, "requires 1 operand");
            AssertFails("RST", 1, "requires 1 operand");
            Console.WriteLine("  [PASS] Test_Error_Invalid_Condition_And_Operands");
        }

        public static void Test_Error_Division_By_Zero_And_Address_Overflow()
        {
            AssertFails("DB 1/0", 1, "Division by zero");
            AssertFails("DB 10%0", 1, "Division by zero");

            AssembleResult overflow = I8080Assembler.Assemble("NOP\nNOP", 0xFFFF);
            Assert(!overflow.Success, "Address overflow should fail");
            bool found = false;
            foreach (AssemblerError error in overflow.Errors)
            {
                if (error.LineNumber == 2 && error.Message.IndexOf("overflow", StringComparison.OrdinalIgnoreCase) >= 0)
                    found = true;
            }
            Assert(found, "Expected address overflow error on line 2: " + FormatErrors(overflow));

            AssertFails("ORG 1\nDS 0FFFFh\nNOP", 3, "overflow");
            Console.WriteLine("  [PASS] Test_Error_Division_By_Zero_And_Address_Overflow");
        }

        public static void Test_Error_Listing_Still_Returned_On_Failure()
        {
            AssembleResult result = I8080Assembler.Assemble("MOV A,SP\nHLT\nDB 99h", 0x8000);
            Assert(!result.Success, "Invalid register program should fail");
            Assert(result.Listing.Count == 3, "Partial assembler should still list every line");
            Assert(!result.Listing[0].HasAddress && result.Listing[0].Bytes.Length == 0,
                "Errored line should have no bytes");
            Assert(result.Listing[1].HasAddress && result.Listing[1].Address == 0x8001,
                "Following line should retain its computed address");
            Assert(BytesEqual(result.Listing[1].Bytes, new byte[] { 0x76 }) &&
                   BytesEqual(result.Listing[2].Bytes, new byte[] { 0x99 }),
                "Valid lines should still emit partial output");
            Assert(result.Chunks.Count == 1 && result.Chunks[0].Address == 0x8001,
                "Partial chunk should start after the errored line");
            Console.WriteLine("  [PASS] Test_Error_Listing_Still_Returned_On_Failure");
        }
    }
}
