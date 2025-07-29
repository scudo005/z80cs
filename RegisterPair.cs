namespace z80cs
{
    class RegisterPair(ushort regPair, byte lowByte, byte highByte)
    {
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
}