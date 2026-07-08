using System.Text;
using System.Text.RegularExpressions;

namespace LayaAir3.Converter
{
    /// <summary>
    /// VFX 转换器主驱动用到的纯字符串/枚举 helper。C# 移植自 unity-vfx-to-laya.js：
    /// unityBlendModeToLaya / unityTypeToGlslType / unityTypeToLayaPropType / sanitizePropName。
    /// </summary>
    public static class VfxHelpers
    {
        /// <summary>Unity VFXAbstractParticleOutput.BlendMode 整数转 Laya blendMode 字符串。</summary>
        public static string UnityBlendModeToLaya(int blendModeInt)
        {
            switch (blendModeInt)
            {
                case 0: return "Additive";
                case 1: return "Alpha";
                case 2: return "Premultiplied";
                case 3: return "Opaque";
                case 4: return "Opaque";   // Masked 无 Laya 对应，降级 Opaque
                default: return "Alpha";
            }
        }

        /// <summary>Unity m_SerializableType 转 IDE GLSL 类型（可能返回 null）。</summary>
        public static string UnityTypeToGlslType(string unityType)
        {
            if (string.IsNullOrEmpty(unityType)) return null;
            string t = unityType.ToLowerInvariant();
            if (t.Contains("single") || t.Contains("float")) return "float";
            if (t.Contains("int32") || t.Contains("int64")) return "int";
            if (t.Contains("uint32") || t.Contains("uint64")) return "uint";
            if (t.Contains("boolean") || t.Contains("bool")) return "bool";
            if (t.Contains("vector2")) return "vec2";
            if (t.Contains("vector3")) return "vec3";
            if (t.Contains("vector4")) return "vec4";
            if (t.Contains("color")) return "color";
            if (t.Contains("position") || t.Contains("direction") || t.Contains("vector")) return "vec3";
            return null;
        }

        /// <summary>Unity m_SerializableType 转 Laya 属性类型（默认 float）。</summary>
        public static string UnityTypeToLayaPropType(string serializableType)
        {
            if (string.IsNullOrEmpty(serializableType)) return "float";
            string t = serializableType.ToLowerInvariant();
            if (t.Contains("single")) return "float";
            if (t.Contains("int32") || t.Contains("int64")) return "int";
            if (t.Contains("uint32") || t.Contains("uint64")) return "uint";
            if (t.Contains("boolean")) return "bool";
            if (t.Contains("vector2")) return "vec2";
            if (t.Contains("vector3")) return "vec3";
            if (t.Contains("vector4")) return "vec4";
            if (t.Contains("color")) return "color";
            if (t.Contains("animationcurve")) return "Curve";
            if (t.Contains("gradient")) return "Gradient";
            if (t.Contains("mesh")) return "Mesh";
            if (t.Contains("texture2d")) return "Texture2D";
            if (t.Contains("texture3d")) return "Texture3D";
            if (t.Contains("texturecube")) return "TextureCube";
            if (t.Contains("transform")) return "Transform";
            return "float";
        }

        /// <summary>把 Unity exposed property 名规整为合法 GLSL 标识符。</summary>
        public static string SanitizePropName(string name)
        {
            string s = (name ?? "").Trim();
            s = Regex.Replace(s, "[^A-Za-z0-9_]", "_");
            s = Regex.Replace(s, @"^(\d)", "_$1");
            return s;
        }
    }
}
