using System;
using System.Collections.Generic;
using System.Globalization;

namespace LayaAir3.Converter
{
    /// <summary>function/custom 节点的 GLSL 配置（对应 JS customGlsl 返回值）。</summary>
    public class CustomGlslCfg
    {
        public string[] ParamNames;
        public string[] ParamTypes;
        public string OutputType;
        public string Code;
    }

    /// <summary>Unity 节点类型 → Laya 节点定义（对应 JS NODE_MAPPING 的一条）。</summary>
    public class NodeMap
    {
        public string Id;
        public int Ver;                 // 0 = 无 ver 字段
        public string[] Inputs;
        public string[] Outputs;
        public bool Saturate;
        public bool ColorAsVec4;
        public bool CustomFunction;
        public Func<Jval, ShaderGraphConverter, CustomGlslCfg> CustomGlsl;
    }

    /// <summary>
    /// 映射表：对应 JS unity-shader-to-laya.js 顶部的 PBR_FRAGMENT_SLOTS / BLOCK_TO_* / VERTEX_SLOTS /
    /// UNLIT_FRAGMENT_SLOTS / NODE_MAPPING。
    /// </summary>
    public static class SgNodeMapping
    {
        // PBR_fragment 22 个 input（0-indexed，来自真实 IDE ShaderBlueprint.bps）
        public static readonly Dictionary<string, int> PBR_FRAGMENT_SLOTS = new Dictionary<string, int>
        {
            {"NormalTS",1},{"BaseColor",2},{"Metallic",3},{"Specular",4},{"SpecularColor",5},
            {"Smoothness",6},{"Occlusion",7},{"EmissionColor",8},{"Opacity",9},
        };

        // Master Stack BlockNode 名 → PBR_fragment 输入槽名。
        // 约定：字典不含该 key = undefined（未知块，warn）；含 key 但值为 null = 已知但跳过。
        public static readonly Dictionary<string, string> BLOCK_TO_PBR_SLOT = new Dictionary<string, string>
        {
            {"BaseColor","BaseColor"},
            {"NormalTS","NormalTS"},{"Normal (Tangent Space)","NormalTS"},{"Normal","NormalTS"},
            {"Metallic","Metallic"},{"Specular","Specular"},{"Smoothness","Smoothness"},
            {"Occlusion","Occlusion"},{"AmbientOcclusion","Occlusion"},
            {"Emission","EmissionColor"},{"Alpha","Opacity"},
            {"AlphaClipThreshold",null},
        };

        // Vertex 终端 3 input：0:positionOS 1:normalOS 2:tangentOS
        public static readonly Dictionary<string, int> VERTEX_SLOTS = new Dictionary<string, int>
        {
            {"Position",0},{"Normal",1},{"Tangent",2},
        };

        // Unlit_fragment 3 input：0:NormalTS 1:Color 2:Alpha
        public static readonly Dictionary<string, int> UNLIT_FRAGMENT_SLOTS = new Dictionary<string, int>
        {
            {"NormalTS",0},{"Color",1},{"Alpha",2},
        };

        public static readonly Dictionary<string, string> BLOCK_TO_UNLIT_SLOT = new Dictionary<string, string>
        {
            {"BaseColor","Color"},
            {"NormalTS","NormalTS"},{"Normal (Tangent Space)","NormalTS"},{"Normal","NormalTS"},
            {"Emission","Color"},{"Alpha","Alpha"},
            {"AlphaClipThreshold",null},
            {"Metallic",null},{"Specular",null},{"SpecularColor",null},
            {"Smoothness",null},{"Occlusion",null},{"AmbientOcclusion",null},
        };

        public static readonly Dictionary<string, NodeMap> NODE_MAPPING = Build();

        // 小工具：格式化浮点（模仿 JS toFixed）
        private static string F(double v, int digits) { return v.ToString("F" + digits, CultureInfo.InvariantCulture); }

        private static CustomGlslCfg Cfg(string[] names, string[] types, string outType, string code)
        {
            return new CustomGlslCfg { ParamNames = names, ParamTypes = types, OutputType = outType, Code = code };
        }

        private static string[] S(params string[] a) { return a; }

        private static NodeMap M(string id, string[] inputs, string[] outputs, int ver = 0,
            bool saturate = false, bool colorAsVec4 = false, bool customFunction = false,
            Func<Jval, ShaderGraphConverter, CustomGlslCfg> customGlsl = null)
        {
            return new NodeMap
            {
                Id = id, Ver = ver, Inputs = inputs, Outputs = outputs,
                Saturate = saturate, ColorAsVec4 = colorAsVec4, CustomFunction = customFunction,
                CustomGlsl = customGlsl,
            };
        }

        private static Dictionary<string, NodeMap> Build()
        {
            var m = new Dictionary<string, NodeMap>();

            // ── 输入常量 ──
            m["Vector1Node"] = M("basic/Float", S("X"), S("Out"));
            m["Vector2Node"] = M("basic/Vector2", S("__in", "X", "Y"), S("Out"), ver: 1);
            m["Vector3Node"] = M("basic/Vector3", S("__in", "X", "Y", "Z"), S("Out"), ver: 1);
            m["Vector4Node"] = M("basic/Vector4", S("__in", "X", "Y", "Z", "W"), S("Out"), ver: 1);
            m["ColorNode"] = M("basic/Vector4", S("X", "Y", "Z", "W"), S("Out"), colorAsVec4: true);
            m["BooleanNode"] = M("basic/Boolean", S("X"), S("Out"));
            m["IntegerNode"] = M("basic/Int", S("X"), S("Out"));
            m["TimeNode"] = M("inputdata/scene/Time", S(), S("Time", "Sine Time", "Cosine Time", "Delta Time", "Smooth Delta"));

            // ── 几何/输入 ──
            m["UVNode"] = M("inputdata/vertex/uv", S(), S("Out"));
            m["PositionNode"] = M("inputdata/vertex/positionOS", S(), S("Out"));
            m["NormalVectorNode"] = M("inputdata/vertex/normalOS", S(), S("Out"));
            m["TangentVectorNode"] = M("inputdata/vertex/tangentOS", S(), S("Out"));
            m["VertexColorNode"] = M("inputdata/vertex/VertexColor", S(), S("Out"));
            m["ViewDirectionNode"] = M("inputdata/camera/viewDirection", S(), S("Out"));
            m["CameraNode"] = M("inputdata/camera/cameraPosition", S(), S("Position"));

            // ── Math/Basic ──
            m["AddNode"] = M("math/basic/add", S("A", "B"), S("Out"));
            m["SubtractNode"] = M("math/basic/minus", S("A", "B"), S("Out"));
            m["MultiplyNode"] = M("math/basic/multiply", S("A", "B"), S("Out"));
            m["DivideNode"] = M("math/basic/divide", S("A", "B"), S("Out"));
            m["OneMinusNode"] = M("math/basic/oneMinus", S("In"), S("Out"));

            // ── Math/Common ──
            m["AbsoluteNode"] = M("math/common/abs", S("In"), S("Out"));
            m["SignNode"] = M("math/common/sign", S("In"), S("Out"));
            m["FloorNode"] = M("math/common/floor", S("In"), S("Out"));
            m["CeilingNode"] = M("math/common/ceil", S("In"), S("Out"));
            m["FractionNode"] = M("math/common/fract", S("In"), S("Out"));
            m["ModuloNode"] = M("math/common/mod", S("A", "B"), S("Out"));
            m["MinimumNode"] = M("math/common/min", S("A", "B"), S("Out"));
            m["MaximumNode"] = M("math/common/max", S("A", "B"), S("Out"));
            m["ClampNode"] = M("math/common/clamp", S("In", "Min", "Max"), S("Out"));
            m["LerpNode"] = M("math/common/mix", S("A", "B", "T"), S("Out"));
            m["SaturateNode"] = M("math/common/clamp", S("In", "Min", "Max"), S("Out"), saturate: true);
            m["StepNode"] = M("math/common/step", S("Edge", "In"), S("Out"));
            m["SmoothstepNode"] = M("math/common/smoothstep", S("Edge1", "Edge2", "In"), S("Out"));

            // ── Math/Trigonometry ──
            m["SineNode"] = M("math/trigonometry/sin", S("In"), S("Out"));
            m["CosineNode"] = M("math/trigonometry/cos", S("In"), S("Out"));
            m["TangentNode"] = M("math/trigonometry/tan", S("In"), S("Out"));

            // ── Math/Exponential ──
            m["PowerNode"] = M("math/exponential/pow", S("A", "B"), S("Out"));
            m["SquareRootNode"] = M("math/exponential/sqrt", S("In"), S("Out"));
            m["ExponentialNode"] = M("math/exponential/exp", S("In"), S("Out"));
            m["LogNode"] = M("math/exponential/log", S("In"), S("Out"));

            // ── Math/Geometric ──
            m["LengthNode"] = M("math/geometric/length", S("In"), S("Out"));
            m["DistanceNode"] = M("math/geometric/distance", S("A", "B"), S("Out"));
            m["DotProductNode"] = M("math/geometric/dot", S("A", "B"), S("Out"));
            m["CrossProductNode"] = M("math/geometric/cross", S("A", "B"), S("Out"));
            m["NormalizeNode"] = M("math/geometric/normalize", S("In"), S("Out"));
            m["ReflectionNode"] = M("math/geometric/reflect", S("In", "Normal"), S("Out"));

            // ── Channel ──
            m["SplitNode"] = M("__special_split__", S("In"), S("__deferred__"));
            m["CombineNode"] = M("math/expression/append", S("R", "G", "B", "A"), S("RGBA", "RGB", "RG"));
            m["SwizzleNode"] = M("math/expression/mask", S("In"), S("Out"));

            // ── Texture ──
            m["SampleTexture2DNode"] = M("texture/sampler2D", S("Texture", "UV", "Sampler"), S("RGBA", "R", "G", "B", "A"));
            m["Texture2DAssetNode"] = M("texture/texture2D", S("path"), S("Out"));

            // ── UV ──
            m["TilingAndOffsetNode"] = M("uv/Tiling And Offset", S("UV", "Tiling", "Offset"), S("Out"));
            m["ScreenPositionNode"] = M("uv/screenTexcoord", S(), S("Out"));

            // ── Effect ──
            m["FresnelEffectNode"] = M("math/effect/fresnel", S("Normal", "View Dir", "Power"), S("Out"));
            m["FresnelNode"] = M("math/effect/fresnel", S("Normal", "View Dir", "Power"), S("Out"));

            // ── Artistic / Normal ──
            m["NormalStrengthNode"] = M("function/normalScale", S("In", "Strength"), S("Out"));

            // ── Procedural / Noise ──
            m["GradientNoiseNode"] = M("function/GradientNoiseFloat", S("UV", "Scale"), S("Out"));
            m["SimpleNoiseNode"] = M("function/SimpleNoiseFloat", S("UV", "Scale"), S("Out"));
            m["VoronoiNode"] = M("function/VoronoiFloat", S("UV", "AngleOffset", "CellDensity"), S("Out"));
            m["NoiseNode"] = M("function/SimpleNoiseFloat", S("UV", "Scale"), S("Out"));

            // ── Math / Range ──
            m["RemapNode"] = M("function/custom", S("In", "In Min Max", "Out Min Max"), S("Out"),
                customGlsl: (u, c) => Cfg(S("a", "b", "c"), S("float", "vec2", "vec2"), "float",
                    "return c.x + (a - b.x) * (c.y - c.x) / (b.y - b.x);"));

            // ── Utility / Logic ──
            m["BranchNode"] = M("function/custom", S("True", "False", "Predicate"), S("Out"),
                customGlsl: (u, c) => Cfg(S("t", "f", "p"), S("vec4", "vec4", "bool"), "vec4", "return p ? t : f;"));
            m["BranchOnInputConnectionNode"] = M("function/custom", S("True", "False", "Predicate"), S("Out"),
                customGlsl: (u, c) => Cfg(S("t", "f", "p"), S("vec4", "vec4", "bool"), "vec4", "return p ? t : f;"));
            m["ComparisonNode"] = M("function/custom", S("A", "B"), S("Out"),
                customGlsl: (u, c) =>
                {
                    string[] ops = { "==", "!=", "<", "<=", ">", ">=" };
                    int ct = (int)u.NumOf("m_ComparisonType", 0);
                    string op = (ct >= 0 && ct < ops.Length) ? ops[ct] : "==";
                    return Cfg(S("a", "b"), S("float", "float"), "bool", "return a " + op + " b;");
                });

            // ── Artistic / Adjustment ──
            m["SaturationNode"] = M("function/custom", S("In", "Saturation"), S("Out"),
                customGlsl: (u, c) => Cfg(S("c", "s"), S("vec3", "float"), "vec3",
                    "float l = dot(c, vec3(0.2126729, 0.7151522, 0.0721750)); return vec3(l) + vec3(s) * (c - vec3(l));"));
            m["InvertColorsNode"] = M("function/custom", S("In"), S("Out"),
                customGlsl: (u, c) =>
                {
                    bool r = u.Has("m_RedChannel") ? u.Get("m_RedChannel").AsBool() : true;
                    bool g = u.Has("m_GreenChannel") ? u.Get("m_GreenChannel").AsBool() : true;
                    bool b = u.Has("m_BlueChannel") ? u.Get("m_BlueChannel").AsBool() : true;
                    string expr = "vec3(" + (r ? "1.0-c.r" : "c.r") + ", " + (g ? "1.0-c.g" : "c.g") + ", " + (b ? "1.0-c.b" : "c.b") + ")";
                    return Cfg(S("c"), S("vec3"), "vec3", "return " + expr + ";");
                });

            // ── UV ──
            m["RotateNode"] = M("function/custom", S("UV", "Center", "Rotation"), S("Out"),
                customGlsl: (u, c) =>
                {
                    int unit = (int)u.NumOf("m_Unit", 0);
                    string angleExpr = unit == 1 ? "r * 0.01745329251" : "r";
                    return Cfg(S("uv", "cn", "r"), S("vec2", "vec2", "float"), "vec2",
                        "vec2 p = uv - cn; float s = sin(" + angleExpr + "); float c = cos(" + angleExpr + "); return mat2(c, -s, s, c) * p + cn;");
                });
            m["FlipbookNode"] = M("function/custom", S("UV", "Width", "Height", "Tile"), S("Out"),
                customGlsl: (u, c) =>
                {
                    bool invX = u.Has("m_InvertX") ? u.Get("m_InvertX").AsBool() : false;
                    bool invY = u.Has("m_InvertY") ? u.Get("m_InvertY").AsBool() : true;
                    string ix = invX ? "1.0" : "0.0";
                    string iy = invY ? "1.0" : "0.0";
                    string tileXexpr = invX
                        ? "(" + ix + " * w - ((t - w * floor(t * tc.x)) + " + ix + " * 1.0))"
                        : "(t - w * floor(t * tc.x))";
                    string tileYexpr = invY
                        ? "(" + iy + " * h - (floor(t * tc.x) + " + iy + " * 1.0))"
                        : "floor(t * tc.x)";
                    return Cfg(S("uv", "w", "h", "t"), S("vec2", "float", "float", "float"), "vec2",
                        "t = floor(mod(t + 0.00001, w*h)); vec2 tc = vec2(1.0, 1.0) / vec2(w, h); float tileX = " + tileXexpr + "; float tileY = " + tileYexpr + "; return (uv + vec2(tileX, tileY)) * tc;");
                });

            // ── Phase D ──
            m["NegateNode"] = M("function/custom", S("In"), S("Out"),
                customGlsl: (u, c) => Cfg(S("a"), S("float"), "float", "return -a;"));
            m["InverseLerpNode"] = M("function/custom", S("A", "B", "T"), S("Out"),
                customGlsl: (u, c) => Cfg(S("a", "b", "t"), S("float", "float", "float"), "float", "return (t - a) / (b - a);"));
            m["NormalBlendNode"] = M("function/custom", S("A", "B"), S("Out"),
                customGlsl: (u, c) => Cfg(S("a", "b"), S("vec3", "vec3"), "vec3", "return normalize(vec3(a.rg + b.rg, a.b * b.b));"));
            m["NormalReconstructZNode"] = M("function/custom", S("In"), S("Out"),
                customGlsl: (u, c) => Cfg(S("a"), S("vec2"), "vec3", "float rz = sqrt(1.0 - clamp(dot(a, a), 0.0, 1.0)); return normalize(vec3(a.x, a.y, rz));"));
            m["NormalFromHeightNode"] = M("function/custom", S("In"), S("Out"),
                customGlsl: (u, c) => Cfg(S("h"), S("float"), "vec3", "float dx = dFdx(h); float dy = dFdy(h); return normalize(vec3(-dx, -dy, 1.0));"));
            m["GradientNode"] = M("function/custom", S(), S("Out"),
                customGlsl: (u, c) =>
                {
                    var keys = u.Get("m_SerializableColorKeys");
                    double kx = 1, ky = 1, kz = 1;
                    if (keys != null && keys.IsArray && keys.Count > 0)
                    {
                        var k0 = keys.At(0);
                        kx = k0.NumOf("x", 1); ky = k0.NumOf("y", 1); kz = k0.NumOf("z", 1);
                    }
                    double a0 = 1;
                    var akeys = u.Get("m_SerializableAlphaKeys");
                    if (akeys != null && akeys.IsArray && akeys.Count > 0) a0 = akeys.At(0).NumOf("x", 1);
                    return Cfg(S(), S(), "vec4",
                        "return vec4(" + F(kx, 4) + ", " + F(ky, 4) + ", " + F(kz, 4) + ", " + F(a0, 4) + ");");
                });
            m["ObjectNode"] = M("function/custom", S(), S("Position"),
                customGlsl: (u, c) => Cfg(S(), S(), "vec3", "return vec3(0.0, 0.0, 0.0);"));
            m["TriplanarNode"] = M("function/custom", S("Texture", "Position", "Normal", "Tile", "Blend"), S("Out"),
                customGlsl: (u, c) => Cfg(S("tex", "pos", "norm", "tile", "blend"), S("sampler2D", "vec3", "vec3", "float", "float"), "vec4",
                    "vec3 uv = pos * tile; vec3 bw = pow(abs(norm), vec3(blend)); bw /= max(dot(bw, vec3(1.0)), 0.0001); return texture(tex, uv.zy) * bw.x + texture(tex, uv.xz) * bw.y + texture(tex, uv.xy) * bw.z;"));
            m["KeywordNode"] = M("function/custom", S("On", "Off"), S("Out"),
                customGlsl: (u, conv) =>
                {
                    string defineName = "_KEYWORD_DEFAULT";
                    var kwRef = u.Get("m_Keyword");
                    string kwId = kwRef != null ? kwRef.StrOf("m_Id") : null;
                    if (kwId != null && conv != null)
                    {
                        var kw = conv.Idx.GetById(kwId);
                        // 注意：与 JS truthy 检查一致，空串引用名不当有效 define 名（保持 _KEYWORD_DEFAULT 兜底）
                        if (kw != null && !string.IsNullOrEmpty(kw.StrOf("m_DefaultReferenceName")))
                        {
                            defineName = kw.StrOf("m_DefaultReferenceName");
                            conv.KeywordDefines[defineName] = kw.BoolOf("m_Value");
                        }
                    }
                    return Cfg(S("on_in", "off_in"), S("vec3", "vec3"), "vec3",
                        "#ifdef " + defineName + "\nreturn on_in;\n#else\nreturn off_in;\n#endif");
                });
            m["EllipseNode"] = M("function/custom", S("UV", "Width", "Height"), S("Out"),
                customGlsl: (u, c) => Cfg(S("uv", "w", "h"), S("vec2", "float", "float"), "float",
                    "vec2 d = (uv * 2.0 - 1.0) / vec2(w, h); float l = length(d); return clamp((1.0 - l) / max(fwidth(l), 0.0001), 0.0, 1.0);"));
            m["SphereMaskNode"] = M("function/custom", S("Coords", "Center", "Radius", "Hardness"), S("Out"),
                customGlsl: (u, c) => Cfg(S("c", "cn", "r", "h"), S("vec3", "vec3", "float", "float"), "float",
                    "return 1.0 - clamp((distance(c, cn) - r) / max(1.0 - h, 0.0001), 0.0, 1.0);"));
            m["IsFrontFaceNode"] = M("function/custom", S(), S("Out"),
                customGlsl: (u, c) => Cfg(S(), S(), "float", "return gl_FrontFacing ? 1.0 : 0.0;"));
            m["SpherizeNode"] = M("function/custom", S("UV", "Center", "Strength", "Offset"), S("Out"),
                customGlsl: (u, c) => Cfg(S("uv", "cn", "str", "off"), S("vec2", "vec2", "vec2", "vec2"), "vec2",
                    "vec2 delta = uv - cn; float d2 = dot(delta, delta); vec2 du = d2 * d2 * str; return uv + delta * du + off;"));
            m["PolarCoordinatesNode"] = M("function/custom", S("UV", "Center", "RadialScale", "LengthScale"), S("Out"),
                customGlsl: (u, c) => Cfg(S("uv", "cn", "rs", "ls"), S("vec2", "vec2", "float", "float"), "vec2",
                    "vec2 d = uv - cn; return vec2(length(d) * 2.0 * rs, atan(d.x, d.y) * 0.15915494 * ls);"));
            m["RotateAboutAxisNode"] = M("function/custom", S("In", "Axis", "Rotation"), S("Out"),
                customGlsl: (u, c) =>
                {
                    int unit = (int)u.NumOf("m_Unit", 0);
                    string angleExpr = unit == 1 ? "r * 0.01745329251" : "r";
                    return Cfg(S("v", "ax", "r"), S("vec3", "vec3", "float"), "vec3",
                        "float a = " + angleExpr + "; float s = sin(a); float co = cos(a); float oc = 1.0 - co; vec3 ax2 = normalize(ax); mat3 R = mat3(oc*ax2.x*ax2.x+co, oc*ax2.x*ax2.y-ax2.z*s, oc*ax2.z*ax2.x+ax2.y*s, oc*ax2.x*ax2.y+ax2.z*s, oc*ax2.y*ax2.y+co, oc*ax2.y*ax2.z-ax2.x*s, oc*ax2.z*ax2.x-ax2.y*s, oc*ax2.y*ax2.z+ax2.x*s, oc*ax2.z*ax2.z+co); return R * v;");
                });

            // ── Phase H: CustomFunctionNode ──
            m["CustomFunctionNode"] = M("function/custom", S(), S("Out"), customFunction: true);

            return m;
        }
    }
}
