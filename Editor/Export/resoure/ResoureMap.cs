using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

using LayaExport;

internal class ResoureMap
{
    private Dictionary<string, FileData> exportFiles;
    private List<NodeMap> nodemaps;
    // Mesh实例ID到路径的映射表（用于简化后的粒子mesh）
    private Dictionary<int, string> meshInstanceToPath;
    // SpriteRenderer color 动画目标映射：子对象 → (AnimatorController, binding.path, Animator所在GO)
    private Dictionary<GameObject, SpriteColorAnimTarget> m_spriteColorAnimTargets = new Dictionary<GameObject, SpriteColorAnimTarget>();
    // 场景文件所在目录（用于 UI2D prefab 输出路径）
    private string m_sceneDir = "";
    // Animator2DSync runtime script file (lazy, one per export)
    private RuntimeScriptFile m_syncScriptFile;

    private struct SpriteColorAnimTarget
    {
        public AnimatorController controller;
        public string targetPath;
        public GameObject animatorGameObject;
    }

    public ResoureMap()
    {
        this.exportFiles = new Dictionary<string, FileData>();
        this.nodemaps = new List<NodeMap>();
        this.meshInstanceToPath = new Dictionary<int, string>();
    }

    public void SetSceneDir(string sceneDir)
    {
        m_sceneDir = sceneDir;
    }

    public PerfabFile getPerfabFile(string path)
    {
        if (!this.exportFiles.ContainsKey(path))
        {
            this.AddExportFile(new PerfabFile(this.AddNodeMap(), path));
        }
        return this.exportFiles[path] as PerfabFile;
    }

    public NodeMap AddNodeMap(int idOff = 0, bool sceneMode = false)
    {
        NodeMap nodemap = new NodeMap(this, idOff, sceneMode);
        this.nodemaps.Add(nodemap);
        return nodemap;
    }

    public void createNodeTree()
    {
        // crateNodeData() may add new PerfabFile entries (nested prefabs) to exportFiles.
        // Process iteratively with a processed-set so we never modify a live enumerator,
        // and nested prefabs are always fully initialised before their NodeMaps are used.
        var processed = new HashSet<string>();
        bool anyNew;
        do
        {
            anyNew = false;
            var keys = new List<string>(this.exportFiles.Keys);
            foreach (var key in keys)
            {
                if (!processed.Add(key)) continue;   // already handled
                anyNew = true;
                FileData file = this.exportFiles[key];
                if (file is PerfabFile val)
                    val.crateNodeData();
            }
        } while (anyNew);
        //创建未引用节点数结构
        foreach(NodeMap nodemap in this.nodemaps)
        {
            nodemap.createNodeTree();
        }
        //创建引用节点,同时生成节点信息
        foreach (NodeMap nodemap in this.nodemaps)
        {
            //nodemap.createRefNodeTree();
            nodemap.writeCompoent();
        }
    }

    public PerfabFile GetPerfabByObject(GameObject gameObject)
    {
        string path = PerfabFile.getPerfabFilePath(gameObject);
        if (this.exportFiles.ContainsKey(path))
        {
            return this.exportFiles[path] as PerfabFile;
        }
        else
        {
            ExportLogger.Warning("[LayaAir Export] Prefab path not found: " + path);
            return null;
        }
    }

    public MeshFile GetMeshFile(Mesh mesh,Renderer renderer)
    {
        // ⭐ 先检查是否有自定义路径映射（用于简化后的粒子mesh）
        string path;
        int meshInstanceID = mesh.GetInstanceID();
        if (meshInstanceToPath.ContainsKey(meshInstanceID))
        {
            path = meshInstanceToPath[meshInstanceID];
        }
        else
        {
            path = AssetsUtil.GetMeshPath(mesh);
        }

        if (!this.HaveFileData(path))
        {
            this.AddExportFile(new MeshFile(mesh, renderer));
        }
        return this.GetFileData(path) as MeshFile;
    }

    /// <summary>
    /// 注册mesh实例到路径的映射（用于程序生成的mesh，如简化后的粒子mesh）
    /// </summary>
    public void RegisterMeshPath(Mesh mesh, string customPath)
    {
        int meshInstanceID = mesh.GetInstanceID();
        meshInstanceToPath[meshInstanceID] = customPath;
    }

    public MaterialFile GetMaterialFile(Material material, Renderer renderer = null, bool is2DUsage = false, bool isCPUParticle = false)
    {
        if (material == null)
        {
            Debug.LogWarning("LayaAir3D: Material is null, cannot export.");
            return null;
        }

        string path = AssetsUtil.GetMaterialPath(material);
        // CPU particle materials use a separate cache key to allow dual-mode export
        if (isCPUParticle)
            path = path + "#cpu";

        // ★ 检测 renderer 类型冲突：同一材质被不同类型 renderer 使用时，创建变体
        if (renderer != null && this.HaveFileData(path))
        {
            MaterialFile existingFile = this.GetFileData(path) as MaterialFile;
            if (existingFile != null)
            {
                bool isParticleRenderer = renderer is ParticleSystemRenderer;
                bool isMeshRenderer = renderer is MeshRenderer || renderer is SkinnedMeshRenderer;

                // 现有材质是 Mesh 类型，新请求来自粒子 → 创建 _Effect 变体
                if (isParticleRenderer && !existingFile.IsUsedByParticleSystem())
                {
                    string variantPath = GetVariantMaterialPath(path, "_Effect");
                    if (!this.HaveFileData(variantPath))
                    {
                        this.AddExportFile(new MaterialFile(this, material, renderer, variantPath));
                    }
                    return this.GetFileData(variantPath) as MaterialFile;
                }

                // 现有材质是粒子类型，新请求来自 Mesh → 创建 _D3 变体
                if (isMeshRenderer && !existingFile.IsUsedByMeshRenderer())
                {
                    string variantPath = GetVariantMaterialPath(path, "_D3");
                    if (!this.HaveFileData(variantPath))
                    {
                        this.AddExportFile(new MaterialFile(this, material, renderer, variantPath));
                    }
                    return this.GetFileData(variantPath) as MaterialFile;
                }
            }
        }

        // 正常路径（首次创建或同类型复用）
        if (!this.HaveFileData(path))
        {
            this.AddExportFile(new MaterialFile(this, material, renderer, null, is2DUsage, isCPUParticle));
        }
        else
        {
            // Update renderer type information if this material is used by a different renderer type
            MaterialFile existingFile = this.GetFileData(path) as MaterialFile;
            if (existingFile != null)
            {
                if (renderer != null) existingFile.AddRendererUsage(renderer);
                if (is2DUsage) existingFile.MarkAs2DUsage();
            }
        }
        return this.GetFileData(path) as MaterialFile;
    }

    /// <summary>
    /// 为材质路径生成变体路径（在扩展名前插入后缀）
    /// 例如: "Materials/MyMat.lmat" + "_Effect" → "Materials/MyMat_Effect.lmat"
    /// </summary>
    private static string GetVariantMaterialPath(string originalPath, string suffix)
    {
        int dotIndex = originalPath.LastIndexOf('.');
        if (dotIndex >= 0)
            return originalPath.Substring(0, dotIndex) + suffix + originalPath.Substring(dotIndex);
        return originalPath + suffix;
    }

    public TextureFile GetTextureFile(Texture texture, bool isNormal, bool isSpriteTexture = false)
    {
        // 检查纹理是否为空
        if (texture == null)
        {
            return null;
        }

        // 检查是否可以转换为Texture2D
        Texture2D texture2D = texture as Texture2D;
        if (texture2D == null)
        {
            Debug.LogWarning($"LayaAir3D: Texture '{texture.name}' is not a Texture2D, skipping export.");
            return null;
        }

        string picturePath = AssetsUtil.GetTextureFile(texture);

        // Unity 内置资源没有普通的 Assets 路径。已知可安全提取的纹理映射到
        // 导出目录中的稳定虚拟路径，其余内置资源继续跳过。
        if (IsBuiltinResource(picturePath))
        {
            if (!TryGetBuiltinTextureExportPath(texture, out picturePath))
            {
                return null;
            }
        }

        if (!this.HaveFileData(picturePath))
        {
            this.AddExportFile(new TextureFile(picturePath, texture2D, isNormal, isSpriteTexture));
        }
        return this.GetFileData(picturePath) as TextureFile;
    }

    /// <summary>
    /// 将支持导出的 Unity 内置纹理映射到 Laya 输出目录中的资源路径。
    /// 当前只支持 Default-Particle；其他内置纹理不能假定为白纹理。
    /// </summary>
    public static bool TryGetBuiltinTextureExportPath(Texture texture, out string exportPath)
    {
        exportPath = null;
        if (texture == null)
        {
            return false;
        }

        Texture2D defaultParticle = AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd");
        bool isDefaultParticle = defaultParticle != null &&
            defaultParticle.GetInstanceID() == texture.GetInstanceID();

        // 部分 Unity 版本返回的对象实例可能不同，但内置资源名称保持一致。
        if (!isDefaultParticle)
        {
            isDefaultParticle = string.Equals(
                texture.name,
                "Default-Particle",
                System.StringComparison.OrdinalIgnoreCase);
        }

        if (!isDefaultParticle)
        {
            return false;
        }

        exportPath = "Assets/LayaBuiltin/Default-Particle.png";
        return true;
    }
    
    /// <summary>
    /// 检查资源路径是否是 Unity 内置资源
    /// </summary>
    public static bool IsBuiltinResource(string path)
    {
        if (string.IsNullOrEmpty(path))
            return true;
        return path.Contains("unity_builtin_extra") || 
               path.Contains("unity_default_resources") ||
               path.StartsWith("Resources/") && !System.IO.File.Exists(path);
    }

    public AnimationClipFile GetAnimationClipFile(AnimationClip aniclip, GameObject gameObject)
    {
        string laniPath = AssetsUtil.GetAnimationClipPath(aniclip);

        if (!this.HaveFileData(laniPath))
        {
            this.AddExportFile(new AnimationClipFile(aniclip, gameObject));
        }


        return this.GetFileData(laniPath) as AnimationClipFile;

    }
    public void AddExportFile(FileData file)
    {
        file.resoureMap = this;
        exportFiles.Add(file.filePath, file);
    }

    public FileData GetFileData(string path)
    {
        if (!exportFiles.ContainsKey(path))
        {
            return null;
        }
        else
        {
            return exportFiles[path];
        }
    }


    public bool HaveFileData(string path)
    {
        return exportFiles.ContainsKey(path);
    }

    public void RemoveFileData(string path)
    {
        if (exportFiles.ContainsKey(path))
        {
            exportFiles.Remove(path);
        }
    }

    /// <summary>
    /// Collect all atlas UUIDs from exported SpriteAtlasExportFile instances.
    /// Used to populate _$preloads in the scene root so atlases load before prefabs.
    /// </summary>
    public List<string> GetAtlasFileUUIDs()
    {
        List<string> uuids = new List<string>();
        foreach (var kv in exportFiles)
        {
            if (kv.Value is SpriteAtlasExportFile)
                uuids.Add(kv.Value.uuid);
        }
        return uuids;
    }

    public void SaveAllFile()
    {
        // Use a snapshot loop: SaveFile() for AnimationClipFile may call GetMaterialFile()
        // which adds new entries to exportFiles.  Iterating a Dictionary while modifying it
        // throws "Collection was modified".  Process iteratively until no new files appear,
        // matching the pattern already used in createNodeTree().
        var processed = new HashSet<string>();
        bool anyNew;
        do
        {
            anyNew = false;
            var keys = new List<string>(exportFiles.Keys);
            int totalFiles = keys.Count;
            int currentFile = 0;
            foreach (string key in keys)
            {
                if (!processed.Add(key)) continue;  // already saved
                anyNew = true;
                currentFile++;
                // 显示保存进度 (从30%到90%)
                float progress = 0.3f + (0.6f * currentFile / totalFiles);
                EditorUtility.DisplayProgressBar(LanguageConfig.str_LayaAirExport,
                    string.Format(LanguageConfig.str_ExportFile, currentFile, totalFiles, key), progress);
                exportFiles[key].SaveFile(exportFiles);
            }
        } while (anyNew);

        // 清理阶段 (90%到100%)
        EditorUtility.DisplayProgressBar(LanguageConfig.str_LayaAirExport, LanguageConfig.str_ExportCleanup, 0.95f);

        foreach (var file in exportFiles)
        {
            if (file.Value is PerfabFile)
            {
                (file.Value as PerfabFile).destory();
            }
        }
    }

    public void getComponentsData(GameObject gameObject, JSONObject node,NodeMap map)
    {
        // 检查不支持的组件
        UnsupportedFeatureCollector.CheckGameObject(gameObject);

        Camera camera = gameObject.GetComponent<Camera>();
        if (camera != null)
        {
            JsonUtils.getCameraComponentData(gameObject, node);
            node.AddField("_$type", "Camera");
        }
        else
        {
            // SpriteRenderer nodes use UI3D path: stay as Sprite3D with 3D transform,
            // UI3D component in _$comp handles the 2D rendering.
            node.AddField("_$type", "Sprite3D");

            // UI3D nodes: negate localScale.x to compensate for left-hand→right-hand coordinate flip.
            // 3D meshes get this compensation via vertex X-negation during mesh export;
            // UI3D has no exported mesh, so we apply it on the node's transform instead.
            if (gameObject.GetComponent<SpriteRenderer>() != null)
            {
                JSONObject transform = node.GetField("transform");
                if (transform != null)
                {
                    JSONObject localScale = transform.GetField("localScale");
                    if (localScale != null)
                    {
                        float origX = gameObject.transform.localScale.x;
                        localScale.RemoveField("x");
                        localScale.AddField("x", -origX);
                    }
                }
            }
        }

        JSONObject compents = new JSONObject(JSONObject.Type.ARRAY);
        node.AddField("_$comp", compents);
        List<Component> componentsList = new List<Component>();
        gameObject.GetComponents(componentsList);
        foreach(Component comp in componentsList)
        {
            this.writeComponentData(compents, comp, map,false);
        }

    }

    /// <summary>
    /// 写入 UI Image 的 skin、color、useSourceSize 属性
    /// </summary>
    private void WriteImageData(JSONObject node, UnityEngine.UI.Image image)
    {
        // 导出 sprite 纹理 → skin
        if (image.sprite != null && image.sprite.texture != null)
        {
            TextureFile textureFile = this.GetTextureFile(image.sprite.texture, false, true);
            if (textureFile != null)
            {
                node.AddField("skin", "res://" + textureFile.uuid);
            }
        }

        // color → "#rrggbb" 或 "#rrggbbaa"
        Color c = image.color;
        int r = Mathf.RoundToInt(c.r * 255);
        int g = Mathf.RoundToInt(c.g * 255);
        int b = Mathf.RoundToInt(c.b * 255);
        int a = Mathf.RoundToInt(c.a * 255);
        string colorHex;
        if (a < 255)
            colorHex = string.Format("#{0:x2}{1:x2}{2:x2}{3:x2}", r, g, b, a);
        else
            colorHex = string.Format("#{0:x2}{1:x2}{2:x2}", r, g, b);
        node.AddField("color", colorHex);

        // useSourceSize: Simple 模式且 preserveAspect 时为 true
        bool useSourceSize = image.type == UnityEngine.UI.Image.Type.Simple && image.preserveAspect;
        if (useSourceSize)
        {
            node.AddField("useSourceSize", true);
        }
    }

    public void writeComponentData(JSONObject compents,Component comp, NodeMap map, bool isOverride)
    {
        if(comp == null)
        {
            return;
        }
        GameObject gameObject = comp.gameObject;
        if(comp is MeshRenderer)
        {
            compents.Add(this.GetMeshRenderComponmentData(comp as MeshRenderer, isOverride));
        }else if(comp is MeshFilter)
        {
            MeshFilter filter = comp as MeshFilter;
            compents.Add(this.GetMeshFilterComponentData(filter.sharedMesh, gameObject.GetComponent<MeshRenderer>(), isOverride));
        }else if(comp is SkinnedMeshRenderer)
        {
            SkinnedMeshRenderer render = comp as SkinnedMeshRenderer;
            compents.Add(this.GetMeshFilterComponentData(render.sharedMesh, render, isOverride));
            compents.Add(this.GetSkinnerMeshRenderComponmentData(render, map, isOverride));
        }else if(comp is Light)
        {
            compents.Add(this.GetLightComponentData(comp as Light, isOverride));
        }
        else if(comp is Animator)
        {
            Animator anim = comp as Animator;
            // Skip 3D Animator if it only has SpriteRenderer color animation
            // (handled by 2D Animator2D in SpriteRenderer export path)
            bool hasNonColorAnims = !IsSpriteRendererColorOnlyAnimator(anim);
            // Collect child SpriteRenderer color animation targets for Animator2D export
            // (must run before GetAnimatorComponentData so we know if sync script is needed)
            int prevTargetCount = m_spriteColorAnimTargets.Count;
            CollectSpriteColorAnimTargets(anim);
            bool hasChildColorAnims = m_spriteColorAnimTargets.Count > prevTargetCount;

            if (hasNonColorAnims)
            {
                // If both 3D and 2D animations exist, pass sync script UUID to controller export
                string syncScriptUuid = (hasChildColorAnims) ? EnsureAnimator2DSyncScript() : null;
                compents.Add(this.GetAnimatorComponentData(anim, isOverride, syncScriptUuid));
            }
        }else if (comp is ReflectionProbe)
        {
            compents.Add(this.GetReflectionProbe(comp as ReflectionProbe, isOverride));
        }        else if(comp is LODGroup)
        {
            compents.Add(this.GetLodGroup(comp as LODGroup, map, isOverride));
        }
        else if(comp is ParticleSystem)
        {
            int mode = GetParticleExportMode(gameObject);

            // 自动检测日志：当无手动设置且自动切换为 CPU 时，提示用户触发原因
            if (mode == 1 && FindParticleExportSetting(gameObject) < 0)
            {
                ParticleSystem _ps = comp as ParticleSystem;
                if (_ps != null)
                {
                    var reasons = new System.Collections.Generic.List<string>();
                    if (_ps.limitVelocityOverLifetime.enabled) reasons.Add("LimitVelocityOverLifetime");
                    if (_ps.trails.enabled) reasons.Add("Trails");
                    if (_ps.noise.enabled) reasons.Add("Noise");
                    if (reasons.Count > 0)
                    {
                        Debug.Log($"[LayaAir Export] '{gameObject.name}': 检测到 GPU 不支持的模块 [{string.Join(", ", reasons)}]，自动切换为 CPU 粒子导出。");
                    }
                }
            }

            if (mode == 0) // Shuriken (GPU)
            {
                JSONObject particleComp = LayaParticleExportV2.ExportParticleSystemV2(gameObject, this);
                if (particleComp != null)
                {
                    compents.Add(particleComp);
                }
            }
            else if (mode == 1) // CPU Particle
            {
                ParticleSystem ps = comp as ParticleSystem;
                ParticleSystemRenderer psr = gameObject.GetComponent<ParticleSystemRenderer>();
                if (ps != null && psr != null)
                {
                    JSONObject particleSystemData = ParticleSystemData.GetParticleSystem(ps, isOverride, map, this);
                    compents.Add(particleSystemData);
                    ParticleSystemData.GetParticleSystemRenderer(psr, isOverride, this, particleSystemData);
                }
            }
        }
        else if (comp is ParticleSystemRenderer)
        {
            // GPU/CPU 模式均已在 ParticleSystem 阶段完成, 此处跳过
        }
        else if (comp is ParticleSystemForceField)
        {
            // 力场组件仅在 CPU 粒子模式下有意义
            compents.Add(ParticleSystemForceFieldData.GetParticleSystemForceField(
                comp as ParticleSystemForceField, isOverride, map, this));
        }
        else if (comp is SpriteRenderer)
        {
            JSONObject spriteComp = this.GetSpriteRendererComponentData(comp as SpriteRenderer, isOverride);
            if (spriteComp != null)
            {
                compents.Add(spriteComp);
            }
        }
        else if (comp is MonoBehaviour)
        {
            string typeName = comp.GetType().Name;
            if (GameObjectUitls.HasComponentScriptMapping(typeName))
            {
                string uuid = GameObjectUitls.GetComponentIdentifier(typeName);
                JSONObject scriptComp = new JSONObject(JSONObject.Type.OBJECT);
                JsonUtils.SetComponentsType(scriptComp, uuid, isOverride);
                // 导出脚本属性数据
                SerializeMonoBehaviourProperties(scriptComp, comp as MonoBehaviour, typeName);
                compents.Add(scriptComp);
                ExportLogger.Log($"LayaAir3D: Exported custom script '{typeName}' → UUID: {uuid}");
            }
        }
    }

    /// <summary>
    /// 序列化MonoBehaviour的可序列化属性到JSON
    /// 根据属性名映射配置，将Unity属性名转换为LayaAir属性名
    /// </summary>
    private void SerializeMonoBehaviourProperties(JSONObject scriptComp, MonoBehaviour mono, string typeName)
    {
        if (mono == null) return;

        // 获取属性名映射（可能为null，表示使用原名导出所有属性）
        Dictionary<string, string> propertyMapping = GameObjectUitls.GetComponentPropertyMapping(typeName);

        SerializedObject so = new SerializedObject(mono);
        SerializedProperty prop = so.GetIterator();
        bool enterChildren = true;
        while (prop.NextVisible(enterChildren))
        {
            enterChildren = false;
            // 跳过Unity内置字段
            if (prop.name == "m_Script" || prop.name == "m_ObjectHideFlags" ||
                prop.name == "m_Enabled")
                continue;

            string unityName = prop.name;
            string layaName;

            if (propertyMapping != null)
            {
                // 有属性映射配置：只导出映射表中列出的属性
                if (!propertyMapping.TryGetValue(unityName, out layaName))
                    continue;
            }
            else
            {
                // 无属性映射配置：使用原名导出所有可序列化属性
                layaName = unityName;
            }

            SerializeProperty(scriptComp, layaName, prop);
        }
    }

    /// <summary>
    /// 将单个SerializedProperty序列化为JSON字段
    /// </summary>
    private void SerializeProperty(JSONObject json, string name, SerializedProperty prop)
    {
        switch (prop.propertyType)
        {
            case SerializedPropertyType.Float:
                json.AddField(name, prop.floatValue);
                break;
            case SerializedPropertyType.Integer:
                json.AddField(name, prop.intValue);
                break;
            case SerializedPropertyType.Boolean:
                json.AddField(name, prop.boolValue);
                break;
            case SerializedPropertyType.String:
                json.AddField(name, prop.stringValue);
                break;
            case SerializedPropertyType.Enum:
                json.AddField(name, prop.enumValueIndex);
                break;
            case SerializedPropertyType.Color:
                json.AddField(name, JsonUtils.GetColorObject(prop.colorValue));
                break;
            case SerializedPropertyType.Vector2:
            {
                Vector2 v = prop.vector2Value;
                JSONObject vec = new JSONObject(JSONObject.Type.OBJECT);
                vec.AddField("_$type", "Vector2");
                vec.AddField("x", v.x);
                vec.AddField("y", v.y);
                json.AddField(name, vec);
                break;
            }
            case SerializedPropertyType.Vector3:
                json.AddField(name, JsonUtils.GetVector3Object(prop.vector3Value));
                break;
            case SerializedPropertyType.Vector4:
            {
                Vector4 v = prop.vector4Value;
                JSONObject vec = new JSONObject(JSONObject.Type.OBJECT);
                vec.AddField("_$type", "Vector4");
                vec.AddField("x", v.x);
                vec.AddField("y", v.y);
                vec.AddField("z", v.z);
                vec.AddField("w", v.w);
                json.AddField(name, vec);
                break;
            }
            case SerializedPropertyType.Quaternion:
                json.AddField(name, JsonUtils.GetQuaternionObject(prop.quaternionValue));
                break;
            case SerializedPropertyType.Rect:
            {
                Rect r = prop.rectValue;
                JSONObject rect = new JSONObject(JSONObject.Type.OBJECT);
                rect.AddField("x", r.x);
                rect.AddField("y", r.y);
                rect.AddField("width", r.width);
                rect.AddField("height", r.height);
                json.AddField(name, rect);
                break;
            }
            default:
                // 不支持的类型静默跳过
                break;
        }
    }

    public JSONObject GetLightComponentData(Light light,bool isOverride)
    {
        if (light.type == LightType.Directional)
        {
            return JsonUtils.GetDirectionalLightComponentData(light, isOverride);
        }
        else if (light.type == LightType.Point)
        {
            return JsonUtils.GetPointLightComponentData(light, isOverride);
        }
        else if (light.type == LightType.Spot)
        {
            return JsonUtils.GetSpotLightComponentData(light, isOverride);
        }
        else
        {
            return null;
        }
    }
    public JSONObject GetMeshFilterComponentData(Mesh mesh, Renderer render,bool isOverride)
    {
        JSONObject compData = new JSONObject(JSONObject.Type.OBJECT);
        if (isOverride)
        {
            compData.AddField("_$override", "MeshFilter");
        }
        else
        {
            compData.AddField("_$type", "MeshFilter");
        }
       
        if (mesh != null)
        {
            MeshFile meshFile = this.GetMeshFile(mesh, render);
            JSONObject meshFiledata = new JSONObject(JSONObject.Type.OBJECT);
            meshFiledata.AddField("_$uuid", meshFile.uuid);
            meshFiledata.AddField("_$type", "Mesh");
            compData.AddField("sharedMesh", meshFiledata);
        }

        return compData;

    }
    public JSONObject GetSkinnerMeshRenderComponmentData(SkinnedMeshRenderer skinnedMeshRenderer, NodeMap map,bool isOverride)
    {
        Material[] materials = skinnedMeshRenderer.sharedMaterials;
        JSONObject sharedMaterials = new JSONObject(JSONObject.Type.ARRAY);
        GameObject gameObject = skinnedMeshRenderer.gameObject;
        for (var i = 0; i < materials.Length; i++)
        {
            sharedMaterials.Add(this.GetMaterialData(materials[i], skinnedMeshRenderer));
        }

        JSONObject compData = new JSONObject(JSONObject.Type.OBJECT);
        compData.AddField("_$type", "SkinnedMeshRenderer");
        compData.AddField("sharedMaterials", sharedMaterials);
        compData.AddField("enabled", skinnedMeshRenderer.enabled);
        compData.AddField("receiveShadow", skinnedMeshRenderer.receiveShadows);
        compData.AddField("castShadow", skinnedMeshRenderer.shadowCastingMode == ShadowCastingMode.On);

        // localBounds for SkinnedMeshRenderer — X-axis flip for left-hand to right-hand conversion
        {
            Bounds bounds = skinnedMeshRenderer.localBounds;
            JSONObject boundBoxNode = new JSONObject(JSONObject.Type.OBJECT);
            compData.AddField("localBounds", boundBoxNode);
            boundBoxNode.AddField("_$type", "Bounds");

            Vector3 oriCenter = bounds.center;
            Vector3 center = new Vector3(-oriCenter.x, oriCenter.y, oriCenter.z);
            Vector3 extents = bounds.extents;
            Vector3 min = center - extents;
            Vector3 max = center + extents;

            boundBoxNode.AddField("min", JsonUtils.GetVector3Object(min));
            boundBoxNode.AddField("max", JsonUtils.GetVector3Object(max));
        }

        JSONObject bones = new JSONObject(JSONObject.Type.ARRAY);
        compData.AddField("_bones", bones);
        Transform[] bonesTransform = skinnedMeshRenderer.bones;
        for (int i = 0; i < bonesTransform.Length; i++)
        {
            bones.Add(map.getRefNodeIdObjet(bonesTransform[i].gameObject));
        }

        if (skinnedMeshRenderer.rootBone)
        {
            compData.AddField("rootBone", map.getRefNodeIdObjet(skinnedMeshRenderer.rootBone.gameObject));
        }
        return compData;
    }


    public JSONObject GetMeshRenderComponmentData(MeshRenderer render, bool isOverride)
    {
        GameObject gameObject = render.gameObject;

        Material[] materials = render.sharedMaterials;
        JSONObject sharedMaterials = new JSONObject(JSONObject.Type.ARRAY);
        for (var i = 0; i < materials.Length; i++)
        {
            Material mat = materials[i];
            if (mat == null) {
                Debug.LogWarningFormat(gameObject, "LayaAir3D Warning(Code:1002) : " + gameObject.name + "'s MeshRender Component materials data can't be null!");
            } else {
                sharedMaterials.Add(this.GetMaterialData(mat, render));
            }
        }

        JSONObject compData = JsonUtils.SetComponentsType(new JSONObject(JSONObject.Type.OBJECT), "MeshRenderer", isOverride);
        compData.AddField("enabled", render.enabled);
        compData.AddField("sharedMaterials", sharedMaterials);
        compData.AddField("receiveShadow", render.receiveShadows);
        compData.AddField("castShadow", render.shadowCastingMode == ShadowCastingMode.On);
        return compData;
    }


    public JSONObject GetLodGroup(LODGroup lodGroup, NodeMap map, bool isOverride)
    {
        JSONObject compData = JsonUtils.SetComponentsType(new JSONObject(JSONObject.Type.OBJECT), "LODGroup", isOverride);
        compData.AddField("enabled", lodGroup.enabled);
        JSONObject lodDatas = new JSONObject(JSONObject.Type.ARRAY);
        LOD[] lods = lodGroup.GetLODs();
        for (var i = 0; i < lods.Length; i++)
        {
            LOD lod = lods[i];
            JSONObject lodData = new JSONObject(JSONObject.Type.OBJECT);
            lodData.AddField("_$type", "LODInfo");
            lodData.AddField("mincullRate", lod.screenRelativeTransitionHeight);
            JSONObject renderDatas = new JSONObject(JSONObject.Type.ARRAY);
            Renderer[] renders = lod.renderers;
            for (var j = 0; j < renders.Length; j++)
            {
                if (renders[j])
                    renderDatas.Add(map.getRefNodeIdObjet(renders[j].gameObject));
            }
            lodData.AddField("renders", renderDatas);
            lodDatas.Add(lodData);
        }

        compData.AddField("lods", lodDatas);
        return compData;
    }
    public JSONObject GetReflectionProbe(ReflectionProbe probe, bool isOverride)
    {
        GameObject gameObject = probe.gameObject;
        Matrix4x4 matirx = gameObject.transform.worldToLocalMatrix;

        Vector4 helpVec = new Vector4(0, 0, 0, 1);

        JSONObject compData = JsonUtils.SetComponentsType(new JSONObject(JSONObject.Type.OBJECT), "ReflectionProbe", isOverride);
        Vector3 min = probe.bounds.min;
        helpVec.Set(min.x, min.y, min.z, 1);
        helpVec = matirx * helpVec;
        min.Set(helpVec.x, helpVec.y, helpVec.z);
        compData.AddField("boundsMin", JsonUtils.GetVector3Object(min));
        Vector3 max = probe.bounds.max;
        helpVec.Set(max.x, max.y, max.z, 1);
        helpVec = matirx * helpVec;
        max.Set(helpVec.x, helpVec.y, helpVec.z);
        compData.AddField("boundsMax", JsonUtils.GetVector3Object(max));
        compData.AddField("importance", probe.importance);
        compData.AddField("cullingMask", 2147483647);
        compData.AddField("boxProjection", probe.boxProjection);
        compData.AddField("nearPlane", probe.nearClipPlane);
        compData.AddField("farPlane", probe.farClipPlane);
        compData.AddField("ambientColor", JsonUtils.GetColorObject(Color.black));
        compData.AddField("clearFlag", probe.clearFlags == ReflectionProbeClearFlags.Skybox ? 1 : 0);
        compData.AddField("resolution", probe.resolution);
        compData.AddField("_reflectionsIblSamples", 128);
        compData.AddField("enabled", probe.enabled);
        return compData;
    }
    /// <summary>
    /// 将 Unity SpriteRenderer 导出为 Laya UI3D 组件。
    /// UI3D 引用一个内嵌 Image 的 2D 预制体，Image 的 skin 指向导出的 sprite 纹理。
    /// </summary>
    /// <summary>
    /// 检查SpriteRenderer/Image是否使用了自定义材质（非Unity默认Sprite材质）
    /// </summary>
    private static bool IsCustomSpriteMaterial(Material material)
    {
        if (material == null) return false;
        string shaderName = material.shader.name;
        // Unity 默认 Sprite shader 不需要导出
        return shaderName != "Sprites/Default"
            && shaderName != "UI/Default"
            && shaderName != "Hidden/InternalErrorShader"
            && !string.IsNullOrEmpty(shaderName);
    }

    public JSONObject GetSpriteRendererComponentData(SpriteRenderer spriteRenderer, bool isOverride)
    {
        Sprite sprite = spriteRenderer.sprite;
        if (sprite == null)
        {
            Debug.LogWarning($"LayaAir3D: SpriteRenderer on '{spriteRenderer.gameObject.name}' has no sprite, skipping UI3D export.");
            return null;
        }

        // 在触发任何资产管线操作之前，立即缓存所有 Sprite 属性。
        // GetTextureFile / AssetDatabase 调用可能触发重导入，导致原始 Sprite 原生对象
        // 被销毁，之后再访问 sprite.rect / sprite.bounds 等属性就会报 "destroyed" 错误。
        Texture2D    spriteTexture  = sprite.texture;
        Rect         spriteRect     = sprite.rect;
        Bounds       spriteBounds   = sprite.bounds;
        string       spriteName     = sprite.name;
        SpriteDrawMode drawMode     = spriteRenderer.drawMode;
        Vector2      rendererSize   = spriteRenderer.size;
        Color        spriteColor    = spriteRenderer.color;
        int          texFullWidth   = spriteTexture.width;
        int          texFullHeight  = spriteTexture.height;

        if (spriteTexture == null)
        {
            Debug.LogWarning($"LayaAir3D: Sprite '{spriteName}' on '{spriteRenderer.gameObject.name}' has no texture, skipping UI3D export.");
            return null;
        }

        // 导出 sprite 源纹理（isSpriteTexture=true：生成 Laya 精灵纹理 meta {textureType:2}）
        TextureFile textureFile = this.GetTextureFile(spriteTexture, false, isSpriteTexture: true);
        if (textureFile == null)
        {
            Debug.LogWarning($"LayaAir3D: Cannot export texture for sprite '{spriteName}' on '{spriteRenderer.gameObject.name}'.");
            return null;
        }

        // ★ 检测并导出自定义材质（非默认Sprite shader）
        Material sharedMaterial = spriteRenderer.sharedMaterial;
        MaterialFile materialFile = null;
        if (IsCustomSpriteMaterial(sharedMaterial))
        {
            materialFile = this.GetMaterialFile(sharedMaterial, spriteRenderer);
            ExportLogger.Log($"LayaAir3D: SpriteRenderer '{spriteRenderer.gameObject.name}' uses custom shader '{sharedMaterial.shader.name}', exporting material.");
        }

        // Sprite 像素尺寸（在图集中的 rect，使用缓存值）
        int pixelWidth  = Mathf.RoundToInt(spriteRect.width);
        int pixelHeight = Mathf.RoundToInt(spriteRect.height);
        if (pixelWidth < 1) pixelWidth = 1;
        if (pixelHeight < 1) pixelHeight = 1;

        // Check for color animation on this SpriteRenderer — export as Animator2D with full state machine
        // Source 1: Animator on the same GameObject (binding.path == "")
        // Source 2: Ancestor Animator targeting this child (binding.path != "", collected in m_spriteColorAnimTargets)
        // NOTE: Animation detection must happen BEFORE prefab key generation so we can
        //       include a variant suffix — two SpriteRenderers sharing the same sprite but
        //       with different colors or different AnimatorControllers need separate prefabs.
        AnimatorController ac = null;
        string colorTargetPath = "";
        GameObject animatorGO = spriteRenderer.gameObject;
        bool hasColorAnim = false;

        // Check same-GO Animator first
        Animator animator = spriteRenderer.GetComponent<Animator>();
        if (animator != null)
        {
            ac = animator.runtimeAnimatorController as AnimatorController;
            colorTargetPath = "";
            animatorGO = spriteRenderer.gameObject;
        }
        // Check pre-collected sub-object targets from ancestor Animators
        if (ac == null && m_spriteColorAnimTargets.ContainsKey(spriteRenderer.gameObject))
        {
            var target = m_spriteColorAnimTargets[spriteRenderer.gameObject];
            ac = target.controller;
            colorTargetPath = target.targetPath;
            animatorGO = target.animatorGameObject;
        }

        if (ac != null)
        {
            hasColorAnim = AnimatorController2DFile.HasAnySpriteColorCurves(ac, colorTargetPath);
            if (!hasColorAnim)
            {
                Debug.Log($"[LayaAir Export 2D] '{spriteRenderer.gameObject.name}': AnimatorController '{ac.name}' has no SpriteRenderer m_Color curves for path '{colorTargetPath}'");
            }
        }
        else
        {
            Debug.Log($"[LayaAir Export 2D] '{spriteRenderer.gameObject.name}': No color animation source found");
        }

        // 创建或获取 2D Image 预制体文件（使用缓存的纹理和名称）
        // Compute a variant suffix so that SpriteRenderers with the same sprite but different
        // colors or different AnimatorControllers get separate .lh / .mcc files.
        string texturePath     = AssetsUtil.GetTextureFile(spriteTexture);
        string variantSuffix   = GetSpriteVariantSuffix(spriteRenderer.gameObject, animatorGO, spriteColor, hasColorAnim ? ac : null);
        string spritePrefabKey = GetSpritePrefabVirtualPath(texturePath, spriteName, variantSuffix);

        // Build animation data if color curves exist
        JSONObject animationData = null;
        if (hasColorAnim && ac != null)
        {
            // Build .mcc path alongside the sprite prefab .lh
            string mccPath = spritePrefabKey.Replace(".lh", ".mcc");

            if (!this.HaveFileData(mccPath))
            {
                this.AddExportFile(new AnimatorController2DFile(mccPath, ac,
                    animatorGO, this, colorTargetPath));
            }
            AnimatorController2DFile ctrlFile = this.GetFileData(mccPath) as AnimatorController2DFile;

            if (ctrlFile != null)
            {
                animationData = new JSONObject(JSONObject.Type.OBJECT);
                animationData.AddField("_$type", "Animator2D");
                JSONObject ctrlRef = new JSONObject(JSONObject.Type.OBJECT);
                ctrlRef.AddField("_$uuid", ctrlFile.uuid);
                ctrlRef.AddField("_$type", "AnimatorController2D");
                animationData.AddField("controller", ctrlRef);
                Debug.Log($"[LayaAir Export 2D] '{spriteRenderer.gameObject.name}': Animator2D exported, controller UUID={ctrlFile.uuid}, mccPath={mccPath}, targetPath='{colorTargetPath}'");
            }
        }

        // Detect whether the sprite is a sub-region of its texture (spritesheet/atlas case).
        // If so, export a .atlas file and use sub-texture URL; otherwise use texture UUID directly.
        bool isSubRegion = !(Mathf.Approximately(spriteRect.x, 0f) && Mathf.Approximately(spriteRect.y, 0f)
                           && Mathf.Approximately(spriteRect.width, texFullWidth)
                           && Mathf.Approximately(spriteRect.height, texFullHeight));

        // Generate or retrieve the default baseRender2D material for Mesh2DRender
        string defaultMatPath = "Assets/Mesh2DRender_DefaultMaterial.lmat";
        if (!this.HaveFileData(defaultMatPath))
        {
            this.AddExportFile(new Mesh2DDefaultMaterialFile());
        }
        Mesh2DDefaultMaterialFile matFile = this.GetFileData(defaultMatPath) as Mesh2DDefaultMaterialFile;
        string materialUUID = matFile != null ? matFile.uuid : null;

        if (!this.HaveFileData(spritePrefabKey))
        {
            if (isSubRegion)
            {
                // Atlas/spritesheet: create or reuse a SpriteAtlasExportFile for this texture
                SpriteAtlasExportFile atlasFile = GetOrCreateSpriteAtlas(texturePath, textureFile, texFullHeight);
                atlasFile.AddFrame(spriteName, spriteRect, texFullHeight,
                                   new Vector2Int(pixelWidth, pixelHeight), Vector2Int.zero);

                string subTextureRef = atlasFile.GetSubTextureRef(spriteName);
                this.AddExportFile(new UI2DPrefabFile(spritePrefabKey, subTextureRef,
                                                      atlasFile.uuid,
                                                      spriteName, pixelWidth, pixelHeight,
                                                      spriteColor, materialUUID, animationData));

            }
            else
            {
                // Full texture: use texture UUID directly
                this.AddExportFile(new UI2DPrefabFile(spritePrefabKey, textureFile,
                                                      spriteName, pixelWidth, pixelHeight,
                                                      spriteColor, materialUUID, animationData));
            }
        }
        else if (isSubRegion)
        {
            // Prefab already exists, but make sure the atlas frame is registered
            SpriteAtlasExportFile atlasFile = GetOrCreateSpriteAtlas(texturePath, textureFile, texFullHeight);
            if (!atlasFile.HasFrame(spriteName))
            {
                atlasFile.AddFrame(spriteName, spriteRect, texFullHeight,
                                   new Vector2Int(pixelWidth, pixelHeight), Vector2Int.zero);
            }
        }
        UI2DPrefabFile ui2DPrefab = this.GetFileData(spritePrefabKey) as UI2DPrefabFile;

        // 世界空间尺寸：Simple 模式用 sprite 自然边界（像素数 / pixelsPerUnit），其他模式用 size 属性
        float scaleX, scaleY;
        if (drawMode == SpriteDrawMode.Simple)
        {
            scaleX = spriteBounds.size.x;
            scaleY = spriteBounds.size.y;
        }
        else
        {
            scaleX = rendererSize.x;
            scaleY = rendererSize.y;
        }
        if (scaleX < 0.0001f) scaleX = 0.0001f;
        if (scaleY < 0.0001f) scaleY = 0.0001f;

        // 渲染分辨率（每世界单位的像素数），确保不低于 1
        int resolutionRate = Mathf.Max(1, Mathf.RoundToInt(pixelWidth / scaleX));

        // 构建 UI3D 组件 JSON
        JSONObject compData = JsonUtils.SetComponentsType(new JSONObject(JSONObject.Type.OBJECT), "UI3D", isOverride);

        // lightmapScaleOffset（必填，默认 Vector4）
        JSONObject lightmap = new JSONObject(JSONObject.Type.OBJECT);
        lightmap.AddField("_$type", "Vector4");
        compData.AddField("lightmapScaleOffset", lightmap);

        // prefab 引用
        JSONObject prefabRef = new JSONObject(JSONObject.Type.OBJECT);
        prefabRef.AddField("_$uuid", ui2DPrefab.uuid);
        prefabRef.AddField("_$type", "Prefab");
        compData.AddField("prefab", prefabRef);

        compData.AddField("cameraSpace", false);
        compData.AddField("resolutionRate", resolutionRate);

        JSONObject scaleVec = new JSONObject(JSONObject.Type.OBJECT);
        scaleVec.AddField("_$type", "Vector2");
        scaleVec.AddField("x", scaleX);
        scaleVec.AddField("y", scaleY);
        compData.AddField("scale", scaleVec);

        compData.AddField("billboard", false);
        compData.AddField("cull", 0); // RenderState.CULL_NONE
        compData.AddField("renderMode", PropDatasConfig.DetectTransparentRenderMode(sharedMaterial));

        return compData;
    }

    /// <summary>
    /// Ensure the Animator2DSync runtime script is registered for export.
    /// Returns the script's UUID for use in component JSON (_$type).
    /// </summary>
    private string EnsureAnimator2DSyncScript()
    {
        if (m_syncScriptFile == null)
        {
            m_syncScriptFile = new RuntimeScriptFile("Animator2DSync.ts");
            AddExportFile(m_syncScriptFile);
        }
        return m_syncScriptFile.uuid;
    }

    private string GetSpritePrefabVirtualPath(string texturePath, string spriteName, string variantSuffix = "")
    {
        string texName = System.IO.Path.GetFileNameWithoutExtension(texturePath);
        string cleanName = GameObjectUitls.cleanIllegalChar(spriteName, true);
        string dir = string.IsNullOrEmpty(m_sceneDir) ? "_ui2d_prefabs" : m_sceneDir + "/_ui2d_prefabs";
        if (string.IsNullOrEmpty(variantSuffix))
            return dir + "/" + texName + "_" + cleanName + "_sprite.lh";
        return dir + "/" + texName + "_" + cleanName + "_" + variantSuffix + "_sprite.lh";
    }

    /// <summary>
    /// Compute a variant suffix for a SpriteRenderer so that instances with different
    /// initial colors or different AnimatorControllers produce separate prefab files.
    /// Returns "" when the sprite uses default white color and has no color animation.
    /// Uses the Animator node name as suffix (in prefabs, Animator is typically at or near root).
    /// </summary>
    private string GetSpriteVariantSuffix(GameObject spriteGO, GameObject animatorGO, Color color, AnimatorController ac)
    {
        bool isDefaultColor = Mathf.Approximately(color.r, 1f) && Mathf.Approximately(color.g, 1f)
                           && Mathf.Approximately(color.b, 1f) && Mathf.Approximately(color.a, 1f);
        bool hasAnim = ac != null;

        if (isDefaultColor && !hasAnim)
            return "";

        // Use the Animator node name — short and unique enough within a prefab
        string nodeName = (hasAnim && animatorGO != null) ? animatorGO.name : spriteGO.name;
        return GameObjectUitls.cleanIllegalChar(nodeName, true);
    }

    /// <summary>
    /// Get or create a SpriteAtlasExportFile for the given texture.
    /// All sprites from the same texture share one .atlas file.
    /// </summary>
    /// <param name="texturePath">Unity asset path of the texture (e.g. "Assets/Textures/mySheet.png")</param>
    /// <param name="textureFile">The exported TextureFile for this texture</param>
    /// <param name="texFullHeight">Full texture height (for Y-flip in AddFrame)</param>
    private SpriteAtlasExportFile GetOrCreateSpriteAtlas(string texturePath, TextureFile textureFile, int texFullHeight)
    {
        // Atlas path: replace texture extension with .atlas
        int dotIndex = texturePath.LastIndexOf('.');
        string basePath = dotIndex >= 0 ? texturePath.Substring(0, dotIndex) : texturePath;
        string atlasPath = basePath + ".atlas";

        if (!this.HaveFileData(atlasPath))
        {
            // Texture output filename: the TextureFile may change extension (e.g. .png -> .jpg)
            string textureFileName = System.IO.Path.GetFileName(textureFile.outPath);
            SpriteAtlasExportFile atlasFile = new SpriteAtlasExportFile(atlasPath, textureFileName);
            this.AddExportFile(atlasFile);

            // 补全图集：加载该纹理上的所有 Sprite 子区域，而不仅仅是场景中引用到的
            Object[] allAssets = AssetDatabase.LoadAllAssetsAtPath(texturePath);
            foreach (Object asset in allAssets)
            {
                Sprite sp = asset as Sprite;
                if (sp == null) continue;
                // 跳过占据整张纹理的 Sprite（不属于子区域）
                Rect spRect = sp.rect;
                if (Mathf.Approximately(spRect.x, 0f) && Mathf.Approximately(spRect.y, 0f)
                    && Mathf.Approximately(spRect.width, sp.texture.width)
                    && Mathf.Approximately(spRect.height, sp.texture.height))
                    continue;
                if (!atlasFile.HasFrame(sp.name))
                {
                    int pw = Mathf.RoundToInt(spRect.width);
                    int ph = Mathf.RoundToInt(spRect.height);
                    if (pw < 1) pw = 1;
                    if (ph < 1) ph = 1;
                    atlasFile.AddFrame(sp.name, spRect, texFullHeight,
                                       new Vector2Int(pw, ph), Vector2Int.zero);
                }
            }
        }

        return this.GetFileData(atlasPath) as SpriteAtlasExportFile;
    }

    /// <summary>
    /// Ensure a FileConfigFile is registered in the export files.
    /// Called when at least one atlas is created, so fileconfig.json will be generated.
    /// </summary>
    private void EnsureFileConfigFile()
    {
        string fileConfigPath = "fileconfig.json";
        if (!this.HaveFileData(fileConfigPath))
        {
            this.AddExportFile(new FileConfigFile());
        }
    }

    /// <summary>
    /// Collect child SpriteRenderer color animation targets from an Animator's clips.
    /// For each binding.path that targets a SpriteRenderer m_Color curve,
    /// resolve the child GameObject and store in m_spriteColorAnimTargets.
    /// </summary>
    private void CollectSpriteColorAnimTargets(Animator animator)
    {
        AnimatorController controller = animator.runtimeAnimatorController as AnimatorController;
        if (controller == null) return;

        HashSet<string> processedPaths = new HashSet<string>();
        foreach (AnimationClip clip in controller.animationClips)
        {
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.type != typeof(SpriteRenderer)) continue;
                if (!binding.propertyName.StartsWith("m_Color")) continue;
                if (string.IsNullOrEmpty(binding.path)) continue; // Same-GO handled in GetSpriteRendererComponentData
                if (processedPaths.Contains(binding.path)) continue;
                processedPaths.Add(binding.path);

                Transform childTransform = animator.gameObject.transform.Find(binding.path);
                if (childTransform == null)
                {
                    Debug.LogWarning($"[LayaAir Export 2D] Cannot resolve animation target path '{binding.path}' from '{animator.gameObject.name}'");
                    continue;
                }
                GameObject childGO = childTransform.gameObject;
                if (!m_spriteColorAnimTargets.ContainsKey(childGO))
                {
                    m_spriteColorAnimTargets[childGO] = new SpriteColorAnimTarget
                    {
                        controller = controller,
                        targetPath = binding.path,
                        animatorGameObject = animator.gameObject
                    };
                }
            }
        }
    }

    /// <summary>
    /// Returns true if this Animator only contains SpriteRenderer m_Color curves
    /// and should be skipped for 3D Animator export (handled by 2D Animator2D instead).
    /// </summary>
    private static bool IsSpriteRendererColorOnlyAnimator(Animator animator)
    {
        if (animator.GetComponent<SpriteRenderer>() == null) return false;

        AnimatorController controller = animator.runtimeAnimatorController as AnimatorController;
        if (controller == null) return false;

        foreach (AnimationClip clip in controller.animationClips)
        {
            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
            foreach (EditorCurveBinding binding in bindings)
            {
                // If there's any curve that is NOT a SpriteRenderer m_Color curve,
                // this animator has other animations too — keep the 3D Animator
                if (!(binding.type == typeof(SpriteRenderer) && binding.propertyName.StartsWith("m_Color")))
                {
                    return false;
                }
            }
            // Also check object reference curves (material swaps, etc.)
            EditorCurveBinding[] objRefBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
            if (objRefBindings.Length > 0) return false;
        }
        return true;
    }

    public JSONObject GetAnimatorComponentData(Animator animator, bool isOverride, string syncScriptUuid = null)
    {
        GameObject gameObject = animator.gameObject;
        AnimatorController animatorController = (AnimatorController)animator.runtimeAnimatorController;

        if (animatorController == null)
        {
            Debug.LogWarningFormat(animator, "LayaAir3D Warning(Code:1002) : " + gameObject.name + "'s Animator Compoment must have a Controller!");
            return null;
        }
        string animatorControllerPath = AssetsUtil.GetAnimatorControllerPath(animatorController);
        JsonFile controlFile;
        if (!this.HaveFileData(animatorControllerPath))
        {
            controlFile = new JsonFile(animatorControllerPath, new JSONObject(JSONObject.Type.OBJECT));
            JSONObject controllData = controlFile.jsonData;
            JsonUtils.SetComponentsType(controllData, "Animator", isOverride);
            controllData.AddField("enabled", true);
            controllData.AddField("controller", "null");
            controllData.AddField("cullingMode", 0);
            JSONObject controllerLayers = new JSONObject(JSONObject.Type.ARRAY);
            AnimatorControllerLayer[] layers = animatorController.layers;
            int layerLength = layers.Length;
            for (var i = 0; i < layerLength; i++)
            {
                controllerLayers.Add(GetAnimaterLayerData(layers[i], gameObject, i == 0, syncScriptUuid));
            }
            controllData.AddField("controllerLayers", controllerLayers);
            this.AddExportFile(controlFile);
        }
        else
        {
            controlFile = this.GetFileData(animatorControllerPath) as JsonFile;
        }

        JSONObject compData = new JSONObject(JSONObject.Type.OBJECT);
        compData.AddField("_$type", "Animator");
        JSONObject controller = new JSONObject(JSONObject.Type.OBJECT);
        compData.AddField("controller", controller);
        compData.AddField("enabled", animator.enabled);
        controller.AddField("_$type", "AnimationController");
        controller.AddField("_$uuid", controlFile.uuid);

        return compData;
    }


    private JSONObject GetAnimaterLayerData(AnimatorControllerLayer layer, GameObject gameObject, bool isbaseLayer, string syncScriptUuid = null)
    {
        JSONObject layarNode = new JSONObject(JSONObject.Type.OBJECT);
        layarNode.AddField("_$type", "AnimatorControllerLayer");
        layarNode.AddField("playOnWake", true);
        layarNode.AddField("name", layer.name);
        if (isbaseLayer)
        {
            layarNode.AddField("defaultWeight", 1);
        }
        else
        {
            layarNode.AddField("defaultWeight", layer.defaultWeight);
        }

        if (layer.blendingMode == AnimatorLayerBlendingMode.Override)
        {
            layarNode.AddField("blendingMode", 0);
        }
        else if (layer.blendingMode == AnimatorLayerBlendingMode.Additive)
        {
            layarNode.AddField("blendingMode", 1);
        }
        else
        {
            layarNode.AddField("blendingMode", 0);
        }
        AnimatorStateMachine stateMachine = layer.stateMachine;
        ChildAnimatorState[] states = stateMachine.states;
        JSONObject statuesNode = new JSONObject(JSONObject.Type.ARRAY);
        layarNode.AddField("states", statuesNode);
        Dictionary<string, int> stateMap = new Dictionary<string, int>();
        for (int i = 0; i < states.Length; i++)
        {
            stateMap.Add(states[i].state.name, i);
        }

        for (int i = 0; i < states.Length; i++)
        {
            Vector3 postion = states[i].position;
            AnimatorState state = states[i].state;

            JSONObject statueNode = new JSONObject(JSONObject.Type.OBJECT);
            statuesNode.Add(statueNode);
            statueNode.AddField("_$type", "AnimatorState");
            statueNode.AddField("name", state.name);
            statueNode.AddField("speed", state.speed);
            statueNode.AddField("clipStart", 0);
            statueNode.AddField("clipEnd", 1);
            statueNode.AddField("x", postion.x);
            statueNode.AddField("y", postion.y);

            AnimationClip clip = state.motion as AnimationClip;
            if (clip != null)
            {
                JSONObject clipData = new JSONObject(JSONObject.Type.OBJECT);
                clipData.AddField("_$type", "AnimationClip");
                AnimationClipFile laniFile = GetAnimationClipFile(clip, gameObject);
                clipData.AddField("_$uuid", laniFile.uuid);
                statueNode.AddField("clip", clipData);
            } else {
                Debug.LogErrorFormat(gameObject, gameObject.name + " have empty or not  AnimationClip " + state.name);
            }
            statueNode.AddField("id", stateMap[state.name].ToString());

            // Animator2DSync: attach state script to sync Animator2D playback
            if (syncScriptUuid != null && clip != null)
            {
                JSONObject scriptsArray = new JSONObject(JSONObject.Type.ARRAY);
                scriptsArray.Add("res://" + syncScriptUuid);
                statueNode.AddField("scripts", scriptsArray);
            }

            AnimatorStateTransition[] transitions = state.transitions;
            if (transitions.Length > 0)
            {
                JSONObject solotrans = new JSONObject(JSONObject.Type.ARRAY);
                statueNode.AddField("soloTransitions", solotrans);
                for (int j = 0; j < transitions.Length; j++)
                {
                    AnimatorStateTransition transition = transitions[j];

                    // Check if destinationState is valid
                    if (transition.destinationState == null)
                    {
                        Debug.LogWarning($"[LayaAir Export] State '{state.name}' has a transition with null destinationState, skipping");
                        continue;
                    }

                    // Check if destinationState exists in stateMap
                    if (!stateMap.ContainsKey(transition.destinationState.name))
                    {
                        Debug.LogWarning($"[LayaAir Export] State '{state.name}' has a transition to unknown state '{transition.destinationState.name}', skipping");
                        continue;
                    }

                    JSONObject solotran = new JSONObject(JSONObject.Type.OBJECT);
                    solotrans.Add(solotran);
                    solotran.AddField("id", stateMap[transition.destinationState.name].ToString());
                    solotran.AddField("exitByTime", transition.hasExitTime);
                    solotran.AddField("exitTime", transition.exitTime);
                    solotran.AddField("transduration", transition.duration);
                    solotran.AddField("transstartoffset", transition.offset);
                    if (transition.solo)
                    {
                        solotran.AddField("solo", true);
                    }
                    if (transition.mute)
                    {
                        solotran.AddField("mute", true);
                    }
                }
            }

        }

        Vector3 enterPostion = stateMachine.entryPosition;
        JSONObject enterNode = new JSONObject(JSONObject.Type.OBJECT);
        statuesNode.Add(enterNode);
        enterNode.AddField("id", "-1");
        enterNode.AddField("name", "进入");
        enterNode.AddField("speed", 1);
        enterNode.AddField("clipEnd", 1);
        enterNode.AddField("x", enterPostion.x);
        enterNode.AddField("y", enterPostion.y);
        JSONObject soloTransitions = new JSONObject(JSONObject.Type.ARRAY);
        if (stateMachine.entryTransitions.Length > 0)
        {
            for (int j = 0; j < stateMachine.entryTransitions.Length; j++)
            {
                AnimatorTransition transition = stateMachine.entryTransitions[j];

                // Check if destinationState is valid
                if (transition.destinationState == null)
                {
                    Debug.LogWarning($"[LayaAir Export] Entry transition has null destinationState, skipping");
                    continue;
                }

                // Check if destinationState exists in stateMap
                if (!stateMap.ContainsKey(transition.destinationState.name))
                {
                    Debug.LogWarning($"[LayaAir Export] Entry transition points to unknown state '{transition.destinationState.name}', skipping");
                    continue;
                }

                JSONObject soloTransition = new JSONObject(JSONObject.Type.OBJECT);
                soloTransition.AddField("id", stateMap[transition.destinationState.name].ToString());
                soloTransitions.Add(soloTransition);
            }
        }
        else
        {
            if (stateMachine.defaultState != null)
            {
                // Check if defaultState exists in stateMap
                if (stateMap.ContainsKey(stateMachine.defaultState.name))
                {
                    JSONObject soloTransition = new JSONObject(JSONObject.Type.OBJECT);
                    soloTransition.AddField("id", stateMap[stateMachine.defaultState.name].ToString());
                    soloTransitions.Add(soloTransition);
                }
                else
                {
                    Debug.LogWarning($"[LayaAir Export] Default state '{stateMachine.defaultState.name}' not found in stateMap");
                }
            }

        }
        enterNode.AddField("soloTransitions", soloTransitions);

        Vector3 anyPostion = stateMachine.anyStatePosition;
        JSONObject anyNode = new JSONObject(JSONObject.Type.OBJECT);
        statuesNode.Add(anyNode);
        anyNode.AddField("id", "-2");
        anyNode.AddField("name", "任何状态");
        anyNode.AddField("speed", 1);
        anyNode.AddField("clipEnd", 1);
        anyNode.AddField("x", anyPostion.x);
        anyNode.AddField("y", anyPostion.y);

        if (stateMachine.anyStateTransitions.Length > 0)
        {
            soloTransitions = new JSONObject(JSONObject.Type.ARRAY);

            for (int j = 0; j < stateMachine.anyStateTransitions.Length; j++)
            {
                JSONObject soloTransition = new JSONObject(JSONObject.Type.OBJECT);
                AnimatorStateTransition anytransition = stateMachine.anyStateTransitions[j];
                soloTransition.AddField("id", stateMap[anytransition.destinationState.name].ToString());
                soloTransition.AddField("exitByTime", anytransition.hasExitTime);
                soloTransition.AddField("exitTime", anytransition.exitTime);
                soloTransition.AddField("transduration", anytransition.duration);
                soloTransitions.Add(soloTransition);
            }
            anyNode.AddField("soloTransitions", soloTransitions);
        }


        return layarNode;
    }

    
    public void GetSHOrigin(JSONObject shObj)
    {
        SphericalHarmonicsL2 sh = RenderSettings.ambientProbe;
        for (var i = 0; i < 3; i++)
        {
            shObj.Add(sh[i, 0]); shObj.Add(sh[i, 1]); shObj.Add(sh[i, 2]); shObj.Add(sh[i, 3]);
            shObj.Add(sh[i, 4]); shObj.Add(sh[i, 5]); shObj.Add(sh[i, 6]); shObj.Add(sh[i, 7]);
            shObj.Add(sh[i, 8]);
        }
    }

    public JSONObject GetMaterialData(Material material, Renderer renderer = null, bool isCPUParticle = false)
    {
        JSONObject materFiledata = new JSONObject(JSONObject.Type.OBJECT);
        materFiledata.AddField("_$type", "Material");
        if (material != null)
        {
            MaterialFile jsonFile = this.GetMaterialFile(material, renderer, isCPUParticle: isCPUParticle);
            if (jsonFile != null)
            {
                materFiledata.AddField("_$uuid", jsonFile.uuid);
            }
        }
        return materFiledata;
    }

    /// <summary>
    /// 获取 Mesh 的 JSON 引用数据 (uuid), CPU 粒子导出需要
    /// </summary>
    public JSONObject GetMeshData(Mesh mesh, Renderer renderer)
    {
        MeshFile meshFile = this.GetMeshFile(mesh, renderer);
        JSONObject meshData = new JSONObject(JSONObject.Type.OBJECT);
        meshData.AddField("_$uuid", meshFile.uuid);
        meshData.AddField("_$type", "Mesh");
        return meshData;
    }

    /// <summary>
    /// 获取指定 GameObject 的粒子导出模式
    /// 优先查找当前节点, 再向上查找父节点的 LayaParticleExportSetting 组件,
    /// 都没有则使用全局默认。
    /// 使用类型名称匹配 + 反射读取，避免 Editor/Runtime 程序集不匹配导致 GetComponent 失败。
    /// </summary>
    private static int GetParticleExportMode(GameObject gameObject)
    {
        int result = FindParticleExportSetting(gameObject);
        if (result >= 0) return result;

        // 向上查找父节点
        Transform parent = gameObject.transform.parent;
        while (parent != null)
        {
            result = FindParticleExportSetting(parent.gameObject);
            if (result >= 0) return result;
            parent = parent.parent;
        }

        // 自动检测：GPU 不支持的模块 → 强制 CPU
        if (RequiresCPUParticle(gameObject))
            return 1;

        return ExportConfig.ParticleExportMode;
    }

    /// <summary>
    /// 通过类型名称查找 LayaParticleExportSetting 并用反射读取 exportMode，
    /// 避免 Editor 与 Runtime 程序集类型不一致导致 GetComponent&lt;T&gt; 返回 null 的问题。
    /// 返回 -1 表示未找到。
    /// </summary>
    /// <summary>
    /// 检测粒子系统是否使用了 GPU 导出不支持的模块（LimitVelocityOverLifetime / Trails / Noise），
    /// 若是则需要强制使用 CPU 粒子导出。
    /// </summary>
    private static bool RequiresCPUParticle(GameObject gameObject)
    {
        ParticleSystem ps = gameObject.GetComponent<ParticleSystem>();
        if (ps == null) return false;
        if (ps.limitVelocityOverLifetime.enabled) return true;
        if (ps.trails.enabled) return true;
        if (ps.noise.enabled) return true;
        return false;
    }

    private static int FindParticleExportSetting(GameObject go)
    {
        foreach (var comp in go.GetComponents<MonoBehaviour>())
        {
            if (comp != null && comp.GetType().Name == "LayaParticleExportSetting")
            {
                var field = comp.GetType().GetField("exportMode");
                if (field != null)
                {
                    return (int)field.GetValue(comp);
                }
            }
        }
        return -1;
    }
}
