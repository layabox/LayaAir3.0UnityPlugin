using System.IO;
internal class HalfFloatUtils
{
    private static MemoryStream buffer = new MemoryStream(4);
    private static BinaryReader bufferReader = new BinaryReader(buffer);
    private static BinaryWriter bufferWriter = new BinaryWriter(buffer);

    private static uint[] baseTable = new uint[512];
    private static uint[] shiftTable = new uint[512];

    private static uint[] mantissaTable = new uint[2048];
    private static uint[] exponentTable = new uint[64];
    private static uint[] offsetTable = new uint[64];


    static HalfFloatUtils()
    {
        for (int i = 0; i < 256; ++i)
        {
            int e = i - 127;
            // very small number (0, -0)
            if (e < -27)
            {
                baseTable[i | 0x000] = 0x0000;
                baseTable[i | 0x100] = 0x8000;
                shiftTable[i | 0x000] = 24;
                shiftTable[i | 0x100] = 24;

                // small number (denorm)
            }
            else if (e < -14)
            {
                baseTable[i | 0x000] = (uint)(0x0400 >> (-e - 14));
                baseTable[i | 0x100] = (uint)((0x0400 >> (-e - 14)) | 0x8000);
                shiftTable[i | 0x000] = (uint)(-e - 1);
                shiftTable[i | 0x100] = (uint)(-e - 1);

                // normal number
            }
            else if (e <= 15)
            {
                baseTable[i | 0x000] = (uint)((e + 15) << 10);
                baseTable[i | 0x100] = (uint)(((e + 15) << 10) | 0x8000);
                shiftTable[i | 0x000] = 13;
                shiftTable[i | 0x100] = 13;

                // large number (Infinity, -Infinity)
            }
            else if (e < 128)
            {
                baseTable[i | 0x000] = 0x7c00;
                baseTable[i | 0x100] = 0xfc00;
                shiftTable[i | 0x000] = 24;
                shiftTable[i | 0x100] = 24;

                // stay (NaN, Infinity, -Infinity)
            }
            else
            {
                baseTable[i | 0x000] = 0x7c00;
                baseTable[i | 0x100] = 0xfc00;
                shiftTable[i | 0x000] = 13;
                shiftTable[i | 0x100] = 13;
            }
        }

        mantissaTable[0] = 0;
        for (int i = 1; i < 1024; ++i)
        {
            int m = i << 13;    // zero pad mantissa bits
            int e = 0;          // zero exponent

            // normalized
            while ((m & 0x00800000) == 0)
            {
                e -= 0x00800000;    // decrement exponent
                m <<= 1;
            }

            m &= ~0x00800000;   // clear leading 1 bit
            e += 0x38800000;    // adjust bias

            mantissaTable[i] = (uint)(m | e);
        }
        for (int i = 1024; i < 2048; ++i)
        {
            mantissaTable[i] = (uint)(0x38000000 + ((i - 1024) << 13));
        }

        exponentTable[0] = 0;
        for (int i = 1; i < 31; ++i)
        {
            exponentTable[i] = (uint)(i << 23);
        }
        exponentTable[31] = 0x47800000;
        exponentTable[32] = 0x80000000;
        for (int i = 33; i < 63; ++i)
        {
            exponentTable[i] = (uint)(0x80000000 + ((i - 32) << 23));
        }
        exponentTable[63] = 0xc7800000;

        offsetTable[0] = 0;
        for (int i = 1; i < 64; ++i)
        {
            if (i == 32)
            {
                offsetTable[i] = 0;
            }
            else
            {
                offsetTable[i] = 1024;
            }
        }
    }

    /**
     * round a number to a half float number bits.
     * @param {number} num
     */
    public static ushort roundToFloat16Bits(float num)
    {
        buffer.Position = 0;
        bufferWriter.Write(num);
        buffer.Position = 0;
        uint f = bufferReader.ReadUInt32();
        uint e = (f >> 23) & 0x1ff;

        return (ushort)(baseTable[e] + ((f & 0x007fffff) >> (int)shiftTable[e]));
    }

    /**
     * convert a half float number bits to a number.
     * @param {number} float16bits - half float number bits
     */
    private static float convertToNumber(uint float16bits)
    {
        uint m = float16bits >> 10;
        buffer.Position = 0;
        bufferWriter.Write(mantissaTable[offsetTable[m] + (float16bits & 0x3ff)] + exponentTable[m]);
        buffer.Position = 0;
        return bufferReader.ReadSingle();
    }

}

