using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using System;
using System.Linq;
using System.Reflection;



internal class AboutLayaAir : EditorWindow
{

    private static AboutLayaAir version;
    [MenuItem("LayaAir3D/Help/About LayaAir")]
    static void initTutorial()
    {
        version = (AboutLayaAir)EditorWindow.GetWindow(typeof(AboutLayaAir));
        Texture2D wtest = new Texture2D(16, 16);
        Util.FileUtil.FileStreamLoadTexture("Assets/LayaAir3D/LayaResouce/layabox.png",wtest);
        GUIContent titleContent = new GUIContent("LayaAir3D", wtest);
        version.titleContent = titleContent;

        LanguageConfig.ReadLanguage(1);

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
    public enum languages
    {
        English = 0,
        中文 = 1,
    }
    private static Setting setting;

    private static languages frontLanguage;
    private static languages currentLanguage;
    
    [MenuItem("LayaAir3D/Setting")]
    public static void initTutorial()
    {
        frontLanguage = languages.中文;
        currentLanguage = languages.中文;
        setting = (Setting)EditorWindow.GetWindow(typeof(Setting));
        Texture2D title = new Texture2D(16, 16);
        Util.FileUtil.FileStreamLoadTexture("Assets/LayaAir3D/LayaResouce/layabox.png", title);
        GUIContent titleContent = new GUIContent("LayaAir3D", title);
        setting.titleContent = titleContent;
    }
    private void OnGUI()
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label("", GUILayout.Width(15));
        currentLanguage = (languages)EditorGUILayout.EnumPopup("Language",currentLanguage);
        if (currentLanguage != frontLanguage)
        {
            frontLanguage = currentLanguage;
            if (LayaAir3D.layaWindow != null)
            {
                LanguageConfig.ReadLanguage((int)currentLanguage);
                LayaAir3D.layaWindow.Repaint();
            }
            else
            {
                LayaAir3D.initLayaExport();
                LanguageConfig.ReadLanguage((int)currentLanguage);
            }
        }
        GUILayout.EndHorizontal();
    }

    private void OnDestroy()
    {

    }
}
