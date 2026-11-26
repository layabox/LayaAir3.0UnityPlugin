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
        /// LayaAir粒子系统顶点数量限制
        /// 在LayaAir中: totalVertexCount = maxParticles * meshVertexCount
        /// 如果 totalVertexCount > 65535，会抛出异常
        /// 此限制仅适用于Mesh渲染模式
        /// </summary>
        private const int MAX_PARTICLE_VERTEX_COUNT = 65535;

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
            
            // [DEBUG] 暂时屏蔽GPU粒子mesh顶点数量检测
            // CheckParticleVertexLimit(ps, psr, gameObject.name, resoureMap);

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
                            sharedMaterials.Add(resoureMap.GetMaterialData(mat, psr));
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

            // [DEBUG] 暂时屏蔽GPU粒子渲染器对齐方式的兼容性检测
            // if (renderMode == 4 && psr.alignment == ParticleSystemRenderSpace.View)
            // {
            //     UnsupportedFeatureCollector.AddWarning(...);
            // }
            // if (renderMode == 0 && psr.alignment == ParticleSystemRenderSpace.Local)
            // {
            //     UnsupportedFeatureCollector.AddWarning(...);
            // }

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

            // pivot - 粒子轴心点偏移（Billboard模式下的对齐偏移）
            // Unity pivot范围: -1到1，(0,0,0)表示中心对齐
            // (-1,-1,0)=左下角, (1,1,0)=右上角, (0,1,0)=顶部中心, 等等
            // 原样导出Unity的值到LayaAir
            Vector3 pivot = psr.pivot;
            JSONObject pivotObj = new JSONObject(JSONObject.Type.OBJECT);
            pivotObj.AddField("_$type", "Vector3");
            pivotObj.AddField("x", pivot.x);
            pivotObj.AddField("y", pivot.y);
            pivotObj.AddField("z", pivot.z);
            comp.AddField("pivot", pivotObj);
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

            // looping - 始终导出（Laya默认为false，不导出则粒子只播放一次不循环）
            particleSystem.AddField("looping", main.loop);

            // playOnAwake - 始终导出（Laya默认为false，不导出则粒子不自动播放）
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
            float gravityModifier = EvaluateMinMaxCurveConstant(main.gravityModifier);
            if (gravityModifier != 0)
                particleSystem.AddField("gravityModifier", gravityModifier);

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

            // [DEBUG] 暂时屏蔽GPU粒子不支持功能的检测，排查CPU粒子问题
            // CheckUnsupportedParticleFeatures(ps, main);
        }

        /// <summary>
        /// 检测并汇报粒子系统中 LayaAir 不支持的功能参数。
        /// 路径信息由 UnsupportedFeatureCollector 自动从 GameObject 层级生成。
        /// </summary>
        private static void CheckUnsupportedParticleFeatures(ParticleSystem ps, ParticleSystem.MainModule main)
        {
            GameObject go = ps.gameObject;

            // ── 1. GravityModifier 非 Constant 类型 ──────────────────────────────
            if (main.gravityModifier.mode != ParticleSystemCurveMode.Constant)
            {
                UnsupportedFeatureCollector.AddWarning(
                    "ParticleSystem GravityModifier（重力修改器非常量类型）",
                    go,
                    $"当前模式: {main.gravityModifier.mode}，LayaAir 仅支持 Constant 类型，将以常量值 {EvaluateMinMaxCurveConstant(main.gravityModifier):F3} 导出"
                );
            }

            // ── 2. StartColor Gradient / RandomColor 类型 ────────────────────────
            var colorMode = main.startColor.mode;
            if (colorMode == ParticleSystemGradientMode.Gradient ||
                colorMode == ParticleSystemGradientMode.RandomColor)
            {
                UnsupportedFeatureCollector.AddWarning(
                    "ParticleSystem StartColor（初始颜色 Gradient/RandomColor 类型）",
                    go,
                    $"当前模式: {colorMode}，LayaAir 仅支持 Color 和 TwoColors 类型，Gradient 将降级为渐变起始帧颜色"
                );
            }

            // ── 3. Emission：rateOverTime 非 Constant 模式 ──────────────────────
            if (ps.emission.enabled &&
                ps.emission.rateOverTime.mode != ParticleSystemCurveMode.Constant)
            {
                UnsupportedFeatureCollector.AddWarning(
                    "ParticleSystem Emission RateOverTime（发射率非常量类型）",
                    go,
                    $"当前模式: {ps.emission.rateOverTime.mode}，LayaAir 仅支持 Constant 模式，将以常量值 {EvaluateMinMaxCurveConstant(ps.emission.rateOverTime):F1} 导出（曲线数据丢失）"
                );
            }

            // ── 4. Emission Burst：Cycles 有限次数 + Probability 概率 ────────────
            if (ps.emission.enabled && ps.emission.burstCount > 0)
            {
                var burstArray = new ParticleSystem.Burst[ps.emission.burstCount];
                ps.emission.GetBursts(burstArray);
                foreach (var burst in burstArray)
                {
                    // cycleCount > 0 表示有限循环次数（0 = 无限，LayaAir 支持）
                    if (burst.cycleCount > 0)
                    {
                        UnsupportedFeatureCollector.AddWarning(
                            "ParticleSystem Emission Burst Cycles（爆发有限循环次数）",
                            go,
                            $"Burst time={burst.time:F2}s, cycleCount={burst.cycleCount}，LayaAir 不支持有限循环次数，将按无限循环导出"
                        );
                    }
                    // probability < 1 表示爆发不是每次都触发
                    if (burst.probability < 1f)
                    {
                        UnsupportedFeatureCollector.AddWarning(
                            "ParticleSystem Emission Burst Probability（爆发触发概率）",
                            go,
                            $"Burst time={burst.time:F2}s, probability={burst.probability:F2}，LayaAir 不支持 Burst 概率设置，将按 100% 概率导出"
                        );
                    }
                }
            }

            // ── 5. Shape 不支持的形状类型 + Shape 变换偏移 ─────────────────────────
            if (ps.shape.enabled)
            {
                // 5a. 不支持的形状类型
                var st = ps.shape.shapeType;
                bool isSupportedShape =
                    st == ParticleSystemShapeType.Sphere          ||
                    st == ParticleSystemShapeType.SphereShell     ||
                    st == ParticleSystemShapeType.Hemisphere      ||
                    st == ParticleSystemShapeType.HemisphereShell ||
                    st == ParticleSystemShapeType.Cone            ||
                    st == ParticleSystemShapeType.ConeVolume      ||
                    st == ParticleSystemShapeType.ConeVolumeShell ||
                    st == ParticleSystemShapeType.Box             ||
                    st == ParticleSystemShapeType.BoxShell        ||
                    st == ParticleSystemShapeType.BoxEdge         ||
                    st == ParticleSystemShapeType.Circle          ||
                    st == ParticleSystemShapeType.CircleEdge;
                if (!isSupportedShape)
                {
                    UnsupportedFeatureCollector.AddWarning(
                        "ParticleSystem Shape（不支持的形状类型）",
                        go,
                        $"当前形状: {st}，LayaAir 仅支持 Sphere/Hemisphere/Cone/Box/Circle，将降级为 SphereShape"
                    );
                }

                // 5b. Shape Rotation 非默认值（LayaAir 所有形状均不支持 rotation 参数）
                if (ps.shape.rotation != Vector3.zero)
                {
                    UnsupportedFeatureCollector.AddWarning(
                        "ParticleSystem Shape Rotation（形状旋转偏移）",
                        go,
                        $"Shape rotation={ps.shape.rotation}，LayaAir 不支持 Shape 模块的旋转参数，发射朝向的旋转偏移将丢失。" +
                        "建议将 Shape Rotation 的值合并到粒子系统 GameObject 的 Transform Rotation 中以保持视觉效果一致"
                    );
                }

                // 5c. Shape Position 非默认值
                if (ps.shape.position != Vector3.zero)
                {
                    UnsupportedFeatureCollector.AddWarning(
                        "ParticleSystem Shape Position（形状位置偏移）",
                        go,
                        $"Shape position={ps.shape.position}，LayaAir 不支持 Shape 模块的 position 偏移参数，发射位置偏移将丢失"
                    );
                }
            }

            // ── 6. LimitVelocityOverLifetime ─────────────────────────────────────
            if (ps.limitVelocityOverLifetime.enabled)
            {
                UnsupportedFeatureCollector.AddWarning(
                    "ParticleSystem LimitVelocityOverLifetime（生命周期速度限制）",
                    go,
                    "LayaAir 粒子系统不支持 Limit Velocity over Lifetime 模块，该模块不会被导出"
                );
            }

            // ── 7. InheritVelocity（继承速度）────────────────────────────────────
            if (ps.inheritVelocity.enabled)
            {
                UnsupportedFeatureCollector.AddWarning(
                    "ParticleSystem InheritVelocity（继承速度）",
                    go,
                    "LayaAir 粒子系统不支持 Inherit Velocity 模块，该模块不会被导出"
                );
            }

            // ── 8. ForceOverLifetime（生命周期受力）──────────────────────────────
            if (ps.forceOverLifetime.enabled)
            {
                UnsupportedFeatureCollector.AddWarning(
                    "ParticleSystem ForceOverLifetime（生命周期受力）",
                    go,
                    "LayaAir 粒子系统不支持 Force over Lifetime 模块，该模块不会被导出"
                );
            }

            // ── 9. ColorBySpeed（按速度变色）────────────────────────────────────
            if (ps.colorBySpeed.enabled)
            {
                UnsupportedFeatureCollector.AddWarning(
                    "ParticleSystem ColorBySpeed（按速度变色）",
                    go,
                    "LayaAir 粒子系统不支持 Color by Speed 模块，该模块不会被导出"
                );
            }

            // ── 10. SizeBySpeed（按速度变尺寸）──────────────────────────────────
            if (ps.sizeBySpeed.enabled)
            {
                UnsupportedFeatureCollector.AddWarning(
                    "ParticleSystem SizeBySpeed（按速度变尺寸）",
                    go,
                    "LayaAir 粒子系统不支持 Size by Speed 模块，该模块不会被导出"
                );
            }

            // ── 11. RotationBySpeed（按速度旋转）────────────────────────────────
            if (ps.rotationBySpeed.enabled)
            {
                UnsupportedFeatureCollector.AddWarning(
                    "ParticleSystem RotationBySpeed（按速度旋转）",
                    go,
                    "LayaAir 粒子系统不支持 Rotation by Speed 模块，该模块不会被导出"
                );
            }

            // ── 12. ExternalForces（外部风力区域）───────────────────────────────
            if (ps.externalForces.enabled)
            {
                UnsupportedFeatureCollector.AddWarning(
                    "ParticleSystem ExternalForces（外部风力区域）",
                    go,
                    "LayaAir 粒子系统不支持 External Forces 模块，WindZone 等外力不会被导出"
                );
            }

            // ── 13. Collision（粒子碰撞）────────────────────────────────────────
            if (ps.collision.enabled)
            {
                UnsupportedFeatureCollector.AddWarning(
                    "ParticleSystem Collision（粒子碰撞）",
                    go,
                    "LayaAir 粒子系统不支持 Collision 模块，该模块不会被导出"
                );
            }

            // ── 14. Trigger（粒子触发器）────────────────────────────────────────
            if (ps.trigger.enabled)
            {
                UnsupportedFeatureCollector.AddWarning(
                    "ParticleSystem Trigger（粒子触发器）",
                    go,
                    "LayaAir 粒子系统不支持 Trigger 模块，该模块不会被导出"
                );
            }

            // ── 15. Trails（粒子拖尾）────────────────────────────────────────────
            if (ps.trails.enabled)
            {
                UnsupportedFeatureCollector.AddWarning(
                    "ParticleSystem Trails（粒子拖尾）",
                    go,
                    "LayaAir 粒子系统不支持 Trails 模块，拖尾效果不会被导出"
                );
            }

            // ── 16. Lights（粒子灯光）────────────────────────────────────────────
            if (ps.lights.enabled)
            {
                UnsupportedFeatureCollector.AddWarning(
                    "ParticleSystem Lights（粒子灯光）",
                    go,
                    "LayaAir 粒子系统不支持 Lights 模块，粒子灯光效果不会被导出"
                );
            }

            // ── 17. Noise（噪声扰动）─────────────────────────────────────────────
            if (ps.noise.enabled)
            {
                UnsupportedFeatureCollector.AddWarning(
                    "ParticleSystem Noise（噪声扰动）",
                    go,
                    "LayaAir 粒子系统不支持 Noise 模块，该模块不会被导出"
                );
            }

            // ── 18. SubEmitters（子发射器）───────────────────────────────────────
            if (ps.subEmitters.enabled)
            {
                UnsupportedFeatureCollector.AddWarning(
                    "ParticleSystem SubEmitters（子发射器）",
                    go,
                    "LayaAir 粒子系统不支持 SubEmitters 模块，子发射器不会被导出"
                );
            }

            // ── 19. CustomData（自定义数据）──────────────────────────────────────
            if (ps.customData.enabled)
            {
                UnsupportedFeatureCollector.AddWarning(
                    "ParticleSystem CustomData（粒子自定义数据）",
                    go,
                    "LayaAir 粒子系统不支持 CustomData 模块，该模块不会被导出。如果 Shader 依赖 CustomData，请改用 UV/颜色等已支持的通道传递数据"
                );
            }
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

            // 始终导出 startLifetimeConstant (TwoConstants 取 constantMax，Curve 模式通过求值获取真实值)
            float lifetimeConstant = lifetimeType == 2 ? main.startLifetime.constantMax : EvaluateMinMaxCurveConstant(main.startLifetime, 5f);
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

            // 始终导出 startSpeedConstant (TwoConstants 取 constantMax，Curve 模式通过求值获取真实值)
            float speedConstant = speedType == 2 ? main.startSpeed.constantMax : EvaluateMinMaxCurveConstant(main.startSpeed, 5f);
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
                    // Curve 模式下 .constant 返回 0；使用 helper 正确求值
                    float sizeConstant = EvaluateMinMaxCurveConstant(main.startSize, 1f);
                    if (sizeConstant != 1)
                        particleSystem.AddField("startSizeConstant", sizeConstant);
                }
                else
                {
                    if (main.startSize.constantMin != 0)
                        particleSystem.AddField("startSizeConstantMin", main.startSize.constantMin);
                    if (main.startSize.constantMax != 1)
                        particleSystem.AddField("startSizeConstantMax", main.startSize.constantMax);
                }
            }

            // startSizeConstantSeparate (始终导出，Curve 模式用 helper 求值)
            JSONObject sizeSeparate = CreateVector3Object(
                EvaluateMinMaxCurveConstant(main.startSizeX, 1f),
                EvaluateMinMaxCurveConstant(main.startSizeY, 1f),
                EvaluateMinMaxCurveConstant(main.startSizeZ, 1f)
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

            // 始终导出emissionRate（Laya默认不一定是10，显式导出以确保正确）
            emissionObj.AddField("emissionRate", EvaluateMinMaxCurveConstant(emission.rateOverTime));

            float rateOverDistance = EvaluateMinMaxCurveConstant(emission.rateOverDistance);
            if (rateOverDistance != 0)
                emissionObj.AddField("emissionRateOverDistance", rateOverDistance);

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
                    // 只导出非默认值: _time 默认0
                    if (burst.time != 0)
                        burstObj.AddField("_time", burst.time);

                    // 使用 burst.count (MinMaxCurve) 而非已废弃的 minCount/maxCount
                    var countCurve = burst.count;
                    if (countCurve.mode == ParticleSystemCurveMode.Constant)
                    {
                        // 常量模式: min 和 max 都设为同一个值
                        int count = (int)countCurve.constant;
                        burstObj.AddField("_minCount", count);
                        burstObj.AddField("_maxCount", count);
                    }
                    else if (countCurve.mode == ParticleSystemCurveMode.TwoConstants)
                    {
                        // 随机双常量模式: 分别设置 min 和 max
                        burstObj.AddField("_minCount", (int)countCurve.constantMin);
                        burstObj.AddField("_maxCount", (int)countCurve.constantMax);
                    }
                    else
                    {
                        // 曲线模式等其他情况: 回退到 evaluate 取当前值
                        int count = (int)countCurve.Evaluate(0f);
                        burstObj.AddField("_minCount", count);
                        burstObj.AddField("_maxCount", count);
                    }

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
        ///
        /// 注意：Unity ShapeModule 的 rotation/position 变换偏移不被 LayaAir 支持，
        /// 这些参数会由 CheckUnsupportedParticleFeatures 在导出时产生警告。
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
                    // Unity Shape.scale 会影响锥体几何形状，LayaAir ConeShape 无 scale 参数，需转换到 angleDEG/radius 上
                    // scale.z 压缩锥体轴向分量: effectiveAngle = atan2(sin(angle), cos(angle) * scaleZ)
                    // scale.x/y 缩放底面圆: effectiveRadius = radius * avg(scaleX, scaleY)
                    float coneAngle = shape.angle;
                    float coneRadius = shape.radius;
                    Vector3 coneScale = shape.scale;
                    if (coneScale.z != 1f || coneScale.x != 1f || coneScale.y != 1f)
                    {
                        if (coneScale.z != 1f)
                        {
                            float angleRad = coneAngle * Mathf.Deg2Rad;
                            coneAngle = Mathf.Atan2(Mathf.Sin(angleRad), Mathf.Cos(angleRad) * Mathf.Abs(coneScale.z)) * Mathf.Rad2Deg;
                        }
                        if (coneScale.x != 1f || coneScale.y != 1f)
                        {
                            coneRadius = shape.radius * (coneScale.x + coneScale.y) * 0.5f;
                        }
                        Debug.Log($"LayaAir3D: ConeShape scale({coneScale.x}, {coneScale.y}, {coneScale.z}) 已转换 → angleDEG: {shape.angle}→{coneAngle:F1}, radius: {shape.radius}→{coneRadius:F3}");
                    }
                    if (coneAngle != 25)
                        shapeObj.AddField("angleDEG", coneAngle);
                    if (coneRadius != 1)
                        shapeObj.AddField("radius", coneRadius);
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
            tsaObj.AddField("_$type", "TextureSheetAnimation");
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
            frame.AddField("_$type", "FrameOverTime");
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
                ParticleSystem.MinMaxCurve frameCurve = tsa.frameOverTime;
                float multiplier = frameCount * frameCurve.curveMultiplier;
                if (frameType == 3)
                {
                    WriteGradientCurve(frameCurve.curveMin, frame, "_overTimeMin", multiplier, "GradientDataInt");
                    WriteGradientCurve(frameCurve.curveMax, frame, "_overTimeMax", multiplier, "GradientDataInt");
                }
                else
                {
                    WriteGradientCurve(frameCurve.curve, frame, "_overTime", multiplier, "GradientDataInt");
                }
            }
            tsaObj.AddField("_frame", frame);

            // _startFrame
            JSONObject startFrame = new JSONObject(JSONObject.Type.OBJECT);
            startFrame.AddField("_$type", "StartFrame");
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

        #region Validation Methods

        /// <summary>
        /// 检查并处理粒子系统顶点数量超限问题
        /// 只检查Mesh渲染模式的粒子系统
        /// LayaAir限制：粒子系统总顶点数 = maxParticles × meshVertexCount ≤ 65535
        /// </summary>
        private static void CheckParticleVertexLimit(ParticleSystem ps, ParticleSystemRenderer psr, string objectName, ResoureMap resoureMap = null)
        {
            // 只有Mesh渲染模式才需要检查顶点限制
            if (psr.renderMode != ParticleSystemRenderMode.Mesh)
            {
                return;
            }

            // 没有Mesh则不需要检查
            if (psr.mesh == null)
            {
                return;
            }

            int maxParticles = ps.main.maxParticles;
            Mesh originalMesh = psr.mesh;
            int meshVertexCount = originalMesh.vertexCount;
            int totalVertexCount = maxParticles * meshVertexCount;
            int vertexLimit = ExportConfig.ParticleMeshMaxVertices;

#if ENABLE_PARTICLE_MESH_OPTIMIZATION
            if (totalVertexCount > vertexLimit)
            {
                // ⚠️ 粒子Mesh优化功能已暂时禁用
                // 如需启用，请在项目设置中添加 ENABLE_PARTICLE_MESH_OPTIMIZATION 脚本定义符号

                // 计算建议的最大粒子数和目标顶点数
                int suggestedMaxParticles = MeshSimplifier.CalculateSuggestedMaxParticles(meshVertexCount, vertexLimit);
                int targetMeshVertexCount = MeshSimplifier.CalculateTargetVertexCount(maxParticles, vertexLimit);

                string problemDescription = string.Format(
                    "粒子系统 '{0}' (Mesh模式) 的顶点数量超过限制!\n" +
                    "当前配置: maxParticles={1}, Mesh顶点数={2}, 总顶点数={3}\n" +
                    "限制: 总顶点数 ≤ {4}",
                    objectName, maxParticles, meshVertexCount, totalVertexCount, vertexLimit);

                // 尝试自动简化mesh
                if (ExportConfig.AutoSimplifyParticleMesh)
                {
                    ExportLogger.Log($"LayaAir3D: {problemDescription}");

                    // ⭐ 优先检查ParticleSystemRenderer是否有meshes数组（支持多个mesh）
                    // 如果meshes[1]存在，可以作为低面数版本
                    Mesh simplifiedMesh = null;
                    bool useManualLOD = false;

                    #if UNITY_2018_1_OR_NEWER
                    if (psr.meshCount > 1)
                    {
                        Mesh[] meshes = new Mesh[psr.meshCount];
                        int actualMeshCount = psr.GetMeshes(meshes);

                        if (actualMeshCount > 1 && meshes[1] != null)
                        {
                            Mesh lodMesh = meshes[1];

                            int lodTotalVertices = maxParticles * lodMesh.vertexCount;
                            if (lodTotalVertices <= vertexLimit * 1.3f) // 弹性判断
                            {
                                simplifiedMesh = lodMesh;
                                useManualLOD = true;
                                ExportLogger.Log($"LayaAir3D: 检测到手动LOD Mesh (meshes[1]): {lodMesh.name}");
                                ExportLogger.Log($"  顶点数: {lodMesh.vertexCount}, 总顶点: {lodTotalVertices}");
                            }
                            else
                            {
                                Debug.LogWarning($"LayaAir3D: meshes[1]的顶点数({lodTotalVertices})仍超限，使用自动简化");
                            }
                        }
                    }
                    #endif

                    if (!useManualLOD)
                    {
                        ExportLogger.Log($"LayaAir3D: 尝试自动简化mesh到 {targetMeshVertexCount} 个顶点...");

                        try
                        {
                            simplifiedMesh = MeshSimplifier.SimplifyMesh(
                                originalMesh,
                                targetMeshVertexCount,
                                ExportConfig.ParticleMeshSimplifyQuality
                            );
                        }
                        catch (System.Exception e)
                        {
                            Debug.LogError($"LayaAir3D: Mesh简化过程中出现错误: {e.Message}");
                            simplifiedMesh = null;
                        }
                    }

                    // 检查简化后的总顶点数
                    if (simplifiedMesh != null)
                    {
                            int newTotalVertices = maxParticles * simplifiedMesh.vertexCount;

                            // ⭐ 允许适度超限以保证mesh质量
                            // 根据简化质量参数决定允许超限比例
                            // quality越高（0.7-1.0），允许超限越多，质量越好
                            float qualityFactor = ExportConfig.ParticleMeshSimplifyQuality;
                            float allowedOverrunRatio = 1.0f + qualityFactor * 0.8f; // 0.7质量→允许超出56%
                            int flexibleLimit = Mathf.CeilToInt(vertexLimit * allowedOverrunRatio);
                            bool meetsFlexibleLimit = newTotalVertices <= flexibleLimit;
                            bool meetsStrictLimit = newTotalVertices <= vertexLimit;

                            // 计算简化比例
                            float reductionPercent = (1.0f - (float)simplifiedMesh.vertexCount / meshVertexCount) * 100;

                            ExportLogger.Log($"LayaAir3D: 简化结果检查");
                            ExportLogger.Log($"  简化后Mesh顶点数: {simplifiedMesh.vertexCount} (原始: {meshVertexCount}, 减少 {reductionPercent:F1}%)");
                            ExportLogger.Log($"  总顶点数: {newTotalVertices} (原始: {totalVertexCount})");
                            ExportLogger.Log($"  严格限制: {vertexLimit}, 满足: {(meetsStrictLimit ? "✓" : "✗")}");
                            ExportLogger.Log($"  弹性限制: {flexibleLimit} (quality={qualityFactor:F1}, 允许超出{(allowedOverrunRatio-1)*100:F0}%), 满足: {(meetsFlexibleLimit ? "✓" : "✗")}");

                            if (meetsFlexibleLimit)
                            {
                                // 保留原始mesh的名称，确保mesh名称一致
                                simplifiedMesh.name = originalMesh.name;

                                // 替换为简化后的mesh
                                psr.mesh = simplifiedMesh;

                                // ⭐ 关键修复：如果使用ResoureMap，需要强制更新缓存
                                if (resoureMap != null)
                                {
                                    // 获取原始mesh的路径
                                    string originalPath = AssetsUtil.GetMeshPath(originalMesh);

                                    // 注册简化mesh实例到原始路径的映射
                                    resoureMap.RegisterMeshPath(simplifiedMesh, originalPath);

                                    // 移除原始mesh的缓存
                                    resoureMap.RemoveFileData(originalPath);

                                    // 添加简化后的mesh到ResoureMap（使用原始mesh的路径）
                                    MeshFile simplifiedMeshFile = new MeshFile(simplifiedMesh, psr, originalPath);
                                    resoureMap.AddExportFile(simplifiedMeshFile);

                                    ExportLogger.Log($"LayaAir3D: 已更新ResoureMap中的Mesh缓存: {originalPath}");
                                }

                                string statusIcon = meetsStrictLimit ? "✓" : "⚠";
                                string statusText = meetsStrictLimit ? "完全满足限制" : "超出严格限制但在可接受范围内";

                                string successMessage = string.Format(
                                    "{0} 成功简化粒子Mesh\n" +
                                    "  Mesh顶点: {1} → {2} (减少 {3:F1}%)\n" +
                                    "  总顶点数: {4} → {5}\n" +
                                    "  状态: {6}",
                                    statusIcon,
                                    meshVertexCount, simplifiedMesh.vertexCount, reductionPercent,
                                    totalVertexCount, newTotalVertices,
                                    statusText);

                                ExportLogger.Log($"LayaAir3D: {successMessage}");

                                if (!meetsStrictLimit)
                                {
                                    ExportLogger.Log($"LayaAir3D: 提示 - 如需严格满足限制({vertexLimit})，可以:");
                                    ExportLogger.Log($"  1. 降低简化质量到 0.5-0.6 (当前 {qualityFactor:F1})");
                                    ExportLogger.Log($"  2. 减少maxParticles到 {suggestedMaxParticles} (当前 {maxParticles})");
                                    ExportLogger.Log($"  3. 提高顶点数限制到 {newTotalVertices} 以上");
                                }

                                // 简化成功，不再显示警告
                                return;
                            }
                            else
                            {
                                Debug.LogWarning($"LayaAir3D: Mesh简化后仍超出弹性限制");
                                Debug.LogWarning($"  总顶点数 {newTotalVertices} > 弹性限制 {flexibleLimit}");
                                Debug.LogWarning($"  建议: 提高简化质量到 0.8-0.9, 或降低maxParticles到 {suggestedMaxParticles}, 或提高顶点限制");
                            }
                    }
                    else
                    {
                        Debug.LogWarning("LayaAir3D: Mesh简化返回null，将显示警告");
                    }
                }

                // 显示警告（如果没有自动简化或简化失败）
                if (ExportConfig.ShowParticleMeshWarning)
                {
                    string warningMessage = string.Format(
                        "{0}\n\n" +
                        "解决方案:\n" +
                        "1. 将 maxParticles 减少到 {1} 或以下\n" +
                        "2. 使用顶点数更少的Mesh (目标: ≤{2} 个顶点)\n" +
                        "3. 启用'自动简化粒子Mesh'选项 (LayaAir导出设置)\n" +
                        "4. 在导出设置中调整顶点数限制",
                        problemDescription, suggestedMaxParticles, targetMeshVertexCount);

                    Debug.LogWarning($"LayaAir3D: {warningMessage}");

                    // 在编辑器中显示对话框提醒用户
                    if (!Application.isBatchMode)
                    {
                        EditorUtility.DisplayDialog(
                            "LayaAir3D 粒子导出警告",
                            warningMessage,
                            "我知道了");
                    }
                }
            }
#endif
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

        /// <summary>
        /// 从 MinMaxCurve 获取代表性常量值，支持全部四种模式：
        /// Constant      → curve.constant
        /// TwoConstants  → curve.constantMax
        /// Curve         → curveMultiplier * max(curve.curve)
        /// TwoCurves     → curveMultiplier * max(curve.curveMax)
        /// 这样可避免 Curve 模式下 .constant 永远返回 0 的问题。
        /// </summary>
        private static float EvaluateMinMaxCurveConstant(ParticleSystem.MinMaxCurve curve, float fallback = 0f)
        {
            switch (curve.mode)
            {
                case ParticleSystemCurveMode.Constant:
                    return curve.constant;
                case ParticleSystemCurveMode.TwoConstants:
                    return curve.constantMax;
                case ParticleSystemCurveMode.Curve:
                    return curve.curveMultiplier * EvaluateCurveMax(curve.curve);
                case ParticleSystemCurveMode.TwoCurves:
                    return curve.curveMultiplier * EvaluateCurveMax(curve.curveMax);
                default:
                    return fallback;
            }
        }

        /// <summary>
        /// 对 AnimationCurve 采样取最大值。
        /// </summary>
        private static float EvaluateCurveMax(AnimationCurve curve, int samples = 32)
        {
            if (curve == null || curve.length == 0) return 0f;
            float max = float.MinValue;
            for (int i = 0; i <= samples; i++)
            {
                float t = (float)i / samples;
                float v = curve.Evaluate(t);
                if (v > max) max = v;
            }
            return max;
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
            WriteGradientCurve(curve.curve, parent, fieldName, multiplier);
        }

        private static void WriteGradientCurve(AnimationCurve animCurve, JSONObject parent, string fieldName, float multiplier, string dataType = null)
        {
            if (animCurve == null || animCurve.length == 0)
            {
                animCurve = AnimationCurve.Linear(0, 0, 1, 1);
            }

            JSONObject gradientData = new JSONObject(JSONObject.Type.OBJECT);
            if (dataType != null)
                gradientData.AddField("_$type", dataType);

            int actualKeyCount;
            List<float> elements = SampleCurveElements(animCurve, multiplier, out actualKeyCount);

            // 从采样结果中计算 min/max
            float minValue = float.MaxValue;
            float maxValue = float.MinValue;
            for (int i = 0; i < actualKeyCount; i++)
            {
                float value = elements[i * 2 + 1];
                minValue = Mathf.Min(minValue, value);
                maxValue = Mathf.Max(maxValue, value);
            }

            gradientData.AddField("_elements", CreateFloat32Array(elements));
            gradientData.AddField("_currentLength", actualKeyCount * 2);

            if (minValue == float.MaxValue) minValue = -1;
            if (maxValue == float.MinValue) maxValue = 1;
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
                int actualKeyCount;
                List<float> elementsMin = SampleCurveElements(curveMin, multiplier, out actualKeyCount);

                float minVal = float.MaxValue, maxVal = float.MinValue;
                for (int i = 0; i < actualKeyCount; i++)
                {
                    float value = elementsMin[i * 2 + 1];
                    minVal = Mathf.Min(minVal, value);
                    maxVal = Mathf.Max(maxVal, value);
                }

                gradientDataMin.AddField("_elements", CreateFloat32Array(elementsMin));
                gradientDataMin.AddField("_currentLength", actualKeyCount * 2);

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
                int actualKeyCount;
                List<float> elementsMax = SampleCurveElements(curveMax, multiplier, out actualKeyCount);

                float minVal = float.MaxValue, maxVal = float.MinValue;
                for (int i = 0; i < actualKeyCount; i++)
                {
                    float value = elementsMax[i * 2 + 1];
                    minVal = Mathf.Min(minVal, value);
                    maxVal = Mathf.Max(maxVal, value);
                }

                gradientDataMax.AddField("_elements", CreateFloat32Array(elementsMax));
                gradientDataMax.AddField("_currentLength", actualKeyCount * 2);

                float absMax = Mathf.Max(Mathf.Abs(minVal), Mathf.Abs(maxVal));
                if (absMax < 1) absMax = 1;
                gradientDataMax.AddField("_curveMin", -absMax);
                gradientDataMax.AddField("_curveMax", absMax);

                parent.AddField(maxFieldName, gradientDataMax);
            }
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

            int actualKeyCount;
            List<float> elements = SampleCurveElements(animCurve, multiplier, out actualKeyCount);

            gradientData.AddField("_elements", CreateFloat32Array(elements));
            gradientData.AddField("_currentLength", actualKeyCount * 2);

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

                int actualKeyCount;
                List<float> elementsMin = SampleCurveElements(curveMin, multiplier, out actualKeyCount);

                gradientDataMin.AddField("_elements", CreateFloat32Array(elementsMin));
                gradientDataMin.AddField("_currentLength", actualKeyCount * 2);

                parent.AddField(minFieldName, gradientDataMin);
            }

            AnimationCurve curveMax = curve.curveMax;
            if (curveMax != null && curveMax.length > 0)
            {
                JSONObject gradientDataMax = new JSONObject(JSONObject.Type.OBJECT);
                gradientDataMax.AddField("_$type", "GradientDataNumber");

                int actualKeyCount;
                List<float> elementsMax = SampleCurveElements(curveMax, multiplier, out actualKeyCount);

                gradientDataMax.AddField("_elements", CreateFloat32Array(elementsMax));
                gradientDataMax.AddField("_currentLength", actualKeyCount * 2);

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
        /// Laya GradientDataNumber._dataBuffer = new Float32Array(8)，最多4个关键帧。
        /// 当 Unity AnimationCurve 关键帧超过4个时，均匀采样4个点保留曲线形状。
        /// </summary>
        private const int MAX_GRADIENT_KEYFRAMES = 4;
        private const int MAX_GRADIENT_ELEMENTS = MAX_GRADIENT_KEYFRAMES * 2; // 8

        private static List<float> SampleCurveElements(AnimationCurve animCurve, float multiplier, out int actualKeyCount)
        {
            List<float> elements = new List<float>();

            if (animCurve.length <= MAX_GRADIENT_KEYFRAMES)
            {
                actualKeyCount = animCurve.length;
                foreach (var key in animCurve.keys)
                {
                    elements.Add(key.time);
                    elements.Add(key.value * multiplier);
                }
            }
            else
            {
                // 关键帧超过限制，均匀采样4个点（首尾精确，中间插值）
                actualKeyCount = MAX_GRADIENT_KEYFRAMES;
                float startTime = animCurve.keys[0].time;
                float endTime = animCurve.keys[animCurve.length - 1].time;

                for (int i = 0; i < MAX_GRADIENT_KEYFRAMES; i++)
                {
                    float t = (float)i / (MAX_GRADIENT_KEYFRAMES - 1);
                    float time = Mathf.Lerp(startTime, endTime, t);
                    float value = animCurve.Evaluate(time) * multiplier;
                    elements.Add(time);
                    elements.Add(value);
                }

                Debug.LogWarning($"LayaAir3D: AnimationCurve has {animCurve.length} keyframes, exceeds GradientDataNumber limit of {MAX_GRADIENT_KEYFRAMES}. Resampled to {MAX_GRADIENT_KEYFRAMES} points.");
            }

            // 填充到8个float
            while (elements.Count < MAX_GRADIENT_ELEMENTS)
                elements.Add(0);

            return elements;
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
