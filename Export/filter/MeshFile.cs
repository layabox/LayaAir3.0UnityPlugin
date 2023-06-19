
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

internal class MeshFile : FileData
{
    private Mesh m_mesh;
    private string outUrl;
    private Renderer render;
    override public string outPath
    {
        get
        {
            return ExportConfig.SavePath() + "/" + this.outUrl;
        }
    }
    public MeshFile(Mesh mesh,Renderer render) :base(null)
    {
        string path = AssetsUtil.GetMeshPath(mesh);
        this.updatePath(path);
        this.outUrl = path;
        this.m_mesh = mesh;
        this.render = render;
    }

    public override void SaveFile(Dictionary<string, FileData> exportFiles)
    {
        if (this.m_mesh.uv2.Length > 0 && ExportConfig.AutoVerticesUV1)
        {
            JSONObject autouv1 = new JSONObject(JSONObject.Type.OBJECT);
            autouv1.AddField("generateLightmapUVs", true);
            this.metaData().AddField("importer", autouv1);
        }
        base.saveMeta();
        FileStream fs = Util.FileUtil.saveFile(this.outPath);
        string meshName = GameObjectUitls.cleanIllegalChar(this.m_mesh.name, true);
        if (this.render is SkinnedMeshRenderer)
        {
            MeshUitls.writeSkinnerMesh(this.render as SkinnedMeshRenderer, meshName, fs);
        }
        else
        {
            MeshUitls.writeMesh(this.m_mesh, meshName, fs);
        }
    }


   
}
