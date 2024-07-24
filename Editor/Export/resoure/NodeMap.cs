using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

struct OriderIndex
{
    public int childIndex;
    public int perfabIndex;
}

internal class RefObject
{
    public GameObject gameObject;
    public List<GameObject> perfabRootGameObjects = new List<GameObject>();
    public List<PerfabFile> perfabfiles = new List<PerfabFile>();
    public JSONObject jsonDatas;
    public int nodeId;
    public int RefCount = -1;

    public void addRefData(GameObject gameObject,PerfabFile perfabFile)
    {
        this.perfabRootGameObjects.Add(gameObject);
        this.perfabfiles.Add(perfabFile);
        this.RefCount = this.perfabfiles.Count - 1;
    }
    public bool isRefObject()
    {
        return this.RefCount >= 0;
    }

    public bool isRefRoot()
    {
        return this.gameObject == this.perfabRootGameObjects[this.RefCount];
    }

    public GameObject refGameObject()
    {
        return this.perfabRootGameObjects[this.RefCount];
    }

    public PerfabFile getBasePerfab()
    {
        return this.perfabfiles[this.RefCount];
    }
}

internal class NodeMap 
{
    private Dictionary<GameObject, RefObject> refMap;
    private Dictionary<GameObject, JSONObject> overrideMaps;
    private Dictionary<GameObject, string> nodeIdMaps;
    private Dictionary<GameObject, OriderIndex> oriderIndexMap;
    private ResoureMap _resoureMap;
    private List<GameObject> _roots;
    private int idOff;
    public NodeMap(ResoureMap map,int idOff = 0)
    {
        this._resoureMap = map;
        this.idOff = idOff;
        this.refMap = new Dictionary<GameObject, RefObject>();
        this.nodeIdMaps = new Dictionary<GameObject, string>();
        this.overrideMaps = new Dictionary<GameObject, JSONObject>();
        this.oriderIndexMap = new Dictionary<GameObject, OriderIndex>();
        this._roots = new List<GameObject>();
    }

    public void setNode(GameObject gameObject,bool isFirstNode = false, bool isperfabRoot = false)
    {
        this.addGameObjectMap(gameObject, isperfabRoot);
        if (isFirstNode) this._roots.Add(gameObject);
    }

    private void addGameObjectMap(GameObject gameObject,   bool isperfabRoot = false)
    {
        if (this.refMap.ContainsKey(gameObject))
        {
            return;
        }
        JSONObject nodeData = JsonUtils.GetGameObject(gameObject, isperfabRoot);
        int nodeId = this.refMap.Count + this.idOff;
        string nodeStringId = "#" + nodeId;
        this.nodeIdMaps.Add(gameObject, nodeStringId);
        nodeData.AddField("_$id", nodeStringId);
        

        RefObject refObject = new RefObject();
        refObject.gameObject = gameObject;
        GameObject baseRefRoot = PerfabFile.getPrefabInstanceRoot(gameObject);
        if (baseRefRoot != null)
        {
            GameObject nearRoot = PerfabFile.GetNearestPrefabInstanceRoot(gameObject);
            string rt;
            while (nearRoot != baseRefRoot)
            {
                rt = PerfabFile.getPerfabFilePath(nearRoot);
                refObject.addRefData(nearRoot, this._resoureMap.getPerfabFile(rt));
                nearRoot = nearRoot.transform.parent.gameObject;
            }
            rt = PerfabFile.getPerfabFilePath(baseRefRoot);
            refObject.addRefData(baseRefRoot, this._resoureMap.getPerfabFile(rt));
        }
        

        refObject.jsonDatas = nodeData;
        refObject.nodeId = nodeId;
        refObject.gameObject = gameObject;
        if (refObject.isRefObject() && refObject.isRefRoot())
        {
            nodeData.AddField("_$prefab", refObject.getBasePerfab().uuid);
        }
        else
        {
            nodeData.AddField("_$type", "Sprite3D");
        }

        this.refMap.Add(gameObject, refObject);

    }
    public ResoureMap resoureMap
    {
        get
        {
            return this._resoureMap;
        }
    }

    public void createNodeTree()
    {
        foreach(var refdata in this.refMap)
        {
            RefObject refobject = refdata.Value;
            if (refobject.isRefObject()&&!refobject.isRefRoot())
            {
                continue;
            }
            GameObject gameObject = refobject.gameObject;
           
            if (gameObject.transform.parent == null) continue;
            GameObject partner = gameObject.transform.parent.gameObject;
            RefObject partnerref;
            if (this.refMap.TryGetValue(partner,out partnerref))
            {
                this.addChildToPartner(refobject.jsonDatas, partnerref.jsonDatas);
            }
            
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


    private List<string> getRefectIndex(GameObject gameObject,bool contentRoot = false)
    {
        List<string> outIndex = new List<string>();
        RefObject refObject;
        if (!this.refMap.TryGetValue(gameObject, out refObject))
        {
            return outIndex;
        }
        // GameObject root = refObject.refGameObject();
        if (refObject.RefCount < 0)
        {
            outIndex.Add("#" + refObject.nodeId.ToString());
        }
        else
        {
            GameObject foundObjec = gameObject;
            List<int> nodeIds = new List<int>();
            for (int index = refObject.RefCount; index >= 0; index--)
            {
                GameObject rootData = refObject.perfabRootGameObjects[index];
                this.getNodeRef(foundObjec, rootData, ref nodeIds);
                outIndex.Add("#" + refObject.perfabfiles[index].nodeMap.getGameNode(nodeIds).ToString());
                foundObjec = rootData;
            }
        }
       
      
        return outIndex;
    }

    private void getNodeRef(GameObject foundObject,GameObject root, ref List<int> paths)
    {
        paths.Clear();
        Transform gt = foundObject.transform;
        while(gt!=null&&gt.gameObject != root)
        {
            paths.Insert(0, gt.GetSiblingIndex());
            gt = gt.parent;
        }
        paths.Insert(0, 0);
    }
    public int getGameNode(List<int> paths)
    {
        Transform root = this._roots[paths[0]].transform;
        for(var i = 1; i < paths.Count; i++)
        {
            root = root.GetChild(paths[i]);
        }
        RefObject refObject;
        if(!this.refMap.TryGetValue(root.gameObject, out refObject))
        {
            return -1;
        }
        return refObject.nodeId;
    }
   /* public void getRefNodeId(List<OriderIndex> nodeList, List<string> outIndex)
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
        if (this.refMap.ContainsKey(root))
        {
            root = root.transform.parent.gameObject;
            OriderIndex oriderIndex = nodeList[0];
            oriderIndex.childIndex = 0;
            oriderIndex.perfabIndex = 0;
        }
        outIndex.Add(this.getGameObjectId(root));

        if (this.refMap.ContainsKey(root))
        {
            this._resoureMap.GetPerfabByObject(root).nodeMap.getRefNodeId(nodeList, outIndex);
        }
    }*/
    

    public string getGameObjectId(GameObject gameObject)
    {
        return this.nodeIdMaps[gameObject];
    }

    public  JSONObject getRefNodeIdObjet(GameObject gameObject)
    {
        JSONObject data = new JSONObject(JSONObject.Type.OBJECT);
        List<string> outIndex = this.getRefectIndex(gameObject, true);
      /*  if (this.nodeIdMaps.ContainsKey(gameObject)){
            data.AddField("_$ref", this.getGameObjectId(gameObject));
        }
        else
        {*/
           
            JSONObject overrideIndex = new JSONObject(JSONObject.Type.ARRAY);
            foreach (var index in outIndex)
            {
                overrideIndex.Add(index);
            }
            data.AddField("_$ref", overrideIndex);
      /*  }*/
        
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
        if (this.refMap.ContainsKey(gameObject))
        {
            return this.refMap[gameObject].jsonDatas;
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
        foreach (var remap in this.refMap)
        {
            RefObject refObject = remap.Value;
            if (!refObject.isRefObject())
            {
                this._resoureMap.getComponentsData(refObject.gameObject, refObject.jsonDatas, this);
            }
            else if(refObject.isRefRoot())
            {
                List<ObjectOverride> list = PrefabUtility.GetObjectOverrides(refObject.gameObject);
                for (var i = 0; i < list.Count; i++)
                {
                    ObjectOverride overdata = list[i];
                    GameObject gameObject;
                    if (overdata.instanceObject is GameObject)
                    {
                        gameObject = overdata.instanceObject as GameObject;
                        JSONObject jsData = this.getOverrideObject(gameObject, refObject.refGameObject());
                        JsonUtils.GetGameObject(gameObject, false, jsData);
                    }
                    else if (overdata.instanceObject is Transform)
                    {
                        gameObject = ((Transform)overdata.instanceObject).gameObject;
                        JSONObject jsData = this.getOverrideObject(gameObject, refObject.refGameObject());
                        jsData.AddField("transform", JsonUtils.GetTransfrom(gameObject));
                    }
                    else if (overdata.instanceObject is Component)
                    {
                        Component comp = overdata.instanceObject as Component;
                        gameObject = comp.gameObject;
                        JSONObject compents = this.getOverCompentsObject(gameObject, refObject.refGameObject());
                        this.resoureMap.writeComponentData(compents, comp, this, true);
                    }
                }
                List<AddedComponent> addlist = PrefabUtility.GetAddedComponents(refObject.gameObject);
                for (var i = 0; i < addlist.Count; i++)
                {
                    AddedComponent overdata = addlist[i];
                    Component comp = overdata.instanceComponent;
                    GameObject gameObject = comp.gameObject;
                    JSONObject compents = this.getOverCompentsObject(gameObject, refObject.gameObject);
                    this.resoureMap.writeComponentData(compents, comp, this, false);
                }
            }

        }
    }
}
