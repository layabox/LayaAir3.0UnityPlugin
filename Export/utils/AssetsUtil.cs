using UnityEditor;
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
    public static string GetMaterialPath(Material material)
    {
        string materialPath = AssetDatabase.GetAssetPath(material.GetInstanceID());
        if (materialPath == "Resources/unity_builtin_extra")
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
        string basePath = GameObjectUitls.cleanIllegalChar(path.Split('.')[0], false);
        if (fileName != null)
        {
            basePath += "-" + GameObjectUitls.cleanIllegalChar(fileName,true);
        }
        return basePath + exit;
    }

}
