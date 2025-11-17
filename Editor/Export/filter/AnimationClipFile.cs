using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;

internal class AnimationClipFile : FileData
{
    private AnimationClip m_clip;
    private GameObject m_root;
    // store original root name for diagnostics (safe to read even after GameObject is destroyed)
    private string m_rootName;
    // additional diagnostics to help recover lost references
    private int m_rootInstanceId = 0;
    private string m_rootHierarchyPath = null;
    private string m_rootPrefabAssetPath = null;
    public AnimationClipFile(AnimationClip clip, GameObject root) : base(null)
    {
        this.m_clip = clip;
        this.m_root = root;
        this.m_rootName = (this.m_root != null) ? this.m_root.name : "<null>";
        if (this.m_root != null)
        {
            this.m_rootInstanceId = this.m_root.GetInstanceID();
            this.m_rootHierarchyPath = GetHierarchyPath(this.m_root);
            // try to capture prefab asset path if this is an instance
            this.m_rootPrefabAssetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(this.m_root);
        }

        //Debug.Log("AnimationClipFile: " + this.m_clip.name + " root: " + this.m_rootName + " path: " + this.m_rootHierarchyPath + " prefab: " + this.m_rootPrefabAssetPath);

        if (this.m_root == null)
        {
            Debug.LogWarning("1.AnimationClipFile: " + this.m_clip.name + " root is null");
            return;
        }
        this.updatePath(AssetsUtil.GetAnimationClipPath(clip));
    }

    // build a hierarchy path like Root/Child/SubChild to uniquely identify the transform in a scene
    private static string GetHierarchyPath(GameObject go)
    {
        if (go == null) return null;
        var parts = new System.Collections.Generic.List<string>();
        Transform t = go.transform;
        while (t != null)
        {
            parts.Add(t.name);
            t = t.parent;
        }
        parts.Reverse();
        return string.Join("/", parts.ToArray());
    }

    // Attempt to recover the m_root reference using saved diagnostics.
    // Returns true if recovered.
    private bool TryRecoverRoot()
    {
        if (this.m_root != null) return true;

        // 1) Try to find by hierarchy path among all loaded scene objects
        if (!string.IsNullOrEmpty(this.m_rootHierarchyPath))
        {
            var all = Resources.FindObjectsOfTypeAll<GameObject>();
            foreach (var go in all)
            {
                // skip objects that are assets (prefab assets) if we want scene objects first
                if (string.IsNullOrEmpty(go.scene.name)) continue;
                if (GetHierarchyPath(go) == this.m_rootHierarchyPath)
                {
                    this.m_root = go;
//                    Debug.Log("Recovered m_root by hierarchy path: " + this.m_rootHierarchyPath + " -> " + go.name);
                    return true;
                }
            }
        }

        // 2) Try to find by prefab asset path if known
        if (!string.IsNullOrEmpty(this.m_rootPrefabAssetPath))
        {
            var all = Resources.FindObjectsOfTypeAll<GameObject>();
            foreach (var go in all)
            {
                var prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
                if (!string.IsNullOrEmpty(prefabPath) && prefabPath == this.m_rootPrefabAssetPath)
                {
                    this.m_root = go;
                    Debug.Log("Recovered m_root by prefab asset path: " + prefabPath + " -> " + go.name);
                    return true;
                }
            }
        }

        // 3) Fallback: try to find by name (may be ambiguous)
        if (!string.IsNullOrEmpty(this.m_rootName) && this.m_rootName != "<null>")
        {
            var all = Resources.FindObjectsOfTypeAll<GameObject>();
            GameObject found = null;
            int count = 0;
            foreach (var go in all)
            {
                if (go.name == this.m_rootName)
                {
                    found = go;
                    count++;
                }
            }
            if (count == 1 && found != null)
            {
                this.m_root = found;
                Debug.Log("Recovered m_root by unique name: " + this.m_rootName + " -> " + found.name);
                return true;
            }
            else if (count > 1)
            {
                Debug.LogWarning("Multiple GameObjects found with name '" + this.m_rootName + "', can't unambiguously recover m_root. Candidates: " + count);
            }
        }

        Debug.LogWarning("TryRecoverRoot failed for clip: " + this.m_clip.name + ", originalRootName: " + this.m_rootName + ", hierarchyPath: " + this.m_rootHierarchyPath + ", prefabPath: " + this.m_rootPrefabAssetPath);
        return false;
    }


    public override void SaveFile(Dictionary<string, FileData> exportFiles)
    {
        base.saveMeta();
        FileStream fs = Util.FileUtil.saveFile(this.outPath);
        string clipName = GameObjectUitls.cleanIllegalChar(this.m_clip.name, true);
        if (null == this.m_root)
        {
            // attempt to recover the root reference using saved diagnostics
            if (!TryRecoverRoot())
            {
                // Provide original root name to help diagnose when the GameObject was destroyed or lost
                Debug.LogWarning("2.AnimationClipFile: " + this.m_clip.name + " root is null. originalRootName: " + this.m_rootName);
                return;
            }
        }
        GameObjectUitls.writeClip(this.m_clip, fs, this.m_root, clipName);
    }
}
