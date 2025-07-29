using System.Globalization;
using System.Collections;
// ReSharper disable InconsistentNaming
// ReSharper disable SuggestVarOrType_SimpleTypes
// ReSharper disable SuggestVarOrType_BuiltInTypes
#pragma warning disable CS0414 // Field is assigned but its value is never used

namespace z80cs
{
    class Processor
    {
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

        private bool NMITrigger,
            IFF1, // IFF1 is for disabling maskable interrupts: if it's 1, IRQs are enabled.
            IFF2;

        private readonly bool quiet;
        private bool haltFlag;
        private const ushort NMIVector = 0x0066;
        private readonly byte[] AddressSpace = new byte[0x10000]; // this always reserves 64k of RAM. lol
        private readonly CultureInfo ci = new("en-us");

        // TODO: create an assembler that can assemble Z80 machine code on the fly, so the user can type instructions and data and automatically have them written to memory.
        // Also, a way to load compiled Z80 programs from a file would be nice.
        // Not needed because of test suite.

        /// <summary>
        /// Creates a new CPU object instance and starts it.
        /// </summary>
        public Processor()
        {
            Reset();
            haltFlag = false;
            bool exit = false;
            quiet = false;
            while (!exit && !haltFlag)
            {
                switch (quiet)
                {
                    case false:
                        Console.WriteLine("Press N to advance to the next instruction, I to insert data in the address space, J to jump to a memory location, R to check the registers, V to view current memory contents, and Q to quit. ");
                        break;
                }
                string s = Console.ReadLine()!; // we say the string can't be null
                char c = 'a';
                try
                {
                    c = char.Parse(s.ToLower());
                }
                catch (NullReferenceException)
                {
                    Console.WriteLine("Error: insert a quit command into your testing script.");
                    Environment.Exit(1);
                }
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
                        switch (quiet)
                        {
                            case false:
                                Console.WriteLine("Insert the jump location: ");
                                break;
                        }
                        ushort tempPc = 0xFFFF;
                        try
                        {
                            tempPc = ushort.Parse(Console.ReadLine()!);
                        }
                        catch (FormatException e)
                        {
                            switch (quiet)
                            {
                                case false:
                                    Console.WriteLine("Error parsing tempPC.");
                                    break;
                            }
                            Console.WriteLine(e.ToString());
                        }
                        ProgCounterReg = tempPc;
                        break;
                    case 'r':
                        RegisterStatus();
                        break;
                    case 'q':
                        exit = true;
                        Console.WriteLine("Closing program.");
                        break;
                    case 's':
                        quiet = true;
                        break;
                    default:
                        Console.WriteLine("Unknown command.");
                        break;
                }
            }
            if (haltFlag)
                Console.WriteLine("CPU has halted.");
        }

        /// <summary>
        /// Initializes all the registers and clears RAM.
        /// </summary>
        private void Reset()
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
            AFReg = 0;
            BCReg = 0;
            DEReg = 0;
            HLReg = 0;
            AFRegSec = 0;
            BCRegSec = 0;
            DERegSec = 0;
            HLRegSec = 0;
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
                AddressSpace[i] = 0xFF;
            }
        }

        // these update methods must be called every time A, F, H, L, D, E, B, C registers are used to not desync the state of AF, HL, DE, BC and vice versa
        // TODO: this machine is little endian, invert bytes

        /// <summary>
        /// Updates the value of the paired register based on the status of the two register that make it up.
        /// <param name="upperReg">The 8-bit register that occupies the upper 8 bits of our register pair.</param>
        /// <param name="lowerReg">The 8-bit register that occupies the lower 8 bits of our register pair.</param>
        /// </summary>
        /// <remarks>Use only valid register pairs (AF, BC, DE, HL).</remarks>
        private static RegisterPair UpdatePairedRegFrom8Bit(byte upperReg, byte lowerReg)
        {
            ushort registerPair = upperReg; // the bits of the upper register go into the register pair
            registerPair = (ushort)(registerPair << 8); // the 8 bits of the upper register are shifted into the upper 16 bits of the register pair
            registerPair = (ushort)(registerPair + lowerReg); // and now we put in the contents of the lower register
            return new RegisterPair(registerPair, lowerReg, upperReg);
        }

        /// <summary>
        /// Updates the value of the two 8 bit registers that make up a register pair.
        /// <param name="registerPair">The register pair to be updated.</param>
        /// <param name="upperReg">The 8-bit register that occupies the upper 8 bits of our register pair.</param>
        /// </summary>
        /// <remarks>Use only valid register pairs (AF, BC, DE, HL).</remarks>
        private static RegisterPair UpdateUnpairedRegFrom16Bit(ushort registerPair, byte upperReg)
        {
            byte lowerReg = (byte)registerPair; // we simply discard the upper 16 bits
            upperReg = (byte)((registerPair - upperReg) >> 8); // the upper register is stored in the upper 8 bits of the register pair
            return new RegisterPair(registerPair, lowerReg, upperReg);
        }

        /// <summary>
        /// Creates a little-endian 16-bit number from 2 8-bit numbers.
        /// </summary>
        /// <param name="lowByte">The low byte.</param>
        /// <param name="highByte">The high byte.</param>
        /// <returns>A LE 16-bit number.</returns>
        /// <remarks>Remember, again, that this is a little-endian number: it's 0x02 0x01, NOT 0x01 0x02 to represent 0x0102.</remarks>
        private static ushort Create16BitLENumberFrom8Bit(byte lowByte, byte highByte)
        {
            ushort ret = highByte;
            ret = (ushort)(ret << 8);
            ret = (ushort)(ret + lowByte);
            return ret;
        }

        /// <summary>
        /// Automatically fetches and executes the next instruction found in the address space.
        /// </summary>
        private void NextInstruction()
        {
            ProgCounterReg = (ProgCounterReg + 1) switch
            {
                > 0x10000 => 0x0000,
                _ => ProgCounterReg // EH
            };

            byte opcode = AddressSpace[ProgCounterReg];
            switch (opcode)
            {
                case 0x00:
                    Nop();
                    break;
                case 0x01:
                    Ld16BC(FetchData16Bit());
                    break;
                case 0x02:
                    LdBCPointerWithARegContents();
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
                    ExchangeAfWithAfSec();
                    break;
                case 0x09:
                    AddHLBC();
                    break;
                case 0x0A:
                    LdARegFromBCPointer();
                    break;
                case 0x0B:
                    DecBC();
                    break;
                case 0x0C:
                    IncCReg();
                    break;
                case 0x0D:
                    DecCReg();
                    break;
                case 0x0E:
                    LdCConst(FetchData8Bit());
                    break;
                case 0x0F:
                    Rrca();
                    break;
                case 0x10:
                    DjnzConst((sbyte)FetchData8Bit()); // this looks awful...
                    break;
                case 0x11:
                    LdDEConst(FetchData16Bit());
                    break;
                case 0x12:
                    LdDEPointerWithARegContents();
                    break;
                case 0x13:
                    IncDEReg();
                    break;
                case 0x14:
                    IncDReg();
                    break;
                case 0x15:
                    DecDReg();
                    break;
                case 0x16:
                    LdDRegConst(FetchData8Bit());
                    break;
                case 0x17:
                    Rla();
                    break;
                case 0x18:
                    JrConst((sbyte)FetchData8Bit());
                    break;
                case 0x19:
                    AddHLDE();
                    break;
                case 0x1A:
                    LdARegFromDEPointer();
                    break;
                case 0x1B:
                    DecDE();
                    break;
                case 0x1C:
                    IncEReg();
                    break;
                case 0x1D:
                    DecEReg();
                    break;
                case 0x1E:
                    LdEConst(FetchData8Bit());
                    break;
                case 0x1F:
                    Rra();
                    break;
                case 0x20:
                    JrNz((sbyte)FetchData8Bit());
                    break;
                case 0x21:
                    LdHLConst(FetchData16Bit());
                    break;
                case 0x22:
                    Ld16BPointerInHLReg(FetchData16Bit());
                    break;
                case 0x23:
                    IncHLReg();
                    break;
                case 0x24:
                    IncHReg();
                    break;
                case 0x25:
                    DecHReg();
                    break;
                case 0x26:
                    LdHRegConst(FetchData8Bit());
                    break;
                case 0x28:
                    JrZ((sbyte)FetchData8Bit());
                    break;
                case 0x29:
                    AddHLHL();
                    break;
                case 0x2A:
                    LdHLWithPtr(FetchData16Bit());
                    break;
                case 0x2B:
                    DecHL();
                    break;
                case 0x2C:
                    IncLReg();
                    break;
                case 0x2D:
                    DecLReg();
                    break;
                case 0x2E:
                    LdLConst(FetchData8Bit());
                    break;
                case 0x30:
                    JrNc((sbyte)FetchData8Bit());
                    break;
                case 0x31:
                    LdSPConst(FetchData16Bit());
                    break;
                case 0x32:
                    LdPtrWithARegContents(FetchData16Bit());
                    break;
                case 0x33:
                    IncSp();
                    break;
                case 0x34:
                    IncHLRegFromPtr();
                    break;
                case 0x35:
                    DecHLRegFromPtr();
                    break;
                case 0x36:
                    LdHLPointerWithConst(FetchData8Bit());
                    break;
                case 0x37:
                    Scf();
                    break;
                case 0x38:
                    JrC((sbyte)FetchData8Bit());
                    break;
                case 0x39:
                    AddHLSP();
                    break;
                case 0x3A:
                    LdARegFrom16BitPointer(FetchData16Bit());
                    break;
                case 0x3B:
                    DecSP();
                    break;
                case 0x3C:
                    IncAReg();
                    break;
                case 0x3D:
                    DecAReg();
                    break;
                case 0x3E:
                    LdAConst(FetchData8Bit());
                    break;
                case 0x3F:
                    Ccf();
                    break;
                case 0x40:
                    LdBB();
                    break;
                case 0x41:
                    LdBC();
                    break;
                case 0x42:
                    LdBD();
                    break;
                case 0x43:
                    LdBE();
                    break;
                case 0x44:
                    LdBH();
                    break;
                case 0x45:
                    LdBL();
                    break;
                case 0x46:
                    LdBRegFromHLPointer();
                    break;
                case 0x47:
                    LdBA();
                    break;
                case 0x48:
                    LdCB();
                    break;
                case 0x49:
                    LdCC();
                    break;
                case 0x4A:
                    LdCD();
                    break;
                case 0x4B:
                    LdCE();
                    break;
                case 0x4C:
                    LdCH();
                    break;
                case 0x4D:
                    LdCL();
                    break;
                case 0x4E:
                    LdCRegFromHLPointer();
                    break;
                case 0x4F:
                    LdCA();
                    break;
                case 0x50:
                    LdDB();
                    break;
                case 0x51:
                    LdDC();
                    break;
                case 0x52:
                    LdDD();
                    break;
                case 0x53:
                    LdDE();
                    break;
                case 0x54:
                    LdDH();
                    break;
                case 0x55:
                    LdDL();
                    break;
                case 0x56:
                    LdDRegFromHLPointer();
                    break;
                case 0x57:
                    LdDA();
                    break;
                case 0x58:
                    LdEB();
                    break;
                case 0x59:
                    LdEC();
                    break;
                case 0x5A:
                    LdED();
                    break;
                case 0x5B:
                    LdEE();
                    break;
                case 0x5C:
                    LdEH();
                    break;
                case 0x5D:
                    LdEL();
                    break;
                case 0x5E:
                    LdERegFromHLPointer();
                    break;
                case 0x5F:
                    LdEA();
                    break;
                case 0x60:
                    LdHB();
                    break;
                case 0x61:
                    LdHC();
                    break;
                case 0x62:
                    LdHD();
                    break;
                case 0x63:
                    LdHE();
                    break;
                case 0x64:
                    LdHH();
                    break;
                case 0x65:
                    LdHL();
                    break;
                case 0x66:
                    LdHRegFromHLPointer();
                    break;
                case 0x67:
                    LdHA();
                    break;
                case 0x68:
                    LdLB();
                    break;
                case 0x69:
                    LdLC();
                    break;
                case 0x6A:
                    LdLD();
                    break;
                case 0x6B:
                    LdLE();
                    break;
                case 0x6C:
                    LdLH();
                    break;
                case 0x6D:
                    LdLL();
                    break;
                case 0x6E:
                    LdLRegFromHLPointer();
                    break;
                case 0x6F:
                    LdLA();
                    break;
                case 0x70:
                    LdHLPointerWithBReg();
                    break;
                case 0x71:
                    LdHLPointerWithCReg();
                    break;
                case 0x72:
                    LdHLPointerWithDReg();
                    break;
                case 0x73:
                    LdHLPointerWithEReg();
                    break;
                case 0x74:
                    LdHLPointerWithHReg();
                    break;
                case 0x75:
                    LdHLPointerWithLReg();
                    break;
                case 0x76:
                    Halt();
                    break;
                case 0x77:
                    LdHLPointerWithAReg();
                    break;
                case 0x78:
                    LdAB();
                    break;
                case 0x79:
                    LdAC();
                    break;
                case 0x7A:
                    LdAD();
                    break;
                case 0x7B:
                    LdAE();
                    break;
                case 0x7C:
                    LdAH();
                    break;
                case 0x7D:
                    LdAL();
                    break;
                case 0x7E:
                    LdARegFromHLPointer();
                    break;
                case 0x7F:
                    LdAA();
                    break;
                case 0x80:
                    AddAB();
                    break;
                case 0x81:
                    AddAC();
                    break;
                case 0x82:
                    AddAD();
                    break;
                case 0x83:
                    AddAE();
                    break;
                case 0x84:
                    AddAH();
                    break;
                case 0x85:
                    AddAL();
                    break;
                case 0x86:
                    AddARegFromHLPointer();
                    break;
                case 0x87:
                    AddAA();
                    break;
                default:
                    Console.WriteLine(
                        "Unknown opcode 0x{0} occurred at: 0x{1}",
                        opcode.ToString("X", ci),
                        ProgCounterReg.ToString("X", ci)
                    );
                    //ProgCounterReg++; // for now, ignore unknown opcodes like 3DS Virtual Console. Maybe call halt instruction when implemented.
                    haltFlag = true;
                    Console.WriteLine("The CPU halted due to an unknown opcode.");
                    break;
            }
        }

        /// <summary>
        /// Fetches the next byte from the address space to be used as data.
        /// <returns>A byte of data.</returns>
        /// </summary>
        private byte FetchData8Bit()
        {
            return AddressSpace[++ProgCounterReg];
        }

        /// <summary>
        /// Fetches the next 2 bytes from the address space to be used as data.
        /// <returns>2 bytes of data packed as a 16 bit value.</returns>
        /// </summary>
        private ushort FetchData16Bit()
        {
            byte lowerByte = AddressSpace[++ProgCounterReg];
            byte highByte = AddressSpace[++ProgCounterReg]; // this is LE so we have to fetch backwards
            ushort ret = highByte;
            ret <<= 8; // we shift the first 8 bits to the left
            ret = (ushort)(ret + lowerByte);
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
        private void Ld16BC(ushort operand)
        {
            Console.WriteLine("ld bc, {0}", operand.ToString("X", ci));
            BCReg = operand;
            RegisterPair p = UpdateUnpairedRegFrom16Bit(BCReg, BReg);
            BReg = p.GetHighByte();
            CReg = p.GetLowByte();
            ProgCounterReg++;
        }

        /// <summary>
        /// Stores the A register at the pointer pointed by register BC.
        /// </summary>

        private void LdBCPointerWithARegContents()
        {
            Console.WriteLine("ld a, ({0})", BCReg.ToString("X", ci));
            AddressSpace[BCReg] = AReg;
            RegisterPair p = UpdatePairedRegFrom8Bit(AReg, FReg);
            AFReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Stores the operand at the pointer pointed by register HL.
        /// <param name="operand">The operand.</param>
        /// </summary>

        private void LdHLPointerWithConst(byte operand)
        {
            Console.WriteLine("ld (hl), {0}", operand.ToString("X", ci));
            AddressSpace[HLReg] = operand;
            RegisterPair p = UpdateUnpairedRegFrom16Bit(HLReg, HReg);
            HReg = p.GetHighByte();
            LReg = p.GetLowByte();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads an 8 bit value into register A, found at the location pointed by the operand.
        /// <param name="operand">The 16 bit pointer to the desired value.</param>
        /// </summary>
        private void LdARegFrom16BitPointer(ushort operand)
        {
            Console.WriteLine("ld a, ({0})", operand.ToString("X", ci));
            AReg = AddressSpace[operand];
            Console.WriteLine("[bc] is now {0} with bc = {1}", AddressSpace[BCReg].ToString("X", ci), BCReg.ToString("X", ci));
            RegisterPair p = UpdateUnpairedRegFrom16Bit(BCReg, BReg);
            BReg = p.GetHighByte();
            CReg = p.GetLowByte();
            ProgCounterReg++;
        }

        /// <summary>
        /// Increases the value of register BC by 1.
        /// </summary>
        private void IncBCReg() // we don't check for carry here: the CPU couldn't care less.
        {
            Console.WriteLine("inc bc\n");
            BCReg++;
            RegisterPair p = UpdateUnpairedRegFrom16Bit(BCReg, BReg);
            BReg = p.GetHighByte();
            CReg = p.GetLowByte();
            ProgCounterReg++;
        }

        /// <summary>
        /// Increases the value of register B by 1.
        /// </summary>
        private void IncBReg()
        {
            Console.WriteLine("inc b\n");
            FReg = SetAddSubFlagState(false);
            switch (BReg + 1)
            {
                case > 0xFF:
                    {
                        BReg = 0;
                        SetParityOverflowState(true); // note to self: remind to take care of the carry.
                        RegisterPair p = UpdatePairedRegFrom8Bit(BReg, CReg);
                        BCReg = p.GetRegPair();
                        break;
                    }
                default:
                    switch (BReg + 1) // yo
                    {
                        case 0x100:
                            {
                                BReg = 0;
                                FReg = SetZeroFlagState(true);
                                RegisterPair g = UpdatePairedRegFrom8Bit(AReg, FReg);
                                AFReg = g.GetRegPair();
                                RegisterPair p = UpdatePairedRegFrom8Bit(BReg, CReg);
                                BCReg = p.GetRegPair();
                                break;
                            }
                        case < 0:
                            {
                                BReg++;
                                FReg = SetSignFlagState(true);
                                RegisterPair p = UpdatePairedRegFrom8Bit(AReg, FReg);
                                AFReg = p.GetRegPair();
                                RegisterPair g = UpdatePairedRegFrom8Bit(BReg, CReg);
                                BCReg = g.GetRegPair();
                                break;
                            }
                        default:
                            {
                                BReg++;
                                RegisterPair p = UpdatePairedRegFrom8Bit(BReg, CReg);
                                BCReg = p.GetRegPair();
                                break;
                            }
                    }
                    break;
            }
            ProgCounterReg++;
            RegisterPair j = UpdatePairedRegFrom8Bit(AReg, FReg);
            AFReg = j.GetRegPair();
        }

        /// <summary>
        /// Decreases the value of register B by 1.
        /// </summary>
        private void DecBReg()
        {
            Console.WriteLine("dec b");
            switch (BReg - 1)
            {
                case 0:
                    {
                        BReg = 0;
                        FReg = SetZeroFlagState(true);
                        RegisterPair g = UpdatePairedRegFrom8Bit(AReg, FReg);
                        AFReg = g.GetRegPair();
                        RegisterPair j = UpdatePairedRegFrom8Bit(BReg, CReg);
                        BCReg = j.GetRegPair();
                        break;
                    }
                case < 0:
                    {
                        BReg--;
                        FReg = SetSignFlagState(true);
                        RegisterPair p = UpdatePairedRegFrom8Bit(AReg, FReg);
                        AFReg = p.GetRegPair();
                        RegisterPair g = UpdatePairedRegFrom8Bit(BReg, CReg);
                        BCReg = g.GetRegPair();
                        break;
                    }
                default:
                    {
                        BReg--;
                        RegisterPair p = UpdatePairedRegFrom8Bit(BReg, CReg);
                        BCReg = p.GetRegPair();
                        break;
                    }
            }

            ProgCounterReg++;
        }

        /// <summary>
        /// Loads an 8 bit value into register B.
        /// <param name="operand">The 8 bit value to be loaded.</param>
        /// </summary>
        private void LdBRegConst(byte operand)
        {
            Console.WriteLine("ld b, {0}", operand.ToString("X", ci));
            BReg = operand;
            RegisterPair p = UpdatePairedRegFrom8Bit(BReg, CReg);
            BCReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Rotates left A register contents by one bit, taking care of the carry bit.
        /// <remark>https://stackoverflow.com/questions/4439078/how-do-you-set-only-certain-bits-of-a-byte-in-c-without-affecting-the-rest#4439221</remark>
        /// </summary>
        private void Rlca()
        {
            Console.WriteLine("rlca\n");
            bool firstBitStatus = VerifyBitInByteStatus(AReg, 0);
            bool lastBitStatus = VerifyBitInByteStatus(AReg, 7);
            FReg = SetHalfCarryState(false);
            FReg = SetAddSubFlagState(false);
            FReg = SetParityOverflowState(firstBitStatus);
            AReg = SetBitInByte(AReg, firstBitStatus, 7);
            AReg = SetBitInByte(AReg, lastBitStatus, 0);
            AReg <<= 1; // left shift + assignment; we do this AFTER rotating.
            RegisterPair p = UpdateUnpairedRegFrom16Bit(AFReg, FReg);
            AReg = p.GetHighByte();
            FReg = p.GetLowByte();
            ProgCounterReg++;
        }

        /// <summary>
        /// Exchanges the values of the AF and AF' registers.
        /// </summary>
        private void ExchangeAfWithAfSec()
        {
            Console.WriteLine("ex af, af'\n");
            (AFReg, AFRegSec) = (AFRegSec, AFReg); // VS Code suggests we do this: I say it's incomprehensible.
            RegisterPair p = UpdateUnpairedRegFrom16Bit(AFReg, FReg);
            AReg = p.GetHighByte();
            FReg = p.GetLowByte();
            ProgCounterReg++;
        }

        /// <summary>
        /// Adds the contents of the BC register to the contents of the HL register.
        /// </summary>
        private void AddHLBC()
        {
            Console.WriteLine("add hl, bc\n");
            FReg = SetAddSubFlagState(false);
            switch (HLReg + BCReg)
            {
                // this gets valued as an int and not as an ushort, because C#.
                case > 0xFFFF:
                    FReg = SetCarryFlagState(true);
                    HLReg = 0;
                    break;
                default:
                    HLReg = (ushort)(HLReg + BCReg);
                    break;
            }
            RegisterPair p = UpdateUnpairedRegFrom16Bit(HLReg, HReg);
            HReg = p.GetHighByte();
            LReg = p.GetLowByte();
            RegisterPair g = UpdatePairedRegFrom8Bit(AReg, FReg);
            AFReg = g.GetRegPair();
            ProgCounterReg++;
        }

        // ld a, (bc)
        /// <summary>
        /// Loads the A register with the memory contents pointed by register BC.
        /// </summary>

        private void LdARegFromBCPointer()
        {
            Console.WriteLine("ld a, ({0})", BCReg.ToString("X", ci));
            AReg = AddressSpace[BCReg];
            RegisterPair p = UpdatePairedRegFrom8Bit(AReg, FReg);
            AFReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Decreases by one the contents of the BC register.
        /// </summary>
        private void DecBC()
        {
            Console.WriteLine("dec bc");
            BCReg--;
            RegisterPair p = UpdateUnpairedRegFrom16Bit(BCReg, BReg);
            BReg = p.GetHighByte();
            CReg = p.GetLowByte();
            ProgCounterReg++;
        }

        /// <summary>
        /// Increases the value of register C by 1.
        /// </summary>
        private void IncCReg()
        {
            Console.WriteLine("inc c");
            FReg = SetAddSubFlagState(false);
            switch (CReg + 1)
            {
                case > 0xFF:
                    {
                        CReg = 0;
                        SetParityOverflowState(true); // note to self: remind to take care of the carry.
                        RegisterPair p = UpdatePairedRegFrom8Bit(BReg, CReg);
                        BCReg = p.GetRegPair();
                        break;
                    }
                case 0:
                    {
                        CReg = 0;
                        FReg = SetZeroFlagState(true);
                        RegisterPair g = UpdatePairedRegFrom8Bit(AReg, FReg);
                        AFReg = g.GetRegPair();
                        RegisterPair p = UpdatePairedRegFrom8Bit(BReg, CReg);
                        BCReg = p.GetRegPair();
                        break;
                    }
                case < 0:
                    {
                        CReg++;
                        FReg = SetSignFlagState(true);
                        RegisterPair p = UpdatePairedRegFrom8Bit(AReg, FReg);
                        AFReg = p.GetRegPair();
                        RegisterPair g = UpdatePairedRegFrom8Bit(BReg, CReg);
                        BCReg = g.GetRegPair();
                        break;
                    }
                default:
                    {
                        CReg++;
                        RegisterPair p = UpdatePairedRegFrom8Bit(BReg, CReg);
                        BCReg = p.GetRegPair();
                        break;
                    }
            }
            ProgCounterReg++;
            RegisterPair j = UpdatePairedRegFrom8Bit(AReg, FReg); // flags
            AFReg = j.GetRegPair();
        }

        /// <summary>
        /// Decreases the value of register B by 1.
        /// </summary>
        private void DecCReg()
        {
            Console.WriteLine("dec c");
            switch (CReg - 1)
            {
                case > 0xFF:
                    {
                        CReg = 0;
                        FReg = SetParityOverflowState(true); // yes
                        RegisterPair p = UpdatePairedRegFrom8Bit(BReg, CReg);
                        BCReg = p.GetRegPair();
                        RegisterPair g = UpdatePairedRegFrom8Bit(AReg, FReg);
                        AFReg = g.GetRegPair();
                        break;
                    }
                case 0:
                    {
                        CReg = 0;
                        FReg = SetZeroFlagState(true);
                        RegisterPair g = UpdatePairedRegFrom8Bit(AReg, FReg);
                        AFReg = g.GetRegPair();
                        RegisterPair j = UpdatePairedRegFrom8Bit(BReg, CReg);
                        BCReg = j.GetRegPair();
                        break;
                    }
                case < 0:
                    {
                        CReg--;
                        FReg = SetSignFlagState(true);
                        RegisterPair p = UpdatePairedRegFrom8Bit(AReg, FReg);
                        AFReg = p.GetRegPair();
                        RegisterPair g = UpdatePairedRegFrom8Bit(BReg, CReg);
                        BCReg = g.GetRegPair();
                        break;
                    }
                default:
                    {
                        CReg--;
                        RegisterPair p = UpdatePairedRegFrom8Bit(BReg, CReg);
                        BCReg = p.GetRegPair();
                        break;
                    }
            }
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads a constant into the C register.
        /// <param name="operand">The constant.</param>
        /// </summary>
        private void LdCConst(byte operand)
        {
            Console.WriteLine("ld c, 0x{0}", operand.ToString("X", ci));
            CReg = operand;
            RegisterPair p = UpdatePairedRegFrom8Bit(BReg, CReg);
            BCReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Rotates right A register contents by one bit.
        /// </summary>
        private void Rrca()
        {
            Console.WriteLine("rrca");
            bool firstBitStatus = VerifyBitInByteStatus(AReg, 0);
            bool lastBitStatus = VerifyBitInByteStatus(AReg, 7);
            FReg = SetHalfCarryState(false);
            FReg = SetAddSubFlagState(false);
            FReg = SetParityOverflowState(firstBitStatus);
            AReg = SetBitInByte(AReg, firstBitStatus, 7);
            AReg = SetBitInByte(AReg, lastBitStatus, 0); // wrong????? may also be wrong in the original function IDK
            AReg >>= 1;
            RegisterPair p = UpdateUnpairedRegFrom16Bit(AFReg, FReg);
            AReg = p.GetHighByte();
            FReg = p.GetLowByte();
            ProgCounterReg++;
        }

        /// <summary>
        /// Decreases the B register by 1; if it is == 0, continue with the next instruction, if it isn't, branch forward or backward as specified by the operand.
        /// <param name="operand">A signed byte. This is a relative jump.</param>
        /// </summary>
        private void DjnzConst(sbyte operand)
        {
            Console.WriteLine("djnz 0x{0}", operand.ToString("X", ci));
            BReg--; // warning: the Z80 programmer should not set B to 0 or their loop will loop 127 times regardless.
            switch (BReg)
            {
                // yes, underflowing is intended behaviour, if it underflows it's a programmer skill issue.
                case 0:
                    ProgCounterReg++;
                    break;
                default:
                    {
                        switch (operand)
                        {
                            case >= 0:
                                ProgCounterReg += (ushort)operand; // we jump from the location of the djnz opcode: beware of off-by-one errors when programming the CPU.
                                break;
                            default:
                                ProgCounterReg -= (ushort)(operand * -1); // ugly but we avoid signed byte vs unsigned short bugs
                                break;
                        }

                        break;
                    }
            }
            RegisterPair p = UpdatePairedRegFrom8Bit(BReg, CReg);
            BCReg = p.GetRegPair();
        }

        /// <summary>
        /// Loads a 16 bit value into register DE.
        /// <param name="operand">The 16 bit value to be loaded.</param>
        /// </summary>
        private void LdDEConst(ushort operand)
        {
            Console.WriteLine("ld de, 0x{0}", operand.ToString("X", ci));
            DEReg = operand;
            RegisterPair p = UpdateUnpairedRegFrom16Bit(DEReg, DReg);
            DReg = p.GetHighByte();
            EReg = p.GetLowByte();
            ProgCounterReg++;
        }

        /// <summary>
        /// Stores the A register at the pointer pointed by register DE.
        /// </summary>

        private void LdDEPointerWithARegContents()
        {
            Console.WriteLine("ld a, [{0}]", BCReg.ToString("X", ci));
            AddressSpace[DEReg] = AReg;
            RegisterPair p = UpdatePairedRegFrom8Bit(AReg, FReg);
            AFReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Stores the A register at the pointer pointed by the 16 bit pointer.
        /// <param name="operand">The pointer.</param>
        /// </summary>

        private void LdPtrWithARegContents(ushort operand)
        {
            Console.WriteLine("ld a, [{0}]", operand.ToString("X", ci));
            AddressSpace[operand] = AReg;
            RegisterPair p = UpdatePairedRegFrom8Bit(AReg, FReg);
            AFReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Increases the value of register DE by 1.
        /// </summary>
        private void IncDEReg()
        {
            Console.WriteLine("inc de\n");
            DEReg++;
            RegisterPair p = UpdateUnpairedRegFrom16Bit(DEReg, DReg);
            DReg = p.GetHighByte();
            EReg = p.GetLowByte();
            ProgCounterReg++;
        }

        /// <summary>
        /// Increases the value of register D by 1.
        /// </summary>
        private void IncDReg()
        {
            Console.WriteLine("inc d");
            FReg = SetAddSubFlagState(false);
            switch (DReg + 1)
            {
                case > 0xFF:
                    {
                        DReg = 0;
                        SetParityOverflowState(true); // note to self: remind to take care of the carry.
                        RegisterPair p = UpdatePairedRegFrom8Bit(DReg, EReg);
                        DEReg = p.GetRegPair();
                        break;
                    }
                case 0:
                    {
                        DReg = 0;
                        FReg = SetZeroFlagState(true);
                        RegisterPair g = UpdatePairedRegFrom8Bit(AReg, FReg);
                        AFReg = g.GetRegPair();
                        RegisterPair p = UpdatePairedRegFrom8Bit(DReg, EReg);
                        DEReg = p.GetRegPair();
                        break;
                    }
                case < 0:
                    {
                        DReg++;
                        FReg = SetSignFlagState(true);
                        RegisterPair p = UpdatePairedRegFrom8Bit(AReg, FReg);
                        AFReg = p.GetRegPair();
                        RegisterPair g = UpdatePairedRegFrom8Bit(DReg, EReg);
                        DEReg = g.GetRegPair();
                        break;
                    }
                default:
                    {
                        DReg++;
                        RegisterPair p = UpdatePairedRegFrom8Bit(DReg, EReg);
                        DEReg = p.GetRegPair();
                        break;
                    }
            }
            ProgCounterReg++;
            RegisterPair j = UpdatePairedRegFrom8Bit(AReg, FReg);
            AFReg = j.GetRegPair();
        }

        /// <summary>
        /// Decreases the value of register D by 1.
        /// </summary>
        private void DecDReg()
        {
            Console.WriteLine("dec d");
            switch (DReg - 1)
            {
                case > 0xFF:
                    {
                        DReg = 0;
                        FReg = SetParityOverflowState(true); // yes
                        RegisterPair p = UpdatePairedRegFrom8Bit(DReg, EReg);
                        DEReg = p.GetRegPair();
                        RegisterPair g = UpdatePairedRegFrom8Bit(AReg, FReg);
                        AFReg = g.GetRegPair();
                        break;
                    }
                case 0:
                    {
                        DReg = 0;
                        FReg = SetZeroFlagState(true);
                        RegisterPair g = UpdatePairedRegFrom8Bit(AReg, FReg);
                        AFReg = g.GetRegPair();
                        RegisterPair j = UpdatePairedRegFrom8Bit(DReg, EReg);
                        DEReg = j.GetRegPair();
                        break;
                    }
                case < 0:
                    {
                        DReg--;
                        FReg = SetSignFlagState(true);
                        RegisterPair p = UpdatePairedRegFrom8Bit(AReg, FReg);
                        AFReg = p.GetRegPair();
                        RegisterPair g = UpdatePairedRegFrom8Bit(DReg, EReg);
                        DEReg = g.GetRegPair();
                        break;
                    }
                default:
                    {
                        DReg--;
                        RegisterPair p = UpdatePairedRegFrom8Bit(DReg, EReg);
                        DEReg = p.GetRegPair();
                        break;
                    }
            }
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads an 8 bit value into register D.
        /// <param name="operand">The 8 bit value to be loaded.</param>
        /// </summary>
        private void LdDRegConst(byte operand)
        {
            Console.WriteLine("ld d, {0}\n", operand.ToString("X", ci));
            DReg = operand;
            RegisterPair p = UpdatePairedRegFrom8Bit(DReg, EReg); // maybe automate this somehow IDK
            DEReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads an 8 bit value into register H.
        /// <param name="operand">The 8 bit value to be loaded.</param>
        /// </summary>
        private void LdHRegConst(byte operand)
        {
            Console.WriteLine("ld h, {0}\n", operand.ToString("X", ci));
            HReg = operand;
            RegisterPair p = UpdatePairedRegFrom8Bit(HReg, LReg); // maybe automate this somehow IDK
            HLReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Rotates left A register contents by one bit using the carry bit to perform the operation.
        /// </summary>
        private void Rla()
        {
            Console.WriteLine("rla");
            bool firstBitStatus = VerifyBitInByteStatus(AReg, 0);
            FReg = SetHalfCarryState(false);
            FReg = SetAddSubFlagState(false);
            FReg = SetParityOverflowState(firstBitStatus);
            bool carryFlagCopy = CheckCarryFlagState();
            bool MSBARegStatus = VerifyBitInByteStatus(AReg, 7);
            FReg = SetBitInByte(FReg, MSBARegStatus, 7);
            AReg = SetBitInByte(AReg, carryFlagCopy, 0); // oh boy.
            AReg <<= 1; // left shift + assignment; we do this AFTER rotating.
            RegisterPair p = UpdateUnpairedRegFrom16Bit(AFReg, FReg);
            AReg = p.GetHighByte();
            FReg = p.GetLowByte();
            ProgCounterReg++;
        }

        /// <summary>
        /// Jumps to a relative point in memory.
        /// <param name="operand">A signed byte. This dictates how many bytes we branch forward.</param>
        /// </summary>
        private void JrConst(sbyte operand)
        {
            Console.WriteLine("jr 0x{0}", operand.ToString("X", ci));
            switch (operand)
            {
                case >= 0:
                    ProgCounterReg += (ushort)operand;
                    break;
                default:
                    ProgCounterReg -= (ushort)(operand * -1); // see dnjz implementation
                    break;
            }
        }

        /// <summary>
        /// Adds the contents of the DE register to the contents of the HL register.
        /// </summary>
        private void AddHLDE()
        {
            Console.WriteLine("add hl, de");
            FReg = SetAddSubFlagState(false);
            switch (HLReg + DEReg)
            {
                case > 0xFFFF:
                    FReg = SetCarryFlagState(true);
                    HLReg = 0;
                    break;
                default:
                    HLReg = (ushort)(HLReg + DEReg);
                    break;
            }
            RegisterPair p = UpdateUnpairedRegFrom16Bit(HLReg, HReg);
            HReg = p.GetHighByte();
            LReg = p.GetLowByte();
            RegisterPair g = UpdatePairedRegFrom8Bit(AReg, FReg);
            AFReg = g.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Adds the contents of the HL register to the contents of the HL register.
        /// </summary>
        private void AddHLHL()
        {
            Console.WriteLine("add hl, hl");
            FReg = SetAddSubFlagState(false);
            switch (HLReg + HLReg)
            {
                case > 0xFFFF:
                    FReg = SetCarryFlagState(true);
                    HLReg = 0;
                    break;
                default:
                    HLReg = (ushort)(HLReg + HLReg);
                    break;
            }
            RegisterPair p = UpdateUnpairedRegFrom16Bit(HLReg, HReg);
            HReg = p.GetHighByte();
            LReg = p.GetLowByte();
            RegisterPair g = UpdatePairedRegFrom8Bit(AReg, FReg);
            AFReg = g.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Adds the contents of the DE register to the contents of the stack pointer.
        /// </summary>
        private void AddHLSP()
        {
            Console.WriteLine("add hl, sp");
            FReg = SetAddSubFlagState(false);
            switch (HLReg + StackPointerReg)
            {
                case > 0xFFFF:
                    FReg = SetCarryFlagState(true);
                    HLReg = 0;
                    break;
                default:
                    HLReg = (ushort)(HLReg + StackPointerReg);
                    break;
            }
            RegisterPair p = UpdateUnpairedRegFrom16Bit(HLReg, HReg);
            HReg = p.GetHighByte();
            LReg = p.GetLowByte();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the A register with the memory contents pointed by register DE.
        /// </summary>

        private void LdARegFromDEPointer()
        {
            Console.WriteLine("ld a, [{0}]", BCReg.ToString("X", ci));
            AReg = AddressSpace[DEReg];
            RegisterPair p = UpdatePairedRegFrom8Bit(AReg, FReg);
            AFReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Decreases by one the contents of the DE register.
        /// </summary>
        private void DecDE()
        {
            Console.WriteLine("dec de");
            DEReg--;
            RegisterPair p = UpdateUnpairedRegFrom16Bit(BCReg, BReg);
            BReg = p.GetHighByte();
            CReg = p.GetLowByte();
            ProgCounterReg++;
        }

        /// <summary>
        /// Increases the value of register E by 1.
        /// </summary>
        private void IncEReg()
        {
            Console.WriteLine("inc e");
            FReg = SetAddSubFlagState(false);
            switch (EReg + 1)
            {
                case > 0xFF:
                    {
                        EReg = 0;
                        SetParityOverflowState(true); // note to self: remind to take care of the carry.
                        RegisterPair p = UpdatePairedRegFrom8Bit(DReg, EReg);
                        DEReg = p.GetRegPair();
                        break;
                    }
                case 0:
                    {
                        EReg = 0;
                        FReg = SetZeroFlagState(true);
                        RegisterPair g = UpdatePairedRegFrom8Bit(AReg, FReg);
                        AFReg = g.GetRegPair();
                        RegisterPair p = UpdatePairedRegFrom8Bit(DReg, EReg);
                        DEReg = p.GetRegPair();
                        break;
                    }
                case < 0:
                    {
                        EReg++;
                        FReg = SetSignFlagState(true);
                        RegisterPair p = UpdatePairedRegFrom8Bit(AReg, FReg);
                        AFReg = p.GetRegPair();
                        RegisterPair g = UpdatePairedRegFrom8Bit(DReg, EReg);
                        DEReg = g.GetRegPair();
                        break;
                    }
                default:
                    {
                        EReg++;
                        RegisterPair p = UpdatePairedRegFrom8Bit(DReg, EReg);
                        DEReg = p.GetRegPair();
                        break;
                    }
            }
            ProgCounterReg++;
            RegisterPair j = UpdatePairedRegFrom8Bit(AReg, FReg); // flags
            AFReg = j.GetRegPair();
        }

        /// <summary>
        /// Decreases the value of register E by 1.
        /// </summary>
        private void DecEReg()
        {
            Console.WriteLine("dec e");
            switch (EReg - 1)
            {
                case > 0xFF:
                    {
                        EReg = 0;
                        FReg = SetParityOverflowState(true); // yes
                        RegisterPair p = UpdatePairedRegFrom8Bit(DReg, EReg);
                        DEReg = p.GetRegPair();
                        RegisterPair g = UpdatePairedRegFrom8Bit(AReg, FReg);
                        AFReg = g.GetRegPair();
                        break;
                    }
                case 0:
                    {
                        EReg = 0;
                        FReg = SetZeroFlagState(true);
                        RegisterPair g = UpdatePairedRegFrom8Bit(AReg, FReg);
                        AFReg = g.GetRegPair();
                        RegisterPair j = UpdatePairedRegFrom8Bit(DReg, EReg);
                        DEReg = j.GetRegPair();
                        break;
                    }
                case < 0:
                    {
                        EReg--;
                        FReg = SetSignFlagState(true);
                        RegisterPair p = UpdatePairedRegFrom8Bit(AReg, FReg);
                        AFReg = p.GetRegPair();
                        RegisterPair g = UpdatePairedRegFrom8Bit(DReg, EReg);
                        DEReg = g.GetRegPair();
                        break;
                    }
                default:
                    {
                        EReg--;
                        RegisterPair p = UpdatePairedRegFrom8Bit(DReg, EReg);
                        DEReg = p.GetRegPair();
                        break;
                    }
            }
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads a constant into the E register.
        /// <param name="operand">The constant.</param>
        /// </summary>
        private void LdEConst(byte operand)
        {
            Console.WriteLine("ld e, 0x{0}", operand.ToString("X", ci));
            EReg = operand;
            RegisterPair p = UpdatePairedRegFrom8Bit(DReg, EReg);
            DEReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Rotates right A register contents by one bit using the carry bit to perform the operation.
        /// </summary>
        private void Rra()
        {
            Console.WriteLine("rra");
            bool firstBitStatus = VerifyBitInByteStatus(AReg, 0);
            FReg = SetHalfCarryState(false);
            FReg = SetAddSubFlagState(false);
            FReg = SetParityOverflowState(firstBitStatus);
            bool carryFlagCopy = CheckCarryFlagState();
            bool MSBAReg = VerifyBitInByteStatus(AReg, 0);
            FReg = SetBitInByte(FReg, MSBAReg, 7);
            AReg = SetBitInByte(AReg, carryFlagCopy, 7);
            AReg >>= 1;
            RegisterPair p = UpdateUnpairedRegFrom16Bit(AFReg, FReg);
            AReg = p.GetHighByte();
            FReg = p.GetLowByte();
            ProgCounterReg++;
        }

        /// <summary>
        /// Performs a relative jump if the zero flag is unset.
        /// <param name="operand">A signed byte. This dictates how many bytes we branch forward.</param>
        /// </summary>
        private void JrNz(sbyte operand)
        {
            Console.WriteLine("jr nz, 0x{0}", operand.ToString("X", ci));
            if (!CheckZeroFlagState())
            {
                switch (operand)
                {
                    case >= 0:
                        ProgCounterReg += (ushort)operand;
                        break;
                    default:
                        ProgCounterReg -= (ushort)(operand * -1);
                        break;
                }
            }
            else
                ProgCounterReg++;
        }

        /// <summary>
        /// Performs a relative jump if the carry flag is unset.
        /// <param name="operand">A signed byte. This dictates how many bytes we branch forward.</param>
        /// </summary>
        private void JrNc(sbyte operand)
        {
            Console.WriteLine("jr nc, 0x{0}", operand.ToString("X", ci));
            if (!CheckCarryFlagState())
            {
                switch (operand)
                {
                    case >= 0:
                        ProgCounterReg += (ushort)operand;
                        break;
                    default:
                        ProgCounterReg -= (ushort)(operand * -1);
                        break;
                }
            }
            else
                ProgCounterReg++;
        }

        /// <summary>
        /// Performs a relative jump if the carry flag is set.
        /// <param name="operand">A signed byte. This dictates how many bytes we branch forward.</param>
        /// </summary>
        private void JrC(sbyte operand)
        {
            Console.WriteLine("jr c, 0x{0}", operand.ToString("X", ci));
            if (CheckCarryFlagState())
            {
                switch (operand)
                {
                    case >= 0:
                        ProgCounterReg += (ushort)operand;
                        break;
                    default:
                        ProgCounterReg -= (ushort)(operand * -1);
                        break;
                }
            }
            else
                ProgCounterReg++;
        }

        /// <summary>
        /// Loads a 16 bit value into register HL.
        /// <param name="operand">The 16 bit value to be loaded.</param>
        /// </summary>
        private void LdHLConst(ushort operand)
        {
            Console.WriteLine("ld hl, 0x{0}", operand.ToString("X", ci));
            HLReg = operand;
            RegisterPair p = UpdateUnpairedRegFrom16Bit(HLReg, HReg);
            HReg = p.GetHighByte();
            LReg = p.GetLowByte();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads register HL with the contents pointed by the operand.
        /// </summary>
        /// <param name="ptr">A 16 bit pointer for the data to be loaded into register HL.</param>
        private void LdHLWithPtr(ushort ptr)
        {
            Console.WriteLine("ld hl, ({0})", ptr.ToString("X", ci));
            ushort ptr2 = ptr++; // temporary.
            HLReg = Create16BitLENumberFrom8Bit(AddressSpace[ptr2], AddressSpace[ptr]);
            RegisterPair p = UpdateUnpairedRegFrom16Bit(HLReg, HReg);
            HReg = p.GetHighByte();
            LReg = p.GetLowByte();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads a 16 bit value into the stack pointer.
        /// <param name="operand">The 16 bit value to be loaded.</param>
        /// </summary>
        private void LdSPConst(ushort operand)
        {
            Console.WriteLine("ld sp, 0x{0}", operand.ToString("X", ci));
            StackPointerReg = operand;
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads a 16 bit value into register HL at the address specified by the operand.
        /// <param name="ptr">A 16-bit pointer to the data.</param>
        /// </summary>
        private void Ld16BPointerInHLReg(ushort ptr)
        {
            byte packed1 = AddressSpace[ptr];
            byte packed2 = AddressSpace[++ptr];
            HLReg = Create16BitLENumberFrom8Bit(packed2, packed1);
            RegisterPair r = UpdateUnpairedRegFrom16Bit(HLReg, HReg);
            HReg = r.GetHighByte();
            LReg = r.GetLowByte();
            ProgCounterReg++;
        }

        /// <summary>
        /// Increases the value of register HL by 1.
        /// </summary>
        private void IncHLReg()
        {
            Console.WriteLine("inc hl");
            HLReg++;
            RegisterPair p = UpdateUnpairedRegFrom16Bit(HLReg, HReg);
            HReg = p.GetHighByte();
            LReg = p.GetLowByte();
            ProgCounterReg++;
        }

        /// <summary>
        /// Increases the value of the stack pointer register by 1.
        /// </summary>
        private void IncSp()
        {
            Console.WriteLine("inc sp");
            StackPointerReg++;
            ProgCounterReg++;
        }

        /// <summary>
        /// Increases the value of register H by 1.
        /// </summary>
        private void IncHReg()
        {
            Console.WriteLine("inc h");
            FReg = SetAddSubFlagState(false);
            switch (HReg + 1)
            {
                case > 0xFF:
                    {
                        HReg = 0;
                        SetParityOverflowState(true); // note to self: remind to take care of the carry.
                        RegisterPair p = UpdatePairedRegFrom8Bit(HReg, LReg);
                        HLReg = p.GetRegPair();
                        break;
                    }
                case 0:
                    {
                        HReg = 0;
                        FReg = SetZeroFlagState(true);
                        RegisterPair g = UpdatePairedRegFrom8Bit(AReg, FReg);
                        AFReg = g.GetRegPair();
                        RegisterPair p = UpdatePairedRegFrom8Bit(HReg, LReg);
                        HLReg = p.GetRegPair();
                        break;
                    }
                case < 0:
                    {
                        HReg++;
                        FReg = SetSignFlagState(true);
                        RegisterPair p = UpdatePairedRegFrom8Bit(AReg, FReg);
                        AFReg = p.GetRegPair();
                        RegisterPair g = UpdatePairedRegFrom8Bit(HReg, LReg);
                        HLReg = g.GetRegPair();
                        break;
                    }
                default:
                    {
                        HReg++;
                        RegisterPair p = UpdatePairedRegFrom8Bit(HReg, LReg);
                        HLReg = p.GetRegPair();
                        break;
                    }
            }
            ProgCounterReg++;
            RegisterPair j = UpdatePairedRegFrom8Bit(AReg, FReg);
            AFReg = j.GetRegPair();
        }

        /// <summary>
        /// Increases the value of register L by 1.
        /// </summary>
        private void IncLReg()
        {
            Console.WriteLine("inc l");
            FReg = SetAddSubFlagState(false);
            switch (LReg + 1)
            {
                case > 0xFF:
                    {
                        LReg = 0;
                        SetParityOverflowState(true); // note to self: remind to take care of the carry.
                        RegisterPair p = UpdatePairedRegFrom8Bit(HReg, LReg);
                        HLReg = p.GetRegPair();
                        break;
                    }
                case 0:
                    {
                        LReg = 0;
                        FReg = SetZeroFlagState(true);
                        RegisterPair g = UpdatePairedRegFrom8Bit(AReg, FReg);
                        AFReg = g.GetRegPair();
                        RegisterPair p = UpdatePairedRegFrom8Bit(HReg, LReg);
                        HLReg = p.GetRegPair();
                        break;
                    }
                case < 0:
                    {
                        LReg++;
                        FReg = SetSignFlagState(true);
                        RegisterPair p = UpdatePairedRegFrom8Bit(AReg, FReg);
                        AFReg = p.GetRegPair();
                        RegisterPair g = UpdatePairedRegFrom8Bit(HReg, LReg);
                        HLReg = g.GetRegPair();
                        break;
                    }
                default:
                    {
                        LReg++;
                        RegisterPair p = UpdatePairedRegFrom8Bit(HReg, LReg);
                        HLReg = p.GetRegPair();
                        break;
                    }
            }
            ProgCounterReg++;
            RegisterPair j = UpdatePairedRegFrom8Bit(AReg, FReg);
            AFReg = j.GetRegPair();
        }

        /// <summary>
        /// Increases the value of register A by 1.
        /// </summary>
        private void IncAReg()
        {
            Console.WriteLine("inc a");
            FReg = SetAddSubFlagState(false);
            switch (AReg + 1)
            {
                case > 0xFF:
                    {
                        AReg = 0;
                        SetParityOverflowState(true); // note to self: remind to take care of the carry.
                        RegisterPair p = UpdatePairedRegFrom8Bit(AReg, FReg);
                        AFReg = p.GetRegPair();
                        break;
                    }
                case 0:
                    {
                        AReg = 0;
                        FReg = SetZeroFlagState(true);
                        RegisterPair g = UpdatePairedRegFrom8Bit(AReg, FReg);
                        AFReg = g.GetRegPair();
                        break;
                    }
                case < 0:
                    {
                        AReg++;
                        FReg = SetSignFlagState(true);
                        RegisterPair p = UpdatePairedRegFrom8Bit(AReg, FReg);
                        AFReg = p.GetRegPair();
                        break;
                    }
                default:
                    {
                        AReg++;
                        RegisterPair p = UpdatePairedRegFrom8Bit(AReg, FReg);
                        AFReg = p.GetRegPair();
                        break;
                    }
            }
            ProgCounterReg++;
        }

        /// <summary>
        /// Increases by 1 the value located at the pointer located in register HL.
        /// </summary>
        private void IncHLRegFromPtr()
        {
            Console.WriteLine("inc ({0})", HLReg.ToString("X", ci));
            AddressSpace[HLReg]++;
            ProgCounterReg++;
        }

        /// <summary>
        /// Decreases by 1 the value located at the pointer located in register HL.
        /// </summary>
        private void DecHLRegFromPtr()
        {
            Console.WriteLine("dec ({0})", HLReg.ToString("X", ci));
            AddressSpace[HLReg]--;
            ProgCounterReg++;
        }

        /// <summary>
        /// Decreases the value of register H by 1.
        /// </summary>
        private void DecHReg()
        {
            Console.WriteLine("dec h");
            switch (HReg - 1)
            {
                case > 0xFF:
                    {
                        HReg = 0;
                        FReg = SetParityOverflowState(true); // yes
                        RegisterPair p = UpdatePairedRegFrom8Bit(HReg, LReg);
                        HLReg = p.GetRegPair();
                        RegisterPair g = UpdatePairedRegFrom8Bit(AReg, FReg);
                        AFReg = g.GetRegPair();
                        break;
                    }
                case 0:
                    {
                        HReg = 0;
                        FReg = SetZeroFlagState(true);
                        RegisterPair g = UpdatePairedRegFrom8Bit(AReg, FReg);
                        AFReg = g.GetRegPair();
                        RegisterPair j = UpdatePairedRegFrom8Bit(DReg, EReg);
                        HLReg = j.GetRegPair();
                        break;
                    }
                case < 0:
                    {
                        HReg--;
                        FReg = SetSignFlagState(true);
                        RegisterPair p = UpdatePairedRegFrom8Bit(AReg, FReg);
                        HLReg = p.GetRegPair();
                        RegisterPair g = UpdatePairedRegFrom8Bit(DReg, EReg);
                        DEReg = g.GetRegPair();
                        break;
                    }
                default:
                    {
                        HReg--;
                        RegisterPair p = UpdatePairedRegFrom8Bit(HReg, LReg);
                        HLReg = p.GetRegPair();
                        break;
                    }
            }
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the contents of the B register into the C register.
        /// </summary>
        private void LdCB()
        {
            Console.WriteLine("ld c, b");
            CReg = BReg;
            RegisterPair p = UpdatePairedRegFrom8Bit(BReg, CReg);
            BCReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the contents of the C register into the C register.
        /// </summary>
        private void LdCC()
        {
            Console.WriteLine("ld c, c");
#pragma warning disable CS1717 // Assignment made to same variable
            CReg = CReg;
            RegisterPair p = UpdatePairedRegFrom8Bit(BReg, CReg);
            BCReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the contents of the D register into the C register.
        /// </summary>
        private void LdCD()
        {
            Console.WriteLine("ld C, d");
            CReg = DReg;
            RegisterPair p = UpdatePairedRegFrom8Bit(BReg, CReg);
            BCReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the contents of the E register into the C register.
        /// </summary>
        private void LdCE()
        {
            Console.WriteLine("ld c, e");
            CReg = EReg;
            RegisterPair p = UpdatePairedRegFrom8Bit(BReg, CReg);
            BCReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the contents of the H register into the C register.
        /// </summary>
        private void LdCH()
        {
            Console.WriteLine("ld c, h");
            CReg = HReg;
            RegisterPair p = UpdatePairedRegFrom8Bit(BReg, CReg);
            BCReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the contents of the L register into the C register.
        /// </summary>
        private void LdCL()
        {
            Console.WriteLine("ld c, l");
            CReg = LReg;
            RegisterPair p = UpdatePairedRegFrom8Bit(BReg, CReg);
            BCReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the contents of the A register into the C register.
        /// </summary>
        private void LdCA()
        {
            Console.WriteLine("ld c, a");
            CReg = AReg;
            RegisterPair p = UpdatePairedRegFrom8Bit(BReg, CReg);
            BCReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the B register with the memory contents pointed by register HL.
        /// </summary>

        private void LdCRegFromHLPointer()
        {
            Console.WriteLine("ld c, ({0})", HLReg.ToString("X", ci));
            CReg = AddressSpace[HLReg];
            RegisterPair p = UpdatePairedRegFrom8Bit(BReg, CReg);
            BCReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Decreases the value of register L by 1.
        /// </summary>
        private void DecLReg()
        {
            Console.WriteLine("dec l");
            switch (LReg - 1)
            {
                case > 0xFF:
                    {
                        LReg = 0;
                        FReg = SetParityOverflowState(true); // yes
                        RegisterPair p = UpdatePairedRegFrom8Bit(HReg, LReg);
                        HLReg = p.GetRegPair();
                        RegisterPair g = UpdatePairedRegFrom8Bit(AReg, FReg);
                        AFReg = g.GetRegPair();
                        break;
                    }
                case 0:
                    {
                        LReg = 0;
                        FReg = SetZeroFlagState(true);
                        RegisterPair g = UpdatePairedRegFrom8Bit(AReg, FReg);
                        AFReg = g.GetRegPair();
                        RegisterPair j = UpdatePairedRegFrom8Bit(DReg, EReg);
                        HLReg = j.GetRegPair();
                        break;
                    }
                case < 0:
                    {
                        LReg--;
                        FReg = SetSignFlagState(true);
                        RegisterPair p = UpdatePairedRegFrom8Bit(AReg, FReg);
                        HLReg = p.GetRegPair();
                        RegisterPair g = UpdatePairedRegFrom8Bit(DReg, EReg);
                        DEReg = g.GetRegPair();
                        break;
                    }
                default:
                    {
                        HReg--;
                        RegisterPair p = UpdatePairedRegFrom8Bit(HReg, LReg);
                        HLReg = p.GetRegPair();
                        break;
                    }
            }
            ProgCounterReg++;
        }

        /// <summary>
        /// Decreases the value of register A by 1.
        /// </summary>
        private void DecAReg()
        {
            Console.WriteLine("dec a");
            FReg = SetAddSubFlagState(false);
            switch (AReg - 1)
            {
                case > 0xFF:
                    {
                        AReg = 0;
                        SetParityOverflowState(true); // note to self: remind to take care of the carry.
                        RegisterPair p = UpdatePairedRegFrom8Bit(AReg, FReg);
                        AFReg = p.GetRegPair();
                        break;
                    }
                case 0:
                    {
                        AReg = 0;
                        FReg = SetZeroFlagState(true);
                        RegisterPair g = UpdatePairedRegFrom8Bit(AReg, FReg);
                        AFReg = g.GetRegPair();
                        break;
                    }
                case < 0:
                    {
                        AReg--;
                        FReg = SetSignFlagState(true);
                        RegisterPair p = UpdatePairedRegFrom8Bit(AReg, FReg);
                        AFReg = p.GetRegPair();
                        break;
                    }
                default:
                    {
                        AReg--;
                        RegisterPair p = UpdatePairedRegFrom8Bit(AReg, FReg);
                        AFReg = p.GetRegPair();
                        break;
                    }
            }
            ProgCounterReg++;
        }

        /// <summary>
        /// Decreases by one the contents of the HL register.
        /// </summary>
        private void DecHL()
        {
            Console.WriteLine("dec hl");
            HLReg--;
            RegisterPair p = UpdateUnpairedRegFrom16Bit(HLReg, HReg);
            HReg = p.GetHighByte();
            LReg = p.GetLowByte();
            ProgCounterReg++;
        }

        /// <summary>
        /// Decreases by one the contents of the stack pointer.
        /// </summary>
        private void DecSP()
        {
            Console.WriteLine("dec sp");
            StackPointerReg--;
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the contents of the B register into the B register.
        /// </summary>
        private void LdBB()
        {
            Console.WriteLine("ld b, b");
            // compiler stuff below
#pragma warning disable CS1717 // Assignment made to same variable
            BReg = BReg; // this instruction tries to do something, but achieves absolutely nothing. delightful
            RegisterPair p = UpdatePairedRegFrom8Bit(BReg, CReg);
            BCReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the contents of the C register into the B register.
        /// </summary>
        private void LdBC()
        {
            Console.WriteLine("ld b, c");
            BReg = CReg;
            RegisterPair p = UpdatePairedRegFrom8Bit(BReg, CReg);
            BCReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the contents of the D register into the B register.
        /// </summary>
        private void LdBD()
        {
            Console.WriteLine("ld b, d");
            BReg = DReg;
            RegisterPair p = UpdatePairedRegFrom8Bit(BReg, CReg);
            BCReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the contents of the E register into the B register.
        /// </summary>
        private void LdBE()
        {
            Console.WriteLine("ld b, e");
            BReg = EReg;
            RegisterPair p = UpdatePairedRegFrom8Bit(BReg, CReg);
            BCReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the contents of the H register into the B register.
        /// </summary>
        private void LdBH()
        {
            Console.WriteLine("ld b, h");
            BReg = HReg;
            RegisterPair p = UpdatePairedRegFrom8Bit(BReg, CReg);
            BCReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the contents of the L register into the B register.
        /// </summary>
        private void LdBL()
        {
            Console.WriteLine("ld b, l");
            BReg = LReg;
            RegisterPair p = UpdatePairedRegFrom8Bit(BReg, CReg);
            BCReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the contents of the A register into the B register.
        /// </summary>
        private void LdBA()
        {
            Console.WriteLine("ld b, a");
            BReg = AReg;
            RegisterPair p = UpdatePairedRegFrom8Bit(BReg, CReg);
            BCReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the B register with the memory contents pointed by register HL.
        /// </summary>

        private void LdBRegFromHLPointer()
        {
            Console.WriteLine("ld b, ({0})", HLReg.ToString("X", ci));
            BReg = AddressSpace[HLReg];
            RegisterPair p = UpdatePairedRegFrom8Bit(BReg, CReg);
            BCReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the contents of the B register into the D register.
        /// </summary>
        private void LdDB()
        {
            Console.WriteLine("ld d, b");
            DReg = BReg;
            RegisterPair p = UpdatePairedRegFrom8Bit(DReg, EReg);
            DEReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the contents of the C register into the D register.
        /// </summary>
        private void LdDC()
        {
            Console.WriteLine("ld d, c");
            DReg = CReg;
            RegisterPair p = UpdatePairedRegFrom8Bit(DReg, EReg);
            DEReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the contents of the D register into the D register.
        /// </summary>
        private void LdDD()
        {
            Console.WriteLine("ld d, d");
            DReg = DReg;
            RegisterPair p = UpdatePairedRegFrom8Bit(DReg, EReg);
            DEReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the contents of the E register into the D register.
        /// </summary>
        private void LdDE()
        {
            Console.WriteLine("ld b, e");
            EReg = EReg;
            RegisterPair p = UpdatePairedRegFrom8Bit(DReg, EReg);
            DEReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the contents of the H register into the D register.
        /// </summary>
        private void LdDH()
        {
            Console.WriteLine("ld d, h");
            DReg = HReg;
            RegisterPair p = UpdatePairedRegFrom8Bit(DReg, EReg);
            DEReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the contents of the L register into the d register.
        /// </summary>
        private void LdDL()
        {
            Console.WriteLine("ld d, l");
            DReg = LReg;
            RegisterPair p = UpdatePairedRegFrom8Bit(DReg, EReg);
            DEReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the contents of the A register into the D register.
        /// </summary>
        private void LdDA()
        {
            Console.WriteLine("ld d, a");
            DReg = AReg;
            RegisterPair p = UpdatePairedRegFrom8Bit(DReg, EReg);
            DEReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the D register with the memory contents pointed by register HL.
        /// </summary>

        private void LdDRegFromHLPointer()
        {
            Console.WriteLine("ld d, ({0})", HLReg.ToString("X", ci));
            DReg = AddressSpace[HLReg];
            RegisterPair p = UpdatePairedRegFrom8Bit(DReg, EReg);
            DEReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the contents of the B register into the E register.
        /// </summary>
        private void LdEB()
        {
            Console.WriteLine("ld e, b");
            EReg = BReg;
            RegisterPair p = UpdatePairedRegFrom8Bit(DReg, EReg);
            DEReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the contents of the C register into the E register.
        /// </summary>
        private void LdEC()
        {
            Console.WriteLine("ld e, c");
            EReg = CReg;
            RegisterPair p = UpdatePairedRegFrom8Bit(DReg, EReg);
            DEReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the contents of the D register into the E register.
        /// </summary>
        private void LdED()
        {
            Console.WriteLine("ld e, d");
            EReg = DReg;
            RegisterPair p = UpdatePairedRegFrom8Bit(DReg, EReg);
            DEReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the contents of the E register into the E register.
        /// </summary>
        private void LdEE()
        {
            Console.WriteLine("ld e, e");
            EReg = EReg;
            RegisterPair p = UpdatePairedRegFrom8Bit(DReg, EReg);
            DEReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the contents of the H register into the E register.
        /// </summary>
        private void LdEH()
        {
            Console.WriteLine("ld e, h");
            EReg = HReg;
            RegisterPair p = UpdatePairedRegFrom8Bit(DReg, EReg);
            DEReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the contents of the L register into the E register.
        /// </summary>
        private void LdEL()
        {
            Console.WriteLine("ld e, l");
            EReg = LReg;
            RegisterPair p = UpdatePairedRegFrom8Bit(DReg, EReg);
            DEReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the contents of the A register into the E register.
        /// </summary>
        private void LdEA()
        {
            Console.WriteLine("ld e, a");
            EReg = AReg;
            RegisterPair p = UpdatePairedRegFrom8Bit(DReg, EReg);
            DEReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the E register with the memory contents pointed by register HL.
        /// </summary>

        private void LdERegFromHLPointer()
        {
            Console.WriteLine("ld d, ({0})", HLReg.ToString("X", ci));
            EReg = AddressSpace[HLReg];
            RegisterPair p = UpdatePairedRegFrom8Bit(DReg, EReg);
            DEReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the contents of the B register into the H register.
        /// </summary>
        private void LdHB()
        {
            Console.WriteLine("ld h, b");
            HReg = BReg;
            RegisterPair p = UpdatePairedRegFrom8Bit(HReg, LReg);
            HLReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the contents of the C register into the H register.
        /// </summary>
        private void LdHC()
        {
            Console.WriteLine("ld h, c");
            HReg = CReg;
            RegisterPair p = UpdatePairedRegFrom8Bit(HReg, LReg);
            HLReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the contents of the D register into the H register.
        /// </summary>
        private void LdHD()
        {
            Console.WriteLine("ld h, d");
            HReg = DReg;
            RegisterPair p = UpdatePairedRegFrom8Bit(HReg, LReg);
            HLReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the contents of the E register into the H register.
        /// </summary>
        private void LdHE()
        {
            Console.WriteLine("ld h, e");
            HReg = EReg;
            RegisterPair p = UpdatePairedRegFrom8Bit(HReg, LReg);
            HLReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the contents of the H register into the H register.
        /// </summary>
        private void LdHH()
        {
            Console.WriteLine("ld h, h");
            HReg = HReg;
            RegisterPair p = UpdatePairedRegFrom8Bit(HReg, LReg);
            HLReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the contents of the L register into the H register.
        /// </summary>
        private void LdHL()
        {
            Console.WriteLine("ld h, l");
            HReg = LReg;
            RegisterPair p = UpdatePairedRegFrom8Bit(HReg, LReg);
            HLReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the contents of the A register into the H register.
        /// </summary>
        private void LdHA()
        {
            Console.WriteLine("ld h, a");
            HReg = AReg;
            RegisterPair p = UpdatePairedRegFrom8Bit(HReg, LReg);
            HLReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the H register with the memory contents pointed by register HL.
        /// </summary>

        private void LdHRegFromHLPointer()
        {
            Console.WriteLine("ld h, ({0})", HLReg.ToString("X", ci));
            HReg = AddressSpace[HLReg];
            RegisterPair p = UpdatePairedRegFrom8Bit(HReg, LReg);
            HLReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the contents of the B register into the L register.
        /// </summary>
        private void LdLB()
        {
            Console.WriteLine("ld l, b");
            LReg = BReg;
            RegisterPair p = UpdatePairedRegFrom8Bit(HReg, LReg);
            HLReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the contents of the C register into the L register.
        /// </summary>
        private void LdLC()
        {
            Console.WriteLine("ld l, c");
            LReg = CReg;
            RegisterPair p = UpdatePairedRegFrom8Bit(HReg, LReg);
            HLReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the contents of the D register into the L register.
        /// </summary>
        private void LdLD()
        {
            Console.WriteLine("ld l, d");
            LReg = DReg;
            RegisterPair p = UpdatePairedRegFrom8Bit(HReg, LReg);
            HLReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the contents of the E register into the L register.
        /// </summary>
        private void LdLE()
        {
            Console.WriteLine("ld l, e");
            LReg = EReg;
            RegisterPair p = UpdatePairedRegFrom8Bit(HReg, LReg);
            HLReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the contents of the H register into the L register.
        /// </summary>
        private void LdLH()
        {
            Console.WriteLine("ld l, h");
            LReg = HReg;
            RegisterPair p = UpdatePairedRegFrom8Bit(HReg, LReg);
            HLReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the contents of the L register into the L register.
        /// </summary>
        private void LdLL()
        {
            Console.WriteLine("ld l, l");
            LReg = LReg;
            RegisterPair p = UpdatePairedRegFrom8Bit(HReg, LReg);
            HLReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the contents of the A register into the L register.
        /// </summary>
        private void LdLA()
        {
            Console.WriteLine("ld l, a");
            LReg = AReg;
            RegisterPair p = UpdatePairedRegFrom8Bit(HReg, LReg);
            HLReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the L register with the memory contents pointed by register HL.
        /// </summary>

        private void LdLRegFromHLPointer()
        {
            Console.WriteLine("ld l, ({0})", HLReg.ToString("X", ci));
            LReg = AddressSpace[HLReg];
            RegisterPair p = UpdatePairedRegFrom8Bit(HReg, LReg);
            HLReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the contents of the B register into the A register.
        /// </summary>
        private void LdAB()
        {
            Console.WriteLine("ld l, b");
            AReg = BReg;
            RegisterPair p = UpdatePairedRegFrom8Bit(AReg, FReg);
            AFReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the contents of the C register into the A register.
        /// </summary>
        private void LdAC()
        {
            Console.WriteLine("ld l, c");
            AReg = CReg;
            RegisterPair p = UpdatePairedRegFrom8Bit(AReg, FReg);
            AFReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the contents of the D register into the A register.
        /// </summary>
        private void LdAD()
        {
            Console.WriteLine("ld l, d");
            AReg = DReg;
            RegisterPair p = UpdatePairedRegFrom8Bit(AReg, FReg);
            AFReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the contents of the E register into the A register.
        /// </summary>
        private void LdAE()
        {
            Console.WriteLine("ld l, e");
            AReg = EReg;
            RegisterPair p = UpdatePairedRegFrom8Bit(AReg, FReg);
            AFReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the contents of the H register into the A register.
        /// </summary>
        private void LdAH()
        {
            Console.WriteLine("ld l, h");
            AReg = HReg;
            RegisterPair p = UpdatePairedRegFrom8Bit(AReg, FReg);
            AFReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the contents of the L register into the A register.
        /// </summary>
        private void LdAL()
        {
            Console.WriteLine("ld l, l");
            AReg = LReg;
            RegisterPair p = UpdatePairedRegFrom8Bit(AReg, FReg);
            AFReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the contents of the A register into the A register.
        /// </summary>
        private void LdAA()
        {
            Console.WriteLine("ld l, a");
            AReg = AReg;
            RegisterPair p = UpdatePairedRegFrom8Bit(AReg, FReg);
            AFReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the A register with the memory contents pointed by register HL.
        /// </summary>

        private void LdARegFromHLPointer()
        {
            Console.WriteLine("ld a, ({0})", HLReg.ToString("X", ci));
            AReg = AddressSpace[HLReg];
            RegisterPair p = UpdatePairedRegFrom8Bit(AReg, FReg);
            AFReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Sets the carry flag.
        /// </summary>
        private void Scf()
        {

            Console.WriteLine("scf");
            FReg = SetCarryFlagState(true);
            RegisterPair p = UpdatePairedRegFrom8Bit(AReg, FReg);
            AFReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Clears the carry flag.
        /// </summary>
        private void Ccf()
        {
            Console.WriteLine("ccf");
            FReg = SetCarryFlagState(true);
            RegisterPair p = UpdatePairedRegFrom8Bit(AReg, FReg);
            AFReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads a constant into the L register.
        /// <param name="operand">The constant.</param>
        /// </summary>
        private void LdLConst(byte operand)
        {
            Console.WriteLine("ld l, 0x{0}", operand.ToString("X", ci));
            LReg = operand;
            RegisterPair p = UpdatePairedRegFrom8Bit(HReg, LReg);
            HLReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads a constant into the A register.
        /// <param name="operand">The constant.</param>
        /// </summary>
        private void LdAConst(byte operand)
        {
            Console.WriteLine("ld a, 0x{0}", operand.ToString("X", ci));
            AReg = operand;
            RegisterPair p = UpdatePairedRegFrom8Bit(AReg, FReg);
            AFReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Performs a relative jump if the zero flag is set.
        /// <param name="operand">A signed byte. This dictates how many bytes we branch forward.</param>
        /// </summary>
        private void JrZ(sbyte operand)
        {
            Console.WriteLine("jr z, 0x{0}", operand.ToString("X", ci));
            if (CheckZeroFlagState())
            {
                switch (operand)
                {
                    case >= 0:
                        ProgCounterReg += (ushort)operand;
                        break;
                    default:
                        ProgCounterReg -= (ushort)(operand * -1);
                        break;
                }
            }
            else
                ProgCounterReg++;
        }

        /// <summary>
        /// Loads the value of the B register into the pointer contained in the HL register.
        /// </summary>
        private void LdHLPointerWithBReg()
        {
            Console.WriteLine("ld (hl), b");
            AddressSpace[HLReg] = BReg;
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the value of the C register into the pointer contained in the HL register.
        /// </summary>
        private void LdHLPointerWithCReg()
        {
            Console.WriteLine("ld (hl), c");
            AddressSpace[HLReg] = CReg;
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the value of the D register into the pointer contained in the HL register.
        /// </summary>
        private void LdHLPointerWithDReg()
        {
            Console.WriteLine("ld (hl), d");
            AddressSpace[HLReg] = DReg;
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the value of the E register into the pointer contained in the HL register.
        /// </summary>
        private void LdHLPointerWithEReg()
        {
            Console.WriteLine("ld (hl), e");
            AddressSpace[HLReg] = EReg;
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the value of the H register into the pointer contained in the HL register.
        /// </summary>
        private void LdHLPointerWithHReg()
        {
            Console.WriteLine("ld (hl), h");
            AddressSpace[HLReg] = HReg;
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the value of the L register into the pointer contained in the HL register.
        /// </summary>
        private void LdHLPointerWithLReg()
        {
            Console.WriteLine("ld (hl), l");
            AddressSpace[HLReg] = LReg;
            ProgCounterReg++;
        }

        /// <summary>
        /// Loads the value of the A register into the pointer contained in the HL register.
        /// </summary>
        private void LdHLPointerWithAReg()
        {
            Console.WriteLine("ld (hl), a");
            AddressSpace[HLReg] = AReg;
            ProgCounterReg++;
        }

        /// <summary>
        /// Halts the CPU.
        /// </summary>
        private void Halt()
        {
            Console.WriteLine("halt");
            haltFlag = true;
        }

        /// <summary>
        /// Adds the B register to the A register.
        /// </summary>
        private void AddAB()
        {
            Console.WriteLine("add a, b");
            switch (BReg + AReg)
            {
                case > 0xFF:
                    AReg = 0;
                    SetParityOverflowState(true);
                    break;
                case 0:
                    SetZeroFlagState(true);
                    AReg = 0;
                    break;
                default:
                    AReg += BReg;
                    break;
            }
            RegisterPair p = UpdatePairedRegFrom8Bit(AReg, FReg);
            AFReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Adds the C register to the A register.
        /// </summary>
        private void AddAC()
        {
            Console.WriteLine("add a, c");
            switch (CReg + AReg)
            {
                case > 0xFF:
                    AReg = 0;
                    SetParityOverflowState(true);
                    break;
                case 0:
                    SetZeroFlagState(true);
                    AReg = 0;
                    break;
                default:
                    AReg += CReg;
                    break;
            }
            RegisterPair p = UpdatePairedRegFrom8Bit(AReg, FReg);
            AFReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Adds the D register to the A register.
        /// </summary>
        private void AddAD()
        {
            Console.WriteLine("add a, d");
            switch (DReg + AReg)
            {
                case > 0xFF:
                    AReg = 0;
                    SetParityOverflowState(true);
                    break;
                case 0:
                    SetZeroFlagState(true);
                    AReg = 0;
                    break;
                default:
                    AReg += DReg;
                    break;
            }
            RegisterPair p = UpdatePairedRegFrom8Bit(AReg, FReg);
            AFReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Adds the E register to the A register.
        /// </summary>
        private void AddAE()
        {
            Console.WriteLine("add a, e");
            switch (EReg + AReg)
            {
                case > 0xFF:
                    AReg = 0;
                    SetParityOverflowState(true);
                    break;
                case 0:
                    SetZeroFlagState(true);
                    AReg = 0;
                    break;
                default:
                    AReg += EReg;
                    break;
            }
            RegisterPair p = UpdatePairedRegFrom8Bit(AReg, FReg);
            AFReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Adds the H register to the A register.
        /// </summary>
        private void AddAH()
        {
            Console.WriteLine("add a, h");
            switch (HReg + AReg)
            {
                case > 0xFF:
                    AReg = 0;
                    SetParityOverflowState(true);
                    break;
                case 0:
                    SetZeroFlagState(true);
                    AReg = 0;
                    break;
                default:
                    AReg += HReg;
                    break;
            }
            RegisterPair p = UpdatePairedRegFrom8Bit(AReg, FReg);
            AFReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Adds the L register to the A register.
        /// </summary>
        private void AddAL()
        {
            Console.WriteLine("add a, l");
            switch (LReg + AReg)
            {
                case > 0xFF:
                    AReg = 0;
                    SetParityOverflowState(true);
                    break;
                case 0:
                    SetZeroFlagState(true);
                    AReg = 0;
                    break;
                default:
                    AReg += LReg;
                    break;
            }
            RegisterPair p = UpdatePairedRegFrom8Bit(AReg, FReg);
            AFReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Adds the A register to the A register.
        /// </summary>
        private void AddAA()
        {
            Console.WriteLine("add a, a");
            switch (AReg + AReg)
            {
                case > 0xFF:
                    AReg = 0;
                    SetParityOverflowState(true);
                    break;
                case 0:
                    SetZeroFlagState(true);
                    AReg = 0;
                    break;
                default:
                    AReg += AReg;
                    break;
            }
            RegisterPair p = UpdatePairedRegFrom8Bit(AReg, FReg);
            AFReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Adds the A register to the contents pointed by register HL.
        /// </summary>
        private void AddARegFromHLPointer()
        {
            Console.WriteLine("add a, (hl)");
            switch (AReg + AddressSpace[HLReg])
            {
                case > 0xFF:
                    AReg = 0;
                    SetParityOverflowState(true);
                    break;
                case 0:
                    SetZeroFlagState(true);
                    AReg = 0;
                    break;
                default:
                    AReg += AddressSpace[HLReg];
                    break;
            }
            RegisterPair p = UpdatePairedRegFrom8Bit(AReg, FReg);
            AFReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Adds the B register to the A register and then adds the eventual carry.
        /// </summary>
        private void AddABCarry()
        {
            Console.WriteLine("adc a, b");
            byte carryFlag = Convert.ToByte(CheckCarryFlagState());
            switch (AReg + BReg)
            {
                case > 0xFF:
                    AReg = carryFlag;
                    SetParityOverflowState(true);
                    break;
                case 0:
                    SetZeroFlagState(true);
                    AReg = carryFlag;
                    break;
                default:
                    AReg += BReg;
                    break;
            }
            RegisterPair p = UpdatePairedRegFrom8Bit(AReg, FReg);
            AFReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Adds the C register to the A register and then adds the eventual carry.
        /// </summary>
        private void AddACCarry()
        {
            Console.WriteLine("adc a, c");
            byte carryFlag = Convert.ToByte(CheckCarryFlagState());
            switch (AReg + CReg)
            {
                case > 0xFF:
                    AReg = carryFlag;
                    SetParityOverflowState(true);
                    break;
                case 0:
                    SetZeroFlagState(true);
                    AReg = carryFlag;
                    break;
                default:
                    AReg += CReg;
                    break;
            }
            RegisterPair p = UpdatePairedRegFrom8Bit(AReg, FReg);
            AFReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Adds the D register to the A register and then adds the eventual carry.
        /// </summary>
        private void AddADCarry()
        {
            Console.WriteLine("adc a, d");
            byte carryFlag = Convert.ToByte(CheckCarryFlagState());
            switch (AReg + DReg)
            {
                case > 0xFF:
                    AReg = carryFlag;
                    SetParityOverflowState(true);
                    break;
                case 0:
                    SetZeroFlagState(true);
                    AReg = carryFlag;
                    break;
                default:
                    AReg += DReg;
                    break;
            }
            RegisterPair p = UpdatePairedRegFrom8Bit(AReg, FReg);
            AFReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Adds the E register to the A register and then adds the eventual carry.
        /// </summary>
        private void AddAECarry()
        {
            Console.WriteLine("adc a, e");
            byte carryFlag = Convert.ToByte(CheckCarryFlagState());
            switch (AReg + EReg)
            {
                case > 0xFF:
                    AReg = carryFlag;
                    SetParityOverflowState(true);
                    break;
                case 0:
                    SetZeroFlagState(true);
                    AReg = carryFlag;
                    break;
                default:
                    AReg += EReg;
                    break;
            }
            RegisterPair p = UpdatePairedRegFrom8Bit(AReg, FReg);
            AFReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Adds the H register to the A register and then adds the eventual carry.
        /// </summary>
        private void AddAHCarry()
        {
            Console.WriteLine("adc a, h");
            byte carryFlag = Convert.ToByte(CheckCarryFlagState());
            switch (AReg + HReg)
            {
                case > 0xFF:
                    AReg = carryFlag;
                    SetParityOverflowState(true);
                    break;
                case 0:
                    SetZeroFlagState(true);
                    AReg = carryFlag;
                    break;
                default:
                    AReg += HReg;
                    break;
            }
            RegisterPair p = UpdatePairedRegFrom8Bit(AReg, FReg);
            AFReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Adds the L register to the A register and then adds the eventual carry.
        /// </summary>
        private void AddALCarry()
        {
            Console.WriteLine("adc a, l");
            byte carryFlag = Convert.ToByte(CheckCarryFlagState());
            switch (AReg + LReg)
            {
                case > 0xFF:
                    AReg = carryFlag;
                    SetParityOverflowState(true);
                    break;
                case 0:
                    SetZeroFlagState(true);
                    AReg = carryFlag;
                    break;
                default:
                    AReg += LReg;
                    break;
            }
            RegisterPair p = UpdatePairedRegFrom8Bit(AReg, FReg);
            AFReg = p.GetRegPair();
            ProgCounterReg++;
        }

        /// <summary>
        /// Adds the A register to the A register and then adds the eventual carry.
        /// </summary>
        private void AddAACarry()
        {
            Console.WriteLine("adc a, a");
            byte carryFlag = Convert.ToByte(CheckCarryFlagState());
            switch (AReg + AReg)
            {
                case > 0xFF:
                    AReg = carryFlag;
                    SetParityOverflowState(true);
                    break;
                case 0:
                    SetZeroFlagState(true);
                    AReg = carryFlag;
                    break;
                default:
                    AReg += AReg;
                    break;
            }
            RegisterPair p = UpdatePairedRegFrom8Bit(AReg, FReg);
            AFReg = p.GetRegPair();
            ProgCounterReg++;
        }

        // support functions begin here

        private byte SetCarryFlagState(bool toggle)
        {
            return SetBitInByte(FReg, toggle, 7);
        }

        private byte SetAddSubFlagState(bool toggle) // needed for BCD mode
        {
            return SetBitInByte(FReg, toggle, 6);
            // setting this flag should not be needed, since we don't have to deal with dumb silicon but with smart silicon.
        }

        private byte SetParityOverflowState(bool toggle)
        {
            return SetBitInByte(FReg, toggle, 5);
            // reminder to citizens: failure to understand usage of the parity/overflow flag for IO communication with IN and OUT instructions
            // is ground for immediate off-world relocation.
        }

        private byte SetHalfCarryState(bool toggle)
        {
            return SetBitInByte(FReg, toggle, 3);
            // setting this flag should NOT be needed as we don't have a 4 bit ALU on modern CPUs. Might be worth revisiting for accuracy.
        }

        private byte SetZeroFlagState(bool toggle)
        {
            return SetBitInByte(FReg, toggle, 1);
        }

        private byte SetSignFlagState(bool toggle)
        {
            return SetBitInByte(FReg, toggle, 0);
        }

        private bool CheckCarryFlagState()
        {
            return VerifyBitInByteStatus(FReg, 7);
        }

        private bool CheckZeroFlagState()
        {
            return VerifyBitInByteStatus(FReg, 1);
        }

        private static byte SetBitInByte(byte input, bool status, int bitIndex)
        {
            BitArray ba = new BitArray(input)
            {
                [bitIndex] = status // uhm ok.
            };
            return ConvertToByte(ba);
        }

        private static bool VerifyBitInByteStatus(byte input, int bitIndex)
        {
            return new BitArray(input)[bitIndex];
        }

        private void ManageNMI() // "manage"
        {
            ProgCounterReg = NMIVector;
        }

        private void RegisterStatus()
        {
            Console.Write("a: 0x{0}", AReg.ToString("X", ci));
            Console.Write(" b: 0x{0}", BReg.ToString("X", ci));
            Console.Write(" c: 0x{0}", CReg.ToString("X", ci));
            Console.Write(" d: 0x{0}", DReg.ToString("X", ci));
            Console.Write(" e: 0x{0}", EReg.ToString("X", ci));
            Console.Write(" f: 0x{0}", FReg.ToString("X", ci));
            Console.Write(" h: 0x{0}", HReg.ToString("X", ci));
            Console.WriteLine(" l: 0x{0}", LReg.ToString("X", ci));
            Console.Write("af: 0x{0}", AFReg.ToString("X", ci));
            Console.Write(" bc: 0x{0}", BCReg.ToString("X", ci));
            Console.Write(" de: 0x{0}", DEReg.ToString("X", ci));
            Console.WriteLine(" hl: 0x{0}", HLReg.ToString("X", ci));
            /*Console.Write("a': " + ARegSec);
            Console.Write(" b': " + BRegSec);
            Console.Write(" c': " + CRegSec);
            Console.Write(" d': " + DRegSec);
            Console.Write(" e': " + ERegSec);
            Console.Write(" f': " + FRegSec);
            Console.Write(" h': " + HRegSec);
            Console.WriteLine(" l': " + LRegSec);*/
            Console.WriteLine("pc: 0x{0}\n", ProgCounterReg.ToString("X", ci));
        }

        /// <summary>
        /// This function lets the user see the contents of the address space.
        /// </summary>
        private void CheckMemoryState()
        {
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
            ushort pcCopy = ProgCounterReg;
            try
            {
                Console.WriteLine("\naddr: 0x{0}\t val: 0x{1}", pcCopy.ToString("X", ci), AddressSpace[ProgCounterReg].ToString("X", ci));
                pcCopy++;
                Console.WriteLine("addr: 0x{0}\t val: 0x{1}", pcCopy.ToString("X", ci), AddressSpace[pcCopy].ToString("X", ci));
                pcCopy++;
                Console.WriteLine("addr: 0x{0}\t val: 0x{1}", pcCopy.ToString("X", ci), AddressSpace[pcCopy].ToString("X", ci));
                pcCopy++;
                Console.WriteLine("addr: 0x{0}\t val: 0x{1}", pcCopy.ToString("X", ci), AddressSpace[pcCopy].ToString("X", ci));
                pcCopy++;
                Console.WriteLine("addr: 0x{0}\t val: 0x{1}", pcCopy.ToString("X", ci), AddressSpace[pcCopy].ToString("X", ci));
                pcCopy++;
                Console.WriteLine("addr: 0x{0}\t val: 0x{1}", pcCopy.ToString("X", ci), AddressSpace[pcCopy].ToString("X", ci));
                pcCopy++;
                Console.WriteLine("addr: 0x{0}\t val: 0x{1}", pcCopy.ToString("X", ci), AddressSpace[pcCopy].ToString("X", ci));
                pcCopy++;
                Console.WriteLine("addr: 0x{0}\t val: 0x{1}\n", pcCopy.ToString("X", ci), AddressSpace[pcCopy].ToString("X", ci));
            }
            catch (IndexOutOfRangeException)
            {
                ProgCounterReg = 0;
                CheckMemoryState();
            }

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

        private void InsertDataInstrInAddrSpc()
        {
            switch (quiet)
            {
                case false:
                    Console.WriteLine("Insert the address to insert the data: ");
                    break;
            }
            String a = Console.ReadLine()!;
            ushort addr = 0x0000;
            try
            {
                addr = ushort.Parse(a);
                switch (quiet)
                {
                    case false:
                        Console.WriteLine("Address: 0x{0}", addr.ToString("X", ci));
                        break;
                }
            }
            catch (FormatException)
            {
                Console.WriteLine($"Unable to parse '{a}'");
            }
            {
                switch (quiet)
                {
                    case false:
                        Console.WriteLine("Insert a byte to be put in memory: ");
                        break;
                }
                String d = Console.ReadLine()!;
                byte data = 0x00;
                try
                {
                    data = byte.Parse(d);
                    switch (quiet)
                    {
                        case false:
                            Console.WriteLine("Data: 0x{0}", data.ToString("X", ci));
                            break;
                    }
                }
                catch (FormatException)
                {
                    Console.WriteLine($"Unable to parse '{d}'");
                }
                AddressSpace[addr] = data;
                switch (quiet)
                {
                    case false:
                        Console.WriteLine("Inserted byte 0x{0} at location 0x{1}.", data.ToString("X", ci), addr.ToString("X", ci));
                        break;
                }

            }
        }

    }
}