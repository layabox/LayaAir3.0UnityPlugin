using System.Xml;

public class LanguageConfig 
{

    //À˘”–œ‘ æstring
    public static string str_Scene;
    public static string str_Sprite3D;
    public static string str_MeshSetting;
    public static string str_IgnoreVerticesUV;
    public static string str_IgnoreVerticesColor;
    public static string str_IgnoreVerticesNormal;
    public static string str_IgnoreVerticesTangent;
    public static string str_AutoVerticesUV1;
    public static string str_Compress;
    public static string str_ThisfunctionneedVIP;
    public static string str_PleaseBindthecurrentdevice;
    public static string str_TerrainSetting;
    public static string str_ConvertTerrainToMesh;
    public static string str_Resolution;
    public static string str_GameObjectSetting;
    public static string str_IgnoreNotActiveGameObjects;
    public static string str_BatchMakeTheFirstLevelGameObjects;
    public static string str_Assetsplatform;
    public static string str_AnimationSetting;
    public static string str_OtherSetting;
    public static string str_CustomizeExportRootDirectoryName;
    public static string str_SavePathcannotbeempty;
    public static string str_SavePath;
    public static string str_Browse;
    public static string str_ClearConfig;

    public static string str_LayaAirRun;
    public static string str_LayaAirExport;
    public static string str_Exportaddresscannotbeempty;
    public static string str_JPGQuality;
    public static string str_erweimaIcon;

    public static string str_MultiScene;
    public static string str_IOSQuality;

    public static string str_TexAnimation;
    public static string str_TexAniamtionFPS;

    public static string str_TexAnimationSocket;

    public static string str_LayaBoxVersion;
    public static string str_AboutLayaAir;

    public static int language = -1;

    public static void ReadLanguage(int index)
    {
        if(index == language)
        {
            return;
        }
        language = index;
        XmlDocument xmlDoc = new XmlDocument();
        XmlNode xn;
        //”¢”Ô
        if (index == 0)
        {
            xmlDoc.Load("Assets/LayaAir3D/English.xml");
            xn = xmlDoc.SelectSingleNode("EnglishLanguage");
        }//∫∫”Ô
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
        str_Compress = xn.SelectSingleNode("Compress").InnerText;
        str_ThisfunctionneedVIP = xn.SelectSingleNode("ThisfunctionneedVIP").InnerText;
        str_PleaseBindthecurrentdevice = xn.SelectSingleNode("PleaseBindthecurrentdevice").InnerText;
        str_TerrainSetting = xn.SelectSingleNode("TerrainSetting").InnerText;
        str_ConvertTerrainToMesh = xn.SelectSingleNode("ConvertTerrainToMesh").InnerText;
        str_Resolution = xn.SelectSingleNode("Resolution").InnerText;
        str_GameObjectSetting = xn.SelectSingleNode("GameObjectSetting").InnerText;
        str_IgnoreNotActiveGameObjects = xn.SelectSingleNode("IgnoreNotActiveGameObjects").InnerText;
        str_BatchMakeTheFirstLevelGameObjects = xn.SelectSingleNode("BatchMakeTheFirstLevelGameObjects").InnerText;
        str_Assetsplatform = xn.SelectSingleNode("Assetsplatform").InnerText;
        str_AnimationSetting = xn.SelectSingleNode("AnimationSetting").InnerText;
        str_OtherSetting = xn.SelectSingleNode("OtherSetting").InnerText;
        str_CustomizeExportRootDirectoryName = xn.SelectSingleNode("CustomizeExportRootDirectoryName").InnerText;
        str_SavePathcannotbeempty = xn.SelectSingleNode("SavePathcannotbeempty").InnerText;
        str_SavePath = xn.SelectSingleNode("SavePath").InnerText;
        str_Browse = xn.SelectSingleNode("Browse").InnerText;
        str_ClearConfig = xn.SelectSingleNode("ClearConfig").InnerText;

        str_LayaAirRun = xn.SelectSingleNode("LayaAirRun").InnerText;
        str_LayaAirExport = xn.SelectSingleNode("LayaAirExport").InnerText;
        str_Exportaddresscannotbeempty = xn.SelectSingleNode("Exportaddresscannotbeempty").InnerText;
        str_JPGQuality = xn.SelectSingleNode("JPGquality").InnerText;
        str_erweimaIcon = xn.SelectSingleNode("erweimatu").InnerText;
        str_MultiScene = xn.SelectSingleNode("multiScene").InnerText;
        str_IOSQuality = xn.SelectSingleNode("IOSQuality").InnerText;
        str_TexAnimation = xn.SelectSingleNode("TexAnimatorSetting").InnerText;
        str_TexAniamtionFPS = xn.SelectSingleNode("TexAnimatorbakeFPS").InnerText;
        str_TexAnimationSocket = xn.SelectSingleNode("TexAnimationSocket").InnerText;
        str_LayaBoxVersion = xn.SelectSingleNode("LayaBoxVersion").InnerText;
        str_AboutLayaAir = xn.SelectSingleNode("AboutLayaAir").InnerText;
    }
}
