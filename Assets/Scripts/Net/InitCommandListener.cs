using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace RemoteCenter
{
    /// UDP 初始化命令监听器（客户端）：在 InitPort 上等待服务端下发的服务端 IP。
    /// 事件在监听线程触发，UI 层需自行切回主线程。
    public sealed class InitCommandListener : IDisposable
    {
        readonly int _port;
        Thread _thread;
        UdpClient _udp;
        volatile bool _running;

#if UNITY_ANDROID && !UNITY_EDITOR
        static AndroidJavaObject _multicastLock;
#endif

        public event Action<string, int> ServerInfoReceived; // 服务端 IP, TCP 端口
        public event Action<string> Error;

        public InitCommandListener(int port)
        {
            _port = port;
        }

        public void Start()
        {
            if (_running) return;
            _running = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "InitCommandListener" };
            _thread.Start();
        }

        public void Dispose()
        {
            _running = false;
            try { _udp?.Close(); } catch { }
            try { _thread?.Join(400); } catch { }
        }

        void Loop()
        {
            try
            {
                TryAcquireMulticastLock();
                _udp = new UdpClient(new IPEndPoint(IPAddress.Any, _port));
            }
            catch (Exception ex)
            {
                // 端口被占用（例如本机同时跑了多个客户端）时仅提示，不影响其他功能
                Error?.Invoke("初始化命令监听启动失败: " + ex.Message);
                return;
            }

            var remote = new IPEndPoint(IPAddress.Any, 0);
            while (_running)
            {
                byte[] data;
                try { data = _udp.Receive(ref remote); }
                catch (SocketException) { break; }
                catch (ObjectDisposedException) { break; }

                if (data == null || data.Length == 0) continue;
                try
                {
                    var message = JsonUtility.FromJson<InitMessage>(Encoding.UTF8.GetString(data));
                    int port = message.port > 0 ? message.port : VideoTransferProtocol.Port;
                    if (message != null && message.type == "init" && !string.IsNullOrEmpty(message.serverIp))
                    {
                        ServerInfoReceived?.Invoke(message.serverIp, port);
                    }
                }
                catch { }
            }
        }

        /// Android 部分 Wi-Fi 节能策略会丢弃 UDP 广播/组播，申请 MulticastLock 尽力规避（失败可忽略）。
        static void TryAcquireMulticastLock()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                if (_multicastLock != null) return;
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    var wifi = activity.Call<AndroidJavaObject>("getSystemService", "wifi");
                    if (wifi == null) return;
                    var multicastLock = wifi.Call<AndroidJavaObject>("createMulticastLock", "remote_center_init");
                    if (multicastLock == null) return;
                    multicastLock.Call("setReferenceCounted", false);
                    multicastLock.Call("acquire");
                    _multicastLock = multicastLock; // 静态持有，进程存活期间保持
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[InitCommandListener] MulticastLock 申请失败: " + ex.Message);
            }
#endif
        }
    }
}
