using System.Collections.Generic;

namespace MikroLab.Core.Cpu
{
    public interface ICpu
    {
        CpuState State { get; }
        CpuProfile Profile { get; }

        /// <summary>
        /// Debug-only call stack depth. Hardware stack contents live in memory.
        /// </summary>
        int CallStackDepth { get; }

        /// <summary>
        /// Debug-only return addresses, top first.
        /// </summary>
        IReadOnlyList<ushort> GetCallStack();

        /// <summary>
        /// Executes exactly one instruction at State.PC.
        /// Returns true if execution should continue, false if HLT was reached or unsupported command.
        /// </summary>
        bool Step();

        void Reset();
    }
}
