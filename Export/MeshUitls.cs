using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

public class Triangle
{
    public VertexData point1;
    public VertexData point2;
    public VertexData point3;
}


//顶点数据
public class VertexData
{
    public int index;
    public Vector3 vertice;
    public Vector3 normal;
    public Color color;
    public Vector2 uv;
    public Vector2 uv2;
    public Vector4 boneWeight;
    public Vector4 boneIndex;
    public Vector4 tangent;
    //是否已经根据索引队列改变boneindex
    public bool ischange = true;
    //判断index队列
    public int subMeshindex = -1;
    public int subsubMeshindex = -1;
    public Dictionary<string, int> commonPoint;

    public void setValue(VertexData othervertexdata)
    {
        vertice = othervertexdata.vertice;
        normal = othervertexdata.normal;
        color = othervertexdata.color;
        uv = othervertexdata.uv;
        uv2 = othervertexdata.uv2;
        boneWeight = othervertexdata.boneWeight;
        tangent = othervertexdata.tangent;
        boneIndex = new Vector4(othervertexdata.boneIndex.x, othervertexdata.boneIndex.y, othervertexdata.boneIndex.z, othervertexdata.boneIndex.w);
    }
}

public class MeshUitls 
{
    private static string LmVersion = "LAYAMODEL:0501";
    private static int[] VertexStructure = new int[7];
    public static void writeMesh(Mesh mesh, string meshName, FileStream fs)
    {
        int i, j;
        UInt16 subMeshCount = (UInt16)mesh.subMeshCount;
        int blockCount = subMeshCount + 1;

        UInt16 everyVBSize = 0;
        string vbDeclaration = "";


        //获取顶点结构,0代表该顶点结构无此数据;1，反之
        //由于数据量可能很大，为了优化效率，默认顶点的位置，法线，uv，骨骼权重存在，还有tangents
        for (i = 0; i < VertexStructure.Length; i++)
            VertexStructure[i] = 0;

        Vector3[] vertices = mesh.vertices;
        Vector3[] normals = mesh.normals;
        Color[] colors = mesh.colors;
        Vector2[] uv = mesh.uv;
        Vector4[] tangents = mesh.tangents;
        if (vertices != null && vertices.Length != 0)
        {
            VertexStructure[0] = 1;
            vbDeclaration += "POSITION";
            everyVBSize += 12;
        }

        if (normals != null && normals.Length != 0 && !ExportConfig.IgnoreVerticesNormal)
        {
            VertexStructure[1] = 1;
            vbDeclaration += ",NORMAL";
            everyVBSize += 12;
        }

        if (colors != null && colors.Length != 0 && !ExportConfig.IgnoreVerticesColor)
        {
            VertexStructure[2] = 1;
            vbDeclaration += ",COLOR";
            everyVBSize += 16;
        }

        if (uv != null && uv.Length != 0 && !ExportConfig.IgnoreVerticesUV)
        {
            VertexStructure[3] = 1;
            vbDeclaration += ",UV";
            everyVBSize += 8;
        }



        if (tangents != null && tangents.Length != 0 && !ExportConfig.IgnoreVerticesTangent)
        {
            VertexStructure[6] = 1;
            vbDeclaration += ",TANGENT";
            everyVBSize += 16;
        }

        int[] subMeshFirstIndex = new int[subMeshCount];
        int[] subMeshIndexLength = new int[subMeshCount];

        for (i = 0; i < subMeshCount; i++)
        {
            int[] subIndices = mesh.GetIndices(i);
            subMeshFirstIndex[i] = subIndices[0];
            subMeshIndexLength[i] = subIndices.Length;
        }

        long VerionSize = 0;

        long ContentAreaPosition_Start = 0;

        long MeshAreaPosition_Start = 0;
        long MeshAreaPosition_End = 0;
        long MeshAreaSize = 0;
        long VBMeshAreaPosition_Start = 0;
        long IBMeshAreaPosition_Start = 0;
        long BoneAreaPosition_Start = 0;

        long BlockAreaPosition_Start = 0;

        long StringAreaPosition_Start = 0;
        long StringAreaPosition_End = 0;

        long StringDatasAreaPosition_Start = 0;
        long StringDatasAreaPosition_End = 0;
        long StringDatasAreaSize = 0;

        long VBContentDatasAreaPosition_Start = 0;
        long VBContentDatasAreaPosition_End = 0;
        long VBContentDatasAreaSize = 0;

        long IBContentDatasAreaPosition_Start = 0;
        long IBContentDatasAreaPosition_End = 0;
        long IBContentDatasAreaSize = 0;

        long[] subMeshAreaPosition_Start = new long[subMeshCount];
        long[] subMeshAreaPosition_End = new long[subMeshCount];
        long[] subMeshAreaSize = new long[subMeshCount];

        List<string> stringDatas = new List<string>();
        stringDatas.Add("MESH");
        stringDatas.Add("SUBMESH");

        //版本号
        Util.FileUtil.WriteData(fs, LmVersion);
        VerionSize = fs.Position;

        //标记数据信息区
        ContentAreaPosition_Start = fs.Position; // 预留数据区偏移地址
        Util.FileUtil.WriteData(fs, (UInt32)0);//UInt32 offset
        Util.FileUtil.WriteData(fs, (UInt32)0);//UInt32 blockLength

        //内容段落信息区
        BlockAreaPosition_Start = fs.Position;//预留段落数量

        Util.FileUtil.WriteData(fs, (UInt16)blockCount);
        for (i = 0; i < blockCount; i++)
        {
            Util.FileUtil.WriteData(fs, (UInt32)0);//UInt32 blockStart
            Util.FileUtil.WriteData(fs, (UInt32)0);//UInt32 blockLength
        }

        //字符区
        StringAreaPosition_Start = fs.Position;//预留字符区
        Util.FileUtil.WriteData(fs, (UInt32)0);//UInt32 offset
        Util.FileUtil.WriteData(fs, (UInt16)0);//count

        //网格区
        MeshAreaPosition_Start = fs.Position;
        Util.FileUtil.WriteData(fs, (UInt16)stringDatas.IndexOf("MESH"));//解析函数名字符索引
        stringDatas.Add(meshName);
        Util.FileUtil.WriteData(fs, (UInt16)stringDatas.IndexOf(meshName));//网格名字符索引

        //vb
        Util.FileUtil.WriteData(fs, (UInt16)1);//vb数量
        VBMeshAreaPosition_Start = fs.Position;
        for (i = 0; i < 1; i++)//vb
        {
            Util.FileUtil.WriteData(fs, (UInt32)0);//vbStart
            Util.FileUtil.WriteData(fs, (UInt32)0);//vbLength

            stringDatas.Add(vbDeclaration);
            Util.FileUtil.WriteData(fs, (UInt16)stringDatas.IndexOf(vbDeclaration));//vbDeclar
        }

        //ib
        IBMeshAreaPosition_Start = fs.Position;
        Util.FileUtil.WriteData(fs, (UInt32)0);//ibStart
        Util.FileUtil.WriteData(fs, (UInt32)0);//ibLength

        mesh.RecalculateBounds();
        Bounds bound = mesh.bounds;
        Util.FileUtil.WriteData(fs, -(float)bound.max.x);
        Util.FileUtil.WriteData(fs, (float)bound.min.y);
        Util.FileUtil.WriteData(fs, (float)bound.min.z);
        Util.FileUtil.WriteData(fs, -(float)bound.min.x);
        Util.FileUtil.WriteData(fs, (float)bound.max.y);
        Util.FileUtil.WriteData(fs, (float)bound.max.z);

        BoneAreaPosition_Start = fs.Position;



        //uint16 boneCount
        Util.FileUtil.WriteData(fs, (UInt16)0);//boneCount

        Util.FileUtil.WriteData(fs, (UInt32)0);//bindPoseStart
        Util.FileUtil.WriteData(fs, (UInt32)0);//bindPoseLength
        Util.FileUtil.WriteData(fs, (UInt32)0);//inverseGlobalBindPoseStart
        Util.FileUtil.WriteData(fs, (UInt32)0);//inverseGlobalBindPoseLength

        MeshAreaPosition_End = fs.Position;
        MeshAreaSize = MeshAreaPosition_End - MeshAreaPosition_Start;

        //子网格区
        for (i = 0; i < subMeshCount; i++)
        {
            subMeshAreaPosition_Start[i] = fs.Position;

            Util.FileUtil.WriteData(fs, (UInt16)stringDatas.IndexOf("SUBMESH"));//解析函数名字符索引
            Util.FileUtil.WriteData(fs, (UInt16)0);//vbIndex

            Util.FileUtil.WriteData(fs, (UInt32)0);//ibStart
            Util.FileUtil.WriteData(fs, (UInt32)0);//ibLength

            Util.FileUtil.WriteData(fs, (UInt16)1);//drawCount

            Util.FileUtil.WriteData(fs, (UInt32)0);//subIbStart
            Util.FileUtil.WriteData(fs, (UInt32)0);//subIbLength

            Util.FileUtil.WriteData(fs, (UInt32)0);//boneDicStart
            Util.FileUtil.WriteData(fs, (UInt32)0);//boneDicLength

            subMeshAreaPosition_End[i] = fs.Position;

            subMeshAreaSize[i] = subMeshAreaPosition_End[i] - subMeshAreaPosition_Start[i];
        }

        //字符数据区
        StringDatasAreaPosition_Start = fs.Position;
        for (i = 0; i < stringDatas.Count; i++)
        {
            Util.FileUtil.WriteData(fs, stringDatas[i]);
        }
        StringDatasAreaPosition_End = fs.Position;
        StringDatasAreaSize = StringDatasAreaPosition_End - StringDatasAreaPosition_Start;

        //内容数据区
        //vb
        Vector3 vertice;
        Vector3 normal;
        Color color;
        Vector2 uvs;
        Vector4 tangent;
        VBContentDatasAreaPosition_Start = fs.Position;

        for (j = 0; j < mesh.vertexCount; j++)
        {
            vertice = vertices[j];
            Util.FileUtil.WriteData(fs, -vertice.x, vertice.y, vertice.z);
            //法线8
            if (VertexStructure[1] == 1)
            {
                normal = normals[j];
                Util.FileUtil.WriteData(fs, -normal.x, normal.y, normal.z);
            }
            //颜色
            if (VertexStructure[2] == 1)
            {
                color = colors[j];
                Util.FileUtil.WriteData(fs, color.r, color.g, color.b, color.a);
            }
            //uv
            if (VertexStructure[3] == 1)
            {
                uvs = uv[j];
                Util.FileUtil.WriteData(fs, uvs.x, uvs.y * -1.0f + 1.0f);
            }

            //切线
            if (VertexStructure[6] == 1)
            {
                tangent = tangents[j];
                Util.FileUtil.WriteData(fs, -tangent.x, tangent.y, tangent.z, tangent.w);
            }
        }

        VBContentDatasAreaPosition_End = fs.Position;
        VBContentDatasAreaSize = VBContentDatasAreaPosition_End - VBContentDatasAreaPosition_Start;

        //indices
        //TODO:3.0 未来加入标记存入lm内
        IBContentDatasAreaPosition_Start = fs.Position;
        int[] triangles = mesh.triangles;
        if (mesh.indexFormat == IndexFormat.UInt32 && mesh.vertexCount > 65535)
        {
            for (j = 0; j < triangles.Length; j++)
            {
                Util.FileUtil.WriteData(fs, (UInt32)triangles[j]);
            }
        }
        else
        {
            for (j = 0; j < triangles.Length; j++)
            {
                Util.FileUtil.WriteData(fs, (UInt16)triangles[j]);
            }
        }

        IBContentDatasAreaPosition_End = fs.Position;
        IBContentDatasAreaSize = IBContentDatasAreaPosition_End - IBContentDatasAreaPosition_Start;

        //倒推子网格区
        UInt32 ibstart = 0;
        UInt32 iblength = 0;
        UInt32 _ibstart = 0;
        for (i = 0; i < subMeshCount; i++)
        {
            fs.Position = subMeshAreaPosition_Start[i] + 4;

            if (subMeshCount == 1)
            {
                ibstart = 0;
                iblength = mesh.indexFormat == IndexFormat.UInt32 ? (UInt32)(IBContentDatasAreaSize / 4) : (UInt32)(IBContentDatasAreaSize / 2);
            }
            else if (i == 0)
            {
                ibstart = _ibstart;
                iblength = (UInt32)subMeshIndexLength[i];
            }
            else if (i < subMeshCount - 1)
            {
                ibstart = (UInt32)_ibstart;
                iblength = (UInt32)subMeshIndexLength[i];
            }
            else
            {
                ibstart = (UInt32)_ibstart;
                iblength = (UInt32)subMeshIndexLength[i];
            }

            Util.FileUtil.WriteData(fs, ibstart);
            Util.FileUtil.WriteData(fs, iblength);
            _ibstart += iblength;

            fs.Position += 2;

            Util.FileUtil.WriteData(fs, ibstart);
            Util.FileUtil.WriteData(fs, iblength);
        }

        //倒推网格区
        fs.Position = VBMeshAreaPosition_Start;
        Util.FileUtil.WriteData(fs, (UInt32)(VBContentDatasAreaPosition_Start - StringDatasAreaPosition_Start));
        Util.FileUtil.WriteData(fs, (UInt32)mesh.vertexCount);

        fs.Position = IBMeshAreaPosition_Start;
        Util.FileUtil.WriteData(fs, (UInt32)(IBContentDatasAreaPosition_Start - StringDatasAreaPosition_Start));
        Util.FileUtil.WriteData(fs, (UInt32)IBContentDatasAreaSize);

        //倒推字符区
        fs.Position = StringAreaPosition_Start;
        Util.FileUtil.WriteData(fs, (UInt32)0);
        Util.FileUtil.WriteData(fs, (UInt16)stringDatas.Count);
        StringAreaPosition_End = fs.Position;

        //倒推段落区
        fs.Position = BlockAreaPosition_Start + 2;
        Util.FileUtil.WriteData(fs, (UInt32)MeshAreaPosition_Start);
        Util.FileUtil.WriteData(fs, (UInt32)MeshAreaSize);
        for (i = 0; i < subMeshCount; i++)
        {
            Util.FileUtil.WriteData(fs, (UInt32)subMeshAreaPosition_Start[i]);
            Util.FileUtil.WriteData(fs, (UInt32)subMeshAreaSize[i]);
        }

        //倒推标记内容数据信息区
        fs.Position = ContentAreaPosition_Start;
        Util.FileUtil.WriteData(fs, (UInt32)StringDatasAreaPosition_Start);
        Util.FileUtil.WriteData(fs, (UInt32)(StringDatasAreaPosition_Start + StringDatasAreaSize + VBContentDatasAreaSize + IBContentDatasAreaSize + subMeshAreaSize[0]));

        fs.Close();
    }

    public static void writeSkinnerMesh(SkinnedMeshRenderer skinnedMeshRenderer, string meshName, FileStream fs, int MaxBoneCount = 24)
    {
        Mesh mesh = skinnedMeshRenderer.sharedMesh;
        //-------------------------------------------��֯��д������-------------------------------------------
        UInt16 vbCount = (UInt16)1;//unity��Ĭ��������65535
        UInt16 subMeshCount = (UInt16)mesh.subMeshCount;
        UInt16 everyVBSize = 0;
        string vbDeclaration = "";

        //获取顶点结构,0代表该顶点结构无此数据;1，反之
        //由于数据量可能很大，为了优化效率，默认顶点的位置，法线，uv，骨骼权重存在，还有triangles
        for (int i = 0; i < VertexStructure.Length; i++)
            VertexStructure[i] = 0;

        if (mesh.vertices != null && mesh.vertices.Length != 0)
        {
            VertexStructure[0] = 1;
            vbDeclaration += "POSITION";
            everyVBSize += 12;
        }

        if (mesh.normals != null && mesh.normals.Length != 0 && !ExportConfig.IgnoreVerticesNormal)
        {
            VertexStructure[1] = 1;
            vbDeclaration += ",NORMAL";
            everyVBSize += 12;
        }

        if (mesh.colors != null && mesh.colors.Length != 0 && !ExportConfig.IgnoreVerticesColor)
        {
            VertexStructure[2] = 1;
            vbDeclaration += ",COLOR";
            everyVBSize += 16;
        }

        if (mesh.uv != null && mesh.uv.Length != 0 && !ExportConfig.IgnoreVerticesUV)
        {
            VertexStructure[3] = 1;
            vbDeclaration += ",UV";
            everyVBSize += 8;
        }

        if (mesh.uv2 != null && mesh.uv2.Length != 0 && !ExportConfig.IgnoreVerticesUV)
        {
            VertexStructure[4] = 1;
            vbDeclaration += ",UV1";
            everyVBSize += 8;
        }

        if (mesh.boneWeights != null && mesh.boneWeights.Length != 0)
        {
            VertexStructure[5] = 1;
            vbDeclaration += ",BLENDWEIGHT,BLENDINDICES";
            everyVBSize += 32;
        }

        if (mesh.tangents != null && mesh.tangents.Length != 0 && !ExportConfig.IgnoreVerticesTangent)
        {
            VertexStructure[6] = 1;
            vbDeclaration += ",TANGENT";
            everyVBSize += 16;
        }

        //获取骨骼数据
        List<Transform> bones = new List<Transform>();
        for (int j = 0; j < skinnedMeshRenderer.bones.Length; j++)
        {
            Transform _bone = skinnedMeshRenderer.bones[j];
            if (bones.IndexOf(_bone) == -1)
                bones.Add(_bone);
        }


        //重构VB,IB数据
        //所有点的集合
        int vertexlength = mesh.vertexCount;
        List<VertexData> vertexBuffer = new List<VertexData>();


        //所有index的集合
        List<int> indexBuffer = new List<int>();

        //根据subMesh以及骨骼分离骨骼索引数据（最大list长度为24，每个subMesh至少一个list，里面存着骨骼索引）
        List<List<int>>[] boneIndexList = new List<List<int>>[subMeshCount];
        //subMesh index索引的长度
        List<int>[] subIBIndex = new List<int>[subMeshCount];
        List<List<Triangle>>[] subsubMeshtriangles = new List<List<Triangle>>[subMeshCount];

        Vector3[] vertices = mesh.vertices;
        Vector3[] normals = mesh.normals;
        Color[] colors = mesh.colors;
        Vector2[] uvs = mesh.uv;
        Vector2[] uv2s = mesh.uv2;
        BoneWeight[] boneWeights = mesh.boneWeights;
        Vector4[] tangents = mesh.tangents;
        //组织所有的顶点数据
        for (int ii = 0; ii < vertexlength; ii++)
        {
            vertexBuffer.Add(getVertexData(vertices, normals, colors, uvs, uv2s, boneWeights, tangents, ii));
        }


        int[] subMeshFirstIndex = new int[subMeshCount];
        int[] subMeshIndexLength = new int[subMeshCount];
        int _ibLength = 0;
        //循环每个subMesh
        for (int i = 0; i < subMeshCount; i++)
        {
            //获得submesh的所有index
            int[] subIndices = mesh.GetIndices(i);
            //存了需要的骨骼数据索引
            boneIndexList[i] = new List<List<int>>();
            boneIndexList[i].Add(new List<int>());
            //存了骨骼数目 24，24，24，10
            subIBIndex[i] = new List<int>();
            //三角形数组，根据骨骼来划分为好几组划分
            List<List<Triangle>> subsubMeshTriangle = new List<List<Triangle>>();
            subsubMeshtriangles[i] = subsubMeshTriangle;
            //必定有一个三角形组合
            subsubMeshTriangle.Add(new List<Triangle>());
            //subMesh中所有的triangle
            List<Triangle> subAllTriangle = new List<Triangle>();
            //开始组织ib,获得所有的三角形
            for (int j = 0, n = subIndices.Length; j < n; j += 3)
            {
                Triangle triangle = new Triangle();
                triangle.point1 = vertexBuffer[subIndices[j]];
                triangle.point2 = vertexBuffer[subIndices[j + 1]];
                triangle.point3 = vertexBuffer[subIndices[j + 2]];
                subAllTriangle.Add(triangle);
            }
            //将三角形根据骨骼索引分堆
            for (int k = 0; k < subAllTriangle.Count; k++)
            {
                Triangle tri = subAllTriangle[k];
                //获得三角形所有的骨骼顶点索引
                List<int> tigleboneindexs = triangleBoneIndex(tri);
                //遍历循环所有的submesh里面的骨骼索引的list
                bool isAdd = false;
                for (int m = 0; m < boneIndexList[i].Count; m++)
                {
                    List<int> list = listContainCount(tigleboneindexs, boneIndexList[i][m]);
                    //全包含就把三角形全加进去
                    if (list.Count == 0)
                    {
                        subsubMeshTriangle[m].Add(tri);
                        isAdd = true;
                        break;
                    }
                    //不是全包含就看是否加上够24块骨骼
                    else if ((boneIndexList[i][m].Count + list.Count) <= MaxBoneCount)
                    {
                        for (int c = 0; c < list.Count; c++)
                        {
                            boneIndexList[i][m].Add(list[c]);
                        }

                        subsubMeshTriangle[m].Add(tri);
                        isAdd = true;
                        break;
                    }
                }
                if (!isAdd)
                {
                    List<int> newboneindexlist = new List<int>();
                    List<Triangle> newTriangleList = new List<Triangle>();
                    boneIndexList[i].Add(newboneindexlist);
                    subsubMeshTriangle.Add(newTriangleList);
                    for (int w = 0; w < tigleboneindexs.Count; w++)
                    {
                        newboneindexlist.Add(tigleboneindexs[w]);
                    }
                    newTriangleList.Add(tri);
                }
            }

            //分堆之后检测增加点并且修改索引
            for (int q = 0; q < subsubMeshTriangle.Count; q++)
            {
                List<Triangle> subsubtriangles = subsubMeshTriangle[q];
                for (int h = 0; h < subsubtriangles.Count; h++)
                {
                    Triangle trianglle = subsubtriangles[h];
                    //检测三个点
                    trianglle.point1 = checkPoint(trianglle.point1, i, q, vertexBuffer);
                    trianglle.point2 = checkPoint(trianglle.point2, i, q, vertexBuffer);
                    trianglle.point3 = checkPoint(trianglle.point3, i, q, vertexBuffer);
                }
            }

            int lengths = 0;
            for (int o = 0; o < subsubMeshTriangle.Count; o++)
            {
                lengths += subsubMeshTriangle[o].Count * 3;
                subIBIndex[i].Add(lengths);
            }
        }

        //切换缩影且组织index数据
        for (int ii = 0; ii < subMeshCount; ii++)
        {
            List<List<Triangle>> subsubtriangle = subsubMeshtriangles[ii];
            for (int tt = 0; tt < subsubtriangle.Count; tt++)
            {
                List<int> boneindexlist = boneIndexList[ii][tt];
                for (int iii = 0; iii < subsubtriangle[tt].Count; iii++)
                {
                    Triangle trii = subsubtriangle[tt][iii];
                    changeBoneIndex(boneindexlist, trii.point3);
                    changeBoneIndex(boneindexlist, trii.point2);
                    changeBoneIndex(boneindexlist, trii.point1);

                    indexBuffer.Add(trii.point1.index);
                    indexBuffer.Add(trii.point2.index);
                    indexBuffer.Add(trii.point3.index);
                }
            }

        }
        for (int i = 0; i < subMeshCount; i++)
        {
            int[] subIndices = mesh.GetIndices(i);
            subMeshFirstIndex[i] = indexBuffer[_ibLength];
            subMeshIndexLength[i] = subIndices.Length;
            _ibLength += subIndices.Length;
        }





        //vertexBuffer[vertexBuffer.Count - 1].boneIndex[2] = 7;
        //Debug.Log(vertexBuffer[vertexBuffer.Count - 1].boneIndex);

        long VerionSize = 0;

        long ContentAreaPosition_Start = 0;

        long MeshAreaPosition_Start = 0;
        long MeshAreaPosition_End = 0;
        long MeshAreaSize = 0;
        long VBMeshAreaPosition_Start = 0;
        long IBMeshAreaPosition_Start = 0;
        long BoneAreaPosition_Start = 0;

        long BlockAreaPosition_Start = 0;

        long StringAreaPosition_Start = 0;
        long StringAreaPosition_End = 0;

        long StringDatasAreaPosition_Start = 0;
        long StringDatasAreaPosition_End = 0;
        long StringDatasAreaSize = 0;

        long VBContentDatasAreaPosition_Start = 0;
        long VBContentDatasAreaPosition_End = 0;
        long VBContentDatasAreaSize = 0;

        long IBContentDatasAreaPosition_Start = 0;
        long IBContentDatasAreaPosition_End = 0;
        long IBContentDatasAreaSize = 0;

        long inverseGlobalBindPosesDatasAreaPosition_Start = 0;

        long boneDicDatasAreaPosition_Start = 0;
        long boneDicDatasAreaPosition_End = 0;

        long[] subMeshAreaPosition_Start = new long[subMeshCount];
        long[] subMeshAreaPosition_End = new long[subMeshCount];
        long[] subMeshAreaSize = new long[subMeshCount];

        List<string> stringDatas = new List<string>();
        stringDatas.Add("MESH");
        stringDatas.Add("SUBMESH");

        //版本号
        string layaModelVerion = LmVersion;
        Util.FileUtil.WriteData(fs, layaModelVerion);
        VerionSize = fs.Position;

        //标记数据信息区
        ContentAreaPosition_Start = fs.Position; // 预留数据区偏移地址
        Util.FileUtil.WriteData(fs, (UInt32)0);//UInt32 offset
        Util.FileUtil.WriteData(fs, (UInt32)0);//UInt32 blockLength

        //内容段落信息区
        BlockAreaPosition_Start = fs.Position;//预留段落数量
        int blockCount = subMeshCount + 1;
        Util.FileUtil.WriteData(fs, (UInt16)blockCount);
        for (int i = 0; i < blockCount; i++)
        {
            Util.FileUtil.WriteData(fs, (UInt32)0);//UInt32 blockStart
            Util.FileUtil.WriteData(fs, (UInt32)0);//UInt32 blockLength
        }

        //字符区
        StringAreaPosition_Start = fs.Position;//预留字符区
        Util.FileUtil.WriteData(fs, (UInt32)0);//UInt32 offset
        Util.FileUtil.WriteData(fs, (UInt16)0);//count

        //材质区

        //网格区
        MeshAreaPosition_Start = fs.Position;
        Util.FileUtil.WriteData(fs, (UInt16)stringDatas.IndexOf("MESH"));//解析函数名字符索引
        stringDatas.Add(meshName);
        Util.FileUtil.WriteData(fs, (UInt16)stringDatas.IndexOf(meshName));//网格名字符索引

        //vb
        Util.FileUtil.WriteData(fs, (UInt16)vbCount);//vb����
        VBMeshAreaPosition_Start = fs.Position;
        //默认vbCount为1
        //for (ushort i = 0; i < vbCount; i++)
        //{
        Util.FileUtil.WriteData(fs, (UInt32)0);//vbStart
        Util.FileUtil.WriteData(fs, (UInt32)0);//vbLength
        stringDatas.Add(vbDeclaration);
        Util.FileUtil.WriteData(fs, (UInt16)stringDatas.IndexOf(vbDeclaration));//vbDeclar
                                                                                //}

        //ib
        IBMeshAreaPosition_Start = fs.Position;
        Util.FileUtil.WriteData(fs, (UInt32)0);//ibStart
        Util.FileUtil.WriteData(fs, (UInt32)0);//ibLength



        //bounds
        mesh.RecalculateBounds();
        Bounds bound = mesh.bounds;
        Util.FileUtil.WriteData(fs, -(float)bound.max.x);
        Util.FileUtil.WriteData(fs, (float)bound.min.y);
        Util.FileUtil.WriteData(fs, (float)bound.min.z);
        Util.FileUtil.WriteData(fs, -(float)bound.min.x);
        Util.FileUtil.WriteData(fs, (float)bound.max.y);
        Util.FileUtil.WriteData(fs, (float)bound.max.z);

        BoneAreaPosition_Start = fs.Position;
        //uint16 boneCount
        Util.FileUtil.WriteData(fs, (UInt16)bones.Count);//boneCount

        for (int i = 0; i < bones.Count; i++)
        {
            stringDatas.Add(bones[i].name);
            Util.FileUtil.WriteData(fs, (UInt16)stringDatas.IndexOf((bones[i].name)));
        }

        Util.FileUtil.WriteData(fs, (UInt32)0);//inverseGlobalBindPoseStart
        Util.FileUtil.WriteData(fs, (UInt32)0);//inverseGlobalBindPoseLength

        MeshAreaPosition_End = fs.Position;
        MeshAreaSize = MeshAreaPosition_End - MeshAreaPosition_Start;

        //子网格区
        for (int i = 0; i < subMeshCount; i++)
        {
            subMeshAreaPosition_Start[i] = fs.Position;

            Util.FileUtil.WriteData(fs, (UInt16)stringDatas.IndexOf("SUBMESH"));//解析函数名字符索引
            Util.FileUtil.WriteData(fs, (UInt16)0);//vbIndex

            Util.FileUtil.WriteData(fs, (UInt32)0);//ibStart
            Util.FileUtil.WriteData(fs, (UInt32)0);//ibLength

            Util.FileUtil.WriteData(fs, (UInt16)boneIndexList[i].Count);//drawCount

            for (int j = 0; j < boneIndexList[i].Count; j++)
            {
                Util.FileUtil.WriteData(fs, (UInt32)0);//ibStart
                Util.FileUtil.WriteData(fs, (UInt32)0);//ibLength

                Util.FileUtil.WriteData(fs, (UInt32)0);//boneDicStart
                Util.FileUtil.WriteData(fs, (UInt32)0);//boneDicLength
            }

            subMeshAreaPosition_End[i] = fs.Position;

            subMeshAreaSize[i] = subMeshAreaPosition_End[i] - subMeshAreaPosition_Start[i];
        }


        //字符数据区
        StringDatasAreaPosition_Start = fs.Position;

        for (int i = 0; i < stringDatas.Count; i++)
        {
            Util.FileUtil.WriteData(fs, stringDatas[i]);
        }
        StringDatasAreaPosition_End = fs.Position;
        StringDatasAreaSize = StringDatasAreaPosition_End - StringDatasAreaPosition_Start;

        //内容数据区
        //vb
        VBContentDatasAreaPosition_Start = fs.Position;

        VertexData vertexData;
        for (int j = 0; j < vertexBuffer.Count; j++)
        {
            vertexData = vertexBuffer[j];
            Vector3 _vertice = vertexData.vertice;
            Util.FileUtil.WriteData(fs, _vertice.x * -1.0f, _vertice.y, _vertice.z);

            if (VertexStructure[1] == 1)
            {
                Vector3 _normal = vertexData.normal;
                Util.FileUtil.WriteData(fs, _normal.x * -1.0f, _normal.y, _normal.z);
            }

            if (VertexStructure[2] == 1)
            {
                Color _color = vertexData.color;
                Util.FileUtil.WriteData(fs, _color.r, _color.g, _color.b, _color.a);
            }

            if (VertexStructure[3] == 1)
            {
                Vector2 _uv = vertexData.uv;
                Util.FileUtil.WriteData(fs, _uv.x, -_uv.y + 1.0f);
            }

            if (VertexStructure[4] == 1)
            {
                Vector2 _uv2 = vertexData.uv2;
                Util.FileUtil.WriteData(fs, _uv2.x, -_uv2.y + 1.0f);
            }

            if (VertexStructure[5] == 1)
            {
                Vector4 _boneWeight = vertexData.boneWeight;
                Vector4 _boneIndex = vertexData.boneIndex;
                Util.FileUtil.WriteData(fs, _boneWeight.x, _boneWeight.y, _boneWeight.z, _boneWeight.w);
                Util.FileUtil.WriteData(fs, (byte)_boneIndex.x, (byte)_boneIndex.y, (byte)_boneIndex.z, (byte)_boneIndex.w);
            }

            if (VertexStructure[6] == 1)
            {
                Vector4 _tangent = vertexData.tangent;
                Util.FileUtil.WriteData(fs, _tangent.x * -1.0f, _tangent.y, _tangent.z, _tangent.w);
            }
        }



        VBContentDatasAreaPosition_End = fs.Position;
        VBContentDatasAreaSize = VBContentDatasAreaPosition_End - VBContentDatasAreaPosition_Start;


        //indices
        //TODO:未来加入标记存入lm内
        IBContentDatasAreaPosition_Start = fs.Position;
        if (mesh.indexFormat == IndexFormat.UInt32 && vertexBuffer.Count > 65535)
        {
            for (int j = 0; j < indexBuffer.Count; j++)
            {
                Util.FileUtil.WriteData(fs, (UInt32)indexBuffer[j]);
            }
        }
        else
        {
            for (int j = 0; j < indexBuffer.Count; j++)
            {
                Util.FileUtil.WriteData(fs, (UInt16)indexBuffer[j]);
            }
        }

        IBContentDatasAreaPosition_End = fs.Position;
        IBContentDatasAreaSize = IBContentDatasAreaPosition_End - IBContentDatasAreaPosition_Start;

        if (mesh.bindposes != null && mesh.bindposes.Length != 0)
        {
            Matrix4x4[] matrix = new Matrix4x4[mesh.bindposes.Length];
            Vector3 position;
            Quaternion quaternion;
            Vector3 scale;
            for (int i = 0; i < mesh.bindposes.Length; i++)
            {
                matrix[i] = mesh.bindposes[i];
                matrix[i] = matrix[i].inverse;
                Util.MathUtil.Decompose(matrix[i].transpose, out scale, out quaternion, out position);
                position.x *= -1.0f;
                quaternion.x *= -1.0f;
                quaternion.w *= -1.0f;
                quaternion.Normalize();
                matrix[i] = Matrix4x4.TRS(position, quaternion, scale);
            }

            //inverseGlobalBindPoses

            inverseGlobalBindPosesDatasAreaPosition_Start = fs.Position;

            for (int i = 0; i < mesh.bindposes.Length; i++)
            {
                Matrix4x4 m4 = matrix[i].inverse;
                for (int j = 0; j < 16; j++)
                {
                    Util.FileUtil.WriteData(fs, m4[j]);

                }
            }

            //boneDic

            boneDicDatasAreaPosition_Start = fs.Position;

            for (int i = 0; i < subMeshCount; i++)
            {
                for (int j = 0; j < boneIndexList[i].Count; j++)
                {
                    for (int k = 0; k < boneIndexList[i][j].Count; k++)
                    {
                        Util.FileUtil.WriteData(fs, (ushort)boneIndexList[i][j][k]);
                    }
                }
            }
            boneDicDatasAreaPosition_End = fs.Position;
        }

        //倒推子网格区

        UInt32 ibstart = 0;
        UInt32 iblength = 0;
        UInt32 _ibstart = 0;
        long boneDicStart = boneDicDatasAreaPosition_Start - StringDatasAreaPosition_Start;
        for (int i = 0; i < subMeshCount; i++)
        {
            fs.Position = subMeshAreaPosition_Start[i] + 4;

            if (subMeshCount == 1)
            {
                ibstart = 0;
                iblength = mesh.indexFormat == IndexFormat.UInt32 ? (UInt32)(IBContentDatasAreaSize / 4) : (UInt32)(IBContentDatasAreaSize / 2);
            }
            else if (i == 0)
            {
                ibstart = _ibstart;
                iblength = (UInt32)(subMeshIndexLength[i]);
            }
            else if (i < subMeshCount - 1)
            {
                ibstart = (UInt32)(_ibstart);
                iblength = (UInt32)(subMeshIndexLength[i]);
            }
            else
            {
                ibstart = (UInt32)(_ibstart);
                iblength = (UInt32)(subMeshIndexLength[i]);
            }

            Util.FileUtil.WriteData(fs, ibstart);
            Util.FileUtil.WriteData(fs, iblength);
            _ibstart += iblength;

            fs.Position += 2;

            int subIBStart = 0;
            for (int j = 0; j < boneIndexList[i].Count; j++)
            {
                Util.FileUtil.WriteData(fs, (UInt32)subIBStart + ibstart);//ibStart
                Util.FileUtil.WriteData(fs, (UInt32)(subIBIndex[i][j] - subIBStart));//ibLength
                subIBStart = subIBIndex[i][j];

                Util.FileUtil.WriteData(fs, (UInt32)boneDicStart);//boneDicStart
                Util.FileUtil.WriteData(fs, (UInt32)boneIndexList[i][j].Count * 2);//boneDicLength
                boneDicStart += boneIndexList[i][j].Count * 2;
            }
        }

        //倒推网格区
        fs.Position = VBMeshAreaPosition_Start;
        Util.FileUtil.WriteData(fs, (UInt32)(VBContentDatasAreaPosition_Start - StringDatasAreaPosition_Start));
        Util.FileUtil.WriteData(fs, (UInt32)vertexBuffer.Count);

        fs.Position = IBMeshAreaPosition_Start;
        Util.FileUtil.WriteData(fs, (UInt32)(IBContentDatasAreaPosition_Start - StringDatasAreaPosition_Start));
        Util.FileUtil.WriteData(fs, (UInt32)IBContentDatasAreaSize);

        fs.Position = BoneAreaPosition_Start + (bones.Count + 1) * 2;

        Util.FileUtil.WriteData(fs, (UInt32)(inverseGlobalBindPosesDatasAreaPosition_Start - StringDatasAreaPosition_Start));
        Util.FileUtil.WriteData(fs, (UInt32)(boneDicDatasAreaPosition_Start - inverseGlobalBindPosesDatasAreaPosition_Start));

        //倒推字符区
        fs.Position = StringAreaPosition_Start;
        Util.FileUtil.WriteData(fs, (UInt32)0);
        Util.FileUtil.WriteData(fs, (UInt16)stringDatas.Count);
        StringAreaPosition_End = fs.Position;

        //倒推段落区
        fs.Position = BlockAreaPosition_Start + 2;
        Util.FileUtil.WriteData(fs, (UInt32)MeshAreaPosition_Start);
        Util.FileUtil.WriteData(fs, (UInt32)MeshAreaSize);
        for (int i = 0; i < subMeshCount; i++)
        {
            Util.FileUtil.WriteData(fs, (UInt32)subMeshAreaPosition_Start[i]);
            Util.FileUtil.WriteData(fs, (UInt32)subMeshAreaSize[i]);
        }

        //倒推标记内容数据信息区
        fs.Position = ContentAreaPosition_Start;
        Util.FileUtil.WriteData(fs, (UInt32)StringDatasAreaPosition_Start);
        Util.FileUtil.WriteData(fs, (UInt32)(StringDatasAreaPosition_Start + StringDatasAreaSize + VBContentDatasAreaSize + IBContentDatasAreaSize + subMeshAreaSize[0]));

        fs.Close();
    }

    private static VertexData checkPoint(VertexData vertexdata, int subMeshindex, int subsubMeshIndex, List<VertexData> ListVertexData)
    {
        //第一次循环到这个点
        if (vertexdata.subMeshindex == -1 && vertexdata.subsubMeshindex == -1)
        {
            vertexdata.subMeshindex = subMeshindex;
            vertexdata.subsubMeshindex = subsubMeshIndex;
            return vertexdata;
        }//点在与第一次的点相同
        else if (vertexdata.subMeshindex == subMeshindex && vertexdata.subsubMeshindex == subsubMeshIndex)
        {
            return vertexdata;
        }
        //第一个重合点
        else if (vertexdata.commonPoint == null)
        {
            //有重合点就new dictionary
            vertexdata.commonPoint = new Dictionary<string, int>();
            VertexData newvertexdata = new VertexData();
            //添加新顶点
            ListVertexData.Add(newvertexdata);
            //复制顶点数据
            newvertexdata.setValue(vertexdata);
            //给新的Index
            newvertexdata.index = ListVertexData.Count - 1;
            vertexdata.commonPoint.Add(subMeshindex.ToString() + "," + subsubMeshIndex.ToString(), ListVertexData.Count - 1);
            return newvertexdata;
        }//已经有重合点后
        else
        {
            //若是已经有key的点
            if (vertexdata.commonPoint.ContainsKey(subMeshindex.ToString() + "," + subsubMeshIndex.ToString()))
            {
                return ListVertexData[vertexdata.commonPoint[subMeshindex.ToString() + "," + subsubMeshIndex.ToString()]];
            }//没有key,再加一个
            else
            {
                VertexData newvertexdata = new VertexData();
                //添加新顶点
                ListVertexData.Add(newvertexdata);
                //复制顶点数据
                newvertexdata.setValue(vertexdata);
                //给新的index
                newvertexdata.index = ListVertexData.Count - 1;
                vertexdata.commonPoint.Add(subMeshindex.ToString() + "," + subsubMeshIndex.ToString(), ListVertexData.Count - 1);
                return newvertexdata;
                //return vertexdata;
            }
        }
    }
    //获取一个三角形所有的骨骼
    private static List<int> triangleBoneIndex(Triangle triangle)
    {
        List<int> indexs = new List<int>();
        Vector4 v1 = triangle.point1.boneIndex;
        Vector4 v2 = triangle.point2.boneIndex;
        Vector4 v3 = triangle.point3.boneIndex;
        if (indexs.IndexOf((int)v1.x) == -1) indexs.Add((int)v1.x);
        if (indexs.IndexOf((int)v1.y) == -1) indexs.Add((int)v1.y);
        if (indexs.IndexOf((int)v1.z) == -1) indexs.Add((int)v1.z);
        if (indexs.IndexOf((int)v1.w) == -1) indexs.Add((int)v1.w);
        if (indexs.IndexOf((int)v2.x) == -1) indexs.Add((int)v2.x);
        if (indexs.IndexOf((int)v2.y) == -1) indexs.Add((int)v2.y);
        if (indexs.IndexOf((int)v2.z) == -1) indexs.Add((int)v2.z);
        if (indexs.IndexOf((int)v2.w) == -1) indexs.Add((int)v2.w);
        if (indexs.IndexOf((int)v3.x) == -1) indexs.Add((int)v3.x);
        if (indexs.IndexOf((int)v3.y) == -1) indexs.Add((int)v3.y);
        if (indexs.IndexOf((int)v3.z) == -1) indexs.Add((int)v3.z);
        if (indexs.IndexOf((int)v3.w) == -1) indexs.Add((int)v3.w);
        return indexs;
    }

    //两个list包含关系，如果返回0就全包含，如果返回不是0那就得多
    private static List<int> listContainCount(List<int> boneindex, List<int> subsubboneindexs)
    {
        List<int> containcount = new List<int>();
        for (int i = 0; i < boneindex.Count; i++)
        {
            if (subsubboneindexs.IndexOf(boneindex[i]) == -1)
            {
                containcount.Add(boneindex[i]);
            }
        }
        return containcount;
    }
    private static void changeBoneIndex(List<int> boneindexlist, VertexData vertexdata)
    {
        if (vertexdata.ischange)
        {
            for (int i = 0; i < 4; i++)
            {
                vertexdata.boneIndex[i] = (float)boneindexlist.IndexOf((int)vertexdata.boneIndex[i]);
            }
            vertexdata.ischange = false;
        }
    }

    private static VertexData getVertexData(Vector3[] vertices, Vector3[] normals, Color[] colors, Vector2[] uv, Vector2[] uv2, BoneWeight[] boneWeightsX, Vector4[] tangents, int index)
    {
        VertexData vertexData = new VertexData();

        vertexData.index = index;


        vertexData.vertice = vertices[index];


        if (VertexStructure[1] == 1)
        {
            vertexData.normal = normals[index];
        }
        else
        {
            vertexData.normal = new Vector3();
        }

        if (VertexStructure[2] == 1)
        {
            vertexData.color = colors[index];
        }
        else
        {
            vertexData.color = new Color();
        }

        if (VertexStructure[3] == 1)
        {
            vertexData.uv = uv[index];
        }
        else
        {
            vertexData.uv = new Vector2();
        }

        if (VertexStructure[4] == 1)
        {
            vertexData.uv2 = uv2[index];
        }
        else
        {
            vertexData.uv2 = new Vector2();
        }

        if (VertexStructure[5] == 1)
        {
            BoneWeight boneWeights = boneWeightsX[index];

            vertexData.boneWeight.x = boneWeights.weight0;
            vertexData.boneWeight.y = boneWeights.weight1;
            vertexData.boneWeight.z = boneWeights.weight2;
            vertexData.boneWeight.w = boneWeights.weight3;

            vertexData.boneIndex.x = boneWeights.boneIndex0;
            vertexData.boneIndex.y = boneWeights.boneIndex1;
            vertexData.boneIndex.z = boneWeights.boneIndex2;
            vertexData.boneIndex.w = boneWeights.boneIndex3;
        }
        else
        {
            vertexData.boneWeight = new Vector4();
            vertexData.boneIndex = new Vector4();
        }

        if (VertexStructure[6] == 1)
        {
            vertexData.tangent = tangents[index];
        }
        else
        {
            vertexData.tangent = new Vector4();
        }

        return vertexData;
    }
}
