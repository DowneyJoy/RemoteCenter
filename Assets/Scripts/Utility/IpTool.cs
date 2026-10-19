using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using UnityEngine;

public class IpTool : MonoBehaviour
{
    void Start()
    {
        Debug.Log("获取本机IPv4：" + GetLocalIPAddress());
        ////Debug.Log("获取本机IPv4：" + GetLocalIPv4());
        //Debug.Log("获取本机IPv6：" + GetLocalIPv6());
        //Debug.Log("获取外网Ip：" + GetExternalNetIp());

        //Debug.Log("获取指定域名www.baidu.com的Ip信息：");
        //GetOtherNetIP("www.baidu.com");

        //Debug.Log("获取设备名称：" + GetDeviceName());
        //Debug.Log("获取设备模型：" + GetDeviceModel());
        //Debug.Log("获取设备唯一标识码：" + GetDeviceUniqueIdentifier());


    }
    public string GetLocalIPAddress()
    {
        string ip = "";
        try
        {
            // 获取所有网络接口
            IPAddress[] addresses = Dns.GetHostAddresses(Dns.GetHostName());
            foreach (IPAddress address in addresses)
            {
                // 过滤IPv4地址
                if (address.AddressFamily == AddressFamily.InterNetwork)
                {
                    ip = address.ToString();
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("获取IP地址失败: " + ex.Message);
        }
        return ip;
    }

    // 获取本机IPv4
    public string GetLocalIPv4()
    {
        string ipAddress = "";
        try
        {
            IPHostEntry host = Dns.GetHostEntry(Dns.GetHostName());
            //Debug.Log("GetHostName:" + Dns.GetHostName());
            
            //返回IP列表
            foreach (IPAddress ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    ipAddress = ip.ToString();
                    break;
                }
            }

            //获取主机别名列表
            for (int i = 0; i < host.Aliases.Length; i++)
            {
                //Debug.Log("主机别名：" + host.Aliases[i]);
            }


            //获取DNS名称
            //Debug.Log("DNS服务器名称" + host.HostName);



        }
        catch (Exception e)
        {
            Debug.LogError("IP 获取失败" +e.Message);
        }
        return ipAddress;
    }

    // 获取本机IPv6
    public string GetLocalIPv6()
    {
        string ipAddress = "";
        try
        {
            IPHostEntry host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (IPAddress ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    ipAddress = ip.ToString();
                    break;
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError("IP 获取失败" + e.Message);
        }
        return ipAddress;
    }

    //获取指定域名的Ip信息
    public string GetOtherNetIP(string ip)
    {
        string ipAddress = "";
        try
        {

            //获取域名的地址
            IPHostEntry entry = Dns.GetHostEntry(ip);

            for (int i = 0; i < entry.AddressList.Length; i++)
            {
                Debug.Log("IP地址：" + entry.AddressList[i]);
            }

            for (int i = 0; i < entry.Aliases.Length; i++)
            {
                Debug.Log("主机别名：" + entry.Aliases[i]);
            }


            Debug.Log("DNS服务器名称" + entry.HostName);





        }
        catch (Exception e)
        {
            Debug.LogError("IP 获取失败" + e.Message);
        }
        return ipAddress;
    }

    //获取外网Ip
    public string GetExternalNetIp()
    {
        string IP = string.Empty;
        try
        {
            //从网址中获取本机ip数据
            WebClient client = new WebClient();
            client.Encoding = System.Text.Encoding.Default;
            IP = client.DownloadString("http://checkip.amazonaws.com/");
            client.Dispose();
            IP = Regex.Replace(IP, @"[\r\n]", "");
        }
        catch (Exception e)
        {
            Debug.LogError("获取外网Ip" + e.Message);
        }

        return IP;
    }

    //获取设备名称
    public string GetDeviceName()
    {
        return SystemInfo.deviceName;
    }

    //获取设备模型
    public string GetDeviceModel()
    {
        return SystemInfo.deviceModel;
    }

    // 获取设备唯一标识码
    public string GetDeviceUniqueIdentifier()
    {
        return SystemInfo.deviceUniqueIdentifier;
    }



}


