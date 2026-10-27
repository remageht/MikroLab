namespace MikroLab.Core.Devices
{
    public class PpiState : IPpiDevice
    {
        public byte Lamps { get; set; } = 0;
        public byte ControlWord { get; set; } = 0xFF;
        public byte InputWord { get; set; } = 0xFF;

        public bool Tumbler1 { get; set; }
        public bool Tumbler2 { get; set; }
        public bool Tumbler3 { get; set; }

        public byte ReadTumblers()
        {
            // Formula in legacy CInterpreter.cs:491:
            // 1 | (9 * CB1) | (5 * CB2) | (3 * CB3)
            int val = 1 | (Tumbler1 ? 9 : 0) | (Tumbler2 ? 5 : 0) | (Tumbler3 ? 3 : 0);
            return (byte)(val & 0xFF);
        }

        public void Reset()
        {
            Lamps = 0;
            ControlWord = 0xFF;
            InputWord = 0xFF;
            Tumbler1 = false;
            Tumbler2 = false;
            Tumbler3 = false;
        }

        public PpiState Clone()
        {
            return new PpiState
            {
                Lamps = this.Lamps,
                ControlWord = this.ControlWord,
                InputWord = this.InputWord,
                Tumbler1 = this.Tumbler1,
                Tumbler2 = this.Tumbler2,
                Tumbler3 = this.Tumbler3
            };
        }
    }
}
