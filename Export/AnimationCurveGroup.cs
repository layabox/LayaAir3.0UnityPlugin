using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;


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


public class CustomAnimationCurve
{
    public Keyframe[] keys;
}

//����֡��Ϣ
public struct AniNodeFrameData
{
    public UInt16 startTimeIndex;
    public float[] inTangentNumbers;
    public float[] outTangentNumbers;
    public float[] valueNumbers;
}
//�����ڵ���Ϣ
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
    public float floatTime;
    public float[] inTangentNumbers;
    public float[] outTangentNumbers;
    public float[] outWeightNumbers;
    public float[] inWeightNumbers;
    public float[] valueNumbers;
    private List<string> _props;

    public FrameData(float time,  List<string> props)
    {
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
            Debug.LogError("δ�ҵ���Ӧ����");
        }
        this.inTangentNumbers[index] = value.inTangent;
        this.outTangentNumbers[index] = value.outTangent;
        this.valueNumbers[index] = value.value;
        this.inWeightNumbers[index] = value.inWeight;
        this.outWeightNumbers[index] = value.outWeight;
    }

  

}
public class AnimationCurveGroup
{
    private delegate void FrameDelegate(FrameData frame, ref AniNodeFrameData data,bool isRotate);
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
    private Dictionary<string,AnimationClipCurveData> _curveList;
    private string _path;
    private KeyFrameValueType _keyType;
    private List<string> _propnames;
    private Type _type;
    private string _propertyName;
    private string _conpomentType;
    private GameObject _gameobject;
    private Dictionary<float, FrameData> datas;
    List<float> everyStartTime;
    private List<ComponentType> components;
    public AnimationCurveGroup(string path, GameObject gameObject, Type type, string conpomentType,string propertyName, KeyFrameValueType keyType)
    {
        this._curveList = new Dictionary<string, AnimationClipCurveData>();
        this.components = GameObjectUitls.componentsOnGameObject(gameObject);
        this._path = path;
        this._gameobject = gameObject;
        this._keyType = keyType;
        this._conpomentType = conpomentType;
        this._propnames = new List<string>();
        this._type = type;
        this._propertyName = propertyName;
        this.everyStartTime = new List<float>();
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
        if(this._path != AnimationCurveGroup.getCurvePath(curveData))
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

        this._curveList.Add(endKey,curveData);

        Keyframe[] _everyKeyframe = curveData.curve.keys;
        for (int m = 0; m < _everyKeyframe.Length; m++)
        {
            this.addFloatTime(_everyKeyframe[m].time);
        }
        return true;
    }

    private void addFloatTime(float time)
    {
        bool isTimeExist = false;
        for (int k = 0; k < everyStartTime.Count; k++)
        {
            if (Math.Round(everyStartTime[k], 3) == Math.Round(time, 3))
            {
                isTimeExist = true;
            }
        }
        if (!isTimeExist)
        {
            everyStartTime.Add(time);
        }
    }
   
    public bool addEmptyClipCurve(float start, float end)
    {
        this.addFloatTime(start);
        this.addFloatTime(end);
        everyStartTime.Sort();
        int frameCount = everyStartTime.Count - 1;
        float startTime = everyStartTime[0];
        float endTime = everyStartTime[frameCount];
        List<string> props;
        if (!AnimationCurveGroup.keyFrameConfigs.TryGetValue(this._keyType, out props))
        {
            Debug.LogError("not get the Key Value Type" + this._keyType);
            return false;
        }
        //���������
        foreach (string key in props)
        {
            if (this._curveList.ContainsKey(key))
            {
                continue;
            }
            EditorCurveBinding binding = new EditorCurveBinding();
            binding.path = this._path.Split('.')[0];
            binding.propertyName = this._propertyName + "." + key;
            binding.type = this._type;
            float data = 0.0f;
            AnimationUtility.GetFloatValue(this._gameobject, binding, out data);
            AnimationClipCurveData curveData = new AnimationClipCurveData(binding);
            AnimationCurve curve = curveData.curve = new AnimationCurve();
            curve.AddKey(startTime, data);
            curve.AddKey(endTime, data);
            this._curveList.Add(key, curveData);
        }

        for (int k = 0; k <= frameCount; k++)
        {
            float floatTime = everyStartTime[k];
            FrameData frameData = new FrameData(floatTime, props);
            this.datas.Add(floatTime, frameData);
            foreach (string key in props)
            {
                frameData.setValue(key, getKeyByTime(this._curveList[key], floatTime));
            }
        }
        return true;
    }

    private Keyframe getKeyByTime(AnimationClipCurveData curveData,float time)
    {
        foreach(Keyframe keyFame in curveData.curve.keys)
        {
            if(getCurveTime(keyFame.time) == getCurveTime(time))
            {
                 return keyFame;
            }
        }

        // Get the current value of the curve at the insertion point
        float currentValue = curveData.curve.Evaluate(time);
        float derivative = (curveData.curve.Evaluate(time + 0.0001f) - currentValue) / 0.0001f;

        return new Keyframe(time, currentValue, derivative, derivative);
    }

    private void addValueByTime(string key, List<Keyframe> _stdKeyframeList)
    {
        foreach(var frame in _stdKeyframeList)
        {
            this.datas[frame.time].setValue(key, frame);
        }
    }

    public void getAnimaFameData(ref AniNodeData aniNodeData, ref List<float> startTimeList, ref List<string> stringDatas)
    {
        List<string> props;
        if (!AnimationCurveGroup.keyFrameConfigs.TryGetValue(this._keyType, out props))
        {
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
        UInt16 count = aniNodeData.propertyNameLength =(UInt16)this._propnames.Count;
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
        if(this._keyType == KeyFrameValueType.Position)
        {
            frameDelegate = writePosition;
        }else if(this._keyType == KeyFrameValueType.Rotation)
        {
            frameDelegate = writeRotate;
        }else if(this._keyType == KeyFrameValueType.RotationEuler)
        {
            frameDelegate = writeRotateEuler;
        }
        else
        {
            frameDelegate = writeValue;
        }
        bool isRotate = false;
        if (components.IndexOf(ComponentType.Camera) != -1 || components.IndexOf(ComponentType.DirectionalLight) != -1 || components.IndexOf(ComponentType.SpotLight) != -1)
        {
            isRotate = true;
        }
        int porpCount = props.Count;
        aniNodeData.keyFrameCount = (ushort)this.datas.Values.Count;
        foreach (FrameData frame in this.datas.Values)
        {
            AniNodeFrameData data = new AniNodeFrameData();
            data.valueNumbers = new float[porpCount];
            data.inTangentNumbers = new float[porpCount];
            data.outTangentNumbers = new float[porpCount];
            data.startTimeIndex = (UInt16)startTimeList.IndexOf(getCurveTime(frame.floatTime));
            frameDelegate(frame, ref data, isRotate);
            aniNodeFrameDatas.Add(data);
        }
    }

    private static void writePosition(FrameData frame, ref AniNodeFrameData data, bool isRotate)
    {
        SpaceChange.changePostion(ref frame.valueNumbers);
        SpaceChange.changePostion(ref frame.inTangentNumbers);
        SpaceChange.changePostion(ref frame.outTangentNumbers);
        writeValue(frame, ref data, isRotate);
    }

    private static void writeRotate(FrameData frame, ref AniNodeFrameData data, bool isRotate)
    {
        SpaceChange.changeRotate(ref frame.valueNumbers, isRotate);
        SpaceChange.changeRotateTangle(ref frame.inTangentNumbers);
        SpaceChange.changeRotateTangle(ref frame.outTangentNumbers);
        writeValue(frame, ref data, isRotate);
    }

    private static void writeRotateEuler(FrameData frame, ref AniNodeFrameData data, bool isRotate)
    {
        SpaceChange.changeRotateEuler(ref frame.valueNumbers, isRotate);
        SpaceChange.changeRotateEulerTangent(ref frame.inTangentNumbers, false);
        SpaceChange.changeRotateEulerTangent(ref frame.outTangentNumbers, false);
        writeValue(frame, ref data,isRotate);
    }

    private static void writeValue(FrameData frame, ref AniNodeFrameData data, bool isRotate)
    {
        int maxCount = frame.valueNumbers.Length;
        for (var i = 0; i < maxCount; i++)
        {
            data.valueNumbers[i] = frame.valueNumbers[i];
            data.inTangentNumbers[i] = 0;
            data.outTangentNumbers[i] = 0;
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
