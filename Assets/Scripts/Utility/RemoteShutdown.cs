using System.Diagnostics;
using Debug = UnityEngine.Debug;

public static class RemoteShutdown
{
    public static void ShutdownTarget(string targetIP)
    {
        // -m \\IP 指定远程计算机
        // -s 关机
        // -f 强制关闭正在运行的应用程序
        // -t 0 设置超时为0秒
        string arguments = $"-m \\\\{targetIP} -s -f -t 0";

        ProcessStartInfo psi = new ProcessStartInfo
        {
            FileName = "shutdown",
            Arguments = arguments,
            CreateNoWindow = true, // 不显示命令提示符窗口
            UseShellExecute = false
        };

        try
        {
            Process.Start(psi);
            Debug.Log($"关机指令已发送至 {targetIP}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"发送关机指令失败: {e.Message}");
        }
    }
}