using System;
using System.Collections.Generic;
using System.IO;
using LayaAir3.Converter;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Forward-pass state for Built-in/URP Lit and Unlit ShaderGraphs. Graph settings are
/// authoritative unless the active target explicitly allows material overrides.
/// Shared by mapped materials and automatic shader export; no SRP package dependency.
/// </summary>
internal sealed class ShaderGraphRenderState
{
    public int Cull;
    public bool DepthWrite;
    public int DepthTest;
    public int Blend;
    public int SrcRGB = 1, DstRGB, SrcAlpha = 1, DstAlpha;
    public bool AlphaTest;

    private sealed class CachedGraph
    {
        public long ModifiedTicks;
        public long Length;
        public SgIndex Index;
    }

    private static readonly Dictionary<string, CachedGraph> Graphs = new Dictionary<string, CachedGraph>();
    private static readonly HashSet<string> Warnings = new HashSet<string>();

    public int RenderMode
    {
        get
        {
            if (Blend == 0 && DepthWrite) return AlphaTest ? 1 : 0;
            if (!AlphaTest && Blend == 1 && !DepthWrite && SrcRGB == 6)
            {
                if (DstRGB == 7) return 2;
                if (DstRGB == 1) return 3;
            }
            return 5;
        }
    }

    public static bool TryResolve(Material material, out ShaderGraphRenderState state)
    {
        state = null;
        if (material == null || material.shader == null) return false;
        string path = AssetDatabase.GetAssetPath(material.shader);
        if (!path.EndsWith(".shadergraph", StringComparison.OrdinalIgnoreCase)) return false;

        try
        {
            var file = new FileInfo(path);
            CachedGraph cached;
            if (!Graphs.TryGetValue(path, out cached) || cached.ModifiedTicks != file.LastWriteTimeUtc.Ticks || cached.Length != file.Length)
            {
                cached = new CachedGraph
                {
                    ModifiedTicks = file.LastWriteTimeUtc.Ticks,
                    Length = file.Length,
                    Index = SgIndex.Build(SgIndex.ParseShadergraph(File.ReadAllText(path)))
                };
                Graphs[path] = cached;
            }

            // Quality-level overrides take precedence over the default graphics pipeline.
            var pipeline = QualitySettings.renderPipeline ?? GraphicsSettings.renderPipelineAsset;
            string pipelineType = pipeline == null ? "BuiltIn" : pipeline.GetType().FullName;
            bool universal = IsUniversalPipeline(pipeline);
            string targetType = pipeline == null ? "BuiltInTarget" : universal ? "UniversalTarget" : null;
            Jval target = FindActiveTarget(cached.Index, targetType);
            Jval subTarget = target == null ? null : ResolveReference(cached.Index, target.Get("m_ActiveSubTarget"));
            string subType = SgIndex.ShortType(subTarget);
            bool supported = universal
                ? subType == "UniversalLitSubTarget" || subType == "UniversalUnlitSubTarget"
                : subType == "BuiltInLitSubTarget" || subType == "BuiltInUnlitSubTarget";
            if (target == null || !supported)
            {
                WarnOnce(path + pipelineType, "Cannot resolve ShaderGraph forward render state for " + path +
                    " (pipeline " + pipelineType + ", target " + subType + "); using legacy material fallback.");
                return false;
            }

            state = Resolve(target, subTarget, universal,
                name => material.HasProperty(name) ? (int?)material.GetInt(name) : null,
                material.IsKeywordEnabled(universal ? "_ALPHATEST_ON" : "_BUILTIN_ALPHATEST_ON"));
            return true;
        }
        catch (Exception exception)
        {
            WarnOnce(path, "Cannot read ShaderGraph render state: " + path + ": " + exception.Message);
            return false;
        }
    }

    private static bool IsUniversalPipeline(RenderPipelineAsset pipeline)
    {
        for (Type type = pipeline == null ? null : pipeline.GetType(); type != null; type = type.BaseType)
            if (type.FullName == "UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset") return true;
        return false;
    }

    internal static Jval FindActiveTarget(SgIndex index, string targetType)
    {
        var targets = index.GraphData == null ? null : index.GraphData.Get("m_ActiveTargets");
        if (targetType == null || targets == null || !targets.IsArray) return null;
        foreach (Jval reference in targets.Items)
        {
            Jval target = ResolveReference(index, reference);
            if (SgIndex.ShortType(target) == targetType) return target;
        }
        return null;
    }

    private static Jval ResolveReference(SgIndex index, Jval reference)
    {
        return reference == null ? null : index.GetById(reference.StrOf("m_Id"));
    }

    // The property reader is supplied separately so the pipeline/override rules can be
    // regression-tested without importing assets or modifying Unity materials.
    internal static ShaderGraphRenderState Resolve(Jval target, Jval subTarget, bool universal,
        Func<string, int?> property, bool alphaTestKeyword)
    {
        bool allowOverride = target.BoolOf("m_AllowMaterialOverride");
        string prefix = universal ? "_" : "_BUILTIN_";
        int surface = (int)target.NumOf("m_SurfaceType");
        int alphaMode = (int)target.NumOf("m_AlphaMode");
        int zWriteControl = (int)target.NumOf("m_ZWriteControl");
        if (allowOverride)
        {
            surface = property(prefix + "Surface") ?? surface;
            alphaMode = property(prefix + "Blend") ?? alphaMode;
            zWriteControl = property(prefix + "ZWriteControl") ?? zWriteControl;
        }

        var state = new ShaderGraphRenderState
        {
            Cull = (int)target.NumOf("m_RenderFace", 2),
            DepthWrite = zWriteControl == 1 || (zWriteControl == 0 && surface == 0),
            DepthTest = ToDepthTest((int)target.NumOf("m_ZTestMode", 4)),
            AlphaTest = target.BoolOf("m_AlphaClip")
        };

        if (surface != 0)
        {
            // URP Lit can lift the alpha multiply into the shader to preserve specular.
            bool preserveSpecular = universal && SgIndex.ShortType(subTarget) == "UniversalLitSubTarget" &&
                subTarget.BoolOf("m_BlendModePreserveSpecular", true);
            state.Blend = 2;
            switch (alphaMode)
            {
                case 1: // Premultiply
                    state.SrcRGB = state.SrcAlpha = 1;
                    state.DstRGB = state.DstAlpha = 7;
                    break;
                case 2: // Additive
                    state.SrcRGB = preserveSpecular ? 1 : 6;
                    state.SrcAlpha = state.DstRGB = state.DstAlpha = 1;
                    break;
                case 3: // Multiply: URP preserves destination alpha; Built-in multiplies it.
                    state.SrcRGB = 4;
                    state.DstRGB = 0;
                    state.SrcAlpha = universal ? 0 : 4;
                    state.DstAlpha = universal ? 1 : 0;
                    state.Blend = universal ? 2 : 1;
                    break;
                default: // Alpha
                    state.SrcRGB = preserveSpecular ? 1 : 6;
                    state.SrcAlpha = 1;
                    state.DstRGB = state.DstAlpha = 7;
                    break;
            }
        }

        if (allowOverride)
        {
            // These are the actual properties referenced by the generated pass. Never
            // consult another pipeline's stale properties, even if HasProperty succeeds.
            state.Cull = property(universal ? "_Cull" : "_BUILTIN_CullMode") ?? state.Cull;
            state.DepthWrite = (property(prefix + "ZWrite") ?? (state.DepthWrite ? 1 : 0)) != 0;
            state.DepthTest = ToDepthTest(property(prefix + "ZTest") ?? (int)target.NumOf("m_ZTestMode", 4));
            // The pass switches alpha clipping with a local keyword when overrides are allowed.
            state.AlphaTest = alphaTestKeyword;
            state.SrcRGB = ToBlendFactor(property(prefix + "SrcBlend"), state.SrcRGB);
            state.DstRGB = ToBlendFactor(property(prefix + "DstBlend"), state.DstRGB);
            state.SrcAlpha = state.SrcRGB;
            state.DstAlpha = state.DstRGB;
            // Both URP and Built-in material-controlled passes use two-factor Blend.
            state.Blend = state.SrcRGB == 1 && state.DstRGB == 0 ? 0 : 1;
        }
        return state;
    }

    private static int ToDepthTest(int value)
    {
        // Laya uses 0..7 for Never..Always and 8 for disabled depth testing.
        return value == 0 ? 8 : value >= 1 && value <= 8 ? value - 1 : 3;
    }

    private static int ToBlendFactor(int? value, int fallback)
    {
        if (!value.HasValue) return fallback;
        switch ((BlendMode)value.Value)
        {
            case BlendMode.Zero: return 0;
            case BlendMode.One: return 1;
            case BlendMode.SrcColor: return 2;
            case BlendMode.OneMinusSrcColor: return 3;
            case BlendMode.DstColor: return 4;
            case BlendMode.OneMinusDstColor: return 5;
            case BlendMode.SrcAlpha: return 6;
            case BlendMode.OneMinusSrcAlpha: return 7;
            case BlendMode.DstAlpha: return 8;
            case BlendMode.OneMinusDstAlpha: return 9;
            case BlendMode.SrcAlphaSaturate: return 10;
            default: return fallback;
        }
    }

    public static bool TryWrite(Material material, JSONObject props)
    {
        ShaderGraphRenderState state;
        if (!TryResolve(material, out state)) return false;
        state.Write(props, material);
        return true;
    }

    public void Write(JSONObject props, Material material)
    {
        Write(props, material.renderQueue, PropDatasConfig.GetAlphaTestValue(material));
    }

    internal void Write(JSONObject props, int renderQueue, float alphaTestValue)
    {
        // Remove old state first, including the alternate blend representation. The
        // mode must precede explicit fields because Laya applies properties in order.
        foreach (string name in new[] { "materialRenderMode", "s_Cull", "s_Blend", "s_BlendSrc", "s_BlendDst",
            "s_BlendSrcRGB", "s_BlendDstRGB", "s_BlendSrcAlpha", "s_BlendDstAlpha", "s_DepthTest",
            "s_DepthWrite", "alphaTest", "alphaTestValue", "renderQueue" })
            while (props.GetField(name) != null) props.RemoveField(name);
        props.AddField("materialRenderMode", RenderMode);
        props.AddField("s_Cull", Cull);
        props.AddField("s_Blend", Blend);
        if (Blend == 2)
        {
            props.AddField("s_BlendSrcRGB", SrcRGB);
            props.AddField("s_BlendDstRGB", DstRGB);
            props.AddField("s_BlendSrcAlpha", SrcAlpha);
            props.AddField("s_BlendDstAlpha", DstAlpha);
        }
        else if (Blend == 1)
        {
            props.AddField("s_BlendSrc", SrcRGB);
            props.AddField("s_BlendDst", DstRGB);
        }
        props.AddField("s_DepthTest", DepthTest);
        props.AddField("s_DepthWrite", DepthWrite);
        props.AddField("alphaTest", AlphaTest);
        props.AddField("alphaTestValue", alphaTestValue);
        props.AddField("renderQueue", renderQueue);
    }

    private static void WarnOnce(string key, string message)
    {
        if (Warnings.Add(key)) ExportLogger.Warning("LayaAir3D: " + message);
    }
}
