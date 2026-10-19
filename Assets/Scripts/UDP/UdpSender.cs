using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

public class UdpSender
{
    public static void WakeUp(string macString)//发送 物理地址 “FF-FF-FF-FF-FF-FF”格式
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

                client.Connect(IPAddress.Broadcast, 9);
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
    public static void SendUDP(string targetIp, int port, string message)
    {
        // 验证输入参数
        if (string.IsNullOrEmpty(targetIp) ||
            port < 1 || port > 65535 ||
            string.IsNullOrEmpty(message))
        {
            throw new ArgumentException("无效的输入参数");
        }

        using UdpClient udpClient = new UdpClient();
        try
        {
            // 创建目标终结点
            IPEndPoint targetEndPoint = new IPEndPoint(IPAddress.Parse(targetIp), port);
            
            // 将消息转换为字节数组
            byte[] dataBytes = Encoding.UTF8.GetBytes(message);
            
            // 发送UDP数据包
            int bytesSent = udpClient.Send(dataBytes, dataBytes.Length, targetEndPoint);
            
            Debug.Log($"成功发送 {bytesSent} 字节到 {targetIp}:{port}");
        }
        catch (SocketException ex)  // 处理网络相关异常[^1][^2][^3]
        {
            switch (ex.SocketErrorCode)
            {
                case SocketError.HostUnreachable:
                    Debug.Log($"错误: 目标主机不可达 ({targetIp})");
                    break;
                case SocketError.AccessDenied:
                    Debug.Log("错误: 访问被拒绝 (请检查防火墙设置)");
                    break;
                case SocketError.NetworkUnreachable:
                    Debug.Log("错误: 网络不可达");
                    break;
                case SocketError.HostNotFound:
                    Debug.Log("错误: 无法解析主机名或地址");
                    break;
                default:
                    Debug.Log($"网络错误 ({ex.SocketErrorCode}): {ex.Message}");
                    break;
            }
        }
        catch (FormatException)  // IP地址格式错误
        {
            Debug.Log($"错误: 无效的IP地址格式 '{targetIp}'");
        }
        catch (Exception ex)  // 其他异常
        {
            Debug.Log($"未知错误: {ex.Message}");
        }
    }

    // 使用示例
    public void Main()
    {
        try
        {
            // 发送消息到指定IP和端口
            SendUDP("192.168.1.100", 8080, "Hello UDP Server!");
        }
        catch (ArgumentException ex)
        {
            Debug.Log($"参数错误: {ex.Message}");
        }
    }
}
