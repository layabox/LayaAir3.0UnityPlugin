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
        // ── 原生映射扩展（对应 JS inTypes/outTypes/outFromInputs/unifyInputs/lockInputs/property/inputDefaults）──
        public string[] InTypes;        // 输入类型（优先于 GuessInputType）
        public string[] OutTypes;       // 输出类型（优先于 GuessOutputType）
        public Func<Jval, string[]> InTypesFn;   // 函数形式 inTypes（如 swizzle 按 mask 定维）
        public Func<Jval, string[]> OutTypesFn;  // 函数形式 outTypes
        public int[] OutFromInputs;     // 输出维度 = 这些输入解析后的最宽维度（remap/branch/negate）
        public int[] UnifyInputs;       // 这些输入必须同维（distance/dot 不广播，如 sphereMask 的 Coords/Center）
        public int[] LockInputs;        // 锁形状维度不被上游传播覆盖（saturation dot(In,vec3) / uv 系 uv-Center）
        public Func<Jval, Jval> Property;              // → propertyVal（如 comparison 的 mode、rotate 的 unit）
        public Func<Jval, Dictionary<int, object>> InputDefaults;  // Unity 节点开关字段 → Laya 输入默认（flipbook InvertX/Y）
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

        // Unlit_fragment 实际 4 input：0:alphaTest(基类注入) 1:NormalTS 2:Color 3:Alpha
        public static readonly Dictionary<string, int> UNLIT_FRAGMENT_SLOTS = new Dictionary<string, int>
        {
            {"NormalTS",1},{"Color",2},{"Alpha",3},
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

        // Flip / InvertColors 的 4 bool 通道标志（Unity m_RedChannel..m_AlphaChannel，缺省 false）→ Laya {R,G,B,A}
        private static Jval ChannelFlagsProp(Jval u)
        {
            return Jval.Obj()
                .Set("R", u.Has("m_RedChannel") && u.Get("m_RedChannel").AsBool())
                .Set("G", u.Has("m_GreenChannel") && u.Get("m_GreenChannel").AsBool())
                .Set("B", u.Has("m_BlueChannel") && u.Get("m_BlueChannel").AsBool())
                .Set("A", u.Has("m_AlphaChannel") && u.Get("m_AlphaChannel").AsBool());
        }

        // Unity BlendMode 枚举顺序与 Laya BLEND_MODES 数组逐项一致 → 按 m_BlendMode 索引取名。
        public static readonly string[] BLEND_MODE_NAMES = {
            "Burn", "Darken", "Difference", "Dodge", "Divide", "Exclusion", "HardLight", "HardMix",
            "Lighten", "LinearBurn", "LinearDodge", "LinearLight", "LinearLightAddSub", "Multiply",
            "Negation", "Overlay", "PinLight", "Screen", "SoftLight", "Subtract", "VividLight", "Overwrite",
        };

        // ── Swizzle mask 解析（对应 JS SWZ_MAP/swizzleMask/swizzleInDim/dimToType）──
        // 优先取 convertedMask（Unity 已归一化的 xyzw），缺失时按同规则从 _maskInput 归一化。
        private static string SwizzleMask(Jval u)
        {
            string raw = (u.Has("convertedMask") ? u.StrOf("convertedMask") : (u.Has("_maskInput") ? u.StrOf("_maskInput") : "")) ?? "";
            raw = raw.ToLowerInvariant();
            string outStr = "";
            for (int i = 0; i < raw.Length && outStr.Length < 4; i++)
            {
                char c = raw[i];
                if (c == 'r' || c == 'x') outStr += "x";
                else if (c == 'g' || c == 'y') outStr += "y";
                else if (c == 'b' || c == 'z') outStr += "z";
                else if (c == 'a' || c == 'w') outStr += "w";
            }
            return outStr.Length > 0 ? outStr : "x";
        }
        private static int SwizzleInDim(string mask)
        {
            int dim = 1;
            foreach (char c in mask)
            {
                int d = (c == 'x') ? 1 : (c == 'y') ? 2 : (c == 'z') ? 3 : (c == 'w') ? 4 : 1;
                if (d > dim) dim = d;
            }
            return dim;
        }
        private static string DimToType(int d) { return d >= 4 ? "vec4" : (d == 3 ? "vec3" : (d == 2 ? "vec2" : "float")); }

        private static NodeMap M(string id, string[] inputs, string[] outputs, int ver = 0,
            bool saturate = false, bool colorAsVec4 = false, bool customFunction = false,
            Func<Jval, ShaderGraphConverter, CustomGlslCfg> customGlsl = null,
            string[] inTypes = null, string[] outTypes = null, int[] outFromInputs = null,
            int[] unifyInputs = null, int[] lockInputs = null,
            Func<Jval, Jval> property = null, Func<Jval, Dictionary<int, object>> inputDefaults = null,
            Func<Jval, string[]> inTypesFn = null, Func<Jval, string[]> outTypesFn = null)
        {
            return new NodeMap
            {
                Id = id, Ver = ver, Inputs = inputs, Outputs = outputs,
                Saturate = saturate, ColorAsVec4 = colorAsVec4, CustomFunction = customFunction,
                CustomGlsl = customGlsl,
                InTypes = inTypes, OutTypes = outTypes, OutFromInputs = outFromInputs,
                UnifyInputs = unifyInputs, LockInputs = lockInputs,
                Property = property, InputDefaults = inputDefaults,
                InTypesFn = inTypesFn, OutTypesFn = outTypesFn,
            };
        }

        // ── 几何节点 m_Space → Laya constDataID（对应 JS SPACE_NAME / SPACE_NODE）──
        public static readonly Dictionary<int, string> SPACE_NAME = new Dictionary<int, string>
        {
            {0,"Object"},{1,"View"},{2,"World"},{3,"Tangent"},{4,"AbsoluteWorld"},
        };
        public static readonly Dictionary<string, Dictionary<int, string>> SPACE_NODE = new Dictionary<string, Dictionary<int, string>>
        {
            {"PositionNode", new Dictionary<int,string>{ {0,"inputdata/vertex/positionOS"},{2,"inputdata/geometry/positionWS"},{4,"inputdata/geometry/positionWS"} }},
            {"NormalVectorNode", new Dictionary<int,string>{ {0,"inputdata/vertex/normalOS"},{2,"inputdata/geometry/normalWS"},{4,"inputdata/geometry/normalWS"} }},
            {"TangentVectorNode", new Dictionary<int,string>{ {0,"inputdata/vertex/tangentOS"},{2,"inputdata/geometry/tangentWS"},{4,"inputdata/geometry/tangentWS"} }},
            {"BitangentVectorNode", new Dictionary<int,string>{ {2,"inputdata/geometry/biNormalWS"},{4,"inputdata/geometry/biNormalWS"} }},
        };

        private static Dictionary<string, NodeMap> Build()
        {
            var m = new Dictionary<string, NodeMap>();

            // ── 输入常量 ──
            m["Vector1Node"] = M("basic/Float", S("X"), S("Out"));
            // Slider：带范围的 float 常量。m_Value=(值,min,max)，shader 里只用 .x（min/max 仅编辑器滑条）。
            m["SliderNode"] = M("basic/Float", S("X"), S("Out"),
                inputDefaults: (u) => new Dictionary<int, object>
                {
                    { 0, (u.Get("m_Value") != null && u.Get("m_Value").Has("x")) ? (object)u.Get("m_Value").NumOf("x", 0) : (object)0.0 },
                });
            m["Vector2Node"] = M("basic/Vector2", S("__in", "X", "Y"), S("Out"), ver: 1);
            m["Vector3Node"] = M("basic/Vector3", S("__in", "X", "Y", "Z"), S("Out"), ver: 1);
            m["Vector4Node"] = M("basic/Vector4", S("__in", "X", "Y", "Z", "W"), S("Out"), ver: 1);
            m["ColorNode"] = M("basic/Vector4", S("X", "Y", "Z", "W"), S("Out"), colorAsVec4: true);
            m["BooleanNode"] = M("basic/Boolean", S("X"), S("Out"));
            m["IntegerNode"] = M("basic/Int", S("X"), S("Out"));
            // Laya inputdata/scene/Time 只有 1 个 float 输出（u_Time 标量），非 TimeParameters(vec4)。
            // 若声明 5 输出，编译器按 output index 生成 u_Time.x → 标量上 .x 报错。故单输出；
            // 下游连的任意 Time slot 都由输出 slot 映射归一到 index 0（Sine/Cosine 丢波形，Delta 系引擎无对应）。
            m["TimeNode"] = M("inputdata/scene/Time", S(), S("Time"));

            // ── 几何/输入 ──
            m["UVNode"] = M("inputdata/vertex/uv", S(), S("Out"));
            m["PositionNode"] = M("inputdata/vertex/positionOS", S(), S("Out"));
            m["NormalVectorNode"] = M("inputdata/vertex/normalOS", S(), S("Out"));
            m["TangentVectorNode"] = M("inputdata/vertex/tangentOS", S(), S("Out"));
            // 几何节点 constDataID 会按 m_Space 被 ResolveSpaceId 覆盖（见 SPACE_NODE）
            m["BitangentVectorNode"] = M("inputdata/geometry/biNormalWS", S(), S("Out"));
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
            // SwizzleNode → 原生 channel/swizzle（可重排/重复）。输入维度=mask 最高分量、输出维度=mask 长度 → 函数形式。
            m["SwizzleNode"] = M("channel/swizzle", S("In"), S("Out"),
                inTypesFn: (u) => new[] { DimToType(SwizzleInDim(SwizzleMask(u))) },
                outTypesFn: (u) => new[] { DimToType(SwizzleMask(u).Length) },
                property: (u) => Jval.Obj().Set("mask", SwizzleMask(u)));

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

            // ── Math / Range ── 原生 math/range/remap（多态 In，输出随维度）
            m["RemapNode"] = M("math/range/remap", S("In", "In Min Max", "Out Min Max"), S("Out"),
                inTypes: S("float", "vec2", "vec2"), outTypes: S("float"), outFromInputs: new[] { 0 });

            // ── Utility / Logic ── 原生 logic/branch（多态 True/False，输出随维度）
            m["BranchNode"] = M("logic/branch", S("Predicate", "True", "False"), S("Out"),
                inTypes: S("bool", "float", "float"), outTypes: S("float"), outFromInputs: new[] { 1, 2 });
            m["BranchOnInputConnectionNode"] = M("logic/branch", S("Predicate", "True", "False"), S("Out"),
                inTypes: S("bool", "float", "float"), outTypes: S("float"), outFromInputs: new[] { 1, 2 });
            m["ComparisonNode"] = M("logic/comparison", S("A", "B"), S("Out"),
                inTypes: S("float", "float"), outTypes: S("bool"),
                property: (u) =>
                {
                    string[] modes = { "Equal", "NotEqual", "Less", "LessOrEqual", "Greater", "GreaterOrEqual" };
                    int ct = (int)u.NumOf("m_ComparisonType", 0);
                    return Jval.Obj().Set("mode", (ct >= 0 && ct < modes.Length) ? modes[ct] : "Equal");
                });

            // ── Artistic / Adjustment ── 原生 color/saturation（In 锁 vec3，dot 需同维）
            m["SaturationNode"] = M("color/saturation", S("In", "Saturation"), S("Out"),
                inTypes: S("vec3", "float"), outTypes: S("vec3"), lockInputs: new[] { 0 });
            // Unity ColorspaceConversion → 原生 color/colorspaceConversion（enum Colorspace{RGB=0,Linear=1,HSV=2}，读 m_Conversion.{from,to}）
            m["ColorspaceConversionNode"] = M("color/colorspaceConversion", S("In"), S("Out"),
                inTypes: S("vec3"), outTypes: S("vec3"), lockInputs: new[] { 0 },
                property: (u) =>
                {
                    string[] names = { "RGB", "Linear", "HSV" };
                    var c = u.Get("m_Conversion");
                    int from = c != null ? (int)c.NumOf("from", 0) : 0;
                    int to = c != null ? (int)c.NumOf("to", 0) : 0;
                    return Jval.Obj()
                        .Set("from", (from >= 0 && from < 3) ? names[from] : "RGB")
                        .Set("to", (to >= 0 && to < 3) ? names[to] : "RGB");
                });
            // Unity InvertColors → 原生 color/invertColors（多态维度，4 bool；Out = abs(flags - In)）
            m["InvertColorsNode"] = M("color/invertColors", S("In"), S("Out"),
                inTypes: S("float"), outTypes: S("float"), outFromInputs: new[] { 0 },
                property: (u) => ChannelFlagsProp(u));
            // Unity Flip → 原生 channel/flip（多态维度，4 bool；Out = (Flip*-2+1)*In）
            m["FlipNode"] = M("channel/flip", S("In"), S("Out"),
                inTypes: S("float"), outTypes: S("float"), outFromInputs: new[] { 0 },
                property: (u) => ChannelFlagsProp(u));
            // Unity ChannelMask → 原生 channel/channelMask（单 int 位掩码 m_ChannelMask，默认 -1 全通；Out = In*vecN(flags)）
            m["ChannelMaskNode"] = M("channel/channelMask", S("In"), S("Out"),
                inTypes: S("float"), outTypes: S("float"), outFromInputs: new[] { 0 },
                property: (u) =>
                {
                    int mk = u.Has("m_ChannelMask") ? (int)u.NumOf("m_ChannelMask", -1) : -1;
                    return Jval.Obj()
                        .Set("R", (mk & 1) != 0).Set("G", (mk & 2) != 0).Set("B", (mk & 4) != 0).Set("A", (mk & 8) != 0);
                });

            // ── UV ── 原生 uv/rotate（unit: Radians/Degrees 读 m_Unit；UV 锁 vec2）
            m["RotateNode"] = M("uv/rotate", S("UV", "Center", "Rotation"), S("Out"),
                inTypes: S("vec2", "vec2", "float"), outTypes: S("vec2"), lockInputs: new[] { 0 },
                property: (u) => Jval.Obj().Set("unit", ((int)u.NumOf("m_Unit", 0) == 1) ? "Degrees" : "Radians"));
            // 原生 uv/flipbook；InvertX/InvertY 是 Laya bool 输入,从 Unity m_InvertX/m_InvertY 写默认
            m["FlipbookNode"] = M("uv/flipbook", S("UV", "Width", "Height", "Tile", "InvertX", "InvertY"), S("Out"),
                inTypes: S("vec2", "float", "float", "float", "bool", "bool"), outTypes: S("vec2"), lockInputs: new[] { 0 },
                inputDefaults: (u) => new Dictionary<int, object>
                {
                    { 4, u.Has("m_InvertX") ? u.Get("m_InvertX").AsBool() : false },
                    { 5, u.Has("m_InvertY") ? u.Get("m_InvertY").AsBool() : true },
                });

            // ── Phase D ── 原生等价
            m["NegateNode"] = M("math/advanced/negate", S("In"), S("Out"),
                inTypes: S("float"), outTypes: S("float"), outFromInputs: new[] { 0 });
            m["InverseLerpNode"] = M("math/interpolation/inverseLerp", S("A", "B", "T"), S("Out"),
                inTypes: S("float", "float", "float"), outTypes: S("float"), outFromInputs: new[] { 0, 1, 2 });
            m["NormalBlendNode"] = M("color/normalBlend", S("A", "B"), S("Out"),
                inTypes: S("vec3", "vec3"), outTypes: S("vec3"),
                property: (u) => Jval.Obj().Set("mode", ((int)u.NumOf("m_BlendMode", 0) == 1) ? "Reoriented" : "Default"));
            m["NormalReconstructZNode"] = M("color/normalReconstructZ", S("In"), S("Out"),
                inTypes: S("vec2"), outTypes: S("vec3"));
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
            // 原生 procedural/ellipse（Laya 版硬边 *1e7 无 fwidth，顶点段也能编；UV 锁 vec2）
            m["EllipseNode"] = M("procedural/ellipse", S("UV", "Width", "Height"), S("Out"),
                inTypes: S("vec2", "float", "float"), outTypes: S("float"), lockInputs: new[] { 0 });
            // 原生 math/vector/sphereMask（Coords/Center 同维 unifyInputs，Radius/Hardness float，输出 float）
            m["SphereMaskNode"] = M("math/vector/sphereMask", S("Coords", "Center", "Radius", "Hardness"), S("Out"),
                inTypes: S("vec3", "vec3", "float", "float"), outTypes: S("float"), unifyInputs: new[] { 0, 1 });
            // IsFrontFace 暂留 customGlsl（原生输出 bool，与旧 float 输出类型不一致，避免下游回归）
            m["IsFrontFaceNode"] = M("function/custom", S(), S("Out"),
                customGlsl: (u, c) => Cfg(S(), S(), "float", "return gl_FrontFacing ? 1.0 : 0.0;"));
            m["SpherizeNode"] = M("uv/spherize", S("UV", "Center", "Strength", "Offset"), S("Out"),
                inTypes: S("vec2", "vec2", "vec2", "vec2"), outTypes: S("vec2"), lockInputs: new[] { 0 });
            m["PolarCoordinatesNode"] = M("uv/polarCoordinates", S("UV", "Center", "RadialScale", "LengthScale"), S("Out"),
                inTypes: S("vec2", "vec2", "float", "float"), outTypes: S("vec2"), lockInputs: new[] { 0 });
            m["RotateAboutAxisNode"] = M("math/vector/rotateAboutAxis", S("In", "Axis", "Rotation"), S("Out"),
                inTypes: S("vec3", "vec3", "float"), outTypes: S("vec3"),
                property: (u) => Jval.Obj().Set("unit", ((int)u.NumOf("m_Unit", 0) == 1) ? "Degrees" : "Radians"));

            // ═══ Laya 原生节点接线（源类型名 → 早已存在的 Laya 节点，之前 NODE_MAPPING 漏接会静默塌 Float(0)）═══
            // ── Artistic/Blend（22 混合模式，按 m_BlendMode 索引 BLEND_MODE_NAMES）──
            m["BlendNode"] = M("color/blend", S("Base", "Blend", "Opacity"), S("Out"),
                inTypes: S("float", "float", "float"), outTypes: S("float"), outFromInputs: new[] { 0, 1 },
                property: (u) =>
                {
                    int b = (int)u.NumOf("m_BlendMode", 0);
                    return Jval.Obj().Set("mode", (b >= 0 && b < BLEND_MODE_NAMES.Length) ? BLEND_MODE_NAMES[b] : "Burn");
                });
            // ── Logic ──
            m["AndNode"]  = M("logic/and",  S("A", "B"), S("Out"), inTypes: S("bool", "bool"), outTypes: S("bool"));
            m["OrNode"]   = M("logic/or",   S("A", "B"), S("Out"), inTypes: S("bool", "bool"), outTypes: S("bool"));
            m["NandNode"] = M("logic/nand", S("A", "B"), S("Out"), inTypes: S("bool", "bool"), outTypes: S("bool"));
            m["NotNode"]  = M("logic/not",  S("In"),     S("Out"), inTypes: S("bool"), outTypes: S("bool"));
            m["AllNode"]  = M("logic/all",  S("In"),     S("Out"), inTypes: S("float"), outTypes: S("bool"));
            m["AnyNode"]  = M("logic/any",  S("In"),     S("Out"), inTypes: S("float"), outTypes: S("bool"));
            m["IsNanNode"]      = M("logic/isNaN",      S("In"), S("Out"), inTypes: S("float"), outTypes: S("bool"), lockInputs: new[] { 0 });
            m["IsInfiniteNode"] = M("logic/isInfinite", S("In"), S("Out"), inTypes: S("float"), outTypes: S("bool"), lockInputs: new[] { 0 });
            // ── Math 长尾 ──
            m["RoundNode"]    = M("math/round/round",    S("In"), S("Out"), inTypes: S("float"), outTypes: S("float"), outFromInputs: new[] { 0 });
            m["TruncateNode"] = M("math/round/truncate", S("In"), S("Out"), inTypes: S("float"), outTypes: S("float"), outFromInputs: new[] { 0 });
            m["ReciprocalNode"]           = M("math/advanced/reciprocal",     S("In"), S("Out"), inTypes: S("float"), outTypes: S("float"), outFromInputs: new[] { 0 });
            m["ReciprocalSquareRootNode"] = M("math/advanced/reciprocalSqrt", S("In"), S("Out"), inTypes: S("float"), outTypes: S("float"), outFromInputs: new[] { 0 });
            m["PosterizeNode"]   = M("math/advanced/posterize", S("In", "Steps"), S("Out"), inTypes: S("float", "float"), outTypes: S("float"), outFromInputs: new[] { 0 });
            m["RandomRangeNode"] = M("math/range/randomRange",  S("Seed", "Min", "Max"), S("Out"),
                inTypes: S("vec2", "float", "float"), outTypes: S("float"), lockInputs: new[] { 0 });
            // ── Math/Wave ──
            m["SawtoothWaveNode"]  = M("math/wave/sawtoothWave",  S("In"), S("Out"), inTypes: S("float"), outTypes: S("float"), outFromInputs: new[] { 0 });
            m["SquareWaveNode"]    = M("math/wave/squareWave",    S("In"), S("Out"), inTypes: S("float"), outTypes: S("float"), outFromInputs: new[] { 0 });
            m["TriangleWaveNode"]  = M("math/wave/triangleWave",  S("In"), S("Out"), inTypes: S("float"), outTypes: S("float"), outFromInputs: new[] { 0 });
            m["NoiseSineWaveNode"] = M("math/wave/noiseSineWave", S("In", "MinMax"), S("Out"),
                inTypes: S("float", "vec2"), outTypes: S("float"), outFromInputs: new[] { 0 }, lockInputs: new[] { 1 });
            // ── Artistic/Adjustment ──
            m["WhiteBalanceNode"] = M("color/whiteBalance", S("In", "Temperature", "Tint"), S("Out"),
                inTypes: S("vec3", "float", "float"), outTypes: S("vec3"), lockInputs: new[] { 0 });
            m["HueNode"] = M("color/hue", S("In", "Offset"), S("Out"),
                inTypes: S("vec3", "float"), outTypes: S("vec3"), lockInputs: new[] { 0 },
                property: (u) => Jval.Obj().Set("range", ((int)u.NumOf("m_HueMode", 0) == 1) ? "Normalized" : "Degrees"));
            m["ReplaceColorNode"] = M("color/replaceColor", S("In", "From", "To", "Range", "Fuzziness"), S("Out"),
                inTypes: S("vec3", "vec3", "vec3", "float", "float"), outTypes: S("vec3"), lockInputs: new[] { 0, 1, 2 });
            m["ColorMaskNode"] = M("color/colorMask", S("In", "MaskColor", "Range", "Fuzziness"), S("Out"),
                inTypes: S("vec3", "vec3", "float", "float"), outTypes: S("float"), lockInputs: new[] { 0, 1 });
            m["ContrastNode"] = M("color/contrast", S("In", "Contrast"), S("Out"),
                inTypes: S("vec3", "float"), outTypes: S("vec3"), lockInputs: new[] { 0 });
            // ChannelMixer：outRed/outGreen/outBlue 是 UI 控件 → Laya 侧带默认值的输入槽（inputDefaults 写 vec3）。
            m["ChannelMixerNode"] = M("color/channelMixer", S("In", "OutRed", "OutGreen", "OutBlue"), S("Out"),
                inTypes: S("vec3", "vec3", "vec3", "vec3"), outTypes: S("vec3"), lockInputs: new[] { 0, 1, 2, 3 },
                inputDefaults: (u) =>
                {
                    var mx = u.Get("m_ChannelMixer");
                    Func<string, double, double, double, Jval> v = (name, dx, dy, dz) =>
                    {
                        var o = mx != null ? mx.Get(name) : null;
                        return Jval.Obj()
                            .Set("x", (o != null && o.Has("x")) ? o.NumOf("x", 0) : dx)
                            .Set("y", (o != null && o.Has("y")) ? o.NumOf("y", 0) : dy)
                            .Set("z", (o != null && o.Has("z")) ? o.NumOf("z", 0) : dz);
                    };
                    return new Dictionary<int, object> { { 1, v("outRed", 1, 0, 0) }, { 2, v("outGreen", 0, 1, 0) }, { 3, v("outBlue", 0, 0, 1) } };
                });
            m["FadeTransitionNode"] = M("color/fadeTransition", S("NoiseValue", "FadeValue", "FadeContrast"), S("Fade"),
                inTypes: S("float", "float", "float"), outTypes: S("float"));
            m["NormalUnpackNode"] = M("color/normalUnpack", S("In"), S("Out"), inTypes: S("vec4"), outTypes: S("vec3"));
            // ── 常量/PBR 数据 ──
            m["ConstantNode"] = M("inputdata/basic/constant", S(), S("Out"), outTypes: S("float"),
                property: (u) =>
                {
                    string[] names = { "PI", "TAU", "PHI", "E", "SQRT2" };
                    int cc = (int)u.NumOf("m_constant", 0);
                    return Jval.Obj().Set("constant", (cc >= 0 && cc < names.Length) ? names[cc] : "PI");
                });
            m["BlackbodyNode"] = M("inputdata/basic/blackbody", S("Temperature"), S("Out"), inTypes: S("float"), outTypes: S("vec3"));
            m["DielectricSpecularNode"] = M("inputdata/pbr/dielectricSpecular", S("Range", "IOR"), S("Out"),
                inTypes: S("float", "float"), outTypes: S("float"),
                property: (u) =>
                {
                    string[] names = { "Common", "RustedMetal", "Water", "Ice", "Glass", "Custom" };
                    var mt = u.Get("m_Material");
                    int ty = mt != null ? (int)mt.NumOf("type", 0) : 0;
                    return Jval.Obj().Set("material", (ty >= 0 && ty < names.Length) ? names[ty] : "Common");
                },
                inputDefaults: (u) =>
                {
                    var mt = u.Get("m_Material");
                    double range = (mt != null && mt.Has("range")) ? mt.NumOf("range", 0.5) : 0.5;
                    double ior = (mt != null && mt.Has("indexOfRefraction")) ? mt.NumOf("indexOfRefraction", 1) : 1;
                    return new Dictionary<int, object> { { 0, (object)range }, { 1, (object)ior } };
                });
            m["MetalReflectanceNode"] = M("inputdata/pbr/metalReflectance", S(), S("Out"), outTypes: S("vec3"),
                property: (u) =>
                {
                    string[] names = { "Iron", "Silver", "Aluminium", "Gold", "Copper", "Chromium", "Nickel", "Titanium", "Cobalt", "Platinum" };
                    int mm = (int)u.NumOf("m_Material", 0);
                    return Jval.Obj().Set("material", (mm >= 0 && mm < names.Length) ? names[mm] : "Iron");
                });
            m["ViewVectorNode"] = M("inputdata/camera/viewVector", S(), S("Out"), outTypes: S("vec3"));
            // ── UV ──
            m["TwirlNode"] = M("uv/twirl", S("UV", "Center", "Strength", "Offset"), S("Out"),
                inTypes: S("vec2", "vec2", "float", "vec2"), outTypes: S("vec2"), lockInputs: new[] { 0, 1, 3 });
            m["RadialShearNode"] = M("uv/radialShear", S("UV", "Center", "Strength", "Offset"), S("Out"),
                inTypes: S("vec2", "vec2", "vec2", "vec2"), outTypes: S("vec2"), lockInputs: new[] { 0, 1, 2, 3 });
            // ── Procedural/Shape ──
            m["CheckerboardNode"] = M("procedural/checkerboard", S("UV", "ColorA", "ColorB", "Frequency"), S("Out"),
                inTypes: S("vec2", "vec3", "vec3", "vec2"), outTypes: S("vec3"), lockInputs: new[] { 0, 1, 2, 3 });
            m["PolygonNode"] = M("procedural/polygon", S("UV", "Sides", "Width", "Height"), S("Out"),
                inTypes: S("vec2", "float", "float", "float"), outTypes: S("float"), lockInputs: new[] { 0 });
            m["RectangleNode"] = M("procedural/rectangle", S("UV", "Width", "Height"), S("Out"),
                inTypes: S("vec2", "float", "float"), outTypes: S("float"), lockInputs: new[] { 0 },
                property: (u) => Jval.Obj().Set("clamp", ((int)u.NumOf("m_ClampType", 0) == 1) ? "Nicest" : "Fastest"));
            m["RoundedRectangleNode"] = M("procedural/roundedRectangle", S("UV", "Width", "Height", "Radius"), S("Out"),
                inTypes: S("vec2", "float", "float", "float"), outTypes: S("float"), lockInputs: new[] { 0 });
            m["RoundedPolygonNode"] = M("procedural/roundedPolygon", S("UV", "Width", "Height", "Sides", "Roundness"), S("Out"),
                inTypes: S("vec2", "float", "float", "float", "float"), outTypes: S("float"), lockInputs: new[] { 0 });
            // ── 三角/双曲改名接线（Laya 早有原生，源类型名不同）──
            m["DegreesToRadiansNode"] = M("math/trigonometry/radians", S("In"), S("Out"), inTypes: S("float"), outTypes: S("float"), outFromInputs: new[] { 0 });
            m["RadiansToDegreesNode"] = M("math/trigonometry/degrees", S("In"), S("Out"), inTypes: S("float"), outTypes: S("float"), outFromInputs: new[] { 0 });
            m["Arctangent2Node"] = M("math/trigonometry/atan2", S("A", "B"), S("Out"), inTypes: S("float", "float"), outTypes: S("float"));
            m["ArcsineNode"]    = M("math/trigonometry/asin", S("In"), S("Out"), inTypes: S("float"), outTypes: S("float"), outFromInputs: new[] { 0 });
            m["ArccosineNode"]  = M("math/trigonometry/acos", S("In"), S("Out"), inTypes: S("float"), outTypes: S("float"), outFromInputs: new[] { 0 });
            m["ArctangentNode"] = M("math/trigonometry/atan", S("In"), S("Out"), inTypes: S("float"), outTypes: S("float"), outFromInputs: new[] { 0 });
            m["HyperbolicSineNode"]    = M("math/trigonometry/hyperbolicSine",    S("In"), S("Out"), inTypes: S("float"), outTypes: S("float"), outFromInputs: new[] { 0 });
            m["HyperbolicCosineNode"]  = M("math/trigonometry/hyperbolicCosine",  S("In"), S("Out"), inTypes: S("float"), outTypes: S("float"), outFromInputs: new[] { 0 });
            m["HyperbolicTangentNode"] = M("math/trigonometry/hyperbolicTangent", S("In"), S("Out"), inTypes: S("float"), outTypes: S("float"), outFromInputs: new[] { 0 });
            // ── 矩阵操作（transpose 单输出跟随维度；determinant→float；split/construction 多输出按 slot 名匹配）──
            m["MatrixTransposeNode"]   = M("math/matrix/matrixTranspose",   S("In"), S("Out"), outFromInputs: new[] { 0 });
            m["MatrixDeterminantNode"] = M("math/matrix/matrixDeterminant", S("In"), S("Out"), outTypes: S("float"));
            m["MatrixSplitNode"] = M("math/matrix/matrixSplit", S("In"), S("M0", "M1", "M2", "M3"),
                property: (u) => Jval.Obj().Set("axis", ((int)u.NumOf("m_Axis", 0) == 1) ? "Column" : "Row"));
            m["MatrixConstructionNode"] = M("math/matrix/matrixConstruction", S("M0", "M1", "M2", "M3"), S("4x4", "3x3", "2x2"),
                inTypes: S("vec4", "vec4", "vec4", "vec4"), outTypes: S("mat4", "mat3", "mat2"),
                property: (u) => Jval.Obj().Set("axis", ((int)u.NumOf("m_Axis", 0) == 1) ? "Column" : "Row"));
            // ── Refract（双输出 Refracted/Intensity，Safe/CriticalAngle 由 m_RefractMode）──
            m["RefractNode"] = M("math/vector/refract", S("Incident", "Normal", "IORSource", "IORMedium"), S("Refracted", "Intensity"),
                inTypes: S("vec3", "vec3", "float", "float"), outTypes: S("vec3", "float"),
                property: (u) => Jval.Obj().Set("mode", ((int)u.NumOf("m_RefractMode", 0) == 1) ? "Safe" : "CriticalAngle"));
            // ── SampleGradient：渐变编译期已知 → 内联 mix 链（Gradient 输入由 BuildGradientGlsl 手读，Time 是唯一运行时输入）──
            // ⚠ 序列化类型名是 SampleGradient（无 Node 后缀）。
            m["SampleGradient"] = M("function/custom", S("Time"), S("Out"),
                customGlsl: (u, conv) => Cfg(S("Time"), S("float"), "vec4", conv.BuildGradientGlsl(u)));

            // ── Phase H: CustomFunctionNode ──
            m["CustomFunctionNode"] = M("function/custom", S(), S("Out"), customFunction: true);

            return m;
        }
    }
}
