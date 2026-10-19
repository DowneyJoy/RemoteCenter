using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using LitJson;
using UnityEngine;

public class ConfigManger
{
    /// <summary>
    /// 读取json文件
    /// </summary>
    /// <param name="configPath"></param>
    /// <returns></returns>
    public static string LoadInfoString(string configPath)
    {
        return File.ReadAllText(Application.streamingAssetsPath+"/"+configPath);;
    }
    
    /// <summary>
    /// 读取系统信息
    /// </summary>
    /// <param name="configPath"></param>
    /// <returns></returns>
    public static CustomSystemInfo LoadInfo(string configPath)
    {
        var ta = LoadInfoString(configPath);
        CustomSystemInfo csi = JsonMapper.ToObject<CustomSystemInfo>(ta);
        return csi;
    }
}
[Serializable]
public class CustomSystemInfo
{
    public int canDebug;
    public int timeoutDuration;
}
