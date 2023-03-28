using System.Xml;

public class LanguageConfig 
{
    public enum languages
    {
        English = 0,
        中文 = 1,
    }
    //所有显示string
    public static string str_Scene;
    public static string str_Sprite3D;
    public static string str_MeshSetting;
    public static string str_IgnoreVerticesUV;
    public static string str_IgnoreVerticesColor;
    public static string str_IgnoreVerticesNormal;
    public static string str_IgnoreVerticesTangent;
    public static string str_AutoVerticesUV1;
    public static string str_GameObjectSetting;
    public static string str_IgnoreNotActiveGameObjects;
    public static string str_BatchMakeTheFirstLevelGameObjects;

    public static string str_OtherSetting;
    public static string str_CustomizeExportRootDirectoryName;
    public static string str_SavePathcannotbeempty;
    public static string str_SavePath;
    public static string str_ClearConfig;

    public static string str_LayaAirRun;
    public static string str_LayaAirExport;
    public static string str_Exportaddresscannotbeempty;
    

    public static string str_LayaBoxVersion;
    public static string str_AboutLayaAir;
    public static string str_Browse;

    public static int language = -1;


    public static languages GetLanguages()
    {
        if (language == -1) {
            return languages.中文;
        }
        else
        {
            return (languages)language;
        }
    }

    public static bool setLanguages(languages language)
    {
        if(GetLanguages() == language)
        {
            return false;
        }
        int index = ((int)language);
        ReadLanguage(index);
        return true;
    }

    public static void configLanguage()
    {
        int index = ((int)GetLanguages());
        ReadLanguage(index);
    }

    private static void ReadLanguage(int index)
    {
        if(index == language)
        {
            return;
        }
        language = index;
        XmlDocument xmlDoc = new XmlDocument();
        XmlNode xn;
        //英语
        if (index == 0)
        {
            xmlDoc.Load("Assets/LayaAir3D/English.xml");
            xn = xmlDoc.SelectSingleNode("EnglishLanguage");
        }//汉语
        else
        {
            xmlDoc.Load("Assets/LayaAir3D/Chinese.xml");
            xn = xmlDoc.SelectSingleNode("ChineseLanguage");
        }

        str_Scene = xn.SelectSingleNode("Scene").InnerText;
        str_Sprite3D = xn.SelectSingleNode("Sprite3D").InnerText;
        str_MeshSetting = xn.SelectSingleNode("MeshSetting").InnerText;
        str_IgnoreVerticesUV = xn.SelectSingleNode("IgnoreVerticesUV").InnerText;
        str_IgnoreVerticesColor = xn.SelectSingleNode("IgnoreVerticesColor").InnerText;
        str_IgnoreVerticesNormal = xn.SelectSingleNode("IgnoreVerticesNormal").InnerText;
        str_IgnoreVerticesTangent = xn.SelectSingleNode("IgnoreVerticesTangent").InnerText;
        str_AutoVerticesUV1 = xn.SelectSingleNode("AutoVerticesUV1").InnerText;
      
        str_GameObjectSetting = xn.SelectSingleNode("GameObjectSetting").InnerText;
        str_IgnoreNotActiveGameObjects = xn.SelectSingleNode("IgnoreNotActiveGameObjects").InnerText;
        str_BatchMakeTheFirstLevelGameObjects = xn.SelectSingleNode("BatchMakeTheFirstLevelGameObjects").InnerText;
        str_OtherSetting = xn.SelectSingleNode("OtherSetting").InnerText;
        str_CustomizeExportRootDirectoryName = xn.SelectSingleNode("CustomizeExportRootDirectoryName").InnerText;
        str_SavePathcannotbeempty = xn.SelectSingleNode("SavePathcannotbeempty").InnerText;
        str_SavePath = xn.SelectSingleNode("SavePath").InnerText;
        str_ClearConfig = xn.SelectSingleNode("ClearConfig").InnerText;
        str_LayaAirExport = xn.SelectSingleNode("LayaAirExport").InnerText;
        str_Exportaddresscannotbeempty = xn.SelectSingleNode("Exportaddresscannotbeempty").InnerText;
        str_Browse = xn.SelectSingleNode("Browse").InnerText;


    }
}
