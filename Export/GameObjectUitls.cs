using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

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
}

public enum DefindsFrom
{
    floatValue = 0,
    keyWorld = 1,
    HasProps = 2,
    TextureValue = 3
}


public class DefindsValue
{
    private string _keyName;
    private DefindsFrom _from;
    private float _value;
    public DefindsValue(string keyName, DefindsFrom from, float value)
    {
        this._keyName = keyName;
        this._from = from;
        this._value = value;
    }
    public string keyName
    {
        get
        {
            return this._keyName;
        }
    }

    public DefindsFrom from
    {
        get
        {
            return this._from;
        }
    }

    public float data
    {
        get
        {
            return this._value;
        }
    }
}

public class FloatConfig
{
    public string keyName;
    public bool isGamma;
    public string rule;

}

public class TextureConfig
{
    public string keyName;
    public bool isNormal;
    public TextureConfig(string keyname, bool isNomal)
    {
        this.keyName = keyname;
        this.isNormal = isNomal;
    }
}

public class ConditionConfig
{
    public object data;
    public string target;
    public string targetName;
    public string ruleType;
    public string ruleKeyName;
    /*  public ConditionConfig(string ruleType, string ruleKeyName, string target, object data)
      {
          this.data = data;
          this.ruleType = ruleType;
          this.ruleKeyName = ruleKeyName;
          this.target = target;
      }*/
}

public class PropDatasConfig
{
    private Dictionary<string, ConditionConfig> _rules;
    private Dictionary<string, TextureConfig> _pictureList;
    private Dictionary<string, FloatConfig> _floatLists;
    private Dictionary<string, string> _colorLists;
    private Dictionary<string, string> _colorHdrLists;
    private Dictionary<string, string> _tillOffsetLists;
    private Dictionary<string, DefindsValue> _defindsLists;
    private string _materName;
    public PropDatasConfig(string lmaterName)
    {
        this._materName = lmaterName;
        this._rules = new Dictionary<string, ConditionConfig>();
        this._pictureList = new Dictionary<string, TextureConfig>();
        this._floatLists = new Dictionary<string, FloatConfig>();
        this._colorLists = new Dictionary<string, string>();
        this._colorHdrLists = new Dictionary<string, string>();
        this._tillOffsetLists = new Dictionary<string, string>();
        this._defindsLists = new Dictionary<string, DefindsValue>();
    }

    public void addTextureProps(string uprops, string lprops, string definde = null, bool isnormal = false)
    {
        this._pictureList.Add(uprops, new TextureConfig(lprops, isnormal));
        if (definde != null)
        {
            this.addDefineds(uprops, definde, DefindsFrom.TextureValue, 0.0f);
        }
    }
    public void addFloatProps(string uprops, string lprops, bool isgamma = false, string rule = null)
    {
        FloatConfig floatdata = new FloatConfig();
        floatdata.keyName = lprops;
        floatdata.isGamma = isgamma;
        floatdata.rule = rule;
        this._floatLists.Add(uprops, floatdata);

    }
    public void addColorProps(string uprops, string lprops, string otherName = null)
    {
        this._colorLists.Add(uprops, lprops);
        if (otherName != null)
        {
            this._colorHdrLists.Add(uprops, otherName);
        }
    }

    public void addTillOffsetProps(string uprops, string lprops)
    {
        this._tillOffsetLists.Add(uprops, lprops);
    }

    public void addDefineds(string uprops, string lprops, DefindsFrom from, float value = 0)
    {
        if (this._defindsLists.ContainsKey(uprops))
        {
            return;
        }
        this._defindsLists.Add(uprops, new DefindsValue(lprops, from, value));
    }
    public Dictionary<string, ConditionConfig> rules
    {
        get
        {
            return this._rules;
        }
    }
    public Dictionary<string, FloatConfig> floatLists
    {
        get
        {
            return this._floatLists;
        }
    }

    public Dictionary<string, TextureConfig> pictureList
    {
        get
        {
            return this._pictureList;
        }
    }

    public Dictionary<string, string> colorLists
    {
        get
        {
            return this._colorLists;
        }
    }

    public Dictionary<string, string> colorHdrLists
    {
        get
        {
            return this._colorHdrLists;
        }
    }

    public Dictionary<string, string> tillOffsetLists
    {
        get
        {
            return this._tillOffsetLists;
        }
    }

    public Dictionary<string, DefindsValue> defindsLists
    {
        get
        {
            return this._defindsLists;
        }
    }

    public string materalName
    {
        get
        {
            return this._materName;
        }
    }

    public static int GetCull(Material material)
    {
        if (material.HasProperty("_Cull"))
        {
            return material.GetInt("_Cull");
        }
        else
        {
            return 2;
        }
    }

    public static int GetBlend(Material material)
    {
        if (material.IsKeywordEnabled("_ALPHABLEND_ON"))
        {
            return 1;
        }
        else
        {
            return 0;
        }
    }

    public static int GetSrcBlend(Material material)
    {
        if (material.HasProperty("_SrcBlend"))
        {
            switch (material.GetInt("_SrcBlend"))
            {
                case 0:
                    return 0;
                case 1:
                    return 1;
                case 2:
                    return 4;
                case 3:
                    return 2;
                case 4:
                    return 5;
                case 5:
                    return 6;
                case 6:
                    return 3;
                case 7:
                    return 8;
                case 8:
                    return 9;
                case 9:
                    return 4;
                case 10:
                    return 7;
                default:
                    return 1;
            }
        }
        else
        {
            return 1;
        }
    }
    public static int GetDstBlend(Material material)
    {
        if (material.HasProperty("_DstBlend"))
        {
            switch (material.GetInt("_DstBlend"))
            {
                case 0:
                    return 0;
                case 1:
                    return 1;
                case 2:
                    return 4;
                case 3:
                    return 2;
                case 4:
                    return 5;
                case 5:
                    return 6;
                case 6:
                    return 3;
                case 7:
                    return 8;
                case 8:
                    return 9;
                case 9:
                    return 4;
                case 10:
                    return 7;
                default:
                    return 0;
            }
        }
        else
        {
            return 0;
        }
    }

    public static bool GetZWrite(Material material)
    {
        if (material.HasProperty("_ZWrite"))
        {
            if (material.GetInt("_ZWrite") == 1)
            {
                return true;
            }
            else
            {
                return false;
            }
        }
        else
        {
            return true;
        }
    }

    public static int GetZTest(Material material)
    {
        if (material.HasProperty("_ZTest"))
        {
            switch (material.GetInt("_ZTest"))
            {
                case 0:
                    return 0;
                case 1:
                    return 0;
                case 2:
                    return 1;
                case 3:
                    return 2;
                case 4:
                    return 3;
                case 5:
                    return 4;
                case 6:
                    return 5;
                case 7:
                    return 6;
                case 8:
                    return 7;
                default:
                    return 0;
            }
        }
        else
        {
            return 3;
        }
    }

    public static bool GetVerterColor(Material material)
    {
        return material.GetInt("_IsVertexColor") == 0 ? false : true;
    }

    public static bool GetAlphaTest(Material material)
    {
        if (material.IsKeywordEnabled("_ALPHATEST_ON"))
        {
            return true;
        }
        else
        {
            return false;
        }
    }

    public static float GetAlphaTestValue(Material material)
    {
        if (material.HasProperty("_Cutoff"))
        {
            return material.GetFloat("_Cutoff");
        }
        else
        {
            return 0.5f;
        }
    }

    public static int GetRenderModule(Material material)
    {
        string result = material.GetTag("RenderType", true);
        if (result == "Opaque")
        {
            return 0;
        }
        else if (result == "Cutout")
        {
            return 1;
        }
        else if (result == "Transparent")
        {
            return 2;
        }
        else if (result == "Fade")
        {
            return 5;
        }
        else
        {
            return 0;
        }
    }
}

//��������
public class VertexData
{
    public int index;
    public Vector3 vertice;
    public Vector3 normal;
    public Color color;
    public Vector2 uv;
    public Vector2 uv2;
    public Vector4 boneWeight;
    public Vector4 boneIndex;
    public Vector4 tangent;
    //�Ƿ��Ѿ������������иı�boneindex
    public bool ischange = true;
    //�ж�index����
    public int subMeshindex = -1;
    public int subsubMeshindex = -1;
    public Dictionary<string, int> commonPoint;

    public void setValue(VertexData othervertexdata)
    {
        vertice = othervertexdata.vertice;
        normal = othervertexdata.normal;
        color = othervertexdata.color;
        uv = othervertexdata.uv;
        uv2 = othervertexdata.uv2;
        boneWeight = othervertexdata.boneWeight;
        tangent = othervertexdata.tangent;
        boneIndex = new Vector4(othervertexdata.boneIndex.x, othervertexdata.boneIndex.y, othervertexdata.boneIndex.z, othervertexdata.boneIndex.w);
    }
}
//����������
public class Triangle
{
    public VertexData point1;
    public VertexData point2;
    public VertexData point3;
}


class GameObjectUitls
{
    private static string LaniVersion = "LAYAANIMATION:04";
    public static Dictionary<string, PropDatasConfig> MaterialPropsConfigs;
    public static Dictionary<string, string> searchCompoment;

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
        }

        JSONObject metaDatas = JSONObject.Create(File.ReadAllText("Assets/LayaAir3.0UnityPlugin/MetarialPropData.json"));
        MaterialPropsConfigs = new Dictionary<string, PropDatasConfig>();
        int count = metaDatas.Count;
        for (int i = 0; i < count; i++)
        {
            string key = metaDatas.keys[i];
            JSONObject mJdata = metaDatas.GetField(key);
            PropDatasConfig propdata = new PropDatasConfig(mJdata.GetField("targeName").str);
            MaterialPropsConfigs.Add(key, propdata);
            JSONObject textures = mJdata["textures"];
            if (textures != null)
            {
                int texureCount = textures.Count;
                for (int tindex = 0; tindex < texureCount; tindex++)
                {
                    JSONObject texture = textures[tindex];
                    string uName = texture.GetField("uName").str;
                    string lName = texture.GetField("layaName").str;
                    string defined = null;
                    bool isNormal = false;
                    if (texture.GetField("defind") != null)
                    {
                        defined = texture.GetField("defind").str;

                    }
                    if (texture.GetField("isNormal") != null)
                    {
                        isNormal = texture.GetField("isNormal").b;

                    }
                    propdata.addTextureProps(uName, lName, defined, isNormal);
                }
            }
            JSONObject colors = mJdata["colors"];
            if (colors != null)
            {
                int colorCount = colors.Count;
                for (int cindex = 0; cindex < colorCount; cindex++)
                {
                    JSONObject color = colors[cindex];
                    string uName = color.GetField("uName").str;
                    string lName = color.GetField("layaName").str;
                    if (color.GetField("hdrName") != null)
                    {
                        propdata.addColorProps(uName, lName, color.GetField("hdrName").str);
                    }
                    else
                    {
                        propdata.addColorProps(uName, lName);
                    }
                }
            }
            JSONObject floatDatas = mJdata["floats"];
            if (floatDatas != null)
            {
                int floatCount = floatDatas.Count;
                for (int floatIndex = 0; floatIndex < floatCount; floatIndex++)
                {
                    JSONObject floatData = floatDatas[floatIndex];
                    string uName = floatData.GetField("uName").str;
                    string lName = floatData.GetField("layaName").str;
                    bool isGama = false;
                    if (floatData.GetField("isgama") != null)
                    {
                        isGama = floatData.GetField("isgama").b;
                    }
                    string rule = null;
                    if (floatData.GetField("rule") != null)
                    {
                        rule = floatData.GetField("rule").str;
                    }

                    propdata.addFloatProps(uName, lName, isGama, rule);
                }
            }
            JSONObject tillOffset = mJdata["tillOffset"];
            if (tillOffset != null)
            {
                int tillOffsetCount = tillOffset.Count;
                for (int tOffsetIndex = 0; tOffsetIndex < tillOffsetCount; tOffsetIndex++)
                {
                    JSONObject tOffsetData = tillOffset[tOffsetIndex];
                    string uName = tOffsetData.GetField("uName").str;
                    string lName = tOffsetData.GetField("layaName").str;
                    propdata.addTillOffsetProps(uName, lName);
                }
            }
            JSONObject definedDatas = mJdata["defineds"];
            if (definedDatas != null)
            {
                int definedCount = definedDatas.Count;
                for (int defindIndex = 0; defindIndex < definedCount; defindIndex++)
                {
                    JSONObject definedData = definedDatas[defindIndex];
                    string uName = definedData.GetField("uName").str;
                    string lName = definedData.GetField("layaName").str;
                    DefindsFrom from = (DefindsFrom)definedData.GetField("from").n;
                    if (definedData.GetField("deflat") != null)
                    {
                        propdata.addDefineds(uName, lName, from, definedData.GetField("deflat").n);
                    }
                    else
                    {
                        propdata.addDefineds(uName, lName, from);
                    }
                }
            }
            JSONObject rules = mJdata["rules"];
            if (rules != null)
            {
                int ruleCount = rules.Count;
                for (var rindex = 0; rindex < ruleCount; rindex++)
                {
                    JSONObject rule = rules[rindex];
                    ConditionConfig ruleConfig = new ConditionConfig();
                    ruleConfig.target = rule.GetField("target").str;
                    ruleConfig.targetName = rule.GetField("targetName").str;
                    ruleConfig.ruleType = rule.GetField("ruleType").str;
                    ruleConfig.ruleKeyName = rule.GetField("ruleKeyName").str;
                    if (ruleConfig.ruleType == "texture")
                    {
                        ruleConfig.data = rule.GetField("data").b;
                    }
                    else
                    {
                        ruleConfig.data = rule.GetField("data").n;
                    }
                    propdata.rules.Add(rule.GetField("name").str, ruleConfig);
                }
            }
        }


    }
    private static string LmVersion = "LAYAMODEL:0501";
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

    public static List<ComponentType> componentsOnGameObject(GameObject gameObject)
    {

        List<ComponentType> components = new List<ComponentType>();

        Camera camera = gameObject.GetComponent<Camera>();

        Light light = gameObject.GetComponent<Light>();

        MeshFilter meshFilter = gameObject.GetComponent<MeshFilter>();
        MeshRenderer meshRenderer = gameObject.GetComponent<MeshRenderer>();

        SkinnedMeshRenderer skinnedMeshRenderer = gameObject.GetComponent<SkinnedMeshRenderer>();

        Animation animation = gameObject.GetComponent<Animation>();
        Animator animator = gameObject.GetComponent<Animator>();

        ParticleSystem particleSystem = gameObject.GetComponent<ParticleSystem>();

        Terrain terrain = gameObject.GetComponent<Terrain>();

        BoxCollider boxCollider = gameObject.GetComponent<BoxCollider>();
        SphereCollider sphereCollider = gameObject.GetComponent<SphereCollider>();
        CapsuleCollider capsuleCollider = gameObject.GetComponent<CapsuleCollider>();
        //���Meshcoliser
        MeshCollider meshcollider = gameObject.GetComponent<MeshCollider>();



        Rigidbody rigidbody = gameObject.GetComponent<Rigidbody>();

        TrailRenderer trailRenderer = gameObject.GetComponent<TrailRenderer>();

        LineRenderer lineRenderer = gameObject.GetComponent<LineRenderer>();

        FixedJoint fixedJoint = gameObject.GetComponent<FixedJoint>();
        ConfigurableJoint configurableJoint = gameObject.GetComponent<ConfigurableJoint>();

        // todo ͬʱ���ڼ��, ��֧�� reflectionProbe ������������� 
        ReflectionProbe reflectionProbe = gameObject.GetComponent<ReflectionProbe>();
        LODGroup lodGroup = gameObject.GetComponent<LODGroup>();

        components.Add(ComponentType.Transform);
        if (lodGroup != null)
        {
            components.Add(ComponentType.LodGroup);
        }
        // reflectionProbe
        if (reflectionProbe != null)
        {
            components.Add(ComponentType.ReflectionProbe);
        }

        // fixed joint
        if (fixedJoint != null)
        {
            components.Add(ComponentType.Fixedjoint);
        }

        // configurableJoint QTE
        if (configurableJoint != null)
        {
            components.Add(ComponentType.ConfigurableJoint);
        }

        //Line Renderer
        if (lineRenderer != null)
        {
            components.Add(ComponentType.LineRenderer);
        }
        //Trail Renderer
        if (trailRenderer != null)
        {
            components.Add(ComponentType.TrailRenderer);
        }
        //Rigidbody
        if (rigidbody != null)
        {
            components.Add(ComponentType.Rigidbody3D);
        }
        //PhysicsCollider
        else if (boxCollider != null || sphereCollider != null || capsuleCollider != null || meshcollider != null)
        {
            components.Add(ComponentType.PhysicsCollider);
        }

        //Animator
        if (animator != null)
        {
            components.Add(ComponentType.Animator);
        }
        //Camera
        if (camera != null)
        {
            components.Add(ComponentType.Camera);
        }
        //�ƹ�
        if (light != null)
        {
            if (light.type == LightType.Directional)
            {
                components.Add(ComponentType.DirectionalLight);
            }
            else if (light.type == LightType.Point)
            {
                components.Add(ComponentType.PointLight);
            }
            else if (light.type == LightType.Spot)
            {
                components.Add(ComponentType.SpotLight);
            }
        }
        //MeshFilter
        if (meshFilter != null)
        {
            if (camera == null)
            {
                components.Add(ComponentType.MeshFilter);
                if (meshRenderer == null)
                {
                    Debug.LogWarning("LayaAir3D : " + gameObject.name + " need a MeshRenderer ComponentType !");
                }
            }
            else
            {
                if (camera != null)
                {
                    Debug.LogWarning("LayaAir3D : " + gameObject.name + " Camera and MeshFilter can't exist at the same time !");
                }
            }
        }
        //MeshRenderer
        if (meshRenderer != null)
        {
            if (camera == null)
            {
                components.Add(ComponentType.MeshRenderer);
                if (meshFilter == null)
                {
                    Debug.LogWarning("LayaAir3D : " + gameObject.name + " need a meshFilter ComponentType !");
                }
            }
            else
            {
                if (camera != null)
                {
                    Debug.LogWarning("LayaAir3D : " + gameObject.name + " Camera and MeshRenderer can't exist at the same time !");
                }
            }
        }
        //SkinnedMeshRenderer
        if (skinnedMeshRenderer != null)
        {
            if (camera == null && meshFilter == null && meshRenderer == null)
            {
                components.Add(ComponentType.SkinnedMeshRenderer);
            }
            else
            {
                if (camera != null)
                {
                    Debug.LogWarning("LayaAir3D : " + gameObject.name + " Camera and SkinnedMeshRenderer can't exist at the same time !");
                }
                if (meshFilter != null)
                {
                    Debug.LogWarning("LayaAir3D : " + gameObject.name + " MeshFilter and SkinnedMeshRenderer can't exist at the same time !");
                }
                if (meshRenderer != null)
                {
                    Debug.LogWarning("LayaAir3D : " + gameObject.name + " MeshRenderer and SkinnedMeshRenderer can't exist at the same time !");
                }
            }
        }
        //ParticleSystem
        if (particleSystem != null)
        {
            if (camera == null && meshFilter == null && meshRenderer == null && skinnedMeshRenderer == null)
            {
                components.Add(ComponentType.ParticleSystem);
            }
            else
            {
                if (camera != null)
                {
                    Debug.LogWarning("LayaAir3D : " + gameObject.name + " Camera and ParticleSystem can't exist at the same time !");
                }
                if (meshFilter != null)
                {
                    Debug.LogWarning("LayaAir3D : " + gameObject.name + " MeshFilter and ParticleSystem can't exist at the same time !");
                }
                if (meshRenderer != null)
                {
                    Debug.LogWarning("LayaAir3D : " + gameObject.name + " MeshRenderer and ParticleSystem can't exist at the same time !");
                }
                if (skinnedMeshRenderer != null)
                {
                    Debug.LogWarning("LayaAir3D : " + gameObject.name + " SkinnedMeshRenderer and ParticleSystem can't exist at the same time !");
                }
            }
        }
        //Terrain
        if (terrain != null)
        {
            if (camera == null && meshFilter == null && meshRenderer == null && skinnedMeshRenderer == null && particleSystem == null)
            {
                components.Add(ComponentType.Terrain);
            }
            else
            {
                if (camera != null)
                {
                    Debug.LogWarning("LayaAir3D : " + gameObject.name + " Camera and Terrain can't exist at the same time !");
                }
                if (meshFilter != null)
                {
                    Debug.LogWarning("LayaAir3D : " + gameObject.name + " MeshFilter and Terrain can't exist at the same time !");
                }
                if (meshRenderer != null)
                {
                    Debug.LogWarning("LayaAir3D : " + gameObject.name + " MeshRenderer and Terrain can't exist at the same time !");
                }
                if (skinnedMeshRenderer != null)
                {
                    Debug.LogWarning("LayaAir3D : " + gameObject.name + " SkinnedMeshRenderer and Terrain can't exist at the same time !");
                }
                if (particleSystem != null)
                {
                    Debug.LogWarning("LayaAir3D : " + gameObject.name + " ParticleSystem and Terrain can't exist at the same time !");
                }
            }
        }

        return components;
    }
    private static int[] VertexStructure = new int[7];
    public static void writeMesh(Mesh mesh, string meshName, FileStream fs)
    {
        int i, j;
        UInt16 subMeshCount = (UInt16)mesh.subMeshCount;
        int blockCount = subMeshCount + 1;

        UInt16 everyVBSize = 0;
        string vbDeclaration = "";


        //��ȡ����ṹ,0�����ö���ṹ�޴�����;1����֮
        //�������������ܴܺ�Ϊ���Ż�Ч�ʣ�Ĭ�϶����λ�ã����ߣ�uv������Ȩ�ش��ڣ�����tangents
        for (i = 0; i < VertexStructure.Length; i++)
            VertexStructure[i] = 0;

        Vector3[] vertices = mesh.vertices;
        Vector3[] normals = mesh.normals;
        Color[] colors = mesh.colors;
        Vector2[] uv = mesh.uv;
        Vector4[] tangents = mesh.tangents;
        if (vertices != null && vertices.Length != 0)
        {
            VertexStructure[0] = 1;
            vbDeclaration += "POSITION";
            everyVBSize += 12;
        }

        if (normals != null && normals.Length != 0 && !ExportConfig.IgnoreVerticesNormal)
        {
            VertexStructure[1] = 1;
            vbDeclaration += ",NORMAL";
            everyVBSize += 12;
        }

        if (colors != null && colors.Length != 0 && !ExportConfig.IgnoreVerticesColor)
        {
            VertexStructure[2] = 1;
            vbDeclaration += ",COLOR";
            everyVBSize += 16;
        }

        if (uv != null && uv.Length != 0 && !ExportConfig.IgnoreVerticesUV)
        {
            VertexStructure[3] = 1;
            vbDeclaration += ",UV";
            everyVBSize += 8;
        }



        if (tangents != null && tangents.Length != 0 && !ExportConfig.IgnoreVerticesTangent)
        {
            VertexStructure[6] = 1;
            vbDeclaration += ",TANGENT";
            everyVBSize += 16;
        }

        int[] subMeshFirstIndex = new int[subMeshCount];
        int[] subMeshIndexLength = new int[subMeshCount];

        for (i = 0; i < subMeshCount; i++)
        {
            int[] subIndices = mesh.GetIndices(i);
            subMeshFirstIndex[i] = subIndices[0];
            subMeshIndexLength[i] = subIndices.Length;
        }

        long VerionSize = 0;

        long ContentAreaPosition_Start = 0;

        long MeshAreaPosition_Start = 0;
        long MeshAreaPosition_End = 0;
        long MeshAreaSize = 0;
        long VBMeshAreaPosition_Start = 0;
        long IBMeshAreaPosition_Start = 0;
        long BoneAreaPosition_Start = 0;

        long BlockAreaPosition_Start = 0;

        long StringAreaPosition_Start = 0;
        long StringAreaPosition_End = 0;

        long StringDatasAreaPosition_Start = 0;
        long StringDatasAreaPosition_End = 0;
        long StringDatasAreaSize = 0;

        long VBContentDatasAreaPosition_Start = 0;
        long VBContentDatasAreaPosition_End = 0;
        long VBContentDatasAreaSize = 0;

        long IBContentDatasAreaPosition_Start = 0;
        long IBContentDatasAreaPosition_End = 0;
        long IBContentDatasAreaSize = 0;

        long[] subMeshAreaPosition_Start = new long[subMeshCount];
        long[] subMeshAreaPosition_End = new long[subMeshCount];
        long[] subMeshAreaSize = new long[subMeshCount];

        List<string> stringDatas = new List<string>();
        stringDatas.Add("MESH");
        stringDatas.Add("SUBMESH");

        //�汾��
        Util.FileUtil.WriteData(fs, LmVersion);
        VerionSize = fs.Position;

        //���������Ϣ��
        ContentAreaPosition_Start = fs.Position; // Ԥ��������ƫ�Ƶ�ַ
        Util.FileUtil.WriteData(fs, (UInt32)0);//UInt32 offset
        Util.FileUtil.WriteData(fs, (UInt32)0);//UInt32 blockLength

        //���ݶ�����Ϣ��
        BlockAreaPosition_Start = fs.Position;//Ԥ����������

        Util.FileUtil.WriteData(fs, (UInt16)blockCount);
        for (i = 0; i < blockCount; i++)
        {
            Util.FileUtil.WriteData(fs, (UInt32)0);//UInt32 blockStart
            Util.FileUtil.WriteData(fs, (UInt32)0);//UInt32 blockLength
        }

        //�ַ���
        StringAreaPosition_Start = fs.Position;//Ԥ���ַ���
        Util.FileUtil.WriteData(fs, (UInt32)0);//UInt32 offset
        Util.FileUtil.WriteData(fs, (UInt16)0);//count

        //������
        MeshAreaPosition_Start = fs.Position;
        Util.FileUtil.WriteData(fs, (UInt16)stringDatas.IndexOf("MESH"));//�����������ַ�����
        stringDatas.Add(meshName);
        Util.FileUtil.WriteData(fs, (UInt16)stringDatas.IndexOf(meshName));//�������ַ�����

        //vb
        Util.FileUtil.WriteData(fs, (UInt16)1);//vb����
        VBMeshAreaPosition_Start = fs.Position;
        for (i = 0; i < 1; i++)//vb
        {
            Util.FileUtil.WriteData(fs, (UInt32)0);//vbStart
            Util.FileUtil.WriteData(fs, (UInt32)0);//vbLength

            stringDatas.Add(vbDeclaration);
            Util.FileUtil.WriteData(fs, (UInt16)stringDatas.IndexOf(vbDeclaration));//vbDeclar
        }

        //ib
        IBMeshAreaPosition_Start = fs.Position;
        Util.FileUtil.WriteData(fs, (UInt32)0);//ibStart
        Util.FileUtil.WriteData(fs, (UInt32)0);//ibLength

        mesh.RecalculateBounds();
        Bounds bound = mesh.bounds;
        Util.FileUtil.WriteData(fs, -(float)bound.max.x);
        Util.FileUtil.WriteData(fs, (float)bound.min.y);
        Util.FileUtil.WriteData(fs, (float)bound.min.z);
        Util.FileUtil.WriteData(fs, -(float)bound.min.x);
        Util.FileUtil.WriteData(fs, (float)bound.max.y);
        Util.FileUtil.WriteData(fs, (float)bound.max.z);

        BoneAreaPosition_Start = fs.Position;



        //uint16 boneCount
        Util.FileUtil.WriteData(fs, (UInt16)0);//boneCount

        Util.FileUtil.WriteData(fs, (UInt32)0);//bindPoseStart
        Util.FileUtil.WriteData(fs, (UInt32)0);//bindPoseLength
        Util.FileUtil.WriteData(fs, (UInt32)0);//inverseGlobalBindPoseStart
        Util.FileUtil.WriteData(fs, (UInt32)0);//inverseGlobalBindPoseLength

        MeshAreaPosition_End = fs.Position;
        MeshAreaSize = MeshAreaPosition_End - MeshAreaPosition_Start;

        //��������
        for (i = 0; i < subMeshCount; i++)
        {
            subMeshAreaPosition_Start[i] = fs.Position;

            Util.FileUtil.WriteData(fs, (UInt16)stringDatas.IndexOf("SUBMESH"));//�����������ַ�����
            Util.FileUtil.WriteData(fs, (UInt16)0);//vbIndex

            Util.FileUtil.WriteData(fs, (UInt32)0);//ibStart
            Util.FileUtil.WriteData(fs, (UInt32)0);//ibLength

            Util.FileUtil.WriteData(fs, (UInt16)1);//drawCount

            Util.FileUtil.WriteData(fs, (UInt32)0);//subIbStart
            Util.FileUtil.WriteData(fs, (UInt32)0);//subIbLength

            Util.FileUtil.WriteData(fs, (UInt32)0);//boneDicStart
            Util.FileUtil.WriteData(fs, (UInt32)0);//boneDicLength

            subMeshAreaPosition_End[i] = fs.Position;

            subMeshAreaSize[i] = subMeshAreaPosition_End[i] - subMeshAreaPosition_Start[i];
        }

        //�ַ�������
        StringDatasAreaPosition_Start = fs.Position;
        for (i = 0; i < stringDatas.Count; i++)
        {
            Util.FileUtil.WriteData(fs, stringDatas[i]);
        }
        StringDatasAreaPosition_End = fs.Position;
        StringDatasAreaSize = StringDatasAreaPosition_End - StringDatasAreaPosition_Start;

        //����������
        //vb
        Vector3 vertice;
        Vector3 normal;
        Color color;
        Vector2 uvs;
        Vector4 tangent;
        VBContentDatasAreaPosition_Start = fs.Position;

        for (j = 0; j < mesh.vertexCount; j++)
        {
            vertice = vertices[j];
            Util.FileUtil.WriteData(fs, -vertice.x, vertice.y, vertice.z);
            //����8
            if (VertexStructure[1] == 1)
            {
                normal = normals[j];
                Util.FileUtil.WriteData(fs, -normal.x, normal.y, normal.z);
            }
            //��ɫ
            if (VertexStructure[2] == 1)
            {
                color = colors[j];
                Util.FileUtil.WriteData(fs, color.r, color.g, color.b, color.a);
            }
            //uv
            if (VertexStructure[3] == 1)
            {
                uvs = uv[j];
                Util.FileUtil.WriteData(fs, uvs.x, uvs.y * -1.0f + 1.0f);
            }
            /*  //uv2
              if (VertexStructure[4] == 1)
              {
                  uv2s = uv2[j];
                  Util.FileUtil.WriteData(fs, uv2s.x, uv2s.y * -1.0f + 1.0f);
              }*/
            //����
            if (VertexStructure[6] == 1)
            {
                tangent = tangents[j];
                Util.FileUtil.WriteData(fs, -tangent.x, tangent.y, tangent.z, tangent.w);
            }
        }

        VBContentDatasAreaPosition_End = fs.Position;
        VBContentDatasAreaSize = VBContentDatasAreaPosition_End - VBContentDatasAreaPosition_Start;

        //indices
        //TODO:3.0 δ�������Ǵ���lm��
        IBContentDatasAreaPosition_Start = fs.Position;
        int[] triangles = mesh.triangles;
        if (mesh.indexFormat == IndexFormat.UInt32 && mesh.vertexCount > 65535)
        {
            for (j = 0; j < triangles.Length; j++)
            {
                Util.FileUtil.WriteData(fs, (UInt32)triangles[j]);
            }
        }
        else
        {
            for (j = 0; j < triangles.Length; j++)
            {
                Util.FileUtil.WriteData(fs, (UInt16)triangles[j]);
            }
        }

        IBContentDatasAreaPosition_End = fs.Position;
        IBContentDatasAreaSize = IBContentDatasAreaPosition_End - IBContentDatasAreaPosition_Start;

        //������������
        UInt32 ibstart = 0;
        UInt32 iblength = 0;
        UInt32 _ibstart = 0;
        for (i = 0; i < subMeshCount; i++)
        {
            fs.Position = subMeshAreaPosition_Start[i] + 4;

            if (subMeshCount == 1)
            {
                ibstart = 0;
                iblength = mesh.indexFormat == IndexFormat.UInt32 ? (UInt32)(IBContentDatasAreaSize / 4) : (UInt32)(IBContentDatasAreaSize / 2);
            }
            else if (i == 0)
            {
                ibstart = _ibstart;
                iblength = (UInt32)subMeshIndexLength[i];
            }
            else if (i < subMeshCount - 1)
            {
                ibstart = (UInt32)_ibstart;
                iblength = (UInt32)subMeshIndexLength[i];
            }
            else
            {
                ibstart = (UInt32)_ibstart;
                iblength = (UInt32)subMeshIndexLength[i];
            }

            Util.FileUtil.WriteData(fs, ibstart);
            Util.FileUtil.WriteData(fs, iblength);
            _ibstart += iblength;

            fs.Position += 2;

            Util.FileUtil.WriteData(fs, ibstart);
            Util.FileUtil.WriteData(fs, iblength);
        }

        //����������
        fs.Position = VBMeshAreaPosition_Start;
        Util.FileUtil.WriteData(fs, (UInt32)(VBContentDatasAreaPosition_Start - StringDatasAreaPosition_Start));
        Util.FileUtil.WriteData(fs, (UInt32)mesh.vertexCount);

        fs.Position = IBMeshAreaPosition_Start;
        Util.FileUtil.WriteData(fs, (UInt32)(IBContentDatasAreaPosition_Start - StringDatasAreaPosition_Start));
        Util.FileUtil.WriteData(fs, (UInt32)IBContentDatasAreaSize);

        //�����ַ���
        fs.Position = StringAreaPosition_Start;
        Util.FileUtil.WriteData(fs, (UInt32)0);
        Util.FileUtil.WriteData(fs, (UInt16)stringDatas.Count);
        StringAreaPosition_End = fs.Position;

        //���ƶ�����
        fs.Position = BlockAreaPosition_Start + 2;
        Util.FileUtil.WriteData(fs, (UInt32)MeshAreaPosition_Start);
        Util.FileUtil.WriteData(fs, (UInt32)MeshAreaSize);
        for (i = 0; i < subMeshCount; i++)
        {
            Util.FileUtil.WriteData(fs, (UInt32)subMeshAreaPosition_Start[i]);
            Util.FileUtil.WriteData(fs, (UInt32)subMeshAreaSize[i]);
        }

        //���Ʊ������������Ϣ��
        fs.Position = ContentAreaPosition_Start;
        Util.FileUtil.WriteData(fs, (UInt32)StringDatasAreaPosition_Start);
        Util.FileUtil.WriteData(fs, (UInt32)(StringDatasAreaPosition_Start + StringDatasAreaSize + VBContentDatasAreaSize + IBContentDatasAreaSize + subMeshAreaSize[0]));

        fs.Close();
    }

    public static void writeSkinnerMesh(SkinnedMeshRenderer skinnedMeshRenderer, string meshName, FileStream fs, int MaxBoneCount = 24)
    {
        Mesh mesh = skinnedMeshRenderer.sharedMesh;
        //-------------------------------------------��֯��д������-------------------------------------------
        UInt16 vbCount = (UInt16)1;//unity��Ĭ��������65535
        UInt16 subMeshCount = (UInt16)mesh.subMeshCount;
        UInt16 everyVBSize = 0;
        string vbDeclaration = "";

        //��ȡ����ṹ,0�����ö���ṹ�޴�����;1����֮
        //�������������ܴܺ�Ϊ���Ż�Ч�ʣ�Ĭ�϶����λ�ã����ߣ�uv������Ȩ�ش��ڣ�����triangles
        for (int i = 0; i < VertexStructure.Length; i++)
            VertexStructure[i] = 0;

        if (mesh.vertices != null && mesh.vertices.Length != 0)
        {
            VertexStructure[0] = 1;
            vbDeclaration += "POSITION";
            everyVBSize += 12;
        }

        if (mesh.normals != null && mesh.normals.Length != 0 && !ExportConfig.IgnoreVerticesNormal)
        {
            VertexStructure[1] = 1;
            vbDeclaration += ",NORMAL";
            everyVBSize += 12;
        }

        if (mesh.colors != null && mesh.colors.Length != 0 && !ExportConfig.IgnoreVerticesColor)
        {
            VertexStructure[2] = 1;
            vbDeclaration += ",COLOR";
            everyVBSize += 16;
        }

        if (mesh.uv != null && mesh.uv.Length != 0 && !ExportConfig.IgnoreVerticesUV)
        {
            VertexStructure[3] = 1;
            vbDeclaration += ",UV";
            everyVBSize += 8;
        }

        if (mesh.uv2 != null && mesh.uv2.Length != 0 && !ExportConfig.IgnoreVerticesUV)
        {
            VertexStructure[4] = 1;
            vbDeclaration += ",UV1";
            everyVBSize += 8;
        }

        if (mesh.boneWeights != null && mesh.boneWeights.Length != 0)
        {
            VertexStructure[5] = 1;
            vbDeclaration += ",BLENDWEIGHT,BLENDINDICES";
            everyVBSize += 32;
        }

        if (mesh.tangents != null && mesh.tangents.Length != 0 && !ExportConfig.IgnoreVerticesTangent)
        {
            VertexStructure[6] = 1;
            vbDeclaration += ",TANGENT";
            everyVBSize += 16;
        }

        //��ȡ��������
        List<Transform> bones = new List<Transform>();
        for (int j = 0; j < skinnedMeshRenderer.bones.Length; j++)
        {
            Transform _bone = skinnedMeshRenderer.bones[j];
            if (bones.IndexOf(_bone) == -1)
                bones.Add(_bone);
        }


        //�ع�VB,IB����
        //���е�ļ���
        int vertexlength = mesh.vertexCount;
        List<VertexData> vertexBuffer = new List<VertexData>();


        //����index�ļ���
        List<int> indexBuffer = new List<int>();

        //����subMesh�Լ�������������������ݣ����list����Ϊ24��ÿ��subMesh����һ��list��������Ź���������
        List<List<int>>[] boneIndexList = new List<List<int>>[subMeshCount];
        //subMesh index�����ĳ���
        List<int>[] subIBIndex = new List<int>[subMeshCount];
        List<List<Triangle>>[] subsubMeshtriangles = new List<List<Triangle>>[subMeshCount];

        Vector3[] vertices = mesh.vertices;
        Vector3[] normals = mesh.normals;
        Color[] colors = mesh.colors;
        Vector2[] uvs = mesh.uv;
        Vector2[] uv2s = mesh.uv2;
        BoneWeight[] boneWeights = mesh.boneWeights;
        Vector4[] tangents = mesh.tangents;
        //��֯���еĶ�������
        for (int ii = 0; ii < vertexlength; ii++)
        {
            vertexBuffer.Add(getVertexData(vertices, normals, colors, uvs, uv2s, boneWeights, tangents, ii));
        }


        int[] subMeshFirstIndex = new int[subMeshCount];
        int[] subMeshIndexLength = new int[subMeshCount];
        int _ibLength = 0;
        //ѭ��ÿ��subMesh
        for (int i = 0; i < subMeshCount; i++)
        {
            //���submesh������index
            int[] subIndices = mesh.GetIndices(i);
            //������Ҫ�Ĺ�����������
            boneIndexList[i] = new List<List<int>>();
            boneIndexList[i].Add(new List<int>());
            //���˹�����Ŀ 24��24��24��10
            subIBIndex[i] = new List<int>();
            //���������飬���ݹ���������Ϊ�ü��黮��
            List<List<Triangle>> subsubMeshTriangle = new List<List<Triangle>>();
            subsubMeshtriangles[i] = subsubMeshTriangle;
            //�ض���һ�����������
            subsubMeshTriangle.Add(new List<Triangle>());
            //subMesh�����е�triangle
            List<Triangle> subAllTriangle = new List<Triangle>();
            //��ʼ��֯ib,������е�������
            for (int j = 0, n = subIndices.Length; j < n; j += 3)
            {
                Triangle triangle = new Triangle();
                triangle.point1 = vertexBuffer[subIndices[j]];
                triangle.point2 = vertexBuffer[subIndices[j + 1]];
                triangle.point3 = vertexBuffer[subIndices[j + 2]];
                subAllTriangle.Add(triangle);
            }
            //�������θ��ݹ��������ֶ�
            for (int k = 0; k < subAllTriangle.Count; k++)
            {
                Triangle tri = subAllTriangle[k];
                //������������еĹ�����������
                List<int> tigleboneindexs = triangleBoneIndex(tri);
                //����ѭ�����е�submesh����Ĺ���������list
                bool isAdd = false;
                for (int m = 0; m < boneIndexList[i].Count; m++)
                {
                    List<int> list = listContainCount(tigleboneindexs, boneIndexList[i][m]);
                    //ȫ�����Ͱ�������ȫ�ӽ�ȥ
                    if (list.Count == 0)
                    {
                        subsubMeshTriangle[m].Add(tri);
                        isAdd = true;
                        break;
                    }
                    //����ȫ�����Ϳ��Ƿ���Ϲ�24�����
                    else if ((boneIndexList[i][m].Count + list.Count) <= MaxBoneCount)
                    {
                        for (int c = 0; c < list.Count; c++)
                        {
                            boneIndexList[i][m].Add(list[c]);
                        }

                        subsubMeshTriangle[m].Add(tri);
                        isAdd = true;
                        break;
                    }
                }
                if (!isAdd)
                {
                    List<int> newboneindexlist = new List<int>();
                    List<Triangle> newTriangleList = new List<Triangle>();
                    boneIndexList[i].Add(newboneindexlist);
                    subsubMeshTriangle.Add(newTriangleList);
                    for (int w = 0; w < tigleboneindexs.Count; w++)
                    {
                        newboneindexlist.Add(tigleboneindexs[w]);
                    }
                    newTriangleList.Add(tri);
                }
            }

            //�ֶ�֮�������ӵ㲢���޸�����
            for (int q = 0; q < subsubMeshTriangle.Count; q++)
            {
                List<Triangle> subsubtriangles = subsubMeshTriangle[q];
                for (int h = 0; h < subsubtriangles.Count; h++)
                {
                    Triangle trianglle = subsubtriangles[h];
                    //���������
                    trianglle.point1 = checkPoint(trianglle.point1, i, q, vertexBuffer);
                    trianglle.point2 = checkPoint(trianglle.point2, i, q, vertexBuffer);
                    trianglle.point3 = checkPoint(trianglle.point3, i, q, vertexBuffer);
                }
            }

            int lengths = 0;
            for (int o = 0; o < subsubMeshTriangle.Count; o++)
            {
                lengths += subsubMeshTriangle[o].Count * 3;
                subIBIndex[i].Add(lengths);
            }
        }

        //�л���Ӱ����֯index����
        for (int ii = 0; ii < subMeshCount; ii++)
        {
            List<List<Triangle>> subsubtriangle = subsubMeshtriangles[ii];
            for (int tt = 0; tt < subsubtriangle.Count; tt++)
            {
                List<int> boneindexlist = boneIndexList[ii][tt];
                for (int iii = 0; iii < subsubtriangle[tt].Count; iii++)
                {
                    Triangle trii = subsubtriangle[tt][iii];
                    changeBoneIndex(boneindexlist, trii.point3);
                    changeBoneIndex(boneindexlist, trii.point2);
                    changeBoneIndex(boneindexlist, trii.point1);

                    indexBuffer.Add(trii.point1.index);
                    indexBuffer.Add(trii.point2.index);
                    indexBuffer.Add(trii.point3.index);
                }
            }

        }
        for (int i = 0; i < subMeshCount; i++)
        {
            int[] subIndices = mesh.GetIndices(i);
            subMeshFirstIndex[i] = indexBuffer[_ibLength];
            subMeshIndexLength[i] = subIndices.Length;
            _ibLength += subIndices.Length;
        }





        //vertexBuffer[vertexBuffer.Count - 1].boneIndex[2] = 7;
        //Debug.Log(vertexBuffer[vertexBuffer.Count - 1].boneIndex);

        long VerionSize = 0;

        long ContentAreaPosition_Start = 0;

        long MeshAreaPosition_Start = 0;
        long MeshAreaPosition_End = 0;
        long MeshAreaSize = 0;
        long VBMeshAreaPosition_Start = 0;
        long IBMeshAreaPosition_Start = 0;
        long BoneAreaPosition_Start = 0;

        long BlockAreaPosition_Start = 0;

        long StringAreaPosition_Start = 0;
        long StringAreaPosition_End = 0;

        long StringDatasAreaPosition_Start = 0;
        long StringDatasAreaPosition_End = 0;
        long StringDatasAreaSize = 0;

        long VBContentDatasAreaPosition_Start = 0;
        long VBContentDatasAreaPosition_End = 0;
        long VBContentDatasAreaSize = 0;

        long IBContentDatasAreaPosition_Start = 0;
        long IBContentDatasAreaPosition_End = 0;
        long IBContentDatasAreaSize = 0;

        long inverseGlobalBindPosesDatasAreaPosition_Start = 0;

        long boneDicDatasAreaPosition_Start = 0;
        long boneDicDatasAreaPosition_End = 0;

        long[] subMeshAreaPosition_Start = new long[subMeshCount];
        long[] subMeshAreaPosition_End = new long[subMeshCount];
        long[] subMeshAreaSize = new long[subMeshCount];

        List<string> stringDatas = new List<string>();
        stringDatas.Add("MESH");
        stringDatas.Add("SUBMESH");

        //�汾��
        string layaModelVerion = LmVersion;
        Util.FileUtil.WriteData(fs, layaModelVerion);
        VerionSize = fs.Position;

        //���������Ϣ��
        ContentAreaPosition_Start = fs.Position; // Ԥ��������ƫ�Ƶ�ַ
        Util.FileUtil.WriteData(fs, (UInt32)0);//UInt32 offset
        Util.FileUtil.WriteData(fs, (UInt32)0);//UInt32 blockLength

        //���ݶ�����Ϣ��
        BlockAreaPosition_Start = fs.Position;//Ԥ����������
        int blockCount = subMeshCount + 1;
        Util.FileUtil.WriteData(fs, (UInt16)blockCount);
        for (int i = 0; i < blockCount; i++)
        {
            Util.FileUtil.WriteData(fs, (UInt32)0);//UInt32 blockStart
            Util.FileUtil.WriteData(fs, (UInt32)0);//UInt32 blockLength
        }

        //�ַ���
        StringAreaPosition_Start = fs.Position;//Ԥ���ַ���
        Util.FileUtil.WriteData(fs, (UInt32)0);//UInt32 offset
        Util.FileUtil.WriteData(fs, (UInt16)0);//count

        //������

        //������
        MeshAreaPosition_Start = fs.Position;
        Util.FileUtil.WriteData(fs, (UInt16)stringDatas.IndexOf("MESH"));//�����������ַ�����
        stringDatas.Add(meshName);
        Util.FileUtil.WriteData(fs, (UInt16)stringDatas.IndexOf(meshName));//�������ַ�����

        //vb
        Util.FileUtil.WriteData(fs, (UInt16)vbCount);//vb����
        VBMeshAreaPosition_Start = fs.Position;
        //Ĭ��vbCountΪ1
        //for (ushort i = 0; i < vbCount; i++)
        //{
        Util.FileUtil.WriteData(fs, (UInt32)0);//vbStart
        Util.FileUtil.WriteData(fs, (UInt32)0);//vbLength
        stringDatas.Add(vbDeclaration);
        Util.FileUtil.WriteData(fs, (UInt16)stringDatas.IndexOf(vbDeclaration));//vbDeclar
                                                                                //}

        //ib
        IBMeshAreaPosition_Start = fs.Position;
        Util.FileUtil.WriteData(fs, (UInt32)0);//ibStart
        Util.FileUtil.WriteData(fs, (UInt32)0);//ibLength



        //bounds
        mesh.RecalculateBounds();
        Bounds bound = mesh.bounds;
        Util.FileUtil.WriteData(fs, -(float)bound.max.x);
        Util.FileUtil.WriteData(fs, (float)bound.min.y);
        Util.FileUtil.WriteData(fs, (float)bound.min.z);
        Util.FileUtil.WriteData(fs, -(float)bound.min.x);
        Util.FileUtil.WriteData(fs, (float)bound.max.y);
        Util.FileUtil.WriteData(fs, (float)bound.max.z);

        BoneAreaPosition_Start = fs.Position;
        //uint16 boneCount
        Util.FileUtil.WriteData(fs, (UInt16)bones.Count);//boneCount

        for (int i = 0; i < bones.Count; i++)
        {
            stringDatas.Add(bones[i].name);
            Util.FileUtil.WriteData(fs, (UInt16)stringDatas.IndexOf((bones[i].name)));
        }

        Util.FileUtil.WriteData(fs, (UInt32)0);//inverseGlobalBindPoseStart
        Util.FileUtil.WriteData(fs, (UInt32)0);//inverseGlobalBindPoseLength

        MeshAreaPosition_End = fs.Position;
        MeshAreaSize = MeshAreaPosition_End - MeshAreaPosition_Start;

        //��������
        for (int i = 0; i < subMeshCount; i++)
        {
            subMeshAreaPosition_Start[i] = fs.Position;

            Util.FileUtil.WriteData(fs, (UInt16)stringDatas.IndexOf("SUBMESH"));//�����������ַ�����
            Util.FileUtil.WriteData(fs, (UInt16)0);//vbIndex

            Util.FileUtil.WriteData(fs, (UInt32)0);//ibStart
            Util.FileUtil.WriteData(fs, (UInt32)0);//ibLength

            Util.FileUtil.WriteData(fs, (UInt16)boneIndexList[i].Count);//drawCount

            for (int j = 0; j < boneIndexList[i].Count; j++)
            {
                Util.FileUtil.WriteData(fs, (UInt32)0);//ibStart
                Util.FileUtil.WriteData(fs, (UInt32)0);//ibLength

                Util.FileUtil.WriteData(fs, (UInt32)0);//boneDicStart
                Util.FileUtil.WriteData(fs, (UInt32)0);//boneDicLength
            }

            subMeshAreaPosition_End[i] = fs.Position;

            subMeshAreaSize[i] = subMeshAreaPosition_End[i] - subMeshAreaPosition_Start[i];
        }


        //�ַ�������
        StringDatasAreaPosition_Start = fs.Position;

        for (int i = 0; i < stringDatas.Count; i++)
        {
            Util.FileUtil.WriteData(fs, stringDatas[i]);
        }
        StringDatasAreaPosition_End = fs.Position;
        StringDatasAreaSize = StringDatasAreaPosition_End - StringDatasAreaPosition_Start;

        //����������
        //vb
        VBContentDatasAreaPosition_Start = fs.Position;

        VertexData vertexData;
        for (int j = 0; j < vertexBuffer.Count; j++)
        {
            vertexData = vertexBuffer[j];
            Vector3 _vertice = vertexData.vertice;
            Util.FileUtil.WriteData(fs, _vertice.x * -1.0f, _vertice.y, _vertice.z);

            if (VertexStructure[1] == 1)
            {
                Vector3 _normal = vertexData.normal;
                Util.FileUtil.WriteData(fs, _normal.x * -1.0f, _normal.y, _normal.z);
            }

            if (VertexStructure[2] == 1)
            {
                Color _color = vertexData.color;
                Util.FileUtil.WriteData(fs, _color.r, _color.g, _color.b, _color.a);
            }

            if (VertexStructure[3] == 1)
            {
                Vector2 _uv = vertexData.uv;
                Util.FileUtil.WriteData(fs, _uv.x, -_uv.y + 1.0f);
            }

            if (VertexStructure[4] == 1)
            {
                Vector2 _uv2 = vertexData.uv2;
                Util.FileUtil.WriteData(fs, _uv2.x, -_uv2.y + 1.0f);
            }

            if (VertexStructure[5] == 1)
            {
                Vector4 _boneWeight = vertexData.boneWeight;
                Vector4 _boneIndex = vertexData.boneIndex;
                Util.FileUtil.WriteData(fs, _boneWeight.x, _boneWeight.y, _boneWeight.z, _boneWeight.w);
                Util.FileUtil.WriteData(fs, (byte)_boneIndex.x, (byte)_boneIndex.y, (byte)_boneIndex.z, (byte)_boneIndex.w);
            }

            if (VertexStructure[6] == 1)
            {
                Vector4 _tangent = vertexData.tangent;
                Util.FileUtil.WriteData(fs, _tangent.x * -1.0f, _tangent.y, _tangent.z, _tangent.w);
            }
        }



        VBContentDatasAreaPosition_End = fs.Position;
        VBContentDatasAreaSize = VBContentDatasAreaPosition_End - VBContentDatasAreaPosition_Start;


        //indices
        //TODO:δ�������Ǵ���lm��
        IBContentDatasAreaPosition_Start = fs.Position;
        if (mesh.indexFormat == IndexFormat.UInt32 && vertexBuffer.Count > 65535)
        {
            for (int j = 0; j < indexBuffer.Count; j++)
            {
                Util.FileUtil.WriteData(fs, (UInt32)indexBuffer[j]);
            }
        }
        else
        {
            for (int j = 0; j < indexBuffer.Count; j++)
            {
                Util.FileUtil.WriteData(fs, (UInt16)indexBuffer[j]);
            }
        }

        IBContentDatasAreaPosition_End = fs.Position;
        IBContentDatasAreaSize = IBContentDatasAreaPosition_End - IBContentDatasAreaPosition_Start;

        if (mesh.bindposes != null && mesh.bindposes.Length != 0)
        {
            Matrix4x4[] matrix = new Matrix4x4[mesh.bindposes.Length];
            Vector3 position;
            Quaternion quaternion;
            Vector3 scale;
            for (int i = 0; i < mesh.bindposes.Length; i++)
            {
                matrix[i] = mesh.bindposes[i];
                matrix[i] = matrix[i].inverse;
                Util.MathUtil.Decompose(matrix[i].transpose, out scale, out quaternion, out position);
                position.x *= -1.0f;
                quaternion.x *= -1.0f;
                quaternion.w *= -1.0f;
                quaternion.Normalize();
                matrix[i] = Matrix4x4.TRS(position, quaternion, scale);
            }

            //inverseGlobalBindPoses

            inverseGlobalBindPosesDatasAreaPosition_Start = fs.Position;

            for (int i = 0; i < mesh.bindposes.Length; i++)
            {
                Matrix4x4 m4 = matrix[i].inverse;
                for (int j = 0; j < 16; j++)
                {
                    Util.FileUtil.WriteData(fs, m4[j]);

                }
            }

            //boneDic

            boneDicDatasAreaPosition_Start = fs.Position;

            for (int i = 0; i < subMeshCount; i++)
            {
                for (int j = 0; j < boneIndexList[i].Count; j++)
                {
                    for (int k = 0; k < boneIndexList[i][j].Count; k++)
                    {
                        Util.FileUtil.WriteData(fs, (ushort)boneIndexList[i][j][k]);
                    }
                }
            }
            boneDicDatasAreaPosition_End = fs.Position;
        }

        //������������

        UInt32 ibstart = 0;
        UInt32 iblength = 0;
        UInt32 _ibstart = 0;
        long boneDicStart = boneDicDatasAreaPosition_Start - StringDatasAreaPosition_Start;
        for (int i = 0; i < subMeshCount; i++)
        {
            fs.Position = subMeshAreaPosition_Start[i] + 4;

            if (subMeshCount == 1)
            {
                ibstart = 0;
                iblength = mesh.indexFormat == IndexFormat.UInt32 ? (UInt32)(IBContentDatasAreaSize / 4) : (UInt32)(IBContentDatasAreaSize / 2);
            }
            else if (i == 0)
            {
                ibstart = _ibstart;
                iblength = (UInt32)(subMeshIndexLength[i]);
            }
            else if (i < subMeshCount - 1)
            {
                ibstart = (UInt32)(_ibstart);
                iblength = (UInt32)(subMeshIndexLength[i]);
            }
            else
            {
                ibstart = (UInt32)(_ibstart);
                iblength = (UInt32)(subMeshIndexLength[i]);
            }

            Util.FileUtil.WriteData(fs, ibstart);
            Util.FileUtil.WriteData(fs, iblength);
            _ibstart += iblength;

            fs.Position += 2;

            int subIBStart = 0;
            for (int j = 0; j < boneIndexList[i].Count; j++)
            {
                Util.FileUtil.WriteData(fs, (UInt32)subIBStart + ibstart);//ibStart
                Util.FileUtil.WriteData(fs, (UInt32)(subIBIndex[i][j] - subIBStart));//ibLength
                subIBStart = subIBIndex[i][j];

                Util.FileUtil.WriteData(fs, (UInt32)boneDicStart);//boneDicStart
                Util.FileUtil.WriteData(fs, (UInt32)boneIndexList[i][j].Count * 2);//boneDicLength
                boneDicStart += boneIndexList[i][j].Count * 2;
            }
        }

        //����������
        fs.Position = VBMeshAreaPosition_Start;
        Util.FileUtil.WriteData(fs, (UInt32)(VBContentDatasAreaPosition_Start - StringDatasAreaPosition_Start));
        Util.FileUtil.WriteData(fs, (UInt32)vertexBuffer.Count);

        fs.Position = IBMeshAreaPosition_Start;
        Util.FileUtil.WriteData(fs, (UInt32)(IBContentDatasAreaPosition_Start - StringDatasAreaPosition_Start));
        Util.FileUtil.WriteData(fs, (UInt32)IBContentDatasAreaSize);

        fs.Position = BoneAreaPosition_Start + (bones.Count + 1) * 2;

        Util.FileUtil.WriteData(fs, (UInt32)(inverseGlobalBindPosesDatasAreaPosition_Start - StringDatasAreaPosition_Start));
        Util.FileUtil.WriteData(fs, (UInt32)(boneDicDatasAreaPosition_Start - inverseGlobalBindPosesDatasAreaPosition_Start));

        //�����ַ���
        fs.Position = StringAreaPosition_Start;
        Util.FileUtil.WriteData(fs, (UInt32)0);
        Util.FileUtil.WriteData(fs, (UInt16)stringDatas.Count);
        StringAreaPosition_End = fs.Position;

        //���ƶ�����
        fs.Position = BlockAreaPosition_Start + 2;
        Util.FileUtil.WriteData(fs, (UInt32)MeshAreaPosition_Start);
        Util.FileUtil.WriteData(fs, (UInt32)MeshAreaSize);
        for (int i = 0; i < subMeshCount; i++)
        {
            Util.FileUtil.WriteData(fs, (UInt32)subMeshAreaPosition_Start[i]);
            Util.FileUtil.WriteData(fs, (UInt32)subMeshAreaSize[i]);
        }

        //���Ʊ������������Ϣ��
        fs.Position = ContentAreaPosition_Start;
        Util.FileUtil.WriteData(fs, (UInt32)StringDatasAreaPosition_Start);
        Util.FileUtil.WriteData(fs, (UInt32)(StringDatasAreaPosition_Start + StringDatasAreaSize + VBContentDatasAreaSize + IBContentDatasAreaSize + subMeshAreaSize[0]));

        fs.Close();
    }

    private static VertexData checkPoint(VertexData vertexdata, int subMeshindex, int subsubMeshIndex, List<VertexData> ListVertexData)
    {
        //��һ��ѭ���������
        if (vertexdata.subMeshindex == -1 && vertexdata.subsubMeshindex == -1)
        {
            vertexdata.subMeshindex = subMeshindex;
            vertexdata.subsubMeshindex = subsubMeshIndex;
            return vertexdata;
        }//�������һ�εĵ���ͬ
        else if (vertexdata.subMeshindex == subMeshindex && vertexdata.subsubMeshindex == subsubMeshIndex)
        {
            return vertexdata;
        }
        //��һ���غϵ�
        else if (vertexdata.commonPoint == null)
        {
            //���غϵ��new dictionary
            vertexdata.commonPoint = new Dictionary<string, int>();
            VertexData newvertexdata = new VertexData();
            //�����¶���
            ListVertexData.Add(newvertexdata);
            //���ƶ�������
            newvertexdata.setValue(vertexdata);
            //���µ�Index
            newvertexdata.index = ListVertexData.Count - 1;
            vertexdata.commonPoint.Add(subMeshindex.ToString() + "," + subsubMeshIndex.ToString(), ListVertexData.Count - 1);
            return newvertexdata;
        }//�Ѿ����غϵ��
        else
        {
            //�����Ѿ���key�ĵ�
            if (vertexdata.commonPoint.ContainsKey(subMeshindex.ToString() + "," + subsubMeshIndex.ToString()))
            {
                return ListVertexData[vertexdata.commonPoint[subMeshindex.ToString() + "," + subsubMeshIndex.ToString()]];
            }//û��key,�ټ�һ��
            else
            {
                VertexData newvertexdata = new VertexData();
                //�����¶���
                ListVertexData.Add(newvertexdata);
                //���ƶ�������
                newvertexdata.setValue(vertexdata);
                //���µ�index
                newvertexdata.index = ListVertexData.Count - 1;
                vertexdata.commonPoint.Add(subMeshindex.ToString() + "," + subsubMeshIndex.ToString(), ListVertexData.Count - 1);
                return newvertexdata;
                //return vertexdata;
            }
        }
    }
    //��ȡһ�����������еĹ���
    private static List<int> triangleBoneIndex(Triangle triangle)
    {
        List<int> indexs = new List<int>();
        Vector4 v1 = triangle.point1.boneIndex;
        Vector4 v2 = triangle.point2.boneIndex;
        Vector4 v3 = triangle.point3.boneIndex;
        if (indexs.IndexOf((int)v1.x) == -1) indexs.Add((int)v1.x);
        if (indexs.IndexOf((int)v1.y) == -1) indexs.Add((int)v1.y);
        if (indexs.IndexOf((int)v1.z) == -1) indexs.Add((int)v1.z);
        if (indexs.IndexOf((int)v1.w) == -1) indexs.Add((int)v1.w);
        if (indexs.IndexOf((int)v2.x) == -1) indexs.Add((int)v2.x);
        if (indexs.IndexOf((int)v2.y) == -1) indexs.Add((int)v2.y);
        if (indexs.IndexOf((int)v2.z) == -1) indexs.Add((int)v2.z);
        if (indexs.IndexOf((int)v2.w) == -1) indexs.Add((int)v2.w);
        if (indexs.IndexOf((int)v3.x) == -1) indexs.Add((int)v3.x);
        if (indexs.IndexOf((int)v3.y) == -1) indexs.Add((int)v3.y);
        if (indexs.IndexOf((int)v3.z) == -1) indexs.Add((int)v3.z);
        if (indexs.IndexOf((int)v3.w) == -1) indexs.Add((int)v3.w);
        return indexs;
    }

    //����list������ϵ���������0��ȫ������������ز���0�Ǿ͵ö�
    private static List<int> listContainCount(List<int> boneindex, List<int> subsubboneindexs)
    {
        List<int> containcount = new List<int>();
        for (int i = 0; i < boneindex.Count; i++)
        {
            if (subsubboneindexs.IndexOf(boneindex[i]) == -1)
            {
                containcount.Add(boneindex[i]);
            }
        }
        return containcount;
    }
    private static void changeBoneIndex(List<int> boneindexlist, VertexData vertexdata)
    {
        if (vertexdata.ischange)
        {
            for (int i = 0; i < 4; i++)
            {
                vertexdata.boneIndex[i] = (float)boneindexlist.IndexOf((int)vertexdata.boneIndex[i]);
            }
            vertexdata.ischange = false;
        }
    }
    private static VertexData getVertexData(Vector3[] vertices, Vector3[] normals, Color[] colors, Vector2[] uv, Vector2[] uv2, BoneWeight[] boneWeightsX, Vector4[] tangents, int index)
    {
        VertexData vertexData = new VertexData();

        vertexData.index = index;


        vertexData.vertice = vertices[index];


        if (VertexStructure[1] == 1)
        {
            vertexData.normal = normals[index];
        }
        else
        {
            vertexData.normal = new Vector3();
        }

        if (VertexStructure[2] == 1)
        {
            vertexData.color = colors[index];
        }
        else
        {
            vertexData.color = new Color();
        }

        if (VertexStructure[3] == 1)
        {
            vertexData.uv = uv[index];
        }
        else
        {
            vertexData.uv = new Vector2();
        }

        if (VertexStructure[4] == 1)
        {
            vertexData.uv2 = uv2[index];
        }
        else
        {
            vertexData.uv2 = new Vector2();
        }

        if (VertexStructure[5] == 1)
        {
            BoneWeight boneWeights = boneWeightsX[index];

            vertexData.boneWeight.x = boneWeights.weight0;
            vertexData.boneWeight.y = boneWeights.weight1;
            vertexData.boneWeight.z = boneWeights.weight2;
            vertexData.boneWeight.w = boneWeights.weight3;

            vertexData.boneIndex.x = boneWeights.boneIndex0;
            vertexData.boneIndex.y = boneWeights.boneIndex1;
            vertexData.boneIndex.z = boneWeights.boneIndex2;
            vertexData.boneIndex.w = boneWeights.boneIndex3;
        }
        else
        {
            vertexData.boneWeight = new Vector4();
            vertexData.boneIndex = new Vector4();
        }

        if (VertexStructure[6] == 1)
        {
            vertexData.tangent = tangents[index];
        }
        else
        {
            vertexData.tangent = new Vector4();
        }

        return vertexData;
    }

    private const byte k_MaxByteForOverexposedColor = 191;
    public static void DecomposeHdrColor(Color linearColorHdr, out Color baseLinearColor, out float exposure)
    {
        baseLinearColor = linearColorHdr;
        var maxColorComponent = linearColorHdr.maxColorComponent;
        // replicate Photoshops's decomposition behaviour
        if (maxColorComponent == 0f || maxColorComponent <= 1f && maxColorComponent >= 1 / 255f)
        {
            exposure = 1f;

            baseLinearColor.r = linearColorHdr.r;
            baseLinearColor.g = linearColorHdr.g;
            baseLinearColor.b = linearColorHdr.b;
        }
        else
        {
            exposure = maxColorComponent;
            baseLinearColor.r = Mathf.GammaToLinearSpace(linearColorHdr.r / maxColorComponent);
            baseLinearColor.g = Mathf.GammaToLinearSpace(linearColorHdr.g / maxColorComponent);
            baseLinearColor.b = Mathf.GammaToLinearSpace(linearColorHdr.b / maxColorComponent);
        }
    }
    public static TextureFile writePicture(Texture texture, Dictionary<string, FileData> exportFiles, bool isNormal)
    {
        string picturePath = AssetDatabase.GetAssetPath(texture.GetInstanceID());
        TextureFile textureFile;
        if (!exportFiles.ContainsKey(picturePath))
        {
            textureFile = new TextureFile(picturePath, picturePath, texture as Texture2D, isNormal);
            exportFiles.Add(textureFile.filePath, textureFile);
        }
        else
        {
            textureFile = exportFiles[picturePath] as TextureFile;
        }

        return textureFile;
    }

    public static bool getMatarialRole(Material material, ConditionConfig rule)
    {
        if (rule.ruleType == "texture")
        {
            return material.GetTexture(rule.ruleKeyName) != null;
        }
        else if (rule.ruleType == "float")
        {
            return material.GetFloat(rule.ruleKeyName) == (float)rule.data;
        }
        else
        {
            return false;
        }
    }
    public static void writeMetarial(Material material, JsonFile file, Dictionary<string, FileData> exportFiles)
    {
        string shaderName = material.shader.name;
        if (!MaterialPropsConfigs.ContainsKey(shaderName))
        {
            Debug.LogError("LayaAir3D Warning : not get the shader config " + shaderName);
            return;
        }
        file.jsonData.AddField("version", "LAYAMATERIAL:04");
        JSONObject props = new JSONObject(JSONObject.Type.OBJECT);
        PropDatasConfig propsData = MaterialPropsConfigs[shaderName];
        file.jsonData.AddField("props", props);
        props.AddField("type", propsData.materalName);
        props.AddField("s_Cull", PropDatasConfig.GetCull(material));
        props.AddField("s_Blend", PropDatasConfig.GetBlend(material));
        props.AddField("s_BlendSrc", PropDatasConfig.GetSrcBlend(material));
        props.AddField("s_BlendDst", PropDatasConfig.GetDstBlend(material));
        props.AddField("alphaTest", PropDatasConfig.GetAlphaTest(material));
        props.AddField("alphaTestValue", PropDatasConfig.GetAlphaTestValue(material));
        props.AddField("renderQueue", material.renderQueue);
        JSONObject texture = new JSONObject(JSONObject.Type.ARRAY);
        foreach (var plist in propsData.pictureList)
        {
            if (material.GetTexture(plist.Key) != null)
            {
                TextureConfig tConfig = plist.Value;
                TextureFile textureFile = writePicture(material.GetTexture(plist.Key), exportFiles, tConfig.isNormal);
                texture.Add(textureFile.jsonObject(tConfig.keyName));
                file.AddRegistList(textureFile.filePath);
            }
        }
        props.AddField("textures", texture);
        props.AddField("materialRenderMode", PropDatasConfig.GetRenderModule(material));

        foreach (var cList in propsData.colorLists)
        {
            if (!material.HasProperty(cList.Key))
            {
                continue;
            }
            JSONObject colorValue = new JSONObject(JSONObject.Type.ARRAY);
            Color color = material.GetColor(cList.Key);
            if (propsData.colorHdrLists.ContainsKey(cList.Key))
            {
                Color colorf;
                float exp;
                DecomposeHdrColor(color, out colorf, out exp);
                colorValue.Add(colorf.r);
                colorValue.Add(colorf.g);
                colorValue.Add(colorf.b);
                colorValue.Add(colorf.a);
                props.AddField(propsData.colorHdrLists[cList.Key], exp);
            }
            else
            {
                colorValue.Add(color.r);
                colorValue.Add(color.g);
                colorValue.Add(color.b);
                colorValue.Add(color.a);
            }
            props.AddField(cList.Value, colorValue);
        }

        foreach (var tList in propsData.tillOffsetLists)
        {
            JSONObject tillOffData = new JSONObject(JSONObject.Type.ARRAY);
            Vector4 tiling = material.GetVector(tList.Key);
            tillOffData.Add(tiling.x);
            tillOffData.Add(tiling.y);
            tillOffData.Add(tiling.z);
            tillOffData.Add(tiling.w);
            props.AddField(tList.Value, tillOffData);
        }

        foreach (var flist in propsData.floatLists)
        {
            string uName = flist.Key;
            string layaName = flist.Value.keyName;
            if (flist.Value.rule != null)
            {
                ConditionConfig ruleConfig;
                if (propsData.rules.TryGetValue(flist.Value.rule, out ruleConfig))
                {
                    if (getMatarialRole(material, ruleConfig))
                    {
                        if (ruleConfig.target == "uName")
                        {
                            uName = ruleConfig.targetName;
                        }
                        else
                        {
                            layaName = ruleConfig.targetName;
                        }
                    }

                }
            }
            float data = material.GetFloat(uName);
            if (flist.Value.isGamma)
            {
                data = Mathf.LinearToGammaSpace(data);
            }
            props.AddField(layaName, data);
        }

        JSONObject definds = new JSONObject(JSONObject.Type.ARRAY);
        List<string> defindLists = new List<string>();
        foreach (var dlist in propsData.defindsLists)
        {
            if (dlist.Value.from == DefindsFrom.floatValue)
            {
                if (material.GetFloat(dlist.Key) == dlist.Value.data)
                {
                    definds.Add(dlist.Value.keyName);
                    defindLists.Add(dlist.Value.keyName);
                }
            }
            else if (dlist.Value.from == DefindsFrom.TextureValue)
            {
                if (material.GetTexture(dlist.Key))
                {
                    definds.Add(dlist.Value.keyName);
                    defindLists.Add(dlist.Value.keyName);
                }
            }
            else if (dlist.Value.from == DefindsFrom.keyWorld)
            {
                if (material.IsKeywordEnabled(dlist.Key))
                {
                    definds.Add(dlist.Value.keyName);
                    defindLists.Add(dlist.Value.keyName);
                }
            }
        }
        if (defindLists.Contains("NORMALTEXTURE") || defindLists.Contains("DETAILNORMAL") || defindLists.Contains("NORMALMAP"))
        {
            definds.Add("NEEDTBN");
        }
        props.AddField("defines", definds);
    }

    public static void writeSkyMetarial(Material material, JsonFile file, Dictionary<string, FileData> exportFiles)
    {
        string cubeMapPath = file.filePath.Split('.')[0] + ".cubemap";
        JsonFile cubeMapData = new JsonFile(cubeMapPath, new JSONObject(JSONObject.Type.OBJECT));
        exportFiles.Add(cubeMapData.filePath, cubeMapData);
        string shaderName = material.shader.name;
        if (!GameObjectUitls.MaterialPropsConfigs.ContainsKey(shaderName))
        {
            Debug.LogError("LayaAir3D Warning : not get the shader config " + shaderName);
            return;
        }
        PropDatasConfig propsData = GameObjectUitls.MaterialPropsConfigs[shaderName];
        foreach (var plist in propsData.pictureList)
        {
            if (material.GetTexture(plist.Key) != null)
            {
                TextureConfig tConfig = plist.Value;
                TextureFile textureFile = GameObjectUitls.writePicture(material.GetTexture(plist.Key), exportFiles, tConfig.isNormal);
                cubeMapData.jsonData.AddField(tConfig.keyName, "res://" + textureFile.filePath);
                cubeMapData.AddRegistList(textureFile.filePath);
            }
        }
        cubeMapData.jsonData.AddField("cubemapSize", 512);
        cubeMapData.jsonData.AddField("filterMode", 1);
        cubeMapData.jsonData.AddField("cubemapFileMode", "R8G8B8A8");
        cubeMapData.jsonData.AddField("mipmapCoverageIBL", true);
        cubeMapData.jsonData.AddField("generateMipmap", true);
        cubeMapData.jsonData.AddField("sRGB", true);

        JSONObject materialData = file.jsonData;
        materialData.AddField("version", "LAYAMATERIAL:04");

        JSONObject textures = new JSONObject(JSONObject.Type.ARRAY);
        JSONObject constructParams = new JSONObject(JSONObject.Type.ARRAY);
        constructParams.Add(512);
        constructParams.Add(512);
        constructParams.Add(0);
        constructParams.Add(false);
        constructParams.Add(false);
        constructParams.Add(true);

        JSONObject propertyParams = new JSONObject(JSONObject.Type.OBJECT);
        propertyParams.AddField("filterMode", 1);
        propertyParams.AddField("wrapModeU", 0);
        propertyParams.AddField("wrapModeV", 0);
        propertyParams.AddField("anisoLevel", 4);
        JSONObject texture = new JSONObject(JSONObject.Type.OBJECT);
        texture.AddField("path", "res://" + cubeMapData.uuid);
        texture.AddField("constructParams", constructParams);
        texture.AddField("propertyParams", propertyParams);
        texture.AddField("name", "u_CubeTexture");
        textures.Add(texture);

        JSONObject props = new JSONObject(JSONObject.Type.OBJECT);
        props.AddField("textures", textures);
        props.AddField("type", propsData.materalName);
        props.AddField("s_Cull", PropDatasConfig.GetCull(material));
        props.AddField("s_Blend", PropDatasConfig.GetBlend(material));
        props.AddField("s_BlendSrc", PropDatasConfig.GetSrcBlend(material));
        props.AddField("s_BlendDst", PropDatasConfig.GetDstBlend(material));
        props.AddField("alphaTest", PropDatasConfig.GetAlphaTest(material));
        props.AddField("alphaTestValue", PropDatasConfig.GetAlphaTestValue(material));
        props.AddField("renderQueue", material.renderQueue);
        props.AddField("materialRenderMode", 0);
        foreach (var cList in propsData.colorLists)
        {
            if (!material.HasProperty(cList.Key))
            {
                continue;
            }
            JSONObject colorValue = new JSONObject(JSONObject.Type.ARRAY);
            Color color = material.GetColor(cList.Key);
            if (propsData.colorHdrLists.ContainsKey(cList.Key))
            {
                Color colorf;
                float exp;
                GameObjectUitls.DecomposeHdrColor(color, out colorf, out exp);
                colorValue.Add(colorf.r);
                colorValue.Add(colorf.g);
                colorValue.Add(colorf.b);
                colorValue.Add(colorf.a);
                props.AddField(propsData.colorHdrLists[cList.Key], exp);
            }
            else
            {
                colorValue.Add(color.r);
                colorValue.Add(color.g);
                colorValue.Add(color.b);
                colorValue.Add(color.a);
            }
            props.AddField(cList.Value, colorValue);
        }
        foreach (var flist in propsData.floatLists)
        {
            props.AddField(flist.Value.keyName, material.GetFloat(flist.Key));

        }
        materialData.AddField("props", props);
    }


    private static AnimationCurveGroup readTransfromAnimation(EditorCurveBinding binding, GameObject gameObject, object targetObject, string path)
    {
        KeyFrameValueType keyType;
        string propNames = binding.propertyName.Split('.')[0];
        if (propNames == "m_LocalPosition")
        {
            propNames = "localPosition";
            keyType = KeyFrameValueType.Position;
        }
        else if (propNames == "m_LocalRotation")
        {
            propNames = "localRotation";
            keyType = KeyFrameValueType.Rotation;
        }
        else if (propNames == "m_LocalScale")
        {
            propNames = "localScale";
            keyType = KeyFrameValueType.Scale;
        }
        else if (propNames == "localEulerAnglesRaw")
        {
            propNames = "localRotationEuler";
            keyType = KeyFrameValueType.RotationEuler;
        }
        else
        {
            return null;
        }
        string conpomentType = searchCompoment[binding.type.ToString()];
        string propertyName = binding.propertyName;
        propertyName = propertyName.Substring(0, propertyName.LastIndexOf("."));
        AnimationCurveGroup curveGroup = new AnimationCurveGroup(path, gameObject, binding.type, conpomentType, propertyName, keyType);
        curveGroup.propnames.Add(propNames);
        return curveGroup;
    }
    private static AnimationCurveGroup readMaterAnimation(EditorCurveBinding binding, GameObject gameObject, object targetObject, string path)
    {
        PropertyInfo info = targetObject.GetType().GetProperty("material");
        Material material = (Material)info.GetValue(targetObject);
        string shaderName = material.shader.name;
        if (!MaterialPropsConfigs.ContainsKey(shaderName))
        {
            Debug.LogError("LayaAir3D Warning : not get the shader config " + shaderName);
            return null;
        }
        string propNames = binding.propertyName.Split('.')[1];
        PropDatasConfig propsData = MaterialPropsConfigs[shaderName];
        KeyFrameValueType keyType;
        if (propsData.floatLists.ContainsKey(propNames))
        {
            propNames = propsData.floatLists[propNames].keyName;
            keyType = KeyFrameValueType.Float;
        }
        else if (propsData.colorLists.ContainsKey(propNames))
        {
            propNames = propsData.colorLists[propNames];
            keyType = KeyFrameValueType.Color;
        }
        else if (propsData.tillOffsetLists.ContainsKey(propNames))
        {
            propNames = propsData.tillOffsetLists[propNames];
            keyType = KeyFrameValueType.Vector4;
        }
        else
        {
            //Debug.Log("��������" + binding.propertyName);
            return null;
        }
        string conpomentType = searchCompoment[binding.type.ToString()];
        string propertyName = binding.propertyName;
        propertyName = propertyName.Substring(0, propertyName.LastIndexOf("."));
        AnimationCurveGroup curveGroup = new AnimationCurveGroup(path, gameObject, binding.type, conpomentType, propertyName, keyType);
        curveGroup.propnames.Add("sharedMaterials");
        curveGroup.propnames.Add("0");
        curveGroup.propnames.Add(propNames);
        return curveGroup;
    }

    public static void writeClip(AnimationClip aniclip, FileStream fs, GameObject gameObject, string clipName)
    {


        List<string> stringDatas = new List<string>();
        stringDatas.Add("ANIMATIONS");
        stringDatas.Add(clipName);
        int clipFrameRate = (int)aniclip.frameRate;

        List<ComponentType> components = GameObjectUitls.componentsOnGameObject(gameObject);
        // list Curve ����
        List<EditorCurveBinding> editorCurveBindingList = new List<EditorCurveBinding>();



        // ԭʼ Curve ����
        EditorCurveBinding[] oriEditorCurveBindingList = AnimationUtility.GetCurveBindings(aniclip);


        editorCurveBindingList.AddRange(oriEditorCurveBindingList);

        // �������� ����
        EditorCurveBinding[] editorCurveBindings = editorCurveBindingList.ToArray();

        AnimationClipCurveData[] animationClipCurveDatas = new AnimationClipCurveData[editorCurveBindings.Length];
        Dictionary<string, AnimationCurveGroup> groupMap = new Dictionary<string, AnimationCurveGroup>();
        for (int j = 0; j < editorCurveBindings.Length; j++)
        {
            AnimationClipCurveData curveData = animationClipCurveDatas[j] = new AnimationClipCurveData(editorCurveBindings[j]);
            curveData.curve = AnimationUtility.GetEditorCurve(aniclip, editorCurveBindings[j]);

            string path = AnimationCurveGroup.getCurvePath(curveData);

            AnimationCurveGroup curveGroup = null;
            if (groupMap.ContainsKey(path))
            {
                curveGroup = groupMap[path];
            }
            else
            {
                GameObject child = gameObject;
                string[] strArr = curveData.path.Split('/');
                for (int m = 0; m < strArr.Length; m++)
                {
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
                object targetObject = AnimationUtility.GetAnimatedObject(gameObject, editorCurveBindings[j]);
                EditorCurveBinding binding = editorCurveBindings[j];
                if (binding.type == typeof(Transform))
                {
                    curveGroup = readTransfromAnimation(binding, child, targetObject, path);
                }
                else if (binding.type == typeof(RectTransform))
                {
                    curveGroup = readTransfromAnimation(binding, child, targetObject, path);
                }
                else if (typeof(Renderer).IsAssignableFrom(binding.type))
                {
                    curveGroup = readMaterAnimation(binding, child, targetObject, path);
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
        Dictionary<uint, float> timeList = new Dictionary<uint, float>();
        foreach (var group in groupMap)
        {
            group.Value.mergeTimeList(timeList);
        }

        List<float> startTimeList = new List<float>();
        foreach (var time in timeList)
        {
            startTimeList.Add(time.Value);
        }
        startTimeList.Sort();
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

        long MarkContentAreaPosition_Start = 0;

        long BlockAreaPosition_Start = 0;

        long StringAreaPosition_Start = 0;

        long ContentAreaPosition_Start = 0;

        long StringDatasAreaPosition_Start = 0;
        long StringDatasAreaPosition_End = 0;

        //�汾��
        //minner����

        string layaModelVerion = LaniVersion;

        Util.FileUtil.WriteData(fs, layaModelVerion);

        //���������Ϣ��
        MarkContentAreaPosition_Start = fs.Position; // Ԥ��������ƫ�Ƶ�ַ

        Util.FileUtil.WriteData(fs, (UInt32)0);//UInt32 offset
        Util.FileUtil.WriteData(fs, (UInt32)0);//UInt32 blockLength

        //���ݶ�����Ϣ��
        BlockAreaPosition_Start = fs.Position;//Ԥ����������
        int blockCount = 1;
        Util.FileUtil.WriteData(fs, (UInt16)blockCount);
        for (int j = 0; j < blockCount; j++)
        {
            Util.FileUtil.WriteData(fs, (UInt32)0);//UInt32 blockStart
            Util.FileUtil.WriteData(fs, (UInt32)0);//UInt32 blockLength
        }

        //�ַ���
        StringAreaPosition_Start = fs.Position;//Ԥ���ַ���
        Util.FileUtil.WriteData(fs, (UInt32)0);//UInt32 offset
        Util.FileUtil.WriteData(fs, (UInt16)0);//count

        //������
        ContentAreaPosition_Start = fs.Position;//Ԥ���ַ���
        Util.FileUtil.WriteData(fs, (UInt16)stringDatas.IndexOf("ANIMATIONS"));//uint16 ���亯���ַ�ID

        Util.FileUtil.WriteData(fs, (UInt16)startTimeList.Count);//startTime
        for (int j = 0; j < startTimeList.Count; j++)
        {
            Util.FileUtil.WriteData(fs, (float)startTimeList[j]);
        }

        Util.FileUtil.WriteData(fs, (UInt16)stringDatas.IndexOf(clipName));//�������ַ�����

        float aniTotalTime = startTimeList.Count == 0 ? 0.0f : (float)startTimeList[startTimeList.Count - 1];
        Util.FileUtil.WriteData(fs, aniTotalTime);//������ʱ��

        Util.FileUtil.WriteData(fs, aniclip.isLooping);//�����Ƿ�ѭ��

        Util.FileUtil.WriteData(fs, (UInt16)clipFrameRate);//frameRate

        Util.FileUtil.WriteData(fs, (UInt16)aniNodeDatas.Count);//�ڵ����
        for (int j = 0; j < aniNodeDatas.Count; j++)
        {
            aniNodeData = aniNodeDatas[j];
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
            Util.FileUtil.WriteData(fs, aniNodeData.keyFrameCount);//֡����

            for (int m = 0; m < aniNodeData.keyFrameCount; m++)
            {
                Util.FileUtil.WriteData(fs, aniNodeData.aniNodeFrameDatas[m].startTimeIndex);//startTimeIndex
                Util.FileUtil.WriteData(fs, aniNodeData.aniNodeFrameDatas[m].inTangentNumbers);
                Util.FileUtil.WriteData(fs, aniNodeData.aniNodeFrameDatas[m].outTangentNumbers);
                Util.FileUtil.WriteData(fs, aniNodeData.aniNodeFrameDatas[m].valueNumbers);
            }

        }

        //�¼�
        AnimationEvent[] aniEvents = aniclip.events;
        int aniEventCount = aniEvents.Length;
        Util.FileUtil.WriteData(fs, (Int16)aniEventCount);
        for (int k = 0; k < aniEventCount; k++)
        {
            AnimationEvent aniEvent = aniEvents[k];
            //time
            Util.FileUtil.WriteData(fs, (float)aniEvent.time);
            //������������
            string funName = aniEvent.functionName;
            if (stringDatas.IndexOf(funName) == -1)
            {
                stringDatas.Add(funName);
            }
            Util.FileUtil.WriteData(fs, (UInt16)stringDatas.IndexOf(funName));
            //��������
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

        //�ַ�������
        StringDatasAreaPosition_Start = fs.Position;
        for (int j = 0; j < stringDatas.Count; j++)
        {
            Util.FileUtil.WriteData(fs, stringDatas[j]);
        }
        StringDatasAreaPosition_End = fs.Position;

        //�����ַ���
        fs.Position = StringAreaPosition_Start + 4;
        Util.FileUtil.WriteData(fs, (UInt16)stringDatas.Count);//count

        //�������ݶ�����Ϣ��
        fs.Position = BlockAreaPosition_Start + 2 + 4;
        Util.FileUtil.WriteData(fs, (UInt32)(StringDatasAreaPosition_Start - ContentAreaPosition_Start));//UInt32 blockLength

        //����������Ϣ��
        fs.Position = MarkContentAreaPosition_Start;
        Util.FileUtil.WriteData(fs, (UInt32)StringDatasAreaPosition_Start);
        Util.FileUtil.WriteData(fs, (UInt32)(StringDatasAreaPosition_End - StringDatasAreaPosition_Start));

        fs.Close();
    }
}
