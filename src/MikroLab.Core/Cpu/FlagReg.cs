namespace MikroLab.Core.Cpu
{
    public struct FlagReg
    {
        public bool S;
        public bool Z;
        public bool AC;
        public bool P;
        public bool C;

        public byte ToByte()
        {
            // Intel 8080 PSW format: D7=S, D6=Z, D5=0, D4=AC, D3=0, D2=P, D1=1, D0=C
            int val = 0x02; // D1 is always 1
            if (S) val |= 0x80;
            if (Z) val |= 0x40;
            if (AC) val |= 0x10;
            if (P) val |= 0x04;
            if (C) val |= 0x01;
            return (byte)val;
        }

        public void FromByte(byte b)
        {
            S = (b & 0x80) != 0;
            Z = (b & 0x40) != 0;
            AC = (b & 0x10) != 0;
            P = (b & 0x04) != 0;
            C = (b & 0x01) != 0;
        }

        public void Reset()
        {
            S = false;
            Z = false;
            AC = false;
            P = false;
            C = false;
        }
    }
}
