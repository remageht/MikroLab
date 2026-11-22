using System;

namespace MikroLab.Core.Cpu.I8086
{
    public sealed partial class I8086Cpu
    {
        private void ExecuteInstruction()
        {
            byte op = FetchByte();
            bool prefixed = true;
            while (prefixed)
            {
                switch (op)
                {
                    case 0x26: _segmentOverride = 0; op = FetchByte(); break;   // ES:
                    case 0x2E: _segmentOverride = 1; op = FetchByte(); break;   // CS:
                    case 0x36: _segmentOverride = 2; op = FetchByte(); break;   // SS:
                    case 0x3E: _segmentOverride = 3; op = FetchByte(); break;   // DS:
                    case 0xF0: _lock = true; op = FetchByte(); break;           // LOCK
                    case 0xF2: _repeatNotEqual = true; _repeat = false; op = FetchByte(); break;
                    case 0xF3: _repeat = true; _repeatNotEqual = false; op = FetchByte(); break;
                    default: prefixed = false; break;
                }
            }

            State.Cycles++;

            try
            {
                Dispatch(op);
            }
            finally
            {
                _segmentOverride = -1;
                _repeat = false;
                _repeatNotEqual = false;
                _lock = false;
            }
        }

        private void Dispatch(byte op)
        {
            // 00..3F: eight arithmetic and logic groups.
            if (op < 0x40)
            {
                int form = op & 7;
                if (form <= 5)
                {
                    ExecuteGroup(op >> 3, form);
                    return;
                }

                switch (op)
                {
                    case 0x06: PushSegment(0); return;
                    case 0x0E: PushSegment(1); return;
                    case 0x16: PushSegment(2); return;
                    case 0x1E: PushSegment(3); return;
                    case 0x07: PopSegment(0); return;
                    case 0x0F: PopSegment(1); return;
                    case 0x17: PopSegment(2); return;
                    case 0x1F: PopSegment(3); return;
                    case 0x27: ExecuteDaa(); return;
                    case 0x2F: ExecuteDas(); return;
                    case 0x37: ExecuteAaa(); return;
                    case 0x3F: ExecuteAas(); return;
                }
            }

            switch (op)
            {
                case 0x40: case 0x41: case 0x42: case 0x43:
                case 0x44: case 0x45: case 0x46: case 0x47:
                    IncrementRegister16((byte)(op & 7), true);
                    return;

                case 0x48: case 0x49: case 0x4A: case 0x4B:
                case 0x4C: case 0x4D: case 0x4E: case 0x4F:
                    IncrementRegister16((byte)(op & 7), false);
                    return;

                case 0x50: case 0x51: case 0x52: case 0x53:
                case 0x54: case 0x55: case 0x56: case 0x57:
                    PushWord(GetReg16(op & 7));
                    return;

                case 0x58: case 0x59: case 0x5A: case 0x5B:
                case 0x5C: case 0x5D: case 0x5E: case 0x5F:
                    SetReg16(op & 7, PopWord());
                    return;

                case 0x60: case 0x61: case 0x62: case 0x64: case 0x65: case 0x66:
                case 0x67: case 0x68: case 0x69: case 0x6A: case 0x6B:
                case 0x6C: case 0x6D: case 0x6E: case 0x6F:
                    ExecuteGroupOp6(op);
                    return;

                case 0x70: case 0x71: case 0x72: case 0x73:
                case 0x74: case 0x75: case 0x76: case 0x77:
                case 0x78: case 0x79: case 0x7A: case 0x7B:
                case 0x7C: case 0x7D: case 0x7E: case 0x7F:
                    JumpConditional((byte)(op & 0x0F));
                    return;

                case 0x80: case 0x81: case 0x82: case 0x83:
                    ExecuteImmediateGroup(op);
                    return;

                case 0x84: case 0x85:
                    ExecuteTestRm(op == 0x85);
                    return;

                case 0x86: case 0x87:
                    ExecuteXchgRm(op == 0x87);
                    return;

                case 0x88: case 0x89: case 0x8A: case 0x8B:
                    ExecuteMovRm(op);
                    return;

                case 0x8C:
                    ExecuteMovSegmentToRm();
                    return;

                case 0x8D:
                    ExecuteLea();
                    return;

                case 0x8E:
                    ExecuteMovRmToSegment();
                    return;

                case 0x8F:
                    ExecutePopRm();
                    return;

                case 0x90:
                    return;                                   // NOP

                case 0x91: case 0x92: case 0x93: case 0x94:
                case 0x95: case 0x96: case 0x97:
                    ExchangeWithAccumulator((byte)(op & 7));
                    return;

                case 0x98:
                    State.AX = (ushort)(State.AL | ((State.AL & 0x80) != 0 ? 0xFF00 : 0x0000));
                    return;                                   // CBW

                case 0x99:
                    State.DX = (ushort)((State.AX & 0x8000) != 0 ? 0xFFFF : 0x0000);
                    return;                                   // CWD

                case 0x9A:
                    ExecuteFarCall();
                    return;

                case 0x9C:
                    PushWord(State.Flags);
                    return;

                case 0x9D:
                    State.Flags = PopWord();
                    return;

                case 0x9E:
                    State.AH = (byte)((State.Flags >> 8) & 0xFF);
                    return;                                   // SAHF

                case 0x9F:
                    State.AX = (ushort)((State.AX & 0x00FF) | ((State.Flags & 0x00D5) << 8));
                    return;                                   // LAHF

                case 0xA0: case 0xA1: case 0xA2: case 0xA3:
                    ExecuteMovAccumulatorToMemory(op);
                    return;

                case 0xA4: case 0xA5:
                    ExecuteMoveString(op == 0xA5);
                    return;

                case 0xA6: case 0xA7:
                    ExecuteCompareString(op == 0xA7);
                    return;

                case 0xA8: case 0xA9:
                    ExecuteTestAccumulator(op == 0xA9);
                    return;

                case 0xAA: case 0xAB:
                    ExecuteStoreString(op == 0xAB);
                    return;

                case 0xAC: case 0xAD:
                    ExecuteLoadString(op == 0xAD);
                    return;

                case 0xAE: case 0xAF:
                    ExecuteScanString(op == 0xAF);
                    return;

                case 0xB0: case 0xB1: case 0xB2: case 0xB3:
                case 0xB4: case 0xB5: case 0xB6: case 0xB7:
                    SetReg8((byte)(op & 7), FetchByte());
                    return;

                case 0xB8: case 0xB9: case 0xBA: case 0xBB:
                case 0xBC: case 0xBD: case 0xBE: case 0xBF:
                    SetReg16((byte)(op & 7), FetchWord());
                    return;

                case 0xC0: case 0xC1: case 0xD0: case 0xD1:
                case 0xD2: case 0xD3:
                    ExecuteShiftRm(op);
                    return;

                case 0xC2:
                {
                    ushort bytes = FetchWord();
                    State.IP = (ushort)(PopWord() + bytes);
                    return;
                }

                case 0xC3:
                    State.IP = PopWord();
                    return;

                case 0xC4:
                    ExecuteLes();
                    return;

                case 0xC5:
                    ExecuteLds();
                    return;

                case 0xC6: case 0xC7:
                    ExecuteMovImmediate(op == 0xC7);
                    return;

                case 0xC8:
                {
                    ushort bytes = FetchWord();
                    byte level = FetchByte();
                    PushWord(State.BP);
                    State.BP = State.SP;
                    State.SP = (ushort)(State.SP - bytes);
                    for (int i = 0; i < level; i++)
                    {
                        PushWord((ushort)(State.BP - (i + 1) * 2));
                    }
                    return;
                }

                case 0xC9:
                    State.SP = State.BP;
                    State.BP = PopWord();
                    return;

                case 0xCA: case 0xCB:
                {
                    ushort bytes = FetchWord();
                    State.IP = PopWord();
                    State.CS = PopWord();
                    if (op == 0xCA)
                        State.IP = (ushort)(State.IP + bytes);
                    return;
                }

                case 0xCC:
                    TriggerInterrupt(3);
                    return;

                case 0xCD:
                {
                    byte vector = FetchByte();
                    TriggerInterrupt(vector);
                    return;
                }

                case 0xCE:
                    if (State.Overflow)
                        TriggerInterrupt(4);
                    return;

                case 0xCF:
                    State.IP = PopWord();
                    State.CS = PopWord();
                    State.Flags = PopWord();
                    return;

                case 0xD4:
                    State.AH = (byte)(State.AL / 10);
                    State.AL = (byte)(State.AL % 10);
                    return;                                   // AAM

                case 0xD5:
                    State.AL = (byte)(State.AL + State.AH * 10);
                    State.AH = 0;
                    return;                                   // AAD

                case 0xD6:
                    State.AL = ReadByte(State.DS, (ushort)(State.BX + State.AL));
                    return;                                   // XLAT

                case 0xD7:
                    State.AL = ReadByte(State.DS, (ushort)(State.BX + State.AL));
                    return;

                case 0xE0: case 0xE1: case 0xE2:
                    ExecuteLoop(op);
                    return;

                case 0xE3:
                {
                    sbyte rel = (sbyte)FetchByte();
                    if (State.CX == 0)
                        State.IP = (ushort)(State.IP + rel);
                    return;
                }

                case 0xE4: case 0xE5:
                    State.AL = 0;
                    return;                                   // IN AL, imm8

                case 0xE6: case 0xE7:
                    return;                                   // OUT imm8, AL

                case 0xE8:
                {
                    short rel = (short)FetchWord();
                    PushWord(State.IP);
                    State.IP = (ushort)(State.IP + rel);
                    return;
                }

                case 0xE9:
                {
                    short rel = (short)FetchWord();
                    State.IP = (ushort)(State.IP + rel);
                    return;
                }

                case 0xEA:
                    ExecuteFarJump();
                    return;

                case 0xEB:
                {
                    sbyte rel = (sbyte)FetchByte();
                    State.IP = (ushort)(State.IP + rel);
                    return;
                }

                case 0xEC: case 0xED:
                    State.AL = 0;
                    return;                                   // IN AL, DX

                case 0xEE: case 0xEF:
                    return;                                   // OUT DX, AL

                case 0xF1:
                    TriggerInterrupt(1);
                    return;

                case 0xF4:
                    _halted = true;
                    return;

                case 0xF5:
                    State.Carry = !State.Carry;
                    return;

                case 0xF6: case 0xF7:
                    ExecuteGroupF6F7(op == 0xF7);
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
                    byte value = GetRm8(m);
                    byte result = (byte)(m.Reg == 0 ? value + 1 : value - 1);
                    if (m.Reg == 0) SetIncFlags(result, value); else SetDecFlags(result, value);
                    SetRm8(m, result);
                    return;
                }

                case 0xFF:
                    ExecuteGroupFF();
                    return;

                default:
                    UnsupportedCommand?.Invoke(State.IP, op);
                    _halted = true;
                    return;
            }
        }

        // ------------------------------------------------------------------
        // arithmetic and logic groups (00..3F)
        // ------------------------------------------------------------------

        private void ExecuteGroup(int group, int form)
        {
            switch (form)
            {
                case 0:
                {
                    Modrm m = DecodeModrm();
                    byte left = GetRm8(m);
                    byte right = GetReg8(m.Reg);
                    byte result = ComputeGroup8(group, left, right);
                    if (group != 7)
                        SetRm8(m, result);
                    return;
                }
                case 1:
                {
                    Modrm m = DecodeModrm();
                    ushort left = GetRm16(m);
                    ushort right = GetReg16(m.Reg);
                    ushort result = ComputeGroup16(group, left, right);
                    if (group != 7)
                        SetRm16(m, result);
                    return;
                }
                case 2:
                {
                    Modrm m = DecodeModrm();
                    byte left = GetReg8(m.Reg);
                    byte right = GetRm8(m);
                    byte result = ComputeGroup8(group, left, right);
                    if (group != 7)
                        SetReg8(m.Reg, result);
                    return;
                }
                case 3:
                {
                    Modrm m = DecodeModrm();
                    ushort left = GetReg16(m.Reg);
                    ushort right = GetRm16(m);
                    ushort result = ComputeGroup16(group, left, right);
                    if (group != 7)
                        SetReg16(m.Reg, result);
                    return;
                }
                case 4:
                {
                    byte right = FetchByte();
                    byte left = State.AL;
                    byte result = ComputeGroup8(group, left, right);
                    if (group != 7)
                        State.AL = result;
                    return;
                }
                default:
                {
                    ushort right = FetchWord();
                    ushort left = State.AX;
                    ushort result = ComputeGroup16(group, left, right);
                    if (group != 7)
                        State.AX = result;
                    return;
                }
            }
        }

        private byte ComputeGroup8(int group, byte left, byte right)
        {
            switch (group)
            {
                case 0: return AddBytes(left, right, false);
                case 1: return LogicByte(left, right, (a, b) => (byte)(a | b));
                case 2: return AddBytes(left, right, true);
                case 3: return SubBytes(left, right, true);
                case 4: return LogicByte(left, right, (a, b) => (byte)(a & b));
                case 5: return SubBytes(left, right, false);
                case 6: return LogicByte(left, right, (a, b) => (byte)(a ^ b));
                default: return SubBytes(left, right, false);
            }
        }

        private ushort ComputeGroup16(int group, ushort left, ushort right)
        {
            switch (group)
            {
                case 0: return AddWords(left, right, false);
                case 1: return LogicWord(left, right, (a, b) => (ushort)(a | b));
                case 2: return AddWords(left, right, true);
                case 3: return SubWords(left, right, true);
                case 4: return LogicWord(left, right, (a, b) => (ushort)(a & b));
                case 5: return SubWords(left, right, false);
                case 6: return LogicWord(left, right, (a, b) => (ushort)(a ^ b));
                default: return SubWords(left, right, false);
            }
        }

        private byte AddBytes(byte left, byte right, bool withCarry)
        {
            int carry = withCarry && State.Carry ? 1 : 0;
            int sum = left + right + carry;
            byte result = (byte)sum;
            SetSizeFlags(result);
            State.Carry = sum > 0xFF;
            State.Auxiliary = ((left & 0x0F) + (right & 0x0F) + carry) > 0x0F;
            int signed = (sbyte)left + (sbyte)right + carry;
            State.Overflow = signed > 127 || signed < -128;
            return result;
        }

        private ushort AddWords(ushort left, ushort right, bool withCarry)
        {
            int carry = withCarry && State.Carry ? 1 : 0;
            int sum = left + right + carry;
            ushort result = (ushort)sum;
            State.Zero = result == 0;
            State.Sign = (result & 0x8000) != 0;
            State.Parity = ParityEven((byte)result);
            State.Carry = sum > 0xFFFF;
            State.Auxiliary = ((left & 0x0F) + (right & 0x0F) + carry) > 0x0F;
            int signed = (short)left + (short)right + carry;
            State.Overflow = signed > 32767 || signed < -32768;
            return result;
        }

        private byte SubBytes(byte left, byte right, bool withBorrow)
        {
            int borrow = withBorrow && State.Carry ? 1 : 0;
            int diff = left - right - borrow;
            byte result = (byte)diff;
            SetSizeFlags(result);
            State.Carry = diff < 0;
            State.Auxiliary = ((left & 0x0F) - (right & 0x0F) - borrow) < 0;
            int signed = (sbyte)left - (sbyte)right - borrow;
            State.Overflow = signed > 127 || signed < -128;
            return result;
        }

        private ushort SubWords(ushort left, ushort right, bool withBorrow)
        {
            int borrow = withBorrow && State.Carry ? 1 : 0;
            int diff = left - right - borrow;
            ushort result = (ushort)diff;
            State.Zero = result == 0;
            State.Sign = (result & 0x8000) != 0;
            State.Parity = ParityEven((byte)result);
            State.Carry = diff < 0;
            State.Auxiliary = ((left & 0x0F) - (right & 0x0F) - borrow) < 0;
            int signed = (short)left - (short)right - borrow;
            State.Overflow = signed > 32767 || signed < -32768;
            return result;
        }

        private byte LogicByte(byte left, byte right, Func<byte, byte, byte> op)
        {
            byte result = op(left, right);
            SetLogicFlags(result);
            return result;
        }

        private ushort LogicWord(ushort left, ushort right, Func<ushort, ushort, ushort> op)
        {
            ushort result = op(left, right);
            SetLogicFlags(result);
            return result;
        }
    }
}
