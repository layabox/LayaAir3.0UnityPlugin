using System;
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
            this.AddExportFile(new PerfabFile(this.AddNodeMap(), map.Key));
        }
    }

    public NodeMap AddNodeMap(int idOff = 0)
    {
        NodeMap nodemap = new NodeMap(this, idOff);
        this.nodemaps.Add(nodemap);
        return nodemap;
    }

    public void createNodeTree()
    {
        //创建未引用节点数结构
        foreach (NodeMap nodemap in this.nodemaps)
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

    public MeshFile GetMeshFile(Mesh mesh, Renderer renderer)
    {
        string path = AssetsUtil.GetMeshPath(mesh);
        if (!this.HaveFileData(path))
        {
            this.AddExportFile(new MeshFile(mesh, renderer));
        }
        return this.GetFileData(path) as MeshFile;
    }

    public MaterialFile GetMaterialFile(Material material)
    {
        string path = AssetsUtil.GetMaterialPath(material);
        if (!this.HaveFileData(path))
        {
            this.AddExportFile(new MaterialFile(this, material));
        }
        return this.GetFileData(path) as MaterialFile;
    }

    public FileData GetTextureFile(Texture texture, bool isNormal)
    {
        string picturePath = AssetsUtil.GetTextureFile(texture);

        if (!this.HaveFileData(picturePath))
        {
            this.AddExportFile(new TextureFile(picturePath, texture as Texture2D, isNormal));
        }
        return this.GetFileData(picturePath);
    }

    public FileData GetTexture2DFile(Texture2D texture, string picturePath)
    {

        if (!this.HaveFileData(picturePath))
        {
            this.AddExportFile(new TextureFile(picturePath, texture, false));
        }
        return this.GetFileData(picturePath) as TextureFile;
    }


    public AnimationClipFile GetAnimationClipFile(AnimationClip aniclip, GameObject gameObject)
    {
        string laniPath = AssetsUtil.GetAnimationClipPath(aniclip);

        if (!this.HaveFileData(laniPath))
        {
            this.AddExportFile(new AnimationClipFile(aniclip, gameObject));
        }


        return this.GetFileData(laniPath) as AnimationClipFile;

    }
    public void AddExportFile(FileData file)
    {
        file.resoureMap = this;
        exportFiles.Add(file.filePath, file);
    }

    public FileData GetFileData(string path)
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


    public bool HaveFileData(string path)
    {
        return exportFiles.ContainsKey(path);
    }

    public void SaveAllFile()
    {
        foreach (var file in exportFiles)
        {
            file.Value.SaveFile(exportFiles);
        }
    }

    public void getComponentsData(GameObject gameObject, JSONObject node, NodeMap map)
    {
        Camera camera = gameObject.GetComponent<Camera>();
        JSONObject getType = node.GetField("_$type");
        if (camera != null)
        {
            JsonUtils.getCameraComponentData(gameObject, node);
            if (null == getType)
                node.AddField("_$type", "Camera");
            else
                node.SetField("_$type", "Camera");
        }
        else
        {
            if (null == getType)
                node.AddField("_$type", "Sprite3D");
            else
                node.SetField("_$type", "Sprite3D");
        }

        JSONObject compents = new JSONObject(JSONObject.Type.ARRAY);
        node.AddField("_$comp", compents);
        List<Component> componentsList = new List<Component>();
        gameObject.GetComponents(componentsList);
        foreach (Component comp in componentsList)
        {
            this.writeComponentData(compents, comp, map, false);
        }

    }

    public void writeComponentData(JSONObject compents, Component comp, NodeMap map, bool isOverride)
    {
        if (comp == null)
        {
            return;
        }
        GameObject gameObject = comp.gameObject;
        if (comp is MeshRenderer)
        {
            compents.Add(this.GetMeshRenderComponmentData(comp as MeshRenderer, isOverride));
        }
        else if (comp is MeshFilter)
        {
            MeshFilter filter = comp as MeshFilter;
            compents.Add(this.GetMeshFilterComponentData(filter.sharedMesh, gameObject.GetComponent<MeshRenderer>(), isOverride));
        }
        else if (comp is SkinnedMeshRenderer)
        {
            SkinnedMeshRenderer render = comp as SkinnedMeshRenderer;
            compents.Add(this.GetMeshFilterComponentData(render.sharedMesh, render, isOverride));
            compents.Add(this.GetSkinnerMeshRenderComponmentData(render, map, isOverride));
        }
        else if (comp is Light)
        {
            compents.Add(this.GetLightComponentData(comp as Light, isOverride));
        }
        else if (comp is Animator)
        {
            compents.Add(this.GetAnimatorComponentData(comp as Animator, isOverride));
        }
        else if (comp is ReflectionProbe)
        {
            compents.Add(this.GetReflectionProbe(comp as ReflectionProbe, isOverride));
        }
        else if (comp is LODGroup)
        {
            compents.Add(this.GetLodGroup(comp as LODGroup, map, isOverride));
        }
        else if (comp is ParticleSystem)
        {
            compents.Add(ParticleSystemData.GetParticleSystem(comp as ParticleSystem, isOverride, map, this));
        }
        // else if(comp is ParticleSystemRenderer)
        // {
        //     compents.Add(ParticleSystemData.GetParticleSystemRenderer(comp as ParticleSystemRenderer, isOverride,this));
        // }
    }

    public JSONObject GetLightComponentData(Light light, bool isOverride)
    {
        if (light.type == LightType.Directional)
        {
            return JsonUtils.GetDirectionalLightComponentData(light, isOverride);
        }
        else if (light.type == LightType.Point)
        {
            return JsonUtils.GetPointLightComponentData(light, isOverride);
        }
        else if (light.type == LightType.Spot)
        {
            return JsonUtils.GetSpotLightComponentData(light, isOverride);
        }
        else
        {
            return null;
        }
    }
    public JSONObject GetMeshFilterComponentData(Mesh mesh, Renderer render, bool isOverride)
    {
        JSONObject compData = new JSONObject(JSONObject.Type.OBJECT);
        if (isOverride)
        {
            compData.AddField("_$override", "MeshFilter");
        }
        else
        {
            compData.AddField("_$type", "MeshFilter");
        }

        if (mesh != null)
        {
            compData.AddField("sharedMesh", this.GetMeshData(mesh, render));
        }

        return compData;

    }
    public JSONObject GetSkinnerMeshRenderComponmentData(SkinnedMeshRenderer skinnedMeshRenderer, NodeMap map, bool isOverride)
    {
        Material[] materials = skinnedMeshRenderer.sharedMaterials;
        JSONObject sharedMaterials = new JSONObject(JSONObject.Type.ARRAY);
        GameObject gameObject = skinnedMeshRenderer.gameObject;
        for (var i = 0; i < materials.Length; i++)
        {
            sharedMaterials.Add(this.GetMaterialData(materials[i]));
        }

        JSONObject compData = new JSONObject(JSONObject.Type.OBJECT);
        compData.AddField("_$type", "SkinnedMeshRenderer");
        compData.AddField("sharedMaterials", sharedMaterials);
        compData.AddField("enabled", skinnedMeshRenderer.enabled);
        compData.AddField("receiveShadows", skinnedMeshRenderer.receiveShadows);
        compData.AddField("castShadow", skinnedMeshRenderer.shadowCastingMode == ShadowCastingMode.On);

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

        if (skinnedMeshRenderer.rootBone)
        {
            compData.AddField("rootBone", map.getRefNodeIdObjet(skinnedMeshRenderer.rootBone.gameObject));
        }
        return compData;
    }


    public JSONObject GetMeshRenderComponmentData(MeshRenderer render, bool isOverride)
    {
        GameObject gameObject = render.gameObject;

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
                sharedMaterials.Add(this.GetMaterialData(mat));
            }
        }

        JSONObject compData = JsonUtils.SetComponentsType(new JSONObject(JSONObject.Type.OBJECT), "MeshRenderer", isOverride);
        compData.AddField("enabled", render.enabled);
        compData.AddField("sharedMaterials", sharedMaterials);
        compData.AddField("receiveShadows", render.receiveShadows);
        compData.AddField("castShadow", render.shadowCastingMode == ShadowCastingMode.On);
        return compData;
    }


    public JSONObject GetLodGroup(LODGroup lodGroup, NodeMap map, bool isOverride)
    {
        JSONObject compData = JsonUtils.SetComponentsType(new JSONObject(JSONObject.Type.OBJECT), "LODGroup", isOverride);
        compData.AddField("enabled", lodGroup.enabled);
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

    public JSONObject GetReflectionProbe(ReflectionProbe probe, bool isOverride)
    {
        GameObject gameObject = probe.gameObject;
        Matrix4x4 matirx = gameObject.transform.worldToLocalMatrix;

        Vector4 helpVec = new Vector4(0, 0, 0, 1);

        JSONObject compData = JsonUtils.SetComponentsType(new JSONObject(JSONObject.Type.OBJECT), "ReflectionProbe", isOverride);
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
        compData.AddField("enabled", probe.enabled);
        return compData;
    }
    public JSONObject GetAnimatorComponentData(Animator animator, bool isOverride)
    {
        GameObject gameObject = animator.gameObject;
        AnimatorController animatorController = (AnimatorController)animator.runtimeAnimatorController;

        if (animatorController == null)
        {
            Debug.LogWarning("LayaAir3D Warning(Code:1002) : " + gameObject.name + "'s Animator Compoment must have a Controller!");
            return null;
        }
        string animatorControllerPath = AssetsUtil.GetAnimatorControllerPath(animatorController);
        JsonFile controlFile;
        if (!this.HaveFileData(animatorControllerPath))
        {
            controlFile = new JsonFile(animatorControllerPath, new JSONObject(JSONObject.Type.OBJECT));
            JSONObject controllData = controlFile.jsonData;
            JsonUtils.SetComponentsType(controllData, "Animator", isOverride);
            controllData.AddField("enabled", true);
            controllData.AddField("controller", "null");
            controllData.AddField("cullingMode", 0);
            JSONObject controllerLayers = new JSONObject(JSONObject.Type.ARRAY);
            AnimatorControllerLayer[] layers = animatorController.layers;
            int layerLength = layers.Length;
            for (var i = 0; i < layerLength; i++)
            {
                controllerLayers.Add(GetAnimaterLayerData(layers[i], gameObject, i == 0));
            }
            controllData.AddField("controllerLayers", controllerLayers);
            this.AddExportFile(controlFile);
        }
        else
        {
            controlFile = this.GetFileData(animatorControllerPath) as JsonFile;
        }

        JSONObject compData = new JSONObject(JSONObject.Type.OBJECT);
        compData.AddField("_$type", "Animator");
        JSONObject controller = new JSONObject(JSONObject.Type.OBJECT);
        compData.AddField("controller", controller);
        compData.AddField("enabled", animator.enabled);
        controller.AddField("_$type", "AnimationController");
        controller.AddField("_$uuid", controlFile.uuid);

        return compData;
    }


    private JSONObject GetAnimaterLayerData(AnimatorControllerLayer layer, GameObject gameObject, bool isbaseLayer)
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

            AnimationClip clip = state.motion as AnimationClip;
            if (clip != null)
            {
                JSONObject clipData = new JSONObject(JSONObject.Type.OBJECT);
                clipData.AddField("_$type", "AnimationClip");
                AnimationClipFile laniFile = GetAnimationClipFile(clip, gameObject);
                clipData.AddField("_$uuid", laniFile.uuid);
                statueNode.AddField("clip", clipData);
            }
            else
            {
                Debug.LogError(gameObject.name + " have empty or not  AnimationClip " + state.name);
            }
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
            if (stateMachine.defaultState != null)
            {
                JSONObject soloTransition = new JSONObject(JSONObject.Type.OBJECT);
                soloTransition.AddField("id", stateMap[stateMachine.defaultState.name].ToString());
                soloTransitions.Add(soloTransition);
            }

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


    public void GetSHOrigin(JSONObject shObj)
    {
        SphericalHarmonicsL2 sh = RenderSettings.ambientProbe;
        for (var i = 0; i < 3; i++)
        {
            shObj.Add(sh[i, 0]); shObj.Add(sh[i, 1]); shObj.Add(sh[i, 2]); shObj.Add(sh[i, 3]);
            shObj.Add(sh[i, 4]); shObj.Add(sh[i, 5]); shObj.Add(sh[i, 6]); shObj.Add(sh[i, 7]);
            shObj.Add(sh[i, 8]);
        }
    }

    public JSONObject GetMaterialData(Material material)
    {
        JSONObject materFiledata = new JSONObject(JSONObject.Type.OBJECT);
        materFiledata.AddField("_$type", "Material");
        if (material != null)
        {
            MaterialFile jsonFile = this.GetMaterialFile(material);
            materFiledata.AddField("_$uuid", jsonFile.uuid);
        }
        return materFiledata;
    }

    public JSONObject GetMeshData(Mesh mesh, Renderer render)
    {
        MeshFile meshFile = this.GetMeshFile(mesh, render);
        JSONObject meshFiledata = new JSONObject(JSONObject.Type.OBJECT);
        meshFiledata.AddField("_$uuid", meshFile.uuid);
        meshFiledata.AddField("_$type", "Mesh");
        return meshFiledata;
    }
}
