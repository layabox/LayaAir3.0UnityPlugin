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
}



class GameObjectUitls
{
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
    }
    private static string LaniVersion = "LAYAANIMATION:04";
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
        PropDatasConfig propsData = MetarialUitls.getMetarialConfig(shaderName);
        if (propsData == null)
        {
            return null;
        }
        string propNames = binding.propertyName.Split('.')[1];
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

        float aniTotalTime = startTimeList.Count == 0 ? 0.0f : (float)startTimeList[startTimeList.Count - 1];
        Util.FileUtil.WriteData(fs, aniTotalTime);///动画总时长

        Util.FileUtil.WriteData(fs, aniclip.isLooping);//动画是否循环

        Util.FileUtil.WriteData(fs, (UInt16)clipFrameRate);//frameRate

        Util.FileUtil.WriteData(fs, (UInt16)aniNodeDatas.Count);//节点个数
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
            Util.FileUtil.WriteData(fs, aniNodeData.keyFrameCount);//帧个数

            for (int m = 0; m < aniNodeData.keyFrameCount; m++)
            {
                Util.FileUtil.WriteData(fs, aniNodeData.aniNodeFrameDatas[m].startTimeIndex);//startTimeIndex
                Util.FileUtil.WriteData(fs, aniNodeData.aniNodeFrameDatas[m].inTangentNumbers);
                Util.FileUtil.WriteData(fs, aniNodeData.aniNodeFrameDatas[m].outTangentNumbers);
                Util.FileUtil.WriteData(fs, aniNodeData.aniNodeFrameDatas[m].valueNumbers);
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
}
