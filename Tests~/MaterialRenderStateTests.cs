using System;
using System.Collections.Generic;
using System.IO;
using LayaAir3.Converter;

// Compiled with the editor assembly by run-material-state-tests.ps1. These tests
// exercise the production parser, resolver and JSON writer without a Unity process.
public static class MaterialRenderStateTests
{
    private static int assertions;

    private static void Equal<T>(T expected, T actual, string label)
    {
        assertions++;
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception(label + ": expected " + expected + ", got " + actual);
    }

    private static Jval Target(bool allowOverride = false)
    {
        return Jval.Obj().Set("m_SurfaceType", 1).Set("m_AlphaMode", 0)
            .Set("m_ZWriteControl", 0).Set("m_ZTestMode", 4).Set("m_RenderFace", 2)
            .Set("m_AlphaClip", false).Set("m_AllowMaterialOverride", allowOverride);
    }

    private static Jval SubTarget(bool lit = false)
    {
        return Jval.Obj().Set("m_Type", lit ? "UniversalLitSubTarget" : "UniversalUnlitSubTarget");
    }

    private static ShaderGraphRenderState Resolve(Jval target, bool universal = true,
        Dictionary<string, int> properties = null, bool keyword = false, Jval subTarget = null)
    {
        return ShaderGraphRenderState.Resolve(target, subTarget ?? SubTarget(), universal,
            name => properties != null && properties.ContainsKey(name) ? (int?)properties[name] : null, keyword);
    }

    private static void AssertWarp(ShaderGraphRenderState state)
    {
        Equal(false, state.DepthWrite, "transparent Auto ZWrite");
        Equal(2, state.Cull, "front faces -> Cull Back");
        Equal(3, state.DepthTest, "LEqual");
        Equal(2, state.Blend, "separate blend");
        Equal(6, state.SrcRGB, "source RGB");
        Equal(7, state.DstRGB, "destination RGB");
        Equal(1, state.SrcAlpha, "source alpha");
        Equal(7, state.DstAlpha, "destination alpha");
        Equal(false, state.AlphaTest, "alpha clip");
        Equal(5, state.RenderMode, "separate blend requires Custom");
    }

    public static int Main(string[] args)
    {
        try
        {
            var stale = new Dictionary<string, int>
            {
                { "_BUILTIN_Surface", 0 }, { "_Surface", 0 },
                { "_BUILTIN_SrcBlend", 1 }, { "_BUILTIN_DstBlend", 0 },
                { "_SrcBlend", 1 }, { "_DstBlend", 0 },
                { "_BUILTIN_ZWrite", 1 }, { "_ZWrite", 1 },
                { "_BUILTIN_CullMode", 0 }, { "_Cull", 0 },
                { "_BUILTIN_AlphaClip", 1 }, { "_AlphaClip", 1 }
            };
            AssertWarp(Resolve(Target(), properties: stale, keyword: true));
            AssertWarp(Resolve(Target(), false, stale, true));

            // Force states must survive both surface types; queues do not determine blending.
            foreach (int surface in new[] { 0, 1 })
                foreach (int control in new[] { 0, 1, 2 })
                {
                    var state = Resolve(Target().Set("m_SurfaceType", surface).Set("m_ZWriteControl", control));
                    Equal(control == 1 || (control == 0 && surface == 0), state.DepthWrite, "ZWrite control");
                    Equal(surface == 0 ? 0 : 2, state.Blend, "surface blend");
                }
            foreach (int face in new[] { 0, 1, 2 })
                Equal(face, Resolve(Target().Set("m_RenderFace", face)).Cull, "render face");
            for (int depth = 0; depth <= 8; depth++)
                Equal(depth == 0 ? 8 : depth - 1, Resolve(Target().Set("m_ZTestMode", depth)).DepthTest, "depth enum");

            var opaque = Resolve(Target().Set("m_SurfaceType", 0).Set("m_AlphaClip", true));
            Equal(1, opaque.RenderMode, "cutout mode");
            Equal(true, opaque.AlphaTest, "graph alpha clipping without material keyword");
            var queueProps = new JSONObject(JSONObject.Type.OBJECT);
            opaque.Write(queueProps, 3100, 0.1f);
            Equal(0L, queueProps["s_Blend"].i, "opaque with transparent queue stays opaque");
            Equal(3100L, queueProps["renderQueue"].i, "custom queue retained");
            Equal(0.1f, queueProps["alphaTestValue"].n, "cutout threshold retained");
            Resolve(Target()).Write(queueProps, 2973, 0.5f);
            Equal(2L, queueProps["s_Blend"].i, "transparent below 3000 stays transparent");

            var premultiply = Resolve(Target().Set("m_AlphaMode", 1));
            Equal(1, premultiply.SrcRGB, "premultiply RGB");
            Equal(7, premultiply.DstAlpha, "premultiply alpha");
            var additive = Resolve(Target().Set("m_AlphaMode", 2));
            Equal(6, additive.SrcRGB, "additive RGB");
            Equal(1, additive.SrcAlpha, "additive alpha");
            Equal(1, additive.DstAlpha, "additive destination alpha");
            var multiply = Resolve(Target().Set("m_AlphaMode", 3));
            Equal(4, multiply.SrcRGB, "multiply RGB");
            Equal(0, multiply.SrcAlpha, "URP multiply preserves alpha");
            Equal(1, multiply.DstAlpha, "URP multiply destination alpha");
            multiply = Resolve(Target().Set("m_AlphaMode", 3), false);
            Equal(1, multiply.Blend, "Built-in multiply uses shared blend");
            Equal(4, multiply.SrcAlpha, "Built-in multiply alpha");
            Equal(1, Resolve(Target(), subTarget: SubTarget(true)).SrcRGB, "URP Lit preserves specular");
            Equal(6, Resolve(Target(), subTarget: SubTarget(true).Set("m_BlendModePreserveSpecular", false)).SrcRGB,
                "URP Lit preserve specular disabled");

            // Active URP properties win over conflicting Built-in remnants.
            stale["_Surface"] = 1;
            stale["_SrcBlend"] = 5; // Unity SrcAlpha
            stale["_DstBlend"] = 10; // Unity OneMinusSrcAlpha
            stale["_Cull"] = 1;
            stale["_ZWrite"] = 0;
            stale["_ZTest"] = 8;
            var overridden = Resolve(Target(true), properties: stale);
            Equal(1, overridden.Blend, "material-controlled pass uses shared blend");
            Equal(6, overridden.SrcRGB, "URP blend property");
            Equal(7, overridden.DstRGB, "URP destination blend property");
            Equal(1, overridden.Cull, "URP cull property");
            Equal(false, overridden.DepthWrite, "URP depth property");
            Equal(7, overridden.DepthTest, "URP depth test property");
            Equal(false, overridden.AlphaTest, "disabled keyword beats stale alpha property");
            Equal(true, Resolve(Target(true), properties: stale, keyword: true).AlphaTest, "override clip keyword");
            Equal(2, overridden.RenderMode, "shared alpha preset");
            var builtin = Resolve(Target(true), false, stale);
            Equal(0, builtin.Blend, "Built-in uses its own blend properties");
            Equal(0, builtin.Cull, "Built-in uses its own cull property");
            Equal(true, builtin.DepthWrite, "Built-in uses its own depth property");

            // JSON writer must clear previous state and put mode before explicit overrides.
            var props = new JSONObject(JSONObject.Type.OBJECT);
            props.AddField("type", "TestShader");
            props.AddField("s_BlendSrc", 0);
            props.AddField("s_BlendSrc", 1);
            props.AddField("s_BlendDst", 0);
            props.AddField("materialRenderMode", 2);
            props.AddField("s_DepthWrite", true);
            Resolve(Target()).Write(props, 3001, 0.5f);
            Equal(true, props.GetField("s_BlendSrc") == null, "no shared blend leftovers");
            Equal(1L, props["s_BlendSrcAlpha"].i, "written alpha factor");
            Equal(false, props["s_DepthWrite"].b, "written depth");
            Equal("TestShader", props["type"].str, "unrelated fields retained");
            Equal(props.keys.Count, new HashSet<string>(props.keys).Count, "unique fields");
            Equal(true, props.keys.IndexOf("materialRenderMode") < props.keys.IndexOf("s_DepthWrite"), "mode first");
            overridden.Write(props, 3000, 0.5f);
            Equal(true, props.GetField("s_BlendSrcAlpha") == null, "no separate blend leftovers");
            Equal(6L, props["s_BlendSrc"].i, "shared blend restored");

            // Target references, not object order, select the active pipeline.
            var index = SgIndex.Build(SgIndex.ParseShadergraph(
                "{\"m_Type\":\"UnityEditor.ShaderGraph.GraphData\",\"m_ActiveTargets\":[{\"m_Id\":\"u\"},{\"m_Id\":\"b\"}]}" +
                "{\"m_Type\":\"UniversalTarget\",\"m_ObjectId\":\"inactive\"}" +
                "{\"m_Type\":\"BuiltInTarget\",\"m_ObjectId\":\"b\"}" +
                "{\"m_Type\":\"UniversalTarget\",\"m_ObjectId\":\"u\"}"));
            Equal("u", ShaderGraphRenderState.FindActiveTarget(index, "UniversalTarget").StrOf("m_ObjectId"), "active URP reference");
            Equal("b", ShaderGraphRenderState.FindActiveTarget(index, "BuiltInTarget").StrOf("m_ObjectId"), "active Built-in reference");
            Equal(true, ShaderGraphRenderState.FindActiveTarget(index, "HDTarget") == null, "no cross-pipeline fallback");

            if (args.Length > 0)
            {
                index = SgIndex.Build(SgIndex.ParseShadergraph(File.ReadAllText(args[0])));
                var target = ShaderGraphRenderState.FindActiveTarget(index, "UniversalTarget");
                var sub = index.GetById(target.Get("m_ActiveSubTarget").StrOf("m_Id"));
                var actualWarp = Resolve(target, properties: stale, keyword: true, subTarget: sub);
                AssertWarp(actualWarp);
                Console.WriteLine("Verified real Warp ShaderGraph: " + args[0]);
            }
            Console.WriteLine("PASS: " + assertions + " material render state assertions.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }
}
