using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

public class UDPUtility
{
    /// <summary>
    /// UDP发送字符串数据
    /// </summary>
    /// <param name="ip"></param>
    /// <param name="port"></param>
    /// <param name="_msg"></param>
    public static void SendUDP(string ip, int port, string _msg)
    {
        IPEndPoint remotePoint_he = new IPEndPoint(IPAddress.Parse(ip), port);//实例化一个远程端点 

        if (_msg != null)
        {
            byte[] sendData = Encoding.UTF8.GetBytes(_msg);  //网络调试助手中文乱码，应该是收发要统一编码的问题。先不管了。用英文。 
            UdpClient client = new UdpClient();
            client.Send(sendData, sendData.Length, remotePoint_he);//将数据发送到远程端点 
            Debug.Log("Sendudp:"+_msg+ ip);
            client.Close();//关闭连接
        }
    }
    /// <summary>
    /// UDP发送16进制数据
    /// </summary>
    /// <param name="ip"></param>
    /// <param name="port"></param>
    /// <param name="_msg"></param>
    public static void SendUDP_16(string ip, int port, string _msg)
    {
        IPEndPoint remotePoint_he = new IPEndPoint(IPAddress.Parse(ip), port);//实例化一个远程端点 

        byte[] sendData = textWork16(_msg); //调用函数，转为byte;
        if (sendData != null)
        {
            UdpClient client = new UdpClient();
            client.Send(sendData, sendData.Length, remotePoint_he);//将数据发送到远程端点 
            Debug.Log("Sendudp:" + _msg + ip);

            client.Close();//关闭连接
        }
    }
    /// <summary>
    /// 字符串转16进制
    /// </summary>
    /// <param name="strText"></param>
    /// <returns></returns>
    public static byte[] textWork16(string strText)
    {
        strText = strText.Replace(" ", "");//删除strText的所有空格
        byte[] bText = new byte[strText.Length / 2];//数组的大小是输入字符串 strText 长度的一半，因为每两个十六进制字符将被转换为一个字节
        for (int i = 0; i < strText.Length / 2; i++)
        {
            //十六进制到字节的转换
            //strText.Substring(i * 2, 2)：从strText字符串的i*2的位置开始提取两个字符
            //Convert.ToInt32(...,16)：将提取的十六进制字符串转换为整数(16时告诉方法输入的字符串是16进制数)
            //Convert.ToByte()：将上一步得到的整数转换为字节。并将其赋值给bText数组的当前位置i
            bText[i] = Convert.ToByte(Convert.ToInt32(strText.Substring(i * 2, 2), 16));
        }
        return bText;
    }

    /// <summary>
    /// 16进制转字符串 接收端用于显示
    /// </summary>
    /// <param name="data"></param>
    /// <returns></returns>
    public static string ByteArrayToHexStringNoBlank(byte[] data)
    {
        StringBuilder sb = new StringBuilder(data.Length * 3);
        foreach (byte b in data)
            sb.Append(Convert.ToString(b, 16).PadLeft(2, '0'));
        return sb.ToString().ToUpper();
    }
    /// <summary>
    /// 通过MAC地址唤醒主机
    /// </summary>
    /// <param name="macString"></param>
    public static void WakeUpOLD(string macString)//发送 物理地址 “FF-FF-FF-FF-FF-FF”格式
    {
        try
        {
            if (null != macString && macString.Split('-').Length == 6)
            {
                string[] macStringArray = macString.Split('-');
                byte[] macByteArray = new byte[6];  //转为byte；
                for (int i = 0; i < 6; i++)
                {
                    macByteArray[i] = Convert.ToByte(macStringArray[i], 16); 
                }
                UdpClient client = new UdpClient();
                client.Connect(IPAddress.Broadcast, 9090);
                byte[] packet = new byte[17 * 6];
                //写入6字节FF
                for (int i = 0; i < 6; i++)
                    packet[i] = 0xFF;
                //写入16遍mac地址
                for (int i = 1; i <= 16; i++)
                for (int j = 0; j < 6; j++)
                    packet[i * 6 + j] = macByteArray[j];
                int result = client.Send(packet, packet.Length);
                client.Close();
            }
        }
        catch (Exception ex)
        {
            Debug.Log("网络唤起指定计算机异常" + ex.ToString());
        }
    }
    /// <summary>
    /// 发送 Wake-on-LAN 魔术包唤醒局域网主机
    /// </summary>
    /// <param name="macString">MAC地址，格式如 "FF-FF-FF-FF-FF-FF"</param>
    /// <param name="port">目标端口，默认 9090</param>
    public static void WakeUp(string macString, int port = 9090)
    {
        if (string.IsNullOrEmpty(macString))
        {
            Debug.LogError("[WoL] MAC地址为空");
            return;
        }

        string[] macParts = macString.Split('-');
        if (macParts.Length != 6)
        {
            Debug.LogError($"[WoL] MAC地址格式错误: {macString}");
            return;
        }

        try
        {
            // 解析 MAC 地址
            byte[] macBytes = new byte[6];
            for (int i = 0; i < 6; i++)
            {
                macBytes[i] = Convert.ToByte(macParts[i], 16);
            }

            // 构建魔术包：6 字节 0xFF + 16 遍 MAC（共 102 字节）
            byte[] packet = new byte[17 * 6];
            for (int i = 0; i < 6; i++)
                packet[i] = 0xFF;

            for (int i = 1; i <= 16; i++)
            for (int j = 0; j < 6; j++)
                packet[i * 6 + j] = macBytes[j];

            // 发送广播
            using (UdpClient client = new UdpClient())
            {
                client.EnableBroadcast = true;
                IPEndPoint endPoint = new IPEndPoint(IPAddress.Broadcast, port);
                int sent = client.Send(packet, packet.Length, endPoint);
                Debug.Log($"[WoL] 魔术包已发送: MAC={macString}, 端口={port}, 字节数={sent}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[WoL] 网络唤起异常: {ex}");
        }
    }
}
