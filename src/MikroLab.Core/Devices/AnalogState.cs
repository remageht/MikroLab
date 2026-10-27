using System;

namespace MikroLab.Core.Devices
{
    public class AnalogState : IAnalogSource
    {
        private readonly double[] _voltages = new double[4];
        public byte SelectedMuxChannel { get; set; } = 0;

        public double ReadChannel(int channel)
        {
            if (channel >= 0 && channel < 4)
                return _voltages[channel];
            return 0.0;
        }

        public void SetChannel(int channel, double voltage)
        {
            if (channel >= 0 && channel < 4)
                _voltages[channel] = voltage;
        }

        public byte ReadAdc()
        {
            // Conversion in legacy: 0..10V -> 0..255 (255 / 10.0 = 25.5 * voltage)
            int ch = SelectedMuxChannel & 0x03;
            double v = _voltages[ch];
            int adc = (int)Math.Round(v * 25.5);
            if (adc < 0) adc = 0;
            if (adc > 255) adc = 255;
            return (byte)adc;
        }

        public double[] CaptureVoltages()
        {
            double[] copy = new double[4];
            Array.Copy(_voltages, copy, 4);
            return copy;
        }

        public AnalogState Clone()
        {
            var s = new AnalogState { SelectedMuxChannel = this.SelectedMuxChannel };
            for (int i = 0; i < 4; i++)
                s._voltages[i] = this._voltages[i];
            return s;
        }
    }
}
