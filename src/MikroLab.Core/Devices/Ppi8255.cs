using System;

namespace MikroLab.Core.Devices
{
    /// <summary>Operating mode of one 8255 group.</summary>
    public enum PpiMode
    {
        Mode0 = 0,
        Mode1 = 1,
        Mode2 = 2
    }

    /// <summary>
    /// KR580IK55 / Intel 8255A programmable peripheral interface.
    /// Ports A, B and C have output latches and external input pins; the
    /// control word selects mode 0..2 and the direction of each port half.
    /// Handshake lines of modes 1 and 2 are represented by the port C
    /// direction bits; the data registers remain directly accessible.
    /// </summary>
    public sealed class Ppi8255
    {
        public const byte ResetControlWord = 0x9B; // mode 0, all ports input

        public Ppi8255()
        {
            Reset();
        }

        /// <summary>Output latch of port A.</summary>
        public byte PortA { get; private set; }

        /// <summary>Output latch of port B.</summary>
        public byte PortB { get; private set; }

        /// <summary>Output latch of port C.</summary>
        public byte PortC { get; private set; }

        /// <summary>Last mode-set control word (bit 7 = 1).</summary>
        public byte ControlWord { get; private set; } = ResetControlWord;

        /// <summary>External pins of port A. PA0..PA2 are the SA1..SA3 lines.</summary>
        public byte InputA { get; set; }

        /// <summary>External pins of port B.</summary>
        public byte InputB { get; set; }

        /// <summary>External pins of port C.</summary>
        public byte InputC { get; set; }

        /// <summary>
        /// Optional matrix keyboard. When upper port C is an input, PC4..PC7
        /// are read from the keyboard columns selected by PC0..PC3.
        /// </summary>
        public IKeyboardMatrix Keyboard { get; set; }

        public PpiMode GroupAMode { get; private set; }
        public PpiMode GroupBMode { get; private set; }

        public bool PortAInput { get; private set; }
        public bool PortBInput { get; private set; }
        public bool PortCUpperInput { get; private set; }
        public bool PortCLowerInput { get; private set; }

        /// <summary>Mode 2 makes port A a bidirectional bus.</summary>
        public bool PortABidirectional => GroupAMode == PpiMode.Mode2;

        public bool Tumbler1
        {
            get => (InputA & 0x01) != 0;
            set => InputA = SetBit(InputA, 0, value);
        }

        public bool Tumbler2
        {
            get => (InputA & 0x02) != 0;
            set => InputA = SetBit(InputA, 1, value);
        }

        public bool Tumbler3
        {
            get => (InputA & 0x04) != 0;
            set => InputA = SetBit(InputA, 2, value);
        }

        /// <summary>Honest PA0..PA2 line states, without the legacy magic mask.</summary>
        public byte ReadTumblers()
        {
            return (byte)(InputA & 0x07);
        }

        public bool IsPortInput(int portIndex)
        {
            switch (portIndex)
            {
                case 0: return PortAInput;
                case 1: return PortBInput;
                case 2: return PortCUpperInput && PortCLowerInput;
                default: throw new ArgumentOutOfRangeException(nameof(portIndex));
            }
        }

        /// <summary>Reads a port register (0=A, 1=B, 2=C, 3=control).</summary>
        public byte ReadPort(int portIndex)
        {
            switch (portIndex)
            {
                case 0:
                    return (PortAInput || PortABidirectional) ? InputA : PortA;
                case 1:
                    return PortBInput ? InputB : PortB;
                case 2:
                    return ReadPortC();
                case 3:
                    return ControlWord;
                default:
                    throw new ArgumentOutOfRangeException(nameof(portIndex));
            }
        }

        /// <summary>Writes a port register (0=A, 1=B, 2=C, 3=control).</summary>
        public void WritePort(int portIndex, byte value)
        {
            switch (portIndex)
            {
                case 0:
                    if (PortAInput && !PortABidirectional)
                    {
                        return;
                    }
                    PortA = value;
                    break;
                case 1:
                    if (PortBInput)
                    {
                        return;
                    }
                    PortB = value;
                    break;
                case 2:
                    WritePortC(value);
                    break;
                case 3:
                    WriteControl(value);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(portIndex));
            }
        }

        /// <summary>
        /// Writes the control register. Bit 7 = 1 selects a mode word; bit 7 = 0
        /// performs a set/reset of one port C output latch bit.
        /// </summary>
        public void WriteControl(byte value)
        {
            if ((value & 0x80) == 0)
            {
                int bit = (value >> 1) & 0x07;
                bool set = (value & 0x01) != 0;
                PortC = SetBit(PortC, bit, set);
                return;
            }

            ControlWord = value;
            int modeA = (value >> 5) & 0x03;
            GroupAMode = modeA == 0 ? PpiMode.Mode0 :
                modeA == 1 ? PpiMode.Mode1 : PpiMode.Mode2;
            GroupBMode = ((value >> 2) & 0x01) != 0 ? PpiMode.Mode1 : PpiMode.Mode0;

            PortAInput = (value & 0x10) != 0;
            PortCUpperInput = (value & 0x08) != 0;
            PortBInput = (value & 0x02) != 0;
            PortCLowerInput = (value & 0x01) != 0;
        }

        public void Reset()
        {
            PortA = 0;
            PortB = 0;
            PortC = 0;
            InputA = 0;
            InputB = 0;
            InputC = 0;
            WriteControl(ResetControlWord);
        }

        /// <summary>Restores a complete snapshot without running reset logic.</summary>
        public void RestoreState(
            byte portA,
            byte portB,
            byte portC,
            byte controlWord,
            byte inputA,
            byte inputB,
            byte inputC)
        {
            PortA = portA;
            PortB = portB;
            PortC = portC;
            InputA = inputA;
            InputB = inputB;
            InputC = inputC;
            WriteControl((byte)(controlWord | 0x80));
            // A snapshot may contain the reset word or a bit set/reset-derived
            // word. Keep the exact value the control register had.
            ControlWord = controlWord;
        }

        public Ppi8255 Clone()
        {
            var clone = new Ppi8255();
            clone.RestoreState(PortA, PortB, PortC, ControlWord, InputA, InputB, InputC);
            clone.Keyboard = Keyboard;
            return clone;
        }

        /// <summary>
        /// Recomputes the keyboard half of <see cref="InputC"/> from the
        /// currently selected row lines. Hosts call this after a key event so
        /// captured snapshots show the same value the CPU would read.
        /// </summary>
        public void RefreshKeyboard()
        {
            if (Keyboard == null) return;
            if (!PortCUpperInput) return;

            byte rows = (byte)((PortCLowerInput ? InputC : PortC) & 0x0F);
            InputC = (byte)((InputC & 0x0F) | (Keyboard.ReadColumns(rows) & 0xF0));
        }

        private byte ReadPortC()
        {
            byte value = 0;
            if (PortCUpperInput && Keyboard != null)
            {
                byte rows = (byte)((PortCLowerInput ? InputC : PortC) & 0x0F);
                value |= (byte)(Keyboard.ReadColumns(rows) & 0xF0);
            }
            else
            {
                value |= PortCUpperInput ? (byte)(InputC & 0xF0) : (byte)(PortC & 0xF0);
            }
            value |= PortCLowerInput ? (byte)(InputC & 0x0F) : (byte)(PortC & 0x0F);
            return value;
        }

        private void WritePortC(byte value)
        {
            byte next = PortC;
            if (!PortCUpperInput)
            {
                next = (byte)((next & 0x0F) | (value & 0xF0));
            }
            if (!PortCLowerInput)
            {
                next = (byte)((next & 0xF0) | (value & 0x0F));
            }
            PortC = next;
        }

        private static byte SetBit(byte value, int bit, bool set)
        {
            return set
                ? (byte)(value | (1 << bit))
                : (byte)(value & ~(1 << bit));
        }
    }
}
