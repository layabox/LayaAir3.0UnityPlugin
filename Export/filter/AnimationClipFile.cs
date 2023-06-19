using System.Collections.Generic;
using System.IO;
using UnityEngine;

internal class AnimationClipFile : FileData
{
    private string outUrl;
    private AnimationClip m_clip;
    private GameObject m_root;
    override public string outPath
    {
        get
        {
            return ExportConfig.SavePath() + "/" + this.outUrl;
        }
    }
    public AnimationClipFile(AnimationClip clip,GameObject root):base(null)
    {
        this.m_clip = clip;
        this.m_root = root;
        string path = AssetsUtil.GetAnimationClipPath(clip);
        this.updatePath(path);
        this.outUrl = path;
    }
    public override void SaveFile(Dictionary<string, FileData> exportFiles)
    {
        base.saveMeta();
        FileStream fs = Util.FileUtil.saveFile(this.outPath);
        string clipName = GameObjectUitls.cleanIllegalChar(this.m_clip.name, true);
        GameObjectUitls.writeClip(this.m_clip, fs, this.m_root, clipName);
    }
}
