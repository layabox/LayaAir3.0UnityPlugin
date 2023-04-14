using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using FileUtil = Util.FileUtil;

public class LayaAir3Export
{
   
    private static Dictionary<string, FileData> exportFiles = new Dictionary<string, FileData>();
    public static void ExportScene()
    {
        GameObjectUitls.init();
        MetarialUitls.init();

        TextureFile.init();
        AnimationCurveGroup.init();
        LayaInstance = 1;
        unityToLayaInstance.Clear();
        exportFiles.Clear();
        if(ExportConfig.FirstlevelMenu == 0)
        {
            getSceneNode(true);
        }else if (!ExportConfig.BatchMade)
        {
            getSceneNode(false);
        }
        else
        {
            getPerfabeNode();
        }
        
        foreach (var file in exportFiles)
        {
            file.Value.SaveFile(exportFiles);
        }

        if (FileUtil.getStatuse())
            Debug.Log("Exported Successful");
        else
            Debug.Log("Exporting have some error !!!");
    }

   
    private static Dictionary<string, int> unityToLayaInstance = new Dictionary<string, int>();
    private static int LayaInstance = 1;
    private static int getLayaInstance()
    {
        return LayaInstance++;
    }
    private static void getSceneNode(bool isscene)
    {
        JSONObject node = new JSONObject(JSONObject.Type.OBJECT);
        var sceneName = SceneManager.GetActiveScene().name;
        if (isscene)
        {
            sceneName += ".ls";
        }
        else
        {
            sceneName += ".lh";
        }
        JsonFile file = new JsonFile(sceneName, node);
        exportFiles.Add(file.filePath, file);
        var id = getLayaInstance();
        node.AddField("_$ver", id);
        node.AddField("_$id", "#"+ id.ToString());
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
        scene3dNode.AddField("_$id", "#"+getLayaInstance().ToString());
        scene3dNode.AddField("_$type", "Scene3D");
        scene3dNode.AddField("name", "Scene3D");



        Material skyBoxMaterial = RenderSettings.skybox;
        if (skyBoxMaterial != null)
        {

            JSONObject skyRender = new JSONObject(JSONObject.Type.OBJECT);
            skyRender.AddField("meshType", "dome");
            JSONObject filedata = getSkyMaterialData(skyBoxMaterial, file);
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
            getSHOrigin(ambientValue);
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
                getGameObjectData(gameObjects[i].gameObject,  child,file);
            }
        }
        else
        {
            scene3dNode.AddField("_$child", new JSONObject(JSONObject.Type.ARRAY));
        }
    }

    private static void getPerfabeNode()
    {

        GameObject[] gameObjects = SceneManager.GetActiveScene().GetRootGameObjects();
        for (int i = 0; i < gameObjects.Length; i++)
        {
            GameObject gameObject = gameObjects[i];
            if (ExportConfig.IgnoreNotActiveGameObject && !gameObject.activeSelf)
            {
                continue;
            }
            JSONObject jsonData = new JSONObject(JSONObject.Type.OBJECT);
            jsonData.AddField("_$ver", 1);
            JSONObject childData = new JSONObject(JSONObject.Type.ARRAY);
            jsonData.AddField("_$child", childData);
            JsonFile file = new JsonFile(gameObject.name + ".lh", jsonData);
            exportFiles.Add(file.filePath, file);
            getGameObjectData(gameObjects[i].gameObject, childData, file);
        }

    }
    private static JSONObject getSkyMaterialData(Material material, JsonFile file)
    {
        string materialPath = getMatertialPath(material);
        string materialLmatPath = GameObjectUitls.cleanIllegalChar(materialPath.Split('.')[0], false) + ".lmat";
        JsonFile materialFile = new JsonFile(materialLmatPath, new JSONObject(JSONObject.Type.OBJECT));
        exportFiles.Add(materialFile.filePath, materialFile);
        if(material.shader.name == "Skybox/6 Sided")
        {
            MetarialUitls.WriteSkyMetarial(material, materialFile, exportFiles);
        }
        else
        {
            MetarialUitls.WriteMetarial(material, materialFile, exportFiles);
        }
        JSONObject materFiledata = new JSONObject(JSONObject.Type.OBJECT);
        materFiledata.AddField("_$uuid", materialFile.uuid);
        materFiledata.AddField("_$type", "Material");
        return materFiledata;
    }
    private static JSONObject getMaterialData(Material material,JsonFile file)
    {
        string materialPath = getMatertialPath(material);
        string materialLmatPath = GameObjectUitls.cleanIllegalChar(materialPath.Split('.')[0], false) + ".lmat";
        JsonFile jsonFile;
        if (!exportFiles.ContainsKey(materialLmatPath))
        {
            JSONObject materialData = new JSONObject(JSONObject.Type.OBJECT);
            jsonFile = new JsonFile(materialLmatPath, materialData);
            MetarialUitls.WriteMetarial(material, jsonFile, exportFiles);
            exportFiles.Add(jsonFile.filePath, jsonFile);
        }
        else
        {
            jsonFile = exportFiles[materialLmatPath] as JsonFile;
        }

        JSONObject materFiledata = new JSONObject(JSONObject.Type.OBJECT);
        materFiledata.AddField("_$uuid", jsonFile.uuid);
        materFiledata.AddField("_$type", "Material");
        return materFiledata;
    }


    private static string getMatertialPath(Material material)
    {
        string materialPath = AssetDatabase.GetAssetPath(material.GetInstanceID());
        if (materialPath == "Resources/unity_builtin_extra")
        {
            return "Resources/" + material.name;
        }
        else
        {
            return materialPath;
        }
    }


    private static JSONObject getMeshFilterComponentData(GameObject gameObject, JsonFile file)
    {
        Mesh mesh = gameObject.GetComponent<MeshFilter>().sharedMesh;
        if (mesh == null)
        {
            Debug.LogWarning("LayaAir3D Warning(Code:1001) : " + gameObject.name + "'s MeshFilter Component Mesh data can't be null!");
        }

        string meshName = GameObjectUitls.cleanIllegalChar(mesh.name, true);
        string path = AssetDatabase.GetAssetPath(mesh.GetInstanceID());
        string lmPath = GameObjectUitls.cleanIllegalChar(path.Split('.')[0], false) + "-" + meshName;
        lmPath += ".lm";
        BufferFile meshFile;
        if (!exportFiles.ContainsKey(lmPath))
        {
            meshFile = new BufferFile(lmPath);
            MeshUitls.writeMesh(mesh, meshName, meshFile.filesteam);
            exportFiles.Add(meshFile.filePath, meshFile);
            if (mesh.uv2.Length > 0 && ExportConfig.AutoVerticesUV1)
            {
                JSONObject autouv1 = new JSONObject(JSONObject.Type.OBJECT);
                autouv1.AddField("generateLightmapUVs", true);
                meshFile.metaData().AddField("importer", autouv1);
            }
        }
        else
        {
            meshFile = exportFiles[lmPath] as BufferFile;
        }

        JSONObject meshFiledata = new JSONObject(JSONObject.Type.OBJECT);
        meshFiledata.AddField("_$uuid", meshFile.filePath);
        meshFiledata.AddField("_$type", "mesh");
        if (file != null)
        {
            file.AddRegistList(meshFile.filePath);
        }
        JSONObject compData = new JSONObject(JSONObject.Type.OBJECT);
        compData.AddField("_$type", "MeshFilter");
        compData.AddField("sharedMesh", meshFiledata);
        return compData;

    }

    private static JSONObject getMeshRenderComponmentData(GameObject gameObject, JsonFile file)
    {
        MeshRenderer render = gameObject.GetComponent<MeshRenderer>();
        Material[] materials = render.sharedMaterials;
        JSONObject sharedMaterials = new JSONObject(JSONObject.Type.ARRAY);
        for(var i = 0; i < materials.Length; i++)
        {
            Material mat = materials[i];
            if(mat == null)
            {
                Debug.LogWarning("LayaAir3D Warning(Code:1002) : " + gameObject.name + "'s MeshRender Component materials data can't be null!");
            }
            else
            {
                sharedMaterials.Add(getMaterialData(mat, file));
            }
        }

        JSONObject compData = new JSONObject(JSONObject.Type.OBJECT);
        compData.AddField("_$type", "MeshRenderer");
        compData.AddField("sharedMaterials", sharedMaterials);
        return compData;
    }


    private static JSONObject getRefNodeData(GameObject gameObject)
    {
        int nodeId = getInstanceIdByGameObject(gameObject);
        JSONObject boneData = new JSONObject(JSONObject.Type.OBJECT);
        boneData.AddField("_$ref", "#" + nodeId.ToString());
        return boneData;
    }
    private static void getSkinnerMeshRenderComponmentData(GameObject gameObject, JsonFile file, JSONObject components)
    {
        SkinnedMeshRenderer skinnedMeshRenderer = gameObject.GetComponent<SkinnedMeshRenderer>();


        Mesh mesh = skinnedMeshRenderer.sharedMesh;
        if (mesh != null)
        {
            string meshName = GameObjectUitls.cleanIllegalChar(mesh.name, true);
            string path2 = AssetDatabase.GetAssetPath(mesh.GetInstanceID());

            string lmPath2 = GameObjectUitls.cleanIllegalChar(path2.Split('.')[0], false) + "-" + meshName;
            lmPath2 += ".lm";

            BufferFile meshFile;
            if (!exportFiles.ContainsKey(lmPath2))
            {
                meshFile = new BufferFile(lmPath2);
                MeshUitls.writeSkinnerMesh(skinnedMeshRenderer, meshName, meshFile.filesteam);
                exportFiles.Add(meshFile.filePath, meshFile);
            }
            else
            {
                meshFile = exportFiles[lmPath2] as BufferFile;
            }

            JSONObject meshFiledata = new JSONObject(JSONObject.Type.OBJECT);
            meshFiledata.AddField("_$uuid", meshFile.uuid);
            meshFiledata.AddField("_$type", "mesh");
            JSONObject fileterData = new JSONObject(JSONObject.Type.OBJECT);
            fileterData.AddField("_$type", "MeshFilter");
            fileterData.AddField("sharedMesh", meshFiledata);
            components.Add(fileterData);
        }
        else
        {
            Debug.LogWarning("LayaAir3D Warning(Code:1001) : " + gameObject.name + "'s MeshFilter Component Mesh data can't be null!");
        }

        
        Material[] materials = skinnedMeshRenderer.sharedMaterials;
        JSONObject sharedMaterials = new JSONObject(JSONObject.Type.ARRAY);
        for (var i = 0; i < materials.Length; i++)
        {
            Material mat = materials[i];
            if (mat == null)
            {
                Debug.LogWarning("LayaAir3D Warning(Code:1002) : " + gameObject.name + "'s MeshRender Component materials data can't be null!");
            }
            else
            {
                sharedMaterials.Add(getMaterialData(mat, file));
            }
        }

        JSONObject compData = new JSONObject(JSONObject.Type.OBJECT);
        compData.AddField("_$type", "SkinnedMeshRenderer");
        compData.AddField("sharedMaterials", sharedMaterials);
       

        Bounds bounds = skinnedMeshRenderer.localBounds;
        Vector3 oriCenter = bounds.center;
        Vector3 center = new Vector3(-oriCenter.x, oriCenter.y, oriCenter.z);
        Vector3 extents = bounds.extents;
        Vector3 min = center - extents;
        Vector3 max = center + extents;

        JSONObject boundBoxNode = new JSONObject(JSONObject.Type.OBJECT);
        compData.AddField("boundBox", boundBoxNode);
        boundBoxNode.AddField("_$type", "Bounds");
        boundBoxNode.AddField("min", JsonUtils.GetVector3Object(min));
        boundBoxNode.AddField("max", JsonUtils.GetVector3Object(max));

        JSONObject bones = new JSONObject(JSONObject.Type.ARRAY);
        compData.AddField("_bones", bones);
        Transform[] bonesTransform = skinnedMeshRenderer.bones;
        for (int i = 0; i < bonesTransform.Length; i++)
        {
            bones.Add(getRefNodeData(bonesTransform[i].gameObject));
        }

        //rootBone
        if (skinnedMeshRenderer.rootBone)
        {
            JSONObject rootBone = getRefNodeData(skinnedMeshRenderer.rootBone.gameObject);
            compData.AddField("rootBone", rootBone);
        }

        components.Add(compData);
    }
   

    private static int getInstanceIdByGameObject(GameObject gameObject)
    {
        string objectId = gameObject.GetInstanceID().ToString();
        if (!unityToLayaInstance.ContainsKey(gameObject.GetInstanceID().ToString()))
        {
            unityToLayaInstance.Add(objectId, getLayaInstance());
        }
        return unityToLayaInstance[objectId];
    }
    private static void getGameObjectData(GameObject gameObject, JSONObject parentsChildNodes, JsonFile file)
    {
        if (ExportConfig.IgnoreNotActiveGameObject && !gameObject.activeSelf)
        {
            return;
        }
        JSONObject node = new JSONObject(JSONObject.Type.OBJECT);

        node.AddField("_$id", "#" + getInstanceIdByGameObject(gameObject));


        node.AddField("name", gameObject.name);
        node.AddField("active", gameObject.activeSelf);
        StaticEditorFlags staticEditorFlags = GameObjectUtility.GetStaticEditorFlags(gameObject);
        node.AddField("isStatic", ((int)staticEditorFlags & (int)StaticEditorFlags.BatchingStatic) > 0);
        node.AddField("layer", gameObject.layer);
        node.AddField("transform", JsonUtils.GetTransfrom(gameObject));

        getComponentsData(gameObject, node,file);

        if (gameObject.transform.childCount > 0)
        {
            JSONObject child = new JSONObject(JSONObject.Type.ARRAY);
            for (int i = 0; i < gameObject.transform.childCount; i++)
            {
                getGameObjectData(gameObject.transform.GetChild(i).gameObject, child,file);
            }
            node.AddField("_$child", child);
        }
       
        parentsChildNodes.Add(node);

    }
    private static void getComponentsData(GameObject gameObject, JSONObject node, JsonFile file)
    {
        List<ComponentType> components = GameObjectUitls.componentsOnGameObject(gameObject);

        JSONObject component = new JSONObject(JSONObject.Type.ARRAY);


        // components
        node.AddField("_$comp", component);

        //-----------------------------------------------------------------------------------------------------------//
        //DirectionalLight
        if (components.IndexOf(ComponentType.DirectionalLight) != -1)
        {
            component.Add(JsonUtils.GetDirectionalLightComponentData(gameObject));
        }
        if (components.IndexOf(ComponentType.PointLight) != -1)
        {
            component.Add(JsonUtils.GetPointLightComponentData(gameObject));
        }
        if (components.IndexOf(ComponentType.SpotLight) != -1)
        {
            component.Add(JsonUtils.GetSpotLightComponentData(gameObject));
        }
        //Camera
        if (components.IndexOf(ComponentType.Camera) != -1)
        {
            JsonUtils.getCameraComponentData(gameObject, node);
            node.AddField("_$type", "Camera");
        }
        else
        {
            node.AddField("_$type", "Sprite3D");
        }

        if (components.IndexOf(ComponentType.MeshFilter) != -1)
        {
            component.Add(getMeshFilterComponentData(gameObject,file));
        }
        if (components.IndexOf(ComponentType.MeshRenderer) != -1)
        {
            component.Add(getMeshRenderComponmentData(gameObject, file));
        }
        if (components.IndexOf(ComponentType.SkinnedMeshRenderer) != -1)
        {
            getSkinnerMeshRenderComponmentData(gameObject, file, component);
        }

        if (components.IndexOf(ComponentType.Animator) != -1)
        {
            component.Add(getAnimatorComponentData(gameObject));
        }

        if (components.IndexOf(ComponentType.Animation) != -1)
        {
            component.Add(getAnimationComponentData(gameObject));
        }

        if (components.IndexOf(ComponentType.ReflectionProbe) != -1)
        {
            component.Add(getReflectionProbe(gameObject));
        }

        if (components.IndexOf(ComponentType.LodGroup) != -1)
        {
            component.Add(getLodGroup(gameObject));
        }

    }

    private static JSONObject getLodGroup(GameObject gameObject)
    {
        LODGroup lodGroup = gameObject.GetComponent<LODGroup>();
      
        JSONObject compData = new JSONObject(JSONObject.Type.OBJECT);
        compData.AddField("_$type", "LODGroup");
        JSONObject lodDatas = new JSONObject(JSONObject.Type.ARRAY);
        LOD[] lods = lodGroup.GetLODs();
        for(var i = 0; i < lods.Length; i++)
        {
            LOD lod = lods[i];
            JSONObject lodData = new JSONObject(JSONObject.Type.OBJECT);
            lodData.AddField("_$type", "LODInfo");
            lodData.AddField("mincullRate", lod.screenRelativeTransitionHeight);
            JSONObject renderDatas = new JSONObject(JSONObject.Type.ARRAY);
            Renderer[] renders = lod.renderers;
            for(var j = 0; j < renders.Length; j++)
            {
                renderDatas.Add(getRefNodeData(renders[j].gameObject));
            }
            lodData.AddField("renders", renderDatas);
            lodDatas.Add(lodData);
        }

        compData.AddField("lods", lodDatas);
        return compData;
    }
    private static JSONObject getReflectionProbe(GameObject gameObject)
    {
        ReflectionProbe probe = gameObject.GetComponent<ReflectionProbe>();
        Matrix4x4 matirx = gameObject.transform.worldToLocalMatrix;
        
        Vector4 helpVec = new Vector4(0, 0, 0, 1);
       
        JSONObject compData = new JSONObject(JSONObject.Type.OBJECT);
        compData.AddField("_$type", "ReflectionProbe");
        Vector3 min = probe.bounds.min;
        helpVec.Set(min.x, min.y, min.z, 1);
        helpVec = matirx * helpVec;
        min.Set(helpVec.x, helpVec.y, helpVec.z);
        compData.AddField("boundsMin", JsonUtils.GetVector3Object(min));
        Vector3 max = probe.bounds.max;
        helpVec.Set(max.x, max.y, max.z, 1);
        helpVec = matirx * helpVec;
        max.Set(helpVec.x, helpVec.y, helpVec.z);
        compData.AddField("boundsMax", JsonUtils.GetVector3Object(max));
        compData.AddField("importance", probe.importance);
        compData.AddField("cullingMask", 2147483647);
        compData.AddField("boxProjection", probe.boxProjection);
        compData.AddField("nearPlane", probe.nearClipPlane);
        compData.AddField("farPlane", probe.farClipPlane);
        compData.AddField("ambientColor", JsonUtils.GetColorObject(Color.black));
        compData.AddField("clearFlag", probe.clearFlags== ReflectionProbeClearFlags.Skybox?1:0);
        compData.AddField("resolution", probe.resolution);
        compData.AddField("_reflectionsIblSamples", 128);
        return compData;
    }

    private static JSONObject getAnimationComponentData(GameObject gameObject)
    {
        Animation animation = gameObject.GetComponent<Animation>();
        AnimationClip clip = animation.clip;
        string animatorControllerPath = clip.name + ".controller";
       
        JsonFile controlFile;
        if (!exportFiles.ContainsKey(animatorControllerPath))
        {
            controlFile = new JsonFile(animatorControllerPath, new JSONObject(JSONObject.Type.OBJECT));
            exportFiles.Add(controlFile.filePath, controlFile);
            JSONObject controllData = controlFile.jsonData;
            controllData.AddField("_$type", "Animator");
            controllData.AddField("enabled", true);
            controllData.AddField("controller", "null");
            controllData.AddField("cullingMode", 0);
            JSONObject controllerLayers = new JSONObject(JSONObject.Type.ARRAY);
            AnimatorController animatorController = new AnimatorController();
            animatorController.AddLayer("base layer");
            animatorController.AddMotion(clip, 0);
            AnimatorControllerLayer layer = animatorController.layers[0];
            controllerLayers.Add(getAnimaterLayerData(layer, gameObject, true,clip));
            controllData.AddField("controllerLayers", controllerLayers);
        }
        else
        {
            controlFile = exportFiles[animatorControllerPath] as JsonFile;
        }

        JSONObject compData = new JSONObject(JSONObject.Type.OBJECT);
        compData.AddField("_$type", "Animator");
        JSONObject controller = new JSONObject(JSONObject.Type.OBJECT);
        compData.AddField("controller", controller);
        controller.AddField("_$type", "AnimationController");
        controller.AddField("_$uuid", controlFile.uuid);

        return compData;
    }
    private static JSONObject getAnimatorComponentData(GameObject gameObject)
    {
        Animator animator = gameObject.GetComponent<Animator>();
        AnimatorController animatorController = (AnimatorController)animator.runtimeAnimatorController;

        if (animatorController == null)
        {
            Debug.LogWarning("LayaAir3D Warning(Code:1002) : " + gameObject.name + "'s Animator Compoment must have a Controller!");
            return null;
        }
        string path = AssetDatabase.GetAssetPath(animatorController.GetInstanceID());
        string animatorControllerPath = GameObjectUitls.cleanIllegalChar(path.Split('.')[0], false) + ".controller";
        JsonFile controlFile;
        if (!exportFiles.ContainsKey(animatorControllerPath))
        {
            controlFile = new JsonFile(animatorControllerPath,new JSONObject(JSONObject.Type.OBJECT));
            exportFiles.Add(controlFile.filePath, controlFile);
            JSONObject controllData = controlFile.jsonData;
            controllData.AddField("_$type", "Animator");
            controllData.AddField("enabled", true);
            controllData.AddField("controller", "null");
            controllData.AddField("cullingMode", 0);
            JSONObject controllerLayers = new JSONObject(JSONObject.Type.ARRAY);
            AnimatorControllerLayer[] layers = animatorController.layers;
            int layerLength = layers.Length;
            for(var i = 0; i < layerLength; i++)
            {
                controllerLayers.Add(getAnimaterLayerData(layers[i],gameObject,i==0));
            }
            controllData.AddField("controllerLayers", controllerLayers);
        }
        else
        {
            controlFile = exportFiles[animatorControllerPath] as JsonFile;
        }

        JSONObject compData = new JSONObject(JSONObject.Type.OBJECT);
        compData.AddField("_$type", "Animator");
        JSONObject controller = new JSONObject(JSONObject.Type.OBJECT);
        compData.AddField("controller", controller);
        controller.AddField("_$type", "AnimationController");
        controller.AddField("_$uuid", controlFile.uuid);

        return compData;
    }


    private static JSONObject getAnimaterLayerData(AnimatorControllerLayer layer, GameObject gameObject,bool isbaseLayer,AnimationClip clip=null)
    {
        JSONObject layarNode = new JSONObject(JSONObject.Type.OBJECT);
        layarNode.AddField("_$type", "AnimatorControllerLayer");
        layarNode.AddField("playOnWake", true);
        layarNode.AddField("name", layer.name);
        if (isbaseLayer)
        {
            layarNode.AddField("defaultWeight", 1);
        }
        else
        {
            layarNode.AddField("defaultWeight", layer.defaultWeight);
        }
       
        if (layer.blendingMode == AnimatorLayerBlendingMode.Override)
        {
            layarNode.AddField("blendingMode", 0);
        }
        else if (layer.blendingMode == AnimatorLayerBlendingMode.Additive)
        {
            layarNode.AddField("blendingMode", 1);
        }
        else
        {
            layarNode.AddField("blendingMode", 0);
        }
        AnimatorStateMachine stateMachine = layer.stateMachine;
        ChildAnimatorState[] states = stateMachine.states;
        JSONObject statuesNode = new JSONObject(JSONObject.Type.ARRAY);
        layarNode.AddField("states", statuesNode);
        Dictionary<string, int> stateMap = new Dictionary<string, int>();
        for (int i = 0; i < states.Length; i++)
        {
            stateMap.Add(states[i].state.name, i);
        }

        for (int i = 0; i < states.Length; i++)
        {
            Vector3 postion = states[i].position;
            AnimatorState state = states[i].state;

            JSONObject statueNode = new JSONObject(JSONObject.Type.OBJECT);
            statuesNode.Add(statueNode);
            statueNode.AddField("_$type", "AnimatorState");
            statueNode.AddField("name", state.name);
            statueNode.AddField("speed", state.speed);
            statueNode.AddField("clipStart", 0);
            statueNode.AddField("clipEnd", 1);
            statueNode.AddField("x", postion.x);
            statueNode.AddField("y", postion.y);
            BufferFile laniFile;
            if (state.motion == null)
            {
                laniFile = getAnimationClipBuffer(clip, gameObject);
            }
            else
            {
                laniFile = getAnimationClipBuffer(state.motion as AnimationClip, gameObject);
            }
           
            JSONObject clipData = new JSONObject(JSONObject.Type.OBJECT);
            clipData.AddField("_$type", "AnimationClip");
            clipData.AddField("_$uuid", laniFile.uuid);
            statueNode.AddField("clip", clipData);
            statueNode.AddField("id", stateMap[state.name].ToString());

            AnimatorStateTransition[] transitions = state.transitions;
            if (transitions.Length > 0)
            {
                JSONObject solotrans = new JSONObject(JSONObject.Type.ARRAY);
                statueNode.AddField("soloTransitions", solotrans);
                for (int j = 0; j < transitions.Length; j++)
                {
                    AnimatorStateTransition transition = transitions[j];
                    JSONObject solotran = new JSONObject(JSONObject.Type.OBJECT);
                    solotrans.Add(solotran);
                    solotran.AddField("id", stateMap[transition.destinationState.name].ToString());
                    solotran.AddField("exitByTime", transition.hasExitTime);
                    solotran.AddField("exitTime", transition.exitTime);
                    solotran.AddField("transduration", transition.duration);
                    solotran.AddField("transstartoffset", transition.offset);
                    if (transition.solo)
                    {
                        solotran.AddField("solo", true);
                    }
                    if (transition.mute)
                    {
                        solotran.AddField("mute", true);
                    }
                }
            }
            
        }

        Vector3 enterPostion = stateMachine.entryPosition;
        JSONObject enterNode = new JSONObject(JSONObject.Type.OBJECT);
        statuesNode.Add(enterNode);
        enterNode.AddField("id", "-1");
        enterNode.AddField("name", "进入");
        enterNode.AddField("speed", 1);
        enterNode.AddField("clipEnd", 1);
        enterNode.AddField("x", enterPostion.x);
        enterNode.AddField("y", enterPostion.y);
        JSONObject soloTransitions = new JSONObject(JSONObject.Type.ARRAY);
        if (stateMachine.entryTransitions.Length > 0)
        {
            for (int j = 0; j < stateMachine.entryTransitions.Length; j++)
            {
                JSONObject soloTransition = new JSONObject(JSONObject.Type.OBJECT);
                AnimatorTransition transition = stateMachine.entryTransitions[j];
                soloTransition.AddField("id", stateMap[transition.destinationState.name].ToString());
                soloTransitions.Add(soloTransition);
            }
        }
        else
        {
            JSONObject soloTransition = new JSONObject(JSONObject.Type.OBJECT);
            soloTransition.AddField("id", stateMap[stateMachine.defaultState.name].ToString());
            soloTransitions.Add(soloTransition);
        }
        enterNode.AddField("soloTransitions", soloTransitions);

        Vector3 anyPostion = stateMachine.anyStatePosition;
        JSONObject anyNode = new JSONObject(JSONObject.Type.OBJECT);
        statuesNode.Add(anyNode);
        anyNode.AddField("id", "-2");
        anyNode.AddField("name", "任何状态");
        anyNode.AddField("speed", 1);
        anyNode.AddField("clipEnd", 1);
        anyNode.AddField("x", anyPostion.x);
        anyNode.AddField("y", anyPostion.y);
       
        if (stateMachine.anyStateTransitions.Length > 0)
        {
            soloTransitions = new JSONObject(JSONObject.Type.ARRAY);
            
            for (int j = 0; j < stateMachine.anyStateTransitions.Length; j++)
            {
                JSONObject soloTransition = new JSONObject(JSONObject.Type.OBJECT);
                AnimatorStateTransition anytransition = stateMachine.anyStateTransitions[j];
                soloTransition.AddField("id", stateMap[anytransition.destinationState.name].ToString());
                soloTransition.AddField("exitByTime", anytransition.hasExitTime);
                soloTransition.AddField("exitTime", anytransition.exitTime);
                soloTransition.AddField("transduration", anytransition.duration);
                soloTransitions.Add(soloTransition);
            }
            anyNode.AddField("soloTransitions", soloTransitions);
        }
       

        return layarNode;
    }

    private static BufferFile getAnimationClipBuffer(AnimationClip aniclip, GameObject gameObject)
    {
        string clipName = GameObjectUitls.cleanIllegalChar(aniclip.name, true);
        string path = AssetDatabase.GetAssetPath(aniclip.GetInstanceID());
        string laniPath = GameObjectUitls.cleanIllegalChar(path.Split('.')[0], false) + "-" + clipName + ".lani";

        BufferFile laniFile;
        if (!exportFiles.ContainsKey(laniPath))
        {
            laniFile = new BufferFile(laniPath);
            GameObjectUitls.writeClip(aniclip, laniFile.filesteam, gameObject, clipName);
            exportFiles.Add(laniFile.filePath, laniFile);
        }
        else
        {
            laniFile = exportFiles[laniPath] as BufferFile;
        }

        return laniFile;

    }
    static void getSHOrigin(JSONObject shObj)
    {
        SphericalHarmonicsL2 sh = RenderSettings.ambientProbe;
        for (var i = 0; i < 3; i++)
        {
            shObj.Add(sh[i, 0]); shObj.Add(sh[i, 1]); shObj.Add(sh[i, 2]); shObj.Add(sh[i, 3]);
            shObj.Add(sh[i, 4]); shObj.Add(sh[i, 5]); shObj.Add(sh[i, 6]); shObj.Add(sh[i, 7]);
            shObj.Add(sh[i, 8]);
        }
    }
}
