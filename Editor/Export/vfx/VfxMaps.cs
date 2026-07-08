using System.Collections.Generic;

namespace LayaAir3.Converter
{
    /// <summary>
    /// VFX 转换器的节点类型映射表 + 分类 helper。
    /// C# 移植自 unity-vfx-to-laya.js（CONTEXT_CLASSES / CTX_MAP / BLOCK_MAP / OP_MAP / ...）。
    ///
    /// 约定：Dictionary 值为 null 表示"已知但跳过/特殊处理"（对应 JS 的显式 null）；
    /// key 缺失表示"未知"。二者语义不同，移植时保留。
    /// </summary>
    public static class VfxMaps
    {
        public static readonly HashSet<string> CONTEXT_CLASSES = new HashSet<string>
        {
            "VFXBasicSpawner", "VFXBasicInitialize", "VFXBasicUpdate",
            "VFXPlanarPrimitiveOutput", "VFXMeshOutput", "VFXStaticMeshOutput",
            "VFXBasicCubeOutput", "VFXLineOutput", "VFXPointOutput", "VFXLineStripOutput",
            "VFXBasicGPUEvent", "VFXOutputEvent", "VFXBasicEvent",
            "VFXComposedParticleOutput", "VFXComposedParticleStripOutput",
            "VFXURPLitPlanarPrimitiveOutput", "VFXURPLitMeshOutput", "VFXURPLitStaticMeshOutput",
            "VFXURPLitParticleStripOutput", "VFXURPLitParticleQuadStripOutput",
            "VFXLitPlanarPrimitiveOutput", "VFXLitMeshOutput", "VFXLitStaticMeshOutput",
            "VFXQuadStripOutput", "VFXLitQuadStripOutput", "VFXURPLitQuadStripOutput",
            "VFXDecalURPOutput", "VFXDecalHDRPOutput",
        };

        public static readonly HashSet<string> SPAWN_BLOCK_CLASSES = new HashSet<string>
        {
            "VFXSpawnerConstantRate", "VFXSpawnerBurst", "VFXSpawnerBurstOld",
            "VFXSpawnerPeriodicBurst", "VFXSpawnerVariableRate",
            "VFXSpawnerCustomWrapper", "VFXSpawnerSetAttribute",
        };

        public const string SLOT_CLASSES_PREFIX = "VFXSlot";
        public static bool IsSlot(string cls) { return cls != null && cls.StartsWith(SLOT_CLASSES_PREFIX); }

        /// <summary>判断类名是否为算子节点（排除 context/spawn block/slot/data/UI 等已知非算子类）。</summary>
        public static bool IsOperatorNode(string cls)
        {
            if (cls == null) return false;
            if (CONTEXT_CLASSES.Contains(cls)) return false;
            if (SPAWN_BLOCK_CLASSES.Contains(cls)) return false;
            if (IsSlot(cls)) return false;
            if (cls.StartsWith("VFXData")) return false;
            if (cls == "VFXUI" || cls == "VFXGraph" || cls == "VFXParameter") return false;
            if (cls == "VFXDynamicBuiltInParameter") return false;
            if (cls == "VFXCustomAttributeDescriptor") return false;
            if (cls.StartsWith("VFXSubgraph")) return false;
            if (cls.StartsWith("<")) return false;
            return true;
        }

        public static readonly Dictionary<string, string> SHADER_UNIFORM_RENAME = new Dictionary<string, string>
        {
            { "_Materialize", "_MaterializeProgress" },
        };

        public static readonly Dictionary<string, string> CTX_MAP = new Dictionary<string, string>
        {
            { "VFXBasicSpawner", "spawn" }, { "VFXBasicInitialize", "initialize" }, { "VFXBasicUpdate", "update" },
            { "VFXPlanarPrimitiveOutput", "outputBillboard" }, { "VFXMeshOutput", "outputMesh" },
            { "VFXStaticMeshOutput", "outputStaticMesh" }, { "VFXBasicCubeOutput", "outputCube" },
            { "VFXLineOutput", "outputLine" }, { "VFXPointOutput", "outputPoint" },
            { "VFXLineStripOutput", "outputLineStrip" }, { "VFXOutputEvent", "outputEvent" },
            { "VFXComposedParticleOutput", "outputComposedParticle" },
            { "VFXURPLitPlanarPrimitiveOutput", "outputBillboard" }, { "VFXLitPlanarPrimitiveOutput", "outputBillboard" },
            { "VFXURPLitMeshOutput", "outputMesh" }, { "VFXLitMeshOutput", "outputMesh" },
            { "VFXURPLitStaticMeshOutput", "outputStaticMesh" }, { "VFXLitStaticMeshOutput", "outputStaticMesh" },
            { "VFXURPLitParticleStripOutput", "outputTrail" },
            { "VFXURPLitParticleQuadStripOutput", "outputTrail" },
            { "VFXQuadStripOutput", "outputTrail" }, { "VFXLitQuadStripOutput", "outputTrail" },
            { "VFXURPLitQuadStripOutput", "outputTrail" },
            { "VFXDecalURPOutput", "outputBillboard" }, { "VFXDecalHDRPOutput", "outputBillboard" },
        };

        public static readonly Dictionary<string, string> BLOCK_MAP = new Dictionary<string, string>
        {
            { "VFXSpawnerConstantRate", "constantRate" }, { "VFXSpawnerBurst", "singleBurst" },
            { "VFXSpawnerBurstOld", "singleBurst" }, { "VFXSpawnerPeriodicBurst", "periodicBurst" },
            { "VFXSpawnerVariableRate", "variableRate" }, { "VFXSpawnerSetAttribute", "setSpawnEventAttribute" },
            { "VFXSpawnerCustomWrapper", "customSpawn" },
            { "SetAttribute", "setAttribute" }, { "AttributeFromCurve", "setAttributeCurve" },
            { "AttributeFromMap", "attributeFromMap" },
            { "Orient", "orient" }, { "ColorOverLife", "colorOverLife" },
            { "Gravity", "gravity" }, { "Drag", "linearDrag" }, { "Force", "force" }, { "Turbulence", "turbulence" },
            { "VectorFieldForce", "vectorFieldForce" },
            { "ConformToSphere", "conformToSphere" }, { "ConformToSDF", "attractorShapeSDF" },
            { "VortexForceField", "vortex" }, { "Vortex", "vortex" },
            { "PositionSphere", "setPositionShape" }, { "PositionBox", "setPositionShape" },
            { "PositionCone", "setPositionShape" }, { "PositionTorus", "setPositionShape" },
            { "PositionCircle", "setPositionShape" }, { "PositionLine", "setPositionShape" },
            { "PositionMesh", "setPositionMesh" }, { "PositionDepth", "positionDepth" },
            { "PositionSDF", "positionSDF" }, { "PositionSequential", "positionSequential" },
            { "VelocityDirection", "velNewDirection" }, { "VelocityRandomize", "velRandom" },
            { "VelocitySpherical", "velSpherical" }, { "VelocityTangent", "velAlongVelocity" },
            { "CollisionSphere", "collisionSphere" }, { "CollisionAABox", "collisionAABox" },
            { "CollisionPlane", "collisionPlane" }, { "CollisionCone", "collisionCone" },
            { "CollisionTorus", "collisionTorus" }, { "CollisionSDF", "collisionSDF" },
            { "CollisionOrientedBox", "collisionAABox" },
            { "KillSphere", "killSphere" }, { "KillAABox", "killAABox" }, { "KillPlane", "killPlane" },
            { "KillCone", "killCone" }, { "KillTorus", "killTorus" }, { "KillOrientedBox", "killOrientedBox" },
            { "CameraFade", "cameraFade" }, { "SubpixelAA", "subpixelAA" }, { "TileWarp", "tileWarpPositions" },
            { "FlipbookPlay", "flipbookPlay" }, { "ConnectTarget", "connectTarget" },
            { "ScreenSpaceSize", "screenSpaceSize" }, { "TriggerEvent", "triggerEvent" },
            { "AttributeMassFromVolume", "calculateMassFromVolume" },
            { "CustomHLSL", "customGlslBlock" },
        };

        public static readonly Dictionary<string, string> POSITION_SHAPE_FROM_CLASS = new Dictionary<string, string>
        {
            { "PositionSphere", "Sphere" }, { "PositionBox", "Box" }, { "PositionCone", "Cone" },
            { "PositionTorus", "Torus" }, { "PositionCircle", "Circle" }, { "PositionLine", "Line" },
        };

        public static readonly Dictionary<int, string> COMPOSITION_MAP = new Dictionary<int, string>
        { { 0, "Overwrite" }, { 1, "Add" }, { 2, "Multiply" }, { 3, "Blend" } };
        public static readonly Dictionary<int, string> SOURCE_MAP = new Dictionary<int, string>
        { { 0, "Slot" }, { 1, "Source" } };
        public static readonly Dictionary<int, string> RANDOM_MAP = new Dictionary<int, string>
        { { 0, "Off" }, { 1, "Per Component" }, { 2, "Per Component" }, { 3, "Uniform" } };

        public static readonly Dictionary<string, string> ATTR_TO_TYPE = new Dictionary<string, string>
        {
            { "lifetime", "float" }, { "age", "float" }, { "size", "float" }, { "alpha", "float" }, { "mass", "float" },
            { "spawnIndex", "uint" }, { "particleId", "uint" },
            { "position", "vec3" }, { "velocity", "vec3" }, { "direction", "vec3" }, { "pivot", "vec3" },
            { "axisX", "vec3" }, { "axisY", "vec3" }, { "axisZ", "vec3" },
            { "scale", "vec3" }, { "angle", "vec3" }, { "angularVelocity", "vec3" }, { "targetPosition", "vec3" },
            { "color", "color" },
        };
        public static string AttrType(string attrName)
        {
            string t;
            return (attrName != null && ATTR_TO_TYPE.TryGetValue(attrName, out t)) ? t : "float";
        }

        // OP_MAP：value 为 null = 特殊（VFXInlineOperator 按 m_Type 决定）
        public static readonly Dictionary<string, string> OP_MAP = new Dictionary<string, string>
        {
            { "VFXInlineOperator", null },
            { "Add", "add" }, { "Subtract", "subtract" }, { "Multiply", "multiply" }, { "Divide", "divide" },
            { "Modulo", "modulo" }, { "Negate", "negate" }, { "Absolute", "absolute" },
            { "Sine", "sine" }, { "Cosine", "cosine" }, { "Tangent", "tangent" },
            { "Floor", "floor" }, { "Ceiling", "ceiling" }, { "Round", "round" },
            { "Saturate", "saturate" }, { "Smoothstep", "smoothstep" }, { "Clamp", "clamp" },
            { "Lerp", "lerp" }, { "Pow", "power" }, { "Sqrt", "squareRoot" },
            { "Min", "minimum" }, { "Max", "maximum" },
            { "LogicalAnd", "multiply" }, { "LogicalOr", "maximum" },
            { "OneMinus", "oneMinus" }, { "Fractional", "fractional" },
            { "RemapToZeroOne", "linearRemap" },
            { "Length", "length" }, { "Distance", "distance" }, { "Normalize", "normalize" },
            { "DotProduct", "dotProduct" }, { "CrossProduct", "crossProduct" },
            { "Rotate3D", "rotate3D" },
            { "Random", "randomNumber" },
            { "TotalTime", "perParticleTotalTime" },
            { "PeriodicTotalTime", "periodicTotalTime" },
            { "AgeOverLifetime", "ageOverLifetime" },
            { "RatioOverStrip", "ratioOverStrip" },
            { "SpawnState", "spawnState" },
            { "SampleCurve", "sampleCurve" },
            { "SampleGradient", "sampleGradient" },
            { "SampleTexture2D", "sampleTexture2D" },
            { "SampleTexture3D", "sampleTexture3D" },
            { "SampleTextureCube", "sampleTextureCube" },
            { "SampleTexture2DArray", "sampleTexture2DArray" },
            { "SampleMeshPosition", "sampleMeshPosition" },
            { "SampleMeshNormal", "sampleMeshNormal" },
            { "SampleMeshTangent", "sampleMeshTangent" },
            { "SampleMeshUV", "sampleMeshUV" },
            { "SampleMeshColor", "sampleMeshColor" },
            { "SampleIndex", "sampleMeshIndex" },
            { "SamplePointCache", "samplePointCache" },
            { "Compare", "compare" }, { "Condition", "compare" }, { "Branch", "branch" },
            { "VFXAttributeParameter", "getAttribute" },
            { "RandomSelector", "weightedSelector" },
            { "GetProperty", "getProperty" },
            { "Noise", "noise" }, { "PerlinNoise", "noise" }, { "ValueNoise", "noise" },
            { "CurlNoise", "curlNoise" }, { "VoroNoise2D", "voroNoise2D" },
            { "CustomHLSL", "customGlsl" },
            { "Remap", "remap" },
            { "PolarToRectangular", "polarToRectangular" },
            { "RectangularToPolar", "rectangularToPolar" },
            { "SphericalToRectangular", "sphericalToRectangular" },
            { "Sequential3D", "sequential3D" },
            { "SequentialLine", "sequentialLine" },
            { "SequentialCircle", "sequentialCircle" },
            { "Switch", "switchOp" },
            { "SampleMesh", "sampleMeshPosition" },
            { "Append", "appendVector" },
            { "AppendVector", "appendVector" },
            { "Squared", "squaredLength" },
            { "Dot", "dotProduct" },
            { "Cross", "crossProduct" },
            { "SquareRoot", "squareRoot" },
            { "Maximum", "maximum" },
            { "Minimum", "minimum" },
            { "Swizzle", "swizzle" },
            { "TransformDirection", "transformDirection" },
            { "TransformMatrix", "transformMatrix" },
            { "TransformPosition", "transformPosition" },
            { "TransformVector", "transformVector" },
            { "SampleBezier", "sampleBezier" },
            { "SampleSDF", "sampleSDF" },
            { "DistanceToPlane", "distanceToPlane" },
            { "DistanceToSphere", "distanceToSphere" },
            { "DistanceToBox", "distanceToBox" },
            { "DistanceToLine", "distanceToLine" },
            { "LookAt", "lookAtMatrix" },
            { "MeshTriangleCount", "meshTriangleCount" },
            { "MeshIndexCount", "meshIndexCount" },
            { "MeshVertexCount", "meshVertexCount" },
            { "SineWave", "sineWave" },
            { "InverseLerp", "inverseLerp" },
            { "Step", "step" },
            { "Frac", "fractional" },
            { "Reciprocal", "reciprocal" },
            { "Sign", "sign" },
            { "Trace", "trace" },
            { "Pi", "inlineFloat" },
        };

        // op-specific propName alias（Unity input prop name → Laya def input id）
        public static readonly Dictionary<string, Dictionary<string, string>> OP_PROPNAME_ALIAS = new Dictionary<string, Dictionary<string, string>>
        {
            { "compare", new Dictionary<string, string> { { "left", "a" }, { "right", "b" } } },
            { "rotate3D", new Dictionary<string, string> {
                { "rotationCenter", "center" }, { "RotationCenter", "center" },
                { "rotationAxis", "axis" }, { "RotationAxis", "axis" } } },
            { "remap", new Dictionary<string, string> {
                { "oldRangeMin", "oldMin" }, { "oldRangeMax", "oldMax" },
                { "newRangeMin", "newMin" }, { "newRangeMax", "newMax" } } },
            { "switchOp", new Dictionary<string, string> {
                { "value0", "input0" }, { "value1", "input1" }, { "value2", "input2" }, { "value3", "input3" },
                { "Value0", "input0" }, { "Value1", "input1" }, { "Value2", "input2" }, { "Value3", "input3" },
                { "Value 0", "input0" }, { "Value 1", "input1" }, { "Value 2", "input2" }, { "Value 3", "input3" } } },
            { "sampleMeshPosition", new Dictionary<string, string> { { "triangle", "index" } } },
            { "sampleMeshNormal", new Dictionary<string, string> { { "triangle", "index" } } },
            { "sampleMeshTangent", new Dictionary<string, string> { { "triangle", "index" } } },
            { "sampleMeshUV", new Dictionary<string, string> { { "triangle", "index" } } },
            { "sampleMeshColor", new Dictionary<string, string> { { "triangle", "index" } } },
            { "sampleGradient", new Dictionary<string, string> { { "time", "t" }, { "Time", "t" } } },
            { "sampleCurve", new Dictionary<string, string> { { "time", "t" }, { "Time", "t" } } },
            { "branch", new Dictionary<string, string> { { "True", "trueVal" }, { "False", "falseVal" }, { "true", "trueVal" }, { "false", "falseVal" } } },
            { "transformPosition", new Dictionary<string, string> { { "transform", "matrix" }, { "Transform", "matrix" } } },
            { "transformDirection", new Dictionary<string, string> { { "transform", "matrix" }, { "Transform", "matrix" } } },
            { "transformVector", new Dictionary<string, string> { { "transform", "matrix" }, { "Transform", "matrix" } } },
            { "transformVector4", new Dictionary<string, string> { { "transform", "matrix" }, { "Transform", "matrix" } } },
            { "transformMatrix", new Dictionary<string, string> { { "transform", "matrix" }, { "Transform", "matrix" } } },
        };

        public static string NormalizeAttrName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            return char.ToLowerInvariant(raw[0]) + raw.Substring(1);
        }

        // VFXDynamicBuiltInParameter slot 名 → Laya builtin op；null = 无对应，跳过+warn
        public static readonly Dictionary<string, string> BUILTIN_SLOT_MAP = new Dictionary<string, string>
        {
            { "Delta Time", "builtinDeltaTime" },
            { "Unscaled Delta Time", "builtinDeltaTime" },
            { "Total Time", "builtinTotalTime" },
            { "Frame Index", null },
            { "Play Rate", null },
            { "Fixed Time Step", null },
            { "Max Delta Time", null },
        };

        public static readonly string[] CUSTOM_ATTR_TYPE_MAP = { "float", "vec2", "vec3", "vec4", "bool", "uint", "int" };

        public static readonly Dictionary<string, string> BUILTIN_ATTR_TYPE_HINT = new Dictionary<string, string>
        {
            { "position", "vec3" }, { "velocity", "vec3" }, { "direction", "vec3" }, { "color", "color" },
            { "alpha", "float" }, { "age", "float" }, { "lifetime", "float" }, { "normalizedAge", "float" },
            { "size", "float" }, { "scale", "vec3" }, { "angle", "vec3" }, { "angularVelocity", "vec3" },
            { "mass", "float" }, { "oldPosition", "vec3" }, { "targetPosition", "vec3" }, { "pivot", "vec3" },
            { "texIndex", "float" }, { "axisX", "vec3" }, { "axisY", "vec3" }, { "axisZ", "vec3" },
            { "alive", "bool" }, { "seed", "uint" }, { "particleId", "uint" }, { "spawnIndex", "uint" },
            { "spawnTime", "float" }, { "stripIndex", "uint" }, { "particleIndexInStrip", "uint" },
            { "collisionEventPosition", "vec3" },
        };

        public static readonly HashSet<string> CM_UNIT_MESH_NAMES = new HashSet<string>
        {
            "uni_wall-uni_wall", "uni_disc-uni_disc", "uni_fan-uni_fan", "uni_funnel-uni_funnel",
            "uni_melee-uni_swoosh", "uni_missile-uni_missile", "uni_mushroom-uni_mushroom",
            "uni_scan-uni_scan", "uni_arc_high-uni_arc_high", "uni_bolt-uni_bolt",
            "uni_semidonut-uni_semidonut", "uni_sphere_teleport-uni_sphere_teleport",
            "uni_wormhole-uni_portal", "uni_wormhole_outer-uni_wormhole_outer",
            "uni_wormhole_ring-uni_wormhole_ring", "uni_wormhole_streak-uni_portal_streak",
            "uni_wormhole_swirl-uni_wormhole_swirl",
        };

        public static readonly Dictionary<string, double> SPECIAL_MESH_SCALE_UUIDS = new Dictionary<string, double>
        {
            { "f042c66d-1de8-44b5-af1b-0058442f1316", 0.2 },
            { "e38d2d0d-ea52-4bc0-ae3f-09506c2cde20", 0.01 },
            // ST_Candle mesh(FlipbookMode 蜡烛):.lm 是米级 1 单位,但输出 _Size=50.4(cm 语义)直乘=50单位太大;×0.01→0.504≈0.5单位,2个蜡烛才在 ±0.46 分开
            { "e967aaae-7e5a-4d87-910f-8869618df7cc", 0.01 },
        };
    }
}
