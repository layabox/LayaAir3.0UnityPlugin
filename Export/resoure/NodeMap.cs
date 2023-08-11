using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

struct OriderIndex
{
    public int childIndex;
    public int perfabIndex;
}
internal class NodeMap 
{
    private Dictionary<GameObject, JSONObject> nodeMaps;
    private Dictionary<GameObject, JSONObject> refMap;
    private Dictionary<GameObject, JSONObject> overrideMaps;
    private Dictionary<GameObject, string> nodeIdMaps;
    private Dictionary<GameObject, OriderIndex> oriderIndexMap;
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
        this.overrideMaps = new Dictionary<GameObject, JSONObject>();
        this.oriderIndexMap = new Dictionary<GameObject, OriderIndex>();
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
        int length = this._roots.Length;
        for(int i = 0; i < length; i++)
        {
            this.CreateOriderMap(this._roots[i], i, -1);
        }
    }
    private void CreateOriderMap(GameObject gameObject,int childIndex,int perfabIndex)
    {
        OriderIndex oriderIndex = new OriderIndex();
        oriderIndex.childIndex = childIndex;
        oriderIndex.perfabIndex = perfabIndex;
        this.oriderIndexMap.Add(gameObject, oriderIndex);
        int childCount = gameObject.transform.childCount;
        int foundIndex = -1;
        for (int j = 0; j < childCount; j++)
        {
            GameObject child = gameObject.transform.GetChild(j).gameObject;
            if (!this.nodeIdMaps.ContainsKey(child))
            {
                foundIndex++;
                this.CreateOriderMap(child, j, foundIndex);
            }
            else
            {
                this.CreateOriderMap(child, j, -1);
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
        if (!this.oriderIndexMap.ContainsKey(root))
        {
            Debug.LogError("not get the root:" + root.name);
            return;
        }
        this.setNodeParent(gameObject, root);
        List<string> outIndex = this.getRefectIndex(gameObject);
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

        int childIndex = this.oriderIndexMap[gameObject].childIndex;
        if (childIndex > 0)
        {
            nodeData.AddField("_$index", childIndex);
        }
    }

    private List<string> getRefectIndex(GameObject gameObject,bool contentRoot = false)
    {
        GameObject root = PerfabFile.getPrefabInstanceRoot(gameObject);
        List<OriderIndex> nodeList = new List<OriderIndex>();
        GameObject foundObject = gameObject;
        while (foundObject!= root&& !this.nodeMaps.ContainsKey(foundObject))
        {
            nodeList.Insert(0, this.oriderIndexMap[foundObject]);
            foundObject = foundObject.transform.parent.gameObject;
        }
        List<string> outIndex = new List<string>();
        if (contentRoot)
        {
            outIndex.Add(this.getGameObjectId(root));
        }
        if (this.refMap.ContainsKey(root))
        {
            OriderIndex oriderIndex = new OriderIndex();
            oriderIndex.childIndex = 0;
            oriderIndex.perfabIndex = 0;
            nodeList.Insert(0, oriderIndex);
            this._resoureMap.GetPerfabByObject(root).nodeMap.getRefNodeId(nodeList, outIndex);
        }
        return outIndex;
    }
    public void getRefNodeId(List<OriderIndex> nodeList, List<string> outIndex)
    {
        if(nodeList.Count<=0)
        {
            return;
        }
        GameObject root = this._roots[nodeList[0].perfabIndex];
        while (nodeList.Count > 0 && !this.refMap.ContainsKey(root))
        {
            nodeList.RemoveAt(0);
            if(nodeList.Count>0)
            {
                root = root.transform.GetChild(nodeList[0].perfabIndex).gameObject;
            }
        }
        if (!this.nodeMaps.ContainsKey(root))
        {
            root = root.transform.parent.gameObject;
            OriderIndex oriderIndex = new OriderIndex();
            oriderIndex.childIndex = 0;
            oriderIndex.perfabIndex = 0;
            nodeList.Insert(0, oriderIndex);
        }
        outIndex.Add(this.getGameObjectId(root));

        if (this.refMap.ContainsKey(root))
        {
            this._resoureMap.GetPerfabByObject(root).nodeMap.getRefNodeId(nodeList, outIndex);
        }
    }
    public void addNodeMap(GameObject gameObject, JSONObject nodeData,bool isRef)
    {
        if(this.nodeIdMaps.ContainsKey(gameObject))
        {
            return;
        }
		this.nodeMaps.Add(gameObject, nodeData);
        int nodeId = this.nodeMaps.Count + this.idOff;
        string nodeStringId = "#" + nodeId;
        this.nodeIdMaps.Add(gameObject, nodeStringId);
        nodeData.AddField("_$id", nodeStringId);
        nodeData.AddField("_$type", "Sprite3D");
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
        if (this.nodeIdMaps.ContainsKey(gameObject)){
            data.AddField("_$ref", this.getGameObjectId(gameObject));
        }
        else
        {
            List<string> outIndex = this.getRefectIndex(gameObject,true);
            JSONObject overrideIndex = new JSONObject(JSONObject.Type.ARRAY);
            foreach (var index in outIndex)
            {
                overrideIndex.Add(index);
            }
            data.AddField("_$ref", overrideIndex);
        }
        
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
        this.addChildToPartner(this.getJsonObject(gameObject), partner);
        return true;
    }
    
    private bool addChildToPartner(JSONObject child,JSONObject partner)
    {
        JSONObject childs = partner.GetField("_$child");
        if (childs == null)
        {
            childs = new JSONObject(JSONObject.Type.ARRAY);
            partner.AddField("_$child", childs);
        }
        childs.Add(child);
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

    public JSONObject getPerfabJson(GameObject gameObject)
    {
        JSONObject nodeData = new JSONObject(JSONObject.Type.OBJECT);
        nodeData.AddField("_$ver", 1);
        JSONObject jsonData = this.getJsonObject(gameObject);
        foreach(var key in jsonData.keys)
        {
            nodeData.AddField(key, jsonData.GetField(key));
        }
        return nodeData;
    }

    private JSONObject getOverrideObject(GameObject gameObject,GameObject root)
    {
        JSONObject childdata;
        if (gameObject != root)
        {
            if (!this.overrideMaps.TryGetValue(gameObject, out childdata))
            {
                childdata = new JSONObject(JSONObject.Type.OBJECT);
                List<string> outIndex = this.getRefectIndex(gameObject);
                JSONObject overrideIndex = new JSONObject(JSONObject.Type.ARRAY);
                foreach (var index in outIndex)
                {
                    overrideIndex.Add(index);
                }
                childdata.AddField("_$override", overrideIndex);
                this.overrideMaps.Add(gameObject, childdata);
                this.addChildToPartner(childdata, this.getJsonObject(root));
            }
        }
        else
        {
            childdata = this.getJsonObject(gameObject);
        }
            

        return childdata;
    }

    private JSONObject getOverCompentsObject(GameObject gameObject, GameObject root)
    {
        JSONObject objectData = this.getOverrideObject(gameObject, root);
        JSONObject comps = objectData.GetField("_$comp");
        if (comps == null)
        {
            comps = new JSONObject(JSONObject.Type.ARRAY);
            objectData.AddField("_$comp", comps);
        }
        return comps;
    }
    public void writeCompoent()
    {
        foreach (var node in this.nodeMaps)
        {
            GameObject gameObject = node.Key;
            if(gameObject != PerfabFile.getPerfabObject(gameObject))
            {
                this._resoureMap.getComponentsData(node.Key, node.Value, this);
            }
           
        }

        foreach (var remap in this.refMap)
        {
            GameObject rootObject = remap.Key;
            List<ObjectOverride> list = PrefabUtility.GetObjectOverrides(rootObject);
            for (var i = 0; i < list.Count; i++)
            {
                ObjectOverride overdata = list[i];
                GameObject gameObject;
                if(overdata.instanceObject is GameObject)
                {
                    gameObject = overdata.instanceObject  as GameObject;
                    JSONObject jsData = this.getOverrideObject(gameObject, rootObject);
                    JsonUtils.GetGameObject(gameObject, false, jsData);
                }
                else if(overdata.instanceObject is Transform){
                    gameObject = ((Transform)overdata.instanceObject).gameObject;
                    JSONObject jsData = this.getOverrideObject(gameObject, rootObject);
                    jsData.AddField("transform", JsonUtils.GetTransfrom(gameObject));
                }
                else
                {
                    Component comp = overdata.instanceObject as Component;
                    gameObject = comp.gameObject;
                    JSONObject compents = this.getOverCompentsObject(gameObject, rootObject);
                    this.resoureMap.writeComponentData(compents, comp, this, true);
                }
            }
            List<AddedComponent> addlist = PrefabUtility.GetAddedComponents(rootObject);
            for (var i = 0; i < addlist.Count; i++)
            {
                AddedComponent overdata = addlist[i];
                Component comp = overdata.instanceComponent;
                GameObject gameObject = comp.gameObject;
                JSONObject compents = this.getOverCompentsObject(gameObject, rootObject);
                this.resoureMap.writeComponentData(compents, comp, this, false);
            }
        }
    }
}
