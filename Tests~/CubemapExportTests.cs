using System;
using UnityEngine;

// Tests the production pixel conversion and material JSON without native Unity calls.
public static class CubemapExportTests
{
    private static int assertions;

    private static void Check(bool condition, string label)
    {
        assertions++;
        if (!condition) throw new Exception("Cubemap: " + label);
    }

    // Standard cube sampling basis. A direction-coloured cube gives a continuous,
    // asymmetric reference on all six faces, including corners and shared edges.
    private static Color DirectionColour(CubemapFace face, double u, double v, bool reflectX)
    {
        double x, y, z;
        switch (face)
        {
            case CubemapFace.PositiveX: x = 1; y = -v; z = -u; break;
            case CubemapFace.NegativeX: x = -1; y = -v; z = u; break;
            case CubemapFace.PositiveY: x = u; y = 1; z = v; break;
            case CubemapFace.NegativeY: x = u; y = -1; z = -v; break;
            case CubemapFace.PositiveZ: x = u; y = -v; z = 1; break;
            case CubemapFace.NegativeZ: x = -u; y = -v; z = -1; break;
            default: throw new ArgumentOutOfRangeException("face");
        }
        if (reflectX) x = -x;
        double length = Math.Sqrt(x * x + y * y + z * z);
        return new Color((float)(0.5 + x / length * 0.5),
            (float)(0.5 + y / length * 0.5), (float)(0.5 + z / length * 0.5),
            (float)(0.5 + (x + 2 * y + 3 * z) / length * 0.05));
    }

    private static void TestDirections(int size)
    {
        // IDE imports left/right/top/bottom/front/back into KTX +X/-X/+Y/-Y/+Z/-Z.
        CubemapFace[] sourceFaces = {
            CubemapFace.NegativeX, CubemapFace.PositiveX,
            CubemapFace.PositiveY, CubemapFace.NegativeY,
            CubemapFace.PositiveZ, CubemapFace.NegativeZ
        };
        CubemapFace[] targetFaces = {
            CubemapFace.PositiveX, CubemapFace.NegativeX,
            CubemapFace.PositiveY, CubemapFace.NegativeY,
            CubemapFace.PositiveZ, CubemapFace.NegativeZ
        };
        for (int face = 0; face < 6; face++)
        {
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    pixels[y * size + x] = DirectionColour(sourceFaces[face],
                        2.0 * x / (size - 1) - 1, 2.0 * y / (size - 1) - 1, false);

            CustomShaderExporter.ReorientCubemapFacePixels(pixels);

            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // Texture2D.SetPixels/EncodeToPNG writes its bottom-first array
                    // as top-first PNG rows. KTX retains those PNG rows.
                    Color actual = pixels[(size - 1 - y) * size + x];
                    Color expected = DirectionColour(targetFaces[face],
                        2.0 * x / (size - 1) - 1, 2.0 * y / (size - 1) - 1, true);
                    string label = targetFaces[face] + " pixel " + x + "," + y;
                    Check(Math.Abs(expected.r - actual.r) < 0.00001f, label + " R");
                    Check(Math.Abs(expected.g - actual.g) < 0.00001f, label + " G");
                    Check(Math.Abs(expected.b - actual.b) < 0.00001f, label + " B");
                    Check(Math.Abs(expected.a - actual.a) < 0.00001f, label + " A");
                }
        }
    }

    private static void TestReferences(bool mipmap)
    {
        var data = new JSONObject(JSONObject.Type.OBJECT);
        data.AddField("cubemapSize", 1024);
        data.AddField("generateMipmap", mipmap);
        data.AddField("sRGB", true);
        data.AddField("filterMode", 1);
        const string uuid = "00000000-0000-0000-0000-000000000001";
        var first = CustomShaderExporter.CreateCubemapTextureReference("u_Cube", uuid, data);
        var shared = CustomShaderExporter.CreateCubemapTextureReference("u_Environment", uuid, data);
        foreach (var reference in new[] { first, shared })
        {
            var roundtrip = new JSONObject(reference.Print());
            var parameters = roundtrip["constructParams"];
            Check(roundtrip["path"].str == "res://" + uuid, "shared resource UUID");
            Check(parameters[0].i == 1024 && parameters[1].i == 1024, "face dimensions");
            Check(parameters[2].i == 1, "Laya RGBA8 texture format");
            Check(parameters[3].b == mipmap, "material mipmap agrees with cubemap settings");
            Check(!parameters[4].b && parameters[5].b, "readability and sRGB");
            Check(roundtrip["propertyParams"]["filterMode"].i == 1, "filter mode");
        }
        Check(first["name"].str == "u_Cube", "first mapped uniform name");
        Check(shared["name"].str == "u_Environment", "shared mapped uniform name");
    }

    public static void Run()
    {
        assertions = 0;
        TestDirections(4);
        TestDirections(5);
        TestReferences(true);
        TestReferences(false);
        Console.WriteLine("PASS: " + assertions + " cubemap direction and material reference assertions.");
    }
}
