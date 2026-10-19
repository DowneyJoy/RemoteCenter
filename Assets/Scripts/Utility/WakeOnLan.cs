using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using UnityEngine;

public static class WakeOnLan
{
    /// <summary>
    /// 发送 Wake-on-LAN 魔术包（遍历所有网卡，向各子网广播地址发送）
    /// </summary>
    /// <param name="macString">MAC地址，格式如 "58-11-22-9B-E5-A6"</param>
    /// <param name="port">目标端口，默认 9</param>
    public static void WakeUp(string macString, int port = 9)
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
                macBytes[i] = Convert.ToByte(macParts[i], 16);

            // 构建魔术包：6 字节 0xFF + 16 遍 MAC（共 102 字节）
            byte[] packet = new byte[17 * 6];
            for (int i = 0; i < 6; i++)
                packet[i] = 0xFF;

            for (int i = 1; i <= 16; i++)
                for (int j = 0; j < 6; j++)
                    packet[i * 6 + j] = macBytes[j];

            bool sentAny = false;

            // ── ① 遍历所有网卡，向各自的子网广播地址发送 ──
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            
                var props = nic.GetIPProperties();
                if (props == null) continue;
            
                foreach (var addr in props.UnicastAddresses)
                {
                    if (addr.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    if (IPAddress.IsLoopback(addr.Address)) continue;
                    if (addr.IPv4Mask == null) continue;
            
                    // 计算子网广播地址
                    byte[] ip = addr.Address.GetAddressBytes();
                    byte[] mask = addr.IPv4Mask.GetAddressBytes();
                    byte[] broadcastBytes = new byte[4];
                    for (int i = 0; i < 4; i++)
                        broadcastBytes[i] = (byte)(ip[i] | (~mask[i] & 0xFF));
            
                    IPAddress subnetBroadcast = new IPAddress(broadcastBytes);
            
                    if (SendPacket(packet, subnetBroadcast, port))
                    {
                        Debug.Log($"[WoL] 已发送 → {subnetBroadcast} (网卡: {nic.Name})");
                        sentAny = true;
                    }
                }
            }

            // ── ② 兜底：向全局广播地址发送 ──
            if (SendPacket(packet, IPAddress.Broadcast, port))
            {
                Debug.Log($"[WoL] 已发送 → {IPAddress.Broadcast} (全局广播)");
                sentAny = true;
            }

            if (!sentAny)
                Debug.LogWarning("[WoL] 未找到可用的网络接口");
            else
                Debug.Log($"[WoL] 魔术包发送完成: MAC={macString}, 端口={port}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[WoL] 网络唤起异常: {ex}");
        }
    }

    /// <summary>
    /// 向指定地址发送魔术包
    /// </summary>
    private static bool SendPacket(byte[] packet, IPAddress target, int port)
    {
        try
        {
            using (UdpClient client = new UdpClient())
            {
                client.EnableBroadcast = true;
                client.Send(packet, packet.Length, new IPEndPoint(target, port));
            }
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[WoL] 发送到 {target} 失败: {e.Message}");
            return false;
        }
    }
}