using System;

namespace MikroLab.Core.Cpu.X64
{
    public sealed partial class X64Cpu
    {
        private static readonly bool[] ConditionCodes =
        {
            true,  false, true,  false, true,  false, true,  false,
            true,  false, true,  false, true,  false, true,  false
        };

        private void ExecuteInstruction()
        {
            _instructionStart = State.RIP;
            _rexPresent = false;
            _rexW = false;
            _rexR = false;
            _rexX = false;
            _rexB = false;
            _operandSize16 = false;
            _addressSize32 = false;

            byte opcode = FetchByte();
            ReadPrefixes(ref opcode);
            State.Cycles++;

            try
            {
                Dispatch(opcode);
            }
            finally
            {
                _segmentOverride = -1;
                _repeat = false;
                _repeatNotEqual = false;
                _lock = false;
            }
        }

        private void Dispatch(byte opcode)
        {
            // 00..3F: восемь групп арифметических и логических операций
            if (opcode < 0x40)
            {
                int form = opcode & 7;
                if (form <= 5)
                {
                    ExecuteGroup(opcode >> 3, form);
                    return;
                }

                switch (opcode)
                {
                    case 0x06: PushSegment(0); return;
                    case 0x0E: PushSegment(1); return;
                    case 0x16: PushSegment(2); return;
                    case 0x17: PopSegment(2); return;
                    case 0x1E: PushSegment(3); return;
                    case 0x1F: PopSegment(3); return;
                    case 0x27: ExecuteDaa(); return;
                    case 0x2F: ExecuteDas(); return;
                    case 0x37: ExecuteAaa(); return;
                    case 0x3F: ExecuteAas(); return;
                }
            }

            switch (opcode)
            {
                case 0x40: case 0x41: case 0x42: case 0x43:
                case 0x44: case 0x45: case 0x46: case 0x47:
                {
                    int index = (opcode & 7) + (_rexB ? 8 : 0);
                    ulong value = State.GetGpr(index) & 0xFFFF;
                    ulong result = DoIncrement(value, 2);
                    State.SetGpr(index, (State.GetGpr(index) & 0xFFFFFFFFFFFF0000UL) | result);
                    return;
                }

                case 0x48: case 0x49: case 0x4A: case 0x4B:
                case 0x4C: case 0x4D: case 0x4E: case 0x4F:
                {
                    int index = (opcode & 7) + (_rexB ? 8 : 0);
                    ulong value = State.GetGpr(index) & 0xFFFF;
                    ulong result = DoDecrement(value, 2);
                    State.SetGpr(index, (State.GetGpr(index) & 0xFFFFFFFFFFFF0000UL) | result);
                    return;
                }

                case 0x50: case 0x51: case 0x52: case 0x53:
                case 0x54: case 0x55: case 0x56: case 0x57:
                    PushValue(State.GetGpr((opcode & 7) + (_rexB ? 8 : 0)));
                    return;

                case 0x58: case 0x59: case 0x5A: case 0x5B:
                case 0x5C: case 0x5D: case 0x5E: case 0x5F:
                {
                    int index = (opcode & 7) + (_rexB ? 8 : 0);
                    State.SetGpr(index, PopValue());
                    return;
                }

                case 0x63: // MOVSXD r, r/m32
                {
                    Modrm m = DecodeModrm();
                    int size = OperandSize();
                    ulong value = ExtendTo64(GetRm(m, 4), 4);
                    if (size == 2)
                        value &= 0xFFFF;
                    SetReg(m.Reg, size, value);
                    return;
                }

                case 0x68: // PUSH imm32
                    PushValue((ulong)(long)FetchInt32());
                    return;

                case 0x69: // IMUL r, r/m, imm32
                {
                    Modrm m = DecodeModrm();
                    int size = OperandSize();
                    long immediate = FetchInt32();
                    long left = SignExtend(GetRm(m, size), size);
                    long right = SignExtend(GetRm(m, size), size);
                    long result = left * right;
                    SetReg(m.Reg, size, (ulong)result & Mask(size));
                    State.Carry = result != (long)(SignExtend((ulong)result & Mask(size), size));
                    State.Overflow = State.Carry;
                    return;
                }

                case 0x6B: // IMUL r, r/m, imm8
                {
                    Modrm m = DecodeModrm();
                    int size = OperandSize();
                    long immediate = FetchInt8();
                    long left = SignExtend(GetRm(m, size), size);
                    long right = immediate;
                    long result = left * right;
                    SetReg(m.Reg, size, (ulong)result & Mask(size));
                    State.Carry = result != (long)(SignExtend((ulong)result & Mask(size), size));
                    State.Overflow = State.Carry;
                    return;
                }

                case 0x70: case 0x71: case 0x72: case 0x73:
                case 0x74: case 0x75: case 0x76: case 0x77:
                case 0x78: case 0x79: case 0x7A: case 0x7B:
                case 0x7C: case 0x7D: case 0x7E: case 0x7F:
                {
                    sbyte rel = FetchInt8();
                    if (TestCondition((byte)(opcode & 0x0F)))
                        State.RIP += (ulong)(long)rel;
                    return;
                }

                case 0x80: case 0x81: case 0x82: case 0x83:
                    ExecuteImmediateGroup(opcode);
                    return;

                case 0x84: case 0x85:
                {
                    Modrm m = DecodeModrm();
                    int size = opcode == 0x85 ? OperandSize() : 1;
                    DoTest(GetRm(m, size), GetReg(m.Reg, size), size);
                    return;
                }

                case 0x86: case 0x87:
                {
                    Modrm m = DecodeModrm();
                    int size = opcode == 0x87 ? OperandSize() : 1;
                    ulong left = GetRm(m, size);
                    ulong right = GetReg(m.Reg, size);
                    SetRm(m, size, right);
                    SetReg(m.Reg, size, left);
                    return;
                }

                case 0x88: case 0x89: case 0x8A: case 0x8B:
                {
                    Modrm m = DecodeModrm();
                    int size = (opcode == 0x88 || opcode == 0x8A) ? 1 : OperandSize();
                    if (opcode == 0x88 || opcode == 0x89)
                    {
                        SetRm(m, size, GetReg(m.Reg, size));
                    }
                    else
                    {
                        SetReg(m.Reg, size, GetRm(m, size));
                    }

                    return;
                }

                case 0x8C:
                {
                    Modrm m = DecodeModrm();
                    SetRm(m, 2, GetSegment(m.Reg));
                    return;
                }

                case 0x8D:
                {
                    Modrm m = DecodeModrm();
                    if (m.IsRegister)
                        throw new X64Fault("LEA: операнд должен быть адресом памяти");
                    SetReg(m.Reg, OperandSize(), m.Address);
                    return;
                }

                case 0x8E:
                {
                    Modrm m = DecodeModrm();
                    SetSegment(m.Reg, (ushort)GetRm(m, 2));
                    return;
                }

                case 0x8F:
                {
                    Modrm m = DecodeModrm();
                    SetRm(m, 8, PopValue());
                    return;
                }

                case 0x90:
                    if (_rexB)
                    {
                        ulong accumulator = State.RAX;
                        State.RAX = State.R8;
                        State.R8 = accumulator;
                        return;
                    }

                    return;                                    // NOP

                case 0x91: case 0x92: case 0x93: case 0x94:
                case 0x95: case 0x96: case 0x97:
                {
                    int index = (opcode & 7) + (_rexB ? 8 : 0);
                    ulong accumulator = State.RAX;
                    State.RAX = State.GetGpr(index);
                    State.SetGpr(index, accumulator);
                    return;
                }

                case 0x98:
                {
                    int size = OperandSize();
                    if (size == 2)
                        State.AX = (ushort)(SignExtend(State.AL, 1) & 0xFFFF);
                    else if (size == 4)
                        State.RAX = (ulong)(long)(int)(sbyte)State.AL;
                    else
                        State.RAX = (ulong)(long)(int)(uint)State.EAX32;
                    return;
                }

                case 0x99:
                {
                    int size = OperandSize();
                    if (size == 2)
                        State.DX = ((State.AX & 0x8000) != 0) ? (ushort)0xFFFF : (ushort)0x0000;
                    else if (size == 4)
                        State.RDX = ((State.RAX & 0x80000000UL) != 0) ? 0xFFFFFFFFUL : 0UL;
                    else
                        State.RDX = (State.RAX & 0x8000000000000000UL) != 0 ? ulong.MaxValue : 0UL;
                    return;
                }

                case 0x9C:
                    PushValue(State.Flags);
                    return;

                case 0x9D:
                    State.Flags = (ushort)PopValue();
                    return;

                case 0x9E: // SAHF — invalid in 64-bit mode
                    throw new X64Fault("SAHF недоступен в 64-разрядном режиме");

                case 0x9F: // LAHF — invalid in 64-bit mode
                    throw new X64Fault("LAHF недоступен в 64-разрядном режиме");

                case 0xA0: case 0xA1: case 0xA2: case 0xA3:
                {
                    ulong address = FetchUInt64();
                    if (opcode == 0xA0)
                        State.AL = (byte)_memory.ReadByte(address);
                    else if (opcode == 0xA1)
                        State.RAX = _memory.ReadQword(address);
                    else if (opcode == 0xA2)
                        _memory.WriteByte(address, State.AL);
                    else
                        _memory.WriteQword(address, State.RAX);
                    return;
                }

                case 0xA4: case 0xA5:
                    ExecuteMoveString(opcode == 0xA5);
                    return;

                case 0xA6: case 0xA7:
                    ExecuteCompareString(opcode == 0xA7);
                    return;

                case 0xA8: case 0xA9:
                {
                    int size = opcode == 0xA9 ? OperandSize() : 1;
                    ulong immediate = FetchImmediate(size);
                    ulong value = size == 1 ? State.AL : (State.RAX & Mask(size));
                    DoTest(value, immediate, size);
                    return;
                }

                case 0xAA: case 0xAB:
                    ExecuteStoreString(opcode == 0xAB);
                    return;

                case 0xAC: case 0xAD:
                    ExecuteLoadString(opcode == 0xAD);
                    return;

                case 0xAE: case 0xAF:
                    ExecuteScanString(opcode == 0xAF);
                    return;

                case 0xB0: case 0xB1: case 0xB2: case 0xB3:
                case 0xB4: case 0xB5: case 0xB6: case 0xB7:
                    State.SetByteRegister((opcode & 7) + (_rexB ? 8 : 0), _rexPresent, FetchByte());
                    return;

                case 0xB8: case 0xB9: case 0xBA: case 0xBB:
                case 0xBC: case 0xBD: case 0xBE: case 0xBF:
                {
                    int index = (opcode & 7) + (_rexB ? 8 : 0);
                    int size = OperandSize();
                    // в 64-разрядной форме непосредственное значение занимает
                    // все 8 байт, в остальных — 2 или 4 байта
                    ulong value = size == 8 ? FetchUInt64() : FetchImmediate(size);
                    State.SetGpr(index, size >= 8 ? value : (State.GetGpr(index) & ~Mask(size)) | (value & Mask(size)));
                    return;
                }

                case 0xC0: case 0xC1: case 0xD0: case 0xD1: case 0xD2: case 0xD3:
                    ExecuteShift(opcode);
                    return;

                case 0xC2:
                {
                    ushort bytes = FetchUInt16();
                    State.RIP = PopValue();
                    State.RIP += bytes;
                    return;
                }

                case 0xC3:
                    State.RIP = PopValue();
                    return;

                case 0xC6: case 0xC7:
                {
                    Modrm m = DecodeModrm();
                    int size = opcode == 0xC7 ? OperandSize() : 1;
                    ulong immediate = FetchImmediate(size);
                    SetRm(m, size, immediate);
                    return;
                }

                case 0xC9:
                {
                    State.RSP = State.RBP;
                    State.RBP = PopValue();
                    return;
                }

                case 0xCC:
                    TriggerSoftwareInterrupt(3);
                    return;

                case 0xCD:
                {
                    byte vector = FetchByte();
                    TriggerSoftwareInterrupt(vector);
                    return;
                }

                case 0xCF:
                    State.RIP = PopValue();
                    State.CS = (ushort)PopValue();
                    State.Flags = (ushort)PopValue();
                    return;

                case 0xD4: case 0xD5:
                    throw new X64Fault("AAM/AAD недоступны в 64-разрядном режиме");

                case 0xD6: case 0xD7:
                    throw new X64Fault("XLAT недоступен в 64-разрядном режиме");

                case 0xE0: case 0xE1: case 0xE2:
                {
                    sbyte rel = FetchInt8();
                    State.RCX = State.RCX - 1;
                    bool take = State.RCX != 0;
                    if (opcode == 0xE0)
                        take = take && !State.Zero;
                    else if (opcode == 0xE1)
                        take = take && State.Zero;
                    if (take)
                        State.RIP += (ulong)(long)rel;
                    return;
                }

                case 0xE3:
                {
                    sbyte rel = FetchInt8();
                    if (State.RCX == 0)
                        State.RIP += (ulong)(long)rel;
                    return;
                }

                case 0xE8:
                {
                    int rel = FetchInt32();
                    PushValue(State.RIP);
                    State.RIP += (ulong)(long)rel;
                    return;
                }

                case 0xE9:
                {
                    int rel = FetchInt32();
                    State.RIP += (ulong)(long)rel;
                    return;
                }

                case 0xEB:
                {
                    sbyte rel = FetchInt8();
                    State.RIP += (ulong)(long)rel;
                    return;
                }

                case 0xF4:
                    _halted = true;
                    return;

                case 0xF5:
                    State.Carry = !State.Carry;
                    return;

                case 0xF6: case 0xF7:
                    ExecuteGroupF6F7(opcode == 0xF7);
                    return;

                case 0xF8:
                    State.Carry = false;
                    return;

                case 0xF9:
                    State.Carry = true;
                    return;

                case 0xFA:
                    State.Interrupt = false;
                    return;

                case 0xFB:
                    State.Interrupt = true;
                    return;

                case 0xFC:
                    State.Direction = false;
                    return;

                case 0xFD:
                    State.Direction = true;
                    return;

                case 0xFE:
                {
                    Modrm m = DecodeModrm();
                    byte value = (byte)GetRm(m, 1);
                    byte result = m.Reg == 0 ? (byte)(value + 1) : (byte)(value - 1);
                    if (m.Reg == 0)
                    {
                        State.Zero = result == 0;
                        State.Sign = (result & 0x80) != 0;
                        State.Parity = ParityEven(result);
                        State.Auxiliary = (value & 0x0F) == 0x0F;
                        State.Overflow = result == 0x80;
                    }
                    else
                    {
                        State.Zero = result == 0;
                        State.Sign = (result & 0x80) != 0;
                        State.Parity = ParityEven(result);
                        State.Auxiliary = (value & 0x0F) == 0x00;
                        State.Overflow = result == 0x7F;
                    }

                    SetRm(m, 1, result);
                    return;
                }

                case 0xFF:
                {
                    Modrm m = DecodeModrm();
                    int size = OperandSize();
                    switch (m.Reg)
                    {
                        case 0:
                        {
                            ulong value = GetRm(m, size);
                            SetRm(m, size, DoIncrement(value, size));
                            return;
                        }
                        case 1:
                        {
                            ulong value = GetRm(m, size);
                            SetRm(m, size, DoDecrement(value, size));
                            return;
                        }
                        case 2:
                            PushValue(State.RIP);
                            State.RIP = GetRm(m, 8);
                            return;
                        case 3:
                            throw new X64Fault("вызов дальнего адреса не поддерживается");
                        case 4:
                            State.RIP = GetRm(m, 8);
                            return;
                        case 5:
                            throw new X64Fault("переход по дальнему адресу не поддерживается");
                        case 6:
                            PushValue(GetRm(m, 8));
                            return;
                        default:
                            throw new X64Fault("неизвестная команда FF /" + m.Reg);
                    }
                }

                case 0x0F:
                    ExecuteTwoByteOpcode();
                    return;

                default:
                    throw new X64Fault("неизвестный код операции " + opcode.ToString("X2") +
                        " по адресу " + _instructionStart.ToString("X16"));
            }
        }
    }
}
