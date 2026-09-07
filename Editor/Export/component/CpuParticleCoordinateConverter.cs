using UnityEngine;

/// <summary>
/// Unity-source (U) to Laya-asset (A) geometry conversion used exclusively by
/// the CPU Particle exporter. The product basis is Cua = diag(-1, 1, 1).
/// </summary>
internal static class CpuParticleCoordinateConverter
{
    public const int ContractVersion = 2;

    public static Vector3 ConvertPoint(Vector3 value)
    {
        return new Vector3(-value.x, value.y, value.z);
    }

    public static Vector3 ConvertPolar(Vector3 value)
    {
        return new Vector3(-value.x, value.y, value.z);
    }

    public static Vector3 ConvertAxial(Vector3 value)
    {
        return new Vector3(value.x, -value.y, -value.z);
    }

    public static Vector3 ConvertRotationZXY(Vector3 eulerDegrees)
    {
        // For the retained Particle ZXY order, C * R * C^-1 is encoded by
        // the axial component signs below. Runtime v2 consumes ZXY directly.
        return ConvertAxial(eulerDegrees);
    }

    public static float PolarCurveFactor(int axis)
    {
        return axis == 0 ? -1.0f : 1.0f;
    }

    public static float AxialCurveFactor(int axis)
    {
        return axis == 0 ? 1.0f : -1.0f;
    }

    public static float ScalarRotationCurveFactor(
        ParticleSystemRenderMode renderMode)
    {
        // Billboard/freeform jobs consume scalar Z with the opposite planar
        // sign from Laya's native quad. Fixed billboards require the same
        // exporter sign after their generated basis is converted. Mesh
        // particles instead consume the source scalar as a positive
        // axis-angle around axisOfRotation. Convert this distinction once at
        // the CPU export boundary; runtime v2 always consumes Laya-native
        // presentation values directly. Regular Stretch ignores rotation,
        // so sharing the generated-quad sign is inert for that mode.
        return (renderMode == ParticleSystemRenderMode.Mesh ? 1.0f : -1.0f)
            * Mathf.Rad2Deg;
    }

    public static float AxialRotationCurveFactor(int axis)
    {
        return AxialCurveFactor(axis) * Mathf.Rad2Deg;
    }

    public static Vector3 ConvertRendererPivot(
        ParticleSystemRenderMode renderMode,
        ParticleSystemRenderSpace alignment,
        Vector3 value)
    {
        if (renderMode == ParticleSystemRenderMode.Mesh)
        {
            // Unity's Mesh particle GeometryJob consumes renderer pivot as
            // (x, y, -z) before scaling it by the Mesh AABB span. The exported
            // Mesh is expressed in Laya's X-reflected asset basis, so the v2
            // CPU contract must serialize the effective pivot as (-x, y, -z).
            return new Vector3(-value.x, value.y, -value.z);
        }

        // Non-Mesh pivot components are expressed in the generated geometry
        // basis, not in GameObject world axes. The CPU v2 CameraView builds
        // the corresponding Laya basis, so these basis-local coefficients are
        // retained. Keep the mode/alignment arguments explicit so no caller
        // can accidentally replace this rule with a generic Vector3 mirror.
        return value;
    }
}
