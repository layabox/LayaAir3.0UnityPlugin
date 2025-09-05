using UnityEngine;
using static UnityEngine.ParticleSystem;

internal class ParticleSystemData
{
    private static JSONObject writeBurst(Burst burst)
    {
        JSONObject dataObject = new JSONObject(JSONObject.Type.OBJECT);
        JsonUtils.SetComponentsType(dataObject, "PlusBurst");
        dataObject.AddField("cycleCount", burst.cycleCount);
        dataObject.AddField("count", writeMinMaxCurveData(burst.count));
        dataObject.AddField("probability", burst.probability);
        dataObject.AddField("time", burst.time);
        dataObject.AddField("repeatInterval", burst.repeatInterval);
        return dataObject;
    }

    private static JSONObject writeBaseNode(ParticleSystem particleSystem, JSONObject sysData)
    {
        JSONObject mainObject = new JSONObject(JSONObject.Type.OBJECT);
        JSONObject particleSystemData = new JSONObject(JSONObject.Type.OBJECT);
        //JsonUtils.SetComponentsType(mainObject, "MainModule");
        MainModule main = particleSystem.main;
        mainObject.AddField("duration", main.duration);
        mainObject.AddField("loop", main.loop);
        // mainObject.AddField("prewarm", main.prewarm);
        // //startDelay
        mainObject.AddField("startDelay", writeMinMaxCurveData(main.startDelay, 1, 0, 1));
        mainObject.AddField("startLifetime", writeMinMaxCurveData(main.startLifetime, 1, 0, 1));
        mainObject.AddField("startSpeed", writeMinMaxCurveData(main.startSpeed));
        // //threeDStartSize
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

        // //threeDStartRotation
        mainObject.AddField("startRotation3D", main.startRotation3D);
        if (main.startRotation3D)
        {
            mainObject.AddField("startRotationX", writeMinMaxCurveData(main.startRotationX, Mathf.Rad2Deg));
            mainObject.AddField("startRotationY", writeMinMaxCurveData(main.startRotationY, -Mathf.Rad2Deg));
            mainObject.AddField("startRotationZ", writeMinMaxCurveData(main.startRotationZ, -Mathf.Rad2Deg));
        }
        else
        {
            mainObject.AddField("startRotation", writeMinMaxCurveData(main.startRotation, Mathf.Rad2Deg));

        }



        // //randomizeRotationDirection (?)
        mainObject.AddField("flipRotation", main.flipRotation);

        // mainObject.AddField("startColor", writeMinMaxGradientData(main.startColor));

        mainObject.AddField("gravityModifier", writeMinMaxCurveData(main.gravityModifier));

        mainObject.AddField("simulationSpace", (int)(object)main.simulationSpace);
        mainObject.AddField("simulationSpeed", particleSystem.main.simulationSpeed);
        mainObject.AddField("useUnscaledTime", particleSystem.main.useUnscaledTime);
        mainObject.AddField("scalingMode", (int)(object)main.scalingMode);


        // //playOnAwake
        mainObject.AddField("playOnAwake", particleSystem.main.playOnAwake);



        mainObject.AddField("maxParticles", particleSystem.main.maxParticles);
        mainObject.AddField("stopAction", (int)(object)particleSystem.main.stopAction);
        mainObject.AddField("cullingMode", (int)(object)particleSystem.main.cullingMode);
        mainObject.AddField("ringBufferMode", (int)(object)particleSystem.main.ringBufferMode);


        particleSystemData.AddField("useAutoRandomSeed", particleSystem.useAutoRandomSeed);
        if (!particleSystem.useAutoRandomSeed)
            particleSystemData.AddField("randomSeed", particleSystem.randomSeed);

        particleSystemData.AddField("main", mainObject);
        sysData.AddField("particleSystem", particleSystemData);
        return particleSystemData;
    }

    private static void writeRotationOverLifetime(ParticleSystem particleSystem, JSONObject sysData)
    {
        RotationOverLifetimeModule rotationOverLifetime = particleSystem.rotationOverLifetime;
        JSONObject dataObject = new JSONObject(JSONObject.Type.OBJECT);
        JsonUtils.SetComponentsType(dataObject, "PlusRotationOverLife");
        dataObject.AddField("enable", rotationOverLifetime.enabled);
        dataObject.AddField("separateAxes", rotationOverLifetime.separateAxes);

        dataObject.AddField("x", writeMinMaxCurveData(rotationOverLifetime.x, Mathf.Rad2Deg));
        dataObject.AddField("y", writeMinMaxCurveData(rotationOverLifetime.y, -Mathf.Rad2Deg));
        dataObject.AddField("z", writeMinMaxCurveData(rotationOverLifetime.z, -Mathf.Rad2Deg));
        sysData.AddField("rotationOverLifetime", dataObject);
        //  return dataObject;
    }
    private static void writeForceOverLifetime(ParticleSystem particleSystem, JSONObject sysData)
    {
        Vector3 spaceChage = SpaceUtils.getDirection();
        ForceOverLifetimeModule forceOverLifetime = particleSystem.forceOverLifetime;
        JSONObject dataObject = new JSONObject(JSONObject.Type.OBJECT);
        JsonUtils.SetComponentsType(dataObject, "PlusForceOverLife");
        dataObject.AddField("enable", forceOverLifetime.enabled);
        dataObject.AddField("space", (int)(object)forceOverLifetime.space);
        dataObject.AddField("x", writeMinMaxCurveData(forceOverLifetime.x, spaceChage.x));
        dataObject.AddField("y", writeMinMaxCurveData(forceOverLifetime.y, spaceChage.y));
        dataObject.AddField("z", writeMinMaxCurveData(forceOverLifetime.z, spaceChage.z));
        dataObject.AddField("randomized", forceOverLifetime.randomized);
        sysData.AddField("forceOverLifetime", dataObject);
    }

    private static void writeVelocityOverLifetime(ParticleSystem particleSystem, JSONObject sysData)
    {
        Vector3 spaceChage = SpaceUtils.getDirection();
        VelocityOverLifetimeModule velocityOverLifetime = particleSystem.velocityOverLifetime;
        JSONObject dataObject = new JSONObject(JSONObject.Type.OBJECT);
        JsonUtils.SetComponentsType(dataObject, "PlusVelocityOverLife");
        dataObject.AddField("enable", velocityOverLifetime.enabled);
        dataObject.AddField("speedModifier", writeMinMaxCurveData(velocityOverLifetime.speedModifier));
        dataObject.AddField("x", writeMinMaxCurveData(velocityOverLifetime.x, spaceChage.x));
        dataObject.AddField("y", writeMinMaxCurveData(velocityOverLifetime.y, spaceChage.y));
        dataObject.AddField("z", writeMinMaxCurveData(velocityOverLifetime.z, spaceChage.z));
        dataObject.AddField("space", (int)(object)velocityOverLifetime.space);
        dataObject.AddField("orbitalX", writeMinMaxCurveData(velocityOverLifetime.orbitalX));
        dataObject.AddField("orbitalY", writeMinMaxCurveData(velocityOverLifetime.orbitalY));
        dataObject.AddField("orbitalZ", writeMinMaxCurveData(velocityOverLifetime.orbitalZ));
        dataObject.AddField("orbitalOffsetX", writeMinMaxCurveData(velocityOverLifetime.orbitalOffsetX));
        dataObject.AddField("orbitalOffsetY", writeMinMaxCurveData(velocityOverLifetime.orbitalOffsetY));
        dataObject.AddField("orbitalOffsetZ", writeMinMaxCurveData(velocityOverLifetime.orbitalOffsetZ));

        dataObject.AddField("radial", writeMinMaxCurveData(velocityOverLifetime.radial));
        sysData.AddField("velocityOverLifetime", dataObject);
    }

    private static void writeSizeOverLifetime(ParticleSystem particleSystem, JSONObject sysData)
    {
        SizeOverLifetimeModule sizeOverLifetime = particleSystem.sizeOverLifetime;
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
    private static void writeEmission(ParticleSystem particleSystem, JSONObject sysData)
    {
        EmissionModule emission = particleSystem.emission;
        JSONObject emissionObject = new JSONObject(JSONObject.Type.OBJECT);
        //JsonUtils.SetComponentsType(emissionObject, "PlusEmission");
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
    private static void writeShape(ParticleSystem particleSystem, JSONObject sysData, NodeMap map, ResoureMap resMap)
    {
        JSONObject shapObject = new JSONObject(JSONObject.Type.OBJECT);
        JsonUtils.SetComponentsType(shapObject, "PlusShape");
        ShapeModule shape = particleSystem.shape;
        shapObject.AddField("enable", shape.enabled);
        // if (shape.shapeType == ParticleSystemShapeType.Box)
        // {
        //     JsonUtils.SetComponentsType(shapObject, "PlusBoxShape");
        //     shapObject.AddField("emitfrom", 0);
        // }
        // if (shape.shapeType == ParticleSystemShapeType.BoxEdge)
        // {
        //     JsonUtils.SetComponentsType(shapObject, "PlusBoxShape");
        //     shapObject.AddField("emitfrom", 2);
        // }
        // if (shape.shapeType == ParticleSystemShapeType.BoxShell)
        // {
        //     JsonUtils.SetComponentsType(shapObject, "PlusBoxShape");
        //     shapObject.AddField("emitfrom", 1);
        // }
        // else if (shape.shapeType == ParticleSystemShapeType.Donut)
        // {
        //     JsonUtils.SetComponentsType(shapObject, "PlusDountShape");
        //     shapObject.AddField("radius", shape.radius);
        //     shapObject.AddField("radiusThickness", shape.radiusThickness);
        //     shapObject.AddField("arc", shape.arc);
        //     shapObject.AddField("donutRadius", shape.donutRadius);
        // }
        // else if (shape.shapeType == ParticleSystemShapeType.Circle)
        // {
        //     JsonUtils.SetComponentsType(shapObject, "PlusCircleShape");
        //     shapObject.AddField("radius", shape.radius);
        //     shapObject.AddField("radiusThickness", shape.radiusThickness);
        //     shapObject.AddField("arc", shape.arc);
        // }
        // else if (shape.shapeType == ParticleSystemShapeType.Cone)
        // {
        //     JsonUtils.SetComponentsType(shapObject, "PlusConeShape");
        //     shapObject.AddField("radius", shape.radius);
        //     shapObject.AddField("radiusThickness", shape.radiusThickness);
        //     shapObject.AddField("arc", shape.arc);
        //     shapObject.AddField("angle", shape.angle);
        //     shapObject.AddField("length", shape.length);
        //     shapObject.AddField("emitfrom", 0);
        // }
        // else if (shape.shapeType == ParticleSystemShapeType.ConeVolume)
        // {
        //     JsonUtils.SetComponentsType(shapObject, "PlusConeShape");
        //     shapObject.AddField("radius", shape.radius);
        //     shapObject.AddField("radiusThickness", shape.radiusThickness);
        //     shapObject.AddField("arc", shape.arc);
        //     shapObject.AddField("angle", shape.angle);
        //     shapObject.AddField("length", shape.length);
        //     shapObject.AddField("emitfrom", 1);
        // }
        // else if (shape.shapeType == ParticleSystemShapeType.Hemisphere)
        // {
        //     JsonUtils.SetComponentsType(shapObject, "PlusHemisphereShape");
        //     shapObject.AddField("radius", shape.radius);
        //     shapObject.AddField("radiusThickness", shape.radiusThickness);
        //     shapObject.AddField("arc", shape.arc);
        // }
        // else if (shape.shapeType == ParticleSystemShapeType.Sphere)
        // {
        //     JsonUtils.SetComponentsType(shapObject, "PlusSphereShape");
        //     shapObject.AddField("radius", shape.radius);
        //     shapObject.AddField("radiusThickness", shape.radiusThickness);
        //     shapObject.AddField("arc", shape.arc);
        // }
        // else if (shape.shapeType == ParticleSystemShapeType.SingleSidedEdge)
        // {
        //     JsonUtils.SetComponentsType(shapObject, "PlusSideEdgeShape");
        //     shapObject.AddField("radius", shape.radius);
        //     shapObject.AddField("radiusMode", (int)(object)shape.radiusMode);
        //     shapObject.AddField("radiusSpread", shape.radiusSpread);
        //     shapObject.AddField("radiusSpeed", writeMinMaxCurveData(shape.radiusSpeed));
        // }
        // else if (shape.shapeType == ParticleSystemShapeType.Mesh)
        // {
        //     JsonUtils.SetComponentsType(shapObject, "PlusMeshShape");
        //     shapObject.AddField("mesh", resMap.GetMeshData(shape.mesh, null));
        //     shapObject.AddField("type", (int)(object)shape.meshShapeType);
        //     shapObject.AddField("mode", (int)(object)shape.meshSpawnMode);
        //     shapObject.AddField("singleMaterial", shape.useMeshMaterialIndex);
        //     shapObject.AddField("subMeshId", shape.meshMaterialIndex);
        // }
        // else if (shape.shapeType == ParticleSystemShapeType.MeshRenderer)
        // {
        //     JsonUtils.SetComponentsType(shapObject, "PlusMeshRenderShape");
        //     shapObject.AddField("render", map.getRefNodeIdObjet(shape.meshRenderer.gameObject, "MeshRenderer"));
        //     shapObject.AddField("type", (int)(object)shape.meshShapeType);
        //     shapObject.AddField("mode", (int)(object)shape.meshSpawnMode);
        //     shapObject.AddField("singleMaterial", shape.useMeshMaterialIndex);
        //     shapObject.AddField("subMeshId", shape.meshMaterialIndex);
        // }
        // else if (shape.shapeType == ParticleSystemShapeType.Rectangle)
        // {
        //     JsonUtils.SetComponentsType(shapObject, "PlusRectangleShape");
        // }


        // shapObject.AddField("scale", JsonUtils.GetVector3Object(shape.scale));
        // Vector3 pos = shape.position;
        // SpaceUtils.changePostion(ref pos);
        // shapObject.AddField("position", JsonUtils.GetVector3Object(pos));
        // Vector3 rotate = shape.rotation;
        // SpaceUtils.changeRotateEuler(ref rotate, false);
        // shapObject.AddField("rotation", JsonUtils.GetVector3Object(rotate));
        // shapObject.AddField("alignToDirection", shape.alignToDirection);
        // shapObject.AddField("randomDirection", shape.randomDirectionAmount);
        // shapObject.AddField("sphericalDirection", shape.sphericalDirectionAmount);
        // shapObject.AddField("randomPosition", shape.randomPositionAmount);
        sysData.AddField("shape", shapObject);
    }
    private static void writeLifetimeByEmitterSpeed(ParticleSystem particleSystem, JSONObject sysData)
    {
        LifetimeByEmitterSpeedModule lifetimeByEmitterSpeed = particleSystem.lifetimeByEmitterSpeed;
        JSONObject dataObject = new JSONObject(JSONObject.Type.OBJECT);
        JsonUtils.SetComponentsType(dataObject, "PlusLifetimeByEmitterSpeed");
        dataObject.AddField("enable", lifetimeByEmitterSpeed.enabled);
        dataObject.AddField("curve", writeMinMaxCurveData(lifetimeByEmitterSpeed.curve));
        dataObject.AddField("range", JsonUtils.GetVector2Object(lifetimeByEmitterSpeed.range));
        sysData.AddField("lifetimeByEmitterSpeed", dataObject);
    }

    private static void writeLimitVelocityOverLifetime(ParticleSystem particleSystem, JSONObject sysData)
    {
        LimitVelocityOverLifetimeModule limitVelocityOverLifetime = particleSystem.limitVelocityOverLifetime;
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
        dataObject.AddField("multiplyDragBySize", limitVelocityOverLifetime.multiplyDragByParticleSize);
        dataObject.AddField("multiplyDragByVelocity", limitVelocityOverLifetime.multiplyDragByParticleVelocity);
        sysData.AddField("limitVelocityOverLifetime", dataObject);
    }
    private static void writeColorOverLifetime(ParticleSystem particleSystem, JSONObject sysData)
    {
        ColorOverLifetimeModule colorOverLifetime = particleSystem.colorOverLifetime;
        JSONObject dataObject = new JSONObject(JSONObject.Type.OBJECT);
        JsonUtils.SetComponentsType(dataObject, "PlusColorOverLife");
        dataObject.AddField("enable", colorOverLifetime.enabled);
        dataObject.AddField("color", writeMinMaxGradientData(colorOverLifetime.color));
        sysData.AddField("colorOverLifetime", dataObject);
    }

    private static void writeColorBySpeed(ParticleSystem particleSystem, JSONObject sysData)
    {
        ColorBySpeedModule colorBySpeed = particleSystem.colorBySpeed;
        JSONObject dataObject = new JSONObject(JSONObject.Type.OBJECT);
        JsonUtils.SetComponentsType(dataObject, "PlusColorBySpeed");
        dataObject.AddField("enable", colorBySpeed.enabled);
        dataObject.AddField("color", writeMinMaxGradientData(colorBySpeed.color));
        dataObject.AddField("range", JsonUtils.GetVector2Object(colorBySpeed.range));
        sysData.AddField("colorBySpeed", dataObject);
    }

    private static void writeSizeBySpeed(ParticleSystem particleSystem, JSONObject sysData)
    {
        SizeBySpeedModule sizeBySpeed = particleSystem.sizeBySpeed;
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
    private static void writeExternalForces(ParticleSystem particleSystem, JSONObject sysData)
    {
        ExternalForcesModule externalForces = particleSystem.externalForces;
        JSONObject dataObject = new JSONObject(JSONObject.Type.OBJECT);
        JsonUtils.SetComponentsType(dataObject, "PlusExternalForces");
        dataObject.AddField("enable", externalForces.enabled);
        dataObject.AddField("multiplier", writeMinMaxCurveData(externalForces.multiplier));
        dataObject.AddField("influenceFilter", (int)(object)externalForces.influenceFilter);
        //TODO
        Debug.Log(externalForces.influenceCount);
        sysData.AddField("externalForces", dataObject);
    }
    private static void writeRotationBySpeed(ParticleSystem particleSystem, JSONObject sysData)
    {
        RotationBySpeedModule rotationBySpeed = particleSystem.rotationBySpeed;
        JSONObject dataObject = new JSONObject(JSONObject.Type.OBJECT);
        JsonUtils.SetComponentsType(dataObject, "PlusRotationBySpeed");
        dataObject.AddField("enable", rotationBySpeed.enabled);
        dataObject.AddField("separateAxes", rotationBySpeed.separateAxes);
        dataObject.AddField("x", writeMinMaxCurveData(rotationBySpeed.x, Mathf.Rad2Deg));
        dataObject.AddField("y", writeMinMaxCurveData(rotationBySpeed.y, -Mathf.Rad2Deg));
        dataObject.AddField("z", writeMinMaxCurveData(rotationBySpeed.z, -Mathf.Rad2Deg));
        dataObject.AddField("range", JsonUtils.GetVector2Object(rotationBySpeed.range));
        sysData.AddField("rotationBySpeed", dataObject);
    }

    private static void writeInheritVelocity(ParticleSystem particleSystem, JSONObject sysData)
    {
        InheritVelocityModule inheritVelocity = particleSystem.inheritVelocity;
        JSONObject dataObject = new JSONObject(JSONObject.Type.OBJECT);
        JsonUtils.SetComponentsType(dataObject, "PlusInheritVelocity");
        dataObject.AddField("enable", inheritVelocity.enabled);
        dataObject.AddField("mode", (int)(object)inheritVelocity.mode);
        dataObject.AddField("curveMultiplier", writeMinMaxCurveData(inheritVelocity.curveMultiplier));
        sysData.AddField("inheritVelocity", dataObject);
    }


    private static void writeNoise(ParticleSystem particleSystem, JSONObject sysData)
    {
        NoiseModule noise = particleSystem.noise;
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
        dataObject.AddField("remap", writeMinMaxCurveData(noise.remap));


        dataObject.AddField("positionAmount", writeMinMaxCurveData(noise.positionAmount));
        dataObject.AddField("rotationAmount", writeMinMaxCurveData(noise.rotationAmount));
        dataObject.AddField("sizeAmount", writeMinMaxCurveData(noise.sizeAmount));

        sysData.AddField("noise", dataObject);
    }


    private static void writeTextureSheetAnimation(ParticleSystem particleSystem, JSONObject sysData)
    {
        TextureSheetAnimationModule textureSheetAnimation = particleSystem.textureSheetAnimation;
        JSONObject dataObject = new JSONObject(JSONObject.Type.OBJECT);
        JsonUtils.SetComponentsType(dataObject, "PlusTextureSheetAnimation");
        dataObject.AddField("enable", textureSheetAnimation.enabled);
        dataObject.AddField("numTiles", JsonUtils.GetVector2Object(textureSheetAnimation.numTilesX, textureSheetAnimation.numTilesY));
        dataObject.AddField("animation", (int)(object)textureSheetAnimation.animation);
        dataObject.AddField("startFrame", writeMinMaxCurveData(textureSheetAnimation.startFrame));
        dataObject.AddField("cycleCount", textureSheetAnimation.cycleCount);
        dataObject.AddField("rowIndex", textureSheetAnimation.rowIndex);
        dataObject.AddField("rowMode", (int)(object)textureSheetAnimation.rowMode);
        dataObject.AddField("frameOverTime", writeMinMaxCurveData(textureSheetAnimation.frameOverTime));
        sysData.AddField("textureSheetAnimation", dataObject);
    }

    private static void writeSubEmittersModule(ParticleSystem particleSystem, JSONObject sysData, NodeMap map)
    {
        SubEmittersModule subEmitters = particleSystem.subEmitters;
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
            subData.AddField("emitter", map.getRefNodeIdObjet(subSys.gameObject, "ParticleSystem"));
            subData.AddField("type", (int)(object)subEmitters.GetSubEmitterType(i));
            subData.AddField("properties", (int)(object)subEmitters.GetSubEmitterProperties(i));
            subData.AddField("emitProbability", subEmitters.GetSubEmitterEmitProbability(i));
            subDatas.Add(subData);
        }
        dataObject.AddField("subDatas", subDatas);
        sysData.AddField("subEmitters", dataObject);
    }
    public static JSONObject GetParticleSystem(ParticleSystem particleSystem, bool isOverride, NodeMap map, ResoureMap resMap)
    {
        JSONObject compData = JsonUtils.SetComponentsType(new JSONObject(JSONObject.Type.OBJECT), "ParticleSystem", isOverride);
        JSONObject particleSystemData = writeBaseNode(particleSystem, compData);
        writeEmission(particleSystem, particleSystemData);
        //writeShape(particleSystem, compData, map, resMap);
        writeVelocityOverLifetime(particleSystem, particleSystemData);
        writeLimitVelocityOverLifetime(particleSystem, particleSystemData);
        writeLifetimeByEmitterSpeed(particleSystem, particleSystemData);
        writeForceOverLifetime(particleSystem, particleSystemData);
        writeColorOverLifetime(particleSystem, particleSystemData);
        writeColorBySpeed(particleSystem, particleSystemData);
        writeSizeOverLifetime(particleSystem, particleSystemData);
        writeSizeBySpeed(particleSystem, particleSystemData);
        writeRotationOverLifetime(particleSystem, particleSystemData);
        writeRotationBySpeed(particleSystem, particleSystemData);
        writeExternalForces(particleSystem, particleSystemData);
        // writeInheritVelocity(particleSystem, compData);
        writeNoise(particleSystem, particleSystemData);
        // writeTextureSheetAnimation(particleSystem, compData);
        // writeSubEmittersModule(particleSystem, compData, map);

        return compData;
    }


    public static JSONObject GetParticleSystemRenderer(ParticleSystemRenderer renderer, bool isOverride, ResoureMap map)
    {
        JSONObject compData = JsonUtils.SetComponentsType(new JSONObject(JSONObject.Type.OBJECT), "ParticleSystemRenderer", isOverride);
        compData.AddField("renderMode", (int)(object)renderer.renderMode);
        compData.AddField("sortMode", (int)(object)renderer.sortMode);
        compData.AddField("alignment", (int)(object)renderer.alignment);
        compData.AddField("material", map.GetMaterialData(renderer.sharedMaterial));
        compData.AddField("cameraVelocityScale", renderer.cameraVelocityScale);
        compData.AddField("velocityScale", renderer.velocityScale);
        compData.AddField("lengthScale", renderer.lengthScale);
        if (renderer.mesh)
        {
            compData.AddField("sharedMesh", map.GetMeshData(renderer.mesh, renderer));
        }
        compData.AddField("pivot", JsonUtils.GetVector3Object(renderer.pivot));
        return compData;
    }

    private static JSONObject writeMinMaxGradientData(MinMaxGradient gradient)
    {
        JSONObject curveData = new JSONObject(JSONObject.Type.OBJECT);
        JsonUtils.SetComponentsType(curveData, "MinMaxGradient");
        writeGradientData(gradient.gradient, "gradient", curveData);
        writeGradientData(gradient.gradientMax, "gradientMax", curveData);
        writeGradientData(gradient.gradientMin, "gradientMin", curveData);
        curveData.AddField("color", JsonUtils.GetColorObject(gradient.color));
        curveData.AddField("colorMax", JsonUtils.GetColorObject(gradient.colorMax));
        curveData.AddField("colorMin", JsonUtils.GetColorObject(gradient.colorMin));
        curveData.AddField("mode", (int)(object)gradient.mode);
        return curveData;
    }

    private static void writeGradientData(Gradient gradient, string propname, JSONObject props)
    {
        if (gradient == null)
        {
            return;
        }
        JSONObject gradientData = new JSONObject(JSONObject.Type.OBJECT);
        JsonUtils.SetComponentsType(gradientData, "Gradient");
        //alpha
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

        //Color
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
    private static JSONObject writeMinMaxCurveData(MinMaxCurve curve, float factor = 1.0f, float min = -1, float max = 1)
    {
        JSONObject curveData = new JSONObject(JSONObject.Type.OBJECT);
        //JsonUtils.SetComponentsType(curveData, "MinMaxCurve");
        curveData.AddField("mode", (int)(object)curve.mode);
        switch (curve.mode)
        {
            case ParticleSystemCurveMode.Constant:
                curveData.AddField("constant", curve.constant * factor);
                break;
            case ParticleSystemCurveMode.Curve:
                curveData.AddField("curve", ParticleSystemData.getAnimationCurveData(curve.curve, min, max));
                curveData.AddField("curveMultiplier", curve.curveMultiplier * factor);
                break;
            case ParticleSystemCurveMode.TwoConstants:
                curveData.AddField("constantMax", curve.constantMax * factor);
                curveData.AddField("constantMin", curve.constantMin * factor);
                break;
            case ParticleSystemCurveMode.TwoCurves:
                curveData.AddField("curveMax", ParticleSystemData.getAnimationCurveData(curve.curveMax, min, max));
                curveData.AddField("curveMin", ParticleSystemData.getAnimationCurveData(curve.curveMin, min, max));
                curveData.AddField("curveMultiplier", curve.curveMultiplier * factor);
                break;
        }
        return curveData;
    }


    private static JSONObject getAnimationCurveData(AnimationCurve animationcurve, float min = -1, float max = 1)
    {
        JSONObject animationcurveData = new JSONObject(JSONObject.Type.OBJECT);
        //JsonUtils.SetComponentsType(animationcurveData, "AnimationCurve");
        if (animationcurve != null && animationcurve.length > 0)
        {
            JSONObject subnodeArray = new JSONObject(JSONObject.Type.ARRAY);
            for (int i = 0; i < animationcurve.length; i++)
            {
                JSONObject subnodeObject = new JSONObject(JSONObject.Type.OBJECT);
                JsonUtils.SetComponentsType(subnodeObject, "FloatKeyframe");
                subnodeObject.AddField("time", animationcurve[i].time);
                float value = animationcurve[i].value;
                if (value >= min && value <= max)
                {
                    subnodeObject.AddField("value", value);
                }
                else
                {
                    Debug.LogError("不在范围内");
                }

                subnodeObject.AddField("inTangent", animationcurve[i].inTangent);
                subnodeObject.AddField("outTangent", animationcurve[i].outTangent);
                subnodeObject.AddField("inWeight", animationcurve[i].inWeight);
                subnodeObject.AddField("outWeight", animationcurve[i].outWeight);
                subnodeArray.Add(subnodeObject);
            }
            animationcurveData.AddField("keys", subnodeArray);
        }
        return animationcurveData;

    }
}