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
    public static GameObject GetNearestPrefabInstanceRoot(GameObject gameObject)
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
    private string _perfabPath;
    public PerfabFile(NodeMap nodeMap,string perfabPath) : base(perfabPath)
    {
        this._perfabPath = perfabPath;
        this._nodeMap = nodeMap;
       
    }

    public void crateNodeData()
    {
        this.gameObject = PrefabUtility.LoadPrefabContents(this._perfabPath) as GameObject;
        this.getGameObjectData(this.gameObject, true, true);
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

    private void getGameObjectData(GameObject gameObject, bool isFirstNode = false, bool isperfabRoot = false)
    {
        this._nodeMap.setNode(gameObject, isFirstNode, isperfabRoot);

        if (gameObject.transform.childCount > 0)
        {
            for (int i = 0; i < gameObject.transform.childCount; i++)
            {
                getGameObjectData(gameObject.transform.GetChild(i).gameObject,false,false);
            }
        }
    }
    public override void SaveFile(Dictionary<string, FileData> exportFiles)
    {
        base.saveMeta();
        JSONObject data = this._nodeMap.getJsonObject(this.gameObject);
        FileStream fs = new FileStream(this.outPath, FileMode.Create, FileAccess.Write);
        StreamWriter writer = new StreamWriter(fs);
        writer.Write(data.Print(true));
        writer.Close();
    }

     public void destory()
    {
        GameObject.DestroyImmediate(this.gameObject);
    }
}
