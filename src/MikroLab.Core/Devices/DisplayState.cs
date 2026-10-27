using System;

namespace MikroLab.Core.Devices
{
    public class DisplayState : IDisplaySink
    {
        private readonly byte[] _digits = new byte[8];

        public void WriteDigit(int index, byte segments)
        {
            if (index >= 0 && index < 8)
                _digits[index] = segments;
        }

        public byte GetDigit(int index)
        {
            if (index >= 0 && index < 8)
                return _digits[index];
            return 0;
        }

        public void Reset()
        {
            Array.Clear(_digits, 0, _digits.Length);
        }

        public byte[] CaptureDigits()
        {
            byte[] copy = new byte[8];
            Array.Copy(_digits, copy, 8);
            return copy;
        }

        public DisplayState Clone()
        {
            var ds = new DisplayState();
            for (int i = 0; i < 8; i++)
                ds._digits[i] = this._digits[i];
            return ds;
        }
    }
}
