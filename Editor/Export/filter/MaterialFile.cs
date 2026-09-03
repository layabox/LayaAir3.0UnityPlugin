
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using System.IO;

internal class MaterialFile : JsonFile
{
    private Material m_material;
    private bool m_isBuiltinParticleMaterial = false;
    private bool m_isCPUParticle = false;
    private HashSet<System.Type> m_rendererTypes = new HashSet<System.Type>();
    private bool m_isParticleMeshMode = false; // ⭐ Track if particle uses mesh rendering mode
    private bool m_isUsedBy2DComponent = false; // ★ Track if used by 2D/UI component (SpriteRenderer or Image)

    public MaterialFile(ResoureMap map, Material material, Renderer renderer = null,
        string pathOverride = null, bool is2DUsage = false, bool isCPUParticle = false) : base(null,new JSONObject(JSONObject.Type.OBJECT))
    {
        this.resoureMap = map;
        this.m_material = material;
        this.m_isCPUParticle = isCPUParticle;

        // ★ 2D 标记必须在 WriteMetarial 之前设置（Image 没有 Renderer，靠此参数传入）
        if (is2DUsage)
        {
            m_isUsedBy2DComponent = true;
        }

        // Track which type of renderer uses this material
        if (renderer != null)
        {
            m_rendererTypes.Add(renderer.GetType());

            // ⭐ Check if this is a ParticleSystemRenderer in mesh mode
            ParticleSystemRenderer particleRenderer = renderer as ParticleSystemRenderer;
            if (particleRenderer != null)
            {
                ParticleSystemRenderMode renderMode = particleRenderer.renderMode;
                if (renderMode == ParticleSystemRenderMode.Mesh)
                {
                    m_isParticleMeshMode = true;
                    ExportLogger.Log($"LayaAir3D: Particle system '{renderer.gameObject.name}' uses MESH render mode");
                }
            }

            // ★ Check if used by SpriteRenderer (2D component)
            if (renderer is SpriteRenderer)
            {
                m_isUsedBy2DComponent = true;
            }
        }

        // 检查是否是内置粒子材质
        string materialPath = AssetDatabase.GetAssetPath(material.GetInstanceID());
        bool isBuiltinResource = ResoureMap.IsBuiltinResource(materialPath);
        bool isParticleMaterial = IsParticleShader(material.shader.name);
        bool isUsedByParticle = renderer is ParticleSystemRenderer;

        // CPU 粒子模式：使用独立路径和 CPU 覆盖配置表
        if (isCPUParticle)
        {
            string cpuPath = AssetsUtil.GetMaterialPath(material) + "#cpu";
            this.updatePath(cpuPath);
            MetarialUitls.WriteMetarial(material, this.jsonData, map, this, true);
        }
        // 内置资源 + (粒子shader 或 粒子渲染器使用) → 导出为Laya Shuriken粒子材质
        else if (isBuiltinResource && (isParticleMaterial || isUsedByParticle))
        {
            m_isBuiltinParticleMaterial = true;
            this.updatePath(pathOverride ?? AssetsUtil.GetMaterialPath(material));
            WriteBuiltinParticleMaterial(material, this.jsonData, map);
        }
        else
        {
            this.updatePath(pathOverride ?? AssetsUtil.GetMaterialPath(material));
            if(material.shader.name == "Skybox/6 Sided")
            {
                MetarialUitls.WriteSkyMetarial(material, this.jsonData, map);
            }
            else
            {
                MetarialUitls.WriteMetarial(material, this.jsonData, map, this);
            }
        }
    }

    /// <summary>
    /// Add a renderer type that uses this material (called when material is reused)
    /// </summary>
    public void AddRendererUsage(Renderer renderer)
    {
        if (renderer != null)
        {
            m_rendererTypes.Add(renderer.GetType());

            // ⭐ Check if this is a ParticleSystemRenderer in mesh mode
            ParticleSystemRenderer particleRenderer = renderer as ParticleSystemRenderer;
            if (particleRenderer != null)
            {
                ParticleSystemRenderMode renderMode = particleRenderer.renderMode;
                if (renderMode == ParticleSystemRenderMode.Mesh)
                {
                    m_isParticleMeshMode = true;
                    ExportLogger.Log($"LayaAir3D: Particle system '{renderer.gameObject.name}' uses MESH render mode");
                }
            }

            // ★ Check if used by SpriteRenderer (2D component)
            if (renderer is SpriteRenderer)
            {
                m_isUsedBy2DComponent = true;
            }
        }
    }

    /// <summary>
    /// 标记此材质被 2D/UI 组件使用（如 Image，没有 Renderer 组件）
    /// </summary>
    public void MarkAs2DUsage()
    {
        m_isUsedBy2DComponent = true;
    }

    /// <summary>
    /// 检查此材质是否被 2D/UI 组件使用（SpriteRenderer 或 Image）
    /// </summary>
    public bool IsUsedBy2DComponent()
    {
        return m_isUsedBy2DComponent;
    }

    /// <summary>
    /// 检查此材质是否用于 CPU 粒子导出
    /// </summary>
    public bool IsCPUParticle() => m_isCPUParticle;

    /// <summary>
    /// Check if this material is used by ParticleSystemRenderer
    /// </summary>
    public bool IsUsedByParticleSystem()
    {
        return m_rendererTypes.Contains(typeof(ParticleSystemRenderer));
    }

    /// <summary>
    /// Check if this material is used by MeshRenderer or SkinnedMeshRenderer
    /// </summary>
    public bool IsUsedByMeshRenderer()
    {
        return m_rendererTypes.Contains(typeof(MeshRenderer)) ||
               m_rendererTypes.Contains(typeof(SkinnedMeshRenderer));
    }

    /// <summary>
    /// ⭐ Check if this material is used by ParticleSystemRenderer in MESH rendering mode
    /// </summary>
    public bool IsParticleMeshMode()
    {
        return m_isParticleMeshMode;
    }

    /// <summary>
    /// 检查是否是 Unity 内置粒子 Shader
    /// 仅用于内置材质检测，所有 Unity 内置粒子 shader 名称都包含 "particle"
    /// 例如: "Particles/Standard Unlit", "Mobile/Particles/Additive",
    ///       "Legacy Shaders/Particles/Alpha Blended", "Universal Render Pipeline/Particles/*"
    /// </summary>
    private static bool IsParticleShader(string shaderName)
    {
        if (string.IsNullOrEmpty(shaderName))
            return false;

        return shaderName.ToLower().Contains("particle");
    }
    
    /// <summary>
    /// 为内置粒子材质写入数据，基于 ParticleMaterial.lmat 模板
    /// 模板结构: {"version":"LAYAMATERIAL:04","props":{"textures":[{"name":"u_texture"}],
    /// "type":"PARTICLESHURIKEN","renderQueue":3000,"materialRenderMode":2,"s_Cull":2,
    /// "s_Blend":1,"s_BlendSrc":6,"s_BlendDst":7,"s_DepthTest":1,"s_DepthWrite":false,
    /// "u_Tintcolor":[0.5,0.5,0.5,1],"defines":["TINTCOLOR"]}}
    /// </summary>
    private void WriteBuiltinParticleMaterial(Material material, JSONObject jsonData, ResoureMap resoureMap)
    {
        jsonData.AddField("version", "LAYAMATERIAL:04");
        JSONObject props = new JSONObject(JSONObject.Type.OBJECT);
        jsonData.AddField("props", props);
        
        // 纹理数组 - 内置材质通常没有可导出的纹理，保持空数组（shader uniformMap 会使用 "white" 默认值）
        JSONObject textures = new JSONObject(JSONObject.Type.ARRAY);
        props.AddField("textures", textures);

        // 使用 Laya 内置粒子材质类型
        props.AddField("type", "PARTICLESHURIKEN");
        
        // 渲染队列
        props.AddField("renderQueue", material.renderQueue > 0 ? material.renderQueue : 3000);
        
        // 材质渲染模式 - 标准状态匹配 Alpha/Additive，其他组合使用 Custom(5)
        props.AddField("materialRenderMode", PropDatasConfig.DetectTransparentRenderMode(material));
        
        // 剔除模式 - 粒子默认双面 (0=Off, 1=Front, 2=Back)
        int cullMode = 0; // 默认 Off
        if (material.HasProperty("_Cull"))
        {
            cullMode = material.GetInt("_Cull");
        }
        props.AddField("s_Cull", cullMode);
        
        // 混合设置
        props.AddField("s_Blend", 1);
        
        // 源混合因子和目标混合因子 - 根据 shader 名称判断
        int srcBlend = 6; // SrcAlpha
        int dstBlend = 7; // OneMinusSrcAlpha
        
        string shaderName = material.shader.name.ToLower();
        if (shaderName.Contains("additive"))
        {
            srcBlend = 6; // SrcAlpha
            dstBlend = 1; // One
        }
        else if (shaderName.Contains("multiply"))
        {
            srcBlend = 4; // DstColor
            dstBlend = 0; // Zero
        }
        else if (shaderName.Contains("premultiply"))
        {
            srcBlend = 1; // One
            dstBlend = 7; // OneMinusSrcAlpha
        }
        
        // ShaderGraph 的 _BUILTIN_* 是最终生效状态，优先于普通属性和 shader 名称。
        if (material.HasProperty("_BUILTIN_SrcBlend") || material.HasProperty("_SrcBlend"))
            srcBlend = PropDatasConfig.GetSrcBlend(material);
        if (material.HasProperty("_BUILTIN_DstBlend") || material.HasProperty("_DstBlend"))
            dstBlend = PropDatasConfig.GetDstBlend(material);
        
        props.AddField("s_BlendSrc", srcBlend);
        props.AddField("s_BlendDst", dstBlend);
        
        // 深度测试
        props.AddField("s_DepthTest", 1);
        
        // 保留材质的实际深度写入状态。
        props.AddField("s_DepthWrite", PropDatasConfig.GetZWrite(material));
        
        // 颜色
        Color tintColor = new Color(0.5f, 0.5f, 0.5f, 1.0f);
        if (material.HasProperty("_TintColor"))
        {
            tintColor = material.GetColor("_TintColor");
        }
        else if (material.HasProperty("_Color"))
        {
            tintColor = material.GetColor("_Color");
        }
        else if (material.HasProperty("_BaseColor"))
        {
            tintColor = material.GetColor("_BaseColor");
        }
        
        JSONObject colorValue = new JSONObject(JSONObject.Type.ARRAY);
        colorValue.Add(tintColor.r);
        colorValue.Add(tintColor.g);
        colorValue.Add(tintColor.b);
        colorValue.Add(tintColor.a);
        props.AddField("u_Tintcolor", colorValue);

        // Defines
        JSONObject defines = new JSONObject(JSONObject.Type.ARRAY);
        defines.Add("TINTCOLOR");

        // ⭐ Add RENDERMODE_MESH define for particle mesh rendering mode
        if (m_isParticleMeshMode)
        {
            defines.Add("RENDERMODE_MESH");
            ExportLogger.Log($"LayaAir3D: Added RENDERMODE_MESH define for built-in particle material in mesh mode");
        }

        props.AddField("defines", defines);
    }
    
    /// <summary>
    /// Unity BlendMode 转换为 LayaAir BlendFactor
    /// </summary>
    /// <summary>
    /// Unity BlendMode 整数值 → Laya BlendFactor 整数值。
    /// 通过运行时枚举名称映射，兼容不同 Unity 版本中枚举整数值不同的情况。
    /// </summary>
    private static int ConvertUnityBlendToLaya(int unityBlend)
    {
        return CustomShaderExporter.UnityBlendToLaya(unityBlend);
    }

    protected override string getOutFilePath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return "default_material.lmat";
        }
        // Remove #cpu cache suffix before generating output path
        string cleanPath = path;
        if (cleanPath.EndsWith("#cpu"))
        {
            cleanPath = cleanPath.Substring(0, cleanPath.Length - 4);
        }
        // 修复：安全地获取不带扩展名的路径
        int dotIndex = cleanPath.LastIndexOf('.');
        string basePath = dotIndex >= 0 ? cleanPath.Substring(0, dotIndex) : cleanPath;
        string suffix = m_isCPUParticle ? "_cpu" : "";
        return GameObjectUitls.cleanIllegalChar(basePath, false) + suffix + ".lmat";
    }

    public override void SaveFile(Dictionary<string, FileData> exportFiles)
    {
        base.saveMeta();
        string jsonContent = this.jsonData.Print(true);
     
        FileStream fs = new FileStream(outPath, FileMode.Create, FileAccess.Write);
        StreamWriter writer = new StreamWriter(fs);
        writer.Write(jsonContent);
        writer.Close();
    }


}
