using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

internal class JsonUtils 
{
    private static Dictionary<string, string> registclassMap;
    public static void init()
    {
        JsonUtils.registclassMap = new Dictionary<string, string>();
        JsonUtils.registclassMap.Add("ParticleSystem", "4777a3a0-2c73-460c-b47a-7c5a04c18e05");
        JsonUtils.registclassMap.Add("ParticleSystemRenderer", "75e22e26-8d5d-4d92-b088-231b22ce3c41");
        JsonUtils.registclassMap.Add("MinMaxCurve", "b87b8fac-9e9e-4208-b771-9af1d197faff");
        JsonUtils.registclassMap.Add("AnimationCurve", "98c3ef8f-a969-4a69-a9a2-aaa279c0af76");
        JsonUtils.registclassMap.Add("CurveKeyframe", "e841b213-e04b-4d71-91d8-1f3da4c4d90b");
        JsonUtils.registclassMap.Add("MinMaxGradient", "ea294660-23b9-4d09-957b-e522a1044d69");
        JsonUtils.registclassMap.Add("PlusBurst", "e6508117-f0a4-457e-b139-8cdd03c2474d");
        JsonUtils.registclassMap.Add("PlusSubmitterData", "6c5aa0f5-3e3c-42f8-8a31-76441f2a3b20");
        

        JsonUtils.registclassMap.Add("MainModule", "4e51be3f-6ce0-467d-badf-2c601f3c1940");
        JsonUtils.registclassMap.Add("PlusEmission", "4f5e56f1-f217-45be-a510-e6b7a9f9501e");
        JsonUtils.registclassMap.Add("PlusVelocityOverLife", "e9fc6cd1-f96f-4f3b-8154-2ba770d12ea1");
        JsonUtils.registclassMap.Add("PlusSizeOverLife", "c15dd946-91c9-4366-ad7c-f8a2904156c6");
        JsonUtils.registclassMap.Add("PlusForceOverLife", "9c59631d-d033-4e29-a2b6-ebbe295e5ccb");
        JsonUtils.registclassMap.Add("PlusRotationOverLife", "ecc0ebe5-b06d-4dce-87d3-ee5bcec36e93");
        JsonUtils.registclassMap.Add("PlusLimtVelocityOverLife", "83a7beac-dc36-4463-98b9-077ba14a794a");
        JsonUtils.registclassMap.Add("PlusColorOverLife", "363aacd5-3fae-4fc0-bb6f-cb2b9b2c04d8");
        JsonUtils.registclassMap.Add("PlusColorBySpeed", "34fbc2c5-a2ba-40e7-b30a-e1432f217292");
        JsonUtils.registclassMap.Add("PlusSizeBySpeed", "33542cd3-ec1c-4fac-b1f6-9aa26be540e1");
        JsonUtils.registclassMap.Add("PlusRotationBySpeed", "9f229941-9847-4658-9355-e71f323d330e");
        JsonUtils.registclassMap.Add("PlusInheritVelocity", "f76eb54c-1957-4844-ae8d-0a0e66953036");
        JsonUtils.registclassMap.Add("PlusNoise", "f62e3136-b763-4350-88da-0baf08004aba");
        JsonUtils.registclassMap.Add("PlusTextureSheetAnimation", "c265efa5-23bc-4de9-a5cf-91e65b1dc31f");
        JsonUtils.registclassMap.Add("PlusSubEmitters", "4898b4d7-6f6b-45dc-82f6-1fb83d1a62da");


        JsonUtils.registclassMap.Add("PlusBoxShape", "cfdbb0bc-27ab-4d63-8b94-cdcc24a97d2f");
        JsonUtils.registclassMap.Add("PlusSphereShape", "6c5f9470-d0c6-4dc4-8df6-c126bec7424d");
        JsonUtils.registclassMap.Add("PlusHemisphereShape", "2d79ad98-bdc6-44e3-823b-8d1a87670741");
        JsonUtils.registclassMap.Add("PlusConeShape", "01678383-2307-47a0-8c9d-cfaad5ad20ab");
        JsonUtils.registclassMap.Add("PlusCircleShape", "447881cb-4443-43e9-99c3-027b5488a6f8");
        JsonUtils.registclassMap.Add("PlusDountShape", "6f45c7c2-5327-45d2-be33-c3f1faeb9f8f");
        JsonUtils.registclassMap.Add("PlusSideEdgeShape", "3c6ce9b5-f8cf-4e60-8cb9-dbb1e8acac1f");
        JsonUtils.registclassMap.Add("PlusMeshShape", "af66f3c5-08f8-4999-bb2f-2f0044e76150");
        JsonUtils.registclassMap.Add("PlusMeshRenderShape", "ba49942d-7aff-46dd-9ddb-e33e8da4ea01");
        JsonUtils.registclassMap.Add("PlusRectangleShape", "f8006022-8285-4c9e-b193-00f22e704396");
    }
    public static JSONObject GetColorObject(Color color)
    {
        JSONObject colorData = new JSONObject(JSONObject.Type.OBJECT);
        SetComponentsType(colorData, "Color");
        colorData.AddField("r", color.r);
        colorData.AddField("g", color.g);
        colorData.AddField("b", color.b);
        colorData.AddField("a", color.a);
        return colorData;
    }
    public static JSONObject GetVector3Object(Vector3 value)
    {
        JSONObject postionData = new JSONObject(JSONObject.Type.OBJECT);
        SetComponentsType(postionData, "Vector3");
        postionData.AddField("x", value.x);
        postionData.AddField("y", value.y);
        postionData.AddField("z", value.z);
        return postionData;
    }

    public static JSONObject GetVector2Object(Vector2 value)
    {
        JSONObject postionData = new JSONObject(JSONObject.Type.OBJECT);
        SetComponentsType(postionData, "Vector2");
        postionData.AddField("x", value.x);
        postionData.AddField("y", value.y);
        return postionData;
    }

    public static JSONObject GetVector2Object(float x,float y)
    {
        JSONObject postionData = new JSONObject(JSONObject.Type.OBJECT);
        SetComponentsType(postionData, "Vector2");
        postionData.AddField("x", x);
        postionData.AddField("y", y);
        return postionData;
    }

    public static JSONObject GetQuaternionObject(Quaternion quaternion)
    {
        JSONObject postionData = new JSONObject(JSONObject.Type.OBJECT);
        SetComponentsType(postionData, "Quaternion");
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
                lightData.AddField("lightmapBakedType", 1);
                break;
            case LightmapBakeType.Mixed:
                lightData.AddField("lightmapBakedType", 0);
                break;
            case LightmapBakeType.Baked:
                lightData.AddField("lightmapBakedType", 2);
                break;
            default:
                lightData.AddField("lightmapBakedType", 1);
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

    public static JSONObject SetComponentsType(JSONObject compData, string componentsname, bool isOverride=false)
    {
        if (JsonUtils.registclassMap.ContainsKey(componentsname))
        {
            componentsname = JsonUtils.registclassMap[componentsname];
        }
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
        SetComponentsType(viewPort, "Viewport");
        Rect rect = camera.rect;
        viewPort.AddField("x",rect.x);
        viewPort.AddField("y", 1.0f - rect.y - rect.height);
        viewPort.AddField("width", rect.width);
        viewPort.AddField("height", rect.height);
        props.AddField("normalizedViewport", viewPort);

        
        props.AddField("clearColor", GetColorObject(camera.backgroundColor));
    }
}
