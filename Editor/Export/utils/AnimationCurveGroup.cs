using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using System.Linq;
using FileUtil = Util.FileUtil;


public enum KeyFrameValueType
{
    Float = 0,
    Position = 1,
    Rotation = 2,
    Scale = 3,
    RotationEuler = 4,
    Vector2 = 5,
    Vector3 = 6,
    Vector4 = 7,
    Color = 8
}



public class FrameInfo
{
    public float time;
    public int oriderIndex;
    public uint frameIndex;
}

//动画帧信息
public struct AniNodeFrameData
{
    public UInt16 startTimeIndex;
    public float[] inTangentNumbers;
    public float[] outTangentNumbers;
    public float[] valueNumbers;
}

//动画节点信息
public struct AniNodeData
{
    public Byte type;
    public UInt16 pathLength;
    public List<UInt16> pathIndex;
    public UInt16 conpomentTypeIndex;
    public UInt16 propertyNameLength;
    public List<UInt16> propertyNameIndex;
    public UInt16 keyFrameCount;
    public List<AniNodeFrameData> aniNodeFrameDatas;
}

public class PropertyConfig
{
    public string layaProperty;
    public KeyFrameValueType keyType;
    public PropertyConfig(string layaProperty, KeyFrameValueType keyType)
    {
        this.layaProperty = layaProperty;
        this.keyType = keyType;
    }
}

public class FrameData
{
    public uint frameIndex;
    public float floatTime;
    public float[] inTangentNumbers;
    public float[] outTangentNumbers;
    public float[] outWeightNumbers;
    public float[] inWeightNumbers;
    public float[] valueNumbers;
    private List<string> _props;

    public FrameData(uint frameIndex, float time, List<string> props)
    {
        this.frameIndex = frameIndex;
        this.floatTime = time;
        this._props = props;
        int propCount = props.Count;
        this.inTangentNumbers = new float[propCount];
        this.outTangentNumbers = new float[propCount];
        this.outWeightNumbers = new float[propCount];
        this.inWeightNumbers = new float[propCount];
        this.valueNumbers = new float[propCount];
    }

    public void setValue(string key, Keyframe value)
    {
        int index = this._props.IndexOf(key);
        if (index < 0)
        {
            FileUtil.setStatuse(false);
            Debug.LogError("not get the prop : " + key);
        }
        this.inTangentNumbers[index] = value.inTangent;
        this.outTangentNumbers[index] = value.outTangent;
        this.valueNumbers[index] = value.value;
        this.inWeightNumbers[index] = value.inWeight;
        this.outWeightNumbers[index] = value.outWeight;
    }
}

//所有动作帧节点数据
class CustomClipCurveData
{
    private Dictionary<uint, Keyframe> m_keyMap;
    private AnimationClipCurveData _curveData;

    public AnimationClipCurveData curveData
    {
        get
        {
            return this._curveData;
        }
    }
    public CustomClipCurveData(AnimationClipCurveData curveData)
    {
        this._curveData = curveData;
    }
    public void createKeyMap()
    {
        this.m_keyMap = new Dictionary<uint, Keyframe>();
        foreach (Keyframe keyFame in curveData.curve.keys)
        {
            uint frameIndex = AnimationCurveGroup.getFrameByTime(keyFame.time);
            if (!this.m_keyMap.ContainsKey(frameIndex))
            {
                this.m_keyMap.Add(frameIndex, keyFame);
            }
            else
            {
                Debug.Log("zhen" + frameIndex.ToString());
            }
        }
    }

    public Keyframe GetKeyframeByTime(float time,bool istart,bool isEnd)
    {
        uint frameIndex = AnimationCurveGroup.getFrameByTime(time);
        Keyframe keyframe;
        if (!this.m_keyMap.TryGetValue(frameIndex, out keyframe))
        {
            Debug.LogError("插入帧错误；请查找bug");
        }
        return keyframe;
    }

    public void addEmptyFrme(float time, bool istart, bool isEnd)
    {
        uint frameIndex = AnimationCurveGroup.getFrameByTime(time);
       
        if (!this.m_keyMap.ContainsKey(frameIndex))
        {
            Keyframe keyframe;
            float currentValue = curveData.curve.Evaluate(time);
            float derivative = (curveData.curve.Evaluate(time + 0.0001f) - currentValue) / 0.0001f;
            if (istart)
            {
                this.updataNextFrmae(frameIndex, derivative);
                keyframe = new Keyframe(time, currentValue, 0, derivative);
            }
            else if (isEnd)
            {
                this.updataLastFrmae(frameIndex, derivative);
                keyframe = new Keyframe(time, currentValue, derivative, 0);
            }
            else
            {
                this.updataNextFrmae(frameIndex, derivative);
                this.updataLastFrmae(frameIndex, derivative);
                keyframe = new Keyframe(time, currentValue, derivative, derivative);
            }
            this.m_keyMap.Add(frameIndex, keyframe);

        }
    }

    private void updataNextFrmae(uint frameIndex,float tangent)
    {
        uint foundIndex = uint.MaxValue;
        foreach (var data in this.m_keyMap)
        {
            if(data.Key>frameIndex && data.Key < foundIndex)
            {
                foundIndex = data.Key;
            }
        }
        Keyframe keyframe;
        if(this.m_keyMap.TryGetValue(foundIndex,out keyframe))
        {
            keyframe.inTangent = tangent;
            this.m_keyMap.Remove(foundIndex);
            this.m_keyMap.Add(foundIndex, keyframe);

        }
    }

    private void updataLastFrmae(uint frameIndex, float tangent)
    {
        uint foundIndex = uint.MinValue;
        foreach (var data in this.m_keyMap)
        {
            if (data.Key < frameIndex && data.Key > foundIndex)
            {
                foundIndex = data.Key;
            }
        }
        Keyframe keyframe;
        if (this.m_keyMap.TryGetValue(foundIndex, out keyframe))
        {
            keyframe.outTangent = tangent;
            this.m_keyMap.Remove(foundIndex);
            this.m_keyMap.Add(foundIndex, keyframe);

        }
    }
}
public class AnimationCurveGroup
{
    public static uint FPS = 1000;
    private delegate void FrameDelegate(FrameData frame, ref AniNodeFrameData data, bool isRotate);
    private static Dictionary<KeyFrameValueType, List<string>> keyFrameConfigs;
    public static void init()
    {
        keyFrameConfigs = new Dictionary<KeyFrameValueType, List<string>>();
        List<string> xzy = new List<string>();
        xzy.Add("x");
        xzy.Add("y");
        xzy.Add("z");

        keyFrameConfigs.Add(KeyFrameValueType.Position, xzy);
        keyFrameConfigs.Add(KeyFrameValueType.Scale, xzy);
        keyFrameConfigs.Add(KeyFrameValueType.RotationEuler, xzy);
        keyFrameConfigs.Add(KeyFrameValueType.Vector3, xzy);

        List<string> xyzw = new List<string>();
        xyzw.Add("x");
        xyzw.Add("y");
        xyzw.Add("z");
        xyzw.Add("w");
        keyFrameConfigs.Add(KeyFrameValueType.Rotation, xyzw);
        keyFrameConfigs.Add(KeyFrameValueType.Vector4, xyzw);
        List<string> xy = new List<string>();
        xy.Add("x");
        xy.Add("y");
        keyFrameConfigs.Add(KeyFrameValueType.Vector2, xy);
        List<string> color = new List<string>();
        color.Add("r");
        color.Add("g");
        color.Add("b");
        color.Add("a");
        keyFrameConfigs.Add(KeyFrameValueType.Color, color);

        List<string> x = new List<string>();
        x.Add("x");
        keyFrameConfigs.Add(KeyFrameValueType.Float, x);
    }
    private Dictionary<string, CustomClipCurveData> _curveList;
    private string _path;
    private KeyFrameValueType _keyType;
    private List<string> _propnames;
    private Type _type;
    private string _propertyName;
    private string _conpomentType;
    private GameObject _gameobject;
    private Dictionary<float, FrameData> datas;
    private Dictionary<uint, float> _timeLists;
    private bool iscameraOrLight;
    public AnimationCurveGroup(string path, GameObject gameObject, Type type, string conpomentType, string propertyName, KeyFrameValueType keyType)
    {
        this._curveList = new Dictionary<string, CustomClipCurveData>();
        this.iscameraOrLight = GameObjectUitls.isCameraOrLight(gameObject);
        this._path = path;
        this._gameobject = gameObject;
        this._keyType = keyType;
        this._conpomentType = conpomentType;
        this._propnames = new List<string>();
        this._type = type;
        this._propertyName = propertyName;
        this._timeLists = new Dictionary<uint, float>();
        this.datas = new Dictionary<float, FrameData>();
    }
    public string path
    {
        get
        {
            return this._path;
        }
    }

    public List<string> propnames
    {
        get
        {
            return this._propnames;
        }
    }

    public KeyFrameValueType keyType
    {
        get
        {
            return this._keyType;
        }
    }

    public bool pushCurve(AnimationClipCurveData curveData)
    {
        if (this._path != AnimationCurveGroup.getCurvePath(curveData))
        {
            return false;
        }
        string endKey = null;
        if (this._keyType == KeyFrameValueType.Float)
        {
            endKey = "x";
        }
        else
        {
            string[] propertyNames = curveData.propertyName.Split('.');
            endKey = propertyNames[propertyNames.Length - 1];
        }

        if (this._curveList.ContainsKey(endKey))
        {
            return false;
        }

        this._curveList.Add(endKey, new CustomClipCurveData(curveData));

        Keyframe[] _everyKeyframe = curveData.curve.keys;
        for (int m = 0; m < _everyKeyframe.Length; m++)
        {
            this.addFloatTime(_everyKeyframe[m].time);
        }
        return true;
    }

    private void addFloatTime(float time)
    {
        uint frameIndex = getFrameByTime(time);
        if (!this._timeLists.ContainsKey(frameIndex))
        {
            this._timeLists.Add(frameIndex, time);
        }
    }

    public void mergeTimeList(Dictionary<uint, float> timeLists)
    {
        foreach (var value in this._timeLists)
        {
            if (!timeLists.ContainsKey(value.Key))
            {
                timeLists.Add(value.Key, value.Value);
            }
        }
    }

    public static uint getFrameByTime(float time)
    {
        return (uint)Mathf.Floor(time * AnimationCurveGroup.FPS);
    }
    public bool addEmptyClipCurve(float start, float end)
    {
        this.addFloatTime(start);
        this.addFloatTime(end);
        var sort = from pair in this._timeLists orderby pair.Key ascending select pair;
        List<string> props;
        if (!AnimationCurveGroup.keyFrameConfigs.TryGetValue(this._keyType, out props))
        {
            FileUtil.setStatuse(false);
            Debug.LogError("Not get the Key Value Type" + this._keyType);
            return false;
        }
        foreach (string key in props)
        {
            CustomClipCurveData customCurveData;
            if (!this._curveList.TryGetValue(key, out customCurveData))
            {
                EditorCurveBinding binding = new EditorCurveBinding();
                binding.path = this._path.Split('.')[0];
                binding.propertyName = this._propertyName + "." + key;
                binding.type = this._type;
                float data = 0.0f;
                AnimationUtility.GetFloatValue(this._gameobject, binding, out data);
                AnimationClipCurveData curveData = new AnimationClipCurveData(binding);
                AnimationCurve curve = curveData.curve = new AnimationCurve();
                curve.AddKey(start, data);
                curve.AddKey(end, data);
                customCurveData = new CustomClipCurveData(curveData);
                this._curveList.Add(key, customCurveData);
            }
            customCurveData.createKeyMap();
        }

        foreach (var timeValue in sort)
        {
            float floatTime = timeValue.Value;;
            bool istart = floatTime == start;
            bool isend = floatTime == end;
            foreach (string key in props)
            {
                this._curveList[key].addEmptyFrme(floatTime, istart, isend);
            }
        }
        foreach (var timeValue in sort)
        {
            float floatTime = timeValue.Value;
            FrameData frameData = new FrameData(timeValue.Key, floatTime, props);
            this.datas.Add(floatTime, frameData);
            bool istart = floatTime == start;
            bool isend = floatTime == end;
            foreach (string key in props)
            {
                frameData.setValue(key, this._curveList[key].GetKeyframeByTime(floatTime,istart,isend));
            }
        }
        return true;
    }


    public void getAnimaFameData(ref AniNodeData aniNodeData, ref Dictionary<uint, FrameInfo> frameInfoList, ref List<string> stringDatas)
    {
        List<string> props;
        if (!AnimationCurveGroup.keyFrameConfigs.TryGetValue(this._keyType, out props))
        {
            FileUtil.setStatuse(false);
            Debug.LogError("not get the Key Value Type" + this._keyType);
            return;
        }
        aniNodeData.type = (Byte)this._keyType;
        List<UInt16> pathIndex = new List<UInt16>();
        String nodePath = this._path.Split('.')[0];
        string[] strArr = nodePath.Split('/');
        for (int m = 0; m < strArr.Length; m++)
        {
            if (stringDatas.IndexOf(strArr[m]) == -1)
            {
                stringDatas.Add(strArr[m]);
            }
            pathIndex.Add((UInt16)stringDatas.IndexOf(strArr[m]));
        }
        aniNodeData.pathLength = (UInt16)pathIndex.Count;
        aniNodeData.pathIndex = pathIndex;
        if (stringDatas.IndexOf(this._conpomentType) == -1)
        {
            stringDatas.Add(this._conpomentType);
        }
        aniNodeData.conpomentTypeIndex = (UInt16)stringDatas.IndexOf(this._conpomentType);
        UInt16 count = aniNodeData.propertyNameLength = (UInt16)this._propnames.Count;
        List<UInt16> propertyNameIndex = aniNodeData.propertyNameIndex = new List<UInt16>();
        for (var i = 0; i < count; i++)
        {
            if (stringDatas.IndexOf(this._propnames[i]) == -1)
            {
                stringDatas.Add(this._propnames[i]);
            }
            propertyNameIndex.Add((UInt16)stringDatas.IndexOf(this._propnames[i]));
        }
        List<AniNodeFrameData> aniNodeFrameDatas = aniNodeData.aniNodeFrameDatas = new List<AniNodeFrameData>();
        FrameDelegate frameDelegate = null;
        if (this._keyType == KeyFrameValueType.Position)
        {
            frameDelegate = writePosition;
        }
        else if (this._keyType == KeyFrameValueType.Rotation)
        {
            frameDelegate = writeRotate;
        }
        else if (this._keyType == KeyFrameValueType.RotationEuler)
        {
            frameDelegate = writeRotateEuler;
        }
        else
        {
            frameDelegate = writeValue;
        }
        int porpCount = props.Count;
        aniNodeData.keyFrameCount = (ushort)this.datas.Values.Count;
        foreach (FrameData frame in this.datas.Values)
        {
            AniNodeFrameData data = new AniNodeFrameData();
            data.valueNumbers = new float[porpCount];
            data.inTangentNumbers = new float[porpCount];
            data.outTangentNumbers = new float[porpCount];
            uint frameIndex = getFrameByTime(frame.floatTime);
            FrameInfo info;
            if (frameInfoList.TryGetValue(frameIndex, out info))
            {
                data.startTimeIndex = (ushort)info.oriderIndex;
            }
            else
            {
                FileUtil.setStatuse(false);     
                Debug.LogError("not get the frameIndex by time:" + frame.floatTime.ToString());
            }

            frameDelegate(frame, ref data, this.iscameraOrLight);
            aniNodeFrameDatas.Add(data);
        }
    }

    private static void writePosition(FrameData frame, ref AniNodeFrameData data, bool isRotate)
    {
        SpaceUtils.changePostion(ref frame.valueNumbers);
        SpaceUtils.changePostion(ref frame.inTangentNumbers);
        SpaceUtils.changePostion(ref frame.outTangentNumbers);
        writeValue(frame, ref data, isRotate);
    }

    private static void writeRotate(FrameData frame, ref AniNodeFrameData data, bool isRotate)
    {
        SpaceUtils.changeRotate(ref frame.valueNumbers, isRotate);
        SpaceUtils.changeRotateTangle(ref frame.inTangentNumbers);
        SpaceUtils.changeRotateTangle(ref frame.outTangentNumbers);
        writeValue(frame, ref data, isRotate);
    }

    private static void writeRotateEuler(FrameData frame, ref AniNodeFrameData data, bool isRotate)
    {
        SpaceUtils.changeRotateEuler(ref frame.valueNumbers, isRotate);
        SpaceUtils.changeRotateEulerTangent(ref frame.inTangentNumbers, false);
        SpaceUtils.changeRotateEulerTangent(ref frame.outTangentNumbers, false);
        writeValue(frame, ref data, isRotate);
    }

    private static void writeValue(FrameData frame, ref AniNodeFrameData data, bool isRotate)
    {
        int maxCount = frame.valueNumbers.Length;
        for (var i = 0; i < maxCount; i++)
        {
            data.valueNumbers[i] = frame.valueNumbers[i];
            data.inTangentNumbers[i] = frame.inTangentNumbers[i];
            data.outTangentNumbers[i] = frame.outTangentNumbers[i];
        }
    }


    public static string getCurvePath(AnimationClipCurveData curveData)
    {
        string propertyName = curveData.propertyName;
        string _propertyName = propertyName.Substring(0, propertyName.LastIndexOf('.'));
        return curveData.path + "." + _propertyName;
    }
    public static float getCurveTime(float time)
    {
        return (float)Math.Round(time, 3);
    }
}
