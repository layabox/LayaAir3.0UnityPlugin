using System;
using System.Collections;
using UnityEngine;
using UnityEditor;
using UnityEngine.Networking;
using marijnz.EditorCoroutines;

public class LayaAir3D : EditorWindow
{
    private static Vector2 ScrollPosition;

    private static bool GameObjectSetting;
    public static bool MeshSetting;
    private static bool OtherSetting;

    public static bool Scenes;
    public static int sceneIndex;

    private static GUIStyle g = new GUIStyle();

    private static bool PassNull = false;

    public static LayaAir3D layaWindow;

    private static Texture2D exporttu;
   
    

    [MenuItem("LayaAir3D 3.0/Export Tool", false, 1)]
    public static void initLayaExport()
    {
        LanguageConfig.configLanguage();
        layaWindow = (LayaAir3D)EditorWindow.GetWindow(typeof(LayaAir3D));
        exporttu = new Texture2D(52, 52);
        Util.FileUtil.FileStreamLoadTexture(Util.FileUtil.getPluginResUrl("LayaResouce/Export.png"), exporttu);
    }

    [MenuItem("LayaAir3D 3.0/Help/Study")]
    static void initLayaStudy()
    {
        ServeConfig.getInstance().openurl(URLType.StudyURL);
    }

    [MenuItem("LayaAir3D 3.0/Help/Answsers")]
    static void initLayaAsk()
    {
        ServeConfig.getInstance().openurl(URLType.LayaAskURL);
    }
    void OnGUI()
    {
        ExportConfig.initConfig();
        LanguageConfig.configLanguage();

        GUILayout.Space(10);

        
        GUILayout.Space(15);
        GUILayout.BeginHorizontal();
        GUILayout.Space(24);
        ExportConfig.FirstlevelMenu = GUILayout.Toolbar(ExportConfig.FirstlevelMenu, new string[] { LanguageConfig.str_Scene, LanguageConfig.str_Sprite3D }, GUILayout.Height(30), GUILayout.Width(position.width - 48));
        GUILayout.EndHorizontal();
        ScrollPosition = GUILayout.BeginScrollView(ScrollPosition);

        GUILayout.Space(25);
        //---------------------------------------GameObjectSetting------------------------------------------
        GUILayout.BeginHorizontal();
        GUILayout.Space(21);
        GameObjectSetting = EditorGUILayout.Foldout(GameObjectSetting, LanguageConfig.str_GameObjectSetting, true);
        GUILayout.EndHorizontal();
        if (GameObjectSetting)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Space(21);
            GUILayout.Label("", GUILayout.Width(15));
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));

            ExportConfig.IgnoreNotActiveGameObject = GUILayout.Toggle(ExportConfig.IgnoreNotActiveGameObject, LanguageConfig.str_IgnoreNotActiveGameObjects);

            if (ExportConfig.FirstlevelMenu == 1)
            {
                ExportConfig.BatchMade = GUILayout.Toggle(ExportConfig.BatchMade, LanguageConfig.str_BatchMakeTheFirstLevelGameObjects);
            }
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

        }

        //??
        GUILayout.BeginHorizontal();
        GUILayout.Space(25);
        GUILayout.Box("", GUILayout.Height(1), GUILayout.Width(position.width - 50));
        GUILayout.EndHorizontal();

        //---------------------------------------GameObjectSetting------------------------------------------
        GUILayout.Space(10);
        //---------------------------------------MeshSetting------------------------------------------
        GUILayout.BeginHorizontal();
        GUILayout.Space(21);
        MeshSetting = EditorGUILayout.Foldout(MeshSetting, LanguageConfig.str_MeshSetting, true);
        GUILayout.EndHorizontal();
        if (MeshSetting)
        {
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));

            GUILayout.BeginHorizontal();
            GUILayout.Space(21);
            GUILayout.Label("", g, GUILayout.Width(15));

            ExportConfig.IgnoreVerticesUV = GUILayout.Toggle(ExportConfig.IgnoreVerticesUV, LanguageConfig.str_IgnoreVerticesUV);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Space(21);
            GUILayout.Label("", g, GUILayout.Width(15));

            ExportConfig.IgnoreVerticesColor = GUILayout.Toggle(ExportConfig.IgnoreVerticesColor, LanguageConfig.str_IgnoreVerticesColor);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Space(21);
            GUILayout.Label("", g, GUILayout.Width(15));

            ExportConfig.IgnoreVerticesNormal = GUILayout.Toggle(ExportConfig.IgnoreVerticesNormal, LanguageConfig.str_IgnoreVerticesNormal);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Space(21);
            GUILayout.Label("", g, GUILayout.Width(15));

            ExportConfig.IgnoreVerticesTangent = GUILayout.Toggle(ExportConfig.IgnoreVerticesTangent, LanguageConfig.str_IgnoreVerticesTangent);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Space(21);
            GUILayout.Label("", g, GUILayout.Width(15));

            ExportConfig.AutoVerticesUV1 = GUILayout.Toggle(ExportConfig.AutoVerticesUV1, LanguageConfig.str_AutoVerticesUV1);
            GUILayout.EndHorizontal();


            GUILayout.EndVertical();
        }
        //---------------------------------------OtherSetting------------------------------------------
        //??
        GUILayout.BeginHorizontal();
        GUILayout.Space(25);
        GUILayout.Box("", GUILayout.Height(1), GUILayout.Width(position.width - 50));
        GUILayout.EndHorizontal();

        GUILayout.Space(10);

        GUILayout.BeginHorizontal();
        GUILayout.Space(21);
        OtherSetting = EditorGUILayout.Foldout(OtherSetting, LanguageConfig.str_OtherSetting, true);
        GUILayout.EndHorizontal();
        if (OtherSetting)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Space(21);
            GUILayout.Label("", GUILayout.Width(15));
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));

            GUILayout.BeginHorizontal();

            ExportConfig.CustomizeDirectory = GUILayout.Toggle(ExportConfig.CustomizeDirectory, LanguageConfig.str_CustomizeExportRootDirectoryName, GUILayout.Width(250));
            if (ExportConfig.CustomizeDirectory)
                ExportConfig.CustomizeDirectoryName = GUILayout.TextField(ExportConfig.CustomizeDirectoryName);
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }


        //??
        GUILayout.BeginHorizontal();
        GUILayout.Space(21);
        GUILayout.Box("", GUILayout.Height(1), GUILayout.Width(position.width - 60));
        GUILayout.EndHorizontal();

        GUILayout.EndScrollView();
        GUILayout.BeginHorizontal();
        if (PassNull)
        {
            GUIStyle g = new GUIStyle();
            g.normal.textColor = Color.red;

            GUILayout.Label(LanguageConfig.str_SavePathcannotbeempty, g);
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Space(21);
        GUILayout.Label(LanguageConfig.str_SavePath, GUILayout.Width(69), GUILayout.ExpandWidth(false));
        string savePath = ExportConfig.SAVEPATH;
        savePath = GUILayout.TextField(savePath, GUILayout.Height(21));

        if (savePath.Length <= 0)
        {
            savePath = "Assets";
        }
        if (GUILayout.Button(LanguageConfig.str_Browse, GUILayout.MaxWidth(100), GUILayout.Height(22)))
        {
            savePath = EditorUtility.SaveFolderPanel("LayaUnityPlugin", savePath, "");   
        }
        if (savePath.Length > 0)
        {
            ExportConfig.SAVEPATH = savePath;
            PassNull = false;
            this.Repaint();
        }
        GUILayout.Space(21);
        GUILayout.EndHorizontal();
        GUILayout.Space(21);
        
        GUILayout.BeginHorizontal();
        GUILayout.Space(21);
        GUIContent c22 = new GUIContent(LanguageConfig.str_LayaAirExport, exporttu);
        if (GUILayout.Button(c22, GUILayout.Height(30), GUILayout.Width(position.width - 45)))
        {
            try {
                LayaAir3Export.ExportScene();
            } catch(Exception) {
                Debug.LogError(LanguageConfig.str_ExportFailed);
                throw;
            }
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(30);
        ExportConfig.saveConfiguration();

    }

}
