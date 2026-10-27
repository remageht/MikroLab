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

    /// <summary>
    /// Optional clock extension: lets the execution session abort a monitor
    /// delay when the user pauses, resets or closes the machine.
    /// </summary>
    public interface IInterruptibleClock : IClock
    {
        void RequestStop();
        void ClearStop();
    }

    public interface IDiagnostics
    {
        void ReportError(string message);
        void ReportUnsupported(int pc, byte opcode);
    }
}
