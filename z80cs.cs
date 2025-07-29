using System;
using System.Globalization;

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
            IFF1, // IFF1 is for disabling maskable interrupts: if it's 1, IRQs are enabled.
            IFF2;
        public const ushort NMIVector = 0x0066;
        private byte[] AddressSpace = new byte[0xFFFF]; // this always reserves 64k of RAM. lol
        readonly CultureInfo ci = new("en-us");

        public Z80_CPU()
        {
            Reset();
        }

        public void Reset()
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
        // TODO: is this machine big endian or little endian?
        private void UpdateHLFrom8BitReg()
        {
            HLReg = (ushort)(HReg); // the bits of the H Register go into HL
            HLReg = (ushort)(HLReg << 8); // the 8 bits of the H register are shifted into the upper 16 bits of the HL Register
            HLReg = (ushort)(HLReg + LReg); // and now we put in the contents of the L Register
        }

        private void UpdateBCFrom8BitReg()
        {
            BCReg = (ushort)(BReg);
            BCReg = (ushort)(BCReg << 8);
            BCReg = (ushort)(BCReg + CReg);
        }

        private void UpdateDEFrom8BitReg()
        {
            DEReg = (ushort)(DReg);
            DEReg = (ushort)(DEReg << 8);
            DEReg = (ushort)(DEReg + EReg);
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

        private void NextInstruction()
        {
            byte opcode = AddressSpace[ProgCounterReg];
            switch (opcode)
            {
                case 0x00:
                    nop();
                    break;
                case 0x01:
                    ld16bc(FetchData16Bit());
                    break;
                case 0x02:
                    ldARegToBCPointer(FetchData16Bit());
                    break;
                case 0x03:
                    incBCReg();
                    break;
                case 0x04:
                    incBReg();
                    break;
                default:
                    Console.WriteLine(
                        "Unknown opcode 0x{0} occurred at: 0x{1}",
                        opcode.ToString("X", ci),
                        ProgCounterReg.ToString("X", ci)
                    );
                    break;
            }
        }

        private byte FetchData8Bit()
        {
            return AddressSpace[ProgCounterReg++];
        }

        private ushort FetchData16Bit()
        {
            ushort ret = AddressSpace[ProgCounterReg++];
            ret = (ushort)(ret << 8); // we shift the first 8 bits to the right
            ret = AddressSpace[ProgCounterReg++]; // this fits into the lower 8 bits
            return ret;
        }

        private void nop()
        {
            ProgCounterReg++;
        }

        private void ld16bc(ushort operand)
        {
            BCReg = operand;
            UpdateBCFrom16BitReg(); // B and C have synced contents
            ProgCounterReg++;
        }

        private void ldARegToBCPointer(ushort operand)
        {
            AddressSpace[BCReg] = AReg;
            UpdateBCFrom16BitReg();
            ProgCounterReg++;
        }

        private void incBCReg() // we don't check for carry here: the CPU couldn't care less.
        {
            BCReg++;
            UpdateBCFrom16BitReg();
            ProgCounterReg++;
        }

        private void incBReg()
        {
            if ((BReg + 1) > 0xFF)
            {
                BReg = 0;
                SetParityOverfState(0x01); // note to self: remind to take care of the carry.
                UpdateBCFrom8BitReg();
                ProgCounterReg++;
            }
            else
            {
                BReg++;
                UpdateBCFrom8BitReg();
                ProgCounterReg++;
            }
        }

        // these functions should get a status byte of which the LSB/leftmost bit needs to be set to enable the flag and unset to disable it.
        // also these random ass 0x?? bytes
        private void SetCarryFlagState(byte status)
        {
            FReg = (byte)(((byte)(FReg) & ~(byte)(0x01)) | (status & (byte)(0x01))); // WTF??? In theory this should set our carry flag tho.
            // https://stackoverflow.com/questions/127027/how-can-i-check-my-byte-flag-verifying-that-a-specific-bit-is-at-1-or-0#127062
            // https://stackoverflow.com/questions/4439078/how-do-you-set-only-certain-bits-of-a-byte-in-c-without-affecting-the-rest#4439221
            // I bet this is easier to do in silicon design.
        }

        private void SetAddSubFlagState(byte status)
        {
            FReg = (byte)(((byte)(FReg) & ~(byte)(0x02)) | (status & (byte)(0x02)));
            // setting this flag should not be needed, since we don't have to deal with dumb silicon but with smart silicon.
        }

        private void SetParityOverfState(byte status)
        {
            FReg = (byte)(((byte)(FReg) & ~(byte)(0x04)) | (status & (byte)(0x04)));
            // reminder to citizens: failure to understand usage of the parity/overflow flag for IO communication with IN and OUT instructions
            // is ground for immediate off-world relocation.
        }

        private void SetHalfCarryState(byte status)
        {
            FReg = (byte)(((byte)(FReg) & ~(byte)(0x10)) | (status & (byte)(0x10)));
            // setting this flag should NOT be needed as we don't have a 4 bit ALU on modern CPUs. Might be worth revisiting for accuracy.
        }

        private void SetZeroFlagState(byte status)
        {
            FReg = (byte)(((byte)(FReg) & ~(byte)(0x40)) | (status & (byte)(0x40)));
        }

        private void SetSignFlagState(byte status)
        {
            FReg = (byte)(((byte)(FReg) & ~(byte)(0x80)) | (status & (byte)(0x80)));
        }
    }
}
