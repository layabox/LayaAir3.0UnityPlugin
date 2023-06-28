using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

internal class JsonUtils 
{
    public static JSONObject GetColorObject(Color color)
    {
        JSONObject colorData = new JSONObject(JSONObject.Type.OBJECT);
        colorData.AddField("_$type", "Color");
        colorData.AddField("r", color.r);
        colorData.AddField("g", color.g);
        colorData.AddField("b", color.b);
        colorData.AddField("a", color.a);
        return colorData;
    }
    public static JSONObject GetVector3Object(Vector3 value)
    {
        JSONObject postionData = new JSONObject(JSONObject.Type.OBJECT);
        postionData.AddField("_$type", "Vector3");
        postionData.AddField("x", value.x);
        postionData.AddField("y", value.y);
        postionData.AddField("z", value.z);
        return postionData;
    }

    public static JSONObject GetQuaternionObject(Quaternion quaternion)
    {
        JSONObject postionData = new JSONObject(JSONObject.Type.OBJECT);
        postionData.AddField("_$type", "Quaternion");
        postionData.AddField("x", quaternion.x);
        postionData.AddField("y", quaternion.y);
        postionData.AddField("z", quaternion.z);
        postionData.AddField("w", quaternion.w);
        return postionData;
    }
    public static JSONObject GetTransfrom(GameObject gObject)
    {
        JSONObject transfrom = new JSONObject(JSONObject.Type.OBJECT);
        Vector3 position = gObject.transform.localPosition;
        SpaceUtils.changePostion(ref position);
        transfrom.AddField("localPosition", GetVector3Object(position));
      
        Quaternion rotation = gObject.transform.localRotation;
        bool isRotate = GameObjectUitls.isCameraOrLight(gObject);
        SpaceUtils.changeRotate(ref rotation, isRotate);
        transfrom.AddField("localRotation", GetQuaternionObject(rotation));
        transfrom.AddField("localScale", GetVector3Object(gObject.transform.localScale));
        return transfrom;
    }

    public static JSONObject GetGameObject(GameObject gObject,bool isperfab =false, JSONObject nodeData = null)
    {
        if(nodeData == null)
        {
            nodeData = new JSONObject(JSONObject.Type.OBJECT);
        }
        if (isperfab)
        {
            nodeData.AddField("_$ver", 1);
        }
        nodeData.AddField("name", gObject.name);
        nodeData.AddField("active", gObject.activeSelf);
        StaticEditorFlags staticEditorFlags = GameObjectUtility.GetStaticEditorFlags(gObject);
        nodeData.AddField("isStatic", ((int)staticEditorFlags & (int)StaticEditorFlags.BatchingStatic) > 0);
        nodeData.AddField("layer", gObject.layer);
        nodeData.AddField("transform", JsonUtils.GetTransfrom(gObject));
        return nodeData;
    }
    public static JSONObject GetDirectionalLightComponentData(Light light, bool isOverride)
    {
        JSONObject lightData = JsonUtils.SetComponentsType(new JSONObject(JSONObject.Type.OBJECT), "DirectionLightCom",isOverride);
        SetLightData(light, lightData);
        return lightData;
    }

    public static JSONObject GetPointLightComponentData(Light light, bool isOverride)
    {
        JSONObject lightData = JsonUtils.SetComponentsType(new JSONObject(JSONObject.Type.OBJECT), "PointLightCom", isOverride);
        SetLightData(light, lightData);
        lightData.AddField("range", light.range);

        return lightData;
    }

    public static JSONObject GetSpotLightComponentData(Light light, bool isOverride)
    {
        JSONObject lightData = JsonUtils.SetComponentsType(new JSONObject(JSONObject.Type.OBJECT), "SpotLightCom", isOverride);
        SetLightData(light, lightData);
        lightData.AddField("range", light.range);
        lightData.AddField("spotAngle", light.spotAngle);

        return lightData;
    }

    private static void SetLightData(Light light, JSONObject lightData)
    {
        lightData.AddField("intensity", light.intensity);
        switch (light.lightmapBakeType)
        {
            case LightmapBakeType.Realtime:
                lightData.AddField("lightmapBakedType", 0);
                break;
            case LightmapBakeType.Mixed:
                lightData.AddField("lightmapBakedType", 1);
                break;
            case LightmapBakeType.Baked:
                lightData.AddField("lightmapBakedType", 2);
                break;
            default:
                lightData.AddField("lightmapBakedType", 0);
                break;
        }
        lightData.AddField("color", GetColorObject(light.color));
        switch (light.shadows)
        {
            case LightShadows.Hard:
                lightData.AddField("shadowMode", 1);
                break;
            case LightShadows.Soft:
                lightData.AddField("shadowMode", 2);
                break;
            default:
                lightData.AddField("shadowMode", 0);
                break;
        }
        lightData.AddField("shadowStrength", light.shadowStrength);
        lightData.AddField("shadowDepthBias", light.shadowBias);
        lightData.AddField("shadowNormalBias", light.shadowNormalBias);
        lightData.AddField("shadowNearPlane", light.shadowNearPlane);
    }

    public static JSONObject SetComponentsType(JSONObject compData, string componentsname, bool isOverride)
    {
        if (isOverride)
        {
            compData.AddField("_$override", componentsname);
        }
        else
        {
            compData.AddField("_$type", componentsname);
        }
        return compData;
    }

    public static void getCameraComponentData(GameObject gameObject, JSONObject props)
    {
        Camera camera = gameObject.GetComponent<Camera>();
        if (camera.clearFlags == CameraClearFlags.Skybox)
        {
            props.AddField("clearFlag", 1);
        }
        else if (camera.clearFlags == CameraClearFlags.SolidColor || camera.clearFlags == CameraClearFlags.Color)
        {
            props.AddField("clearFlag", 0);
        }
        else if (camera.clearFlags == CameraClearFlags.Depth)
        {
            props.AddField("clearFlag", 2);
        }
        else
        {
            props.AddField("clearFlag", 3);
        }

        props.AddField("orthographic", camera.orthographic);
        props.AddField("orthographicVerticalSize", camera.orthographicSize * 2);
        props.AddField("fieldOfView", camera.fieldOfView);
        props.AddField("enableHDR", camera.allowHDR);
        props.AddField("nearPlane", camera.nearClipPlane);
        props.AddField("farPlane", camera.farClipPlane);

        JSONObject viewPort = new JSONObject(JSONObject.Type.OBJECT);
        viewPort.AddField("_$type", "Viewport");
        Rect rect = camera.rect;
        viewPort.AddField("x",rect.x);
        viewPort.AddField("y", 1.0f - rect.y - rect.height);
        viewPort.AddField("width", rect.width);
        viewPort.AddField("height", rect.height);
        props.AddField("normalizedViewport", viewPort);

        
        props.AddField("clearColor", GetColorObject(camera.backgroundColor));
    }
}
