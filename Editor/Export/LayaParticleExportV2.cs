using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace LayaExport
{
    /// <summary>
    /// 新版LayaAir IDE粒子系统导出器
    /// 基于 Particle.ts 和 Particle.lh 的新结构
    /// </summary>
    public static class LayaParticleExportV2
    {
        /// <summary>
        /// 生成唯一ID (8位随机字符)
        /// </summary>
        private static string GenerateId()
        {
            const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";
            char[] id = new char[8];
            System.Random random = new System.Random();
            for (int i = 0; i < 8; i++)
            {
                id[i] = chars[random.Next(chars.Length)];
            }
            return new string(id);
        }

        /// <summary>
        /// 导出粒子系统组件数据到新版格式
        /// </summary>
        /// <param name="gameObject">要导出的GameObject</param>
        /// <param name="resoureMap">资源映射表，用于复用材质导出逻辑</param>
        internal static JSONObject ExportParticleSystemV2(GameObject gameObject, ResoureMap resoureMap = null)
        {
            ParticleSystem ps = gameObject.GetComponent<ParticleSystem>();
            ParticleSystemRenderer psr = gameObject.GetComponent<ParticleSystemRenderer>();

            if (ps == null || psr == null)
            {
                Debug.LogWarning("LayaAir3D: GameObject does not have ParticleSystem or ParticleSystemRenderer component.");
                return null;
            }

            JSONObject comp = new JSONObject(JSONObject.Type.OBJECT);
            comp.AddField("_$type", "ShurikenParticleRenderer");

            // lightmapScaleOffset
            JSONObject lightmapScaleOffset = new JSONObject(JSONObject.Type.OBJECT);
            lightmapScaleOffset.AddField("_$type", "Vector4");
            comp.AddField("lightmapScaleOffset", lightmapScaleOffset);

            // sharedMaterials - 处理材质
            JSONObject sharedMaterials = new JSONObject(JSONObject.Type.ARRAY);
            Material[] materials = psr.sharedMaterials;
            
            if (materials != null && materials.Length > 0)
            {
                foreach (Material mat in materials)
                {
                    if (mat != null)
                    {
                        // 使用ResoureMap的材质导出逻辑（如果可用）
                        if (resoureMap != null)
                        {
                            sharedMaterials.Add(resoureMap.GetMaterialData(mat));
                        }
                        else
                        {
                            // 回退到简单的材质路径处理
                            JSONObject matRef = new JSONObject(JSONObject.Type.OBJECT);
                            string matPath = AssetDatabase.GetAssetPath(mat.GetInstanceID());
                            if (!string.IsNullOrEmpty(matPath))
                            {
                                string lmatPath = GameObjectUitls.cleanIllegalChar(matPath.Split('.')[0], false) + ".lmat";
                                matRef.AddField("_$uuid", lmatPath);
                            }
                            else
                            {
                                // 使用默认粒子材质
                                matRef.AddField("_$uuid", "../internal/DefaultParticleMaterial.lmat");
                            }
                            matRef.AddField("_$type", "Material");
                            sharedMaterials.Add(matRef);
                        }
                    }
                    else
                    {
                        // 空材质使用默认粒子材质
                        JSONObject defaultMatRef = new JSONObject(JSONObject.Type.OBJECT);
                        defaultMatRef.AddField("_$uuid", "../internal/DefaultParticleMaterial.lmat");
                        defaultMatRef.AddField("_$type", "Material");
                        sharedMaterials.Add(defaultMatRef);
                    }
                }
            }
            else
            {
                // 没有材质时使用默认粒子材质
                JSONObject defaultMatRef = new JSONObject(JSONObject.Type.OBJECT);
                defaultMatRef.AddField("_$uuid", "../internal/DefaultParticleMaterial.lmat");
                defaultMatRef.AddField("_$type", "Material");
                sharedMaterials.Add(defaultMatRef);
            }
            comp.AddField("sharedMaterials", sharedMaterials);

            // Renderer属性
            ExportRendererProperties(psr, comp, resoureMap);

            // _particleSystem
            JSONObject particleSystem = new JSONObject(JSONObject.Type.OBJECT);
            ExportParticleSystemProperties(ps, particleSystem);
            comp.AddField("_particleSystem", particleSystem);

            return comp;
        }

        /// <summary>
        /// 导出渲染器属性
        /// </summary>
        /// <param name="psr">粒子系统渲染器</param>
        /// <param name="comp">组件JSON对象</param>
        /// <param name="resoureMap">资源映射表（可选）</param>
        private static void ExportRendererProperties(ParticleSystemRenderer psr, JSONObject comp, ResoureMap resoureMap = null)
        {
            // renderMode: 0=Billboard, 1=Stretch, 2=HorizontalBillboard, 3=VerticalBillboard, 4=Mesh
            int renderMode = 0;
            switch (psr.renderMode)
            {
                case ParticleSystemRenderMode.Billboard: renderMode = 0; break;
                case ParticleSystemRenderMode.Stretch: renderMode = 1; break;
                case ParticleSystemRenderMode.HorizontalBillboard: renderMode = 2; break;
                case ParticleSystemRenderMode.VerticalBillboard: renderMode = 3; break;
                case ParticleSystemRenderMode.Mesh: renderMode = 4; break;
            }
            if (renderMode != 0)
                comp.AddField("renderMode", renderMode);

            // Stretch Billboard 属性
            if (renderMode == 1)
            {
                if (psr.velocityScale != 0)
                    comp.AddField("stretchedBillboardSpeedScale", psr.velocityScale);
                if (psr.lengthScale != 2)
                    comp.AddField("stretchedBillboardLengthScale", psr.lengthScale);
            }

            // Mesh模式
            if (renderMode == 4 && psr.mesh != null)
            {
                JSONObject meshRef = new JSONObject(JSONObject.Type.OBJECT);
                
                // 使用ResoureMap的Mesh导出逻辑（如果可用）
                if (resoureMap != null)
                {
                    MeshFile meshFile = resoureMap.GetMeshFile(psr.mesh, psr);
                    meshRef.AddField("_$uuid", meshFile.uuid);
                }
                else
                {
                    // 回退到简单的路径处理
                    string meshPath = AssetDatabase.GetAssetPath(psr.mesh.GetInstanceID());
                    string lmPath = GameObjectUitls.cleanIllegalChar(meshPath.Split('.')[0], false) + "-" + 
                                   GameObjectUitls.cleanIllegalChar(psr.mesh.name, true) + ".lm";
                    meshRef.AddField("_$uuid", lmPath);
                }
                meshRef.AddField("_$type", "Mesh");
                comp.AddField("mesh", meshRef);
            }

            // sortingFudge
            if (psr.sortingFudge != 0)
                comp.AddField("sortingFudge", psr.sortingFudge);
        }

        /// <summary>
        /// 导出粒子系统属性
        /// </summary>
        private static void ExportParticleSystemProperties(ParticleSystem ps, JSONObject particleSystem)
        {
            var main = ps.main;

            // 注意: 不导出 _isPlaying，这是运行时状态，不是配置

            // duration
            if (main.duration != 5)
                particleSystem.AddField("duration", main.duration);

            // looping
            if (!main.loop)
                particleSystem.AddField("looping", main.loop);

            // playOnAwake
            if (!main.playOnAwake)
                particleSystem.AddField("playOnAwake", main.playOnAwake);

            // startDelayType & startDelay
            ExportStartDelay(main, particleSystem);

            // startLifetime
            ExportStartLifetime(main, particleSystem);

            // startSpeed
            ExportStartSpeed(main, particleSystem);

            // startSize
            ExportStartSize(main, particleSystem);

            // startRotation
            ExportStartRotation(main, particleSystem);

            // startColor
            ExportStartColor(main, particleSystem);

            // gravityModifier
            if (main.gravityModifier.constant != 0)
                particleSystem.AddField("gravityModifier", main.gravityModifier.constant);

            // simulationSpace: 0=world, 1=local
            int simSpace = main.simulationSpace == ParticleSystemSimulationSpace.World ? 0 : 1;
            if (simSpace != 1)
                particleSystem.AddField("simulationSpace", simSpace);

            // simulationSpeed
            if (main.simulationSpeed != 1)
                particleSystem.AddField("simulationSpeed", main.simulationSpeed);

            // scaleMode: 0=Hierarchy, 1=Local, 2=Shape
            int scaleMode = 1;
            switch (main.scalingMode)
            {
                case ParticleSystemScalingMode.Hierarchy: scaleMode = 0; break;
                case ParticleSystemScalingMode.Local: scaleMode = 1; break;
                case ParticleSystemScalingMode.Shape: scaleMode = 2; break;
            }
            if (scaleMode != 1)
                particleSystem.AddField("scaleMode", scaleMode);

            // maxParticles
            particleSystem.AddField("maxParticles", main.maxParticles);

            // autoRandomSeed
            if (!ps.useAutoRandomSeed)
                particleSystem.AddField("autoRandomSeed", ps.useAutoRandomSeed);

            // randomSeed
            JSONObject randomSeed = new JSONObject(JSONObject.Type.OBJECT);
            randomSeed.AddField("_$type", "Uint32Array");
            JSONObject seedValue = new JSONObject(JSONObject.Type.ARRAY);
            seedValue.Add((int)ps.randomSeed);
            randomSeed.AddField("value", seedValue);
            particleSystem.AddField("randomSeed", randomSeed);

            // emission
            ExportEmission(ps.emission, particleSystem);

            // shape
            ExportShape(ps.shape, particleSystem);

            // velocityOverLifetime
            if (ps.velocityOverLifetime.enabled)
                ExportVelocityOverLifetime(ps.velocityOverLifetime, particleSystem);

            // colorOverLifetime
            if (ps.colorOverLifetime.enabled)
                ExportColorOverLifetime(ps.colorOverLifetime, particleSystem);

            // sizeOverLifetime
            if (ps.sizeOverLifetime.enabled)
                ExportSizeOverLifetime(ps.sizeOverLifetime, particleSystem);

            // rotationOverLifetime
            if (ps.rotationOverLifetime.enabled)
                ExportRotationOverLifetime(ps.rotationOverLifetime, particleSystem);

            // textureSheetAnimation
            if (ps.textureSheetAnimation.enabled)
                ExportTextureSheetAnimation(ps.textureSheetAnimation, particleSystem);
        }

        #region Main Module Exports

        private static void ExportStartDelay(ParticleSystem.MainModule main, JSONObject particleSystem)
        {
            int delayType = main.startDelay.mode == ParticleSystemCurveMode.Constant ? 0 : 1;
            if (delayType != 0)
                particleSystem.AddField("startDelayType", delayType);

            if (delayType == 0)
            {
                if (main.startDelay.constant != 0)
                    particleSystem.AddField("startDelay", main.startDelay.constant);
            }
            else
            {
                if (main.startDelay.constantMin != 0)
                    particleSystem.AddField("startDelayMin", main.startDelay.constantMin);
                if (main.startDelay.constantMax != 0)
                    particleSystem.AddField("startDelayMax", main.startDelay.constantMax);
            }
        }

        private static void ExportStartLifetime(ParticleSystem.MainModule main, JSONObject particleSystem)
        {
            // 0=Constant, 2=TwoConstants (新版只支持这两种)
            int lifetimeType = main.startLifetime.mode == ParticleSystemCurveMode.TwoConstants ? 2 : 0;
            if (lifetimeType != 0)
                particleSystem.AddField("startLifetimeType", lifetimeType);

            // 始终导出 startLifetimeConstant (使用 constantMax 作为默认值)
            float lifetimeConstant = lifetimeType == 2 ? main.startLifetime.constantMax : main.startLifetime.constant;
            if (lifetimeConstant != 5)
                particleSystem.AddField("startLifetimeConstant", lifetimeConstant);

            if (lifetimeType == 2)
            {
                if (main.startLifetime.constantMin != 0)
                    particleSystem.AddField("startLifetimeConstantMin", main.startLifetime.constantMin);
                if (main.startLifetime.constantMax != 5)
                    particleSystem.AddField("startLifetimeConstantMax", main.startLifetime.constantMax);
            }
        }

        private static void ExportStartSpeed(ParticleSystem.MainModule main, JSONObject particleSystem)
        {
            int speedType = main.startSpeed.mode == ParticleSystemCurveMode.TwoConstants ? 2 : 0;
            if (speedType != 0)
                particleSystem.AddField("startSpeedType", speedType);

            // 始终导出 startSpeedConstant (使用 constantMax 作为默认值)
            float speedConstant = speedType == 2 ? main.startSpeed.constantMax : main.startSpeed.constant;
            if (speedConstant != 5)
                particleSystem.AddField("startSpeedConstant", speedConstant);

            if (speedType == 2)
            {
                if (main.startSpeed.constantMin != 0)
                    particleSystem.AddField("startSpeedConstantMin", main.startSpeed.constantMin);
                if (main.startSpeed.constantMax != 5)
                    particleSystem.AddField("startSpeedConstantMax", main.startSpeed.constantMax);
            }
        }

        private static void ExportStartSize(ParticleSystem.MainModule main, JSONObject particleSystem)
        {
            int sizeType = main.startSize.mode == ParticleSystemCurveMode.TwoConstants ? 2 : 0;
            if (sizeType != 0)
                particleSystem.AddField("startSizeType", sizeType);

            // threeDStartSize
            if (main.startSize3D)
                particleSystem.AddField("threeDStartSize", true);

            if (!main.startSize3D)
            {
                // 非3D模式
                if (sizeType == 0)
                {
                    if (main.startSize.constant != 1)
                        particleSystem.AddField("startSizeConstant", main.startSize.constant);
                }
                else
                {
                    if (main.startSize.constantMin != 0)
                        particleSystem.AddField("startSizeConstantMin", main.startSize.constantMin);
                    if (main.startSize.constantMax != 1)
                        particleSystem.AddField("startSizeConstantMax", main.startSize.constantMax);
                }
            }

            // startSizeConstantSeparate (始终导出)
            JSONObject sizeSeparate = CreateVector3Object(
                main.startSizeX.constant,
                main.startSizeY.constant,
                main.startSizeZ.constant
            );
            particleSystem.AddField("startSizeConstantSeparate", sizeSeparate);

            // startSizeConstantMinSeparate
            JSONObject sizeMinSeparate = CreateVector3Object(
                main.startSizeX.constantMin,
                main.startSizeY.constantMin,
                main.startSizeZ.constantMin
            );
            particleSystem.AddField("startSizeConstantMinSeparate", sizeMinSeparate);

            // startSizeConstantMaxSeparate
            JSONObject sizeMaxSeparate = CreateVector3Object(
                main.startSizeX.constantMax,
                main.startSizeY.constantMax,
                main.startSizeZ.constantMax
            );
            particleSystem.AddField("startSizeConstantMaxSeparate", sizeMaxSeparate);
        }

        private static void ExportStartRotation(ParticleSystem.MainModule main, JSONObject particleSystem)
        {
            int rotationType = main.startRotation.mode == ParticleSystemCurveMode.TwoConstants ? 2 : 0;
            if (rotationType != 0)
                particleSystem.AddField("startRotationType", rotationType);

            // threeDStartRotation
            if (main.startRotation3D)
                particleSystem.AddField("threeDStartRotation", true);

            // 始终导出 startRotationConstant (使用 constantMax 作为默认值)
            float rotationConstant = rotationType == 2 ? main.startRotation.constantMax : main.startRotation.constant;
            if (rotationConstant != 0)
                particleSystem.AddField("startRotationConstant", rotationConstant);

            if (!main.startRotation3D && rotationType == 2)
            {
                if (main.startRotation.constantMin != 0)
                    particleSystem.AddField("startRotationConstantMin", main.startRotation.constantMin);
                if (main.startRotation.constantMax != 0)
                    particleSystem.AddField("startRotationConstantMax", main.startRotation.constantMax);
            }

            // startRotationConstantSeparate (始终导出) - 应用旋转坐标转换
            JSONObject rotSeparate = CreateVector3ObjectForRotation(
                main.startRotationX.constant,
                main.startRotationY.constant,
                main.startRotationZ.constant
            );
            particleSystem.AddField("startRotationConstantSeparate", rotSeparate);

            // startRotationConstantSeparate2 (用于编辑器显示) - 转换为角度值
            float rotZ = rotationType == 2 ? main.startRotationZ.constantMax : main.startRotationZ.constant;
            float rotZDegrees = -rotZ * Mathf.Rad2Deg;
            JSONObject rotSeparate2 = new JSONObject(JSONObject.Type.OBJECT);
            rotSeparate2.AddField("_$type", "Vector3");
            if (rotZDegrees != 0)
                rotSeparate2.AddField("z", rotZDegrees);
            particleSystem.AddField("startRotationConstantSeparate2", rotSeparate2);

            // startRotationConstantMinSeparate
            JSONObject rotMinSeparate = CreateVector3ObjectForRotation(
                main.startRotationX.constantMin,
                main.startRotationY.constantMin,
                main.startRotationZ.constantMin
            );
            particleSystem.AddField("startRotationConstantMinSeparate", rotMinSeparate);

            // startRotationConstantMaxSeparate
            JSONObject rotMaxSeparate = CreateVector3ObjectForRotation(
                main.startRotationX.constantMax,
                main.startRotationY.constantMax,
                main.startRotationZ.constantMax
            );
            particleSystem.AddField("startRotationConstantMaxSeparate", rotMaxSeparate);

            // randomizeRotationDirection
            if (main.flipRotation != 0)
                particleSystem.AddField("randomizeRotationDirection", main.flipRotation);
        }

        private static void ExportStartColor(ParticleSystem.MainModule main, JSONObject particleSystem)
        {
            // startColorType: 0=Color, 2=TwoColors
            // Unity 模式: Color, Gradient, TwoColors, TwoGradients, RandomColor
            // LayaAir 只支持 Color(0) 和 TwoColors(2)
            // TwoColors 和 TwoGradients 都映射到 type=2
            // Gradient 和 RandomColor 映射到 type=0
            int colorType = 0;
            switch (main.startColor.mode)
            {
                case ParticleSystemGradientMode.TwoColors:
                case ParticleSystemGradientMode.TwoGradients:
                    colorType = 2;
                    break;
                default:
                    colorType = 0;
                    break;
            }
            
            if (colorType != 0)
                particleSystem.AddField("startColorType", colorType);

            // 根据模式获取颜色值
            Color c, cMin, cMax;
            
            switch (main.startColor.mode)
            {
                case ParticleSystemGradientMode.Color:
                    // 单色模式
                    c = main.startColor.color;
                    cMin = c;
                    cMax = c;
                    break;
                    
                case ParticleSystemGradientMode.Gradient:
                    // 渐变模式 - 使用渐变的起始颜色
                    if (main.startColor.gradient != null)
                    {
                        c = main.startColor.gradient.Evaluate(0);
                    }
                    else
                    {
                        c = main.startColor.color;
                    }
                    cMin = c;
                    cMax = c;
                    break;
                    
                case ParticleSystemGradientMode.TwoColors:
                    // 两个颜色模式
                    c = main.startColor.color;
                    cMin = main.startColor.colorMin;
                    cMax = main.startColor.colorMax;
                    break;
                    
                case ParticleSystemGradientMode.TwoGradients:
                    // 两个渐变模式 - 使用渐变的起始颜色作为 Min/Max
                    c = main.startColor.color;
                    if (main.startColor.gradientMin != null)
                    {
                        cMin = main.startColor.gradientMin.Evaluate(0);
                    }
                    else
                    {
                        cMin = main.startColor.colorMin;
                    }
                    if (main.startColor.gradientMax != null)
                    {
                        cMax = main.startColor.gradientMax.Evaluate(0);
                    }
                    else
                    {
                        cMax = main.startColor.colorMax;
                    }
                    break;
                    
                case ParticleSystemGradientMode.RandomColor:
                    // 随机颜色模式 - 使用渐变的起始颜色
                    if (main.startColor.gradient != null)
                    {
                        c = main.startColor.gradient.Evaluate(0);
                    }
                    else
                    {
                        c = main.startColor.color;
                    }
                    cMin = c;
                    cMax = c;
                    break;
                    
                default:
                    c = main.startColor.color;
                    cMin = main.startColor.colorMin;
                    cMax = main.startColor.colorMax;
                    break;
            }

            // startColorConstant
            JSONObject colorConstant = CreateVector4Object(c.r, c.g, c.b, c.a);
            particleSystem.AddField("startColorConstant", colorConstant);

            // startColorConstantMin
            JSONObject colorMin = CreateVector4Object(cMin.r, cMin.g, cMin.b, cMin.a);
            particleSystem.AddField("startColorConstantMin", colorMin);

            // startColorConstantMax
            JSONObject colorMax = CreateVector4Object(cMax.r, cMax.g, cMax.b, cMax.a);
            particleSystem.AddField("startColorConstantMax", colorMax);
        }

        #endregion

        #region Module Exports

        private static void ExportEmission(ParticleSystem.EmissionModule emission, JSONObject particleSystem)
        {
            JSONObject emissionObj = new JSONObject(JSONObject.Type.OBJECT);

            // 注意: 不导出 enable 字段，标准格式中没有这个字段

            if (emission.rateOverTime.constant != 10)
                emissionObj.AddField("emissionRate", emission.rateOverTime.constant);

            if (emission.rateOverDistance.constant != 0)
                emissionObj.AddField("emissionRateOverDistance", emission.rateOverDistance.constant);

            // bursts
            if (emission.burstCount > 0)
            {
                JSONObject bursts = new JSONObject(JSONObject.Type.ARRAY);
                ParticleSystem.Burst[] burstArray = new ParticleSystem.Burst[emission.burstCount];
                emission.GetBursts(burstArray);

                foreach (var burst in burstArray)
                {
                    JSONObject burstObj = new JSONObject(JSONObject.Type.OBJECT);
                    burstObj.AddField("_$type", "Burst");
                    // 只导出非默认值: _time 默认0, _minCount 默认0, _maxCount 需要导出
                    if (burst.time != 0)
                        burstObj.AddField("_time", burst.time);
                    if (burst.minCount != 0)
                        burstObj.AddField("_minCount", burst.minCount);
                    burstObj.AddField("_maxCount", burst.maxCount);
                    bursts.Add(burstObj);
                }
                emissionObj.AddField("_bursts", bursts);
            }

            particleSystem.AddField("emission", emissionObj);
        }

        /// <summary>
        /// 导出粒子发射形状
        /// 根据 Particle.ts 中的形状类型定义:
        /// - SphereShape: radius, emitFromShell, randomDirection
        /// - HemisphereShape: radius, emitFromShell, randomDirection
        /// - ConeShape: angleDEG, radius, length, emitType, randomDirection
        /// - BoxShape: x, y, z, randomDirection
        /// - CircleShape: radius, emitFromEdge, arcDEG, randomDirection
        /// </summary>
        private static void ExportShape(ParticleSystem.ShapeModule shape, JSONObject particleSystem)
        {
            if (!shape.enabled)
            {
                // shape 模块未启用时不导出
                return;
            }

            JSONObject shapeObj = new JSONObject(JSONObject.Type.OBJECT);

            // 根据形状类型设置 _$type
            string shapeType = GetShapeTypeName(shape.shapeType);
            shapeObj.AddField("_$type", shapeType);
            // 注意: 不导出 enable 字段，标准格式中没有这个字段

            switch (shape.shapeType)
            {
                case ParticleSystemShapeType.Sphere:
                case ParticleSystemShapeType.SphereShell:
                    // SphereShape: radius(default=1), emitFromShell(default=false), randomDirection(default=0)
                    if (shape.radius != 1)
                        shapeObj.AddField("radius", shape.radius);
                    if (shape.radiusThickness == 0 || shape.shapeType == ParticleSystemShapeType.SphereShell)
                        shapeObj.AddField("emitFromShell", true);
                    if (shape.randomDirectionAmount != 0)
                        shapeObj.AddField("randomDirection", shape.randomDirectionAmount > 0 ? 1 : 0);
                    break;

                case ParticleSystemShapeType.Hemisphere:
                case ParticleSystemShapeType.HemisphereShell:
                    // HemisphereShape: radius(default=1), emitFromShell(default=false), randomDirection(default=0)
                    if (shape.radius != 1)
                        shapeObj.AddField("radius", shape.radius);
                    if (shape.radiusThickness == 0 || shape.shapeType == ParticleSystemShapeType.HemisphereShell)
                        shapeObj.AddField("emitFromShell", true);
                    if (shape.randomDirectionAmount != 0)
                        shapeObj.AddField("randomDirection", shape.randomDirectionAmount > 0 ? 1 : 0);
                    break;

                case ParticleSystemShapeType.Cone:
                case ParticleSystemShapeType.ConeVolume:
                case ParticleSystemShapeType.ConeVolumeShell:
                    // ConeShape: angleDEG(default=25), radius(default=1), length(default=5), emitType(default=0), randomDirection(default=0)
                    if (shape.angle != 25)
                        shapeObj.AddField("angleDEG", shape.angle);
                    if (shape.radius != 1)
                        shapeObj.AddField("radius", shape.radius);
                    if (shape.length != 5)
                        shapeObj.AddField("length", shape.length);
                    // emitType: 0=Base, 1=BaseShell, 2=Volume, 3=VolumeShell
                    int emitType = GetConeEmitType(shape);
                    if (emitType != 0)
                        shapeObj.AddField("emitType", emitType);
                    if (shape.randomDirectionAmount != 0)
                        shapeObj.AddField("randomDirection", shape.randomDirectionAmount > 0 ? 1 : 0);
                    break;

                case ParticleSystemShapeType.Box:
                case ParticleSystemShapeType.BoxShell:
                case ParticleSystemShapeType.BoxEdge:
                    // BoxShape: x(default=1), y(default=1), z(default=1), randomDirection(default=0)
                    if (shape.scale.x != 1)
                        shapeObj.AddField("x", shape.scale.x);
                    if (shape.scale.y != 1)
                        shapeObj.AddField("y", shape.scale.y);
                    if (shape.scale.z != 1)
                        shapeObj.AddField("z", shape.scale.z);
                    if (shape.randomDirectionAmount != 0)
                        shapeObj.AddField("randomDirection", shape.randomDirectionAmount > 0 ? 1 : 0);
                    break;

                case ParticleSystemShapeType.Circle:
                case ParticleSystemShapeType.CircleEdge:
                    // CircleShape: radius(default=1), emitFromEdge(default=false), arcDEG(default=360), randomDirection(default=0)
                    if (shape.radius != 1)
                        shapeObj.AddField("radius", shape.radius);
                    if (shape.radiusThickness == 0 || shape.shapeType == ParticleSystemShapeType.CircleEdge)
                        shapeObj.AddField("emitFromEdge", true);
                    if (shape.arc != 360)
                        shapeObj.AddField("arcDEG", shape.arc);
                    if (shape.randomDirectionAmount != 0)
                        shapeObj.AddField("randomDirection", shape.randomDirectionAmount > 0 ? 1 : 0);
                    break;

                default:
                    // 不支持的形状类型，使用默认球形
                    Debug.LogWarning($"LayaAir3D: Unsupported particle shape type '{shape.shapeType}', using SphereShape instead.");
                    break;
            }

            particleSystem.AddField("shape", shapeObj);
        }

        /// <summary>
        /// 获取锥形发射类型
        /// </summary>
        private static int GetConeEmitType(ParticleSystem.ShapeModule shape)
        {
            // emitType: 0=Base, 1=BaseShell, 2=Volume, 3=VolumeShell
            switch (shape.shapeType)
            {
                case ParticleSystemShapeType.Cone:
                    return shape.radiusThickness == 0 ? 1 : 0;
                case ParticleSystemShapeType.ConeVolume:
                    return shape.radiusThickness == 0 ? 3 : 2;
                case ParticleSystemShapeType.ConeVolumeShell:
                    return 3;
                default:
                    return 0;
            }
        }

        /// <summary>
        /// 获取LayaAir形状类型名称
        /// </summary>
        private static string GetShapeTypeName(ParticleSystemShapeType shapeType)
        {
            switch (shapeType)
            {
                case ParticleSystemShapeType.Sphere:
                case ParticleSystemShapeType.SphereShell:
                    return "SphereShape";
                case ParticleSystemShapeType.Hemisphere:
                case ParticleSystemShapeType.HemisphereShell:
                    return "HemisphereShape";
                case ParticleSystemShapeType.Cone:
                case ParticleSystemShapeType.ConeVolume:
                case ParticleSystemShapeType.ConeVolumeShell:
                    return "ConeShape";
                case ParticleSystemShapeType.Box:
                case ParticleSystemShapeType.BoxShell:
                case ParticleSystemShapeType.BoxEdge:
                    return "BoxShape";
                case ParticleSystemShapeType.Circle:
                case ParticleSystemShapeType.CircleEdge:
                    return "CircleShape";
                default:
                    return "SphereShape";
            }
        }

        private static void ExportVelocityOverLifetime(ParticleSystem.VelocityOverLifetimeModule vol, JSONObject particleSystem)
        {
            JSONObject volObj = new JSONObject(JSONObject.Type.OBJECT);
            volObj.AddField("enable", true);

            // space: 0=local, 1=world
            int space = vol.space == ParticleSystemSimulationSpace.World ? 1 : 0;
            volObj.AddField("space", space);

            // _velocity
            JSONObject velocity = new JSONObject(JSONObject.Type.OBJECT);

            // _type: 0=Constant, 1=Curve, 2=TwoConstants, 3=TwoCurves
            int velType = GetCurveType(vol.x.mode);
            velocity.AddField("_type", velType);

            if (velType == 0)
            {
                // constant - 应用坐标系转换
                JSONObject constant = CreateVector3ObjectWithCoordConvert(
                    vol.x.constant,
                    vol.y.constant,
                    vol.z.constant
                );
                velocity.AddField("_constant", constant);
            }
            else if (velType == 2)
            {
                // constantMin/Max - 应用坐标系转换
                JSONObject constantMin = CreateVector3ObjectWithCoordConvert(
                    vol.x.constantMin,
                    vol.y.constantMin,
                    vol.z.constantMin
                );
                velocity.AddField("_constantMin", constantMin);

                JSONObject constantMax = CreateVector3ObjectWithCoordConvert(
                    vol.x.constantMax,
                    vol.y.constantMax,
                    vol.z.constantMax
                );
                velocity.AddField("_constantMax", constantMax);
            }
            else if (velType == 1 || velType == 3)
            {
                // Curve模式 - 导出GradientDataNumber
                ExportGradientDataNumber(vol.x, velocity, "_gradientX", -vol.x.curveMultiplier);
                ExportGradientDataNumber(vol.y, velocity, "_gradientY", vol.y.curveMultiplier);
                ExportGradientDataNumber(vol.z, velocity, "_gradientZ", vol.z.curveMultiplier);

                if (velType == 3)
                {
                    ExportGradientDataNumberMinMax(vol.x, velocity, "_gradientXMin", "_gradientXMax", -vol.x.curveMultiplier);
                    ExportGradientDataNumberMinMax(vol.y, velocity, "_gradientYMin", "_gradientYMax", vol.y.curveMultiplier);
                    ExportGradientDataNumberMinMax(vol.z, velocity, "_gradientZMin", "_gradientZMax", vol.z.curveMultiplier);
                }
            }

            volObj.AddField("_velocity", velocity);
            particleSystem.AddField("velocityOverLifetime", volObj);
        }

        private static void ExportColorOverLifetime(ParticleSystem.ColorOverLifetimeModule col, JSONObject particleSystem)
        {
            JSONObject colObj = new JSONObject(JSONObject.Type.OBJECT);
            colObj.AddField("enable", true);

            JSONObject color = new JSONObject(JSONObject.Type.OBJECT);

            // _type: 0=Constant, 1=Gradient, 2=TwoColors, 3=TwoGradients
            int colorType = 0;
            switch (col.color.mode)
            {
                case ParticleSystemGradientMode.Color: colorType = 0; break;
                case ParticleSystemGradientMode.Gradient: colorType = 1; break;
                case ParticleSystemGradientMode.TwoColors: colorType = 2; break;
                case ParticleSystemGradientMode.TwoGradients: colorType = 3; break;
            }
            color.AddField("_type", colorType);

            if (colorType == 0)
            {
                Color c = col.color.color;
                color.AddField("_constant", CreateVector4Object(c.r, c.g, c.b, c.a));
            }
            else if (colorType == 1)
            {
                ExportGradient(col.color.gradient, color, "_gradient");
            }
            else if (colorType == 2)
            {
                Color cMin = col.color.colorMin;
                Color cMax = col.color.colorMax;
                color.AddField("_constantMin", CreateVector4Object(cMin.r, cMin.g, cMin.b, cMin.a));
                color.AddField("_constantMax", CreateVector4Object(cMax.r, cMax.g, cMax.b, cMax.a));
            }
            else if (colorType == 3)
            {
                ExportGradient(col.color.gradientMin, color, "_gradientMin");
                ExportGradient(col.color.gradientMax, color, "_gradientMax");
            }

            colObj.AddField("_color", color);
            particleSystem.AddField("colorOverLifetime", colObj);
        }

        private static void ExportSizeOverLifetime(ParticleSystem.SizeOverLifetimeModule sol, JSONObject particleSystem)
        {
            JSONObject solObj = new JSONObject(JSONObject.Type.OBJECT);
            solObj.AddField("_$type", "SizeOverLifetime");
            solObj.AddField("enable", true);

            JSONObject size = new JSONObject(JSONObject.Type.OBJECT);
            size.AddField("_$type", "GradientSize");

            // _separateAxes
            if (sol.separateAxes)
                size.AddField("_separateAxes", true);

            // _type: 0=Curve, 1=TwoConstants, 2=TwoCurves
            // 注意: 标准格式中 Curve 模式不导出 _type 字段
            int sizeType = 0;
            switch (sol.size.mode)
            {
                case ParticleSystemCurveMode.Curve: sizeType = 0; break;
                case ParticleSystemCurveMode.TwoConstants: sizeType = 1; break;
                case ParticleSystemCurveMode.TwoCurves: sizeType = 2; break;
            }
            // 只有非 Curve 模式才导出 _type
            if (sizeType != 0)
                size.AddField("_type", sizeType);

            if (sizeType == 1)
            {
                // TwoConstants
                if (!sol.separateAxes)
                {
                    size.AddField("_constantMin", sol.size.constantMin);
                    size.AddField("_constantMax", sol.size.constantMax);
                }
                else
                {
                    size.AddField("_constantMinSeparate", CreateVector3Object(
                        sol.x.constantMin, sol.y.constantMin, sol.z.constantMin));
                    size.AddField("_constantMaxSeparate", CreateVector3Object(
                        sol.x.constantMax, sol.y.constantMax, sol.z.constantMax));
                }
            }
            else
            {
                // Curve模式
                if (!sol.separateAxes)
                {
                    ExportGradientDataNumberSimple(sol.size, size, "_gradient", sol.size.curveMultiplier);
                    if (sizeType == 2)
                    {
                        ExportGradientDataNumberMinMaxSimple(sol.size, size, "_gradientMin", "_gradientMax", sol.size.curveMultiplier);
                    }
                }
                else
                {
                    ExportGradientDataNumberSimple(sol.x, size, "_gradientX", sol.x.curveMultiplier);
                    ExportGradientDataNumberSimple(sol.y, size, "_gradientY", sol.y.curveMultiplier);
                    ExportGradientDataNumberSimple(sol.z, size, "_gradientZ", sol.z.curveMultiplier);
                    if (sizeType == 2)
                    {
                        ExportGradientDataNumberMinMaxSimple(sol.x, size, "_gradientXMin", "_gradientXMax", sol.x.curveMultiplier);
                        ExportGradientDataNumberMinMaxSimple(sol.y, size, "_gradientYMin", "_gradientYMax", sol.y.curveMultiplier);
                        ExportGradientDataNumberMinMaxSimple(sol.z, size, "_gradientZMin", "_gradientZMax", sol.z.curveMultiplier);
                    }
                }
            }

            solObj.AddField("_size", size);
            particleSystem.AddField("sizeOverLifetime", solObj);
        }

        private static void ExportRotationOverLifetime(ParticleSystem.RotationOverLifetimeModule rol, JSONObject particleSystem)
        {
            JSONObject rolObj = new JSONObject(JSONObject.Type.OBJECT);
            rolObj.AddField("_$type", "RotationOverLifetime");
            rolObj.AddField("enable", true);

            JSONObject angularVelocity = new JSONObject(JSONObject.Type.OBJECT);
            angularVelocity.AddField("_$type", "GradientAngularVelocity");

            // _separateAxes
            if (rol.separateAxes)
                angularVelocity.AddField("_separateAxes", true);

            // _type: 0=Constant, 1=Curve, 2=TwoConstants, 3=TwoCurves
            // 注意: 标准格式中 Constant 模式不导出 _type 字段
            int rotType = GetCurveType(rol.z.mode);
            // 只有非 Constant 模式才导出 _type
            if (rotType != 0)
                angularVelocity.AddField("_type", rotType);

            if (rotType == 0)
            {
                if (!rol.separateAxes)
                {
                    angularVelocity.AddField("_constant", rol.z.constant);
                    // 标准格式需要导出 _constantMin 和 _constantMax，默认值为 0
                    angularVelocity.AddField("_constantMin", 0);
                    angularVelocity.AddField("_constantMax", 0);
                }
                else
                {
                    angularVelocity.AddField("_constantSeparate", CreateVector3Object(
                        rol.x.constant, -rol.y.constant, -rol.z.constant));
                }
            }
            else if (rotType == 2)
            {
                if (!rol.separateAxes)
                {
                    angularVelocity.AddField("_constantMin", rol.z.constantMin);
                    angularVelocity.AddField("_constantMax", rol.z.constantMax);
                }
                else
                {
                    angularVelocity.AddField("_constantMinSeparate", CreateVector3Object(
                        rol.x.constantMin, -rol.y.constantMin, -rol.z.constantMin));
                    angularVelocity.AddField("_constantMaxSeparate", CreateVector3Object(
                        rol.x.constantMax, -rol.y.constantMax, -rol.z.constantMax));
                }
            }
            else
            {
                // Curve模式
                if (!rol.separateAxes)
                {
                    ExportGradientDataNumber(rol.z, angularVelocity, "_gradient", rol.z.curveMultiplier);
                    if (rotType == 3)
                    {
                        ExportGradientDataNumberMinMax(rol.z, angularVelocity, "_gradientMin", "_gradientMax", rol.z.curveMultiplier);
                    }
                }
                else
                {
                    ExportGradientDataNumber(rol.x, angularVelocity, "_gradientX", rol.x.curveMultiplier);
                    ExportGradientDataNumber(rol.y, angularVelocity, "_gradientY", -rol.y.curveMultiplier);
                    ExportGradientDataNumber(rol.z, angularVelocity, "_gradientZ", -rol.z.curveMultiplier);
                    if (rotType == 3)
                    {
                        ExportGradientDataNumberMinMax(rol.x, angularVelocity, "_gradientXMin", "_gradientXMax", rol.x.curveMultiplier);
                        ExportGradientDataNumberMinMax(rol.y, angularVelocity, "_gradientYMin", "_gradientYMax", -rol.y.curveMultiplier);
                        ExportGradientDataNumberMinMax(rol.z, angularVelocity, "_gradientZMin", "_gradientZMax", -rol.z.curveMultiplier);
                    }
                }
            }

            rolObj.AddField("_angularVelocity", angularVelocity);
            particleSystem.AddField("rotationOverLifetime", rolObj);
        }

        private static void ExportTextureSheetAnimation(ParticleSystem.TextureSheetAnimationModule tsa, JSONObject particleSystem)
        {
            JSONObject tsaObj = new JSONObject(JSONObject.Type.OBJECT);
            tsaObj.AddField("enable", true);

            // tiles
            JSONObject tiles = CreateVector2Object(tsa.numTilesX, tsa.numTilesY);
            tsaObj.AddField("tiles", tiles);

            // type: 0=WholeSheet, 1=SingleRow
            int animType = tsa.animation == ParticleSystemAnimationType.SingleRow ? 1 : 0;
            if (animType != 0)
                tsaObj.AddField("type", animType);

            // rowIndex
            if (animType == 1 && tsa.rowIndex != 0)
                tsaObj.AddField("rowIndex", tsa.rowIndex);

            // _frame
            float frameCount = animType == 1 ? tsa.numTilesX : tsa.numTilesX * tsa.numTilesY;
            JSONObject frame = new JSONObject(JSONObject.Type.OBJECT);
            int frameType = GetCurveType(tsa.frameOverTime.mode);
            frame.AddField("_type", frameType);

            if (frameType == 0)
            {
                frame.AddField("_constant", tsa.frameOverTime.constant * frameCount);
            }
            else if (frameType == 2)
            {
                frame.AddField("_constantMin", tsa.frameOverTime.constantMin * frameCount);
                frame.AddField("_constantMax", tsa.frameOverTime.constantMax * frameCount);
            }
            else
            {
                float maxCurve = frameCount * tsa.frameOverTime.curveMultiplier;
                ExportGradientDataInt(tsa.frameOverTime, frame, "_overTime", maxCurve);
                if (frameType == 3)
                {
                    ExportGradientDataIntMinMax(tsa.frameOverTime, frame, "_overTimeMin", "_overTimeMax", maxCurve);
                }
            }
            tsaObj.AddField("_frame", frame);

            // _startFrame
            JSONObject startFrame = new JSONObject(JSONObject.Type.OBJECT);
            int startFrameType = tsa.startFrame.mode == ParticleSystemCurveMode.TwoConstants ? 1 : 0;
            startFrame.AddField("_type", startFrameType);
            if (startFrameType == 0)
            {
                startFrame.AddField("_constant", tsa.startFrame.constant * frameCount);
            }
            else
            {
                startFrame.AddField("_constantMin", tsa.startFrame.constantMin * frameCount);
                startFrame.AddField("_constantMax", tsa.startFrame.constantMax * frameCount);
            }
            tsaObj.AddField("_startFrame", startFrame);

            // cycles
            if (tsa.cycleCount != 1)
                tsaObj.AddField("cycles", tsa.cycleCount);

            particleSystem.AddField("textureSheetAnimation", tsaObj);
        }

        #endregion

        #region Helper Methods

        private static int GetCurveType(ParticleSystemCurveMode mode)
        {
            switch (mode)
            {
                case ParticleSystemCurveMode.Constant: return 0;
                case ParticleSystemCurveMode.Curve: return 1;
                case ParticleSystemCurveMode.TwoConstants: return 2;
                case ParticleSystemCurveMode.TwoCurves: return 3;
                default: return 0;
            }
        }

        private static JSONObject CreateVector2Object(float x, float y)
        {
            JSONObject vec = new JSONObject(JSONObject.Type.OBJECT);
            vec.AddField("_$type", "Vector2");
            vec.AddField("x", x);
            vec.AddField("y", y);
            return vec;
        }

        private static JSONObject CreateVector3Object(float x, float y, float z)
        {
            JSONObject vec = new JSONObject(JSONObject.Type.OBJECT);
            vec.AddField("_$type", "Vector3");
            if (x != 0) vec.AddField("x", x);
            if (y != 0) vec.AddField("y", y);
            if (z != 0) vec.AddField("z", z);
            return vec;
        }

        /// <summary>
        /// 创建Vector3对象，应用Unity到LayaAir的坐标系转换
        /// Unity使用左手坐标系，LayaAir使用右手坐标系
        /// 转换规则: x取反
        /// </summary>
        private static JSONObject CreateVector3ObjectWithCoordConvert(float x, float y, float z)
        {
            return CreateVector3Object(-x, y, z);
        }

        /// <summary>
        /// 创建Vector3对象，用于旋转值的坐标系转换
        /// 旋转转换规则: y和z取反
        /// </summary>
        private static JSONObject CreateVector3ObjectForRotation(float x, float y, float z)
        {
            return CreateVector3Object(x, -y, -z);
        }

        private static JSONObject CreateVector4Object(float x, float y, float z, float w)
        {
            JSONObject vec = new JSONObject(JSONObject.Type.OBJECT);
            vec.AddField("_$type", "Vector4");
            if (x != 0) vec.AddField("x", x);
            if (y != 0) vec.AddField("y", y);
            if (z != 0) vec.AddField("z", z);
            if (w != 0) vec.AddField("w", w);
            return vec;
        }

        /// <summary>
        /// 导出GradientDataNumber曲线数据
        /// 根据 Particle.ts 中 GradientDataNumber 类型定义:
        /// - _elements: Float32Array - 每个关键帧2个值: time, value (最多4个key = 8个float)
        /// - _currentLength: 当前使用的元素数量
        /// - _curveMin/_curveMax: 曲线值范围
        /// </summary>
        private static void ExportGradientDataNumber(ParticleSystem.MinMaxCurve curve, JSONObject parent, string fieldName, float multiplier)
        {
            AnimationCurve animCurve = curve.curve;
            if (animCurve == null || animCurve.length == 0)
            {
                // 创建默认曲线数据
                animCurve = AnimationCurve.Linear(0, 0, 1, 1);
            }

            JSONObject gradientData = new JSONObject(JSONObject.Type.OBJECT);

            // _elements: Float32Array - 每个关键帧2个值: time, value
            // 根据 Particle.ts 的 init 值: [0, 0, 1, 0, 0, 0, 0, 0] 表示最多4个关键帧
            List<float> elements = new List<float>();
            float minValue = float.MaxValue;
            float maxValue = float.MinValue;

            foreach (var key in animCurve.keys)
            {
                float value = key.value * multiplier;
                elements.Add(key.time);
                elements.Add(value);
                minValue = Mathf.Min(minValue, value);
                maxValue = Mathf.Max(maxValue, value);
            }

            // 填充到8个float (4个关键帧)
            while (elements.Count < 8)
                elements.Add(0);

            gradientData.AddField("_elements", CreateFloat32Array(elements));
            gradientData.AddField("_currentLength", animCurve.length * 2);

            // 设置曲线范围
            if (minValue == float.MaxValue) minValue = -1;
            if (maxValue == float.MinValue) maxValue = 1;
            // 确保范围对称或合理
            float absMax = Mathf.Max(Mathf.Abs(minValue), Mathf.Abs(maxValue));
            if (absMax < 1) absMax = 1;
            gradientData.AddField("_curveMin", -absMax);
            gradientData.AddField("_curveMax", absMax);

            parent.AddField(fieldName, gradientData);
        }

        /// <summary>
        /// 导出GradientDataNumber的Min/Max曲线数据
        /// </summary>
        private static void ExportGradientDataNumberMinMax(ParticleSystem.MinMaxCurve curve, JSONObject parent, string minFieldName, string maxFieldName, float multiplier)
        {
            // 导出Min曲线
            AnimationCurve curveMin = curve.curveMin;
            if (curveMin != null && curveMin.length > 0)
            {
                JSONObject gradientDataMin = new JSONObject(JSONObject.Type.OBJECT);
                List<float> elementsMin = new List<float>();
                float minVal = float.MaxValue, maxVal = float.MinValue;

                foreach (var key in curveMin.keys)
                {
                    float value = key.value * multiplier;
                    elementsMin.Add(key.time);
                    elementsMin.Add(value);
                    minVal = Mathf.Min(minVal, value);
                    maxVal = Mathf.Max(maxVal, value);
                }
                while (elementsMin.Count < 8)
                    elementsMin.Add(0);

                gradientDataMin.AddField("_elements", CreateFloat32Array(elementsMin));
                gradientDataMin.AddField("_currentLength", curveMin.length * 2);

                float absMax = Mathf.Max(Mathf.Abs(minVal), Mathf.Abs(maxVal));
                if (absMax < 1) absMax = 1;
                gradientDataMin.AddField("_curveMin", -absMax);
                gradientDataMin.AddField("_curveMax", absMax);

                parent.AddField(minFieldName, gradientDataMin);
            }

            // 导出Max曲线
            AnimationCurve curveMax = curve.curveMax;
            if (curveMax != null && curveMax.length > 0)
            {
                JSONObject gradientDataMax = new JSONObject(JSONObject.Type.OBJECT);
                List<float> elementsMax = new List<float>();
                float minVal = float.MaxValue, maxVal = float.MinValue;

                foreach (var key in curveMax.keys)
                {
                    float value = key.value * multiplier;
                    elementsMax.Add(key.time);
                    elementsMax.Add(value);
                    minVal = Mathf.Min(minVal, value);
                    maxVal = Mathf.Max(maxVal, value);
                }
                while (elementsMax.Count < 8)
                    elementsMax.Add(0);

                gradientDataMax.AddField("_elements", CreateFloat32Array(elementsMax));
                gradientDataMax.AddField("_currentLength", curveMax.length * 2);

                float absMax = Mathf.Max(Mathf.Abs(minVal), Mathf.Abs(maxVal));
                if (absMax < 1) absMax = 1;
                gradientDataMax.AddField("_curveMin", -absMax);
                gradientDataMax.AddField("_curveMax", absMax);

                parent.AddField(maxFieldName, gradientDataMax);
            }
        }

        private static void ExportGradientDataInt(ParticleSystem.MinMaxCurve curve, JSONObject parent, string fieldName, float multiplier)
        {
            ExportGradientDataNumber(curve, parent, fieldName, multiplier);
        }

        private static void ExportGradientDataIntMinMax(ParticleSystem.MinMaxCurve curve, JSONObject parent, string minFieldName, string maxFieldName, float multiplier)
        {
            ExportGradientDataNumberMinMax(curve, parent, minFieldName, maxFieldName, multiplier);
        }

        /// <summary>
        /// 导出GradientDataNumber曲线数据 - 简化版，不包含 _curveMin/_curveMax
        /// 用于 sizeOverLifetime 等模块，标准格式中不需要这些字段
        /// </summary>
        private static void ExportGradientDataNumberSimple(ParticleSystem.MinMaxCurve curve, JSONObject parent, string fieldName, float multiplier)
        {
            AnimationCurve animCurve = curve.curve;
            if (animCurve == null || animCurve.length == 0)
            {
                animCurve = AnimationCurve.Linear(0, 0, 1, 1);
            }

            JSONObject gradientData = new JSONObject(JSONObject.Type.OBJECT);
            gradientData.AddField("_$type", "GradientDataNumber");

            List<float> elements = new List<float>();
            foreach (var key in animCurve.keys)
            {
                float value = key.value * multiplier;
                elements.Add(key.time);
                elements.Add(value);
            }

            while (elements.Count < 8)
                elements.Add(0);

            gradientData.AddField("_elements", CreateFloat32Array(elements));
            gradientData.AddField("_currentLength", animCurve.length * 2);

            parent.AddField(fieldName, gradientData);
        }

        /// <summary>
        /// 导出GradientDataNumber的Min/Max曲线数据 - 简化版
        /// </summary>
        private static void ExportGradientDataNumberMinMaxSimple(ParticleSystem.MinMaxCurve curve, JSONObject parent, string minFieldName, string maxFieldName, float multiplier)
        {
            AnimationCurve curveMin = curve.curveMin;
            if (curveMin != null && curveMin.length > 0)
            {
                JSONObject gradientDataMin = new JSONObject(JSONObject.Type.OBJECT);
                gradientDataMin.AddField("_$type", "GradientDataNumber");
                List<float> elementsMin = new List<float>();

                foreach (var key in curveMin.keys)
                {
                    float value = key.value * multiplier;
                    elementsMin.Add(key.time);
                    elementsMin.Add(value);
                }
                while (elementsMin.Count < 8)
                    elementsMin.Add(0);

                gradientDataMin.AddField("_elements", CreateFloat32Array(elementsMin));
                gradientDataMin.AddField("_currentLength", curveMin.length * 2);

                parent.AddField(minFieldName, gradientDataMin);
            }

            AnimationCurve curveMax = curve.curveMax;
            if (curveMax != null && curveMax.length > 0)
            {
                JSONObject gradientDataMax = new JSONObject(JSONObject.Type.OBJECT);
                gradientDataMax.AddField("_$type", "GradientDataNumber");
                List<float> elementsMax = new List<float>();

                foreach (var key in curveMax.keys)
                {
                    float value = key.value * multiplier;
                    elementsMax.Add(key.time);
                    elementsMax.Add(value);
                }
                while (elementsMax.Count < 8)
                    elementsMax.Add(0);

                gradientDataMax.AddField("_elements", CreateFloat32Array(elementsMax));
                gradientDataMax.AddField("_currentLength", curveMax.length * 2);

                parent.AddField(maxFieldName, gradientDataMax);
            }
        }

        /// <summary>
        /// 导出Gradient颜色渐变
        /// 根据 Particle.ts 中 Gradient 类型定义:
        /// - _alphaElements: Float32Array - 每个alpha key 2个值: time, alpha (最多8个key = 16个float)
        /// - _rgbElements: Float32Array - 每个color key 4个值: time, r, g, b (最多8个key = 32个float)
        /// </summary>
        private static void ExportGradient(Gradient gradient, JSONObject parent, string fieldName)
        {
            if (gradient == null)
                return;

            JSONObject gradientObj = new JSONObject(JSONObject.Type.OBJECT);

            // _mode: 0 = Blend, 1 = Fixed (Unity的GradientMode)
            gradientObj.AddField("_mode", (int)gradient.mode);

            // _alphaElements: Float32Array - 每个alpha key 2个值: time, alpha
            List<float> alphaElements = new List<float>();
            foreach (var key in gradient.alphaKeys)
            {
                alphaElements.Add(key.time);
                alphaElements.Add(key.alpha);
            }
            // 填充到8个key (16个float)
            while (alphaElements.Count < 16)
                alphaElements.Add(0);

            gradientObj.AddField("_alphaElements", CreateFloat32Array(alphaElements));
            gradientObj.AddField("_colorAlphaKeysCount", gradient.alphaKeys.Length);

            // _rgbElements: Float32Array - 每个color key 4个值: time, r, g, b
            List<float> rgbElements = new List<float>();
            foreach (var key in gradient.colorKeys)
            {
                rgbElements.Add(key.time);
                rgbElements.Add(key.color.r);
                rgbElements.Add(key.color.g);
                rgbElements.Add(key.color.b);
            }
            // 填充到8个key (32个float)
            while (rgbElements.Count < 32)
                rgbElements.Add(0);

            gradientObj.AddField("_rgbElements", CreateFloat32Array(rgbElements));
            gradientObj.AddField("_colorRGBKeysCount", gradient.colorKeys.Length);

            parent.AddField(fieldName, gradientObj);
        }

        /// <summary>
        /// 创建Float32Array类型的JSON对象
        /// </summary>
        private static JSONObject CreateFloat32Array(List<float> values)
        {
            JSONObject obj = new JSONObject(JSONObject.Type.OBJECT);
            obj.AddField("_$type", "Float32Array");
            JSONObject valueArray = new JSONObject(JSONObject.Type.ARRAY);
            foreach (var v in values)
                valueArray.Add(v);
            obj.AddField("value", valueArray);
            return obj;
        }

        #endregion
    }
}
