using System.Collections.Generic;
using System.IO;
using UnityEngine;

internal class JsonFile : FileData
{
    private JSONObject m_data;
    private List<string> m_regexlist;
    public JsonFile(string path,JSONObject data):base(path)
    {
        this.m_data = data;
        this.m_regexlist = new List<string>();
    }
    public JSONObject jsonData
    {
        get
        {
            return this.m_data;
        }
    }
    public void AddRegistList(string path)
    {
        if (!this.m_regexlist.Exists(t => t == path))
        {
            this.m_regexlist.Add(path);
        }
    }
    public override void SaveFile(Dictionary<string, FileData> exportFiles)
    {
        string jsonContent = this.m_data.Print(true);
        for (var i = 0; i < this.m_regexlist.Count; i++)
        {
            string filename = this.m_regexlist[i];
            FileData file = exportFiles[filename];
            if (file == null)
            {
                Debug.LogWarning("LayaAir3D Warning: can not found file " + filename);
            }
            jsonContent = jsonContent.Replace(filename, file.uuid);
        }
        string filePath = outPath;
        string folder = Path.GetDirectoryName(filePath);
        if (!Directory.Exists(folder))
            Directory.CreateDirectory(folder);

        FileStream fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        StreamWriter writer = new StreamWriter(fs);
        writer.Write(jsonContent);
        writer.Close();

        base.saveMeta();
    }
}
