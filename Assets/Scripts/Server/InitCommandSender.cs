using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace RemoteCenter
{
    /// UDP 初始化命令发送器（服务端）：向指定 IP 单发或向局域网广播本机服务端地址。
    public static class InitCommandSender
    {
        const int RepeatCount = 3;      // UDP 不可靠，重复发送提高到达率
        const int RepeatIntervalMs = 300;

        /// 取本机最可能的局域网 IPv4。
        /// 优先级：带默认网关的以太网/无线（真实物理网卡）> 其他以太网/无线 > 其他可用网卡；
        /// 排除回环、隧道与 169.254 自分配地址，避免虚拟网卡（VPN/TAP/虚拟交换机）被误选。
        public static string GetPrimaryLanIpv4()
        {
            string preferred = null;
            string fallback = null;
            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces();
                foreach (var nic in interfaces)
                {
                    if (!IsPreferredNic(nic) || !HasGateway(nic)) continue;
                    string ip = TryGetIpv4(nic);
                    if (ip != null) return ip;
                }
                foreach (var nic in interfaces)
                {
                    if (!IsPreferredNic(nic)) continue;
                    string ip = TryGetIpv4(nic);
                    if (ip != null && preferred == null) preferred = ip;
                }
                foreach (var nic in interfaces)
                {
                    string ip = TryGetIpv4(nic);
                    if (ip != null && fallback == null) fallback = ip;
                }
            }
            catch { }
            return preferred ?? fallback ?? "127.0.0.1";
        }

        static bool HasGateway(NetworkInterface nic)
        {
            try
            {
                foreach (var gateway in nic.GetIPProperties().GatewayAddresses)
                {
                    if (gateway.Address == null) continue;
                    if (gateway.Address.Equals(IPAddress.Any)) continue;
                    return true;
                }
            }
            catch { }
            return false;
        }

        static bool IsPreferredNic(NetworkInterface nic)
        {
            if (nic == null) return false;
            if (nic.OperationalStatus != OperationalStatus.Up) return false;
            var type = nic.NetworkInterfaceType;
            return type == NetworkInterfaceType.Ethernet || type == NetworkInterfaceType.Wireless80211;
        }

        static string TryGetIpv4(NetworkInterface nic)
        {
            try
            {
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) return null;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel) return null;
                foreach (var address in nic.GetIPProperties().UnicastAddresses)
                {
                    if (address.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    string ip = address.Address.ToString();
                    if (ip.StartsWith("169.254.")) continue;
                    return ip;
                }
            }
            catch { }
            return null;
        }

        /// 向指定 IP 单发初始化命令。
        public static void SendUnicast(string serverIp, string targetIp)
        {
            Send(serverIp, targetIp, false);
        }

        /// 向局域网广播初始化命令。
        public static void SendBroadcast(string serverIp)
        {
            Send(serverIp, null, true);
        }

        static void Send(string serverIp, string targetIp, bool broadcast)
        {
            try
            {
                var message = new InitMessage { type = "init", serverIp = serverIp, port = VideoTransferProtocol.Port };
                byte[] payload = Encoding.UTF8.GetBytes(JsonUtility.ToJson(message));
                var target = broadcast
                    ? new IPEndPoint(IPAddress.Broadcast, VideoTransferProtocol.InitPort)
                    : new IPEndPoint(IPAddress.Parse(targetIp), VideoTransferProtocol.InitPort);

                using (var udp = new UdpClient())
                {
                    udp.EnableBroadcast = true;
                    for (int i = 0; i < RepeatCount; i++)
                    {
                        udp.Send(payload, payload.Length, target);
                        if (i < RepeatCount - 1) Thread.Sleep(RepeatIntervalMs);
                    }
                }
                Debug.Log($"[InitCommandSender] 已{(broadcast ? "广播" : $"发送到 {targetIp}")}初始化命令（服务端 IP: {serverIp}，x{RepeatCount}）");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[InitCommandSender] 初始化命令发送失败: " + ex.Message);
            }
        }
        /// <summary>
    /// 获取本机局域网 IPv4：优先 192.168.x.x，找不到回退通用检测，仍找不到返回 null
    /// </summary>
    public static string GetLocal192168Ipv4()
    {
        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();

            // 第一轮：优先物理网卡（以太网 / Wi-Fi）上的 192.168 地址
            foreach (var nic in interfaces)
            {
                if (!IsPreferredNicTemp(nic)) continue;
                string ip = TryGet192168Ipv4(nic);
                if (ip != null) return ip;
            }

            // 第二轮：其他网卡上的 192.168 地址
            foreach (var nic in interfaces)
            {
                string ip = TryGet192168Ipv4(nic);
                if (ip != null) return ip;
            }
        }
        catch { }

        // 回退：通用局域网检测（任意网段，带网关的物理网卡优先）
        return GetPrimaryLanIpv4();
    }

    /// <summary>
    /// 是否为优先网卡（有线或无线，且处于启用状态）
    /// </summary>
    private static bool IsPreferredNicTemp(NetworkInterface nic)
    {
        if (nic == null) return false;
        if (nic.OperationalStatus != OperationalStatus.Up) return false;

        switch (nic.NetworkInterfaceType)
        {
            case NetworkInterfaceType.Ethernet:
            case NetworkInterfaceType.GigabitEthernet:
            case NetworkInterfaceType.Wireless80211:
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// 尝试从指定网卡中取一个 192.168 开头的 IPv4
    /// </summary>
    private static string TryGet192168Ipv4(NetworkInterface nic)
    {
        if (nic == null) return null;
        if (nic.OperationalStatus != OperationalStatus.Up) return null;

        var props = nic.GetIPProperties();
        if (props == null) return null;

        foreach (var addr in props.UnicastAddresses)
        {
            if (addr == null || addr.Address == null) continue;
            if (addr.Address.AddressFamily != AddressFamily.InterNetwork) continue; // 只取 IPv4
            if (IPAddress.IsLoopback(addr.Address)) continue;                      // 排除回环

            string ip = addr.Address.ToString();
            if (ip.StartsWith("192.168."))
                return ip;
        }

        return null;
    }
    }
}
