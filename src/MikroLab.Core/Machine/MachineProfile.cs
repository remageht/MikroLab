using System;

namespace MikroLab.Core.Machine
{
    /// <summary>
    /// A named memory and stack configuration for the educational board.
    /// The VNTU preset keeps the familiar 8000h origin and 83F0h stack. The
    /// UMK preset uses the 0800h monitor area and allows the origin, initial
    /// PC and stack pointer to be adjusted by the host.
    /// </summary>
    public sealed class MachineProfile
    {
        public string Name { get; }
        public ushort Origin { get; }
        public ushort EntryPoint { get; }
        public ushort StackPointer { get; }

        public MachineProfile(string name, ushort origin, ushort entryPoint, ushort stackPointer)
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Профиль" : name.Trim();
            Origin = origin;
            EntryPoint = entryPoint;
            StackPointer = stackPointer;
        }

        public static MachineProfile Vntu => new MachineProfile("ВНТУ", 0x8000, 0x8000, 0x83F0);

        public static MachineProfile Umk => new MachineProfile("УМК", 0x0800, 0x0800, 0x0FFF);

        public MachineProfile With(string name, ushort origin, ushort entryPoint, ushort stackPointer)
        {
            return new MachineProfile(name, origin, entryPoint, stackPointer);
        }

        public override string ToString()
        {
            return Name + " · ORG " + Origin.ToString("X4") + "h · PC " +
                EntryPoint.ToString("X4") + "h · SP " + StackPointer.ToString("X4") + "h";
        }
    }
}
