using System;
using System.Globalization;

namespace z80cs
{
    internal class Z80_CPU
    {
        static void Main()
        {
            _ = new Z80_CPU();
            // we need to init memory...
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
            IMF;

        /// this register takes care of managing interrupt modes and must be managed with its 2 lower bits
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

        /// <summary>
        /// Creates a new CPU object instance and starts it.
        /// </summary>
        public Z80_CPU()
        {
            Reset();
            while (true)
            {
                if (!NMITrigger) // TODO: add some form of IRQ management.
                    NextInstruction();
                else
                {
                    ManageNMI();
                    NextInstruction();
                }
            }
        }

        /// <summary>
        /// Initializes all of the registers and clears RAM.
        /// </summary>
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
            for (int i = 0; i < 0xFFFF; i++)
            {
                AddressSpace[i] = (byte)(0x02);
            }
        }

        // these update methods must be called every time A, F, H, L, D, E, B, C registers are used to not desync the state of AF, HL, DE, BC and vice versa
        // TODO: this machine is little endian, invert bytes

        /// <summary>
        /// Updates the value of the paired register based on the status of the two register that make it up.
        /// <param name="registerPair">The register pair to be updated.</param>
        /// <param name="upperReg">The 8-bit register that occupies the upper 8 bits of our register pair.</param>
        /// <param name="lowerReg">The 8-bit register that occupies the lower 8 bits of our register pair.</param>
        /// </summary>
        /// <remarks>Use only valid register pairs (AF, BC, DE, HL).</remarks>
        private void UpdatePairedRegFrom8Bit(ushort registerPair, byte upperReg, byte lowerReg)
        {
            registerPair = (ushort)(upperReg); // the bits of the upper register go into the register pair
            registerPair = (ushort)(registerPair << 8); // the 8 bits of the upper register are shifted into the upper 16 bits of the register pair
            registerPair = (ushort)(registerPair + lowerReg); // and now we put in the contents of the lower register
        }

        /// <summary>
        /// Updates the value of the two 8 bit registers that make up a register pair.
        /// <param name="registerPair">The register pair to be updated.</param>
        /// <param name="upperReg">The 8-bit register that occupies the upper 8 bits of our register pair.</param>
        /// <param name="lowerReg">The 8-bit register that occupies the lower 8 bits of our register pair.</param>
        /// </summary>
        /// <remarks>Use only valid register pairs (AF, BC, DE, HL).</remarks>
        private void UpdateUnpairedRegFrom16Bit(ushort registerPair, byte upperReg, byte lowerReg)
        {
            lowerReg = (byte)(registerPair); // we simply discard the upper 16 bits
            upperReg = (byte)(registerPair >> 8); // the upper register is stored in the upper 8 bits of the register pair
        }

        /// <summary>
        /// Automatically fetches and executes the next instruction found in the address space.
        /// </summary>
        private void NextInstruction()
        {
            if ((ProgCounterReg + 1) > 0xFFFF)
            {
                Console.WriteLine("overflow pc");
                ProgCounterReg = 0x0000;
                // Environment.Exit(-1);
            }

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
                    LdARegToBCPointer(FetchData16Bit());
                    break;
                case 0x03:
                    IncBCReg();
                    break;
                case 0x04:
                    IncBReg();
                    break;
                case 0x05:
                    DecBReg();
                    break;
                case 0x06:
                    LdBRegConst(FetchData8Bit());
                    break;
                case 0x07:
                    Rlca();
                    break;
                case 0x08:
                    ExchangeAFWithAFSec();
                    break;
                case 0x09:
                    AddHLBC();
                    break;
                case 0x0A:
                    LdARegFromBCPointer();
                    break;
                default:
                    Console.WriteLine(
                        "Unknown opcode 0x{0} occurred at: 0x{1}",
                        opcode.ToString("X", ci),
                        ProgCounterReg.ToString("X", ci)
                    );
                    ProgCounterReg++; // for now, ignore unknown opcodes like 3DS Virtual Console. Maybe call halt instruction when implemented.
                    break;
            }
        }

        /// <summary>
        /// Fetches the next byte from the address space to be used as data.
        /// <returns>A byte of data.</returns>
        /// </summary>
        private byte FetchData8Bit()
        {
            return AddressSpace[ProgCounterReg++];
        }

        /// <summary>
        /// Fetches the next 2 bytes from the address space to be used as data.
        /// <returns>2 bytes of data packed as a 16 bit value.</returns>
        /// </summary>
        private ushort FetchData16Bit()
        {
            ushort ret = AddressSpace[ProgCounterReg++];
            ret = (ushort)(ret << 8); // we shift the first 8 bits to the right // can't we just say ret << 8 instead of reassigning the value?
            ret = AddressSpace[ProgCounterReg++]; // this fits into the lower 8 bits
            return ret;
        }

        /// Here start the instructions' implementations. Note that writing to the console is essential to the program not running too fast and overflowing PC immediately.

        /// <summary>
        /// No operation, only increases PC.
        /// </summary>
        private void nop()
        {
            Console.WriteLine("nop");
            ProgCounterReg++;
            RegisterStatus();
        }

        /// <summary>
        /// Loads a 16 bit value into register BC.
        /// <param name="operand">The 16 bit value to be loaded.</param>
        /// </summary>
        private void ld16bc(ushort operand)
        {
            Console.WriteLine("ld bc, " + operand);
            BCReg = operand;
            UpdateUnpairedRegFrom16Bit(BCReg, BReg, CReg); // B and C have synced contents
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads an 8 bit value into register A, found at the location pointed by the operand.
        /// <param name="operand">The 16 bit pointer to the desidered value.</param>
        /// </summary>
        private void LdARegToBCPointer(ushort operand)
        {
            Console.WriteLine("ld a, [" + operand + "]");
            AddressSpace[operand] = AReg;
            Console.WriteLine("[bc] is now " + AddressSpace[BCReg] + " with bc = " + BCReg);
            UpdateUnpairedRegFrom16Bit(BCReg, BReg, CReg);
            ProgCounterReg++;
            RegisterStatus();
        }

        /// <summary>
        /// Increases the value of register BC by 1.
        /// </summary>
        private void IncBCReg() // we don't check for carry here: the CPU couldn't care less.
        {
            Console.WriteLine("inc bc");
            BCReg++;
            UpdateUnpairedRegFrom16Bit(BCReg, BReg, CReg);
            ProgCounterReg++;
            RegisterStatus();
        }

        /// <summary>
        /// Increases the value of register B by 1.
        /// </summary>
        private void IncBReg()
        {
            Console.WriteLine("inc b");
            SetAddSubFlagState(0x00);
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
            else if ((BReg + 1) < 0){
                BReg++;
                SetSignFlagState(0x01);
                UpdatePairedRegFrom8Bit(BCReg, BReg, CReg);
                ProgCounterReg++;
            }
            else
            {
                BReg++;
                UpdatePairedRegFrom8Bit(BCReg, BReg, CReg);
                ProgCounterReg++;
            }
            RegisterStatus();
        }

        /// <summary>
        /// Decreases the value of register B by 1.
        /// </summary>
        private void DecBReg()
        {
            Console.WriteLine("dec b");
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
            else if ((BReg - 1) < 0){
                BReg--;
                SetSignFlagState(0x01);
                UpdatePairedRegFrom8Bit(BCReg, BReg, CReg);
                ProgCounterReg++;
            }
            else
            {
                BReg--;
                UpdatePairedRegFrom8Bit(BCReg, BReg, CReg);
                ProgCounterReg++;
            }
            RegisterStatus();
        }

        /// <summary>
        /// Loads an 8 bit value into register B.
        /// <param name="operand">The 8 bit value to be loaded.</param>
        /// </summary>
        private void LdBRegConst(byte operand)
        {
            Console.WriteLine("ld b, " + operand);
            BReg = operand;
            UpdatePairedRegFrom8Bit(BCReg, BReg, CReg);
            ProgCounterReg++;
            RegisterStatus();
        }

        /// <summary>
        /// Rotates left A register contents by one bit.
        /// <remark>https://stackoverflow.com/questions/4439078/how-do-you-set-only-certain-bits-of-a-byte-in-c-without-affecting-the-rest#4439221</remark>
        /// </summary>
        private void Rlca()
        {
            Console.WriteLine("rlca");
            AReg = (byte)(AReg << 1);
            SetHalfCarryState(0x00);
            SetAddSubFlagState(0x00);
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
            RegisterStatus();
        }

        /// <summary>
        /// Exchanges the values of the AF and AF' registers.
        /// </summary>
        private void ExchangeAFWithAFSec()
        {
            Console.WriteLine("ex af, af'");
            ushort AFCopy = AFReg;
            AFReg = AFRegSec;
            AFRegSec = AFCopy;
            // (AFRegSec, AFReg) = (AFReg, AFRegSec); // VS Code suggests we do this: I say it's incomprehensible.
            ProgCounterReg++;
            RegisterStatus();
        }

        /// <summary>
        /// Adds the contents of the BC register to the contents of the HL register.
        /// </summary>
        private void AddHLBC()
        {
            Console.WriteLine("add hl, bc");
            SetAddSubFlagState(0x00);
            if ((HLReg + BCReg) > 0xFFFF) // this gets valued as an int and not as an ushort, because C#.
            {
                SetCarryFlagState(0x01);
                HLReg = 0;
            }
            else
            {
                HLReg = (ushort)(HLReg + BCReg);
            }

            ProgCounterReg++;
            RegisterStatus();
        }

        // these functions should get a status byte of which the LSB/leftmost bit needs to be set to enable the flag and unset to disable it.
        // also these random ass 0x?? bytes represent the bitmask of the several set flags
        private void SetCarryFlagState(byte status)
        {
            FReg = (byte)(((byte)(FReg) & ~(byte)(0x01)) | (status & (byte)(0x01))); // WTF??? In theory this should set our carry flag tho.
            // https://stackoverflow.com/questions/127027/how-can-i-check-my-byte-flag-verifying-that-a-specific-bit-is-at-1-or-0#127062
            // I bet this is easier to do in silicon design.
        }

        // ld a, (bc)
        /// <summary>
        /// Loads the A register with the memory contents pointed by register BC.
        /// </summary>

        private void LdARegFromBCPointer()
        {
            Console.WriteLine("ld a, [" + BCReg + "]");
            AReg = AddressSpace[BCReg];
            ProgCounterReg++;
        }

        private void SetAddSubFlagState(byte status) // needed for BCD mode
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

        private void RegisterStatus()
        {
            Console.Write("a: " + AReg);
            Console.Write(" b: " + BReg);
            Console.Write(" c: " + CReg);
            Console.Write(" d: " + DReg);
            Console.Write(" e: " + EReg);
            Console.Write(" f: " + FReg);
            Console.Write(" h: " + HReg);
            Console.WriteLine(" l: " + LReg);
            Console.Write("af: " + AFReg);
            Console.Write(" bc: " + BCReg);
            Console.Write(" de: " + DEReg);
            Console.WriteLine(" hl: " + HLReg);
            /*Console.Write("a': " + ARegSec);
            Console.Write(" b': " + BRegSec);
            Console.Write(" c': " + CRegSec);
            Console.Write(" d': " + DRegSec);
            Console.Write(" e': " + ERegSec);
            Console.Write(" f': " + FRegSec);
            Console.Write(" h': " + HRegSec);
            Console.WriteLine(" l': " + LRegSec);*/
            Console.WriteLine("pc: " + ProgCounterReg);
        }

        private void CheckMemoryState(){
            ushort altpc = 0;
            ushort start = 0;
            while (altpc < 0x0200){
                for (altpc = start; altpc < 0x0020; altpc++){
                Console.Write(AddressSpace[altpc] + " ");
            }
            Console.WriteLine("\t{0} - {1}",start.ToString("X", ci),altpc.ToString("X", ci));
            start = altpc;
            }
            
            
        }
    }
}
