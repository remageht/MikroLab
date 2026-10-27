using System;
using System.Collections.Generic;
using MikroLab.Core.Devices;
using MikroLab.Core.Memory;

namespace MikroLab.Core.Cpu
{
    /// <summary>
    /// Complete Intel 8080 / KR580VM80A CPU with a real hardware stack and
    /// documented flag behavior. Memory and I/O devices are reached through
    /// buses, so the CPU contains no board-specific port addresses.
    /// </summary>
    public sealed class ExtendedCpu : ICpu
    {
        private enum ExecuteResult
        {
            Continue,
            Halt,
            Unsupported
        }

        private static readonly bool[] EvenParity = BuildParityTable();
        private static readonly byte[] Seg7Codes =
        {
            64, 6, 91, 79, 102, 109, 125, 7, 128, 111, 119, 124, 57, 94, 121, 113
        };

        private readonly IMemory _memory;
        private readonly IIoBus _ioBus;
        private readonly ISound _sound;
        private readonly IClock _clock;
        private readonly IDiagnostics _diagnostics;
        private readonly Stack<ushort> _callStack = new Stack<ushort>();

        private bool _halted;

        public CpuState State { get; }
        public CpuProfile Profile => CpuProfile.Extended;
        public int CallStackDepth => _callStack.Count;
        public bool IsHalted => _halted;

        public ExtendedCpu(
            IMemory memory,
            IIoBus ioBus,
            ISound sound = null,
            IClock clock = null,
            IDiagnostics diagnostics = null)
        {
            _memory = memory ?? throw new ArgumentNullException(nameof(memory));
            _ioBus = ioBus ?? throw new ArgumentNullException(nameof(ioBus));
            _sound = sound;
            _clock = clock;
            _diagnostics = diagnostics;

            State = new CpuState(_memory);
        }

        public IReadOnlyList<ushort> GetCallStack()
        {
            return _callStack.ToArray();
        }

        /// <summary>
        /// Restores the debug call stack and the HLT latch. The stack is given
        /// top first, matching <see cref="GetCallStack"/>.
        /// </summary>
        public void RestoreDebugState(IReadOnlyList<ushort> callStack, bool halted)
        {
            _callStack.Clear();
            if (callStack != null)
            {
                for (int i = callStack.Count - 1; i >= 0; i--)
                {
                    _callStack.Push(callStack[i]);
                }
            }
            _halted = halted;
        }

        public void Reset()
        {
            _halted = false;
            _callStack.Clear();
            State.Flags.Reset();
            State.PC = MemoryMap.UserMemStart;
            State.SP = 0x83FE;
            State.Cycles = 0;
        }

        public bool Step()
        {
            if (_halted)
                return false;

            ushort pc = State.PC;
            byte opcode = ReadMemory(pc);
            ExecuteResult result = Execute(opcode, pc);

            if (result == ExecuteResult.Unsupported)
            {
                _diagnostics?.ReportUnsupported(pc, opcode);
                return false;
            }

            if (result == ExecuteResult.Halt)
                _halted = true;

            State.Cycles++;
            return result == ExecuteResult.Continue;
        }

        private ExecuteResult Execute(int opcode, ushort pc)
        {
            switch (opcode)
            {
                // NOP plus undocumented NOPs.
                case 0x00:
                case 0x08:
                case 0x10:
                case 0x18:
                case 0x20:
                case 0x28:
                case 0x30:
                case 0x38:
                    State.PC = (ushort)(pc + 1);
                    return ExecuteResult.Continue;

                // LXI rp,d16
                case 0x01:
                case 0x11:
                case 0x21:
                case 0x31:
                    WritePair(opcode >> 4, ReadWord(pc + 1));
                    State.PC = (ushort)(pc + 3);
                    return ExecuteResult.Continue;

                // STAX B/D
                case 0x02:
                case 0x12:
                    WriteMemory(ReadPair(opcode >> 4), State.A);
                    State.PC = (ushort)(pc + 1);
                    return ExecuteResult.Continue;

                // INX rp
                case 0x03:
                case 0x13:
                case 0x23:
                case 0x33:
                {
                    int pair = opcode >> 4;
                    WritePair(pair, (ushort)(ReadPair(pair) + 1));
                    State.PC = (ushort)(pc + 1);
                    return ExecuteResult.Continue;
                }

                // INR r
                case 0x04:
                case 0x0C:
                case 0x14:
                case 0x1C:
                case 0x24:
                case 0x2C:
                case 0x34:
                case 0x3C:
                    Increment((opcode >> 3) & 0x07);
                    State.PC = (ushort)(pc + 1);
                    return ExecuteResult.Continue;

                // DCR r
                case 0x05:
                case 0x0D:
                case 0x15:
                case 0x1D:
                case 0x25:
                case 0x2D:
                case 0x35:
                case 0x3D:
                    Decrement((opcode >> 3) & 0x07);
                    State.PC = (ushort)(pc + 1);
                    return ExecuteResult.Continue;

                // MVI r,d8
                case 0x06:
                case 0x0E:
                case 0x16:
                case 0x1E:
                case 0x26:
                case 0x2E:
                case 0x36:
                case 0x3E:
                    WriteReg((opcode >> 3) & 0x07, ReadMemory(pc + 1));
                    State.PC = (ushort)(pc + 2);
                    return ExecuteResult.Continue;

                // Rotates, decimal adjust and flag operations.
                case 0x07:
                    RotateLeftCircular();
                    State.PC = (ushort)(pc + 1);
                    return ExecuteResult.Continue;
                case 0x0F:
                    RotateRightCircular();
                    State.PC = (ushort)(pc + 1);
                    return ExecuteResult.Continue;
                case 0x17:
                    RotateLeftThroughCarry();
                    State.PC = (ushort)(pc + 1);
                    return ExecuteResult.Continue;
                case 0x1F:
                    RotateRightThroughCarry();
                    State.PC = (ushort)(pc + 1);
                    return ExecuteResult.Continue;
                case 0x27:
                    DecimalAdjust();
                    State.PC = (ushort)(pc + 1);
                    return ExecuteResult.Continue;
                case 0x2F:
                    State.A = (byte)~State.A;
                    State.PC = (ushort)(pc + 1);
                    return ExecuteResult.Continue;
                case 0x37:
                    State.Flags.C = true;
                    State.PC = (ushort)(pc + 1);
                    return ExecuteResult.Continue;
                case 0x3F:
                    State.Flags.C = !State.Flags.C;
                    State.PC = (ushort)(pc + 1);
                    return ExecuteResult.Continue;

                // DAD rp
                case 0x09:
                case 0x19:
                case 0x29:
                case 0x39:
                {
                    int pair = opcode >> 4;
                    int sum = ReadPair(2) + ReadPair(pair);
                    State.Flags.C = sum > 0xFFFF;
                    WritePair(2, (ushort)sum);
                    State.PC = (ushort)(pc + 1);
                    return ExecuteResult.Continue;
                }

                // LDAX B/D
                case 0x0A:
                case 0x1A:
                    State.A = ReadMemory(ReadPair(opcode >> 4));
                    State.PC = (ushort)(pc + 1);
                    return ExecuteResult.Continue;

                // DCX rp
                case 0x0B:
                case 0x1B:
                case 0x2B:
                case 0x3B:
                {
                    int pair = opcode >> 4;
                    WritePair(pair, (ushort)(ReadPair(pair) - 1));
                    State.PC = (ushort)(pc + 1);
                    return ExecuteResult.Continue;
                }

                // SHLD a16
                case 0x22:
                {
                    ushort address = ReadWord(pc + 1);
                    WriteMemory(address, State.L);
                    WriteMemory(address + 1, State.H);
                    State.PC = (ushort)(pc + 3);
                    return ExecuteResult.Continue;
                }

                // LHLD a16
                case 0x2A:
                {
                    ushort address = ReadWord(pc + 1);
                    State.L = ReadMemory(address);
                    State.H = ReadMemory(address + 1);
                    State.PC = (ushort)(pc + 3);
                    return ExecuteResult.Continue;
                }

                // STA a16
                case 0x32:
                    WriteMemory(ReadWord(pc + 1), State.A);
                    State.PC = (ushort)(pc + 3);
                    return ExecuteResult.Continue;

                // LDA a16
                case 0x3A:
                    State.A = ReadMemory(ReadWord(pc + 1));
                    State.PC = (ushort)(pc + 3);
                    return ExecuteResult.Continue;

                // MOV r,r / HLT
                case >= 0x40 and <= 0x7F:
                    if (opcode == 0x76)
                        return ExecuteResult.Halt;

                    WriteReg((opcode >> 3) & 0x07, ReadReg(opcode & 0x07));
                    State.PC = (ushort)(pc + 1);
                    return ExecuteResult.Continue;

                // ADD/ADC/SUB/SBB/ANA/XRA/ORA/CMP r
                case >= 0x80 and <= 0xBF:
                    ExecuteAlu((opcode >> 3) & 0x07, ReadReg(opcode & 0x07));
                    State.PC = (ushort)(pc + 1);
                    return ExecuteResult.Continue;

                // Conditional returns.
                case 0xC0:
                case 0xC8:
                case 0xD0:
                case 0xD8:
                case 0xE0:
                case 0xE8:
                case 0xF0:
                case 0xF8:
                    if (TestCondition((opcode >> 3) & 0x07))
                        Return();
                    else
                        State.PC = (ushort)(pc + 1);
                    return ExecuteResult.Continue;

                // POP rp / POP PSW
                case 0xC1:
                case 0xD1:
                case 0xE1:
                case 0xF1:
                    PopPair((opcode >> 4) & 0x03);
                    State.PC = (ushort)(pc + 1);
                    return ExecuteResult.Continue;

                // Conditional jumps.
                case 0xC2:
                case 0xCA:
                case 0xD2:
                case 0xDA:
                case 0xE2:
                case 0xEA:
                case 0xF2:
                case 0xFA:
                {
                    ushort target = ReadWord(pc + 1);
                    if (TestCondition((opcode >> 3) & 0x07))
                        State.PC = target;
                    else
                        State.PC = (ushort)(pc + 3);
                    return ExecuteResult.Continue;
                }

                // JMP a16 (0xCB is an undocumented duplicate)
                case 0xC3:
                case 0xCB:
                    State.PC = ReadWord(pc + 1);
                    return ExecuteResult.Continue;

                // Conditional calls.
                case 0xC4:
                case 0xCC:
                case 0xD4:
                case 0xDC:
                case 0xE4:
                case 0xEC:
                case 0xF4:
                case 0xFC:
                {
                    ushort target = ReadWord(pc + 1);
                    if (TestCondition((opcode >> 3) & 0x07))
                        Call(target, pc);
                    else
                        State.PC = (ushort)(pc + 3);
                    return ExecuteResult.Continue;
                }

                // PUSH rp / PUSH PSW
                case 0xC5:
                case 0xD5:
                case 0xE5:
                case 0xF5:
                    PushPair((opcode >> 4) & 0x03);
                    State.PC = (ushort)(pc + 1);
                    return ExecuteResult.Continue;

                // Immediate ALU operations.
                case 0xC6:
                case 0xCE:
                case 0xD6:
                case 0xDE:
                case 0xE6:
                case 0xEE:
                case 0xF6:
                case 0xFE:
                    ExecuteAlu((opcode >> 3) & 0x07, ReadMemory(pc + 1));
                    State.PC = (ushort)(pc + 2);
                    return ExecuteResult.Continue;

                // RST n
                case 0xC7:
                case 0xCF:
                case 0xD7:
                case 0xDF:
                case 0xE7:
                case 0xEF:
                case 0xF7:
                case 0xFF:
                {
                    ushort returnAddress = (ushort)(pc + 1);
                    PushWord(returnAddress);
                    _callStack.Push(returnAddress);
                    State.PC = (ushort)(opcode & 0x38);
                    return ExecuteResult.Continue;
                }

                // RET (0xD9 is an undocumented duplicate)
                case 0xC9:
                case 0xD9:
                    Return();
                    return ExecuteResult.Continue;

                // CALL a16 (0xDD, 0xED and 0xFD are undocumented duplicates)
                case 0xCD:
                case 0xDD:
                case 0xED:
                case 0xFD:
                    Call(ReadWord(pc + 1), pc);
                    return ExecuteResult.Continue;

                // OUT d8
                case 0xD3:
                {
                    byte port = ReadMemory(pc + 1);
                    _ioBus.WritePort(port, State.A);
                    State.PC = (ushort)(pc + 2);
                    return ExecuteResult.Continue;
                }

                // IN d8
                case 0xDB:
                    State.A = _ioBus.ReadPort(ReadMemory(pc + 1));
                    State.PC = (ushort)(pc + 2);
                    return ExecuteResult.Continue;

                // XTHL
                case 0xE3:
                {
                    ushort sp = State.SP;
                    byte low = ReadMemory(sp);
                    byte high = ReadMemory(sp + 1);
                    WriteMemory(sp, State.L);
                    WriteMemory(sp + 1, State.H);
                    State.L = low;
                    State.H = high;
                    State.PC = (ushort)(pc + 1);
                    return ExecuteResult.Continue;
                }

                // PCHL
                case 0xE9:
                    State.PC = GetHL();
                    return ExecuteResult.Continue;

                // XCHG
                case 0xEB:
                {
                    ushort de = GetDE();
                    WritePair(1, GetHL());
                    WritePair(2, de);
                    State.PC = (ushort)(pc + 1);
                    return ExecuteResult.Continue;
                }

                // DI / EI are no-ops because there is no interrupt controller.
                case 0xF3:
                case 0xFB:
                    State.PC = (ushort)(pc + 1);
                    return ExecuteResult.Continue;

                // SPHL
                case 0xF9:
                    State.SP = GetHL();
                    State.PC = (ushort)(pc + 1);
                    return ExecuteResult.Continue;

                default:
                    return ExecuteResult.Unsupported;
            }
        }

        private static bool[] BuildParityTable()
        {
            bool[] table = new bool[256];
            for (int value = 0; value < 256; value++)
            {
                int bits = 0;
                int rest = value;
                while (rest != 0)
                {
                    bits += rest & 1;
                    rest >>= 1;
                }
                table[value] = (bits & 1) == 0;
            }
            return table;
        }

        private byte ReadReg(int reg)
        {
            switch (reg)
            {
                case 0: return State.B;
                case 1: return State.C;
                case 2: return State.D;
                case 3: return State.E;
                case 4: return State.H;
                case 5: return State.L;
                case 6: return ReadMemory(GetHL());
                case 7: return State.A;
                default: return 0;
            }
        }

        private void WriteReg(int reg, byte value)
        {
            switch (reg)
            {
                case 0: State.B = value; break;
                case 1: State.C = value; break;
                case 2: State.D = value; break;
                case 3: State.E = value; break;
                case 4: State.H = value; break;
                case 5: State.L = value; break;
                case 6: WriteMemory(GetHL(), value); break;
                case 7: State.A = value; break;
            }
        }

        private ushort GetHL()
        {
            return (ushort)((State.H << 8) | State.L);
        }

        private ushort GetDE()
        {
            return (ushort)((State.D << 8) | State.E);
        }

        private ushort ReadPair(int pair)
        {
            switch (pair)
            {
                case 0: return (ushort)((State.B << 8) | State.C);
                case 1: return GetDE();
                case 2: return GetHL();
                case 3: return State.SP;
                default: return 0;
            }
        }

        private void WritePair(int pair, ushort value)
        {
            byte high = (byte)(value >> 8);
            byte low = (byte)(value & 0xFF);
            switch (pair)
            {
                case 0:
                    State.B = high;
                    State.C = low;
                    break;
                case 1:
                    State.D = high;
                    State.E = low;
                    break;
                case 2:
                    State.H = high;
                    State.L = low;
                    break;
                case 3:
                    State.SP = value;
                    break;
            }
        }

        private byte ReadMemory(int address)
        {
            return _memory.ReadByte((ushort)(address & 0xFFFF));
        }

        private void WriteMemory(int address, byte value)
        {
            _memory.WriteByte((ushort)(address & 0xFFFF), value);
        }

        private ushort ReadWord(int address)
        {
            int low = ReadMemory(address);
            int high = ReadMemory(address + 1);
            return (ushort)(low | (high << 8));
        }

        private void PushWord(ushort value)
        {
            ushort sp = State.SP;
            WriteMemory(sp - 1, (byte)(value >> 8));
            WriteMemory(sp - 2, (byte)(value & 0xFF));
            State.SP = (ushort)(sp - 2);
        }

        private ushort PopWord()
        {
            ushort sp = State.SP;
            byte low = ReadMemory(sp);
            byte high = ReadMemory(sp + 1);
            State.SP = (ushort)(sp + 2);
            return (ushort)(low | (high << 8));
        }

        private void PushPair(int pair)
        {
            if (pair == 3)
            {
                ushort value = (ushort)((State.A << 8) | State.Flags.ToByte());
                PushWord(value);
            }
            else
            {
                PushWord(ReadPair(pair));
            }
        }

        private void PopPair(int pair)
        {
            ushort value = PopWord();
            if (pair == 3)
            {
                State.A = (byte)(value >> 8);
                FlagReg flags = State.Flags;
                flags.FromByte((byte)(value & 0xFF));
                State.Flags = flags;
            }
            else
            {
                WritePair(pair, value);
            }
        }

        private void Call(ushort target, ushort pc)
        {
            ushort returnAddress = (ushort)(pc + 3);

            if (target == MemoryMap.MonitorSeggg)
            {
                Seggg();
                State.PC = returnAddress;
                return;
            }
            if (target == MemoryMap.MonitorSound)
            {
                if (_sound != null)
                    _sound.PlayTestTone();
                State.PC = returnAddress;
                return;
            }
            if (target == MemoryMap.MonitorDelay)
            {
                Delay();
                State.PC = returnAddress;
                return;
            }

            PushWord(returnAddress);
            _callStack.Push(returnAddress);
            State.PC = target;
        }

        private void Return()
        {
            State.PC = PopWord();
            if (_callStack.Count > 0)
                _callStack.Pop();
        }

        private void Seggg()
        {
            for (int i = 0; i < 4; i++)
            {
                byte value = ReadMemory(MemoryMap.SegggInputStart + i);
                int high = (value >> 4) & 0x0F;
                int low = value & 0x0F;
                WriteMemory(MemoryMap.DisplayLatchStart + i * 2, Seg7Codes[high]);
                WriteMemory(MemoryMap.DisplayLatchStart + i * 2 + 1, Seg7Codes[low]);
            }
        }

        private void Delay()
        {
            int s1, l1, s2, l2;
            _memory.GetNibbles(0x8000, out s1, out l1);
            _memory.GetNibbles(0x8001, out s2, out l2);
            int milliseconds = (s1 << 12) | (l1 << 8) | (s2 << 4) | l2;
            if (_clock != null)
                _clock.Delay(milliseconds);
        }

        private void Increment(int reg)
        {
            byte value = ReadReg(reg);
            byte result = (byte)(value + 1);
            State.Flags.AC = (value & 0x0F) == 0x0F;
            WriteReg(reg, result);
            SetSZP(result);
        }

        private void Decrement(int reg)
        {
            byte value = ReadReg(reg);
            byte result = (byte)(value - 1);
            State.Flags.AC = (value & 0x0F) == 0x00;
            WriteReg(reg, result);
            SetSZP(result);
        }

        private void ExecuteAlu(int operation, byte value)
        {
            switch (operation)
            {
                case 0:
                    Add(value, false);
                    break;
                case 1:
                    Add(value, true);
                    break;
                case 2:
                    Subtract(value, false, true);
                    break;
                case 3:
                    Subtract(value, true, true);
                    break;
                case 4:
                    State.A = (byte)(State.A & value);
                    SetLogicalFlags(State.A);
                    break;
                case 5:
                    State.A = (byte)(State.A ^ value);
                    SetLogicalFlags(State.A);
                    break;
                case 6:
                    State.A = (byte)(State.A | value);
                    SetLogicalFlags(State.A);
                    break;
                case 7:
                    Subtract(value, false, false);
                    break;
            }
        }

        private void Add(byte value, bool withCarry)
        {
            int carry = withCarry && State.Flags.C ? 1 : 0;
            int a = State.A;
            int sum = a + value + carry;
            byte result = (byte)sum;

            State.A = result;
            State.Flags.C = sum > 0xFF;
            State.Flags.AC = ((a & 0x0F) + (value & 0x0F) + carry) > 0x0F;
            SetSZP(result);
        }

        private void Subtract(byte value, bool withBorrow, bool storeResult)
        {
            int borrow = withBorrow && State.Flags.C ? 1 : 0;
            int a = State.A;
            int difference = a - value - borrow;
            byte result = (byte)difference;

            State.Flags.C = difference < 0;
            State.Flags.AC = ((a ^ value ^ result) & 0x10) != 0;
            if (storeResult)
                State.A = result;
            SetSZP(result);
        }

        private void SetLogicalFlags(byte result)
        {
            State.Flags.C = false;
            State.Flags.AC = false;
            SetSZP(result);
        }

        private void SetSZP(byte result)
        {
            State.Flags.S = (result & 0x80) != 0;
            State.Flags.Z = result == 0;
            State.Flags.P = EvenParity[result];
        }

        private void RotateLeftCircular()
        {
            bool carry = (State.A & 0x80) != 0;
            State.A = (byte)((State.A << 1) | (carry ? 1 : 0));
            State.Flags.C = carry;
        }

        private void RotateRightCircular()
        {
            bool carry = (State.A & 0x01) != 0;
            State.A = (byte)((State.A >> 1) | (carry ? 0x80 : 0));
            State.Flags.C = carry;
        }

        private void RotateLeftThroughCarry()
        {
            bool carry = (State.A & 0x80) != 0;
            State.A = (byte)((State.A << 1) | (State.Flags.C ? 1 : 0));
            State.Flags.C = carry;
        }

        private void RotateRightThroughCarry()
        {
            bool carry = (State.A & 0x01) != 0;
            State.A = (byte)((State.A >> 1) | (State.Flags.C ? 0x80 : 0));
            State.Flags.C = carry;
        }

        private void DecimalAdjust()
        {
            byte a = State.A;
            bool carry = State.Flags.C;
            int correction = 0;

            if (State.Flags.AC || (a & 0x0F) > 0x09)
                correction |= 0x06;

            if (State.Flags.C || a > 0x99)
            {
                correction |= 0x60;
                carry = true;
            }

            int sum = a + correction;
            byte result = (byte)sum;

            State.A = result;
            State.Flags.C = carry || sum > 0xFF;
            State.Flags.AC = ((a ^ result ^ correction) & 0x10) != 0;
            SetSZP(result);
        }

        private bool TestCondition(int condition)
        {
            switch (condition)
            {
                case 0: return !State.Flags.Z;
                case 1: return State.Flags.Z;
                case 2: return !State.Flags.C;
                case 3: return State.Flags.C;
                case 4: return !State.Flags.P;
                case 5: return State.Flags.P;
                case 6: return !State.Flags.S;
                case 7: return State.Flags.S;
                default: return false;
            }
        }
    }
}
