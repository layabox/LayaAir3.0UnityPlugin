using System.Collections.Generic;
using System.IO;

internal class FileData
{
    protected string m_path;
    private string m_uuid;

    protected JSONObject m_metaData;
    public string metaPath
    {
        get
        {
            return outPath + ".meta";
        }
    }

    public string uuid
    {
        get
        {
            return this.m_uuid;
        }
    }

    public string outPath
    {
        get
        {
            return ExportConfig.SavePath() + "/" + this.m_path;
        }
    }

    public string filePath
    {
        get
        {
            return this.m_path;
        }
    }
    public FileData(string path)
    {
        this.updatePath(path);
    }

    protected void updatePath(string path)
    {
        if (path == null)
        {
            return;
        }
        this.m_path = path;

        if (File.Exists(metaPath))
        {
            JSONObject customMap = this.m_metaData = JSONObject.Create(File.ReadAllText(metaPath));
            this.m_uuid = customMap.GetField("uuid").str;
        }
        else
        {
            this.m_uuid = System.Guid.NewGuid().ToString();
            this.m_metaData = new JSONObject(JSONObject.Type.OBJECT);
            this.m_metaData.SetField("uuid", this.m_uuid);
        }
    }

    public JSONObject metaData()
    {
        return this.m_metaData;
    }

    public void saveMeta()
    {
        if (this.m_metaData == null)
        {
            return;
        }
        string filePath = metaPath;
        FileStream fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        StreamWriter writer = new StreamWriter(fs);
        writer.Write(this.m_metaData.Print(true));
        writer.Close();
    }


    public virtual void SaveFile(Dictionary<string, FileData> exportFiles)
    {

    }

}
