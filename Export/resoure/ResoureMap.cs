using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

internal class ResoureMap 
{
    private Dictionary<string, FileData> exportFiles;
    private List<NodeMap> nodemaps;
    public ResoureMap(Dictionary<string, GameObject> maps)
    {
        this.exportFiles = new Dictionary<string, FileData>();
        this.nodemaps = new List<NodeMap>();
        foreach (var map in maps)
        {
            this.AddExportFile(new PerfabFile(this.addNodeMap(), map.Key));
        }
    }

    public NodeMap addNodeMap(int idOff = 0)
    {
        NodeMap nodemap = new NodeMap(this,idOff);
        this.nodemaps.Add(nodemap);
        return nodemap;
    }

    public void createNodeTree()
    {
        //创建未引用节点数结构
        foreach(NodeMap nodemap in this.nodemaps)
        {
            nodemap.createNodeTree();
        }
        //创建引用节点,同时生成节点信息
        foreach (NodeMap nodemap in this.nodemaps)
        {
            nodemap.createRefNodeTree();
            nodemap.writeCompoent();
        }
    }

    public PerfabFile GetPerfabByObject(GameObject gameObject)
    {
        string path = PerfabFile.getPerfabFilePath(gameObject);
        if (this.exportFiles.ContainsKey(path))
        {
            return this.exportFiles[path] as PerfabFile;
        }
        else
        {
            Debug.Log("not get the parfab path: " + path);
            return null;
        }
    }

    public MeshFile GetMeshFile(Mesh mesh,Renderer renderer)
    {
        string path = AssetsUtil.GetMeshPath(mesh);
        if (!this.haveFileData(path))
        {
            this.AddExportFile(new MeshFile(mesh, renderer));
        }
        return this.getFileData(path) as MeshFile;
    }

    public MaterialFile GetMaterialFile(Material material)
    {
        string path = AssetsUtil.GetMaterialPath(material);
        if (!this.haveFileData(path))
        {
            this.AddExportFile(new MaterialFile(this,material));
        }
        return this.getFileData(path) as MaterialFile;
    }

    public TextureFile GetTextureFile(Texture texture, bool isNormal)
    {
        string picturePath = AssetsUtil.GetTextureFile(texture);
        
        if (!this.haveFileData(picturePath))
        {
            this.AddExportFile(new TextureFile(picturePath,texture as Texture2D, isNormal));
        }
        return this.getFileData(picturePath) as TextureFile;
    }

    private AnimationClipFile GetAnimationClipFile(AnimationClip aniclip, GameObject gameObject)
    {
        string laniPath = AssetsUtil.GetAnimationClipPath(aniclip);

        if (!exportFiles.ContainsKey(laniPath))
        {
            this.AddExportFile(new AnimationClipFile(aniclip, gameObject));
        }


        return this.getFileData(laniPath) as AnimationClipFile;

    }
    public void AddExportFile(FileData file)
    {
        file.resoureMap = this;
        exportFiles.Add(file.filePath, file);
    }

    public FileData getFileData(string path)
    {
        if (!exportFiles.ContainsKey(path))
        {
            return null;
        }
        else
        {
            return exportFiles[path];
        }
    }


    public bool haveFileData(string path)
    {
        return exportFiles.ContainsKey(path);
    }

    public void saveAllFile()
    {
        foreach (var file in exportFiles)
        {
            file.Value.SaveFile(exportFiles);
        }
    }

    public void getComponentsData(GameObject gameObject, JSONObject node,NodeMap map)
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
            component.Add(this.getMeshFilterComponentData(gameObject));
        }
        if (components.IndexOf(ComponentType.MeshRenderer) != -1)
        {
            component.Add(getMeshRenderComponmentData(gameObject));
        }
        if (components.IndexOf(ComponentType.SkinnedMeshRenderer) != -1)
        {
            this.getSkinnerMeshRenderComponmentData(gameObject, component, map);
        }

        if (components.IndexOf(ComponentType.Animator) != -1)
        {
            component.Add(getAnimatorComponentData(gameObject));
        }

        if (components.IndexOf(ComponentType.ReflectionProbe) != -1)
        {
            component.Add(getReflectionProbe(gameObject));
        }

        if (components.IndexOf(ComponentType.LodGroup) != -1)
        {
            component.Add(getLodGroup(gameObject,map));
        }

    }
    private JSONObject getMeshFilterComponentData(GameObject gameObject)
    {
        Mesh mesh = gameObject.GetComponent<MeshFilter>().sharedMesh;
        JSONObject compData = new JSONObject(JSONObject.Type.OBJECT);
        compData.AddField("_$type", "MeshFilter");

        if (mesh == null)
        {
            Debug.LogWarning("LayaAir3D Warning(Code:1001) : " + gameObject.name + "'s MeshFilter Component Mesh data can't be null!");
        }
        else
        {
            MeshFile meshFile = this.GetMeshFile(mesh,gameObject.GetComponent<Renderer>());
            JSONObject meshFiledata = new JSONObject(JSONObject.Type.OBJECT);
            meshFiledata.AddField("_$uuid", meshFile.uuid);
            meshFiledata.AddField("_$type", "mesh");
            compData.AddField("sharedMesh", meshFiledata);
        }

        return compData;

    }
    private void getSkinnerMeshRenderComponmentData(GameObject gameObject,JSONObject components, NodeMap map)
    {
        SkinnedMeshRenderer skinnedMeshRenderer = gameObject.GetComponent<SkinnedMeshRenderer>();
        Mesh mesh = skinnedMeshRenderer.sharedMesh;
        if (mesh != null)
        {
            MeshFile meshFile = this.GetMeshFile(mesh, skinnedMeshRenderer);
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
                sharedMaterials.Add(getMaterialData(mat));
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
            bones.Add(map.getRefNodeIdObjet(bonesTransform[i].gameObject));
        }

        //rootBone
        if (skinnedMeshRenderer.rootBone)
        {
            compData.AddField("rootBone", map.getRefNodeIdObjet(skinnedMeshRenderer.rootBone.gameObject));
        }

        components.Add(compData);
    }


    private JSONObject getMeshRenderComponmentData(GameObject gameObject)
    {
        MeshRenderer render = gameObject.GetComponent<MeshRenderer>();
        Material[] materials = render.sharedMaterials;
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
                sharedMaterials.Add(getMaterialData(mat));
            }
        }

        JSONObject compData = new JSONObject(JSONObject.Type.OBJECT);
        compData.AddField("_$type", "MeshRenderer");
        compData.AddField("sharedMaterials", sharedMaterials);
        return compData;
    }


    private JSONObject getLodGroup(GameObject gameObject, NodeMap map)
    {
        LODGroup lodGroup = gameObject.GetComponent<LODGroup>();

        JSONObject compData = new JSONObject(JSONObject.Type.OBJECT);
        compData.AddField("_$type", "LODGroup");
        JSONObject lodDatas = new JSONObject(JSONObject.Type.ARRAY);
        LOD[] lods = lodGroup.GetLODs();
        for (var i = 0; i < lods.Length; i++)
        {
            LOD lod = lods[i];
            JSONObject lodData = new JSONObject(JSONObject.Type.OBJECT);
            lodData.AddField("_$type", "LODInfo");
            lodData.AddField("mincullRate", lod.screenRelativeTransitionHeight);
            JSONObject renderDatas = new JSONObject(JSONObject.Type.ARRAY);
            Renderer[] renders = lod.renderers;
            for (var j = 0; j < renders.Length; j++)
            {
                if (renders[j])
                    renderDatas.Add(map.getRefNodeIdObjet(renders[j].gameObject));
            }
            lodData.AddField("renders", renderDatas);
            lodDatas.Add(lodData);
        }

        compData.AddField("lods", lodDatas);
        return compData;
    }
    private JSONObject getReflectionProbe(GameObject gameObject)
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
        compData.AddField("clearFlag", probe.clearFlags == ReflectionProbeClearFlags.Skybox ? 1 : 0);
        compData.AddField("resolution", probe.resolution);
        compData.AddField("_reflectionsIblSamples", 128);
        return compData;
    }
    private JSONObject getAnimatorComponentData(GameObject gameObject)
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
            controlFile = new JsonFile(animatorControllerPath, new JSONObject(JSONObject.Type.OBJECT));
            exportFiles.Add(controlFile.filePath, controlFile);
            JSONObject controllData = controlFile.jsonData;
            controllData.AddField("_$type", "Animator");
            controllData.AddField("enabled", true);
            controllData.AddField("controller", "null");
            controllData.AddField("cullingMode", 0);
            JSONObject controllerLayers = new JSONObject(JSONObject.Type.ARRAY);
            AnimatorControllerLayer[] layers = animatorController.layers;
            int layerLength = layers.Length;
            for (var i = 0; i < layerLength; i++)
            {
                controllerLayers.Add(getAnimaterLayerData(layers[i], gameObject, i == 0));
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


    private JSONObject getAnimaterLayerData(AnimatorControllerLayer layer, GameObject gameObject, bool isbaseLayer)
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

            AnimationClipFile laniFile = GetAnimationClipFile(state.motion as AnimationClip, gameObject);
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

    
    public void getSHOrigin(JSONObject shObj)
    {
        SphericalHarmonicsL2 sh = RenderSettings.ambientProbe;
        for (var i = 0; i < 3; i++)
        {
            shObj.Add(sh[i, 0]); shObj.Add(sh[i, 1]); shObj.Add(sh[i, 2]); shObj.Add(sh[i, 3]);
            shObj.Add(sh[i, 4]); shObj.Add(sh[i, 5]); shObj.Add(sh[i, 6]); shObj.Add(sh[i, 7]);
            shObj.Add(sh[i, 8]);
        }
    }

    public JSONObject getMaterialData(Material material)
    {
        MaterialFile jsonFile = this.GetMaterialFile(material);
        if(jsonFile == null)
        {
            Debug.Log(material);
        }
        JSONObject materFiledata = new JSONObject(JSONObject.Type.OBJECT);
        materFiledata.AddField("_$uuid", jsonFile.uuid);
        materFiledata.AddField("_$type", "Material");
        return materFiledata;
    }
}
