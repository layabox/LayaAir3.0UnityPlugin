using UnityEngine;
using System.Collections.Generic;

internal class ParticleSystemData
{
    private static JSONObject writeBurst(ParticleSystem.Burst burst)
    {
        JSONObject dataObject = new JSONObject(JSONObject.Type.OBJECT);
        JsonUtils.SetComponentsType(dataObject, "PlusBurst");
        // Unity: cycleCount=0 表示 Infinite; Laya: -1 表示 Infinite
        dataObject.AddField("cycleCount", burst.cycleCount == 0 ? -1 : burst.cycleCount);
        dataObject.AddField("count", writeMinMaxCurveData(burst.count));
        dataObject.AddField("probability", burst.probability);
        dataObject.AddField("time", burst.time);
        dataObject.AddField("repeatInterval", burst.repeatInterval);
        return dataObject;
    }

    private static JSONObject writeBaseNode(
        UnityEngine.ParticleSystem particleSystem,
        ParticleSystemRenderMode renderMode,
        JSONObject sysData)
    {
        JSONObject mainObject = new JSONObject(JSONObject.Type.OBJECT);
        JSONObject particleSystemData = new JSONObject(JSONObject.Type.OBJECT);
        var main = particleSystem.main;

        mainObject.AddField("duration", main.duration);
        mainObject.AddField("loop", main.loop);
        mainObject.AddField("startDelay", writeMinMaxCurveData(main.startDelay));
        mainObject.AddField("startLifetime", writeMinMaxCurveData(main.startLifetime));
        mainObject.AddField("startSpeed", writeMinMaxCurveData(main.startSpeed));
        mainObject.AddField("startSize3D", main.startSize3D);
        if (main.startSize3D)
        {
            mainObject.AddField("startSizeX", writeMinMaxCurveData(main.startSizeX));
            mainObject.AddField("startSizeY", writeMinMaxCurveData(main.startSizeY));
            mainObject.AddField("startSizeZ", writeMinMaxCurveData(main.startSizeZ));
        }
        else
        {
            mainObject.AddField("startSize", writeMinMaxCurveData(main.startSize));
        }

        mainObject.AddField("startRotation3D", main.startRotation3D);
        if (main.startRotation3D)
        {
            mainObject.AddField("startRotationX", writeMinMaxCurveData(main.startRotationX, CpuParticleCoordinateConverter.RotationCurveFactor(particleSystem, renderMode, 0)));
            mainObject.AddField("startRotationY", writeMinMaxCurveData(main.startRotationY, CpuParticleCoordinateConverter.RotationCurveFactor(particleSystem, renderMode, 1)));
            mainObject.AddField("startRotationZ", writeMinMaxCurveData(main.startRotationZ, CpuParticleCoordinateConverter.RotationCurveFactor(particleSystem, renderMode, 2)));
        }
        else
        {
            mainObject.AddField("startRotation", writeMinMaxCurveData(
                main.startRotation,
                CpuParticleCoordinateConverter.RotationCurveFactor(particleSystem, renderMode, 2)));
        }

        mainObject.AddField("startAxisOfRotation", JsonUtils.GetVector3Object(new Vector3(0, 0, -1)));
        mainObject.AddField("rotationAxisMode", renderMode == ParticleSystemRenderMode.Mesh
            && particleSystem.shape.shapeType != ParticleSystemShapeType.SingleSidedEdge ? 1 : 0);
        mainObject.AddField("rotationAxisReference", JsonUtils.GetVector3Object(Vector3.forward));
        mainObject.AddField("rotationAxisFallback", JsonUtils.GetVector3Object(Vector3.down));
        mainObject.AddField("flipRotation", main.flipRotation);
        mainObject.AddField("startColor", writeMinMaxGradientData(main.startColor));
        mainObject.AddField("gravityModifier", writeMinMaxCurveData(main.gravityModifier));
        mainObject.AddField("simulationSpace", (int)(object)main.simulationSpace);
        mainObject.AddField("simulationSpeed", particleSystem.main.simulationSpeed);
        mainObject.AddField("useUnscaledTime", particleSystem.main.useUnscaledTime);
        mainObject.AddField("scalingMode", (int)(object)main.scalingMode);
        mainObject.AddField("playOnAwake", particleSystem.main.playOnAwake);

        dynamic dmain = particleSystem.main;
        try
        {
            mainObject.AddField("emitterVelocityMode", (int)(object)dmain.emitterVelocityMode);
            Vector3 emitterVelocity = (Vector3)dmain.emitterVelocity;
            mainObject.AddField("emitterVelocity", JsonUtils.GetVector3Object(
                CpuParticleCoordinateConverter.ConvertPolar(emitterVelocity)));
        }
        catch
        {
        }

        mainObject.AddField("maxParticles", particleSystem.main.maxParticles);
        mainObject.AddField("stopAction", (int)(object)particleSystem.main.stopAction);
        mainObject.AddField("cullingMode", (int)(object)particleSystem.main.cullingMode);
        mainObject.AddField("ringBufferMode", (int)(object)particleSystem.main.ringBufferMode);
        mainObject.AddField("ringBufferLoopRange", JsonUtils.GetVector2Object(main.ringBufferLoopRange));

        particleSystemData.AddField("useAutoRandomSeed", particleSystem.useAutoRandomSeed);
        if (!particleSystem.useAutoRandomSeed)
            particleSystemData.AddField("randomSeed", particleSystem.randomSeed);

        particleSystemData.AddField("main", mainObject);
        sysData.AddField("particleSystem", particleSystemData);
        return particleSystemData;
    }

    private static void writeRotationOverLifetime(
        UnityEngine.ParticleSystem particleSystem,
        ParticleSystemRenderMode renderMode,
        JSONObject sysData)
    {
        ParticleSystem.RotationOverLifetimeModule rotationOverLifetime = particleSystem.rotationOverLifetime;
        JSONObject dataObject = new JSONObject(JSONObject.Type.OBJECT);
        JsonUtils.SetComponentsType(dataObject, "PlusRotationOverLife");
        dataObject.AddField("enable", rotationOverLifetime.enabled);
        dataObject.AddField("separateAxes", rotationOverLifetime.separateAxes);
        dataObject.AddField("x", writeMinMaxCurveData(rotationOverLifetime.x, CpuParticleCoordinateConverter.RotationCurveFactor(particleSystem, renderMode, 0)));
        dataObject.AddField("y", writeMinMaxCurveData(rotationOverLifetime.y, CpuParticleCoordinateConverter.RotationCurveFactor(particleSystem, renderMode, 1)));
        float zFactor = CpuParticleCoordinateConverter.RotationCurveFactor(particleSystem, renderMode, 2);
        dataObject.AddField("z", writeMinMaxCurveData(rotationOverLifetime.z, zFactor));
        sysData.AddField("rotationOverLifetime", dataObject);
    }

    private static void writeForceOverLifetime(UnityEngine.ParticleSystem particleSystem, JSONObject sysData)
    {
        ParticleSystem.ForceOverLifetimeModule forceOverLifetime = particleSystem.forceOverLifetime;
        JSONObject dataObject = new JSONObject(JSONObject.Type.OBJECT);
        JsonUtils.SetComponentsType(dataObject, "PlusForceOverLife");
        dataObject.AddField("enable", forceOverLifetime.enabled);
        dataObject.AddField("space", (int)(object)forceOverLifetime.space);
        dataObject.AddField("x", writeMinMaxCurveData(forceOverLifetime.x, CpuParticleCoordinateConverter.PolarCurveFactor(0)));
        dataObject.AddField("y", writeMinMaxCurveData(forceOverLifetime.y, CpuParticleCoordinateConverter.PolarCurveFactor(1)));
        dataObject.AddField("z", writeMinMaxCurveData(forceOverLifetime.z, CpuParticleCoordinateConverter.PolarCurveFactor(2)));
        dataObject.AddField("randomized", forceOverLifetime.randomized);
        sysData.AddField("forceOverLifetime", dataObject);
    }

    private static void writeVelocityOverLifetime(UnityEngine.ParticleSystem particleSystem, JSONObject sysData)
    {
        ParticleSystem.VelocityOverLifetimeModule velocityOverLifetime = particleSystem.velocityOverLifetime;
        JSONObject dataObject = new JSONObject(JSONObject.Type.OBJECT);
        JsonUtils.SetComponentsType(dataObject, "PlusVelocityOverLife");
        dataObject.AddField("enable", velocityOverLifetime.enabled);
        dataObject.AddField("speedModifier", writeMinMaxCurveData(velocityOverLifetime.speedModifier));
        dataObject.AddField("x", writeMinMaxCurveData(velocityOverLifetime.x, CpuParticleCoordinateConverter.PolarCurveFactor(0)));
        dataObject.AddField("y", writeMinMaxCurveData(velocityOverLifetime.y, CpuParticleCoordinateConverter.PolarCurveFactor(1)));
        dataObject.AddField("z", writeMinMaxCurveData(velocityOverLifetime.z, CpuParticleCoordinateConverter.PolarCurveFactor(2)));
        dataObject.AddField("space", (int)(object)velocityOverLifetime.space);
        dataObject.AddField("orbitalX", writeMinMaxCurveData(velocityOverLifetime.orbitalX, CpuParticleCoordinateConverter.AxialCurveFactor(0)));
        dataObject.AddField("orbitalY", writeMinMaxCurveData(velocityOverLifetime.orbitalY, CpuParticleCoordinateConverter.AxialCurveFactor(1)));
        dataObject.AddField("orbitalZ", writeMinMaxCurveData(velocityOverLifetime.orbitalZ, CpuParticleCoordinateConverter.AxialCurveFactor(2)));
        dataObject.AddField("orbitalOffsetX", writeMinMaxCurveData(velocityOverLifetime.orbitalOffsetX, CpuParticleCoordinateConverter.PolarCurveFactor(0)));
        dataObject.AddField("orbitalOffsetY", writeMinMaxCurveData(velocityOverLifetime.orbitalOffsetY, CpuParticleCoordinateConverter.PolarCurveFactor(1)));
        dataObject.AddField("orbitalOffsetZ", writeMinMaxCurveData(velocityOverLifetime.orbitalOffsetZ, CpuParticleCoordinateConverter.PolarCurveFactor(2)));
        dataObject.AddField("radial", writeMinMaxCurveData(velocityOverLifetime.radial));
        sysData.AddField("velocityOverLifetime", dataObject);
    }

    private static void writeSizeOverLifetime(UnityEngine.ParticleSystem particleSystem, JSONObject sysData)
    {
        ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = particleSystem.sizeOverLifetime;
        JSONObject dataObject = new JSONObject(JSONObject.Type.OBJECT);
        JsonUtils.SetComponentsType(dataObject, "PlusSizeOverLife");
        dataObject.AddField("enable", sizeOverLifetime.enabled);
        dataObject.AddField("separateAxes", sizeOverLifetime.separateAxes);
        dataObject.AddField("size", writeMinMaxCurveData(sizeOverLifetime.size));
        dataObject.AddField("x", writeMinMaxCurveData(sizeOverLifetime.x));
        dataObject.AddField("y", writeMinMaxCurveData(sizeOverLifetime.y));
        dataObject.AddField("z", writeMinMaxCurveData(sizeOverLifetime.z));
        sysData.AddField("sizeOverLifetime", dataObject);
    }

    private static void writeEmission(UnityEngine.ParticleSystem particleSystem, JSONObject sysData)
    {
        ParticleSystem.EmissionModule emission = particleSystem.emission;
        JSONObject emissionObject = new JSONObject(JSONObject.Type.OBJECT);
        emissionObject.AddField("enable", emission.enabled);
        emissionObject.AddField("rateOverTime", writeMinMaxCurveData(emission.rateOverTime));
        emissionObject.AddField("rateOverDistance", writeMinMaxCurveData(emission.rateOverDistance));
        JSONObject bursts = new JSONObject(JSONObject.Type.ARRAY);
        int bcount = emission.burstCount;
        for (int i = 0; i < bcount; i++)
        {
            bursts.Add(writeBurst(emission.GetBurst(i)));
        }
        emissionObject.AddField("bursts", bursts);
        sysData.AddField("emission", emissionObject);
    }

    private static void writeShape(UnityEngine.ParticleSystem particleSystem, JSONObject sysData, NodeMap map, ResoureMap resMap)
    {
        JSONObject shapObject = new JSONObject(JSONObject.Type.OBJECT);
        JsonUtils.SetComponentsType(shapObject, "PlusShape");
        ParticleSystem.ShapeModule shape = particleSystem.shape;

        // CpuParticle keeps Unity's current Shape enum values for its 14 supported
        // 3D shapes. Normalize the obsolete shell-only enum values to the current
        // shape + radiusThickness representation before serializing them.
        int sourceShapeType = (int)(object)shape.shapeType;
        int targetShapeType = sourceShapeType;
        float radiusThickness = shape.radiusThickness;
        switch (sourceShapeType)
        {
            case 1:  targetShapeType = 0;  radiusThickness = 0; break; // SphereShell
            case 3:  targetShapeType = 2;  radiusThickness = 0; break; // HemisphereShell
            case 7:  targetShapeType = 4;  radiusThickness = 0; break; // ConeShell
            case 9:  targetShapeType = 8;  radiusThickness = 0; break; // ConeVolumeShell
            case 11: targetShapeType = 10; radiusThickness = 0; break; // CircleEdge
        }

        bool supportedShape = isCpuParticleShapeTypeSupported(targetShapeType);
        if (shape.enabled && !supportedShape)
        {
            Debug.LogWarning(
                $"[LayaAir Export] '{particleSystem.gameObject.name}': CPU Particle Shape " +
                $"'{shape.shapeType}' is not supported. The exported Shape module is disabled.");
        }

        shapObject.AddField("enable", shape.enabled && supportedShape);
        // Keep the serialized type valid even when an unsupported/future Unity
        // shape is disabled, so loading the asset cannot create a null helper.
        shapObject.AddField("type", supportedShape ? targetShapeType : 4);
        shapObject.AddField("angle", shape.angle);
        shapObject.AddField("radius", shape.radius);
        shapObject.AddField("donutRadius", shape.donutRadius);
        shapObject.AddField("radiusThickness", radiusThickness);
        shapObject.AddField("radiusMode", (int)(object)shape.radiusMode);
        shapObject.AddField("radiusSpread", shape.radiusSpread);
        shapObject.AddField("radiusSpeed", writeMinMaxCurveData(shape.radiusSpeed));
        shapObject.AddField("length", shape.length);
        shapObject.AddField("arc", shape.arc);
        shapObject.AddField("arcMode", (int)(object)shape.arcMode);
        shapObject.AddField("arcSpread", shape.arcSpread);
        shapObject.AddField("arcSpeed", writeMinMaxCurveData(shape.arcSpeed));

        shapObject.AddField("boxThickness", JsonUtils.GetVector3Object(shape.boxThickness));

        if (targetShapeType == 6 && shape.mesh != null)
        {
            shapObject.AddField("mesh", resMap.GetMeshData(shape.mesh, null));
        }
        else if (shape.enabled && targetShapeType == 6)
        {
            Debug.LogWarning(
                $"[LayaAir Export] '{particleSystem.gameObject.name}': CPU Particle Mesh Shape has no Mesh source.");
        }
        if (targetShapeType == 13 && shape.meshRenderer != null)
        {
            shapObject.AddField(
                "meshRenderer",
                map.getRefNodeIdObjet(shape.meshRenderer.gameObject, "MeshRenderer"));
        }
        else if (shape.enabled && targetShapeType == 13)
        {
            Debug.LogWarning(
                $"[LayaAir Export] '{particleSystem.gameObject.name}': CPU Particle MeshRenderer Shape has no Renderer source.");
        }
        if (targetShapeType == 14 && shape.skinnedMeshRenderer != null)
        {
            shapObject.AddField(
                "skinnedMeshRenderer",
                map.getRefNodeIdObjet(shape.skinnedMeshRenderer.gameObject, "SkinnedMeshRenderer"));

            Mesh skinnedMesh = shape.skinnedMeshRenderer.sharedMesh;
            if (skinnedMesh != null && skinnedMesh.blendShapeCount > 0)
            {
                Debug.LogWarning(
                    $"[LayaAir Export] '{particleSystem.gameObject.name}': the referenced SkinnedMeshRenderer " +
                    "contains BlendShapes, but the current Unity Mesh exporter does not write morph target data. " +
                    "CPU Particle Shape will sample the exported base skinned mesh.");
            }
        }
        else if (shape.enabled && targetShapeType == 14)
        {
            Debug.LogWarning(
                $"[LayaAir Export] '{particleSystem.gameObject.name}': CPU Particle SkinnedMeshRenderer Shape has no Renderer source.");
        }
        shapObject.AddField("meshShapeType", (int)(object)shape.meshShapeType);
        shapObject.AddField("useMeshMaterialIndex", shape.useMeshMaterialIndex);
        shapObject.AddField("meshMaterialIndex", shape.meshMaterialIndex);
        shapObject.AddField("useMeshColors", shape.useMeshColors);
        shapObject.AddField("normalOffset", shape.normalOffset);
        int meshSpawnMode = (int)(object)shape.meshSpawnMode;
        shapObject.AddField("meshSpawnMode", meshSpawnMode);
        shapObject.AddField("meshSpawnSpread", shape.meshSpawnSpread);
        shapObject.AddField("meshSpawnSpeed", writeMinMaxCurveData(shape.meshSpawnSpeed));

        if (shape.enabled && isCpuParticleMeshShape(targetShapeType) && meshSpawnMode > 2)
        {
            Debug.LogWarning(
                $"[LayaAir Export] '{particleSystem.gameObject.name}': CPU Particle Mesh Shape " +
                "does not support BurstSpread and will fail closed.");
        }

        if (shape.texture != null)
        {
            shapObject.AddField("texture", resMap.GetTextureData(shape.texture, true));
        }
        shapObject.AddField("textureClipChannel", (int)(object)shape.textureClipChannel);
        shapObject.AddField("textureClipThreshold", shape.textureClipThreshold);
        shapObject.AddField("textureColorAffectsParticles", shape.textureColorAffectsParticles);
        shapObject.AddField("textureAlphaAffectsParticles", shape.textureAlphaAffectsParticles);
        shapObject.AddField("textureBilinearFiltering", shape.textureBilinearFiltering);
        shapObject.AddField("textureUVChannel", shape.textureUVChannel);

        if (shape.texture != null && isCpuParticleMeshShape(targetShapeType)
            && shape.textureUVChannel != 0 && shape.textureUVChannel != 1)
        {
            Debug.LogWarning(
                $"[LayaAir Export] '{particleSystem.gameObject.name}': CPU Particle Mesh Shape " +
                $"only supports Texture UV channels 0 and 1; channel {shape.textureUVChannel} will fail closed.");
        }

        Vector3 shapePosition = CpuParticleCoordinateConverter.ConvertPoint(
            shape.position);
        Vector3 shapeRotation = CpuParticleCoordinateConverter.ConvertRotationZXY(
            shape.rotation);
        shapObject.AddField("position", JsonUtils.GetVector3Object(shapePosition));
        shapObject.AddField("rotation", JsonUtils.GetVector3Object(shapeRotation));
        shapObject.AddField("scale", JsonUtils.GetVector3Object(shape.scale));
        shapObject.AddField("alignToDirection", shape.alignToDirection);
        shapObject.AddField("randomDirectionAmount", shape.randomDirectionAmount);
        shapObject.AddField("sphericalDirectionAmount", shape.sphericalDirectionAmount);
        shapObject.AddField("randomPositionAmount", shape.randomPositionAmount);
        sysData.AddField("shape", shapObject);
    }

    private static bool isCpuParticleShapeTypeSupported(int shapeType)
    {
        switch (shapeType)
        {
            case 0:  // Sphere
            case 2:  // Hemisphere
            case 4:  // Cone
            case 5:  // Box
            case 6:  // Mesh
            case 8:  // ConeVolume
            case 10: // Circle
            case 12: // SingleSidedEdge
            case 13: // MeshRenderer
            case 14: // SkinnedMeshRenderer
            case 15: // BoxShell
            case 16: // BoxEdge
            case 17: // Donut
            case 18: // Rectangle
                return true;
            default:
                return false;
        }
    }

    private static bool isCpuParticleMeshShape(int shapeType)
    {
        return shapeType == 6 || shapeType == 13 || shapeType == 14;
    }

    private static void writeLifetimeByEmitterSpeed(UnityEngine.ParticleSystem particleSystem, JSONObject sysData)
    {
        try
        {
            dynamic ps = particleSystem;
            var lifetimeByEmitterSpeed = ps.lifetimeByEmitterSpeed;
            JSONObject dataObject = new JSONObject(JSONObject.Type.OBJECT);
            JsonUtils.SetComponentsType(dataObject, "PlusLifetimeByEmitterSpeed");
            dataObject.AddField("enable", lifetimeByEmitterSpeed.enabled);
            dataObject.AddField("curve", writeMinMaxCurveData((ParticleSystem.MinMaxCurve)lifetimeByEmitterSpeed.curve));
            dataObject.AddField("range", JsonUtils.GetVector2Object((Vector2)lifetimeByEmitterSpeed.range));
            sysData.AddField("lifetimeByEmitterSpeed", dataObject);
        }
        catch
        {
            return;
        }
    }

    private static void writeLimitVelocityOverLifetime(UnityEngine.ParticleSystem particleSystem, JSONObject sysData)
    {
        ParticleSystem.LimitVelocityOverLifetimeModule limitVelocityOverLifetime = particleSystem.limitVelocityOverLifetime;
        JSONObject dataObject = new JSONObject(JSONObject.Type.OBJECT);
        JsonUtils.SetComponentsType(dataObject, "PlusLimtVelocityOverLife");
        dataObject.AddField("enable", limitVelocityOverLifetime.enabled);
        dataObject.AddField("space", (int)(object)limitVelocityOverLifetime.space);
        dataObject.AddField("separateAxes", limitVelocityOverLifetime.separateAxes);
        dataObject.AddField("limit", writeMinMaxCurveData(limitVelocityOverLifetime.limit));
        dataObject.AddField("limitX", writeMinMaxCurveData(limitVelocityOverLifetime.limitX));
        dataObject.AddField("limitY", writeMinMaxCurveData(limitVelocityOverLifetime.limitY));
        dataObject.AddField("limitZ", writeMinMaxCurveData(limitVelocityOverLifetime.limitZ));
        dataObject.AddField("dampen", limitVelocityOverLifetime.dampen);
        dataObject.AddField("drag", writeMinMaxCurveData(limitVelocityOverLifetime.drag));
        dataObject.AddField("multiplyDragByParticleSize", limitVelocityOverLifetime.multiplyDragByParticleSize);
        dataObject.AddField("multiplyDragByParticleVelocity", limitVelocityOverLifetime.multiplyDragByParticleVelocity);
        sysData.AddField("limitVelocityOverLifetime", dataObject);
    }

    private static void writeColorOverLifetime(UnityEngine.ParticleSystem particleSystem, JSONObject sysData)
    {
        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = particleSystem.colorOverLifetime;
        JSONObject dataObject = new JSONObject(JSONObject.Type.OBJECT);
        JsonUtils.SetComponentsType(dataObject, "PlusColorOverLife");
        dataObject.AddField("enable", colorOverLifetime.enabled);
        dataObject.AddField("color", writeMinMaxGradientData(colorOverLifetime.color));
        sysData.AddField("colorOverLifetime", dataObject);
    }

    private static void writeColorBySpeed(UnityEngine.ParticleSystem particleSystem, JSONObject sysData)
    {
        ParticleSystem.ColorBySpeedModule colorBySpeed = particleSystem.colorBySpeed;
        JSONObject dataObject = new JSONObject(JSONObject.Type.OBJECT);
        JsonUtils.SetComponentsType(dataObject, "PlusColorBySpeed");
        dataObject.AddField("enable", colorBySpeed.enabled);
        dataObject.AddField("color", writeMinMaxGradientData(colorBySpeed.color));
        dataObject.AddField("range", JsonUtils.GetVector2Object(colorBySpeed.range));
        sysData.AddField("colorBySpeed", dataObject);
    }

    private static void writeSizeBySpeed(UnityEngine.ParticleSystem particleSystem, JSONObject sysData)
    {
        ParticleSystem.SizeBySpeedModule sizeBySpeed = particleSystem.sizeBySpeed;
        JSONObject dataObject = new JSONObject(JSONObject.Type.OBJECT);
        JsonUtils.SetComponentsType(dataObject, "PlusSizeBySpeed");
        dataObject.AddField("enable", sizeBySpeed.enabled);
        dataObject.AddField("separateAxes", sizeBySpeed.separateAxes);
        dataObject.AddField("size", writeMinMaxCurveData(sizeBySpeed.size));
        dataObject.AddField("x", writeMinMaxCurveData(sizeBySpeed.x));
        dataObject.AddField("y", writeMinMaxCurveData(sizeBySpeed.y));
        dataObject.AddField("z", writeMinMaxCurveData(sizeBySpeed.z));
        dataObject.AddField("range", JsonUtils.GetVector2Object(sizeBySpeed.range));
        sysData.AddField("sizeBySpeed", dataObject);
    }

    private static void writeExternalForces(UnityEngine.ParticleSystem particleSystem, JSONObject sysData, NodeMap map)
    {
        ParticleSystem.ExternalForcesModule externalForces = particleSystem.externalForces;
        JSONObject dataObject = new JSONObject(JSONObject.Type.OBJECT);
        JsonUtils.SetComponentsType(dataObject, "PlusExternalForces");
        dataObject.AddField("enable", externalForces.enabled);
        dataObject.AddField("multiplier", writeMinMaxCurveData(externalForces.multiplier));
        dataObject.AddField("influenceFilter", (int)(object)externalForces.influenceFilter);
        int count = externalForces.influenceCount;
        JSONObject subDatas = new JSONObject(JSONObject.Type.ARRAY);
        for (int i = 0; i < count; i++)
        {
            if (externalForces.GetInfluence(i))
                subDatas.Add(map.getRefNodeIdObjet(externalForces.GetInfluence(i).gameObject, "ParticleSystemForceField"));
        }
        dataObject.AddField("influences", subDatas);
        sysData.AddField("externalForces", dataObject);
    }

    private static void writeRotationBySpeed(
        UnityEngine.ParticleSystem particleSystem,
        ParticleSystemRenderMode renderMode,
        JSONObject sysData)
    {
        ParticleSystem.RotationBySpeedModule rotationBySpeed = particleSystem.rotationBySpeed;
        JSONObject dataObject = new JSONObject(JSONObject.Type.OBJECT);
        JsonUtils.SetComponentsType(dataObject, "PlusRotationBySpeed");
        dataObject.AddField("enable", rotationBySpeed.enabled);
        dataObject.AddField("separateAxes", rotationBySpeed.separateAxes);
        dataObject.AddField("x", writeMinMaxCurveData(rotationBySpeed.x, CpuParticleCoordinateConverter.RotationCurveFactor(particleSystem, renderMode, 0)));
        dataObject.AddField("y", writeMinMaxCurveData(rotationBySpeed.y, CpuParticleCoordinateConverter.RotationCurveFactor(particleSystem, renderMode, 1)));
        float zFactor = CpuParticleCoordinateConverter.RotationCurveFactor(particleSystem, renderMode, 2);
        dataObject.AddField("z", writeMinMaxCurveData(rotationBySpeed.z, zFactor));
        dataObject.AddField("range", JsonUtils.GetVector2Object(rotationBySpeed.range));
        sysData.AddField("rotationBySpeed", dataObject);
    }

    private static void writeInheritVelocity(UnityEngine.ParticleSystem particleSystem, JSONObject sysData)
    {
        ParticleSystem.InheritVelocityModule inheritVelocity = particleSystem.inheritVelocity;
        JSONObject dataObject = new JSONObject(JSONObject.Type.OBJECT);
        JsonUtils.SetComponentsType(dataObject, "PlusInheritVelocity");
        dataObject.AddField("enable", inheritVelocity.enabled);
        dataObject.AddField("mode", (int)(object)inheritVelocity.mode);
        dataObject.AddField("curve", writeMinMaxCurveData(inheritVelocity.curveMultiplier));
        sysData.AddField("inheritVelocity", dataObject);
    }

    private static void writeCollision(UnityEngine.ParticleSystem particleSystem, JSONObject sysData, NodeMap map)
    {
        ParticleSystem.CollisionModule collision = particleSystem.collision;
        JSONObject dataObject = new JSONObject(JSONObject.Type.OBJECT);
        JsonUtils.SetComponentsType(dataObject, "PlusCollision");
        dataObject.AddField("enable", collision.enabled);
        dataObject.AddField("type", (int)(object)collision.type);

        try
        {
            dynamic col = collision;
            int count = col.planeCount;
            JSONObject subDatas = new JSONObject(JSONObject.Type.ARRAY);
            for (int i = 0; i < count; i++)
            {
                var plane = col.GetPlane(i);
                if (plane != null)
                {
                    subDatas.Add(map.getRefNodeIdObjet(plane.gameObject));
                }
            }
            dataObject.AddField("planeSps", subDatas);
        }
        catch
        {
            dataObject.AddField("planeSps", new JSONObject(JSONObject.Type.ARRAY));
        }

        dataObject.AddField("dampen", writeMinMaxCurveData(collision.dampen));
        dataObject.AddField("bounce", writeMinMaxCurveData(collision.bounce));
        dataObject.AddField("lifetimeLoss", writeMinMaxCurveData(collision.lifetimeLoss));
        dataObject.AddField("minKillSpeed", collision.minKillSpeed);
        dataObject.AddField("maxKillSpeed", collision.maxKillSpeed);
        dataObject.AddField("radiusScale", collision.radiusScale);
        sysData.AddField("collision", dataObject);
    }

    private static void writeNoise(UnityEngine.ParticleSystem particleSystem, JSONObject sysData)
    {
        ParticleSystem.NoiseModule noise = particleSystem.noise;
        JSONObject dataObject = new JSONObject(JSONObject.Type.OBJECT);
        JsonUtils.SetComponentsType(dataObject, "PlusNoise");
        dataObject.AddField("enable", noise.enabled);
        dataObject.AddField("separateAxes", noise.separateAxes);
        dataObject.AddField("strengthX", writeMinMaxCurveData(noise.strengthX));
        dataObject.AddField("strengthY", writeMinMaxCurveData(noise.strengthY));
        dataObject.AddField("strengthZ", writeMinMaxCurveData(noise.strengthZ));
        dataObject.AddField("strength", writeMinMaxCurveData(noise.strength));
        dataObject.AddField("frequency", noise.frequency);
        dataObject.AddField("scrollSpeed", writeMinMaxCurveData(noise.scrollSpeed));
        dataObject.AddField("damping", noise.damping);
        dataObject.AddField("octaveCount", noise.octaveCount);
        dataObject.AddField("octaveMultiplier", noise.octaveMultiplier);
        dataObject.AddField("octaveScale", noise.octaveScale);
        dataObject.AddField("quality", (int)(object)noise.quality);
        dataObject.AddField("remapEnabled", noise.remapEnabled);
        dataObject.AddField("remapX", writeMinMaxCurveData(noise.remapX));
        dataObject.AddField("remapY", writeMinMaxCurveData(noise.remapY));
        dataObject.AddField("remapZ", writeMinMaxCurveData(noise.remapZ));
        dataObject.AddField("positionAmount", writeMinMaxCurveData(noise.positionAmount));
        dataObject.AddField("rotationAmount", writeMinMaxCurveData(noise.rotationAmount));
        dataObject.AddField("sizeAmount", writeMinMaxCurveData(noise.sizeAmount));
        sysData.AddField("noise", dataObject);
    }

    private static void writeTrails(UnityEngine.ParticleSystem particleSystem, JSONObject sysData, NodeMap map, ResoureMap resMap)
    {
        ParticleSystem.TrailModule trails = particleSystem.trails;
        JSONObject dataObject = new JSONObject(JSONObject.Type.OBJECT);
        JsonUtils.SetComponentsType(dataObject, "PlusTrails");
        dataObject.AddField("enable", trails.enabled);
        dataObject.AddField("mode", (int)(object)trails.mode);
        dataObject.AddField("ribbonCount", (int)(object)trails.ribbonCount);
        dataObject.AddField("ratio", trails.ratio);
        dataObject.AddField("lifetime", writeMinMaxCurveData(trails.lifetime));
        dataObject.AddField("minVertexDistance", trails.minVertexDistance);
        dataObject.AddField("worldSpace", trails.worldSpace);
        dataObject.AddField("dieWithParticles", trails.dieWithParticles);
        dataObject.AddField("attachRibbonsToTransform", trails.attachRibbonsToTransform);
        dataObject.AddField("textureMode", (int)(object)trails.textureMode);
        try
        {
            dynamic dynamicTrails = trails;
            dataObject.AddField("textureScale", JsonUtils.GetVector2Object(dynamicTrails.textureScale));
        }
        catch
        {
        }
        dataObject.AddField("sizeAffectsWidth", trails.sizeAffectsWidth);
        dataObject.AddField("sizeAffectsLifetime", trails.sizeAffectsLifetime);
        dataObject.AddField("inheritParticleColor", trails.inheritParticleColor);
        dataObject.AddField("colorOverLifetime", writeMinMaxGradientData(trails.colorOverLifetime));
        dataObject.AddField("widthOverTrail", writeMinMaxCurveData(trails.widthOverTrail));
        dataObject.AddField("colorOverTrail", writeMinMaxGradientData(trails.colorOverTrail));
        sysData.AddField("trails", dataObject);
    }

    private static void writeTextureSheetAnimation(UnityEngine.ParticleSystem particleSystem, JSONObject sysData)
    {
        ParticleSystem.TextureSheetAnimationModule textureSheetAnimation = particleSystem.textureSheetAnimation;
        JSONObject dataObject = new JSONObject(JSONObject.Type.OBJECT);
        JsonUtils.SetComponentsType(dataObject, "PlusTextureSheetAnimation");
        dataObject.AddField("enable", textureSheetAnimation.enabled);
        dataObject.AddField("numTiles", JsonUtils.GetVector2Object(textureSheetAnimation.numTilesX, textureSheetAnimation.numTilesY));
        dataObject.AddField("animation", (int)(object)textureSheetAnimation.animation);
        dataObject.AddField("frameOverTime", writeMinMaxCurveData(textureSheetAnimation.frameOverTime));
        dataObject.AddField("speedRange", JsonUtils.GetVector2Object(textureSheetAnimation.speedRange));
        dataObject.AddField("rowIndex", textureSheetAnimation.rowIndex);
        try
        {
            dynamic dynamicModule = textureSheetAnimation;
            dataObject.AddField("rowMode", (int)(object)dynamicModule.rowMode);
        }
        catch
        {
        }
        dataObject.AddField("timeMode", (int)(object)textureSheetAnimation.timeMode);
        dataObject.AddField("fps", textureSheetAnimation.fps);
        dataObject.AddField("startFrame", writeMinMaxCurveData(textureSheetAnimation.startFrame));
        dataObject.AddField("cycleCount", textureSheetAnimation.cycleCount);
        sysData.AddField("textureSheetAnimation", dataObject);
    }

    private static void writeSubEmittersModule(UnityEngine.ParticleSystem particleSystem, JSONObject sysData, NodeMap map)
    {
        ParticleSystem.SubEmittersModule subEmitters = particleSystem.subEmitters;
        JSONObject dataObject = new JSONObject(JSONObject.Type.OBJECT);
        JsonUtils.SetComponentsType(dataObject, "PlusSubEmitters");
        dataObject.AddField("enable", subEmitters.enabled);
        JSONObject subDatas = new JSONObject(JSONObject.Type.ARRAY);
        int count = subEmitters.subEmittersCount;
        for (int i = 0; i < count; i++)
        {
            JSONObject subData = new JSONObject(JSONObject.Type.OBJECT);
            JsonUtils.SetComponentsType(subData, "PlusSubmitterData");
            ParticleSystem subSys = subEmitters.GetSubEmitterSystem(i);
            if (subSys)
                subData.AddField("particleSystem", map.getRefNodeIdObjet(subSys.gameObject));
            subData.AddField("type", (int)(object)subEmitters.GetSubEmitterType(i));
            subData.AddField("properties", (int)(object)subEmitters.GetSubEmitterProperties(i));
            subData.AddField("probability", subEmitters.GetSubEmitterEmitProbability(i));
            subDatas.Add(subData);
        }
        dataObject.AddField("subEmitters", subDatas);
        sysData.AddField("subEmitters", dataObject);
    }

    private static JSONObject writeCustomDataStream(
        ParticleSystem.CustomDataModule customData,
        ParticleSystemCustomData stream)
    {
        JSONObject streamObject = new JSONObject(JSONObject.Type.OBJECT);
        ParticleSystemCustomDataMode mode = customData.GetMode(stream);
        streamObject.AddField("mode", (int)(object)mode);

        if (mode == ParticleSystemCustomDataMode.Vector)
        {
            int componentCount = Mathf.Clamp(customData.GetVectorComponentCount(stream), 1, 4);
            streamObject.AddField("vectorComponentCount", componentCount);

            string[] componentNames = { "vectorX", "vectorY", "vectorZ", "vectorW" };
            for (int component = 0; component < componentCount; component++)
            {
                streamObject.AddField(
                    componentNames[component],
                    writeMinMaxCurveData(customData.GetVector(stream, component))
                );
            }
        }
        else if (mode == ParticleSystemCustomDataMode.Color)
        {
            streamObject.AddField("color", writeMinMaxGradientData(customData.GetColor(stream)));
        }

        return streamObject;
    }

    private static void writeCustomData(UnityEngine.ParticleSystem particleSystem, JSONObject sysData)
    {
        ParticleSystem.CustomDataModule customData = particleSystem.customData;
        JSONObject dataObject = new JSONObject(JSONObject.Type.OBJECT);
        dataObject.AddField("enable", customData.enabled);
        dataObject.AddField(
            "custom1",
            writeCustomDataStream(customData, ParticleSystemCustomData.Custom1)
        );
        dataObject.AddField(
            "custom2",
            writeCustomDataStream(customData, ParticleSystemCustomData.Custom2)
        );
        sysData.AddField("customData", dataObject);
    }

    private static JSONObject writeVertexStreamArray(List<ParticleSystemVertexStream> streams)
    {
        JSONObject data = new JSONObject(JSONObject.Type.ARRAY);
        for (int i = 0; i < streams.Count; i++)
        {
            data.Add((int)streams[i]);
        }
        return data;
    }

    private static bool tryValidateVertexStreams(
        List<ParticleSystemVertexStream> streams,
        bool trail,
        out string reason)
    {
        HashSet<int> seen = new HashSet<int>();
        bool hasPosition = false;

        for (int i = 0; i < streams.Count; i++)
        {
            int stream = (int)streams[i];
            if (stream < 0 || stream > 52)
            {
                reason = "contains an unsupported stream value: " + stream;
                return false;
            }
            if (!seen.Add(stream))
            {
                reason = "contains a duplicate stream: " + streams[i];
                return false;
            }

            if (stream == (int)ParticleSystemVertexStream.Position)
                hasPosition = true;

            // CPUParticle uses Unity 2022.3 stream ordinals and validates the
            // target-specific Main/Trail subsets during deserialization.
            if (!trail && stream >= 49)
            {
                reason = "contains a Trail-only stream in the Main layout: " + streams[i];
                return false;
            }
            if (trail && (stream == 5 || stream == 6 || stream == 7 || stream == 45))
            {
                reason = "contains a Main-only stream in the Trail layout: " + streams[i];
                return false;
            }
        }

        if (!hasPosition)
        {
            reason = "does not contain Position";
            return false;
        }

        reason = null;
        return true;
    }

    private static void writeVertexStreamConfiguration(
        UnityEngine.ParticleSystemRenderer renderer,
        JSONObject compData,
        List<ParticleSystemVertexStream> streams,
        bool useCustomStreams,
        bool trail)
    {
        string toggleField = trail
            ? "useCustomTrailVertexStreams"
            : "useCustomVertexStreams";
        string streamsField = trail
            ? "trailVertexStreams"
            : "vertexStreams";

        // GetActiveVertexStreams/GetActiveTrailVertexStreams still return the
        // default layout when Unity's custom-stream override is disabled, so
        // the serialized toggle must be checked independently.
        if (!useCustomStreams)
        {
            compData.AddField(toggleField, false);
            return;
        }

        // An enabled override without a stream layout cannot be consumed by
        // CPUParticle's strict deserializer. Fall back to its default layout.
        if (streams.Count == 0)
        {
            Debug.LogWarning(
                "[LayaAir Export] '" + renderer.gameObject.name + "': enabled "
                + (trail ? "Trail" : "Main")
                + " Custom Vertex Streams has an empty layout. The custom layout was not exported."
            );
            compData.AddField(toggleField, false);
            return;
        }

        string reason;
        if (!tryValidateVertexStreams(streams, trail, out reason))
        {
            Debug.LogWarning(
                "[LayaAir Export] '" + renderer.gameObject.name + "': invalid "
                + (trail ? "Trail" : "Main") + " Custom Vertex Streams ("
                + reason + "). The custom layout was not exported."
            );
            compData.AddField(toggleField, false);
            return;
        }

        compData.AddField(toggleField, true);
        compData.AddField(streamsField, writeVertexStreamArray(streams));
    }

    private static bool getUseCustomVertexStreams(
        UnityEngine.ParticleSystemRenderer renderer,
        bool trail)
    {
        // Unity 2022 serializes these Inspector toggles but does not expose them
        // through ParticleSystemRenderer's public API.
        UnityEditor.SerializedObject serializedRenderer = new UnityEditor.SerializedObject(renderer);
        serializedRenderer.UpdateIfRequiredOrScript();
        string propertyName = trail
            ? "m_UseCustomTrailVertexStreams"
            : "m_UseCustomVertexStreams";
        UnityEditor.SerializedProperty property = serializedRenderer.FindProperty(propertyName);

        // Preserve the previous behaviour on Unity versions that do not expose
        // the serialized field; stream validation still guards the final data.
        return property == null || property.boolValue;
    }

    private static void writeCustomVertexStreams(
        UnityEngine.ParticleSystemRenderer renderer,
        JSONObject compData)
    {
        List<ParticleSystemVertexStream> streams = new List<ParticleSystemVertexStream>();
        renderer.GetActiveVertexStreams(streams);
        writeVertexStreamConfiguration(
            renderer,
            compData,
            streams,
            getUseCustomVertexStreams(renderer, false),
            false);

#if UNITY_2022_1_OR_NEWER
        streams.Clear();
        renderer.GetActiveTrailVertexStreams(streams);
        writeVertexStreamConfiguration(
            renderer,
            compData,
            streams,
            getUseCustomVertexStreams(renderer, true),
            true);
#else
        compData.AddField("useCustomTrailVertexStreams", false);
#endif
    }

    private static bool getApplyActiveColorSpace(UnityEngine.ParticleSystemRenderer renderer)
    {
        // Unity 2022 serializes this Renderer option but does not expose it through
        // ParticleSystemRenderer's public C# API. Read the Inspector/YAML field directly.
        UnityEditor.SerializedObject serializedRenderer = new UnityEditor.SerializedObject(renderer);
        serializedRenderer.UpdateIfRequiredOrScript();
        UnityEditor.SerializedProperty property = serializedRenderer.FindProperty("m_ApplyActiveColorSpace");
        return property == null || property.boolValue;
    }

    public static JSONObject GetParticleSystem(
        UnityEngine.ParticleSystem particleSystem,
        ParticleSystemRenderMode renderMode,
        bool isOverride,
        NodeMap map,
        ResoureMap resMap)
    {
        JSONObject compData = JsonUtils.SetComponentsType(new JSONObject(JSONObject.Type.OBJECT), "ParticleSystem", isOverride);
        compData.AddField("coordinateContractVersion", CpuParticleCoordinateConverter.ContractVersion);
        JSONObject particleSystemData = writeBaseNode(
            particleSystem,
            renderMode,
            compData);
        writeEmission(particleSystem, particleSystemData);
        writeShape(particleSystem, particleSystemData, map, resMap);
        writeVelocityOverLifetime(particleSystem, particleSystemData);
        writeLimitVelocityOverLifetime(particleSystem, particleSystemData);
        writeLifetimeByEmitterSpeed(particleSystem, particleSystemData);
        writeForceOverLifetime(particleSystem, particleSystemData);
        writeColorOverLifetime(particleSystem, particleSystemData);
        writeColorBySpeed(particleSystem, particleSystemData);
        writeSizeOverLifetime(particleSystem, particleSystemData);
        writeSizeBySpeed(particleSystem, particleSystemData);
        writeRotationOverLifetime(
            particleSystem,
            renderMode,
            particleSystemData);
        writeRotationBySpeed(
            particleSystem,
            renderMode,
            particleSystemData);
        writeExternalForces(particleSystem, particleSystemData, map);
        writeInheritVelocity(particleSystem, particleSystemData);
        writeNoise(particleSystem, particleSystemData);
        writeCollision(particleSystem, particleSystemData, map);
        writeSubEmittersModule(particleSystem, particleSystemData, map);
        writeTextureSheetAnimation(particleSystem, particleSystemData);
        writeTrails(particleSystem, particleSystemData, map, resMap);
        writeCustomData(particleSystem, particleSystemData);
        return compData;
    }

    public static JSONObject GetParticleSystemRenderer(UnityEngine.ParticleSystemRenderer renderer, bool isOverride, ResoureMap map, JSONObject compData)
    {
        compData.AddField("enabled", renderer.enabled);
        compData.AddField("renderMode", (int)(object)renderer.renderMode);
        compData.AddField("sortMode", (int)(object)renderer.sortMode);
        compData.AddField("alignment", (int)(object)renderer.alignment);

        if (renderer.sharedMaterial) { compData.AddField("material", map.GetMaterialData(renderer.sharedMaterial, renderer, true)); }
        if (renderer.trailMaterial) compData.AddField("trailMaterial", map.GetMaterialData(renderer.trailMaterial, renderer, true));

        compData.AddField("cameraVelocityScale", renderer.cameraVelocityScale);
        compData.AddField("velocityScale", renderer.velocityScale);
        compData.AddField("lengthScale", renderer.lengthScale);
        compData.AddField("freeformStretching", renderer.freeformStretching);
        compData.AddField("rotateWithStretchDirection", renderer.rotateWithStretchDirection);
        compData.AddField("maxParticleSize", renderer.maxParticleSize);
        compData.AddField("minParticleSize", renderer.minParticleSize);
        compData.AddField("normalDirection", renderer.normalDirection);
        compData.AddField("allowRoll", renderer.allowRoll);
        compData.AddField("rollCorrectionScale", renderer.renderMode == ParticleSystemRenderMode.Mesh ? -1 : 1);
        compData.AddField("alignmentRotation", JsonUtils.GetVector3Object(CpuParticleCoordinateConverter.AlignmentRotation(renderer)));
        bool velocityBillboard = renderer.renderMode == ParticleSystemRenderMode.Billboard && renderer.alignment == ParticleSystemRenderSpace.Velocity;
        compData.AddField("velocityReferenceAxis", JsonUtils.GetVector3Object(velocityBillboard ? Vector3.forward : Vector3.up));
        compData.AddField("velocityReferenceSpace", velocityBillboard ? 1 : 0);
        compData.AddField("verticalAlignment", 1);
        bool fixedBillboard = renderer.renderMode == ParticleSystemRenderMode.HorizontalBillboard || renderer.renderMode == ParticleSystemRenderMode.VerticalBillboard;
        compData.AddField("pivotMode", fixedBillboard ? 1 : 0);
        compData.AddField("pivotRotation", fixedBillboard ? -45 : 0);
        compData.AddField("applyActiveColorSpace", getApplyActiveColorSpace(renderer));
        compData.AddField("flip", JsonUtils.GetVector3Object(renderer.flip));
        writeCustomVertexStreams(renderer, compData);

        JSONObject meshes = new JSONObject(JSONObject.Type.ARRAY);
        var meshCount = renderer.meshCount;
        Mesh[] particleMeshes = new Mesh[meshCount];
        renderer.GetMeshes(particleMeshes);
        for (int i = 0; i < meshCount; i++)
        {
            JSONObject meshItemObj = new JSONObject(JSONObject.Type.OBJECT);
            JsonUtils.SetComponentsType(meshItemObj, "MeshItem");
            meshItemObj.AddField("mesh", map.GetMeshData(
                particleMeshes[i], renderer));
            meshes.Add(meshItemObj);
        }
        compData.AddField("meshes", meshes);

        Vector3 pivot = CpuParticleCoordinateConverter.ConvertRendererPivot(
            renderer.renderMode,
            renderer.alignment,
            renderer.pivot);
        compData.AddField("pivot", JsonUtils.GetVector3Object(pivot));
        return compData;
    }

    private static JSONObject writeMinMaxGradientData(ParticleSystem.MinMaxGradient gradient)
    {
        JSONObject curveData = new JSONObject(JSONObject.Type.OBJECT);
        ParticleSystemGradientMode mode = gradient.mode;
        curveData.AddField("mode", (int)(object)mode);

        // MinMaxGradient behaves like a tagged union. Reading properties that do not
        // belong to the active mode can return undefined native data in some Unity
        // versions, which previously produced NaN and extreme float values in .lh.
        switch (mode)
        {
            case ParticleSystemGradientMode.Color:
                curveData.AddField("colorMax", JsonUtils.GetColorObject(gradient.color));
                break;
            case ParticleSystemGradientMode.Gradient:
            case ParticleSystemGradientMode.RandomColor:
                writeGradientData(gradient.gradient, "gradientMax", curveData);
                break;
            case ParticleSystemGradientMode.TwoColors:
                curveData.AddField("colorMin", JsonUtils.GetColorObject(gradient.colorMin));
                curveData.AddField("colorMax", JsonUtils.GetColorObject(gradient.colorMax));
                break;
            case ParticleSystemGradientMode.TwoGradients:
                writeGradientData(gradient.gradientMin, "gradientMin", curveData);
                writeGradientData(gradient.gradientMax, "gradientMax", curveData);
                break;
        }
        return curveData;
    }

    private static void writeGradientData(Gradient gradient, string propname, JSONObject props)
    {
        if (gradient == null)
        {
            return;
        }
        JSONObject gradientData = new JSONObject(JSONObject.Type.OBJECT);
        {
            JSONObject alphaElements = new JSONObject(JSONObject.Type.OBJECT);
            gradientData.AddField("_alphaElements", alphaElements);
            JsonUtils.SetComponentsType(alphaElements, "Float32Array");
            JSONObject alphaElementValue = new JSONObject(JSONObject.Type.ARRAY);
            for (var i = 0; i < gradient.alphaKeys.Length; i++)
            {
                alphaElementValue.Add(gradient.alphaKeys[i].time);
                alphaElementValue.Add(gradient.alphaKeys[i].alpha);
            }
            alphaElements.AddField("value", alphaElementValue);
            gradientData.AddField("_colorAlphaKeysCount", gradient.alphaKeys.Length);
        }
        {
            JSONObject colorElements = new JSONObject(JSONObject.Type.OBJECT);
            gradientData.AddField("_rgbElements", colorElements);
            JsonUtils.SetComponentsType(colorElements, "Float32Array");
            JSONObject colorElementValue = new JSONObject(JSONObject.Type.ARRAY);
            for (var i = 0; i < gradient.colorKeys.Length; i++)
            {
                colorElementValue.Add(gradient.colorKeys[i].time);
                colorElementValue.Add(gradient.colorKeys[i].color.r);
                colorElementValue.Add(gradient.colorKeys[i].color.g);
                colorElementValue.Add(gradient.colorKeys[i].color.b);
            }
            colorElements.AddField("value", colorElementValue);
            gradientData.AddField("_colorRGBKeysCount", gradient.colorKeys.Length);
        }
        props.AddField(propname, gradientData);
    }

    public static JSONObject writeMinMaxCurveData(ParticleSystem.MinMaxCurve curve, float factor = 1.0f)
    {
        JSONObject curveData = new JSONObject(JSONObject.Type.OBJECT);
        curveData.AddField("mode", (int)(object)curve.mode);
        switch (curve.mode)
        {
            case ParticleSystemCurveMode.Constant:
                curveData.AddField("constant", curve.constant * factor);
                break;
            case ParticleSystemCurveMode.Curve:
                curveData.AddField("curve", getAnimationCurveData(curve.curve));
                curveData.AddField("curveMultiplier", curve.curveMultiplier * factor);
                break;
            case ParticleSystemCurveMode.TwoConstants:
                curveData.AddField("constantMax", curve.constantMax * factor);
                curveData.AddField("constantMin", curve.constantMin * factor);
                break;
            case ParticleSystemCurveMode.TwoCurves:
                curveData.AddField("curveMax", getAnimationCurveData(curve.curveMax));
                curveData.AddField("curveMin", getAnimationCurveData(curve.curveMin));
                curveData.AddField("curveMultiplier", curve.curveMultiplier * factor);
                break;
        }
        return curveData;
    }

    public static JSONObject getAnimationCurveData(AnimationCurve animationcurve)
    {
        JSONObject animationcurveData = new JSONObject(JSONObject.Type.OBJECT);
        if (animationcurve != null && animationcurve.length > 0)
        {
            JSONObject subnodeArray = new JSONObject(JSONObject.Type.ARRAY);
            for (int i = 0; i < animationcurve.length; i++)
            {
                var key = animationcurve[i];
                if (float.IsNaN(key.time) || float.IsInfinity(key.time)
                    || float.IsNaN(key.value) || float.IsInfinity(key.value))
                {
                    throw new System.InvalidOperationException(
                        "Cannot export CPU particle curve key " + i
                        + " (time=" + key.time + ", value=" + key.value
                        + "): keyframe time and value must be finite.");
                }

                JSONObject subnodeObject = new JSONObject(JSONObject.Type.OBJECT);
                JsonUtils.SetComponentsType(subnodeObject, "FloatKeyframe");
                subnodeObject.AddField("time", key.time);
                // Unity curves may overshoot the inspector's normalized range.
                // Keep the original value; dropping or clamping it corrupts the curve.
                subnodeObject.AddField("value", key.value);
                subnodeObject.AddField("inTangent", key.inTangent);
                subnodeObject.AddField("outTangent", key.outTangent);
                subnodeObject.AddField("inWeight", key.inWeight);
                subnodeObject.AddField("outWeight", key.outWeight);
                subnodeArray.Add(subnodeObject);
            }
            animationcurveData.AddField("keys", subnodeArray);
        }
        return animationcurveData;
    }
}
