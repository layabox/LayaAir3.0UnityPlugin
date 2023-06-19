
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using System.IO;

internal class MaterialFile : JsonFile
{
    private string outUrl;
    private Material m_material;
    override public string outPath
    {
        get
        {
            return ExportConfig.SavePath() + "/" + this.outUrl;
        }
    }
    public MaterialFile(ResoureMap map, Material material) : base(null,new JSONObject(JSONObject.Type.OBJECT))
    {
        this.resoureMap = map;
        string materialPath = AssetsUtil.GetMaterialPath(material);
        this.updatePath(materialPath);
        this.outUrl =  GameObjectUitls.cleanIllegalChar(materialPath.Split('.')[0], false) + ".lmat";
        this.m_material = material;
        if(material.shader.name == "Skybox/6 Sided")
        {
            MetarialUitls.WriteSkyMetarial(material, this.jsonData, map);
        }
         else
        {
            MetarialUitls.WriteMetarial(material, this.jsonData, map);
        }
      
    }

    public override void SaveFile(Dictionary<string, FileData> exportFiles)
    {
        base.saveMeta();
        string jsonContent = this.jsonData.Print(true);
     
        FileStream fs = new FileStream(outPath, FileMode.Create, FileAccess.Write);
        StreamWriter writer = new StreamWriter(fs);
        writer.Write(jsonContent);
        writer.Close();
    }


}
