namespace MikroLab.Core.Devices
{
    public interface ISound
    {
        void PlayTone(uint frequency, uint duration);
        void PlayTestTone(); // Monitor 0300h
    }

    public interface IClock
    {
        void Delay(int milliseconds); // Monitor 0400h
    }

    public interface IDiagnostics
    {
        void ReportError(string message);
        void ReportUnsupported(int pc, byte opcode);
    }
}
