using System;
using LayaExport;
using UnityEngine;

public static class ParticleTextureSheetExportTests
{
    public static void Run()
    {
        var go = new GameObject("GPU flipbook regression", typeof(ParticleSystem));
        try
        {
            var sheet = go.GetComponent<ParticleSystem>().textureSheetAnimation;
            sheet.enabled = false;
            Check(ExportSheet(go) == null, "Disabled module should be omitted");

            sheet.enabled = true;
            sheet.numTilesX = 4;
            sheet.numTilesY = 4;
            sheet.animation = ParticleSystemAnimationType.WholeSheet;
            sheet.startFrame = new ParticleSystem.MinMaxCurve(0f);
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0.9999f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
            var module = ExportSheet(go);
            CheckType(module, "TextureSheetAnimation");
            Check(module["enable"] != null && module["enable"].IsBool && module["enable"].b, "enable");
            CheckType(module["tiles"], "Vector2");
            CheckNumber(module["tiles"], "x", 4);
            CheckNumber(module["tiles"], "y", 4);
            var start = module["_startFrame"];
            CheckType(start, "StartFrame");
            CheckNumber(start, "_type", 0);
            CheckNumber(start, "_constant", 0);
            var frame = CheckFrame(module, 1);
            CheckGradient(frame, "_overTime", 0f, 15.9984f);

            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0.25f);
            sheet.startFrame = new ParticleSystem.MinMaxCurve(0.125f, 0.5f);
            module = ExportSheet(go);
            frame = CheckFrame(module, 0);
            CheckNumber(frame, "_constant", 4);
            start = module["_startFrame"];
            CheckType(start, "StartFrame");
            CheckNumber(start, "_type", 1);
            CheckNumber(start, "_constantMin", 2);
            CheckNumber(start, "_constantMax", 8);

            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0.25f, 0.75f);
            frame = CheckFrame(ExportSheet(go), 2);
            CheckNumber(frame, "_constantMin", 4);
            CheckNumber(frame, "_constantMax", 12);

            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(
                0.5f, AnimationCurve.Linear(0f, 0f, 1f, 0.5f), AnimationCurve.Linear(0f, 0.25f, 1f, 1f));
            frame = CheckFrame(ExportSheet(go), 3);
            Check(frame["_overTime"] == null, "Two curves should only export min/max gradients");
            CheckGradient(frame, "_overTimeMin", 0f, 4f);
            CheckGradient(frame, "_overTimeMax", 2f, 8f);
            Debug.Log("ParticleTextureSheetExportTests passed");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(go);
        }
    }

    private static JSONObject ExportSheet(GameObject go)
    {
        var renderer = LayaParticleExportV2.ExportParticleSystemV2(go);
        CheckType(renderer, "ShurikenParticleRenderer");
        return renderer["_particleSystem"]["textureSheetAnimation"];
    }

    private static JSONObject CheckFrame(JSONObject module, int mode)
    {
        CheckType(module, "TextureSheetAnimation");
        var frame = module["_frame"];
        CheckType(frame, "FrameOverTime");
        CheckNumber(frame, "_type", mode);
        return frame;
    }

    private static void CheckGradient(JSONObject frame, string field, float first, float last)
    {
        var curve = frame[field];
        CheckType(curve, "GradientDataInt");
        CheckNumber(curve, "_currentLength", 4);
        var elements = curve["_elements"];
        CheckType(elements, "Float32Array");
        var values = elements["value"];
        Check(values != null && values.IsArray && values.Count == 8, field + " buffer length");
        float[] expected = { 0f, first, 1f, last };
        for (int i = 0; i < expected.Length; i++)
            Check(values[i].IsNumber && Mathf.Approximately(values[i].f, expected[i]), field + " value[" + i + "]");
    }

    private static void CheckType(JSONObject node, string expected)
    {
        Check(node != null && node["_$type"] != null && node["_$type"].str == expected, "Expected _$type " + expected);
    }

    private static void CheckNumber(JSONObject node, string field, float expected)
    {
        var value = node[field];
        Check(value != null && value.IsNumber && Mathf.Approximately(value.f, expected), field + " expected " + expected);
    }

    private static void Check(bool passed, string message)
    {
        if (!passed) throw new InvalidOperationException("GPU texture sheet export: " + message);
    }
}
