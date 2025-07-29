using System.Collections;
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
        private byte[] AddressSpace = new byte[0x10000]; // this always reserves 64k of RAM. lol
        readonly CultureInfo ci = new("en-us");

        // TODO: create an assembler that can assemble Z80 machine code on the fly, so the user can type instructions and data and automatically have them written to memory.
        // Also, a way to load compiled Z80 programs from a file would be nice.

        /// <summary>
        /// Creates a new CPU object instance and starts it.
        /// </summary>
        public Z80_CPU()
        {
            Reset();
            Boolean exit = false;
            while (!exit)
            {
                Console.WriteLine("Press N to advance to the next instruction, I to insert data in the address space, J to jump to a memory location, R to check the registers, V to view current memory contents, and Q to quit. ");
                String s = Console.ReadLine()!; // we say the string can't be null
                char c = Char.Parse(s.ToLower());
                switch (c)
                {
                    case 'n':
                         NextInstruction();
                         break;
                    case 'i':
                        InsertDataInstrInAddrSpc();
                        break;
                    case 'v':
                        CheckMemoryState();
                        break;
                    case 'j':
                        Console.WriteLine("Insert the jump location: ");
                        ushort tempPC = 0xFFFF;
                        try
                        {
                            tempPC = (ushort)Int16.Parse(Console.ReadLine()); // oh god
                        }
                        catch (System.Exception)
                        {
                            Console.WriteLine("Error parsing tempPC.");
                        }
                        if (tempPC > 0xFFFF)
                            Console.WriteLine("Illegal address."); // this should never happen.
                        else
                            ProgCounterReg = tempPC;
                        break;
                    case 'r':
                        RegisterStatus();
                        break;
                    case 'q':
                        exit = true;
                        Console.WriteLine("Closing program.");
                        break;
                    default:
                        Console.WriteLine("Unknown command.");
                        break;
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
            for (int i = 0; i < 0x10000; i++) // 0x10000 is there to prevent an off by one error.
            {
                AddressSpace[i] = (byte)(0xFF);
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
            Console.WriteLine("registerpair 0x{0}, hireg 0x{1}, loreg {2}", registerPair.ToString("X", ci), upperReg.ToString("X"
            registerPair = (ushort)(upperReg); // the bits of the upper register go into the register pair
            Console.WriteLine("registerpair 0x{0}, hireg 0x{1}, loreg {2}", registerPair.ToString("X", ci), upperReg.ToString("X"
            registerPair = (ushort)(registerPair << 8); // the 8 bits of the upper register are shifted into the upper 16 bits of the register pair
            Console.WriteLine("registerpair 0x{0}, hireg 0x{1}, loreg {2}", registerPair.ToString("X", ci), upperReg.ToString("X"
            registerPair = (ushort)(registerPair + lowerReg); // and now we put in the contents of the lower register
        }

        /// <summary>
        /// Updates the value of the two 8 bit registers that make up a register pair.
        /// <param name="registerPair">The register pair to be updated.</param>
        /// <param name="upperReg">The 8-bit register that occupies the upper 8 bits of our register pair.</param>
        /// <param name="lowerReg">The 8-bit register that occupies the lower 8 bits of our register pair.</param>
        /// </summary>
        /// <remarks>Use only valid register pairs (AF, BC, DE, HL).</remarks>
        private static RegisterPair UpdateUnpairedRegFrom16Bit(ushort registerPair, byte upperReg, byte lowerReg)
        {
            lowerReg = (byte)(registerPair); // we simply discard the upper 16 bits
            upperReg = (byte)((registerPair - upperReg) >> 8); // the upper register is stored in the upper 8 bits of the register pair
            return new RegisterPair(registerPair, lowerReg, upperReg);
        }

        /// <summary>
        /// Automatically fetches and executes the next instruction found in the address space.
        /// </summary>
        private void NextInstruction()
        {
            if ((ProgCounterReg + 1) > 0x10000)
            {
                ProgCounterReg = 0x0000;
            }

            byte opcode = AddressSpace[ProgCounterReg];
            switch (opcode)
            {
                case 0x00:
                    Nop();
                    break;
                case 0x01:
                    Ld16bc(FetchData16Bit());
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
            ushort ret = 0x0000;
            byte lowerByte = AddressSpace[ProgCounterReg++];
            byte highByte = AddressSpace[ProgCounterReg++]; // this is LE so we have to fetch backwards
            ret = highByte;
            ret <<= 8; // we shift the first 8 bits to the left
            ret = (ushort)(ret & lowerByte); // we AND the bits together to pack our 2 bytes together
            return ret;
        }

        // Here start the instructions' implementations. Note that writing to the console is essential to the program not running too fast and overflowing PC immediately.

        /// <summary>
        /// No operation, only increases PC.
        /// </summary>
        private void Nop()
        {
            Console.WriteLine("nop");
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads a 16 bit value into register BC.
        /// <param name="operand">The 16 bit value to be loaded.</param>
        /// </summary>
        private void Ld16bc(ushort operand)
        {
            Console.WriteLine("ld bc, " + operand);
            BCReg = operand;
            RegisterPair p = UpdateUnpairedRegFrom16Bit(BCReg, BReg, CReg);
            BReg = p.GetHighByte();
            CReg = p.GetLowByte();
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
        }

        /// <summary>
        /// Increases the value of register B by 1.
        /// </summary>
        private void IncBReg()
        {
            Console.WriteLine("inc b");
            SetAddSubFlagState(false);
            if ((BReg + 1) > 0xFF)
            {
                BReg = 0;
                SetParityOverfState(true); // note to self: remind to take care of the carry.
                UpdatePairedRegFrom8Bit(BCReg, BReg, CReg);
                ProgCounterReg++;
            }
            else if ((BReg + 1) == 0)
            {
                BReg = 0;
                SetZeroFlagState(true);
                UpdateUnpairedRegFrom16Bit(BCReg, BReg, CReg);
                ProgCounterReg++;
            }
            else if ((BReg + 1) < 0){
                BReg++;
                SetSignFlagState(true);
                UpdatePairedRegFrom8Bit(BCReg, BReg, CReg);
                ProgCounterReg++;
            }
            else
            {
                BReg++;
                UpdatePairedRegFrom8Bit(BCReg, BReg, CReg);
                ProgCounterReg++;
            }
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
                SetParityOverfState(true); // yes
                UpdatePairedRegFrom8Bit(BCReg, BReg, CReg);
                ProgCounterReg++;
            }
            else if ((BReg - 1) == 0)
            {
                BReg = 0;
                SetZeroFlagState(true);
                UpdateUnpairedRegFrom16Bit(BCReg, BReg, CReg);
                ProgCounterReg++;
            }
            else if ((BReg - 1) < 0){
                BReg--;
                SetSignFlagState(true);
                UpdatePairedRegFrom8Bit(BCReg, BReg, CReg);
                ProgCounterReg++;
            }
            else
            {
                BReg--;
                UpdatePairedRegFrom8Bit(BCReg, BReg, CReg);
                ProgCounterReg++;
            }
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
        }

        /// <summary>
        /// Rotates left A register contents by one bit.
        /// <remark>https://stackoverflow.com/questions/4439078/how-do-you-set-only-certain-bits-of-a-byte-in-c-without-affecting-the-rest#4439221</remark>
        /// </summary>
        private void Rlca()
        {
            Console.WriteLine("rlca");
            AReg <<= 1; // left shift + assignment
            SetHalfCarryState(false);
            SetAddSubFlagState(false);
            if ((AReg & (1 << 0x80)) != 0) // we take the MSB/rightmost bit of the A register and we copy its value to the carry flag.
            {
                SetCarryFlagState(true);
                 // we have to set bit 0 corresponding to bit 7's value.
                // didn't the leftshift operation already do this?
            }
            else
            {
                SetCarryFlagState(false);
                // AReg = (byte)(((byte)(AReg) & ~(byte)(0x01)) | (0x00 & (byte)(0x01)));
            }
            ProgCounterReg++;
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
        }

        /// <summary>
        /// Adds the contents of the BC register to the contents of the HL register.
        /// </summary>
        private void AddHLBC()
        {
            Console.WriteLine("add hl, bc");
            SetAddSubFlagState(false);
            if ((HLReg + BCReg) > 0xFFFF) // this gets valued as an int and not as an ushort, because C#.
            {
                SetCarryFlagState(true);
                HLReg = 0;
            }
            else
            {
                HLReg = (ushort)(HLReg + BCReg);
            }
            ProgCounterReg++;
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


        // these functions should get a status byte of which the LSB/leftmost bit needs to be set to enable the flag and unset to disable it.
        private void SetCarryFlagState(bool toggle)
        {
            BitArray ba = new(FReg);
            ba[7] = toggle; // we set the last bit
            FReg = ConvertToByte(ba);
        }

        private void SetAddSubFlagState(bool toggle) // needed for BCD mode
        {
            BitArray ba = new(FReg);
            ba[6] = toggle;
            FReg = ConvertToByte(ba);
            // setting this flag should not be needed, since we don't have to deal with dumb silicon but with smart silicon.
        }

        private void SetParityOverfState(bool toggle)
        {
            BitArray ba = new(FReg);
            ba[5] = toggle;
            FReg = ConvertToByte(ba);
            // reminder to citizens: failure to understand usage of the parity/overflow flag for IO communication with IN and OUT instructions
            // is ground for immediate off-world relocation.
        }

        private void SetHalfCarryState(bool toggle)
        {
            BitArray ba = new(FReg);
            ba[3] = toggle;
            FReg = ConvertToByte(ba);
            // setting this flag should NOT be needed as we don't have a 4 bit ALU on modern CPUs. Might be worth revisiting for accuracy.
        }

        private void SetZeroFlagState(bool toggle)
        {
            BitArray ba = new(FReg);
            ba[1] = toggle;
            FReg = ConvertToByte(ba);
        }

        private void SetSignFlagState(bool toggle)
        {
            BitArray ba = new(FReg);
            ba[0] = toggle;
            FReg = ConvertToByte(ba);
        }

        private void ManageNMI() // "manage"
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

        /// <summary>
        /// This function lets the user see the contents of the address space.
        /// </summary>
        private void CheckMemoryState(){
            /*ushort altpc = 0;
            ushort start = 0;
            while (altpc < 0x0020){
                Console.WriteLine("addr: 0x{0}", altpc.ToString("X", ci));
                for (altpc = start; altpc < 0x0010; altpc++){
                    Console.Write("0x{0} ", AddressSpace[altpc].ToString("X", ci));
                    altpc++;
                }
                altpc++;
            //Console.WriteLine("\t{0} - {1}",start.ToString("X", ci),altpc.ToString("X", ci));
            // start = altpc;
            altpc++;
            }*/ // this doesn't work

            Console.WriteLine("addr: 0x{0}\t val: 0x{1}",ProgCounterReg.ToString("X", ci),AddressSpace[ProgCounterReg].ToString("X", ci));
            Console.WriteLine("addr: 0x{0}\t val: 0x{1}",ProgCounterReg+1.ToString("X", ci),AddressSpace[ProgCounterReg+1].ToString("X", ci));
            Console.WriteLine("addr: 0x{0}\t val: 0x{1}",ProgCounterReg+2.ToString("X", ci),AddressSpace[ProgCounterReg+2].ToString("X", ci));
            Console.WriteLine("addr: 0x{0}\t val: 0x{1}",ProgCounterReg+3.ToString("X", ci),AddressSpace[ProgCounterReg+3].ToString("X", ci));
            Console.WriteLine("addr: 0x{0}\t val: 0x{1}",ProgCounterReg+4.ToString("X", ci),AddressSpace[ProgCounterReg+4].ToString("X", ci));
            Console.WriteLine("addr: 0x{0}\t val: 0x{1}",ProgCounterReg+5.ToString("X", ci),AddressSpace[ProgCounterReg+5].ToString("X", ci));
            Console.WriteLine("addr: 0x{0}\t val: 0x{1}",ProgCounterReg+6.ToString("X", ci),AddressSpace[ProgCounterReg+6].ToString("X", ci));
            Console.WriteLine("addr: 0x{0}\t val: 0x{1}",ProgCounterReg+7.ToString("X", ci),AddressSpace[ProgCounterReg+7].ToString("X", ci));
        }

        /// <summary>
        /// Converts a System.Collections.BitArray to a byte, in a bad way I can understand.
        /// </summary>
        /// <remark> A poor man's solution. https://stackoverflow.com/questions/560123/convert-from-bitarray-to-byte </remark>
        private static byte ConvertToByte(BitArray bits)
        {
            if (bits.Count != 8)
            {
                throw new ArgumentException("illegal number of bits");
            }
            byte b = 0;
            if (bits.Get(7)) b++;
            if (bits.Get(6)) b += 2;
            if (bits.Get(5)) b += 4;
            if (bits.Get(4)) b += 8;
            if (bits.Get(3)) b += 16;
            if (bits.Get(2)) b += 32;
            if (bits.Get(1)) b += 64;
            if (bits.Get(0)) b += 128;
            return b;
        }

        private void InsertDataInstrInAddrSpc(){
            Console.WriteLine("Insert the address to insert the data: ");
                    String a = Console.ReadLine()!;
                    ushort addr = 0x0000;
                     try
                        {
                            addr = (ushort)Int16.Parse(a);
                            Console.WriteLine("Address: 0x{0}" + addr.ToString("X", ci));
                        }
                        catch (FormatException)
                        {
                            Console.WriteLine($"Unable to parse '{a}'");
                        }
                    if (addr > 0xFFFF){
                        Console.WriteLine("Illegal address.");
                    }
                    else{
                        Console.WriteLine("Insert a byte to be put in memory: ");
                        String d = Console.ReadLine()!;
                        byte data = 0x00;
                     try
                        {
                            data = byte.Parse(d);
                            Console.WriteLine("Data: 0x{0}", data.ToString("X", ci));
                        }
                        catch (FormatException)
                        {
                            Console.WriteLine($"Unable to parse '{d}'");
                        }
                    if (data > 0xFF){
                        Console.WriteLine("Please write one byte at a time.");
                    }
                    else{
                        AddressSpace[addr] = (byte)data;
                        Console.WriteLine("Inserted byte 0x{0} at location 0x{1}.", data.ToString("X", ci), addr.ToString("X", ci));
                    }

                    }
        }
    }
<<<<<<< HEAD
    
=======


    class RegisterPair(ushort regPair, byte lowByte, byte highByte)
    {
        private ushort regPair = regPair;
        private byte lowByte = lowByte;
        private byte highByte = highByte;

        public ushort GetRegPair()
        {
            return regPair;
        }

        public byte GetLowByte()
        {
            return lowByte;
        }

        public byte GetHighByte()
        {
            return highByte;
        }
    }

>>>>>>> 3e46f49 (ok this is better)
}
