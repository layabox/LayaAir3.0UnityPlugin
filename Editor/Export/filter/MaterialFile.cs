
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using System.IO;

internal class MaterialFile : JsonFile
{
    private Material m_material;
    public MaterialFile(ResoureMap map, Material material) : base(null,new JSONObject(JSONObject.Type.OBJECT))
    {
        this.resoureMap = map;
        this.updatePath(AssetsUtil.GetMaterialPath(material));
        this.m_material = material;
      /*  if(material.shader.name == "Skybox/6 Sided")
        {
            MetarialUitls.WriteSkyMetarial(material, this.jsonData, map);
        }
         else
        {*/
            MetarialUitls.WriteMetarial(material, this.jsonData, map);
        //}
      
    }

    protected override string getOutFilePath(string path)
    {
        return GameObjectUitls.cleanIllegalChar(path.Split('.')[0], false) + ".lmat";
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
