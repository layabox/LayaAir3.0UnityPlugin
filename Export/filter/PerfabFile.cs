using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using System.IO;

internal class PerfabFile :FileData
{
    private static bool isPerfabAsset(GameObject gameObject)
    {
        var isprefab = PrefabUtility.GetPrefabType(gameObject);//物体的PrefabType
        if (isprefab != PrefabType.PrefabInstance)//不是Prefab实例
            return false;

        var prefab = PrefabUtility.GetCorrespondingObjectFromSource(gameObject);
        if (prefab == null)
            return false;
        return true;
    }
    /**
     *获得对象所在perfab资源的路径  
     */
    public static string getPerfabFilePath(GameObject gameObject)
    {
        if (!isPerfabAsset(gameObject))
        {
            return null;
        }
        return PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject);//物体的Prefab根节点
    }
    /**
    *获得对象所在perfab的最近根节点
    */
    public static GameObject getPerfabObject(GameObject gameObject)
    {
        if (!isPerfabAsset(gameObject))
        {
            return null;
        }

        return PrefabUtility.GetNearestPrefabInstanceRoot(gameObject);//物体的Prefab根节点
    }
    /**
    *获得对象所在perfab的所有根节点
    */
    public static GameObject getPrefabInstanceRoot(GameObject gameObject)
    {
        if (!isPerfabAsset(gameObject))
        {
            return null;
        }
        //return PrefabUtility.FindValidUploadPrefabInstanceRoot(gameObject);//物体的Prefab根节点
        return PrefabUtility.GetOutermostPrefabInstanceRoot(gameObject);//物体的Prefab根节点
    }

    private GameObject gameObject;
    private NodeMap _nodeMap;
    private string outUrl;
    public PerfabFile(NodeMap nodeMap,string perfabPath) : base(perfabPath)
    {
        this.outUrl = perfabPath.Replace(".prefab", ".lh");
        this.gameObject = PrefabUtility.LoadPrefabContents(perfabPath) as GameObject;
        this._nodeMap = nodeMap;
        this.getGameObjectData(this.gameObject,true);
        GameObject[] list = new GameObject[1];
        list[0] = this.gameObject;
        this._nodeMap.setRoots(list);
    }

    public NodeMap nodeMap
    {
        get
        {
            return this._nodeMap;
        }
    }
    override public string outPath
    {
        get
        {
            return ExportConfig.SavePath() + "/" + this.outUrl;
        }
    }

    private JSONObject getGameObjectData(GameObject gameObject, bool isperfab = false)
    {
        JSONObject nodeData = JsonUtils.GetGameObject(gameObject,isperfab);
        bool isperfabObject = false;
        if (gameObject != this.gameObject)
        {
            isperfabObject = PerfabFile.getPerfabObject(gameObject) == gameObject;
        }
        if (!isperfabObject)
        {
            if (gameObject.transform.childCount > 0)
            {
                for (int i = 0; i < gameObject.transform.childCount; i++)
                {
                    getGameObjectData(gameObject.transform.GetChild(i).gameObject);
                }
            }
        }
        this._nodeMap.addNodeMap(gameObject, nodeData, isperfabObject);
        return nodeData;
    }
    public override void SaveFile(Dictionary<string, FileData> exportFiles)
    {
        base.saveMeta();
        JSONObject data = this._nodeMap.getJsonObject(this.gameObject);
        FileStream fs = new FileStream(this.outPath, FileMode.Create, FileAccess.Write);
        StreamWriter writer = new StreamWriter(fs);
        writer.Write(data.Print(true));
        writer.Close();
        GameObject.DestroyImmediate(this.gameObject);
    }
}
