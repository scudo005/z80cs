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
            ARegSec,
            BRegSec,
            CRegSec,
            DRegSec,
            ERegSec,
            FRegSec,
            HRegSec,
            LRegSec,
            MemRefreshReg,
            InterruptVectorReg,
            IODeviceAddress,
            IMF; // this register takes care of managing interrupt modes and must be managed with its 2 lower bits
        private ushort ProgCounterReg,
            IXIndexReg,
            IYIndexReg,
            StackPointerReg,
            AFReg,
            HLReg,
            BCReg,
            DEReg,
            AFRegSec,
            HLRegSec,
            BCRegSec,
            DERegSec;
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
            ARegSec = 0;
            BRegSec = 0;
            CRegSec = 0;
            DRegSec = 0;
            ERegSec = 0;
            FRegSec = 0;
            HRegSec = 0;
            LRegSec = 0;
            UpdatePairedRegFrom8Bit(AFReg, AReg, FReg);
            UpdatePairedRegFrom8Bit(BCReg, BReg, CReg);
            UpdatePairedRegFrom8Bit(DEReg, DReg, EReg);
            UpdatePairedRegFrom8Bit(HLReg, HReg, LReg);
            UpdatePairedRegFrom8Bit(AFRegSec, ARegSec, FRegSec);
            UpdatePairedRegFrom8Bit(BCRegSec, BRegSec, CRegSec);
            UpdatePairedRegFrom8Bit(DERegSec, DRegSec, ERegSec);
            UpdatePairedRegFrom8Bit(HLRegSec, HRegSec, LRegSec);
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

        // these update methods must be called every time A, F, H, L, D, E, B, C registers are used to not desync the state of AF, HL, DE, BC and vice versa
        // TODO: is this machine big endian or little endian?

        private void UpdatePairedRegFrom8Bit(ushort registerPair, byte upperReg, byte lowerReg)
        {
            registerPair = (ushort)(upperReg); // the bits of the upper Register go into the register pair
            registerPair = (ushort)(registerPair << 8); // the 8 bits of the upper register are shifted into the upper 16 bits of the register pair
            registerPair = (ushort)(registerPair + lowerReg); // and now we put in the contents of the lower register
        }

        private void UpdateUnpairedRegFrom16Bit(ushort registerPair, byte upperReg, byte lowerReg)
        {
            lowerReg = (byte)(registerPair); // we simply discard the upper 16 bits
            upperReg = (byte)(registerPair >> 8); // the upper register is stored in the upper 8 bits of the register pair
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
                case 0x05:
                    decBReg();
                    break;
                case 0x06:
                    ldBRegConst(FetchData8Bit());
                    break;
                case 0x07:
                    rlca();
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
            ret = (ushort)(ret << 8); // we shift the first 8 bits to the right // can't we just say ret << 8 instead of reassigning the value?
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
            UpdateUnpairedRegFrom16Bit(BCReg, BReg, CReg); // B and C have synced contents
            ProgCounterReg++;
        }

        private void ldARegToBCPointer(ushort operand)
        {
            AddressSpace[BCReg] = AReg;
            UpdateUnpairedRegFrom16Bit(BCReg, BReg, CReg);
            ProgCounterReg++;
        }

        private void incBCReg() // we don't check for carry here: the CPU couldn't care less.
        {
            BCReg++;
            UpdateUnpairedRegFrom16Bit(BCReg, BReg, CReg);
            ProgCounterReg++;
        }

        private void incBReg()
        {
            if ((BReg + 1) > 0xFF)
            {
                BReg = 0;
                SetParityOverfState(0x01); // note to self: remind to take care of the carry.
                UpdatePairedRegFrom8Bit(BCReg, BReg, CReg);
                ProgCounterReg++;
            }
            else if ((BReg + 1) == 0)
            {
                BReg = 0;
                SetZeroFlagState(0x01);
                UpdateUnpairedRegFrom16Bit(BCReg, BReg, CReg);
                ProgCounterReg++;
            }
            else
            {
                BReg++;
                UpdatePairedRegFrom8Bit(BCReg, BReg, CReg);
                ProgCounterReg++;
            }
        }

        private void decBReg()
        {
            if ((BReg - 1) > 0xFF)
            {
                BReg = 0;
                SetParityOverfState(0x01); // yes
                UpdatePairedRegFrom8Bit(BCReg, BReg, CReg);
                ProgCounterReg++;
            }
            else if ((BReg - 1) == 0)
            {
                BReg = 0;
                SetZeroFlagState(0x01);
                UpdateUnpairedRegFrom16Bit(BCReg, BReg, CReg);
                ProgCounterReg++;
            }
            else
            {
                BReg--;
                UpdatePairedRegFrom8Bit(BCReg, BReg, CReg);
                ProgCounterReg++;
            }
        }

        private void ldBRegConst(byte operand)
        {
            BReg = operand;
            UpdatePairedRegFrom8Bit(BCReg, BReg, CReg);
            ProgCounterReg++;
        }

        private void rlca()
        { // rotate left A Register contents by one bit
            AReg = (byte)(AReg << 1);
            SetHalfCarryState(0x00);
            SetAddSubFlagState(0x00);
            // https://stackoverflow.com/questions/4439078/how-do-you-set-only-certain-bits-of-a-byte-in-c-without-affecting-the-rest#4439221
            if ((AReg & (1 << 0x80)) != 0) // we take the MSB/rightmost bit of the A register and we copy its value to the carry flag.
            {
                SetCarryFlagState(0x01);
                // AReg = (byte)(((byte)(AReg) & ~(byte)(0x01)) | (0x01 & (byte)(0x01))); // we set bit 0 corresponding to bit 7's value.
                // didn't the leftshift operation already do this?
            }
            else
            {
                SetCarryFlagState(0x00);
                // AReg = (byte)(((byte)(AReg) & ~(byte)(0x01)) | (0x00 & (byte)(0x01)));
            }
            ProgCounterReg++;
        }

        // these functions should get a status byte of which the LSB/leftmost bit needs to be set to enable the flag and unset to disable it.
        // also these random ass 0x?? bytes represent the bitmask of the several set flags
        private void SetCarryFlagState(byte status)
        {
            FReg = (byte)(((byte)(FReg) & ~(byte)(0x01)) | (status & (byte)(0x01))); // WTF??? In theory this should set our carry flag tho.
            // https://stackoverflow.com/questions/127027/how-can-i-check-my-byte-flag-verifying-that-a-specific-bit-is-at-1-or-0#127062
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

        private void ManageNMI()
        {
            ProgCounterReg = NMIVector;
        }
    }
}
