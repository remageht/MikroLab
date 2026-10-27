using System;
using System.Collections.Generic;
using MikroLab.Core.Devices;
using MikroLab.Core.Memory;

namespace MikroLab.Core.Cpu
{
    public class LegacyCpu : ICpu
    {
        public const int REG_B = 0;
        public const int REG_C = 1;
        public const int REG_D = 2;
        public const int REG_E = 3;
        public const int REG_H = 4;
        public const int REG_L = 5;
        public const int REG_M = 6;
        public const int REG_A = 7;

        private readonly IMemory _memory;
        private readonly IPpiDevice _ppi;
        private readonly IDisplaySink _display;
        private readonly IAnalogSource _analog;
        private readonly ISound _sound;
        private readonly IClock _clock;
        private readonly IDiagnostics _diagnostics;

        public CpuState State { get; }
        public CpuProfile Profile => CpuProfile.Legacy;
        public int CallStackDepth => CallStack.Count;

        public class CallStackElement
        {
            public FlagReg FlagReg;
            public ushort[] Regs = new ushort[8];
            public int RetAddress;
        }

        public Stack<CallStackElement> CallStack { get; } = new Stack<CallStackElement>();

        public IReadOnlyList<ushort> GetCallStack()
        {
            List<ushort> addresses = new List<ushort>(CallStack.Count);
            foreach (CallStackElement element in CallStack)
            {
                addresses.Add((ushort)element.RetAddress);
            }
            return addresses;
        }

        public LegacyCpu(
            IMemory memory,
            IPpiDevice ppi = null,
            IDisplaySink display = null,
            IAnalogSource analog = null,
            ISound sound = null,
            IClock clock = null,
            IDiagnostics diagnostics = null)
        {
            _memory = memory ?? throw new ArgumentNullException(nameof(memory));
            _ppi = ppi;
            _display = display;
            _analog = analog;
            _sound = sound;
            _clock = clock;
            _diagnostics = diagnostics;

            State = new CpuState(_memory);
        }

        public void Reset()
        {
            CallStack.Clear();
            State.Flags.Reset();
            State.PC = MemoryMap.UserMemStart;
            State.SP = 0x83FE;
            State.Cycles = 0;
        }

        public void UpdateFlags(int val)
        {
            State.Flags.Z = (val % 256) == 0;
            if (val < 0 || val > 255)
                State.Flags.C = true;
            else
                State.Flags.C = false;

            if ((val & 128) == 128)
                State.Flags.S = true;
            else
                State.Flags.S = false;
        }

        public void ClearFlags()
        {
            State.Flags.AC = false;
            State.Flags.Z = false;
            State.Flags.C = false;
            State.Flags.P = false;
            State.Flags.S = false;
        }

        public int GetRegister(int regNum)
        {
            switch (regNum)
            {
                case REG_B: return State.B;
                case REG_C: return State.C;
                case REG_D: return State.D;
                case REG_E: return State.E;
                case REG_H: return State.H;
                case REG_L: return State.L;
                case REG_M: return State.M;
                case REG_A: return State.A;
                default: return 0;
            }
        }

        public void SetRegister(int regNum, int val, bool bSetFlags = true)
        {
            if (bSetFlags)
                UpdateFlags(val);

            byte byteVal = (byte)((val + 256) % 256);
            switch (regNum)
            {
                case REG_B: State.B = byteVal; break;
                case REG_C: State.C = byteVal; break;
                case REG_D: State.D = byteVal; break;
                case REG_E: State.E = byteVal; break;
                case REG_H: State.H = byteVal; break;
                case REG_L: State.L = byteVal; break;
                case REG_M: State.M = byteVal; break;
                case REG_A: State.A = byteVal; break;
            }
        }

        public int GetRegPair(int regPairNum)
        {
            // Legacy bug in CInterpreter.cs:88:
            // return getRegister(regPairNum) << 8 | getRegister(regPairNum + 1);
            return (GetRegister(regPairNum) << 8) | GetRegister(regPairNum + 1);
        }

        public void SetRegPair(int regPairNum, int sign, int low, bool bSetFlags = true)
        {
            SetRegister(regPairNum * 2, sign, bSetFlags);
            SetRegister(regPairNum * 2 + 1, low, bSetFlags);
        }

        public void SetRegPair(int regPairNum, int val, bool bSetFlags = true)
        {
            // Legacy bug in CInterpreter.cs:83:
            // setRegPair(regPairNum, val >> 4, val & 15, bSetFlags);
            SetRegPair(regPairNum, val >> 4, val & 15, bSetFlags);
        }

        public int GetWordFromTwoBytes(int sign, int low)
        {
            return (sign << 8) | low;
        }

        public int GetMemory(int addr)
        {
            int a = addr & 0xFFFF;
            if (a == 0x00FA && _ppi != null)
            {
                byte w = _ppi.InputWord;
                _ppi.InputWord = 0xFF;
                return w;
            }
            if (a == 0x00FE && _analog != null)
            {
                return _analog.ReadAdc();
            }
            return _memory.ReadByte((ushort)a);
        }

        public void SetMemory(int addr, int val)
        {
            int a = addr & 0xFFFF;
            byte b = (byte)(val & 0xFF);
            _memory.WriteByte((ushort)a, b);

            // Port handling
            if (a == MemoryMap.PortLamps1 || a == MemoryMap.PortLampsF9)
            {
                if (_ppi != null) _ppi.Lamps = b;
            }
            else if (a == MemoryMap.PortControlWord)
            {
                if (_ppi != null) _ppi.ControlWord = b;
            }
            else if (a == MemoryMap.PortAnalogMux)
            {
                if (_analog != null) _analog.SelectedMuxChannel = (byte)(b & 0x03);
            }

            // Display latch 83F8h..83FFh
            if (a >= MemoryMap.DisplayLatchStart && a <= MemoryMap.DisplayLatchEnd)
            {
                int digitIndex = a - MemoryMap.DisplayLatchStart;
                _display?.WriteDigit(digitIndex, b);
            }
        }

        public bool CallFunc(int addr)
        {
            if (addr == MemoryMap.MonitorSound) // 0300h
            {
                _sound?.PlayTestTone();
                return true;
            }
            if (addr == MemoryMap.MonitorSeggg) // 0100h
            {
                Seggg();
                return true;
            }
            if (addr == MemoryMap.MonitorDelay) // 0400h
            {
                Delay();
                return true;
            }
            return false;
        }

        public void Seggg()
        {
            int[] numArray = new int[16] { 64, 6, 91, 79, 102, 109, 125, 7, 128, 111, 119, 124, 57, 94, 121, 113 };
            for (int i = 0; i < 4; i++)
            {
                int mem = GetMemory(33780 + i); // 83F4h..83F7h
                int hi = (mem >> 4) & 0x0F;
                int lo = mem & 0x0F;
                SetMemory(33784 + i * 2, numArray[hi]);
                SetMemory(33784 + i * 2 + 1, numArray[lo]);
            }
        }

        public void Delay()
        {
            // Reads 4 nibbles from 8000h and 8001h as 16-bit milliseconds
            int s1, l1, s2, l2;
            _memory.GetNibbles(0x8000, out s1, out l1);
            _memory.GetNibbles(0x8001, out s2, out l2);
            int ms = (s1 << 12) | (l1 << 8) | (s2 << 4) | l2;
            _clock?.Delay(ms);
        }

        public bool Step()
        {
            ushort pc = State.PC;
            int curCmd = GetMemory(pc);
            int curOp1 = GetMemory(pc + 1);
            int curOp2 = GetMemory(pc + 2);

            bool success = InterpretCommand(curCmd, curOp1, curOp2);
            if (success)
            {
                State.Cycles++;
            }
            else
            {
                _diagnostics?.ReportUnsupported(pc, (byte)curCmd);
            }
            return success;
        }

        public bool InterpretCommand(int curCmd, int curOp1, int curOp2)
        {
            // MOV
            for (int regNum = 0; regNum <= 7; ++regNum)
            {
                int num1 = 64 + regNum * 8;
                int num2 = num1 + 7;
                if (curCmd >= num1 && curCmd <= num2 && curCmd != 118) // 118 is HLT
                {
                    SetRegister(regNum, GetRegister(curCmd - num1));
                    State.PC += 1;
                    return true;
                }
            }

            // MVI
            for (int regNum = 0; regNum <= 7; ++regNum)
            {
                if (curCmd == 6 + regNum * 8)
                {
                    SetRegister(regNum, curOp1);
                    State.PC += 2;
                    return true;
                }
            }

            // LXI
            for (int regPairNum = 0; regPairNum <= 3; ++regPairNum)
            {
                if (curCmd == 16 * regPairNum + 1)
                {
                    SetRegPair(regPairNum, curOp2, curOp1);
                    State.PC += 3;
                    return true;
                }
            }

            // LDAX / STAX
            for (int regPairNum = 0; regPairNum <= 1; ++regPairNum)
            {
                if (curCmd == 16 * regPairNum + 10) // LDAX
                {
                    SetRegister(7, GetMemory(GetRegPair(regPairNum)));
                    State.PC += 1;
                    return true;
                }
                if (curCmd == 16 * regPairNum + 2) // STAX
                {
                    SetMemory(GetRegPair(regPairNum), GetRegister(7));
                    State.PC += 1;
                    return true;
                }
            }

            // LHLD / SHLD
            for (int index = 2; index <= 3; ++index)
            {
                if (curCmd == 16 * index + 10) // LHLD
                {
                    if (index == 2)
                        SetRegPair(2, 0, GetMemory(GetWordFromTwoBytes(curOp2, curOp1)));
                    else
                        SetRegister(7, GetMemory(GetWordFromTwoBytes(curOp2, curOp1)));
                    State.PC += 3;
                    return true;
                }
                if (curCmd == 16 * index + 2) // SHLD
                {
                    if (index == 2)
                        SetMemory(GetWordFromTwoBytes(curOp2, curOp1), GetRegPair(2));
                    else
                        SetMemory(GetWordFromTwoBytes(curOp2, curOp1), GetRegister(7));
                    State.PC += 3;
                    return true;
                }
            }

            // XCHG
            if (curCmd == 235) // 0xEB
            {
                int regPair = GetRegPair(1);
                SetRegPair(1, GetRegPair(2));
                SetRegPair(2, regPair);
                State.PC += 1;
                return true;
            }

            // INR / DCR
            for (int regNum = 0; regNum <= 7; ++regNum)
            {
                if (curCmd == 8 * regNum + 4) // INR
                {
                    SetRegister(regNum, GetRegister(regNum) + 1);
                    State.PC += 1;
                    return true;
                }
                if (curCmd == 8 * regNum + 5) // DCR
                {
                    SetRegister(regNum, GetRegister(regNum) - 1);
                    State.PC += 1;
                    return true;
                }
            }

            // INX / DCX
            for (int regPairNum = 0; regPairNum <= 3; ++regPairNum)
            {
                if (curCmd == 16 * regPairNum + 3) // INX
                {
                    SetRegister(regPairNum * 2 + 1, GetRegister(regPairNum * 2 + 1) + 1);
                    if (State.Flags.C)
                    {
                        SetRegister(regPairNum * 2, GetRegister(regPairNum * 2) + 1);
                    }
                    ClearFlags();
                    State.PC += 1;
                    return true;
                }
                if (curCmd == 16 * regPairNum + 11) // DCX
                {
                    SetRegister(regPairNum * 2 + 1, GetRegister(regPairNum * 2 + 1) - 1);
                    if (State.Flags.C)
                    {
                        SetRegister(regPairNum * 2, GetRegister(regPairNum * 2) - 1);
                    }
                    ClearFlags();
                    State.PC += 1;
                    return true;
                }
            }

            // ADD / SUB
            for (int regNum = 0; regNum <= 7; ++regNum)
            {
                if (curCmd == 128 + regNum) // ADD
                {
                    SetRegister(REG_A, GetRegister(regNum) + GetRegister(REG_A));
                    State.PC += 1;
                    return true;
                }
                if (curCmd == 144 + regNum) // SUB
                {
                    SetRegister(REG_A, GetRegister(REG_A) - GetRegister(regNum));
                    State.PC += 1;
                    return true;
                }
            }

            // DAD
            for (int regPairNum = 0; regPairNum <= 3; ++regPairNum)
            {
                if (curCmd == 16 * regPairNum + 9) // DAD
                {
                    SetRegPair(regPairNum, GetRegPair(regPairNum) + GetRegPair(2));
                    State.PC += 1;
                    return true;
                }
            }

            // ANA / XRA / ORA / CMP
            for (int regNum = 0; regNum <= 7; ++regNum)
            {
                if (curCmd == 160 + regNum) // ANA
                {
                    SetRegister(REG_A, GetRegister(REG_A) & GetRegister(regNum));
                    State.PC += 1;
                    return true;
                }
                if (curCmd == 168 + regNum) // XRA
                {
                    SetRegister(REG_A, GetRegister(REG_A) ^ GetRegister(regNum));
                    State.PC += 1;
                    return true;
                }
                if (curCmd == 176 + regNum) // ORA
                {
                    SetRegister(REG_A, GetRegister(REG_A) | GetRegister(regNum));
                    State.PC += 1;
                    return true;
                }
                if (curCmd == 184 + regNum) // CMP
                {
                    int num = (ushort)GetRegister(REG_A) - (ushort)GetRegister(regNum);
                    UpdateFlags(num);
                    State.PC += 1;
                    return true;
                }
            }

            // JMP
            if (curCmd == 195) // 0xC3
            {
                State.PC = (ushort)((curOp2 << 8) | curOp1);
                return true;
            }

            // JNZ
            if (curCmd == 194) // 0xC2
            {
                if (!State.Flags.Z)
                    State.PC = (ushort)((curOp2 << 8) | curOp1);
                else
                    State.PC += 3;
                return true;
            }

            // JZ
            if (curCmd == 202) // 0xCA
            {
                if (State.Flags.Z)
                    State.PC = (ushort)((curOp2 << 8) | curOp1);
                else
                    State.PC += 3;
                return true;
            }

            // JNC
            if (curCmd == 210) // 0xD2
            {
                if (!State.Flags.C)
                    State.PC = (ushort)((curOp2 << 8) | curOp1);
                else
                    State.PC += 3;
                return true;
            }

            // JC
            if (curCmd == 218) // 0xDA
            {
                if (State.Flags.C)
                    State.PC = (ushort)((curOp2 << 8) | curOp1);
                else
                    State.PC += 3;
                return true;
            }

            // JM
            if (curCmd == 0xFA)
            {
                if (State.Flags.S)
                    State.PC = (ushort)((curOp2 << 8) | curOp1);
                else
                    State.PC += 3;
                return true;
            }

            // JP
            if (curCmd == 0xF2)
            {
                if (!State.Flags.S)
                    State.PC = (ushort)((curOp2 << 8) | curOp1);
                else
                    State.PC += 3;
                return true;
            }

            // RC (Legacy memory load quirk)
            if (curCmd == 216) // 0xD8
            {
                SetRegister(REG_A, GetMemory(250 + curOp1));
                State.PC += 2;
                return true;
            }

            // OUT
            if (curCmd == 211) // 0xD3
            {
                SetMemory(curOp1, GetRegister(REG_A));
                State.PC += 2;
                return true;
            }

            // IN
            if (curCmd == 0xDB)
            {
                if (curOp1 == 0xFA) // tumblers
                {
                    if (_ppi != null)
                        SetRegister(REG_A, _ppi.ReadTumblers());
                    else
                        SetRegister(REG_A, 1);
                }
                State.PC += 2;
                return true;
            }

            // HLT
            if (curCmd == 118) // 0x76
            {
                return false; // Halted
            }

            // RET
            if (curCmd == 0xC9)
            {
                if (CallStack.Count > 0)
                {
                    var elem = CallStack.Pop();
                    for (int i = REG_B; i <= REG_A; i++)
                    {
                        SetRegister(i, elem.Regs[i], bSetFlags: false);
                    }
                    State.Flags = elem.FlagReg;
                    State.PC = (ushort)elem.RetAddress;
                    return true;
                }
                else
                {
                    _diagnostics?.ReportError("unable to execute command RET: callStack is empty");
                    return false;
                }
            }

            // CALL
            if (curCmd == 0xCD)
            {
                int targetAddr = GetWordFromTwoBytes(curOp2, curOp1);
                if (CallFunc(targetAddr))
                {
                    State.PC += 3;
                }
                else
                {
                    var elem = new CallStackElement
                    {
                        FlagReg = State.Flags,
                        RetAddress = State.PC + 3
                    };
                    for (int i = REG_B; i <= REG_A; i++)
                    {
                        elem.Regs[i] = (ushort)GetRegister(i);
                    }
                    CallStack.Push(elem);
                    State.PC = (ushort)targetAddr;
                }
                return true;
            }

            return false;
        }
    }
}
