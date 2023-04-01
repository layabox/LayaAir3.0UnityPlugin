using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Util
{
    internal class FileUtil
    {
        static bool ExportStatuse = true;
        //存储文件
        public static FileStream saveFile(string fileName, JSONObject node = null)
        {
            string folder = Path.GetDirectoryName(fileName);
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            FileStream fs = new FileStream(fileName, FileMode.Create, FileAccess.Write);

            if (node == null)
                return fs;

            StreamWriter writer = new StreamWriter(fs);
            string encodedString = node.Print(true);
            writer.Write(encodedString);
            writer.Close();

            return fs;
        }

        static public void setStatuse(bool statuse)
        {
            FileUtil.ExportStatuse = statuse;
        }

        static public bool getStatuse()
        {
            return FileUtil.ExportStatuse;
        }

        public static FileStream saveFile(string fileName)
        {
            string folder = Path.GetDirectoryName(fileName);
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            FileStream fs = new FileStream(fileName, FileMode.OpenOrCreate, FileAccess.Write);

            return fs;
        }

        //获取path2对于path1的相对路径,自己使用，不做非法判断
        public static string getRelativePath(string path1, string path2)
        {
            String relativePath = "";
            String[] pathArr1 = path1.Split('/');
            String[] pathArr2 = path2.Split('/');
            int equalNum = 0;

            for (int i = 0; i < pathArr1.Length - 1; i++)
            {
                if (pathArr1[i] != pathArr2[i])
                    break;
                equalNum++;
            }

            for (int j = 0; j < pathArr1.Length - equalNum - 1; j++)
            {
                relativePath += "../";
            }

            for (int j = equalNum; j < pathArr2.Length; j++)
            {
                relativePath += pathArr2[j];
                if (j < pathArr2.Length - 1)
                    relativePath += "/";
            }

            return relativePath;
        }

        public static void WriteData(FileStream fs, params Int32[] datas)
        {
            foreach (Int32 data in datas)
            {
                byte[] blockAddressIndexBytes = BitConverter.GetBytes(data);
                fs.Write(blockAddressIndexBytes, 0, blockAddressIndexBytes.Length);
            }
        }

        private static void WriteData(FileStream fs, params long[] datas)
        {
            foreach (long data in datas)
            {
                byte[] blockAddressIndexBytes = BitConverter.GetBytes(data);
                fs.Write(blockAddressIndexBytes, 0, blockAddressIndexBytes.Length);
            }
        }

        public static void WriteData(FileStream fs, params byte[] datas)
        {
            foreach (byte data in datas)
            {
                fs.WriteByte(data);
            }
        }

        public static void WriteData(FileStream fs, params ushort[] datas)
        {
            foreach (ushort data in datas)
            {
                byte[] blockAddressIndexBytes = BitConverter.GetBytes(data);
                fs.Write(blockAddressIndexBytes, 0, blockAddressIndexBytes.Length);
            }
        }

        public static void WriteData(FileStream fs, params short[] datas)
        {
            foreach (short data in datas)
            {
                byte[] blockAddressIndexBytes = BitConverter.GetBytes(data);
                fs.Write(blockAddressIndexBytes, 0, blockAddressIndexBytes.Length);
            }
        }

        public static void WriteData(FileStream fs, params uint[] datas)
        {
            foreach (uint data in datas)
            {
                byte[] blockAddressIndexBytes = BitConverter.GetBytes(data);
                fs.Write(blockAddressIndexBytes, 0, blockAddressIndexBytes.Length);
            }
        }

        private static void WriteData(FileStream fs, params sbyte[] datas)
        {
            //byte[] bs = (byte[])(Array)datas;
            BinaryWriter s = new BinaryWriter(fs);

            foreach (sbyte data in datas)
            {
                //fs.WriteByte(data);
                s.Write(data);
            }
        }

        public static void WriteData(FileStream fs, params float[] datas)
        {
            foreach (float data in datas)
            {
                byte[] blockAddressIndexBytes = BitConverter.GetBytes(data);
                fs.Write(blockAddressIndexBytes, 0, blockAddressIndexBytes.Length);
            }
        }

        private static void WriteData(FileStream fs, params double[] datas)
        {
            foreach (double data in datas)
            {
                byte[] blockAddressIndexBytes = BitConverter.GetBytes(data);
                fs.Write(blockAddressIndexBytes, 0, blockAddressIndexBytes.Length);
            }
        }

        public static void WriteData(FileStream fs, params bool[] datas)
        {
            foreach (bool data in datas)
            {
                byte[] blockAddressIndexBytes = BitConverter.GetBytes(data);
                fs.Write(blockAddressIndexBytes, 0, blockAddressIndexBytes.Length);
            }
        }

        public static void WriteData(FileStream fs, string data)
        {
            byte[] blockAddressIndexBytes = UTF8Encoding.UTF8.GetBytes(data);
            short blocklength = (short)blockAddressIndexBytes.Length;
            WriteData(fs, blocklength);
            fs.Write(blockAddressIndexBytes, 0, blockAddressIndexBytes.Length);
        }

        public static void FileStreamLoadTexture(string url, Texture2D texture)
        {
            //通过路径加载本地图片
            FileStream fs = new FileStream(url, FileMode.Open);
            byte[] buffer = new byte[fs.Length];
            fs.Read(buffer, 0, buffer.Length);
            fs.Close();
            bool iSLoad = texture.LoadImage(buffer);
            texture.Apply();
            if (!iSLoad)
            {
                Debug.Log("Texture存在但生成Texture失败");
            }
        }

        static string GetPath(string _scriptName)
        {
            string[] path = UnityEditor.AssetDatabase.FindAssets(_scriptName);
            if(path.Length>1)
            {
                FileUtil.setStatuse(false);
                Debug.LogError("File Name Clash"+_scriptName+"Get Path ERROR!!");
                return null;
            }
            string _path = AssetDatabase.GUIDToAssetPath(path[0]).Replace((@"Export" + @"/"+_scriptName+".cs"),"");
            return _path;
        }

        public static string getPluginResUrl(string url)
        {
            string rootPath = GetPath("LanguageConfig");
            return rootPath + url;
        }

    }
}
