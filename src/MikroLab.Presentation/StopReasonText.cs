using System.Globalization;
using MikroLab.Core.Machine;
using MikroLab.Core.Memory;

namespace MikroLab.Presentation
{
    /// <summary>Human-readable description of the last pause reason.</summary>
    public static class StopReasonText
    {
        public static string Describe(MachineSession session)
        {
            if (session == null) return string.Empty;

            switch (session.PauseReason)
            {
                case PauseReason.Breakpoint:
                    return "точка останова " + Address(session.BreakpointAddress ?? session.StopAddress);
                case PauseReason.Watchpoint:
                    return "наблюдение " + Address(session.WatchpointAddress ?? session.StopAddress) +
                           " (" + Access(session.WatchpointAccess) + ")";
                case PauseReason.RunToAddress:
                    return "достигнут адрес " + Address(session.StopAddress);
                case PauseReason.Manual:
                    return "пауза вручную";
                case PauseReason.Halted:
                    return "останов по HLT";
                default:
                    return string.Empty;
            }
        }

        private static string Address(ushort? address)
        {
            return address.HasValue
                ? address.Value.ToString("X4", CultureInfo.InvariantCulture) + "h"
                : "—";
        }

        private static string Access(MemoryAccess? access)
        {
            if (!access.HasValue) return "доступ";
            if (access.Value == MemoryAccess.Read) return "чтение";
            if (access.Value == MemoryAccess.Write) return "запись";
            return "чтение/запись";
        }
    }
}
