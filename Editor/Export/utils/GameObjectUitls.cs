using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public enum ComponentType
{
    Transform = 0,
    Camera = 1,
    DirectionalLight = 2,
    PointLight = 3,
    SpotLight = 4,
    MeshFilter = 5,
    MeshRenderer = 6,
    SkinnedMeshRenderer = 7,
    Animator = 8,
    ParticleSystem = 9,
    Terrain = 10,
    PhysicsCollider = 11,
    Rigidbody3D = 12,
    TrailRenderer = 13,
    LineRenderer = 14,
    Fixedjoint = 15,
    ConfigurableJoint = 16,
    ReflectionProbe = 17,
    LodGroup = 18,
    Animation = 19,
}



class GameObjectUitls
{
    public static Dictionary<string, string> searchCompoment;
    private static Dictionary<string, string> componentScriptUuidMapping;
    // 组件属性名映射：组件类型名 → (Unity属性名 → LayaAir属性名)
    private static Dictionary<string, Dictionary<string, string>> componentScriptPropertyMapping;

    public static void init()
    {
        if (searchCompoment == null)
        {
            searchCompoment = new Dictionary<string, string>();
            searchCompoment.Add("UnityEngine.GameObject", "");
            searchCompoment.Add("UnityEngine.Transform", "transform");
            searchCompoment.Add("UnityEngine.MeshRenderer", "MeshRenderer");
            searchCompoment.Add("UnityEngine.SkinnedMeshRenderer", "SkinnedMeshRenderer");
            searchCompoment.Add("UnityEngine.ParticleSystemRenderer", "particleRenderer");
            searchCompoment.Add("UnityEngine.TrailRenderer", "trailRenderer");
            searchCompoment.Add("UnityEngine.Camera", "");
        }

        // 加载组件UUID映射配置
        LoadComponentScriptUuidMapping();
    }

    /// <summary>
    /// 加载Unity组件类型到LayaAir脚本UUID的映射配置
    /// </summary>
    private static void LoadComponentScriptUuidMapping()
    {
        // 每次都重新加载，确保获取最新配置
        componentScriptUuidMapping = new Dictionary<string, string>();
        componentScriptPropertyMapping = new Dictionary<string, Dictionary<string, string>>();

        string mappingPath = Path.Combine(Application.dataPath, "LayaAir3.0UnityPlugin/Editor/Mappings/component_script_uuid_mapping.json");

        // 如果文件不存在，自动创建默认配置文件
        if (!File.Exists(mappingPath))
        {
            CreateDefaultComponentMappingFile(mappingPath);
            return; // 首次创建，返回空映射
        }

        try
        {
            string jsonContent = File.ReadAllText(mappingPath);
            JSONObject jsonObj = new JSONObject(jsonContent);

            JSONObject mappings = jsonObj.GetField("mappings");
            if (mappings != null && mappings.keys != null && mappings.keys.Count > 0)
            {
                foreach (string key in mappings.keys)
                {
                    JSONObject field = mappings.GetField(key);
                    if (field == null) continue;

                    // 旧格式：值为字符串UUID
                    if (field.IsString && !string.IsNullOrEmpty(field.str))
                    {
                        componentScriptUuidMapping[key] = field.str;
                    }
                    // 新格式：值为对象 { "uuid": "...", "properties": { "unityName": "layaName" } }
                    else if (field.IsObject)
                    {
                        JSONObject uuidObj = field.GetField("uuid");
                        if (uuidObj != null && uuidObj.IsString && !string.IsNullOrEmpty(uuidObj.str))
                        {
                            componentScriptUuidMapping[key] = uuidObj.str;
                        }

                        JSONObject propsObj = field.GetField("properties");
                        if (propsObj != null && propsObj.IsObject && propsObj.keys != null)
                        {
                            var propMap = new Dictionary<string, string>();
                            foreach (string propKey in propsObj.keys)
                            {
                                JSONObject propVal = propsObj.GetField(propKey);
                                if (propVal != null && propVal.IsString)
                                {
                                    propMap[propKey] = propVal.str;
                                }
                            }
                            if (propMap.Count > 0)
                            {
                                componentScriptPropertyMapping[key] = propMap;
                            }
                        }
                    }
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[LayaAir Export] Failed to load component UUID mapping: {e.Message}");
        }
    }

    /// <summary>
    /// 创建默认的组件UUID映射配置文件
    /// </summary>
    private static void CreateDefaultComponentMappingFile(string filePath)
    {
        try
        {
            // 确保目录存在
            string directory = Path.GetDirectoryName(filePath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // 创建默认配置内容
            JSONObject jsonObj = new JSONObject(JSONObject.Type.OBJECT);
            jsonObj.AddField("_comment", "Unity组件类型到LayaAir脚本UUID的映射配置");

            JSONObject instructionsArray = new JSONObject(JSONObject.Type.ARRAY);
            instructionsArray.Add("如何获取LayaAir脚本的UUID：");
            instructionsArray.Add("1. 在LayaAir项目的src目录中找到对应的TypeScript脚本文件（例如：LookAtConstraint.ts）");
            instructionsArray.Add("2. 找到同名的.meta文件（例如：LookAtConstraint.ts.meta）");
            instructionsArray.Add("3. 打开.meta文件，复制其中的uuid值");
            instructionsArray.Add("4. 使用菜单 LayaAir > 组件脚本导出配置 打开配置面板添加映射");
            jsonObj.AddField("_instructions", instructionsArray);

            // 创建空的mappings对象
            JSONObject mappingsObj = new JSONObject(JSONObject.Type.OBJECT);
            jsonObj.AddField("mappings", mappingsObj);

            // 写入文件
            File.WriteAllText(filePath, jsonObj.Print(true));

            ExportLogger.Log($"[LayaAir Export] Created default component UUID mapping file at: {filePath}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[LayaAir Export] Failed to create default component UUID mapping file: {e.Message}");
        }
    }

    /// <summary>
    /// 获取组件在LayaAir中的标识符（UUID或类型名）
    /// </summary>
    public static string GetComponentIdentifier(string componentTypeName)
    {
        if (componentScriptUuidMapping != null && componentScriptUuidMapping.ContainsKey(componentTypeName))
        {
            return componentScriptUuidMapping[componentTypeName];
        }
        return componentTypeName;
    }
    public static bool HasComponentScriptMapping(string componentTypeName)
    {
        return componentScriptUuidMapping != null &&
               componentScriptUuidMapping.ContainsKey(componentTypeName);
    }

    /// <summary>
    /// 获取组件的属性名映射（Unity属性名 → LayaAir属性名）
    /// 如果没有配置属性映射，返回null
    /// </summary>
    public static Dictionary<string, string> GetComponentPropertyMapping(string componentTypeName)
    {
        if (componentScriptPropertyMapping != null &&
            componentScriptPropertyMapping.ContainsKey(componentTypeName))
        {
            return componentScriptPropertyMapping[componentTypeName];
        }
        return null;
    }

    private static string LaniVersion = "LAYAANIMATION:WEIGHT_05";
    public static Color EncodeRGBM(Color color, float maxRGBM)
    {
        float kOneOverRGBMMaxRange = 1.0f / maxRGBM;
        const float kMinMultiplier = 2.0f * 1e-2f;

        Color rgb = color * kOneOverRGBMMaxRange;
        float alpha = Math.Max(Math.Max(rgb.r, rgb.g), Math.Max(rgb.b, kMinMultiplier));
        alpha = ((float)Math.Ceiling(alpha * 255.0f)) / 255.0f;

        // Division-by-zero warning from d3d9, so make compiler happy.
        alpha = Math.Max(alpha, kMinMultiplier);

        return new Color(rgb.r / alpha, rgb.g / alpha, rgb.b / alpha, alpha);
    }

    public static bool isCameraOrLight(GameObject gameObject)
    {
        if (gameObject.GetComponent<Camera>() != null)
        {
            return true;
        }
        else if (gameObject.GetComponent<Light>() != null)
        {
            return true;
        }
        else
        {
            return false;
        }
    }


    /// <summary>
    /// Splits Unity's final linear HDR color into a normalized linear color and
    /// the linear multiplier expected by Laya materials.
    ///
    /// Unity's HDR picker represents intensity as exposure stops:
    ///     linear HDR = base linear color * pow(2, exposure EV)
    /// Laya shaders multiply color by a linear intensity, so the exported
    /// intensity must be pow(2, EV), not the EV value itself.
    /// </summary>
    public static void DecomposeHdrColor(Color linearColorHdr, out Color baseLinearColor, out float intensity)
    {
        float maxColorComponent = Mathf.Max(
            linearColorHdr.r,
            Mathf.Max(linearColorHdr.g, linearColorHdr.b));

        if (maxColorComponent <= 0f)
        {
            baseLinearColor = new Color(0f, 0f, 0f, linearColorHdr.a);
            intensity = 1f;
            return;
        }

        // Material.GetColor already returns Unity's final linear HDR value.
        // Recover an equivalent EV and immediately convert it to the linear
        // multiplier consumed by Laya. This keeps the round trip exact while
        // avoiding a second HDR/gamma conversion in the generated shader.
        float exposureEv = Mathf.Log(maxColorComponent, 2f);
        intensity = Mathf.Pow(2f, exposureEv);
        float inverseIntensity = 1f / intensity;
        baseLinearColor = new Color(
            linearColorHdr.r * inverseIntensity,
            linearColorHdr.g * inverseIntensity,
            linearColorHdr.b * inverseIntensity,
            linearColorHdr.a);
    }


    public static string cleanIllegalChar(string str, bool heightLevel)
    {
        str = str.Replace("<", "_");
        str = str.Replace(">", "_");
        str = str.Replace("\"", "_");
        str = str.Replace("|", "_");
        str = str.Replace("?", "_");
        str = str.Replace("*", "_");
        str = str.Replace("#", "_");
        if (heightLevel)
        {
            str = str.Replace("/", "_");
            str = str.Replace(":", "_");
        }
        return str;
    }
    private static AnimationCurveGroup readTransfromAnimation(EditorCurveBinding binding, GameObject gameObject, object targetObject, string path, string propertyName)
    {
        KeyFrameValueType keyType;
        string propNames = "";
        string property = propertyName.Split('.')[0];
        if (property == "m_LocalPosition")
        {
            propNames = "localPosition";
            keyType = KeyFrameValueType.Position;
        }
        else if (property == "m_LocalRotation")
        {
            propNames = "localRotation";
            keyType = KeyFrameValueType.Rotation;
        }
        else if (property == "m_LocalScale")
        {
            propNames = "localScale";
            keyType = KeyFrameValueType.Scale;
        }
        else if (property == "localEulerAnglesRaw")
        {
            propNames = "localRotationEuler";
            keyType = KeyFrameValueType.RotationEuler;
        }
        else if (property == "m_IsActive")
        {
            propNames = "active";
            keyType = KeyFrameValueType.Float;
        }
        else
        {
            return null;
        }
        string conpomentType = searchCompoment[binding.type.ToString()];
        AnimationCurveGroup curveGroup = new AnimationCurveGroup(path, gameObject, binding.type, conpomentType, propertyName, keyType);
        curveGroup.propnames.Add(propNames);
        return curveGroup;
    }
    private static AnimationCurveGroup readCameraAnimation(EditorCurveBinding binding, GameObject gameObject, object targetObject, string path, string propertyName)
    {
        KeyFrameValueType keyType = KeyFrameValueType.Float;
        string propNames = "";
        string property = propertyName.Split('.')[0];

        // Unity Camera 属性映射到 LayaAir Camera 属性
        if (property == "field of view" || property == "m_FieldOfView")
        {
            propNames = "fieldOfView";
        }
        else if (property == "near clip plane" || property == "m_NearClipPlane")
        {
            propNames = "nearPlane";
        }
        else if (property == "far clip plane" || property == "m_FarClipPlane")
        {
            propNames = "farPlane";
        }
        else if (property == "orthographic size" || property == "m_OrthographicSize")
        {
            propNames = "orthographicVerticalSize";
        }
        else
        {
            // 其他Camera属性，通过反射获取类型
            try
            {
                PropertyInfo propInfo = typeof(Camera).GetProperty(property);
                FieldInfo fieldInfo = typeof(Camera).GetField(property);

                Type memberType = null;
                if (propInfo != null)
                {
                    memberType = propInfo.PropertyType;
                }
                else if (fieldInfo != null)
                {
                    memberType = fieldInfo.FieldType;
                }

                if (memberType != null)
                {
                    // 根据类型确定关键帧类型
                    if (memberType == typeof(float) || memberType == typeof(int) || memberType == typeof(bool))
                    {
                        keyType = KeyFrameValueType.Float;
                    }
                    else if (memberType == typeof(Vector2))
                    {
                        keyType = KeyFrameValueType.Vector2;
                    }
                    else if (memberType == typeof(Vector3))
                    {
                        keyType = KeyFrameValueType.Vector3;
                    }
                    else if (memberType == typeof(Vector4))
                    {
                        keyType = KeyFrameValueType.Vector4;
                    }
                    else if (memberType == typeof(Color))
                    {
                        keyType = KeyFrameValueType.Color;
                    }
                    else
                    {
                        Debug.LogWarning($"[LayaAir Export] Unsupported Camera property type: {property} ({memberType.Name})");
                        return null;
                    }

                    // 使用Unity原始属性名
                    propNames = property;
                }
                else
                {
                    Debug.LogWarning($"[LayaAir Export] Cannot find Camera property: {property}");
                    return null;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[LayaAir Export] Error analyzing Camera property: {property} - {e.Message}");
                return null;
            }
        }

        // Camera属性直接在节点上，组件类型为空字符串
        string conpomentType = searchCompoment[binding.type.ToString()];
        AnimationCurveGroup curveGroup = new AnimationCurveGroup(path, gameObject, binding.type, conpomentType, propertyName, keyType);
        curveGroup.propnames.Add(propNames);

        return curveGroup;
    }
    /// <summary>
    /// Parse a Unity Renderer material animation binding property name.
    /// Unity format: "material.SHADER_PROP[.COMPONENT]"  (slot 0)
    ///               "materials[N].SHADER_PROP[.COMPONENT]"  (slot N)
    /// Returns the shader property name (e.g. "_Color") and the material slot index.
    /// Returns null if the property name is not a recognized material-property format.
    /// </summary>
    private static string ParseMaterialBindingProperty(string bindingPropertyName, out int matSlot)
    {
        matSlot = 0;
        string raw = bindingPropertyName;

        if (raw.StartsWith("material."))
        {
            raw = raw.Substring("material.".Length);
        }
        else if (raw.StartsWith("materials["))
        {
            int close = raw.IndexOf(']');
            if (close < 0 || raw.Length <= close + 1 || raw[close + 1] != '.') return null;
            string slotStr = raw.Substring("materials[".Length, close - "materials[".Length);
            int.TryParse(slotStr, out matSlot);
            raw = raw.Substring(close + 2);
        }
        else
        {
            return null;
        }

        // Strip trailing single-char component suffix (.r .g .b .a .x .y .z .w)
        int lastDot = raw.LastIndexOf('.');
        if (lastDot >= 0)
        {
            string suffix = raw.Substring(lastDot + 1);
            if (suffix.Length == 1 && "rgbaxyzw".IndexOf(suffix[0]) >= 0)
                raw = raw.Substring(0, lastDot);
        }
        return raw; // e.g. "_Color", "_MainTex_ST", "_Metallic"
    }

    /// <summary>
    /// Compute a unique groupMap key for a Renderer material property binding.
    /// Ensures each (objectPath, material slot, shader property) combination gets its own group,
    /// which is critical for float properties that share a common dot-prefix.
    /// </summary>
    private static string GetMaterialAnimationGroupPath(string objectPath, string bindingPropertyName)
    {
        int matSlot;
        string shaderProp = ParseMaterialBindingProperty(bindingPropertyName, out matSlot);
        if (shaderProp == null) return null;
        string slotTag = matSlot == 0 ? "material" : "materials[" + matSlot + "]";
        return (string.IsNullOrEmpty(objectPath) ? "" : objectPath + ".") + slotTag + "." + shaderProp;
    }

    private static AnimationCurveGroup readMaterAnimation(EditorCurveBinding binding, GameObject gameObject, object targetObject, string path, string propertyName)
    {
        if (targetObject == null)
        {
            Debug.LogWarning($"[LayaAir Export] readMaterAnimation: targetObject is null for path '{path}', property '{propertyName}'");
            return null;
        }

        // Parse shader property name and material slot from the binding property name.
        // Unity format: "material.SHADER_PROP[.COMPONENT]" or "materials[N].SHADER_PROP[.COMPONENT]"
        int matSlot;
        string shaderPropName = ParseMaterialBindingProperty(binding.propertyName, out matSlot);
        if (shaderPropName == null) return null;

        // Get the material at the correct slot (supports multi-material renderers)
        Renderer renderer = targetObject as Renderer;
        if (renderer == null)
        {
            Debug.LogWarning($"[LayaAir Export] readMaterAnimation: targetObject is not a Renderer (path '{path}')");
            return null;
        }
        Material[] mats = renderer.sharedMaterials;
        if (mats == null || mats.Length == 0) return null;
        if (matSlot >= mats.Length) matSlot = 0;
        Material material = mats[matSlot];
        if (material == null)
        {
            Debug.LogWarning($"[LayaAir Export] readMaterAnimation: sharedMaterials[{matSlot}] is null (path '{path}')");
            return null;
        }

        PropDatasConfig propsData = MetarialUitls.getMetarialConfig(material.shader.name);
        if (propsData == null) return null;

        // Look up Laya property name using the extracted shader property name
        string layaPropName;
        KeyFrameValueType keyType;
        if (propsData.floatLists.ContainsKey(shaderPropName))
        {
            layaPropName = propsData.floatLists[shaderPropName].keyName;
            keyType = KeyFrameValueType.Float;
        }
        else if (propsData.colorLists.ContainsKey(shaderPropName))
        {
            layaPropName = propsData.colorLists[shaderPropName];
            keyType = KeyFrameValueType.Color;
        }
        else if (propsData.tillOffsetLists.ContainsKey(shaderPropName))
        {
            layaPropName = propsData.tillOffsetLists[shaderPropName];
            keyType = KeyFrameValueType.Vector4;
        }
        else
        {
            return null;
        }

        string conpomentType = searchCompoment[binding.type.ToString()];
        AnimationCurveGroup curveGroup = new AnimationCurveGroup(path, gameObject, binding.type, conpomentType, propertyName, keyType);
        curveGroup.propnames.Add("sharedMaterials");
        curveGroup.propnames.Add(matSlot.ToString()); // use the parsed material slot
        curveGroup.propnames.Add(layaPropName);
        return curveGroup;
    }
    private static AnimationCurveGroup readScriptAnimation(EditorCurveBinding binding, GameObject gameObject, object targetObject, string path, string propertyName)
    {
        // 自定义脚本组件的动画
        // targetObject 是 MonoBehaviour 实例
        if (targetObject == null)
        {
            return null;
        }

        // 获取脚本类型名称（不包含命名空间，只使用类名）
        string scriptTypeName = targetObject.GetType().Name;

        // 解析属性名和类型
        string propName = propertyName.Split('.')[0];
        KeyFrameValueType keyType = KeyFrameValueType.Float; // 默认类型

        try
        {
            // 通过反射获取属性或字段的类型（包括私有和非公开成员）
            BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            PropertyInfo propInfo = targetObject.GetType().GetProperty(propName, bindingFlags);
            FieldInfo fieldInfo = targetObject.GetType().GetField(propName, bindingFlags);

            Type memberType = null;
            if (propInfo != null)
            {
                memberType = propInfo.PropertyType;
            }
            else if (fieldInfo != null)
            {
                memberType = fieldInfo.FieldType;
            }

            if (memberType != null)
            {
                // 根据成员类型确定关键帧类型
                if (memberType == typeof(float) || memberType == typeof(int) || memberType == typeof(bool))
                {
                    keyType = KeyFrameValueType.Float;
                }
                else if (memberType == typeof(Vector2))
                {
                    keyType = KeyFrameValueType.Vector2;
                }
                else if (memberType == typeof(Vector3))
                {
                    keyType = KeyFrameValueType.Vector3;
                }
                else if (memberType == typeof(Vector4))
                {
                    keyType = KeyFrameValueType.Vector4;
                }
                else if (memberType == typeof(Color))
                {
                    keyType = KeyFrameValueType.Color;
                }
                else
                {
                    // 不支持的类型，使用默认Float类型
                    Debug.LogWarning($"[LayaAir Export] Unsupported animation property type: {scriptTypeName}.{propName} ({memberType.Name}), using Float as default");
                    keyType = KeyFrameValueType.Float;
                }
            }
            else
            {
                // 无法通过反射找到字段，根据属性名推断类型
                if (propertyName.Contains(".x") || propertyName.Contains(".y") || propertyName.Contains(".z"))
                {
                    keyType = KeyFrameValueType.Vector3;
                }
                else if (propertyName.Contains(".r") || propertyName.Contains(".g") || propertyName.Contains(".b") || propertyName.Contains(".a"))
                {
                    keyType = KeyFrameValueType.Color;
                }
                else if (propertyName.Contains(".w"))
                {
                    keyType = KeyFrameValueType.Vector4;
                }
                else
                {
                    // 默认使用Float类型
                    keyType = KeyFrameValueType.Float;
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[LayaAir Export] Error analyzing script property: {scriptTypeName}.{propName} - {e.Message}");
            // 发生异常时，使用默认Float类型而不是返回null
            keyType = KeyFrameValueType.Float;
        }

        // 使用脚本UUID或类型名作为组件标识符
        // 优先使用UUID（如果在映射表中），否则使用类型名
        string componentIdentifier = GetComponentIdentifier(scriptTypeName);

        AnimationCurveGroup curveGroup = new AnimationCurveGroup(path, gameObject, binding.type, componentIdentifier, propertyName, keyType);
        curveGroup.propnames.Add(propName);

        return curveGroup;
    }
    #region ParticleSystem Animation Mapping

    /// <summary>
    /// Unity ParticleSystem 属性 → Laya ShurikenParticleRenderer 属性路径映射
    /// </summary>
    private class ParticlePropertyMapping
    {
        public string[] layaPropertyPath;
        public KeyFrameValueType keyType;
        public ParticlePropertyMapping(string[] path, KeyFrameValueType type)
        {
            this.layaPropertyPath = path;
            this.keyType = type;
        }
    }

    private static Dictionary<string, ParticlePropertyMapping> _particlePropertyMappings;

    private static void InitParticlePropertyMappings()
    {
        if (_particlePropertyMappings != null) return;
        _particlePropertyMappings = new Dictionary<string, ParticlePropertyMapping>();

        // InitialModule (Unity Main module 的序列化名)
        _particlePropertyMappings["InitialModule.startLifetime"] = new ParticlePropertyMapping(
            new[] { "_particleSystem", "startLifetimeConstant" }, KeyFrameValueType.Float);
        _particlePropertyMappings["InitialModule.startSpeed"] = new ParticlePropertyMapping(
            new[] { "_particleSystem", "startSpeedConstant" }, KeyFrameValueType.Float);
        _particlePropertyMappings["InitialModule.startSize"] = new ParticlePropertyMapping(
            new[] { "_particleSystem", "startSizeConstant" }, KeyFrameValueType.Float);
        _particlePropertyMappings["InitialModule.startSizeY"] = new ParticlePropertyMapping(
            new[] { "_particleSystem", "startSizeConstantSeparate" }, KeyFrameValueType.Vector3);
        _particlePropertyMappings["InitialModule.startSizeZ"] = new ParticlePropertyMapping(
            new[] { "_particleSystem", "startSizeConstantSeparate" }, KeyFrameValueType.Vector3);
        _particlePropertyMappings["InitialModule.startRotation"] = new ParticlePropertyMapping(
            new[] { "_particleSystem", "startRotationConstant" }, KeyFrameValueType.Float);
        _particlePropertyMappings["InitialModule.startColor.maxColor"] = new ParticlePropertyMapping(
            new[] { "_particleSystem", "startColorConstant" }, KeyFrameValueType.Color);
        _particlePropertyMappings["InitialModule.startColor.minColor"] = new ParticlePropertyMapping(
            new[] { "_particleSystem", "startColorConstantMin" }, KeyFrameValueType.Color);
        _particlePropertyMappings["InitialModule.startColor"] = new ParticlePropertyMapping(
            new[] { "_particleSystem", "startColorConstant" }, KeyFrameValueType.Color);
        _particlePropertyMappings["InitialModule.gravityModifier"] = new ParticlePropertyMapping(
            new[] { "_particleSystem", "gravityModifier" }, KeyFrameValueType.Float);

        // EmissionModule
        _particlePropertyMappings["EmissionModule.rateOverTime"] = new ParticlePropertyMapping(
            new[] { "_particleSystem", "emission", "emissionRate" }, KeyFrameValueType.Float);
        _particlePropertyMappings["EmissionModule.rateOverDistance"] = new ParticlePropertyMapping(
            new[] { "_particleSystem", "emission", "emissionRateOverDistance" }, KeyFrameValueType.Float);

        // MainModule 直接属性
        _particlePropertyMappings["MainModule.simulationSpeed"] = new ParticlePropertyMapping(
            new[] { "_particleSystem", "simulationSpeed" }, KeyFrameValueType.Float);

        // VelocityModule
        _particlePropertyMappings["VelocityModule.x"] = new ParticlePropertyMapping(
            new[] { "_particleSystem", "velocityOverLifetime", "_constantX" }, KeyFrameValueType.Float);
        _particlePropertyMappings["VelocityModule.y"] = new ParticlePropertyMapping(
            new[] { "_particleSystem", "velocityOverLifetime", "_constantY" }, KeyFrameValueType.Float);
        _particlePropertyMappings["VelocityModule.z"] = new ParticlePropertyMapping(
            new[] { "_particleSystem", "velocityOverLifetime", "_constantZ" }, KeyFrameValueType.Float);

        // RotationModule
        _particlePropertyMappings["RotationModule.z"] = new ParticlePropertyMapping(
            new[] { "_particleSystem", "rotationOverLifetime", "_constantZ" }, KeyFrameValueType.Float);
        _particlePropertyMappings["RotationModule.x"] = new ParticlePropertyMapping(
            new[] { "_particleSystem", "rotationOverLifetime", "_constantX" }, KeyFrameValueType.Float);
        _particlePropertyMappings["RotationModule.y"] = new ParticlePropertyMapping(
            new[] { "_particleSystem", "rotationOverLifetime", "_constantY" }, KeyFrameValueType.Float);

        // ShapeModule
        _particlePropertyMappings["ShapeModule.radius"] = new ParticlePropertyMapping(
            new[] { "_particleSystem", "shape", "radius" }, KeyFrameValueType.Float);
        _particlePropertyMappings["ShapeModule.angle"] = new ParticlePropertyMapping(
            new[] { "_particleSystem", "shape", "angle" }, KeyFrameValueType.Float);
    }

    /// <summary>
    /// 处理 ParticleSystem 动画属性，映射到 Laya ShurikenParticleRenderer 的属性路径
    /// </summary>
    private static AnimationCurveGroup readParticleSystemAnimation(EditorCurveBinding binding, GameObject gameObject, object targetObject, string path, string propertyName)
    {
        InitParticlePropertyMappings();

        // 查找匹配的映射（前缀匹配）
        ParticlePropertyMapping mapping = null;
        string matchedKey = null;
        foreach (var kvp in _particlePropertyMappings)
        {
            if (propertyName.StartsWith(kvp.Key))
            {
                // 确保是完整前缀匹配（后面是 '.' 或结尾）
                if (propertyName.Length == kvp.Key.Length || propertyName[kvp.Key.Length] == '.')
                {
                    // 优先匹配最长的 key
                    if (matchedKey == null || kvp.Key.Length > matchedKey.Length)
                    {
                        mapping = kvp.Value;
                        matchedKey = kvp.Key;
                    }
                }
            }
        }

        if (mapping == null)
        {
            Debug.LogWarning($"[LayaAir Export] Unsupported ParticleSystem animation property: '{propertyName}', skipping.");
            return null;
        }

        AnimationCurveGroup curveGroup = new AnimationCurveGroup(
            path, gameObject, binding.type,
            "ShurikenParticleRenderer",
            propertyName,
            mapping.keyType
        );

        // 设置 Laya 属性路径（多段路径）
        foreach (string segment in mapping.layaPropertyPath)
        {
            curveGroup.propnames.Add(segment);
        }

        // LayaAir 粒子系统颜色属性类型为 Vector4，需覆盖输出 type byte
        if (mapping.keyType == KeyFrameValueType.Color)
        {
            curveGroup.SetOutputTypeOverride(KeyFrameValueType.Vector4);
        }

        return curveGroup;
    }

    #endregion

    private static AnimationCurveGroup readComponentAnimation(EditorCurveBinding binding, GameObject gameObject, object targetObject, string path, string propertyName)
    {
        // 处理Unity内置组件（Light、ParticleSystem、AudioSource等）的动画属性
        if (targetObject == null)
        {
            return null;
        }

        // 获取组件类型名称
        string componentTypeName = binding.type.Name;

        // 解析属性名
        string propName = propertyName.Split('.')[0];
        KeyFrameValueType keyType = KeyFrameValueType.Float; // 默认类型

        try
        {
            // 通过反射获取属性或字段的类型（包括私有和非公开成员）
            BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            PropertyInfo propInfo = binding.type.GetProperty(propName, bindingFlags);
            FieldInfo fieldInfo = binding.type.GetField(propName, bindingFlags);

            Type memberType = null;
            if (propInfo != null)
            {
                memberType = propInfo.PropertyType;
            }
            else if (fieldInfo != null)
            {
                memberType = fieldInfo.FieldType;
            }

            if (memberType != null)
            {
                // 根据成员类型确定关键帧类型
                if (memberType == typeof(float) || memberType == typeof(int) || memberType == typeof(bool))
                {
                    keyType = KeyFrameValueType.Float;
                }
                else if (memberType == typeof(Vector2))
                {
                    keyType = KeyFrameValueType.Vector2;
                }
                else if (memberType == typeof(Vector3))
                {
                    keyType = KeyFrameValueType.Vector3;
                }
                else if (memberType == typeof(Vector4))
                {
                    keyType = KeyFrameValueType.Vector4;
                }
                else if (memberType == typeof(Color))
                {
                    keyType = KeyFrameValueType.Color;
                }
                else
                {
                    // 不支持的类型，使用默认Float类型
                    Debug.LogWarning($"[LayaAir Export] Unsupported animation property type: {componentTypeName}.{propName} ({memberType.Name}), using Float as default");
                    keyType = KeyFrameValueType.Float;
                }
            }
            else
            {
                // 无法通过反射找到字段，根据属性名推断类型
                // 如果属性名包含.x/.y/.z/.w等后缀，根据后缀推断
                if (propertyName.Contains(".x") || propertyName.Contains(".y") || propertyName.Contains(".z"))
                {
                    keyType = KeyFrameValueType.Vector3;
                }
                else if (propertyName.Contains(".r") || propertyName.Contains(".g") || propertyName.Contains(".b") || propertyName.Contains(".a"))
                {
                    keyType = KeyFrameValueType.Color;
                }
                else if (propertyName.Contains(".w"))
                {
                    keyType = KeyFrameValueType.Vector4;
                }
                else
                {
                    // 默认使用Float类型
                    keyType = KeyFrameValueType.Float;
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[LayaAir Export] Error analyzing component property: {componentTypeName}.{propName} - {e.Message}");
            return null;
        }

        // 使用组件UUID或类型名作为组件标识符
        // 优先使用UUID（如果在映射表中），否则使用类型名
        string componentIdentifier = GetComponentIdentifier(componentTypeName);

        AnimationCurveGroup curveGroup = new AnimationCurveGroup(path, gameObject, binding.type, componentIdentifier, propertyName, keyType);
        curveGroup.propnames.Add(propName);

        return curveGroup;
    }
    public static void writeClip(AnimationClip aniclip, FileStream fs, GameObject gameObject, string clipName, ResoureMap resoureMap = null)
    {

        List<string> stringDatas = new List<string>();
        stringDatas.Add("ANIMATIONS");
        stringDatas.Add(clipName);
        int clipFrameRate = (int)aniclip.frameRate;

        //list Curve 数据
        List<EditorCurveBinding> editorCurveBindingList = new List<EditorCurveBinding>();



        // 原始 Curve 数据
        EditorCurveBinding[] oriEditorCurveBindingList = AnimationUtility.GetCurveBindings(aniclip);


        editorCurveBindingList.AddRange(oriEditorCurveBindingList);

        //  创建数据 数组
        EditorCurveBinding[] editorCurveBindings = editorCurveBindingList.ToArray();

        AnimationClipCurveData[] animationClipCurveDatas = new AnimationClipCurveData[editorCurveBindings.Length];
        Dictionary<string, AnimationCurveGroup> groupMap = new Dictionary<string, AnimationCurveGroup>();
        for (int j = 0; j < editorCurveBindings.Length; j++)
        {
            AnimationClipCurveData curveData = animationClipCurveDatas[j] = new AnimationClipCurveData(editorCurveBindings[j]);
            curveData.curve = AnimationUtility.GetEditorCurve(aniclip, editorCurveBindings[j]);

            string path = AnimationCurveGroup.getCurvePath(curveData);
            // For Renderer material bindings, override path with a unique key per
            // (objectPath, matSlot, shaderProperty) so that different float properties
            // on the same renderer don't collapse into the same group.
            if (typeof(Renderer).IsAssignableFrom(editorCurveBindings[j].type))
            {
                // Skip SpriteRenderer m_Color — not a material property,
                // handled by writeClip2D as 2D Animator2D instead
                if (editorCurveBindings[j].type == typeof(SpriteRenderer)
                    && curveData.propertyName.StartsWith("m_Color"))
                {
                    continue;
                }
                string matPath = GetMaterialAnimationGroupPath(curveData.path, curveData.propertyName);
                if (matPath != null) path = matPath;
            }

            AnimationCurveGroup curveGroup = null;
            if (groupMap.ContainsKey(path))
            {
                curveGroup = groupMap[path];
            }
            else
            {
                GameObject child = gameObject;

                // Handle animation curves for child GameObjects
                if (!string.IsNullOrEmpty(curveData.path))
                {
                    string[] strArr = curveData.path.Split('/');
                    for (int m = 0; m < strArr.Length; m++)
                    {
                        // Skip empty strings (can happen with trailing slashes)
                        if (string.IsNullOrEmpty(strArr[m]))
                        {
                            continue;
                        }

                        if (stringDatas.IndexOf(strArr[m]) == -1)
                        {
                            stringDatas.Add(strArr[m]);
                        }
                        Transform ct = child.transform.Find(strArr[m]);
                        if (ct)
                        {
                            child = ct.gameObject;
                        }
                        else
                        {
                            child = null;
                            Debug.LogWarning(gameObject.name + "'s Aniamtor: " + clipName + " clip " + strArr[m] + " is missing");
                            break;
                        }
                    }

                    // Skip only this curve if child GameObject was not found, not the entire clip
                    if (child == null)
                    {
                        continue;
                    }
                }

                object targetObject = AnimationUtility.GetAnimatedObject(gameObject, editorCurveBindings[j]);
                EditorCurveBinding binding = editorCurveBindings[j];
                if (binding.type == typeof(Transform))
                {
                    curveGroup = readTransfromAnimation(binding, child, targetObject, curveData.path, curveData.propertyName);
                }
                else if (binding.type == typeof(RectTransform))
                {
                    curveGroup = readTransfromAnimation(binding, child, targetObject, curveData.path, curveData.propertyName);
                }
                else if (binding.type == typeof(GameObject))
                {
                    // Handle GameObject properties like m_IsActive
                    curveGroup = readTransfromAnimation(binding, child, targetObject, curveData.path, curveData.propertyName);
                }
                else if (binding.type == typeof(Camera))
                {
                    // Handle Camera properties (all properties directly on Camera node)
                    curveGroup = readCameraAnimation(binding, child, targetObject, curveData.path, curveData.propertyName);
                }
                else if (binding.type == typeof(SpriteRenderer)
                         && curveData.propertyName.StartsWith("m_Color"))
                {
                    // SpriteRenderer m_Color curves are handled by writeClip2D — skip in 3D animation
                    continue;
                }
                else if (typeof(Renderer).IsAssignableFrom(binding.type))
                {
                    curveGroup = readMaterAnimation(binding, child, targetObject, curveData.path, curveData.propertyName);
                }
                else if (typeof(MonoBehaviour).IsAssignableFrom(binding.type))
                {
                    // Handle custom script (MonoBehaviour) properties
                    curveGroup = readScriptAnimation(binding, child, targetObject, curveData.path, curveData.propertyName);
                }
                else if (binding.type == typeof(ParticleSystem))
                {
                    // ParticleSystem → Laya ShurikenParticleRenderer 属性映射
                    curveGroup = readParticleSystemAnimation(binding, child, targetObject, curveData.path, curveData.propertyName);
                }
                else if (typeof(Component).IsAssignableFrom(binding.type))
                {
                    // Handle other Unity built-in components (Light, AudioSource, etc.)
                    curveGroup = readComponentAnimation(binding, child, targetObject, curveData.path, curveData.propertyName);
                }
                else
                {
                    // Unknown type, log warning
                    Debug.LogWarning($"[LayaAir Export] Unknown animation binding type: {binding.type.Name} on path '{curveData.path}' property '{curveData.propertyName}'");
                }
                if (curveGroup != null)
                {
                    groupMap.Add(path, curveGroup);
                }
            }
            if (curveGroup != null)
            {
                curveGroup.pushCurve(curveData);
            }
        }
        // Collect ObjectReferenceCurveBindings for material swap animations
        var rawMatSwapBindings = new List<KeyValuePair<EditorCurveBinding, ObjectReferenceKeyframe[]>>();
        if (resoureMap != null)
        {
            EditorCurveBinding[] objRefBindings = AnimationUtility.GetObjectReferenceCurveBindings(aniclip);
            foreach (EditorCurveBinding objBinding in objRefBindings)
            {
                if (!typeof(Renderer).IsAssignableFrom(objBinding.type)) continue;
                if (!objBinding.propertyName.StartsWith("m_Materials.Array.data[")) continue;
                ObjectReferenceKeyframe[] kfs = AnimationUtility.GetObjectReferenceCurve(aniclip, objBinding);
                if (kfs != null && kfs.Length > 0)
                    rawMatSwapBindings.Add(new KeyValuePair<EditorCurveBinding, ObjectReferenceKeyframe[]>(objBinding, kfs));
            }
        }

        Dictionary<uint, float> timeList = new Dictionary<uint, float>();
        foreach (var group in groupMap)
        {
            group.Value.mergeTimeList(timeList);
        }

        // Add material swap keyframe times to the global time list
        foreach (var pair in rawMatSwapBindings)
        {
            foreach (ObjectReferenceKeyframe kf in pair.Value)
            {
                uint fi = AnimationCurveGroup.getFrameByTime(kf.time);
                if (!timeList.ContainsKey(fi)) timeList.Add(fi, kf.time);
            }
        }

        List<float> startTimeList = new List<float>();
        foreach (var time in timeList)
        {
            startTimeList.Add(time.Value);
        }
        startTimeList.Sort();

        // Check if startTimeList is empty (no valid keyframes)
        if (startTimeList.Count == 0)
        {
            // Create a minimal valid animation file with 0 duration to prevent crash
            startTimeList.Add(0.0f);
        }

        float startTime = startTimeList[0];
        float endTime = startTimeList[startTimeList.Count - 1];

        Dictionary<uint, FrameInfo> frameInfoList = new Dictionary<uint, FrameInfo>();
        for (int i = 0, legnth = startTimeList.Count; i < legnth; i++)
        {
            FrameInfo info = new FrameInfo();
            info.oriderIndex = i;
            float time = info.time = startTimeList[i];
            var frameIndex = info.frameIndex = AnimationCurveGroup.getFrameByTime(time);
            frameInfoList.Add(frameIndex, info);
        }
        List<AniNodeData> aniNodeDatas = new List<AniNodeData>();

        AniNodeData aniNodeData;
        foreach (AnimationCurveGroup group in groupMap.Values)
        {
            group.addEmptyClipCurve(startTime, endTime);
            aniNodeData = new AniNodeData();
            group.getAnimaFameData(ref aniNodeData, ref frameInfoList, ref stringDatas);
            aniNodeDatas.Add(aniNodeData);
        }

        // Build MaterialSwapNodeData from collected bindings
        var materialSwapNodes = new List<MaterialSwapNodeData>();
        foreach (var pair in rawMatSwapBindings)
        {
            EditorCurveBinding binding = pair.Key;
            ObjectReferenceKeyframe[] keyframes = pair.Value;

            // Parse slot index from "m_Materials.Array.data[N]"
            int bracketStart = binding.propertyName.IndexOf('[');
            int bracketEnd = binding.propertyName.IndexOf(']');
            if (bracketStart < 0 || bracketEnd < 0) continue;
            string slotIndexStr = binding.propertyName.Substring(bracketStart + 1, bracketEnd - bracketStart - 1);

            // Path indices (empty list if binding.path is empty = root object)
            List<UInt16> pathIdx = new List<UInt16>();
            if (!string.IsNullOrEmpty(binding.path))
            {
                foreach (string seg in binding.path.Split('/'))
                {
                    if (!stringDatas.Contains(seg)) stringDatas.Add(seg);
                    pathIdx.Add((UInt16)stringDatas.IndexOf(seg));
                }
            }

            // Component type name
            string compTypeName = typeof(SkinnedMeshRenderer).IsAssignableFrom(binding.type)
                ? "SkinnedMeshRenderer" : "MeshRenderer";
            if (!stringDatas.Contains(compTypeName)) stringDatas.Add(compTypeName);

            // Property names in string pool
            const string propSharedMaterials = "sharedMaterials";
            if (!stringDatas.Contains(propSharedMaterials)) stringDatas.Add(propSharedMaterials);
            if (!stringDatas.Contains(slotIndexStr)) stringDatas.Add(slotIndexStr);

            // Build per-keyframe data
            List<MaterialSwapFrameData> swapKfs = new List<MaterialSwapFrameData>();
            foreach (ObjectReferenceKeyframe kf in keyframes)
            {
                Material mat = kf.value as Material;
                if (mat == null) continue;
                MaterialFile mf = resoureMap.GetMaterialFile(mat);
                if (mf == null) continue;
                uint fi = AnimationCurveGroup.getFrameByTime(kf.time);
                FrameInfo info;
                if (!frameInfoList.TryGetValue(fi, out info)) continue;
                swapKfs.Add(new MaterialSwapFrameData
                {
                    startTimeIndex = (UInt16)info.oriderIndex,
                    materialPath = "res://" + mf.filePath
                });
            }
            if (swapKfs.Count == 0) continue;

            materialSwapNodes.Add(new MaterialSwapNodeData
            {
                type = (Byte)KeyFrameValueType.MaterialSwap,
                pathLength = (UInt16)pathIdx.Count,
                pathIndex = pathIdx,
                componentTypeIndex = (UInt16)stringDatas.IndexOf(compTypeName),
                propertyNameLength = (UInt16)2,
                propertyNameIndex = new List<UInt16>
                {
                    (UInt16)stringDatas.IndexOf(propSharedMaterials),
                    (UInt16)stringDatas.IndexOf(slotIndexStr)
                },
                keyFrameCount = (UInt16)swapKfs.Count,
                keyFrames = swapKfs
            });
        }

        long MarkContentAreaPosition_Start = 0;

        long BlockAreaPosition_Start = 0;

        long StringAreaPosition_Start = 0;

        long ContentAreaPosition_Start = 0;

        long StringDatasAreaPosition_Start = 0;
        long StringDatasAreaPosition_End = 0;

        //版本号
        //minner动画

        string layaModelVerion = LaniVersion;

        Util.FileUtil.WriteData(fs, layaModelVerion);

        //标记数据信息区
        MarkContentAreaPosition_Start = fs.Position; // 预留数据区偏移地址

        Util.FileUtil.WriteData(fs, (UInt32)0);//UInt32 offset
        Util.FileUtil.WriteData(fs, (UInt32)0);//UInt32 blockLength

        //预留数据区偏移地址
        BlockAreaPosition_Start = fs.Position;//预留段落数量
        int blockCount = 1;
        Util.FileUtil.WriteData(fs, (UInt16)blockCount);
        for (int j = 0; j < blockCount; j++)
        {
            Util.FileUtil.WriteData(fs, (UInt32)0);//UInt32 blockStart
            Util.FileUtil.WriteData(fs, (UInt32)0);//UInt32 blockLength
        }

        //字符区
        StringAreaPosition_Start = fs.Position;//预留字符区
        Util.FileUtil.WriteData(fs, (UInt32)0);//UInt32 offset
        Util.FileUtil.WriteData(fs, (UInt16)0);//count

        //内容区
        ContentAreaPosition_Start = fs.Position;//预留字符区
        Util.FileUtil.WriteData(fs, (UInt16)stringDatas.IndexOf("ANIMATIONS"));//uint16 段落函数字符ID

        Util.FileUtil.WriteData(fs, (UInt16)startTimeList.Count);//startTime
        for (int j = 0; j < startTimeList.Count; j++)
        {
            Util.FileUtil.WriteData(fs, (float)startTimeList[j]);
        }

        Util.FileUtil.WriteData(fs, (UInt16)stringDatas.IndexOf(clipName));//动画名字符索引

        float aniTotalTime = aniclip.length;
        Util.FileUtil.WriteData(fs, aniTotalTime);///动画总时长
        if (aniclip.wrapMode == UnityEngine.WrapMode.Loop)
        {
            Util.FileUtil.WriteData(fs, true);
        }
        else
        {
            Util.FileUtil.WriteData(fs, aniclip.isLooping);//动画是否循环
        }


        Util.FileUtil.WriteData(fs, (UInt16)clipFrameRate);//frameRate

        Util.FileUtil.WriteData(fs, (UInt16)(aniNodeDatas.Count + materialSwapNodes.Count));//节点个数
        for (int j = 0; j < aniNodeDatas.Count; j++)
        {
            aniNodeData = aniNodeDatas[j];
            // WEIGHT_05 per-node prefix: no propertyChangePath, no callbackFunData, paramLen=0
            Util.FileUtil.WriteData(fs, (Byte)0);
            Util.FileUtil.WriteData(fs, (Byte)0);
            Util.FileUtil.WriteData(fs, (Byte)0);
            Util.FileUtil.WriteData(fs, aniNodeData.type);//type
            Util.FileUtil.WriteData(fs, aniNodeData.pathLength);//pathLength
            for (int m = 0; m < aniNodeData.pathLength; m++)
            {
                Util.FileUtil.WriteData(fs, aniNodeData.pathIndex[m]);//pathIndex
            }
            Util.FileUtil.WriteData(fs, aniNodeData.conpomentTypeIndex);//conpomentTypeIndex
            Util.FileUtil.WriteData(fs, aniNodeData.propertyNameLength);//propertyNameLength
            for (int m = 0; m < aniNodeData.propertyNameLength; m++)//frameDataLengthIndex
            {
                Util.FileUtil.WriteData(fs, aniNodeData.propertyNameIndex[m]);//propertyNameLength
            }
            Util.FileUtil.WriteData(fs, aniNodeData.keyFrameCount);//帧个数

            for (int m = 0; m < aniNodeData.keyFrameCount; m++)
            {
                Util.FileUtil.WriteData(fs, aniNodeData.aniNodeFrameDatas[m].startTimeIndex);//startTimeIndex
                Util.FileUtil.WriteData(fs, aniNodeData.aniNodeFrameDatas[m].inTangentNumbers);
                Util.FileUtil.WriteData(fs, aniNodeData.aniNodeFrameDatas[m].outTangentNumbers);
                Util.FileUtil.WriteData(fs, aniNodeData.aniNodeFrameDatas[m].valueNumbers);
                Util.FileUtil.WriteData(fs, (Byte)0); // WEIGHT_05: weightedMode/isWeight = not weighted
            }
        }

        // Write material swap nodes (WEIGHT_05 format, type=11)
        foreach (MaterialSwapNodeData swapNode in materialSwapNodes)
        {
            // WEIGHT_05 per-node prefix
            Util.FileUtil.WriteData(fs, (Byte)0);
            Util.FileUtil.WriteData(fs, (Byte)0);
            Util.FileUtil.WriteData(fs, (Byte)0);
            Util.FileUtil.WriteData(fs, swapNode.type);
            Util.FileUtil.WriteData(fs, swapNode.pathLength);
            for (int m = 0; m < swapNode.pathLength; m++)
                Util.FileUtil.WriteData(fs, swapNode.pathIndex[m]);
            Util.FileUtil.WriteData(fs, swapNode.componentTypeIndex);
            Util.FileUtil.WriteData(fs, swapNode.propertyNameLength);
            for (int m = 0; m < swapNode.propertyNameLength; m++)
                Util.FileUtil.WriteData(fs, swapNode.propertyNameIndex[m]);
            Util.FileUtil.WriteData(fs, swapNode.keyFrameCount);
            foreach (MaterialSwapFrameData kf in swapNode.keyFrames)
            {
                Util.FileUtil.WriteData(fs, kf.startTimeIndex);
                Util.FileUtil.WriteData(fs, kf.materialPath); // writes int16 length + UTF8 bytes
            }
        }

        //事件
        AnimationEvent[] aniEvents = aniclip.events;
        int aniEventCount = aniEvents.Length;
        Util.FileUtil.WriteData(fs, (Int16)aniEventCount);
        for (int k = 0; k < aniEventCount; k++)
        {
            AnimationEvent aniEvent = aniEvents[k];
            //time
            Util.FileUtil.WriteData(fs, (float)aniEvent.time);
            //函数名字索引
            string funName = aniEvent.functionName;
            if (stringDatas.IndexOf(funName) == -1)
            {
                stringDatas.Add(funName);
            }
            Util.FileUtil.WriteData(fs, (UInt16)stringDatas.IndexOf(funName));
            //参数个数
            UInt16 paramCount = 3;
            Util.FileUtil.WriteData(fs, paramCount);
            for (int m = 0; m < 1; m++)
            {
                //Number
                Util.FileUtil.WriteData(fs, (Byte)2);
                Util.FileUtil.WriteData(fs, (float)aniEvent.floatParameter);

                //Int
                Util.FileUtil.WriteData(fs, (Byte)1);
                Util.FileUtil.WriteData(fs, (Int32)aniEvent.intParameter);

                //Strings
                Util.FileUtil.WriteData(fs, (Byte)3);
                string stringParam = aniEvent.stringParameter;
                if (stringDatas.IndexOf(stringParam) == -1)
                {
                    stringDatas.Add(stringParam);
                }
                Util.FileUtil.WriteData(fs, (UInt16)stringDatas.IndexOf(stringParam));
            }
        }

        //字符数据区
        StringDatasAreaPosition_Start = fs.Position;
        for (int j = 0; j < stringDatas.Count; j++)
        {
            Util.FileUtil.WriteData(fs, stringDatas[j]);
        }
        StringDatasAreaPosition_End = fs.Position;

        //倒推字符区
        fs.Position = StringAreaPosition_Start + 4;
        Util.FileUtil.WriteData(fs, (UInt16)stringDatas.Count);//count

        //倒推内容段落信息区
        fs.Position = BlockAreaPosition_Start + 2 + 4;
        Util.FileUtil.WriteData(fs, (UInt32)(StringDatasAreaPosition_Start - ContentAreaPosition_Start));//UInt32 blockLength

        //倒推数据信息区
        fs.Position = MarkContentAreaPosition_Start;
        Util.FileUtil.WriteData(fs, (UInt32)StringDatasAreaPosition_Start);
        Util.FileUtil.WriteData(fs, (UInt32)(StringDatasAreaPosition_End - StringDatasAreaPosition_Start));

        fs.Close();
    }

    /// <summary>
    /// Write a 2D animation clip (.mc) in LAYAANIMATION2D:01 binary format.
    /// Extracts SpriteRenderer m_Color curves and maps them to Mesh2DRender color r/g/b/a properties
    /// using 3-segment property paths: ["Mesh2DRender", "color", "r"].
    /// Compatible with AnimationClip2DParse01.READ_ANIMATIONS2D() on the engine side.
    /// </summary>
    /// <param name="targetPath">binding.path filter — only extract curves matching this path (empty = root object)</param>
    public static void writeClip2D(AnimationClip aniclip, FileStream fs, GameObject gameObject, string targetPath = "")
    {
        // Extract SpriteRenderer m_Color curves matching targetPath
        EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(aniclip);
        AnimationCurve curveR = null, curveG = null, curveB = null, curveA = null;

        foreach (EditorCurveBinding binding in bindings)
        {
            if (binding.type != typeof(SpriteRenderer)) continue;
            if (binding.path != targetPath) continue; // Match specified target path

            AnimationCurve curve = AnimationUtility.GetEditorCurve(aniclip, binding);
            if (binding.propertyName == "m_Color.r") curveR = curve;
            else if (binding.propertyName == "m_Color.g") curveG = curve;
            else if (binding.propertyName == "m_Color.b") curveB = curve;
            else if (binding.propertyName == "m_Color.a") curveA = curve;
        }

        // Build list of non-null curves with their color component name (3rd segment)
        var curveEntries = new List<KeyValuePair<string, AnimationCurve>>();
        if (curveR != null) curveEntries.Add(new KeyValuePair<string, AnimationCurve>("r", curveR));
        if (curveG != null) curveEntries.Add(new KeyValuePair<string, AnimationCurve>("g", curveG));
        if (curveB != null) curveEntries.Add(new KeyValuePair<string, AnimationCurve>("b", curveB));
        if (curveA != null) curveEntries.Add(new KeyValuePair<string, AnimationCurve>("a", curveA));

        if (curveEntries.Count == 0)
        {
            fs.Close();
            return;
        }

        // Pad curves: if last keyframe ends before clip duration, add a hold keyframe at clip end
        // This ensures the 2D clip spans the full duration (matching the 3D clip) for correct loop sync
        float clipLength = aniclip.length;
        for (int ci = 0; ci < curveEntries.Count; ci++)
        {
            AnimationCurve curve = curveEntries[ci].Value;
            Keyframe[] keys = curve.keys;
            if (keys.Length > 0 && keys[keys.Length - 1].time < clipLength - 0.001f)
            {
                Keyframe lastKey = keys[keys.Length - 1];
                Keyframe padKey = new Keyframe(clipLength, lastKey.value, 0f, 0f);
                padKey.weightedMode = lastKey.weightedMode;
                curve.AddKey(padKey);
                // Rebuild entry with modified curve
                curveEntries[ci] = new KeyValuePair<string, AnimationCurve>(curveEntries[ci].Key, curve);
            }
        }

        // Build string pool: 3-segment paths ["Mesh2DRender", "color", "r/g/b/a"]
        List<string> stringDatas = new List<string>();
        stringDatas.Add("ANIMATIONS2D");
        stringDatas.Add("Mesh2DRender");
        if (!stringDatas.Contains("color")) stringDatas.Add("color");
        foreach (var entry in curveEntries)
        {
            if (!stringDatas.Contains(entry.Key)) stringDatas.Add(entry.Key);
        }

        // Collect all unique float values into numList (float pool)
        List<float> numList = new List<float>();
        Dictionary<int, int> numMap = new Dictionary<int, int>(); // bitwise int key for exact float matching

        System.Func<float, int> addNum = (float val) =>
        {
            if (float.IsPositiveInfinity(val)) val = float.MaxValue;
            else if (float.IsNegativeInfinity(val)) val = float.MinValue;
            else if (float.IsNaN(val)) val = 0f;

            int bits = System.BitConverter.ToInt32(System.BitConverter.GetBytes(val), 0);
            if (!numMap.ContainsKey(bits))
            {
                numMap[bits] = numList.Count;
                numList.Add(val);
            }
            return numMap[bits];
        };

        System.Func<float, int> getNumIndex = (float val) =>
        {
            if (float.IsPositiveInfinity(val)) val = float.MaxValue;
            else if (float.IsNegativeInfinity(val)) val = float.MinValue;
            else if (float.IsNaN(val)) val = 0f;

            int bits = System.BitConverter.ToInt32(System.BitConverter.GetBytes(val), 0);
            return numMap[bits];
        };

        // Add duration
        float duration = aniclip.length;
        addNum(duration);

        // Pre-collect all keyframe values into numList
        foreach (var entry in curveEntries)
        {
            foreach (Keyframe kf in entry.Value.keys)
            {
                addNum(kf.time);
                addNum(kf.value);
                addNum(kf.inTangent);
                addNum(kf.outTangent);
            }
        }

        int clipFrameRate = Mathf.RoundToInt(aniclip.frameRate);
        if (clipFrameRate <= 0) clipFrameRate = 30;

        // Position markers for backfilling
        long MarkContentAreaPosition_Start = 0;
        long BlockAreaPosition_Start = 0;
        long StringAreaPosition_Start = 0;
        long ContentAreaPosition_Start = 0;
        long StringDatasAreaPosition_Start = 0;
        long StringDatasAreaPosition_End = 0;

        // Version string
        Util.FileUtil.WriteData(fs, "LAYAANIMATION2D:01");

        // DATA section: offset + size (backfilled later)
        MarkContentAreaPosition_Start = fs.Position;
        Util.FileUtil.WriteData(fs, (UInt32)0); // offset
        Util.FileUtil.WriteData(fs, (UInt32)0); // size

        // BLOCK section
        BlockAreaPosition_Start = fs.Position;
        Util.FileUtil.WriteData(fs, (UInt16)1); // block count
        Util.FileUtil.WriteData(fs, (UInt32)0); // block start (backfilled)
        Util.FileUtil.WriteData(fs, (UInt32)0); // block length (backfilled)

        // STRINGS section header
        StringAreaPosition_Start = fs.Position;
        Util.FileUtil.WriteData(fs, (UInt32)0); // offset (backfilled)
        Util.FileUtil.WriteData(fs, (UInt16)0); // count (backfilled)

        // Content area
        ContentAreaPosition_Start = fs.Position;

        // Block name index ("ANIMATIONS2D")
        Util.FileUtil.WriteData(fs, (UInt16)stringDatas.IndexOf("ANIMATIONS2D"));

        // numList
        Util.FileUtil.WriteData(fs, (UInt16)numList.Count);
        for (int i = 0; i < numList.Count; i++)
        {
            Util.FileUtil.WriteData(fs, numList[i]);
        }

        // duration (index into numList)
        Util.FileUtil.WriteData(fs, (Int16)getNumIndex(duration));

        // isLooping
        bool isLooping = aniclip.isLooping || aniclip.wrapMode == UnityEngine.WrapMode.Loop;
        Util.FileUtil.WriteData(fs, isLooping);

        // frameRate
        Util.FileUtil.WriteData(fs, (Int16)clipFrameRate);

        // nodeCount
        Util.FileUtil.WriteData(fs, (Int16)curveEntries.Count);

        // Per node
        foreach (var entry in curveEntries)
        {
            string propName = entry.Key;
            AnimationCurve curve = entry.Value;

            // ownerPath: 0 entries (target is the prefab root itself)
            Util.FileUtil.WriteData(fs, (UInt16)0);

            // propertyLength: 3 entries ["Mesh2DRender", "color", "r/g/b/a"]
            Util.FileUtil.WriteData(fs, (UInt16)3);
            Util.FileUtil.WriteData(fs, (UInt16)stringDatas.IndexOf("Mesh2DRender"));
            Util.FileUtil.WriteData(fs, (UInt16)stringDatas.IndexOf("color"));
            Util.FileUtil.WriteData(fs, (UInt16)stringDatas.IndexOf(propName));

            // keyframeCount
            Keyframe[] keys = curve.keys;
            Util.FileUtil.WriteData(fs, (UInt16)keys.Length);

            foreach (Keyframe kf in keys)
            {
                // time (numList index)
                Util.FileUtil.WriteData(fs, (UInt16)getNumIndex(kf.time));

                // hasTweenType: 0 (no tween type string)
                Util.FileUtil.WriteData(fs, (Byte)0);

                // hasTweenInfo: 1 (we have tangent info)
                Util.FileUtil.WriteData(fs, (Byte)1);

                // inTangent, outTangent (sanitized)
                float inT = kf.inTangent;
                float outT = kf.outTangent;
                if (float.IsPositiveInfinity(inT)) inT = float.MaxValue;
                else if (float.IsNegativeInfinity(inT)) inT = float.MinValue;
                else if (float.IsNaN(inT)) inT = 0f;
                if (float.IsPositiveInfinity(outT)) outT = float.MaxValue;
                else if (float.IsNegativeInfinity(outT)) outT = float.MinValue;
                else if (float.IsNaN(outT)) outT = 0f;

                Util.FileUtil.WriteData(fs, (UInt16)getNumIndex(inT));
                Util.FileUtil.WriteData(fs, (UInt16)getNumIndex(outT));

                // hasInWeight: 0
                Util.FileUtil.WriteData(fs, (Byte)0);
                // hasOutWeight: 0
                Util.FileUtil.WriteData(fs, (Byte)0);

                // valueType: 0 (number)
                Util.FileUtil.WriteData(fs, (Byte)0);
                // value (numList index)
                Util.FileUtil.WriteData(fs, (UInt16)getNumIndex(kf.value));

                // hasExtend: 0
                Util.FileUtil.WriteData(fs, (Byte)0);
            }
        }

        // events: 0
        Util.FileUtil.WriteData(fs, (UInt16)0);

        // String data area
        StringDatasAreaPosition_Start = fs.Position;
        for (int i = 0; i < stringDatas.Count; i++)
        {
            Util.FileUtil.WriteData(fs, stringDatas[i]);
        }
        StringDatasAreaPosition_End = fs.Position;

        // Backfill STRINGS header (leave offset as 0 — READ_STRINGS adds offset + DATA.offset)
        fs.Position = StringAreaPosition_Start + 4; // skip offset field (stays 0)
        Util.FileUtil.WriteData(fs, (UInt16)stringDatas.Count);

        // Backfill BLOCK
        fs.Position = BlockAreaPosition_Start + 2; // skip block count
        Util.FileUtil.WriteData(fs, (UInt32)0); // block start (relative to content area = 0)
        Util.FileUtil.WriteData(fs, (UInt32)(StringDatasAreaPosition_Start - ContentAreaPosition_Start));

        // Backfill DATA
        fs.Position = MarkContentAreaPosition_Start;
        Util.FileUtil.WriteData(fs, (UInt32)StringDatasAreaPosition_Start);
        Util.FileUtil.WriteData(fs, (UInt32)(StringDatasAreaPosition_End - StringDatasAreaPosition_Start));

        fs.Close();
    }
}
