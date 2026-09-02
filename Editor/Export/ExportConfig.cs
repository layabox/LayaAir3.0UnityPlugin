using System.IO;
using System.Xml;
using UnityEngine;

public class ExportConfig
{
    private static bool _updateConfig = false;
    //场景 or 预制体
    private static int _FirstlevelMenu = -1;
    //忽略未激活节点
    private static bool _IgnoreNotActiveGameObject;
    //批量导出一级节点
    private static bool _BatchMade;
    //忽略uv
    private static bool _IgnoreVerticesUV;
    //忽略Normal
    private static bool _IgnoreVerticesNormal;
    //忽略tangent
    private static bool _IgnoreVerticesTangent;
    //忽略Color
    private static bool _IgnoreVerticesColor;
    //自动生成uv1
    private static bool _AutoVerticesUV1;
    //自定义根目录
    private static bool _CustomizeDirectory;
    //自定义根目录名
    private static string _CustomizeDirectoryName = "";

    //导出地址
    private static string _SAVEPATH = "Assets";

    // 公共依赖资源导出地址。为空时与 SAVEPATH 相同，保持旧行为。
    // 该值主要供批处理/自定义导出入口在一次导出期间临时设置。
    private static string _SHAREDRESOURCEPATH = "";

    // 保留输出目录中已经存在的手工迁移 Shader。
    private static bool _PreserveExistingShaderFiles = false;

    //启用自定义Shader导出
    private static bool _EnableCustomShaderExport = false;

    //粒子系统Mesh顶点限制处理
    private static bool _AutoSimplifyParticleMesh = false;
    private static int _ParticleMeshMaxVertices = 65535;
    private static float _ParticleMeshSimplifyQuality = 0.7f; // 0-1之间，越高质量越好
    private static bool _ShowParticleMeshWarning = true;

    // 粒子系统默认导出模式: 0=Shuriken(GPU), 1=CPU Particle
    private static int _ParticleExportMode = 0;

    //场景 or 预制体
    public static int FirstlevelMenu
    {
        get { return _FirstlevelMenu; }
        set
        {
            if (_FirstlevelMenu != value)
            {
                _FirstlevelMenu = value;
                _updateConfig = true;
            }
        }
    }
    //忽略未激活节点
    public static bool IgnoreNotActiveGameObject
    {
        get { return _IgnoreNotActiveGameObject; }
        set
        {
            if (_IgnoreNotActiveGameObject != value)
            {
                _IgnoreNotActiveGameObject = value;
                _updateConfig = true;
            }
        }
    }
    //批量导出一级节点
    public static bool BatchMade
    {
        get { return _BatchMade; }
        set
        {
            if (_BatchMade != value)
            {
                _BatchMade = value;
                _updateConfig = true;
            }
        }
    }
    //忽略uv
    public static bool IgnoreVerticesUV
    {
        get { return _IgnoreVerticesUV; }
        set
        {
            if (_IgnoreVerticesUV != value)
            {
                _IgnoreVerticesUV = value;
                _updateConfig = true;
            }
        }
    }
    //忽略Normal
    public static bool IgnoreVerticesNormal
    {
        get { return _IgnoreVerticesNormal; }
        set
        {
            if (_IgnoreVerticesNormal != value)
            {
                _IgnoreVerticesNormal = value;
                _updateConfig = true;
            }
        }
    }
    //忽略tangent
    public static bool IgnoreVerticesTangent
    {
        get { return _IgnoreVerticesTangent; }
        set
        {
            if (_IgnoreVerticesTangent != value)
            {
                _IgnoreVerticesTangent = value;
                _updateConfig = true;
            }
        }
    }
    //自动生成uv1
    public static bool AutoVerticesUV1
    {
        get { return _AutoVerticesUV1; }
        set
        {
            if (_AutoVerticesUV1 != value)
            {
                _AutoVerticesUV1 = value;
                _updateConfig = true;
            }
        }
    }

    //忽略Color
    public static bool IgnoreVerticesColor
    {
        get { return _IgnoreVerticesColor; }
        set
        {
            if (_IgnoreVerticesColor != value)
            {
                _IgnoreVerticesColor = value;
                _updateConfig = true;
            }
        }
    }
    //自定义根目录
    public static bool CustomizeDirectory
    {
        get { return _CustomizeDirectory; }
        set
        {
            if (_CustomizeDirectory != value)
            {
                _CustomizeDirectory = value;
                _updateConfig = true;
            }
        }
    }

    //自定义根目录名
    public static string CustomizeDirectoryName
    {
        get { return _CustomizeDirectoryName; }
        set
        {
            if (_CustomizeDirectoryName != value)
            {
                _CustomizeDirectoryName = value;
                _updateConfig = true;
            }
        }
    }

    //导出地址
    public static string SAVEPATH
    {
        get { return _SAVEPATH; }
        set
        {
            if (_SAVEPATH != value)
            {
                _SAVEPATH = value;
                _updateConfig = true;
            }
        }
    }

    /// <summary>
    /// 材质、纹理、模型、Shader 等依赖资源的公共输出目录。
    /// 空字符串表示继续输出到 SavePath()，兼容现有导出流程。
    /// </summary>
    public static string SHAREDRESOURCEPATH
    {
        get { return _SHAREDRESOURCEPATH; }
        set { _SHAREDRESOURCEPATH = value ?? ""; }
    }

    public static bool PreserveExistingShaderFiles
    {
        get { return _PreserveExistingShaderFiles; }
        set { _PreserveExistingShaderFiles = value; }
    }

    //启用自定义Shader导出
    public static bool EnableCustomShaderExport
    {
        get { return _EnableCustomShaderExport; }
        set
        {
            if (_EnableCustomShaderExport != value)
            {
                _EnableCustomShaderExport = value;
                _updateConfig = true;
            }
        }
    }

    //粒子系统Mesh自动简化
    public static bool AutoSimplifyParticleMesh
    {
        get { return _AutoSimplifyParticleMesh; }
        set
        {
            if (_AutoSimplifyParticleMesh != value)
            {
                _AutoSimplifyParticleMesh = value;
                _updateConfig = true;
            }
        }
    }

    //粒子系统总顶点数限制
    public static int ParticleMeshMaxVertices
    {
        get { return _ParticleMeshMaxVertices; }
        set
        {
            if (_ParticleMeshMaxVertices != value)
            {
                _ParticleMeshMaxVertices = value;
                _updateConfig = true;
            }
        }
    }

    //Mesh简化质量 (0-1)
    public static float ParticleMeshSimplifyQuality
    {
        get { return _ParticleMeshSimplifyQuality; }
        set
        {
            if (_ParticleMeshSimplifyQuality != value)
            {
                _ParticleMeshSimplifyQuality = Mathf.Clamp01(value);
                _updateConfig = true;
            }
        }
    }

    //是否显示粒子Mesh警告
    public static bool ShowParticleMeshWarning
    {
        get { return _ShowParticleMeshWarning; }
        set
        {
            if (_ShowParticleMeshWarning != value)
            {
                _ShowParticleMeshWarning = value;
                _updateConfig = true;
            }
        }
    }

    /// <summary>
    /// 粒子系统默认导出模式: 0=ShurikenParticle(GPU), 1=CPUParticle
    /// 单个物体可通过 LayaParticleExportSetting 组件覆盖此全局值
    /// </summary>
    public static int ParticleExportMode
    {
        get { return _ParticleExportMode; }
        set
        {
            if (_ParticleExportMode != value)
            {
                _ParticleExportMode = value;
                _updateConfig = true;
            }
        }
    }

    public static string SavePath()
    {
        if (CustomizeDirectory)
        {
            return _SAVEPATH + "/" + CustomizeDirectoryName;
        }
        else
        {
            return _SAVEPATH;
        }
    }

    public static string ResourceSavePath()
    {
        return string.IsNullOrEmpty(_SHAREDRESOURCEPATH)
            ? SavePath()
            : _SHAREDRESOURCEPATH;
    }
    public static void initConfig()
    {
        if (_FirstlevelMenu>=0)
        {
            return;
        }
        string configUrl = getConfig();
        XmlDocument xmlDoc = new XmlDocument();
        xmlDoc.Load(configUrl);
        XmlNode xn = xmlDoc.SelectSingleNode("LayaExportSetting");
        FirstlevelMenu = int.Parse(xn.SelectSingleNode("FirstlevelMenu").InnerText);
        IgnoreNotActiveGameObject = bool.Parse(xn.SelectSingleNode("IgnoreNotActiveGameObject").InnerText);
        BatchMade = bool.Parse(xn.SelectSingleNode("BatchMade").InnerText);
        IgnoreVerticesUV = bool.Parse(xn.SelectSingleNode("IgnoreVerticesUV").InnerText);
        IgnoreVerticesNormal = bool.Parse(xn.SelectSingleNode("IgnoreVerticesNormal").InnerText);
        IgnoreVerticesTangent = bool.Parse(xn.SelectSingleNode("IgnoreVerticesTangent").InnerText);
        IgnoreVerticesColor = bool.Parse(xn.SelectSingleNode("IgnoreVerticesColor").InnerText);
        AutoVerticesUV1 = bool.Parse(xn.SelectSingleNode("AutoVerticesUV1").InnerText);
        CustomizeDirectory = bool.Parse(xn.SelectSingleNode("CustomizeDirectory").InnerText);
        CustomizeDirectoryName = xn.SelectSingleNode("CustomizeDirectoryName").InnerText;
        _SAVEPATH = xn.SelectSingleNode("SavePath").InnerText;
        
        // 自定义Shader配置
        if (xn.SelectSingleNode("EnableCustomShaderExport") != null)
            EnableCustomShaderExport = bool.Parse(xn.SelectSingleNode("EnableCustomShaderExport").InnerText);

        // 粒子系统Mesh优化配置
        if (xn.SelectSingleNode("AutoSimplifyParticleMesh") != null)
            AutoSimplifyParticleMesh = bool.Parse(xn.SelectSingleNode("AutoSimplifyParticleMesh").InnerText);

        if (xn.SelectSingleNode("ParticleMeshMaxVertices") != null)
            ParticleMeshMaxVertices = int.Parse(xn.SelectSingleNode("ParticleMeshMaxVertices").InnerText);

        if (xn.SelectSingleNode("ParticleMeshSimplifyQuality") != null)
            ParticleMeshSimplifyQuality = float.Parse(xn.SelectSingleNode("ParticleMeshSimplifyQuality").InnerText);

        if (xn.SelectSingleNode("ShowParticleMeshWarning") != null)
            ShowParticleMeshWarning = bool.Parse(xn.SelectSingleNode("ShowParticleMeshWarning").InnerText);

        // 粒子导出模式配置
        if (xn.SelectSingleNode("ParticleExportMode") != null)
            ParticleExportMode = int.Parse(xn.SelectSingleNode("ParticleExportMode").InnerText);

        _updateConfig = false;
    }
    public static void saveConfiguration()
    {
        if (!_updateConfig)
        {
            return;
        }
        _updateConfig = false;
        string configUrl = getConfig();
        XmlDocument xmlDoc = new XmlDocument();
        xmlDoc.Load(configUrl);
        XmlNode xn = xmlDoc.SelectSingleNode("LayaExportSetting");
        xn.SelectSingleNode("FirstlevelMenu").InnerText = FirstlevelMenu.ToString();
        xn.SelectSingleNode("IgnoreNotActiveGameObject").InnerText = IgnoreNotActiveGameObject.ToString();
        xn.SelectSingleNode("BatchMade").InnerText = BatchMade.ToString();
        xn.SelectSingleNode("IgnoreVerticesUV").InnerText = IgnoreVerticesUV.ToString();
        xn.SelectSingleNode("IgnoreVerticesNormal").InnerText = IgnoreVerticesNormal.ToString();
        xn.SelectSingleNode("IgnoreVerticesTangent").InnerText = IgnoreVerticesTangent.ToString();
        xn.SelectSingleNode("IgnoreVerticesColor").InnerText = IgnoreVerticesColor.ToString();
        xn.SelectSingleNode("AutoVerticesUV1").InnerText = AutoVerticesUV1.ToString();
        xn.SelectSingleNode("CustomizeDirectory").InnerText = CustomizeDirectory.ToString();
        xn.SelectSingleNode("CustomizeDirectoryName").InnerText = CustomizeDirectoryName;
        xn.SelectSingleNode("SavePath").InnerText = SAVEPATH;
        
        // 自定义Shader配置
        if (xn.SelectSingleNode("EnableCustomShaderExport") != null)
            xn.SelectSingleNode("EnableCustomShaderExport").InnerText = EnableCustomShaderExport.ToString();

        // 粒子系统Mesh优化配置
        if (xn.SelectSingleNode("AutoSimplifyParticleMesh") != null)
            xn.SelectSingleNode("AutoSimplifyParticleMesh").InnerText = AutoSimplifyParticleMesh.ToString();

        if (xn.SelectSingleNode("ParticleMeshMaxVertices") != null)
            xn.SelectSingleNode("ParticleMeshMaxVertices").InnerText = ParticleMeshMaxVertices.ToString();

        if (xn.SelectSingleNode("ParticleMeshSimplifyQuality") != null)
            xn.SelectSingleNode("ParticleMeshSimplifyQuality").InnerText = ParticleMeshSimplifyQuality.ToString();

        if (xn.SelectSingleNode("ShowParticleMeshWarning") != null)
            xn.SelectSingleNode("ShowParticleMeshWarning").InnerText = ShowParticleMeshWarning.ToString();

        // 粒子导出模式配置
        if (xn.SelectSingleNode("ParticleExportMode") != null)
            xn.SelectSingleNode("ParticleExportMode").InnerText = ParticleExportMode.ToString();

        xmlDoc.Save(configUrl);
    }

    private static string getConfig()
    {
        return Util.FileUtil.getPluginResUrl("Configuration.xml");
    }

    public static void ResetConfig()
    {
        FirstlevelMenu = 0;
        IgnoreNotActiveGameObject = false;
        BatchMade = false;
        IgnoreVerticesUV = false;
        IgnoreVerticesNormal = false;
        IgnoreVerticesTangent = false;
        IgnoreVerticesColor = false;
        AutoVerticesUV1 = true;
        CustomizeDirectory = false;
        CustomizeDirectoryName = "";
        _SAVEPATH = "Assets";
        _SHAREDRESOURCEPATH = "";
        _PreserveExistingShaderFiles = false;
        EnableCustomShaderExport = false;
    }
}
