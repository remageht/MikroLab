namespace MikroLab.Core.Memory
{
    public static class MemoryMap
    {
        public const int MemSize = 0x10000; // 64K (65536)
        public const int UserMemStart = 0x8000;
        public const int UserMemEnd = 0x83C6;

        public const int RegsStart = 0x83E4;
        public const int RegsEnd = 0x83EB;
        public const int RegL = 0x83E4;
        public const int RegH = 0x83E5;
        public const int RegE = 0x83E6;
        public const int RegD = 0x83E7;
        public const int RegC = 0x83E8;
        public const int RegB = 0x83E9;
        public const int RegF = 0x83EA;
        public const int RegA = 0x83EB;

        public const int SegggInputStart = 0x83F4;
        public const int SegggInputEnd = 0x83F7;

        public const int DisplayLatchStart = 0x83F8;
        public const int DisplayLatchEnd = 0x83FF;

        // Monitor entry points
        public const int MonitorSeggg = 0x0100;
        public const int MonitorSound = 0x0300;
        public const int MonitorDelay = 0x0400;

        // Ports mapped to memory addresses in legacy
        public const int PortInputWord = 0x00FA; // aWord
        public const int PortLamps1 = 0x0001;    // OUT 01h
        public const int PortLampsF9 = 0x00F9;   // OUT F9h
        public const int PortControlWord = 0x0002; // OUT 02h
        public const int PortAnalogMux = 0x00FD; // OUT FDh
        public const int PortAdc = 0x00FE;       // IN FEh
    }
}
