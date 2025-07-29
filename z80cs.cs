using System;

namespace z80cs
{
    internal class Z80_CPU
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello World!");
        }

        private byte AReg,
            BReg,
            CReg,
            DReg,
            EReg,
            FReg,
            HReg,
            LReg,
            MemRefreshReg,
            InterruptVectorReg,
            IODeviceAddress,
            IMF; // this register takes care of managing interrupt modes and must be managed with its 2 lower bits
        private ushort ProgCounterReg,
            IXIndexReg,
            IYIndexReg,
            StackPointerReg,
            HLReg,
            BCReg,
            DEReg;
        private Boolean NMITrigger,
            IFF1,
            IFF2; // IFF1 is for disabling maskable interrupts: if it's 1, IRQs are enabled.
        public const ushort NMIVector = 0x0066;

        public Z80_CPU()
        {
            reset();
        }

        public void reset()
        {
            AReg = 0;
            BReg = 0;
            CReg = 0;
            DReg = 0;
            EReg = 0;
            FReg = 0;
            HReg = 0;
            LReg = 0;
            UpdateHLFrom8BitReg();
            UpdateBCFrom8BitReg();
            UpdateDEFrom8BitReg();
            MemRefreshReg = 0;
            InterruptVectorReg = 0;
            ProgCounterReg = 0x0000; // verified good
            IXIndexReg = 0;
            IYIndexReg = 0;
            IODeviceAddress = 0;
            NMITrigger = false;
            IFF1 = true; // maskable interrupts are enabled by default
            IFF2 = false;
            IMF = 0; // mode 0 because I say so
        }

        // these update methods must be called every time H, L, D, E, B, C registers are used to not desync the state of HL, DE, BC
        private void UpdateHLFrom8BitReg()
        {
            HLReg = (ushort)(HReg); // the bits of the H Register go into HL
            HLReg = (ushort)(HLReg << 8); // the 8 bits of the H register are shifted into the upper 16 bits of the HL Register
            HLReg = (ushort)(HLReg + LReg); // and now we put in the contents of the L Register
        }

        private void UpdateBCFrom8BitReg()
        {
            HLReg = (ushort)(BReg);
            HLReg = (ushort)(HLReg << 8);
            HLReg = (ushort)(HLReg + CReg);
        }

        private void UpdateDEFrom8BitReg()
        {
            HLReg = (ushort)(DReg);
            HLReg = (ushort)(HLReg << 8);
            HLReg = (ushort)(HLReg + EReg);
        }

        private void UpdateHLFrom16BitReg()
        {
            LReg = (byte)(HLReg); // we discard the upper 16 bits
            HReg = (byte)(HLReg >> 8); // the H register is stored in the upper 8 bits of the HL Register
        }

        private void UpdateDEFrom16BitReg()
        {
            EReg = (byte)(DEReg);
            DReg = (byte)(DEReg >> 8);
        }

        private void UpdateBCFrom16BitReg()
        {
            CReg = (byte)(BCReg);
            BReg = (byte)(BCReg >> 8);
        }
    }
}
