using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEditor.SceneManagement;
using System.IO;
using UnityEngine.SceneManagement;


internal class HierarchyFile
{
    private ResoureMap resouremap;
    private NodeMap nodeMap;
    private Scene scene;
    public HierarchyFile(Scene scene)
    {
        this.scene = scene;
        GameObject[] gameObjects = scene.GetRootGameObjects();
        var allNodes = getSceneAllNode(gameObjects);//场景中所有GameObject
        this.resouremap = new ResoureMap();
        // Set scene directory for UI2D prefab output path (_ui2d_prefabs/)
        string sceneLsPath = scene.path.Replace(System.IO.Path.GetExtension(scene.path), ".ls");
        this.resouremap.SetSceneDir(System.IO.Path.GetDirectoryName(sceneLsPath).Replace("\\", "/"));
        this.nodeMap = this.resouremap.AddNodeMap(2, sceneMode: true);
       
        foreach (var gameObject in allNodes)//遍历
        {
            this.nodeMap.setNode(gameObject,true,false);
        }
        this.resouremap.createNodeTree();
      
    }

    private List<GameObject> getSceneAllNode(GameObject[] gameObjects)
    {
        List<GameObject> lists = new List<GameObject>();
        for (int i = 0; i < gameObjects.Length; i++)
        {
            this.AddtoList(gameObjects[i], lists);
        }
        return lists;
    }

    private void AddtoList(GameObject gameObject, List<GameObject> list)
    {
        if (!gameObject.activeInHierarchy && ExportConfig.IgnoreNotActiveGameObject)
        {
            return;
        }

        // Canvas 及其子树整体跳过（UI 2D 导出暂未启用）
        if (gameObject.GetComponent<Canvas>() != null)
        {
            return;
        }

        list.Add(gameObject);
        if (gameObject.transform.childCount > 0)
        {
            for (int i = 0; i < gameObject.transform.childCount; i++)
            {
                AddtoList(gameObject.transform.GetChild(i).gameObject, list);
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
            GameObject[] gameObjects = scene.GetRootGameObjects();

            // The first level of a Unity scene is its root GameObjects.
            if (ExportConfig.BatchMade)
            {
                HashSet<string> usedFileNames = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < gameObjects.Length; i++)
                {
                    GameObject rootObject = gameObjects[i];
                    if (!rootObject.activeInHierarchy && ExportConfig.IgnoreNotActiveGameObject)
                        continue;
                    if (!this.nodeMap.checkHaveNode(rootObject))
                        continue;

                    string baseName = GameObjectUitls.cleanIllegalChar(rootObject.name, true);
                    string fileName = baseName + ".lh";
                    for (int suffix = 1; !usedFileNames.Add(fileName); suffix++)
                        fileName = baseName + "_" + suffix + ".lh";

                    JSONObject prefabJson = this.nodeMap.getPerfabJson(rootObject);
                    addAtlasPreloads(prefabJson);
                    this.resouremap.AddExportFile(new JsonFile(fileName, prefabJson, true));
                }
            }
            else
            {
                // Export the entire scene as one prefab with an identity transform.
                JSONObject prefabJson = new JSONObject(JSONObject.Type.OBJECT);
                prefabJson.AddField("_$ver", 1);
                prefabJson.AddField("_$id", "#0");
                prefabJson.AddField("_$type", "Sprite3D");
                prefabJson.AddField("name", scene.name);
                prefabJson.AddField("active", true);
                prefabJson.AddField("isStatic", false);
                prefabJson.AddField("layer", 0);

                JSONObject transform = new JSONObject(JSONObject.Type.OBJECT);
                transform.AddField("localPosition", JsonUtils.GetVector3Object(Vector3.zero));
                Quaternion rotation = Quaternion.identity;
                SpaceUtils.changeRotate(ref rotation, false);
                transform.AddField("localRotation", JsonUtils.GetQuaternionObject(rotation));
                transform.AddField("localScale", JsonUtils.GetVector3Object(Vector3.one));
                prefabJson.AddField("transform", transform);

                JSONObject children = new JSONObject(JSONObject.Type.ARRAY);
                for (int i = 0; i < gameObjects.Length; i++)
                {
                    GameObject rootObject = gameObjects[i];
                    if (!rootObject.activeInHierarchy && ExportConfig.IgnoreNotActiveGameObject)
                        continue;
                    if (!this.nodeMap.checkHaveNode(rootObject))
                        continue;
                    children.Add(this.nodeMap.getJsonObject(rootObject));
                }
                prefabJson.AddField("_$child", children);
                addAtlasPreloads(prefabJson);
                string fileName = GameObjectUitls.cleanIllegalChar(scene.name, true) + ".lh";
                this.resouremap.AddExportFile(new JsonFile(fileName, prefabJson, true));
            }
        }

        this.resouremap.SaveAllFile();
    }

    /// <summary>
    /// Add atlas _$preloads to a root JSON node (scene or prefab) so that
    /// atlas files are loaded before any sub-texture references are resolved.
    /// </summary>
    private void addAtlasPreloads(JSONObject rootNode)
    {
        List<string> atlasUUIDs = this.resouremap.GetAtlasFileUUIDs();
        if (atlasUUIDs.Count == 0) return;

        JSONObject preloads = new JSONObject(JSONObject.Type.ARRAY);
        JSONObject preloadTypes = new JSONObject(JSONObject.Type.ARRAY);
        foreach (string atlasUUID in atlasUUIDs)
        {
            preloads.Add(atlasUUID);
            preloadTypes.Add("Atlas");
        }
        rootNode.AddField("_$preloads", preloads);
        rootNode.AddField("_$preloadTypes", preloadTypes);
    }

    private void getSceneNode() {
        JSONObject node = new JSONObject(JSONObject.Type.OBJECT);
        var sceneName = scene.path.Replace(Path.GetExtension(scene.path), ".ls");
        node.AddField("_$ver", 0);
        node.AddField("_$id", "#0");
        node.AddField("_$type", "Scene");
        node.AddField("left", 0);
        node.AddField("right", 0);
        node.AddField("top", 0);
        node.AddField("bottom", 0);
        node.AddField("name", "Scene2D");

        addAtlasPreloads(node);

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

        GameObject[] gameObjects = scene.GetRootGameObjects();

        JSONObject child = new JSONObject(JSONObject.Type.ARRAY);
        scene3dNode.AddField("_$child", child);
        for (int i = 0; i < gameObjects.Length; i++)
        {
            // Canvas 已在 AddtoList 中被整体跳过，这里不会出现 Canvas 根节点
            child.Add(this.nodeMap.getJsonObject(gameObjects[i].gameObject));
        }

        this.resouremap.AddExportFile(new JsonFile(sceneName, node, true));
    }
}
