using marijnz.EditorCoroutines;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;


struct ConfigInfo
{
    public string Study;
    public string LayaAsk;
}

public enum URLType
{
    StudyURL = 0,
    LayaAskURL = 1
}

public class ServeConfig 
{
    private static ServeConfig _instance;
    public static ServeConfig getInstance()
    {
        if (_instance == null)
        {
            _instance = new ServeConfig();
        }
        return _instance;
    }
    private ConfigInfo _getConfig;
    private bool _isGetConfig = false;
    public IEnumerator initConfig(Action ac)
    {
        string url = "https://ldc-1251285021.cos.ap-shanghai.myqcloud.com/layaair/unity/ExportPlugin.conf";
        UnityWebRequest request = UnityWebRequest.Get(url);
        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            string json = request.downloadHandler.text;
            this._getConfig = JsonUtility.FromJson<ConfigInfo>(json);
            /*  _layaAskURL = this._getConfig.LayaAsk;
              _studyURL = this._getConfig.Study;*/
            if (ac != null)
            {
                ac();
            }
        }
        else
        {
            Debug.Log("Error: " + request.error);
        }
    }
    public void openurl(URLType type)
    {
        if (this._isGetConfig)
        {
            this._openUrl(type);
        }
        else
        {
            EditorCoroutines.StartCoroutine(initConfig(() => { this._openUrl(type); }), this);
        }
    }
    private void _openUrl(URLType type)
    {
        if (type == URLType.LayaAskURL)
        {
            Application.OpenURL(this._getConfig.Study);
        }else
        {
            Application.OpenURL(this._getConfig.LayaAsk);
        }
    }
}
