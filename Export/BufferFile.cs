using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

internal class BufferFile : FileData
{
    private FileStream m_fs;
    public BufferFile(string path) : base(path)
    {
        m_fs = Util.FileUtil.saveFile(this.outPath);
    }

    public FileStream filesteam
    {
        get
        {
            return this.m_fs;
        }
    }
    
    public override void SaveFile(Dictionary<string, FileData> exportFiles)
    {
       
        base.saveMeta();
    }
}