namespace MikroLab.Core.Cpu
{
    /// <summary>Accumulator rotation performed by RLC, RRC, RAL and RAR.</summary>
    public enum RotationKind
    {
        LeftCircular,
        RightCircular,
        LeftThroughCarry,
        RightThroughCarry
    }

    public readonly struct RotationResult
    {
        public byte A { get; }
        public bool Carry { get; }

        public RotationResult(byte a, bool carry)
        {
            A = a;
            Carry = carry;
        }
    }

    /// <summary>
    /// Pure rotation formulas shared by the animation and by tests. The CPU
    /// executes the same operations inline; keeping the formula here makes the
    /// visualisation verifiable without a running machine.
    /// </summary>
    public static class RotationMath
    {
        public static RotationResult Apply(RotationKind kind, byte a, bool carry)
        {
            switch (kind)
            {
                case RotationKind.LeftCircular:
                    return new RotationResult((byte)((a << 1) | (a >> 7)), (a & 0x80) != 0);
                case RotationKind.RightCircular:
                    return new RotationResult((byte)((a >> 1) | (a << 7)), (a & 0x01) != 0);
                case RotationKind.LeftThroughCarry:
                    return new RotationResult((byte)((a << 1) | (carry ? 1 : 0)), (a & 0x80) != 0);
                case RotationKind.RightThroughCarry:
                    return new RotationResult((byte)((a >> 1) | (carry ? 0x80 : 0)), (a & 0x01) != 0);
                default:
                    return new RotationResult(a, carry);
            }
        }

        public static RotationKind? FromMnemonic(string mnemonic)
        {
            switch ((mnemonic ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "RLC": return RotationKind.LeftCircular;
                case "RRC": return RotationKind.RightCircular;
                case "RAL": return RotationKind.LeftThroughCarry;
                case "RAR": return RotationKind.RightThroughCarry;
                default: return null;
            }
        }
    }
}
