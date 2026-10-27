namespace MikroLab.Core.Machine
{
    public enum RunState
    {
        Stopped,
        Running,
        Paused,
        Halted
    }

    public enum ResetKind
    {
        Soft, // Reset PC to 8000h, preserve memory
        Hard  // Re-randomize memory and registers
    }
}
