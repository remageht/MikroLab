using System;

namespace MikroLab.Core.Devices
{
    /// <summary>
    /// Address map of the 8255 on the board. The four primary ports are used
    /// by IN/OUT; legacy addresses keep old VNTU programs working on the
    /// memory-mapped bus. Set a legacy address to -1 to disable it.
    /// </summary>
    public sealed class PpiPortMap
    {
        public int PortA { get; set; } = 0xFA;
        public int PortB { get; set; } = 0xF9;
        public int PortC { get; set; } = 0xF8;
        public int Control { get; set; } = 0xFB;

        /// <summary>Old OUT 01h / STA 0001h lamp port (writes port B).</summary>
        public int LegacyPortB { get; set; } = 0x01;

        /// <summary>Old OUT 02h / STA 0002h control port.</summary>
        public int LegacyControl { get; set; } = 0x02;

        public static PpiPortMap Default => new PpiPortMap();

        public PpiPortMap Clone()
        {
            return new PpiPortMap
            {
                PortA = PortA,
                PortB = PortB,
                PortC = PortC,
                Control = Control,
                LegacyPortB = LegacyPortB,
                LegacyControl = LegacyControl
            };
        }

        public void Validate()
        {
            ValidatePrimary(PortA, nameof(PortA));
            ValidatePrimary(PortB, nameof(PortB));
            ValidatePrimary(PortC, nameof(PortC));
            ValidatePrimary(Control, nameof(Control));
            ValidateLegacy(LegacyPortB, nameof(LegacyPortB));
            ValidateLegacy(LegacyControl, nameof(LegacyControl));

            int[] ports = { PortA, PortB, PortC, Control };
            for (int i = 0; i < ports.Length; i++)
            {
                for (int j = i + 1; j < ports.Length; j++)
                {
                    if (ports[i] == ports[j])
                    {
                        throw new ArgumentException(
                            "PPI port addresses must be unique; duplicate " +
                            ports[i].ToString("X2") + "h.");
                    }
                }
            }

            if (LegacyPortB >= 0 && Array.IndexOf(ports, LegacyPortB) >= 0)
            {
                throw new ArgumentException("Legacy port B aliases a primary PPI port.");
            }
            if (LegacyControl >= 0 && Array.IndexOf(ports, LegacyControl) >= 0)
            {
                throw new ArgumentException("Legacy control aliases a primary PPI port.");
            }
            if (LegacyPortB >= 0 && LegacyPortB == LegacyControl)
            {
                throw new ArgumentException("Legacy port B and control must be different.");
            }
        }

        private static void ValidatePrimary(int port, string name)
        {
            if (port < 0 || port > 0xFF)
            {
                throw new ArgumentOutOfRangeException(name, port, "A PPI port must be in 00h..FFh.");
            }
        }

        private static void ValidateLegacy(int port, string name)
        {
            if (port < -1 || port > 0xFF)
            {
                throw new ArgumentOutOfRangeException(name, port, "A legacy PPI address must be -1 or 00h..FFh.");
            }
        }
    }
}
