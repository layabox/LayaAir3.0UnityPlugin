using UnityEditor;
using UnityEngine;
using static LanguageConfig;

internal class AboutLayaAir : EditorWindow
{

    private static AboutLayaAir version;
    [MenuItem("LayaAir3D/Help/About LayaAir")]
    static void initTutorial()
    {
        version = (AboutLayaAir)EditorWindow.GetWindow(typeof(AboutLayaAir));
        Texture2D wtest = new Texture2D(16, 16);
        Util.FileUtil.FileStreamLoadTexture(Util.FileUtil.getPluginResUrl("LayaResouce/layabox.png"), wtest);
        GUIContent titleContent = new GUIContent("LayaAir3D", wtest);
        version.titleContent = titleContent;

    }
    private void OnGUI()
    {
        GUI.Label(new Rect(position.width / 2 - 70, position.height / 2 - 20, 200, 30), LanguageConfig.str_LayaBoxVersion);
        if (GUI.Button(new Rect(position.width / 2 - 70, position.height / 2 + 10, 100, 30), LanguageConfig.str_AboutLayaAir))
        {
            Application.OpenURL("https://www.layabox.com");
        }

      
    }
}



public class Setting : EditorWindow
{
    private static Setting setting;

    
    [MenuItem("LayaAir3D/Setting")]
    public static void initTutorial()
    {
        setting = (Setting)EditorWindow.GetWindow(typeof(Setting));
        Texture2D title = new Texture2D(16, 16);
        Util.FileUtil.FileStreamLoadTexture(Util.FileUtil.getPluginResUrl("LayaResouce/layabox.png"), title);
        GUIContent titleContent = new GUIContent("LayaAir3D", title);
        setting.titleContent = titleContent;
    }
    private void OnGUI()
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label("", GUILayout.Width(15));
        languages currentLanguage = (languages)EditorGUILayout.EnumPopup("Language",LanguageConfig.GetLanguages());
        if (LanguageConfig.setLanguages(currentLanguage))
        {
            if (LayaAir3D.layaWindow != null)
            {
                LayaAir3D.layaWindow.Repaint();
            }
           
        }
        GUILayout.EndHorizontal();
    }

    private void OnDestroy()
    {

    }
}
