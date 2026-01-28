using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

internal class AssetsUtil 
{
    public static string GetTextureFile(Texture texture)
    {
        return AssetDatabase.GetAssetPath(texture.GetInstanceID());
    }

    public static string GetAnimationClipPath(AnimationClip clip)
    {
        return AssetsUtil.GetFilePath(AssetDatabase.GetAssetPath(clip.GetInstanceID()), ".lani", clip.name);
    }
    public static string GetAnimatorControllerPath(AnimatorController animatorController)
    {
        return AssetsUtil.GetFilePath(AssetDatabase.GetAssetPath(animatorController.GetInstanceID()), ".controller", animatorController.name);
    }
    public static string GetMaterialPath(Material material)
    {
        string materialPath = AssetDatabase.GetAssetPath(material.GetInstanceID());
        if (materialPath.Length < 1)
        {
            return material.name + ".lmat";
        }else if (materialPath == "Resources/unity_builtin_extra")
        {
            return "Resources/" + material.name+ ".lmat";
        }
        else
        {
            return AssetsUtil.GetFilePath(materialPath, ".lmat");
        }
    }

    public static string GetMeshPath(Mesh mesh)
    {
        return AssetsUtil.GetFilePath(AssetDatabase.GetAssetPath(mesh.GetInstanceID()), ".lm", mesh.name); ;
    }
    private static string GetFilePath(string path, string exit, string fileName  = null)
    {
        if (string.IsNullOrEmpty(path))
        {
            return (fileName != null ? GameObjectUitls.cleanIllegalChar(fileName, true) : "default") + exit;
        }
        // 修复：安全地获取不带扩展名的路径
        int dotIndex = path.LastIndexOf('.');
        string basePath = dotIndex >= 0 ? path.Substring(0, dotIndex) : path;
        basePath = GameObjectUitls.cleanIllegalChar(basePath, false);
        if (fileName != null)
        {
            basePath += "-" + GameObjectUitls.cleanIllegalChar(fileName, true);
        }
        return basePath + exit;
    }

}
