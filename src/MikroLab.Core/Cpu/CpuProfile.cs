namespace MikroLab.Core.Cpu
{
    public enum CpuProfile
    {
        /// <summary>
        /// Exact legacy decompiled emulator behavior (registers in memory 83E4h–83EBh, quirks, call stack).
        /// </summary>
        Legacy,

        /// <summary>
        /// Extended / strict i8080 behavior: full instruction set, hardware stack, accurate flags and pairs.
        /// </summary>
        Extended
    }
}
