using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public class LayaAir3Export
{
   
    public static void ExportScene()
    {
        GameObjectUitls.init();
        MetarialUitls.init();
        AnimationCurveGroup.init();

        var active = EditorSceneManager.GetActiveScene();
        var sceneCount = EditorSceneManager.sceneCount;
        for (int i = 0; i < sceneCount; i++)
        {
            Scene scene = EditorSceneManager.GetSceneAt(i);
            EditorSceneManager.OpenScene(scene.path, OpenSceneMode.Additive);
            HierarchyFile hierachy = new HierarchyFile(scene);
            hierachy.saveAllFile(ExportConfig.FirstlevelMenu == 0);
        }
        if (sceneCount > 1) {
            EditorSceneManager.OpenScene(active.path, OpenSceneMode.Additive);
        }

        SceneView.lastActiveSceneView.ShowNotification(new GUIContent(LanguageConfig.str_Exported));
        Debug.Log(LanguageConfig.str_Exported);
    }
}
