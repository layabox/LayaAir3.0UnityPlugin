using UnityEngine;

internal class ParticleSystemData
{
    private static JSONObject writeBurst(ParticleSystem.Burst burst)
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

    private static JSONObject writeBaseNode(UnityEngine.ParticleSystem particleSystem, JSONObject sysData)
    {
        JSONObject mainObject = new JSONObject(JSONObject.Type.OBJECT);
        JSONObject particleSystemData = new JSONObject(JSONObject.Type.OBJECT);
        var main = particleSystem.main;

        mainObject.AddField("duration", main.duration);
        mainObject.AddField("loop", main.loop);
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

        mainObject.AddField("startColor", writeMinMaxGradientData(main.startColor));

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

    private static void writeRotationOverLifetime(UnityEngine.ParticleSystem particleSystem, JSONObject sysData)
    {
        ParticleSystem.RotationOverLifetimeModule rotationOverLifetime = particleSystem.rotationOverLifetime;
        //if (!rotationOverLifetime.enabled) return;
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
    private static void writeForceOverLifetime(UnityEngine.ParticleSystem particleSystem, JSONObject sysData)
    {
        ParticleSystem.ForceOverLifetimeModule forceOverLifetime = particleSystem.forceOverLifetime;
        //if (!forceOverLifetime.enabled) return;
        Vector3 spaceChage = SpaceUtils.getDirection();
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

    private static void writeVelocityOverLifetime(UnityEngine.ParticleSystem particleSystem, JSONObject sysData)
    {
        ParticleSystem.VelocityOverLifetimeModule velocityOverLifetime = particleSystem.velocityOverLifetime;
        //if (!velocityOverLifetime.enabled) return;
        Vector3 spaceChage = SpaceUtils.getDirection();
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

    private static void writeSizeOverLifetime(UnityEngine.ParticleSystem particleSystem, JSONObject sysData)
    {
        ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = particleSystem.sizeOverLifetime;
        //if (!sizeOverLifetime.enabled) return;
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
        shapObject.AddField("enable", shape.enabled);
        shapObject.AddField("type", (int)(object)shape.shapeType);
        shapObject.AddField("radius", shape.radius);
        shapObject.AddField("radiusThickness", shape.radiusThickness);
        shapObject.AddField("arc", shape.arc);
        shapObject.AddField("arcMode", (int)(object)shape.arcMode);
        shapObject.AddField("arcSpread", shape.arcSpread);
        shapObject.AddField("arcSpeed", writeMinMaxCurveData(shape.arcSpeed));
        shapObject.AddField("position", JsonUtils.GetVector3Object(shape.position));
        shapObject.AddField("rotation", JsonUtils.GetVector3Object(shape.rotation));
        shapObject.AddField("scale", JsonUtils.GetVector3Object(shape.scale));
        shapObject.AddField("alignToDirection", shape.alignToDirection);

        shapObject.AddField("randomDirectionAmount", shape.randomDirectionAmount);
        shapObject.AddField("sphericalDirectionAmount", shape.sphericalDirectionAmount);
        shapObject.AddField("randomPositionAmount", shape.randomPositionAmount);

        sysData.AddField("shape", shapObject);
    }
    private static void writeLifetimeByEmitterSpeed(UnityEngine.ParticleSystem particleSystem, JSONObject sysData)
    {
        try
        {
            // 使用dynamic绕过编译时类型检查
            dynamic ps = particleSystem;

            // 尝试访问属性，如果在2018中不存在会抛出异常
            var lifetimeByEmitterSpeed = ps.lifetimeByEmitterSpeed;

            // 检查是否启用（同样用dynamic访问）
            //if (!lifetimeByEmitterSpeed.enabled) return;

            // 后续逻辑和之前一致，但都用dynamic处理
            JSONObject dataObject = new JSONObject(JSONObject.Type.OBJECT);
            JsonUtils.SetComponentsType(dataObject, "PlusLifetimeByEmitterSpeed");
            dataObject.AddField("enable", lifetimeByEmitterSpeed.enabled);
            dataObject.AddField("curve", writeMinMaxCurveData((ParticleSystem.MinMaxCurve)lifetimeByEmitterSpeed.curve));
            dataObject.AddField("range", JsonUtils.GetVector2Object((Vector2)lifetimeByEmitterSpeed.range));
            sysData.AddField("lifetimeByEmitterSpeed", dataObject);
        }
        catch
        {
            // 在2018版本中访问不存在的属性会进入这里，直接忽略
            return;
        }
    }

    private static void writeLimitVelocityOverLifetime(UnityEngine.ParticleSystem particleSystem, JSONObject sysData)
    {
        ParticleSystem.LimitVelocityOverLifetimeModule limitVelocityOverLifetime = particleSystem.limitVelocityOverLifetime;
        //if (!limitVelocityOverLifetime.enabled) return;
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
        //if (!colorOverLifetime.enabled) return;
        JSONObject dataObject = new JSONObject(JSONObject.Type.OBJECT);
        JsonUtils.SetComponentsType(dataObject, "PlusColorOverLife");
        dataObject.AddField("enable", colorOverLifetime.enabled);
        dataObject.AddField("color", writeMinMaxGradientData(colorOverLifetime.color));
        sysData.AddField("colorOverLifetime", dataObject);
    }

    private static void writeColorBySpeed(UnityEngine.ParticleSystem particleSystem, JSONObject sysData)
    {
        ParticleSystem.ColorBySpeedModule colorBySpeed = particleSystem.colorBySpeed;
        //if (!colorBySpeed.enabled) return;
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
        //if (!sizeBySpeed.enabled) return;
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
        //if (!externalForces.enabled) return;
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
    private static void writeRotationBySpeed(UnityEngine.ParticleSystem particleSystem, JSONObject sysData)
    {
        ParticleSystem.RotationBySpeedModule rotationBySpeed = particleSystem.rotationBySpeed;
        //if (!rotationBySpeed.enabled) return;
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

    private static void writeInheritVelocity(UnityEngine.ParticleSystem particleSystem, JSONObject sysData)
    {
        ParticleSystem.InheritVelocityModule inheritVelocity = particleSystem.inheritVelocity;
        //if (!inheritVelocity.enabled) return;
        JSONObject dataObject = new JSONObject(JSONObject.Type.OBJECT);
        JsonUtils.SetComponentsType(dataObject, "PlusInheritVelocity");
        dataObject.AddField("enable", inheritVelocity.enabled);
        dataObject.AddField("mode", (int)(object)inheritVelocity.mode);
        dataObject.AddField("curveMultiplier", writeMinMaxCurveData(inheritVelocity.curveMultiplier));
        sysData.AddField("inheritVelocity", dataObject);
    }
    private static void writeCollision(UnityEngine.ParticleSystem particleSystem, JSONObject sysData, NodeMap map)
    {
        ParticleSystem.CollisionModule collision = particleSystem.collision;
        //if (!collision.enabled) return;
        JSONObject dataObject = new JSONObject(JSONObject.Type.OBJECT);
        JsonUtils.SetComponentsType(dataObject, "PlusCollision");
        dataObject.AddField("enable", collision.enabled);
        dataObject.AddField("type", (int)(object)collision.type);

        try
        {
            // 使用dynamic绕过编译时检查
            dynamic col = collision;

            // 尝试获取平面数量，2018中不存在会抛出异常
            int count = col.planeCount;
            JSONObject subDatas = new JSONObject(JSONObject.Type.ARRAY);

            for (int i = 0; i < count; i++)
            {
                // 尝试获取平面，同样用dynamic处理
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
            // Unity 2018中不存在相关属性，直接添加空数组或忽略
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
        //if (!noise.enabled) return;
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
        //if (!trails.enabled) return;
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
        //if (!textureSheetAnimation.enabled) return;
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
        //if (!subEmitters.enabled) return;
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
    public static JSONObject GetParticleSystem(UnityEngine.ParticleSystem particleSystem, bool isOverride, NodeMap map, ResoureMap resMap)
    {
        JSONObject compData = JsonUtils.SetComponentsType(new JSONObject(JSONObject.Type.OBJECT), "ParticleSystem", isOverride);
        JSONObject particleSystemData = writeBaseNode(particleSystem, compData);
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
        writeRotationOverLifetime(particleSystem, particleSystemData);
        writeRotationBySpeed(particleSystem, particleSystemData);
        writeExternalForces(particleSystem, particleSystemData, map);
        writeInheritVelocity(particleSystem, compData);
        writeNoise(particleSystem, particleSystemData);
        writeCollision(particleSystem, particleSystemData, map);
        writeSubEmittersModule(particleSystem, particleSystemData, map);
        writeTextureSheetAnimation(particleSystem, particleSystemData);
        writeTrails(particleSystem, particleSystemData, map, resMap);

        return compData;
    }


    public static JSONObject GetParticleSystemRenderer(UnityEngine.ParticleSystemRenderer renderer, bool isOverride, ResoureMap map, JSONObject compData)
    {
        //JSONObject compData = JsonUtils.SetComponentsType(new JSONObject(JSONObject.Type.OBJECT), "ParticleSystemRenderer", isOverride);
        compData.AddField("renderMode", (int)(object)renderer.renderMode);
        compData.AddField("sortMode", (int)(object)renderer.sortMode);
        compData.AddField("alignment", (int)(object)renderer.alignment);

        if (renderer.sharedMaterial) { compData.AddField("material", map.GetMaterialData(renderer.sharedMaterial)); }

        if (renderer.trailMaterial) compData.AddField("trailMaterial", map.GetMaterialData(renderer.trailMaterial));

        compData.AddField("cameraVelocityScale", renderer.cameraVelocityScale);
        compData.AddField("velocityScale", renderer.velocityScale);
        compData.AddField("lengthScale", renderer.lengthScale);
        compData.AddField("flip", JsonUtils.GetVector3Object(renderer.flip));
        JSONObject meshes = new JSONObject(JSONObject.Type.ARRAY);
        var meshCount = renderer.meshCount;

        Mesh[] particleMeshes = new Mesh[meshCount];
        renderer.GetMeshes(particleMeshes); // 将网格填充到数组中
        for (int i = 0; i < meshCount; i++)
        {
            JSONObject meshItemObj = new JSONObject(JSONObject.Type.OBJECT);
            JsonUtils.SetComponentsType(meshItemObj, "MeshItem");
            meshItemObj.AddField("mesh", map.GetMeshData(particleMeshes[i], renderer));
            meshes.Add(meshItemObj);
        }
        compData.AddField("meshes", meshes);
        // if (renderer.mesh)
        // {
        //     compData.AddField("sharedMesh", map.GetMeshData(renderer.mesh, renderer));
        // }
        compData.AddField("pivot", JsonUtils.GetVector3Object(renderer.pivot));
        return compData;
    }

    private static JSONObject writeMinMaxGradientData(ParticleSystem.MinMaxGradient gradient)
    {
        JSONObject curveData = new JSONObject(JSONObject.Type.OBJECT);
        //JsonUtils.SetComponentsType(curveData, "MinMaxGradient");
        //writeGradientData(gradient.gradient, "gradient", curveData);
        writeGradientData(gradient.gradientMax, "gradientMax", curveData);
        writeGradientData(gradient.gradientMin, "gradientMin", curveData);
        //curveData.AddField("color", JsonUtils.GetColorObject(gradient.color));
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
        //JsonUtils.SetComponentsType(gradientData, "Gradient");
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
    public static JSONObject writeMinMaxCurveData(ParticleSystem.MinMaxCurve curve, float factor = 1.0f, float min = -1, float max = 1)
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