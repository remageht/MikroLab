using System;

namespace MikroLab.Core.Cpu.X64
{
    public sealed partial class X64Cpu
    {
        // ------------------------------------------------------------------
        // арифметические группы
        // ------------------------------------------------------------------

        private ulong ApplyGroup(int group, ulong left, ulong right, int size)
        {
            switch (group)
            {
                case 0: return DoAdd(left, right, size, false);          // ADD
                case 1: return DoLogic(left, right, size, 1);            // OR
                case 2: return DoAdd(left, right, size, true);           // ADC
                case 3: return DoSub(left, right, size, true);           // SBB
                case 4: return DoLogic(left, right, size, 0);            // AND
                case 5: return DoSub(left, right, size, false);          // SUB
                case 6: return DoLogic(left, right, size, 2);            // XOR
                default: return DoSub(left, right, size, false);         // CMP
            }
        }

        private void ExecuteGroup(int group, int form)
        {
            int size = (form == 0 || form == 2 || form == 4) ? 1 : OperandSize();

            switch (form)
            {
                case 0:
                {
                    Modrm m = DecodeModrm();
                    ulong result = ApplyGroup(group, GetRm(m, 1), GetReg(m.Reg, 1), 1);
                    if (group != 7)
                        SetRm(m, 1, result);
                    return;
                }
                case 1:
                {
                    Modrm m = DecodeModrm();
                    ulong result = ApplyGroup(group, GetRm(m, size), GetReg(m.Reg, size), size);
                    if (group != 7)
                        SetRm(m, size, result);
                    return;
                }
                case 2:
                {
                    Modrm m = DecodeModrm();
                    ulong result = ApplyGroup(group, GetReg(m.Reg, 1), GetRm(m, 1), 1);
                    if (group != 7)
                        SetReg(m.Reg, 1, result);
                    return;
                }
                case 3:
                {
                    Modrm m = DecodeModrm();
                    ulong result = ApplyGroup(group, GetReg(m.Reg, size), GetRm(m, size), size);
                    if (group != 7)
                        SetReg(m.Reg, size, result);
                    return;
                }
                case 4:
                {
                    byte immediate = FetchByte();
                    ulong result = ApplyGroup(group, State.AL, immediate, 1);
                    if (group != 7)
                        State.AL = (byte)result;
                    return;
                }
                default:
                {
                    ulong immediate = FetchImmediate(size);
                    ulong result = ApplyGroup(group, State.RAX & Mask(size), immediate, size);
                    if (group != 7)
                        SetReg(0, size, result);
                    return;
                }
            }
        }

        private void ExecuteImmediateGroup(byte opcode)
        {
            Modrm m = DecodeModrm();
            int size = (opcode == 0x80 || opcode == 0x82) ? 1 : OperandSize();

            ulong immediate;
            if (opcode == 0x83)
                immediate = (ulong)(long)FetchInt8() & Mask(size);
            else if (size == 1)
                immediate = FetchByte();
            else
                immediate = FetchImmediate(size);

            ulong result = ApplyGroup(m.Reg, GetRm(m, size), immediate, size);
            if (m.Reg != 7)
                SetRm(m, size, result);
        }

        private ulong FetchImmediate(int size)
        {
            if (size == 1)
                return FetchByte();
            if (size == 2)
                return FetchUInt16();
            if (size == 4)
                return FetchUInt32();
            return (ulong)(long)FetchInt32();
        }

        // ------------------------------------------------------------------
        // сдвиги
        // ------------------------------------------------------------------

        private void ExecuteShift(byte opcode)
        {
            Modrm m = DecodeModrm();
            int size = (opcode == 0xC0 || opcode == 0xD0 || opcode == 0xD2) ? 1 : OperandSize();

            int count;
            if (opcode == 0xC0 || opcode == 0xC1)
                count = FetchByte();
            else if (opcode == 0xD0 || opcode == 0xD1)
                count = 1;
            else
                count = (int)(State.RCX & 0xFF);

            count &= size == 8 ? 0x3F : 0x1F;
            ulong value = GetRm(m, size);
            SetRm(m, size, DoShift(m.Reg, value, count, size));
        }

        private ulong DoShift(int type, ulong value, int count, int size)
        {
            ulong mask = Mask(size);
            ulong result = value & mask;
            if (count == 0)
                return result;

            int bits = size * 8;
            bool carry = State.Carry;

            for (int i = 0; i < count; i++)
            {
                switch (type)
                {
                    case 0: // ROL
                    {
                        bool next = (result & (1UL << (bits - 1))) != 0;
                        result = ((result << 1) | (result >> (bits - 1))) & mask;
                        carry = next;
                        break;
                    }
                    case 1: // ROR
                    {
                        bool next = (result & 1) != 0;
                        result = ((result >> 1) | (result << (bits - 1))) & mask;
                        carry = next;
                        break;
                    }
                    case 2: // RCL
                    {
                        bool next = (result & (1UL << (bits - 1))) != 0;
                        result = ((result << 1) | (carry ? 1UL : 0UL)) & mask;
                        carry = next;
                        break;
                    }
                    case 3: // RCR
                    {
                        bool next = (result & 1) != 0;
                        result = ((result >> 1) | (carry ? (1UL << (bits - 1)) : 0UL)) & mask;
                        carry = next;
                        break;
                    }
                    case 4: // SHL
                    {
                        bool next = (result & (1UL << (bits - 1))) != 0;
                        result = (result << 1) & mask;
                        carry = next;
                        break;
                    }
                    case 5: // SHR
                    {
                        bool next = (result & 1) != 0;
                        result = result >> 1;
                        carry = next;
                        break;
                    }
                    default: // SAR
                    {
                        bool next = (result & 1) != 0;
                        result = ((result >> 1) | (result & (1UL << (bits - 1)))) & mask;
                        carry = next;
                        break;
                    }
                }
            }

            State.Carry = carry;
            State.Zero = result == 0;
            State.Sign = (result & (1UL << (bits - 1))) != 0;
            State.Parity = ParityEven((byte)result);

            if (count == 1)
            {
                bool sign = (result & (1UL << (bits - 1))) != 0;
                bool nextSign = (result & (1UL << (bits - 2))) != 0;
                switch (type)
                {
                    case 0:
                    case 2:
                    case 4:
                        State.Overflow = sign != carry;
                        break;
                    case 1:
                    case 3:
                        State.Overflow = sign != nextSign;
                        break;
                    case 5:
                        State.Overflow = (value & (1UL << (bits - 1))) != 0;
                        break;
                    default:
                        State.Overflow = false;
                        break;
                }
            }

            return result;
        }

        // ------------------------------------------------------------------
        // умножение и деление
        // ------------------------------------------------------------------

        private void ExecuteGroupF6F7(bool wide)
        {
            Modrm m = DecodeModrm();
            int size = wide ? OperandSize() : 1;

            switch (m.Reg)
            {
                case 0:
                {
                    ulong immediate = wide ? FetchImmediate(size) : FetchByte();
                    DoTest(GetRm(m, size), immediate, size);
                    return;
                }
                case 2:
                    SetRm(m, size, ~GetRm(m, size) & Mask(size));
                    return;
                case 3:
                {
                    ulong value = GetRm(m, size);
                    SetRm(m, size, DoSub(0, value, size, false));
                    return;
                }
                case 4:
                    ExecuteMultiply(m, size, false);
                    return;
                case 5:
                    ExecuteMultiply(m, size, true);
                    return;
                case 6:
                    ExecuteDivide(m, size, false);
                    return;
                default:
                    ExecuteDivide(m, size, true);
                    return;
            }
        }

        private void ExecuteMultiply(Modrm m, int size, bool signedMultiply)
        {
            ulong operand = GetRm(m, size);
            UInt128 product;

            if (signedMultiply)
                product = (UInt128)((Int128)SignExtend(GetAccumulator(size), size) * (Int128)SignExtend(operand, size));
            else
                product = (UInt128)GetAccumulator(size) * (UInt128)operand;

            ulong low = (ulong)(product & (UInt128)Mask(size));
            ulong high = (ulong)(product >> (size * 8));

            switch (size)
            {
                case 1:
                    State.RAX = (low & 0xFF) | (high << 8);
                    break;
                case 2:
                    State.RAX = low & 0xFFFF;
                    State.RDX = high & 0xFFFF;
                    break;
                case 4:
                    State.RAX = low & 0xFFFFFFFF;
                    State.RDX = high & 0xFFFFFFFF;
                    break;
                default:
                    State.RAX = low;
                    State.RDX = high;
                    break;
            }

            bool overflow = high != 0;
            State.Carry = overflow;
            State.Overflow = overflow;
        }

        private ulong GetAccumulator(int size)
        {
            switch (size)
            {
                case 1: return State.AL;
                case 2: return State.AX;
                case 4: return State.EAX32;
                default: return State.RAX;
            }
        }

        private void ExecuteDivide(Modrm m, int size, bool signedDivide)
        {
            ulong divisor = GetRm(m, size);
            if (divisor == 0)
                throw new X64Fault("деление на ноль");

            ulong quotient;
            ulong remainder;

            if (size == 1)
            {
                ulong dividend = State.RAX & 0xFFFF;
                if (signedDivide)
                {
                    long d = (short)dividend;
                    long dv = (sbyte)divisor;
                    long q = d / dv;
                    if (q > 127 || q < -128)
                        throw new X64Fault("переполнение при делении");
                    quotient = (ulong)(q & 0xFF);
                    remainder = (ulong)(d % dv & 0xFF);
                }
                else
                {
                    ulong q = dividend / divisor;
                    if (q > 0xFF)
                        throw new X64Fault("переполнение при делении");
                    quotient = q;
                    remainder = dividend % divisor;
                }

                State.RAX = (remainder << 8) | quotient;
                return;
            }

            if (size == 2)
            {
                ulong dividend = (State.RDX & 0xFFFF) << 16 | (State.RAX & 0xFFFF);
                if (signedDivide)
                {
                    long d2 = (short)dividend;
                    long dv2 = (short)divisor;
                    long q2 = d2 / dv2;
                    if (q2 > 32767 || q2 < -32768)
                        throw new X64Fault("переполнение при делении");
                    quotient = (ulong)(q2 & 0xFFFF);
                    remainder = (ulong)(d2 % dv2 & 0xFFFF);
                }
                else
                {
                    ulong q = dividend / divisor;
                    if (q > 0xFFFF)
                        throw new X64Fault("переполнение при делении");
                    quotient = q;
                    remainder = dividend % divisor;
                }

                State.RAX = quotient & 0xFFFF;
                State.RDX = remainder & 0xFFFF;
                return;
            }

            if (size == 4)
            {
                ulong dividend = (State.RDX & 0xFFFFFFFF) << 32 | (State.RAX & 0xFFFFFFFF);
                if (signedDivide)
                {
                    long d4 = (int)dividend;
                    long dv4 = (int)divisor;
                    long q4 = d4 / dv4;
                    if (q4 > 2147483647L || q4 < -2147483648L)
                        throw new X64Fault("переполнение при делении");
                    quotient = (ulong)(q4 & 0xFFFFFFFF);
                    remainder = (ulong)(d4 % dv4 & 0xFFFFFFFF);
                }
                else
                {
                    ulong q = dividend / divisor;
                    if (q > 0xFFFFFFFF)
                        throw new X64Fault("переполнение при делении");
                    quotient = q;
                    remainder = dividend % divisor;
                }

                State.RAX = quotient & 0xFFFFFFFF;
                State.RDX = remainder & 0xFFFFFFFF;
                return;
            }

            if (signedDivide)
            {
                Int128 bigDividend = ((Int128)(long)State.RDX << 64) | (long)State.RAX;
                Int128 bigDivisor = (long)divisor;
                Int128 bigQuotient = bigDividend / bigDivisor;
                if (bigQuotient > long.MaxValue || bigQuotient < long.MinValue)
                    throw new X64Fault("переполнение при делении");
                State.RAX = (ulong)(long)bigQuotient;
                State.RDX = (ulong)(long)(bigDividend % bigDivisor);
                return;
            }

            UInt128 wideDividend = ((UInt128)State.RDX << 64) | State.RAX;
            UInt128 wideQuotient = wideDividend / divisor;
            State.RAX = (ulong)wideQuotient;
            State.RDX = (ulong)(wideDividend % divisor);
        }

        // ------------------------------------------------------------------
        // строковые операции
        // ------------------------------------------------------------------

        private ulong AdvanceIndex(ulong index, bool word)
        {
            int step = word ? 2 : 1;
            return State.Direction ? index - (ulong)step : index + (ulong)step;
        }

        private void ExecuteMoveString(bool word)
        {
            bool repeat = _repeat || _repeatNotEqual;
            do
            {
                if (word)
                    _memory.WriteWord(State.RDI, _memory.ReadWord(State.RSI));
                else
                    _memory.WriteByte(State.RDI, _memory.ReadByte(State.RSI));

                State.RSI = AdvanceIndex(State.RSI, word);
                State.RDI = AdvanceIndex(State.RDI, word);
                State.RCX--;
            }
            while (repeat && State.RCX != 0);
        }

        private void ExecuteCompareString(bool word)
        {
            bool repeat = _repeat || _repeatNotEqual;
            do
            {
                if (word)
                {
                    ushort left = _memory.ReadWord(State.RSI);
                    ushort right = _memory.ReadWord(State.RDI);
                    DoSub(left, right, 2, false);
                }
                else
                {
                    byte left = _memory.ReadByte(State.RSI);
                    byte right = _memory.ReadByte(State.RDI);
                    DoSub(left, right, 1, false);
                }

                State.RSI = AdvanceIndex(State.RSI, word);
                State.RDI = AdvanceIndex(State.RDI, word);
                State.RCX--;
            }
            while (repeat && State.RCX != 0 && (_repeat ? State.Zero : !State.Zero));
        }

        private void ExecuteStoreString(bool word)
        {
            bool repeat = _repeat || _repeatNotEqual;
            do
            {
                if (word)
                    _memory.WriteWord(State.RDI, State.AX);
                else
                    _memory.WriteByte(State.RDI, State.AL);

                State.RDI = AdvanceIndex(State.RDI, word);
                State.RCX--;
            }
            while (repeat && State.RCX != 0);
        }

        private void ExecuteLoadString(bool word)
        {
            bool repeat = _repeat || _repeatNotEqual;
            do
            {
                if (word)
                    State.RAX = (State.RAX & 0xFFFF000000000000UL) | _memory.ReadWord(State.RSI);
                else
                    State.RAX = (State.RAX & 0xFFFFFFFFFFFFFF00UL) | _memory.ReadByte(State.RSI);

                State.RSI = AdvanceIndex(State.RSI, word);
                State.RCX--;
            }
            while (repeat && State.RCX != 0);
        }

        private void ExecuteScanString(bool word)
        {
            bool repeat = _repeat || _repeatNotEqual;
            do
            {
                if (word)
                    DoSub((ulong)(State.AX & 0xFFFF), (ulong)_memory.ReadWord(State.RDI), 2, false);
                else
                    DoSub((ulong)State.AL, (ulong)_memory.ReadByte(State.RDI), 1, false);

                State.RDI = AdvanceIndex(State.RDI, word);
                State.RCX--;
            }
            while (repeat && State.RCX != 0 && (_repeat ? State.Zero : !State.Zero));
        }

        // ------------------------------------------------------------------
        // условия, стек, сегменты
        // ------------------------------------------------------------------

        private bool TestCondition(byte condition)
        {
            switch (condition)
            {
                case 0x0: return State.Overflow;
                case 0x1: return !State.Overflow;
                case 0x2: return State.Carry;
                case 0x3: return !State.Carry;
                case 0x4: return State.Zero;
                case 0x5: return !State.Zero;
                case 0x6: return State.Carry || State.Zero;
                case 0x7: return !State.Carry && !State.Zero;
                case 0x8: return State.Sign;
                case 0x9: return !State.Sign;
                case 0xA: return State.Parity;
                case 0xB: return !State.Parity;
                case 0xC: return State.Sign != State.Overflow;
                case 0xD: return State.Sign == State.Overflow;
                case 0xE: return State.Zero || State.Sign != State.Overflow;
                default: return !State.Zero && State.Sign == State.Overflow;
            }
        }

        private void PushValue(ulong value)
        {
            State.RSP -= 8;
            _memory.WriteQword(State.RSP, value);
        }

        private ulong PopValue()
        {
            ulong value = _memory.ReadQword(State.RSP);
            State.RSP += 8;
            return value;
        }

        private ushort GetSegment(int index)
        {
            switch (index)
            {
                case 0: return State.ES;
                case 1: return State.CS;
                case 2: return State.SS;
                case 3: return State.DS;
                case 4: return State.FS;
                default: return State.GS;
            }
        }

        private void SetSegment(int index, ushort value)
        {
            switch (index)
            {
                case 0: State.ES = value; break;
                case 1: State.CS = value; break;
                case 2: State.SS = value; break;
                case 3: State.DS = value; break;
                case 4: State.FS = value; break;
                default: State.GS = value; break;
            }
        }

        private void PushSegment(int index)
        {
            PushValue(GetSegment(index));
        }

        private void PopSegment(int index)
        {
            SetSegment(index, (ushort)PopValue());
        }

        // ------------------------------------------------------------------
        // десятичная коррекция
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
            SetSizeFlags(al, 1);
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
            SetSizeFlags(al, 1);
        }

        private void ExecuteAaa()
        {
            if ((State.AL & 0x0F) > 9 || State.Auxiliary)
            {
                State.RAX += 0x0106;
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
                State.RAX -= 0x0106;
                State.Auxiliary = true;
            }
            else
            {
                State.Auxiliary = false;
            }

            State.AL &= 0x0F;
        }

        // ------------------------------------------------------------------
        // прерывания и сервисы DOS
        // ------------------------------------------------------------------

        private void TriggerSoftwareInterrupt(byte vector)
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

            Interrupt?.Invoke(vector);
            throw new X64Fault("прерывание " + vector + " не обрабатывается эмулятором");
        }

        private void HandleDosService()
        {
            switch (State.RAH)
            {
                case 0x01:
                    State.AL = _console.ReadKey();
                    _console.Write((char)State.AL);
                    return;
                case 0x02:
                    _console.Write((char)State.DL);
                    return;
                case 0x09:
                {
                    ulong offset = State.RDX;
                    while (true)
                    {
                        byte ch = _memory.ReadByte(offset);
                        offset++;
                        if (ch == '$')
                            break;
                        _console.Write((char)ch);
                    }

                    return;
                }
                case 0x4C:
                    ExitCode = (int)State.AL;
                    _halted = true;
                    return;
                default:
                    return;
            }
        }

        private void HandleKeyboardService()
        {
            if (State.RAH == 0x00 || State.RAH == 0x01)
            {
                State.AL = _console.ReadKey();
                State.Zero = false;
            }
        }

        // ------------------------------------------------------------------
        // двухбайтовые команды (0F)
        // ------------------------------------------------------------------

        private void ExecuteTwoByteOpcode()
        {
            byte opcode = FetchByte();

            switch (opcode)
            {
                case 0x05:
                    throw new X64Fault("SYSCALL не поддерживается");
                case 0x0B:
                    throw new X64Fault("UD2 — недопустимая команда");
                case 0x1F:
                    DecodeModrm();
                    return;                                 // NOP с ModR/M
                case 0x31: // RDTSC
                    State.RAX = State.Cycles & 0xFFFFFFFF;
                    State.RDX = State.Cycles >> 32;
                    return;
                case 0xA2:
                    ExecuteCpuid();
                    return;
                case 0xB6:
                case 0xB7:
                case 0xBE:
                case 0xBF:
                {
                    Modrm m = DecodeModrm();
                    int sourceSize = (opcode == 0xB6 || opcode == 0xBE) ? 1 : 2;
                    int destinationSize = OperandSize();
                    ulong value = GetRm(m, sourceSize);
                    if (opcode == 0xBE || opcode == 0xBF)
                        value = ExtendTo64(value, sourceSize);
                    SetReg(m.Reg, destinationSize, value);
                    return;
                }
                case 0xAF:
                {
                    Modrm m = DecodeModrm();
                    int size = OperandSize();
                    long left = SignExtend(GetReg(m.Reg, size), size);
                    long right = SignExtend(GetRm(m, size), size);
                    long result = left * right;
                    SetReg(m.Reg, size, (ulong)result & Mask(size));
                    State.Carry = result != (long)SignExtend((ulong)result & Mask(size), size);
                    State.Overflow = State.Carry;
                    return;
                }
            }

            if (opcode >= 0x40 && opcode <= 0x4F) // CMOVcc
            {
                Modrm m = DecodeModrm();
                int size = OperandSize();
                if (TestCondition((byte)(opcode & 0x0F)))
                    SetReg(m.Reg, size, GetRm(m, size));
                return;
            }

            if (opcode >= 0x80 && opcode <= 0x8F) // Jcc rel32
            {
                int rel = FetchInt32();
                if (TestCondition((byte)(opcode & 0x0F)))
                    State.RIP += (ulong)(long)rel;
                return;
            }

            if (opcode >= 0x90 && opcode <= 0x9F) // SETcc
            {
                Modrm m = DecodeModrm();
                SetRm(m, 1, TestCondition((byte)(opcode & 0x0F)) ? 1UL : 0UL);
                return;
            }

            throw new X64Fault("неизвестный код операции 0F " + opcode.ToString("X2") +
                " по адресу " + _instructionStart.ToString("X16"));
        }

        private void ExecuteCpuid()
        {
            uint leaf = (uint)State.RAX;

            if (leaf == 0)
            {
                State.RAX = 1;
                State.RBX = 0x756E654D;    // "Menu"
                State.RDX = 0x49656E69;    // "ineI"
                State.RCX = 0x6C65746B;    // "ktel"
                return;
            }

            if (leaf == 1)
            {
                State.RAX = 0x000306A9;    // семейство 6, модель 58
                State.RBX = 0x00000800;
                State.RCX = 0x7FFAFBBF;
                State.RDX = 0xBFEBFBFF;
                return;
            }

            State.RAX = 0;
            State.RBX = 0;
            State.RCX = 0;
            State.RDX = 0;
        }
    }
}
