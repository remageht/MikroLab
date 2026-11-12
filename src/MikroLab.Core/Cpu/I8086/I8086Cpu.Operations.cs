using System;

namespace MikroLab.Core.Cpu.I8086
{
    public sealed partial class I8086Cpu
    {
        // ------------------------------------------------------------------
        // stack
        // ------------------------------------------------------------------

        private void PushWord(ushort value)
        {
            State.SP = (ushort)(State.SP - 2);
            WriteWord(State.SS, State.SP, value);
        }

        private ushort PopWord()
        {
            ushort value = ReadWord(State.SS, State.SP);
            State.SP = (ushort)(State.SP + 2);
            return value;
        }

        private void PushSegment(int segment)
        {
            PushWord(GetSegment(segment));
        }

        private void PopSegment(int segment)
        {
            SetSegment(segment, PopWord());
        }

        // ------------------------------------------------------------------
        // decimal adjust
        // ------------------------------------------------------------------

        private void ExecuteDaa()
        {
            byte al = State.AL;
            bool carry = State.Carry;
            if ((al & 0x0F) > 9 || State.Auxiliary)
            {
                al = (byte)(al + 6);
                State.Auxiliary = true;
                carry = carry || al < 6;
            }
            else
            {
                State.Auxiliary = false;
            }

            if (al > 0x9F || carry)
            {
                al = (byte)(al + 0x60);
                carry = true;
            }

            State.AL = al;
            State.Carry = carry;
            SetSizeFlags(State.AL);
        }

        private void ExecuteDas()
        {
            byte al = State.AL;
            bool carry = State.Carry;
            if ((al & 0x0F) > 9 || State.Auxiliary)
            {
                al = (byte)(al - 6);
                State.Auxiliary = true;
                carry = carry || al > 0xFA;
            }
            else
            {
                State.Auxiliary = false;
            }

            if (al > 0x99 || carry)
            {
                al = (byte)(al - 0x60);
                carry = true;
            }

            State.AL = al;
            State.Carry = carry;
            SetSizeFlags(State.AL);
        }

        private void ExecuteAaa()
        {
            if ((State.AL & 0x0F) > 9 || State.Auxiliary)
            {
                State.AX = (ushort)(State.AX + 0x0106);
                State.Auxiliary = true;
            }
            else
            {
                State.Auxiliary = false;
            }

            State.AL &= 0x0F;
        }

        private void ExecuteAas()
        {
            if ((State.AL & 0x0F) > 9 || State.Auxiliary)
            {
                State.AX = (ushort)(State.AX - 0x0106);
                State.Auxiliary = true;
            }
            else
            {
                State.Auxiliary = false;
            }

            State.AL &= 0x0F;
        }

        // ------------------------------------------------------------------
        // misc groups
        // ------------------------------------------------------------------

        private void ExecuteGroupOp6(byte op)
        {
            switch (op)
            {
                case 0x60: // PUSHA (80186+)
                {
                    ushort sp = State.SP;
                    PushWord(State.AX);
                    PushWord(State.CX);
                    PushWord(State.DX);
                    PushWord(State.BX);
                    PushWord(State.SP);
                    PushWord(State.BP);
                    PushWord(State.SI);
                    PushWord(State.DI);
                    State.SP = sp;
                    return;
                }

                case 0x61: // POPA (80186+)
                {
                    State.DI = PopWord();
                    State.SI = PopWord();
                    State.BP = PopWord();
                    PopWord();
                    State.BX = PopWord();
                    State.DX = PopWord();
                    State.CX = PopWord();
                    State.AX = PopWord();
                    return;
                }

                case 0x68: // PUSH imm16
                    PushWord(FetchWord());
                    return;

                case 0x6A: // PUSH imm8
                    PushWord((ushort)(sbyte)FetchByte());
                    return;

                default:
                    UnsupportedCommand?.Invoke(State.IP, op);
                    _halted = true;
                    return;
            }
        }

        private void JumpConditional(byte condition)
        {
            sbyte rel = (sbyte)FetchByte();
            bool take;

            switch (condition)
            {
                case 0x0: take = State.Overflow; break;
                case 0x1: take = !State.Overflow; break;
                case 0x2: take = State.Carry; break;
                case 0x3: take = !State.Carry; break;
                case 0x4: take = State.Zero; break;
                case 0x5: take = !State.Zero; break;
                case 0x6: take = State.Carry || State.Zero; break;
                case 0x7: take = !State.Carry && !State.Zero; break;
                case 0x8: take = State.Sign; break;
                case 0x9: take = !State.Sign; break;
                case 0xA: take = State.Parity; break;
                case 0xB: take = !State.Parity; break;
                case 0xC: take = State.Sign != State.Overflow; break;
                case 0xD: take = State.Sign == State.Overflow; break;
                case 0xE: take = State.Zero || State.Sign != State.Overflow; break;
                default: take = !State.Zero && State.Sign == State.Overflow; break;
            }

            if (take)
                State.IP = (ushort)(State.IP + rel);
        }

        private void ExecuteImmediateGroup(byte op)
        {
            Modrm m = DecodeModrm();
            int group = m.Reg;

            if (op == 0x81)
            {
                ushort right = FetchWord();
                ushort left = GetRm16(m);
                ushort result = ComputeGroup16(group, left, right);
                if (group != 7)
                    SetRm16(m, result);
            }
            else
            {
                byte right = FetchByte();
                if (op == 0x83)
                {
                    ushort wide = (ushort)(sbyte)right;
                    ushort left = GetRm16(m);
                    ushort result = ComputeGroup16(group, left, wide);
                    if (group != 7)
                        SetRm16(m, result);
                }
                else
                {
                    byte left = GetRm8(m);
                    byte result = ComputeGroup8(group, left, right);
                    if (group != 7)
                        SetRm8(m, result);
                }
            }
        }

        private void ExecuteTestRm(bool wide)
        {
            Modrm m = DecodeModrm();
            if (wide)
            {
                ushort left = GetRm16(m);
                ushort right = GetReg16(m.Reg);
                SetLogicFlags((ushort)(left & right));
            }
            else
            {
                byte left = GetRm8(m);
                byte right = GetReg8(m.Reg);
                SetLogicFlags((byte)(left & right));
            }
        }

        private void ExecuteTestAccumulator(bool wide)
        {
            if (wide)
            {
                ushort imm = FetchWord();
                SetLogicFlags((ushort)(State.AX & imm));
            }
            else
            {
                byte imm = FetchByte();
                SetLogicFlags((byte)(State.AL & imm));
            }
        }

        private void ExecuteXchgRm(bool wide)
        {
            Modrm m = DecodeModrm();
            if (wide)
            {
                ushort left = GetRm16(m);
                ushort right = GetReg16(m.Reg);
                SetRm16(m, right);
                SetReg16(m.Reg, left);
            }
            else
            {
                byte left = GetRm8(m);
                byte right = GetReg8(m.Reg);
                SetRm8(m, right);
                SetReg8(m.Reg, left);
            }
        }

        private void ExchangeWithAccumulator(byte reg)
        {
            ushort accumulator = State.AX;
            State.AX = GetReg16(reg);
            SetReg16(reg, accumulator);
        }

        private void ExecuteMovRm(byte op)
        {
            Modrm m = DecodeModrm();
            switch (op)
            {
                case 0x88:
                {
                    byte value = GetReg8(m.Reg);
                    SetRm8(m, value);
                    return;
                }
                case 0x89:
                {
                    ushort value = GetReg16(m.Reg);
                    SetRm16(m, value);
                    return;
                }
                case 0x8A:
                {
                    byte value = GetRm8(m);
                    SetReg8(m.Reg, value);
                    return;
                }
                default:
                {
                    ushort value = GetRm16(m);
                    SetReg16(m.Reg, value);
                    return;
                }
            }
        }

        private void ExecuteMovSegmentToRm()
        {
            Modrm m = DecodeModrm();
            SetRm16(m, GetSegment(m.Reg));
        }

        private void ExecuteMovRmToSegment()
        {
            Modrm m = DecodeModrm();
            SetSegment(m.Reg, GetRm16(m));
        }

        private void ExecuteLea()
        {
            Modrm m = DecodeModrm();
            if (m.IsRegister)
            {
                UnsupportedCommand?.Invoke(State.IP, 0x8D);
                _halted = true;
                return;
            }

            SetReg16(m.Reg, m.Offset);
        }

        private void ExecutePopRm()
        {
            Modrm m = DecodeModrm();
            SetRm16(m, PopWord());
        }

        private void ExecuteMovImmediate(bool wide)
        {
            Modrm m = DecodeModrm();
            if (wide)
                SetRm16(m, FetchWord());
            else
                SetRm8(m, FetchByte());
        }

        private void ExecuteMovAccumulatorToMemory(byte op)
        {
            ushort offset = FetchWord();
            ushort segment = _segmentOverride >= 0 ? GetSegment(_segmentOverride) : State.DS;
            switch (op)
            {
                case 0xA0:
                    State.AL = ReadByte(segment, offset);
                    return;
                case 0xA1:
                    State.AX = ReadWord(segment, offset);
                    return;
                case 0xA2:
                    WriteByte(segment, offset, State.AL);
                    return;
                default:
                    WriteWord(segment, offset, State.AX);
                    return;
            }
        }

        private void ExecuteLes()
        {
            Modrm m = DecodeModrm();
            if (m.IsRegister)
            {
                UnsupportedCommand?.Invoke(State.IP, 0xC4);
                _halted = true;
                return;
            }

            SetReg16(m.Reg, ReadWord(m.Segment, m.Offset));
            State.ES = ReadWord(m.Segment, (ushort)(m.Offset + 2));
        }

        private void ExecuteLds()
        {
            Modrm m = DecodeModrm();
            if (m.IsRegister)
            {
                UnsupportedCommand?.Invoke(State.IP, 0xC5);
                _halted = true;
                return;
            }

            SetReg16(m.Reg, ReadWord(m.Segment, m.Offset));
            State.DS = ReadWord(m.Segment, (ushort)(m.Offset + 2));
        }

        private void ExecuteFarCall()
        {
            ushort offset = FetchWord();
            ushort segment = FetchWord();
            PushWord(State.CS);
            PushWord(State.IP);
            State.IP = offset;
            State.CS = segment;
        }

        private void ExecuteFarJump()
        {
            ushort offset = FetchWord();
            ushort segment = FetchWord();
            State.IP = offset;
            State.CS = segment;
        }

        private void ExecuteLoop(byte op)
        {
            sbyte rel = (sbyte)FetchByte();
            State.CX = (ushort)(State.CX - 1);
            bool take = State.CX != 0;
            if (op == 0xE0)
                take = take && !State.Zero;
            else if (op == 0xE1)
                take = take && State.Zero;

            if (take)
                State.IP = (ushort)(State.IP + rel);
        }

        // ------------------------------------------------------------------
        // string operations
        // ------------------------------------------------------------------

        private ushort Advance(ushort index, bool word)
        {
            int step = word ? 2 : 1;
            return State.Direction ? (ushort)(index - step) : (ushort)(index + step);
        }

        private void ExecuteMoveString(bool word)
        {
            bool repeat = _repeat || _repeatNotEqual;
            do
            {
                if (word)
                {
                    WriteWord(State.ES, State.DI, ReadWord(State.DS, State.SI));
                }
                else
                {
                    WriteByte(State.ES, State.DI, ReadByte(State.DS, State.SI));
                }

                State.SI = Advance(State.SI, word);
                State.DI = Advance(State.DI, word);
                State.CX = (ushort)(State.CX - 1);
            }
            while (repeat && State.CX != 0);
        }

        private void ExecuteStoreString(bool word)
        {
            bool repeat = _repeat || _repeatNotEqual;
            do
            {
                if (word)
                    WriteWord(State.ES, State.DI, State.AX);
                else
                    WriteByte(State.ES, State.DI, State.AL);

                State.DI = Advance(State.DI, word);
                State.CX = (ushort)(State.CX - 1);
            }
            while (repeat && State.CX != 0);
        }

        private void ExecuteLoadString(bool word)
        {
            bool repeat = _repeat || _repeatNotEqual;
            do
            {
                if (word)
                    State.AX = ReadWord(State.DS, State.SI);
                else
                    State.AL = ReadByte(State.DS, State.SI);

                State.SI = Advance(State.SI, word);
                State.CX = (ushort)(State.CX - 1);
            }
            while (repeat && State.CX != 0);
        }

        private void ExecuteCompareString(bool word)
        {
            bool repeat = _repeat || _repeatNotEqual;
            do
            {
                if (word)
                {
                    ushort left = ReadWord(State.DS, State.SI);
                    ushort right = ReadWord(State.ES, State.DI);
                    SetSubFlags((ushort)(left - right), left, right);
                }
                else
                {
                    byte left = ReadByte(State.DS, State.SI);
                    byte right = ReadByte(State.ES, State.DI);
                    SetSubFlags((byte)(left - right), left, right);
                }

                State.SI = Advance(State.SI, word);
                State.DI = Advance(State.DI, word);
                State.CX = (ushort)(State.CX - 1);
            }
            while (repeat && State.CX != 0 && (_repeat ? State.Zero : !State.Zero));
        }

        private void ExecuteScanString(bool word)
        {
            bool repeat = _repeat || _repeatNotEqual;
            do
            {
                if (word)
                {
                    ushort left = State.AX;
                    ushort right = ReadWord(State.ES, State.DI);
                    SetSubFlags((ushort)(left - right), left, right);
                }
                else
                {
                    byte left = State.AL;
                    byte right = ReadByte(State.ES, State.DI);
                    SetSubFlags((byte)(left - right), left, right);
                }

                State.DI = Advance(State.DI, word);
                State.CX = (ushort)(State.CX - 1);
            }
            while (repeat && State.CX != 0 && (_repeat ? State.Zero : !State.Zero));
        }

        // ------------------------------------------------------------------
        // shifts
        // ------------------------------------------------------------------

        private void ExecuteShiftRm(byte op)
        {
            Modrm m = DecodeModrm();
            int count;
            switch (op)
            {
                case 0xC0:
                case 0xC1:
                    count = FetchByte();
                    break;
                case 0xD0:
                case 0xD1:
                    count = 1;
                    break;
                default:
                    count = State.CL;
                    break;
            }

            bool wide = op == 0xC1 || op == 0xD1 || op == 0xD3;
            byte result8 = 0;
            bool carry8 = false;
            bool overflow8 = false;
            ushort result16 = 0;
            bool carry16 = false;
            bool overflow16 = false;

            if (wide)
                ExecuteShift16(m.Reg, GetRm16(m), count, out result16, out carry16, out overflow16);
            else
                ExecuteShift8(m.Reg, GetRm8(m), count, out result8, out carry8, out overflow8);

            if (wide)
            {
                State.Carry = carry16;
                State.Overflow = overflow16;
                State.Zero = result16 == 0;
                State.Sign = (result16 & 0x8000) != 0;
                State.Parity = ParityEven((byte)result16);
                SetRm16(m, result16);
            }
            else
            {
                State.Carry = carry8;
                State.Overflow = overflow8;
                State.Zero = result8 == 0;
                State.Sign = (result8 & 0x80) != 0;
                State.Parity = ParityEven(result8);
                SetRm8(m, result8);
            }
        }

        private void ExecuteShift8(int type, byte value, int count, out byte result, out bool carry, out bool overflow)
        {
            result = value;
            carry = State.Carry;
            overflow = false;

            count &= 0x0F;                    // 16-bit mode masks the count to four bits
            if (count == 0)
                return;

            int rotations = (type == 2 || type == 3) ? count % 9 : count % 8;
            if (rotations == 0)
                rotations = (type == 2 || type == 3) ? 9 : 8;

            bool carryIn = State.Carry;
            bool overflowResult = false;

            for (int i = 0; i < rotations; i++)
            {
                switch (type)
                {
                    case 0: // ROL
                    {
                        bool nextCarry = (result & 0x80) != 0;
                        result = (byte)((result << 1) | (result >> 7));
                        carry = nextCarry;
                        overflowResult = ((result & 0x80) != 0) != nextCarry;
                        break;
                    }
                    case 1: // ROR
                    {
                        bool nextCarry = (result & 0x01) != 0;
                        result = (byte)((result >> 1) | (result << 7));
                        carry = nextCarry;
                        overflowResult = ((result & 0x80) != 0) != ((result & 0x40) != 0);
                        break;
                    }
                    case 2: // RCL
                    {
                        bool nextCarry = (result & 0x80) != 0;
                        result = (byte)((result << 1) | (carryIn ? 1 : 0));
                        carryIn = nextCarry;
                        carry = nextCarry;
                        overflowResult = ((result & 0x80) != 0) != nextCarry;
                        break;
                    }
                    case 3: // RCR
                    {
                        bool nextCarry = (result & 0x01) != 0;
                        result = (byte)((result >> 1) | (carryIn ? 0x80 : 0));
                        carryIn = nextCarry;
                        carry = nextCarry;
                        overflowResult = ((result & 0x80) != 0) != ((result & 0x40) != 0);
                        break;
                    }
                    case 4: // SHL
                    case 6: // SAL
                    {
                        bool nextCarry = (result & 0x80) != 0;
                        result = (byte)(result << 1);
                        carry = nextCarry;
                        overflowResult = ((result & 0x80) != 0) != nextCarry;
                        break;
                    }
                    case 5: // SHR
                    {
                        bool nextCarry = (result & 0x01) != 0;
                        result = (byte)(result >> 1);
                        carry = nextCarry;
                        overflowResult = ((result & 0x80) != 0);
                        break;
                    }
                    default: // SAR
                    {
                        bool nextCarry = (result & 0x01) != 0;
                        result = (byte)((result >> 1) | (result & 0x80));
                        carry = nextCarry;
                        overflowResult = false;
                        break;
                    }
                }
            }

            overflow = overflowResult;
        }

        private void ExecuteShift16(int type, ushort value, int count, out ushort result, out bool carry, out bool overflow)
        {
            result = value;
            carry = State.Carry;
            overflow = false;

            count &= 0x0F;
            if (count == 0)
                return;

            int rotations = (type == 2 || type == 3) ? count % 17 : count % 16;
            if (rotations == 0)
                rotations = (type == 2 || type == 3) ? 17 : 16;

            bool carryIn = State.Carry;
            bool overflowResult = false;

            for (int i = 0; i < rotations; i++)
            {
                switch (type)
                {
                    case 0: // ROL
                    {
                        bool nextCarry = (result & 0x8000) != 0;
                        result = (ushort)((result << 1) | (result >> 15));
                        carry = nextCarry;
                        overflowResult = ((result & 0x8000) != 0) != nextCarry;
                        break;
                    }
                    case 1: // ROR
                    {
                        bool nextCarry = (result & 0x0001) != 0;
                        result = (ushort)((result >> 1) | (result << 15));
                        carry = nextCarry;
                        overflowResult = ((result & 0x8000) != 0) != ((result & 0x4000) != 0);
                        break;
                    }
                    case 2: // RCL
                    {
                        bool nextCarry = (result & 0x8000) != 0;
                        result = (ushort)((result << 1) | (carryIn ? 1 : 0));
                        carryIn = nextCarry;
                        carry = nextCarry;
                        overflowResult = ((result & 0x8000) != 0) != nextCarry;
                        break;
                    }
                    case 3: // RCR
                    {
                        bool nextCarry = (result & 0x0001) != 0;
                        result = (ushort)((result >> 1) | (carryIn ? 0x8000 : 0));
                        carryIn = nextCarry;
                        carry = nextCarry;
                        overflowResult = ((result & 0x8000) != 0) != ((result & 0x4000) != 0);
                        break;
                    }
                    case 4: // SHL
                    case 6: // SAL
                    {
                        bool nextCarry = (result & 0x8000) != 0;
                        result = (ushort)(result << 1);
                        carry = nextCarry;
                        overflowResult = ((result & 0x8000) != 0) != nextCarry;
                        break;
                    }
                    case 5: // SHR
                    {
                        bool nextCarry = (result & 0x0001) != 0;
                        result = (ushort)(result >> 1);
                        carry = nextCarry;
                        overflowResult = (result & 0x8000) != 0;
                        break;
                    }
                    default: // SAR
                    {
                        bool nextCarry = (result & 0x0001) != 0;
                        result = (ushort)((result >> 1) | (result & 0x8000));
                        carry = nextCarry;
                        overflowResult = false;
                        break;
                    }
                }
            }

            overflow = overflowResult;
        }

        // ------------------------------------------------------------------
        // group F6/F7 and FF
        // ------------------------------------------------------------------

        private void ExecuteGroupF6F7(bool wide)
        {
            Modrm m = DecodeModrm();
            switch (m.Reg)
            {
                case 0: // TEST rm, imm
                    if (wide)
                    {
                        ushort imm = FetchWord();
                        SetLogicFlags((ushort)(GetRm16(m) & imm));
                    }
                    else
                    {
                        byte imm = FetchByte();
                        SetLogicFlags((byte)(GetRm8(m) & imm));
                    }
                    return;

                case 2: // NOT
                    if (wide)
                        SetRm16(m, (ushort)~GetRm16(m));
                    else
                        SetRm8(m, (byte)~GetRm8(m));
                    return;

                case 3: // NEG
                    if (wide)
                    {
                        ushort value = GetRm16(m);
                        ushort result = (ushort)(0 - value);
                        SetSubFlags(result, 0, value);
                        SetRm16(m, result);
                    }
                    else
                    {
                        byte value = GetRm8(m);
                        byte result = (byte)(0 - value);
                        SetSubFlags(result, 0, value);
                        SetRm8(m, result);
                    }
                    return;

                case 4: // MUL
                    if (wide)
                    {
                        ushort operand = GetRm16(m);
                        uint product = (uint)State.AX * operand;
                        State.AX = (ushort)(product & 0xFFFF);
                        State.DX = (ushort)(product >> 16);
                        bool overflow = State.DX != 0;
                        State.Carry = overflow;
                        State.Overflow = overflow;
                    }
                    else
                    {
                        byte operand = GetRm8(m);
                        ushort product = (ushort)(State.AL * operand);
                        State.AL = (byte)(product & 0xFF);
                        State.AH = (byte)(product >> 8);
                        bool overflow = State.AH != 0;
                        State.Carry = overflow;
                        State.Overflow = overflow;
                    }
                    return;

                case 5: // IMUL
                    if (wide)
                    {
                        short operand = (short)GetRm16(m);
                        int product = (short)State.AX * operand;
                        State.AX = (ushort)(product & 0xFFFF);
                        State.DX = (ushort)((product >> 16) & 0xFFFF);
                        ushort expected = (ushort)((State.AX & 0x8000) != 0 ? 0xFFFF : 0x0000);
                        bool overflow = State.DX != expected;
                        State.Carry = overflow;
                        State.Overflow = overflow;
                    }
                    else
                    {
                        sbyte operand = (sbyte)GetRm8(m);
                        int product = (sbyte)State.AL * operand;
                        State.AL = (byte)(product & 0xFF);
                        State.AH = (byte)((product >> 8) & 0xFF);
                        byte expected = (byte)((State.AL & 0x80) != 0 ? 0xFF : 0x00);
                        bool overflow = State.AH != expected;
                        State.Carry = overflow;
                        State.Overflow = overflow;
                    }
                    return;

                case 6: // DIV
                    if (wide)
                    {
                        ushort divisor = GetRm16(m);
                        if (divisor == 0)
                            throw new DivideByZeroException();
                        uint dividend = ((uint)State.DX << 16) | State.AX;
                        uint quotient = dividend / divisor;
                        if (quotient > 0xFFFF)
                            throw new DivideByZeroException();
                        State.AX = (ushort)quotient;
                        State.DX = (ushort)(dividend % divisor);
                    }
                    else
                    {
                        byte divisor = GetRm8(m);
                        if (divisor == 0)
                            throw new DivideByZeroException();
                        ushort dividend = State.AX;
                        ushort quotient = (ushort)(dividend / divisor);
                        if (quotient > 0xFF)
                            throw new DivideByZeroException();
                        State.AL = (byte)quotient;
                        State.AH = (byte)(dividend % divisor);
                    }
                    return;

                default: // IDIV
                    if (wide)
                    {
                        short divisor = (short)GetRm16(m);
                        if (divisor == 0)
                            throw new DivideByZeroException();
                        int dividend = (short)State.AX;
                        int quotient = dividend / divisor;
                        if (quotient > 32767 || quotient < -32768)
                            throw new DivideByZeroException();
                        State.AX = (ushort)quotient;
                        State.DX = (ushort)(dividend % divisor);
                    }
                    else
                    {
                        sbyte divisor = (sbyte)GetRm8(m);
                        if (divisor == 0)
                            throw new DivideByZeroException();
                        short dividend = (short)State.AX;
                        int quotient = dividend / divisor;
                        if (quotient > 127 || quotient < -128)
                            throw new DivideByZeroException();
                        State.AL = (byte)quotient;
                        State.AH = (byte)(dividend % divisor);
                    }
                    return;
            }
        }

        private void ExecuteGroupFF()
        {
            Modrm m = DecodeModrm();
            switch (m.Reg)
            {
                case 0:
                {
                    ushort value = GetRm16(m);
                    ushort result = (ushort)(value + 1);
                    SetIncFlags(result, value);
                    SetRm16(m, result);
                    return;
                }
                case 1:
                {
                    ushort value = GetRm16(m);
                    ushort result = (ushort)(value - 1);
                    SetDecFlags(result, value);
                    SetRm16(m, result);
                    return;
                }
                case 2:
                {
                    ushort target = GetRm16(m);
                    PushWord(State.IP);
                    State.IP = target;
                    return;
                }
                case 3:
                {
                    if (m.IsRegister)
                    {
                        UnsupportedCommand?.Invoke(State.IP, 0xFF);
                        _halted = true;
                        return;
                    }

                    ushort offset = GetRm16(m);
                    ushort segment = ReadWord(m.Segment, (ushort)(m.Offset + 2));
                    PushWord(State.CS);
                    PushWord(State.IP);
                    State.IP = offset;
                    State.CS = segment;
                    return;
                }
                case 4:
                    State.IP = GetRm16(m);
                    return;

                case 5:
                {
                    if (m.IsRegister)
                    {
                        UnsupportedCommand?.Invoke(State.IP, 0xFF);
                        _halted = true;
                        return;
                    }

                    ushort offset = GetRm16(m);
                    ushort segment = ReadWord(m.Segment, (ushort)(m.Offset + 2));
                    State.IP = offset;
                    State.CS = segment;
                    return;
                }
                case 6:
                    PushWord(GetRm16(m));
                    return;

                default:
                    UnsupportedCommand?.Invoke(State.IP, 0xFF);
                    _halted = true;
                    return;
            }
        }

        // ------------------------------------------------------------------
        // interrupts and DOS services
        // ------------------------------------------------------------------

        private void TriggerInterrupt(int vector)
        {
            if (vector == 0x21)
            {
                HandleDosService();
                return;
            }

            if (vector == 0x16)
            {
                HandleKeyboardService();
                return;
            }

            PushWord(State.Flags);
            PushWord(State.CS);
            PushWord(State.IP);
            State.Interrupt = false;

            int table = vector * 4;
            State.IP = ReadWord(0, (ushort)table);
            State.CS = ReadWord(0, (ushort)(table + 2));
            Interrupt?.Invoke(vector);
        }

        private void HandleDosService()
        {
            switch (State.AH)
            {
                case 0x01: // read character with echo
                    State.AL = _console.ReadKey();
                    _console.Write((char)State.AL);
                    State.Zero = false;
                    return;

                case 0x02: // write character
                    _console.Write((char)State.DL);
                    return;

                case 0x06: // direct console input/output
                    if (State.DL == 0xFF)
                    {
                        State.AL = _console.ReadKey();
                        State.Zero = false;
                    }
                    else
                    {
                        _console.Write((char)State.DL);
                    }
                    return;

                case 0x09: // write string
                {
                    ushort segment = _segmentOverride >= 0 ? GetSegment(_segmentOverride) : State.DS;
                    ushort offset = State.DX;
                    while (true)
                    {
                        byte ch = ReadByte(segment, offset);
                        offset = (ushort)(offset + 1);
                        if (ch == '$')
                            break;
                        _console.Write((char)ch);
                    }
                    return;
                }

                case 0x4C: // terminate program
                    ExitCode = State.AL;
                    _halted = true;
                    return;

                default:
                    return;
            }
        }

        private void HandleKeyboardService()
        {
            switch (State.AH)
            {
                case 0x00:
                case 0x01:
                    State.AL = _console.ReadKey();
                    State.Zero = false;
                    return;

                default:
                    return;
            }
        }
    }
}
