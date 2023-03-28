//using System;
//using System.Drawing;
//using ImageMagick;

//namespace Util
//{
//    public static class TextureUtil
//    {
//        public struct Tuple
//        {
//            public bool Item1;
//            public int Item2;
//            public float Item3;

//            public Tuple(bool item1, int item2, float item3)
//            {
//                this.Item1 = item1;
//                this.Item2 = item2;
//                this.Item3 = item3;
//            }
//        }

//        public static void EXR2HDRPNG(string filePath)
//        {
//            int width = 0;
//            int height = 0;

//            MagickReadSettings settings = new MagickReadSettings();
//            settings.Format = MagickFormat.Exr;

//            // Read from file.
//            MagickImage image = new MagickImage(filePath, settings);

//            width = image.Width;
//            height = image.Height;

//            float[] pixelBuffer = new float[width * height * 4];
//            float[] fixedPixelBuffer = new float[width * height * 4];
//            byte[] rgbeBuffer = new byte[width * height * 4];
//            int count = 0;

//            IPixelCollection pixelCollection = image.GetPixels();
//            pixelBuffer = pixelCollection.GetValues();

//            // 修正參數是2^16,自己總結出來的，目前測試的是正確的
//            int fixConst = 65535;
//            for (int y = 0; y < height; y++)
//            {
//                for (int x = 0; x < width; x++)
//                {
//                    fixedPixelBuffer[count++] = pixelBuffer[((y) * width + x) * 4] / fixConst;
//                    fixedPixelBuffer[count++] = pixelBuffer[((y) * width + x) * 4 + 1] / fixConst;
//                    fixedPixelBuffer[count++] = pixelBuffer[((y) * width + x) * 4 + 2] / fixConst;
//                    fixedPixelBuffer[count++] = pixelBuffer[((y) * width + x) * 4 + 3] / fixConst;
//                }
//            }

//            // 把HDR的Float類型轉成RGBE類型的像素
//            FloatRGB2RGBE(width * height, fixedPixelBuffer, rgbeBuffer);

//            // save as png
//            Bitmap bmp = new Bitmap(width, height);
//            for (int y = 0; y < height; ++y)
//                for (int x = 0; x < width; ++x)
//                {
//                    int offset = (y * width + x) * 4;
//                    bmp.SetPixel(x, y, Color.FromArgb
//                        (rgbeBuffer[offset + 3], rgbeBuffer[offset + 0], rgbeBuffer[offset + 1], rgbeBuffer[offset + 2]));
//                }
//            int lastDotIndex = filePath.LastIndexOf('.');
//            string outputPath = filePath.Substring(0, lastDotIndex) + ".png";
//            bmp.Save(outputPath);

//        }

//        // 把HDR的Float類型轉成RGBE類型的像素
//        private static void FloatRGB2RGBE(int InImageSize, float[] InPixelBuffer, byte[] OutRGBEBuffer)
//        {

//            for (int i = 0; i < InImageSize; i++)
//            {
//                float red = InPixelBuffer[i * 4 + 0];
//                float green = InPixelBuffer[i * 4 + 1];
//                float blue = InPixelBuffer[i * 4 + 2];

//                float maxValue = red;
//                if (green > maxValue) maxValue = green;
//                if (blue > maxValue) maxValue = blue;

//                if (maxValue < Single.Epsilon)
//                {
//                    OutRGBEBuffer[i * 4 + 0] = OutRGBEBuffer[i * 4 + 1]
//                        = OutRGBEBuffer[i * 4 + 2] = OutRGBEBuffer[i * 4 + 3] = 0;
//                }
//                else
//                {
//                    int exponent = 0;
//                    float mantissa = 0;
//                    Tuple tuple = SHARP_frexp(maxValue);
//                    if (tuple.Item1)
//                    {
//                        OutRGBEBuffer[i * 4 + 0] = OutRGBEBuffer[i * 4 + 1]
//                            = OutRGBEBuffer[i * 4 + 2] = OutRGBEBuffer[i * 4 + 3] = 0;
//                        return;
//                    }
//                    else
//                    {
//                        exponent = tuple.Item2;
//                        mantissa = tuple.Item3;
//                        // 当某个值为v的时候，其尾数就是e[0]。 这里*256了，所以反向的时候有个/256即-(128+8)里的8
//                        // e[0]永远不会为1所以结果<256
//                        float scaleRatio = (float)(mantissa * 256.0 / maxValue);
//                        OutRGBEBuffer[i * 4 + 0] = (byte)(red * scaleRatio);
//                        OutRGBEBuffer[i * 4 + 1] = (byte)(green * scaleRatio);
//                        OutRGBEBuffer[i * 4 + 2] = (byte)(blue * scaleRatio);
//                        OutRGBEBuffer[i * 4 + 3] = (byte)(exponent + 128);
//                    }
//                }
//            }

//        }

//        // 把一個浮點數轉換成X*2^N的形式 
//        // 比如8.000000 = 0.500000 * 2^4，123.45 = 0.964453 * 2^7
//        private static Tuple SHARP_frexp(double d)
//        {

//            // Translate the double into sign, exponent and mantissa.
//            long bits = BitConverter.DoubleToInt64Bits(d);
//            // Note that the shift is sign-extended, hence the test against -1 not 1
//            bool negative = (bits < 0);
//            int exponent = (int)((bits >> 52) & 0x7ffL);
//            long mantissa = bits & 0xfffffffffffffL;

//            // Subnormal numbers; exponent is effectively one higher,
//            // but there's no extra normalisation bit in the mantissa
//            if (exponent == 0)
//            {
//                exponent++;
//            }
//            // Normal numbers; leave exponent as it is but add extra
//            // bit to the front of the mantissa
//            else
//            {
//                mantissa = mantissa | (1L << 52);
//            }

//            // Bias the exponent. It's actually biased by 1023, but we're
//            // treating the mantissa as m.0 rather than 0.m, so we need
//            // to subtract another 52 from it.
//            exponent -= 1075;

//            if (mantissa == 0)
//            {
//                return new Tuple(false, 0, 0);
//            }

//            /* Normalize */
//            while ((mantissa & 1) == 0)
//            {    /*  i.e., Mantissa is even */
//                mantissa >>= 1;
//                exponent++;
//            }

//            // 原來的算法是m.0*2^n 要換成0.m*2^n
//            float floatMantissa = mantissa;
//            while (floatMantissa > 1 - Single.Epsilon)
//            {
//                floatMantissa /= 2;
//                exponent++;
//            }

//            return new Tuple(negative, exponent, floatMantissa);

//        }

//    }
//}



