namespace MikroLab.Core.Devices
{
    public interface IAnalogSource
    {
        double ReadChannel(int channel);
        void SetChannel(int channel, double voltage);
        byte SelectedMuxChannel { get; set; }
        byte ReadAdc();
    }
}
