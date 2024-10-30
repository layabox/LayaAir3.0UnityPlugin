using System.IO;
using UnityEngine;
using System.Runtime.InteropServices;

public class TextureUtils 
{
    [DllImport("msvcrt.dll")]
    private static extern double frexp(double val, out int eptr);

    public static float gammaColorsToLinear(float f,bool isgamma)
    {
        if (isgamma)
        {
            return Mathf.LinearToGammaSpace(f);
        }
        else
        {
            return f;
        }
    }
    private static byte[] float2rgbe(float r, float g, float b, bool isgamma)
    {
        byte[] res = new byte[4] { 0, 0, 0, 0 };
      
        float v = Mathf.Max(r, g, b);
        if (v > 1e-32f)
        {
            int e = 0;
            float result = (float)frexp(v, out e) *256.0f/ v;
            e += 128;
            byte r1 = (byte)(r * result);
            byte g1 = (byte)(g * result);
            byte b1 = (byte)(b * result);
            res[0] = r1;
            res[1] = g1;
            res[2] = b1;
            res[3] = ((r1>0) || (g1 > 0) || (b1 > 0)) ?(byte)e : (byte)0u;
        }
        return res;
    }



    private static Texture2D GetTexure2d(Texture2D texture)
    {
        if (texture.isReadable)
        {
            return texture;
        }
        else
        {
            RenderTexture tmp = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.Default, RenderTextureReadWrite.Linear);
            Graphics.Blit(texture, tmp);
            Texture2D copyTexture = new Texture2D(texture.width, texture.height);
            copyTexture.ReadPixels(new Rect(0, 0, tmp.width, tmp.height), 0, 0);
            copyTexture.Apply();
            return copyTexture;
        }
    }


    private static void Color2Rgbe(Color color,byte[] bytes, uint off, uint width)
    {
        float r = color.r;
        float b = color.b;
        float g = color.g;
        float maxv = Mathf.Max(r, g, b);
        if (maxv > 1e-32f)
        {
            int e = 0;
            float result = (float)frexp(maxv, out e) * 256.0f / maxv;
            bytes[off+0] = (byte)(r * result);
            bytes[off + width] = (byte)(g * result);
            bytes[off + 2* width] = (byte)(b * result);
            bytes[off + 3* width] =(byte)(e+128);
        }
        else
        {
            bytes[off + 0] = 0;
            bytes[off + 1] = 0;
            bytes[off + 2] = 0;
            bytes[off + 3] = 0;
        }
    }

    public static void SaveTexture2D(Texture2D texture, string savePath, bool isPng)
    {
        FileStream file = File.Open(savePath, FileMode.Create, FileAccess.ReadWrite);
        BinaryWriter writer = new BinaryWriter(file);
        Texture2D outData = GetTexure2d(texture);
        if (isPng)
        {
            writer.Write(outData.EncodeToPNG());
        }
        else
        {
            writer.Write(outData.EncodeToJPG());
        }

        file.Close();
    }

    private static void WriteRLE(BinaryWriter writer, Color[] pixels, uint width, uint row)
    {
        writer.Write((byte)2);
        writer.Write((byte)2);
        writer.Write((byte)(width >> 8));
        writer.Write((byte)(width & 0xff));
        byte[] colors = new byte[width * 4];
        byte[] copyByte = new byte[127];
        for (uint i = 0; i < width; i++)
        {
            Color2Rgbe(pixels[row * width + i], colors, i, width);
        }
        for (uint channel = 0; channel < 4; channel++)
        {
            uint pixOff = channel * width;

            for (uint pixelCount = 0; pixelCount < width;)
            {
                uint spanLen = 1;
                while (pixelCount + spanLen < width && spanLen < 127)
                {
                    if (colors[pixOff + pixelCount] == colors[pixOff + pixelCount + spanLen])
                    {
                        spanLen++;
                    }
                    else
                    {
                        break;
                    }
                }
                if (spanLen > 1)
                {
                    writer.Write((byte)(spanLen + 128));
                    byte data = colors[pixOff + pixelCount];
                    writer.Write((byte)data);
                    pixelCount += spanLen;
                }
                else
                {

                    copyByte[0] = colors[pixOff + pixelCount];
                    spanLen = 1;
                    while (pixelCount + spanLen < width && spanLen < 127)
                    {
                        if (copyByte[spanLen - 1] != colors[pixOff + pixelCount + spanLen])
                        {
                            copyByte[spanLen] = colors[pixOff + pixelCount + spanLen];
                            spanLen++;
                        }
                        else
                        {
                            break;
                        }
                    }
                    writer.Write((byte)spanLen);
                    writer.Write(copyByte, 0, (int)(spanLen));
                    pixelCount += spanLen;

                }
            }
        }
    }

    public static void SaveHDRTexute2D(Texture2D texture, string savePath)
    {
        Texture2D outData = GetTexure2d(texture);
        Color[] pixels = outData.GetPixels(0);
        int width = texture.width;
        int height = texture.height;
        bool isgamma = QualitySettings.activeColorSpace == ColorSpace.Gamma;

        FileStream file = File.Open(savePath, FileMode.Create, FileAccess.ReadWrite);
        BinaryWriter writer = new BinaryWriter(file);
        //read file header
        string str = "#?RADIANCE\n";
        writer.Write(str.ToCharArray());
        str = "FORMAT=32-bit_rle_rgbe\n";
        writer.Write(str.ToCharArray());
        str = "\n";
        writer.Write(str.ToCharArray());
        str = "-Y " + height + " +X " + width + "\n";
        writer.Write(str.ToCharArray());
        for (int i = 0; i <height; ++i)
        {
            WriteRLE(writer, pixels, (uint)width, (uint)(  i));
        }
        file.Close();
    }


}