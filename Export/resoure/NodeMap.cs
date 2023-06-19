using System.Collections.Generic;
using UnityEngine;

internal class NodeMap 
{
    private Dictionary<GameObject, JSONObject> nodeMaps;
    private Dictionary<GameObject, JSONObject> refMap;
    private Dictionary<GameObject, string> nodeIdMaps;
    private ResoureMap _resoureMap;
    private List<GameObject> needRefectNode;
    private GameObject[] _roots;
    private int idOff;
    public NodeMap(ResoureMap map,int idOff = 0)
    {
        this._resoureMap = map;
        this.idOff = idOff;
        this.nodeMaps = new Dictionary<GameObject, JSONObject>();
        this.refMap = new Dictionary<GameObject, JSONObject>();
        this.nodeIdMaps = new Dictionary<GameObject, string>();
    }
    public ResoureMap resoureMap
    {
        get
        {
            return this._resoureMap;
        }
    }
    public void setRoots(GameObject[] gameObjects)
    {
        this._roots = gameObjects; 
    }
    public void createNodeTree()
    {
        this.needRefectNode = new List<GameObject>();
        foreach (var data in this.nodeMaps)
        {
            GameObject gameObject = data.Key;
            if (gameObject.transform.parent == null)
            {
                continue;
            }
            if (!this.setNodeParent(gameObject))
            {
                this.needRefectNode.Add(gameObject);
            }
        }
    }
    public void createRefNodeTree()
    {
        foreach (var remap in this.refMap)
        {
            PerfabFile perfabFile = this._resoureMap.GetPerfabByObject(remap.Key);
            if (perfabFile != null)
            {
                remap.Value.AddField("_$prefab", perfabFile.uuid);
            }
        }
        foreach (var gameObject in this.needRefectNode){
            this.createRootPartner(gameObject);
        }
    }

    private void createRootPartner(GameObject gameObject)
    {
        GameObject root = PerfabFile.getPrefabInstanceRoot(gameObject.transform.parent.gameObject);
        this.setNodeParent(gameObject, root);
        List<int> indexList = new List<int>();
        for (var i = 0; i < this._roots.Length; i++)
        {
            if (this._roots[i] == root)
            {
                indexList.Add(i);
                break;
            }
        }
        List<GameObject> nodeList = new List<GameObject>();
        GameObject foundObject = gameObject;
        while (foundObject != root)
        {
            nodeList.Add(foundObject);
            foundObject = foundObject.transform.parent.gameObject;
        }
       
        for (var i = nodeList.Count - 1; i >= 0; i--)
        {
            int rootChildCount = root.transform.childCount;
            int foundIndex = -1;
            Transform target = nodeList[i].transform;
            for (var j = 0; j < rootChildCount; j++)
            {
                GameObject child = root.transform.GetChild(j).gameObject;
                if (child.transform == target)
                {
                    foundIndex++;
                    root = child;
                    break;
                }
                if (this.nodeIdMaps.ContainsKey(child))
                {
                    continue;
                }
                else
                {
                    foundIndex++;
                }
            }
            indexList.Add(foundIndex);
        }
        List<string> outIndex = new List<string>();
        this.getRefNodeId(indexList, outIndex);
        JSONObject nodeData = this.getJsonObject(gameObject);
        if (outIndex.Count > 1)
        {
            JSONObject partner = new JSONObject(JSONObject.Type.ARRAY);
            foreach (var index in outIndex)
            {
                partner.Add(index);
            }
            nodeData.AddField("_$parent", partner);
        }
        else if (outIndex.Count == 1)
        {
            nodeData.AddField("_$parent", outIndex[0]);
        }

        Transform pGameObject = gameObject.transform.parent;
        int numberChild = root.transform.childCount;
        int childIndex = -1;
        for(var i = 0; i < numberChild; i++)
        {
            if(pGameObject.GetChild(i) == gameObject.transform)
            {
                childIndex = i;
                break;
            }
        }
        if (childIndex > 0)
        {
            nodeData.AddField("_$index", childIndex);
        }
    }

    public void getRefNodeId(List<int> childIndex,List<string> outIndex,bool needAdd = false)
    {
        if(childIndex.Count<=1)
        {
            return;
        }
        GameObject root = this._roots[childIndex[0]];
        while (childIndex.Count > 2 && !this.refMap.ContainsKey(root))
        {
            childIndex.RemoveAt(0);
            if (childIndex.Count > 1)
            {
                root = root.transform.GetChild(childIndex[0]).gameObject;
            }
        }
        if (needAdd)
        {
            outIndex.Add(this.getGameObjectId(root));
        }
        
        if (this.refMap.ContainsKey(root))
        {
            childIndex[0] = 0;
            this._resoureMap.GetPerfabByObject(root).nodeMap.getRefNodeId(childIndex, outIndex,true);
        }
    }
    public void addNodeMap(GameObject gameObject, JSONObject nodeData,bool isRef)
    {
        this.nodeMaps.Add(gameObject, nodeData);
        int nodeId = this.nodeMaps.Count + this.idOff;
        string nodeStringId = "#" + nodeId;
        this.nodeIdMaps.Add(gameObject, nodeStringId);
        nodeData.AddField("_$id", nodeStringId);
        if (isRef)
        {
            this.refMap.Add(gameObject, nodeData);
        }
    }

    public string getGameObjectId(GameObject gameObject)
    {
        return this.nodeIdMaps[gameObject];
    }

    public  JSONObject getRefNodeIdObjet(GameObject gameObject)
    {
        JSONObject data = new JSONObject(JSONObject.Type.OBJECT);
        data.AddField("_$ref",this.getGameObjectId(gameObject));
        return data;
    }


    public bool setNodeParent(GameObject gameObject,GameObject partnerObject = null)
    {
        if(partnerObject == null)
        {
            partnerObject = gameObject.transform.parent.gameObject;
        }
        JSONObject partner = this.getJsonObject(partnerObject);
        if(partner == null)
        {
            return false;
        }
        JSONObject child = partner.GetField("_$child");
        if(child == null)
        {
            child = new JSONObject(JSONObject.Type.ARRAY);
            partner.AddField("_$child", child);
        }
        child.Add(this.getJsonObject(gameObject));
        return true;
    }

    public bool checkHaveNode(GameObject gameObject)
    {
        return this.nodeIdMaps.ContainsKey(gameObject);
    }

    public JSONObject getJsonObject(GameObject gameObject)
    {
        if (this.nodeMaps.ContainsKey(gameObject))
        {
            return this.nodeMaps[gameObject];
        }
        else
        {
            return null;
        }
    }

    public void writeCompoent()
    {
        foreach (var node in this.nodeMaps)
        {
            this._resoureMap.getComponentsData(node.Key, node.Value, this);
        }
    }
}
