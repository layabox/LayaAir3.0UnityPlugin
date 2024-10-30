using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using System.IO;

internal class PerfabFile :FileData
{
    private static bool isPerfabAsset(GameObject gameObject)
    {
        PrefabAssetType type = PrefabUtility.GetPrefabAssetType(gameObject);//物体的PrefabType
        if (type == PrefabAssetType.NotAPrefab)//不是Prefab实例
            return false;
        string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject);//物体的Prefab根节点

        return Path.GetExtension(path).ToLower() == ".prefab";
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
        string path =  PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject);//物体的Prefab根节点
        return path;
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
        return PrefabUtility.GetOutermostPrefabInstanceRoot(gameObject);//物体的Prefab根节点
    }

    private GameObject gameObject;
    private NodeMap _nodeMap;
    public PerfabFile(NodeMap nodeMap,string perfabPath) : base(perfabPath)
    {
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
    
    override protected string getOutFilePath(string path)
    {
        return path.Replace(".prefab", ".lh");
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
