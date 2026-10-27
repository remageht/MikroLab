namespace MikroLab.Core.Devices
{
    public interface IPpiDevice
    {
        byte Lamps { get; set; }
        byte ControlWord { get; set; }
        byte InputWord { get; set; } // aWord (0x00FA)

        bool Tumbler1 { get; set; }
        bool Tumbler2 { get; set; }
        bool Tumbler3 { get; set; }

        byte ReadTumblers();
        void Reset();
    }
}
