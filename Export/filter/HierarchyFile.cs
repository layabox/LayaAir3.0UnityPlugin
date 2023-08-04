using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

internal class HierarchyFile 
{
    private List<GameObject> notPerfabNodes;
    private ResoureMap resouremap;
    private NodeMap nodeMap;
    public HierarchyFile()
    {
        this.notPerfabNodes = new List<GameObject>();
        var prefabs = Object.FindObjectsOfType<GameObject>(true);//场景中所有GameObject
        Dictionary<string, GameObject> perfabList = new Dictionary<string, GameObject>();//用于避免重复的列表
        foreach (var gameObject in prefabs)//遍历
        {
            var rt = PerfabFile.getPerfabFilePath(gameObject);//物体的Prefab根节点
            if (rt == null)
            {
                this.notPerfabNodes.Add(gameObject);
                continue;
            }
            GameObject perfabRoot = PerfabFile.getPrefabInstanceRoot(gameObject);
            if (perfabList.ContainsKey(rt))
            {
                if (!this.notPerfabNodes.Contains(perfabRoot))
                {
                    this.notPerfabNodes.Add(perfabRoot);
                }
                continue;
            }
            perfabList.Add(rt, perfabRoot);//增加到列表中
        }
        this.resouremap = new ResoureMap(perfabList);
        this.nodeMap = this.resouremap.AddNodeMap(2);
        foreach (var map in perfabList)
        {
            GameObject gameObject = map.Value;
            this.nodeMap.addNodeMap(gameObject, JsonUtils.GetGameObject(gameObject), true);
        }
        GameObject[] gameObjects = SceneManager.GetActiveScene().GetRootGameObjects();

        for (int i = 0; i < gameObjects.Length; i++)
        {
            getGameObjectData(gameObjects[i]);
        }
        this.nodeMap.setRoots(gameObjects);
        this.resouremap.createNodeTree();
      
    }

    private void getGameObjectData(GameObject gameObject)
    {
        if(!gameObject.activeInHierarchy&& ExportConfig.IgnoreNotActiveGameObject)
        {
            return;
        }
        if (this.notPerfabNodes.Contains(gameObject))
        {
            JSONObject nodeData = JsonUtils.GetGameObject(gameObject);
            this.nodeMap.addNodeMap(gameObject, nodeData, false);
        }
        if (gameObject.transform.childCount > 0)
        {
            for (int i = 0; i < gameObject.transform.childCount; i++)
            {
                getGameObjectData(gameObject.transform.GetChild(i).gameObject);
            }
        }
    }

    public void saveAllFile(bool isScene)
    {
        if (isScene)
        {
            this.getSceneNode();
        }
        else
        {
            GameObject[] gameObjects = SceneManager.GetActiveScene().GetRootGameObjects();
            for (int i = 0; i < gameObjects.Length; i++)
            {
                GameObject gameObject = gameObjects[i];
                
                this.resouremap.AddExportFile(new JsonFile(gameObject.name +".lh", this.nodeMap.getPerfabJson(gameObject)));
            }
        }
        
        this.resouremap.SaveAllFile();
    }

    private void getSceneNode()
    {
        JSONObject node = new JSONObject(JSONObject.Type.OBJECT);
        var sceneName = SceneManager.GetActiveScene().name + ".ls";
        node.AddField("_$ver", 0);
        node.AddField("_$id", "#0");
        node.AddField("_$type", "Scene");
        node.AddField("left", 0);
        node.AddField("right", 0);
        node.AddField("top", 0);
        node.AddField("bottom", 0);
        node.AddField("name", "Scene2D");
        JSONObject fchild = new JSONObject(JSONObject.Type.ARRAY);
        node.AddField("_$child", fchild);
        JSONObject scene3dNode = new JSONObject(JSONObject.Type.OBJECT);
        fchild.Add(scene3dNode);
        scene3dNode.AddField("_$id", "#1");
        scene3dNode.AddField("_$type", "Scene3D");
        scene3dNode.AddField("name", "Scene3D");

        Material skyBoxMaterial = RenderSettings.skybox;
        if (skyBoxMaterial != null)
        {
            JSONObject skyRender = new JSONObject(JSONObject.Type.OBJECT);
            skyRender.AddField("meshType", "dome");
            JSONObject filedata = this.resouremap.GetMaterialData(skyBoxMaterial);
            skyRender.AddField("material", filedata);
            scene3dNode.AddField("skyRenderer", skyRender);
        }

        JSONObject ambientColor = JsonUtils.GetColorObject(RenderSettings.ambientLight);
        scene3dNode.AddField("ambientColor", ambientColor);



        if (RenderSettings.ambientMode == AmbientMode.Skybox)
        {
            scene3dNode.AddField("ambientMode", 1);

            JSONObject ambientProbe = new JSONObject(JSONObject.Type.OBJECT);
            scene3dNode.AddField("ambientSH", ambientProbe);
            ambientProbe.AddField("_$type", "Float32Array");
            JSONObject ambientValue = new JSONObject(JSONObject.Type.ARRAY);
            ambientProbe.AddField("value", ambientValue);
            this.resouremap.GetSHOrigin(ambientValue);
            scene3dNode.AddField("ambientSphericalHarmonicsIntensity", RenderSettings.ambientIntensity);
        }
        else
        {
            scene3dNode.AddField("ambientMode", 0);
            scene3dNode.AddField("ambientSphericalHarmonicsIntensity", 1.0f);
        }


        scene3dNode.AddField("enableFog", RenderSettings.fog);
        scene3dNode.AddField("fogStart", RenderSettings.fogStartDistance);
        scene3dNode.AddField("fogRange", RenderSettings.fogEndDistance - RenderSettings.fogStartDistance);

        scene3dNode.AddField("fogColor", JsonUtils.GetColorObject(RenderSettings.fogColor));

        GameObject[] gameObjects = SceneManager.GetActiveScene().GetRootGameObjects();

        if (gameObjects.Length > 0)
        {
            JSONObject child = new JSONObject(JSONObject.Type.ARRAY);
            scene3dNode.AddField("_$child", child);
            for (int i = 0; i < gameObjects.Length; i++)
            {
                child.Add(this.nodeMap.getJsonObject(gameObjects[i].gameObject));
            }
        }
        else
        {
            scene3dNode.AddField("_$child", new JSONObject(JSONObject.Type.ARRAY));
        }
        this.resouremap.AddExportFile(new JsonFile(sceneName, node));
    }
}
