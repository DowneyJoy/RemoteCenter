using UnityEngine;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

public class UDPReceiver : MonoBehaviour {
    public int port = 8848; // 监听端口
    private UdpClient udpClient;
    private Thread receiveThread;
    private bool isReceiving = true;

    void Start() {
        udpClient = new UdpClient(port);
        udpClient.Client.ReceiveTimeout = 1000;
        receiveThread = new Thread(new ThreadStart(ReceiveData));
        receiveThread.Start();
    }

    void ReceiveData() {
        IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
        while (isReceiving) {
            try {
                byte[] data = udpClient.Receive(ref remoteEP);
                string message = Encoding.UTF8.GetString(data);
                Debug.Log($"来自 {remoteEP.Address}:{remoteEP.Port} 的消息: {message}");
                HandleData(message);
            } catch (SocketException e) {
                if (e.SocketErrorCode != SocketError.TimedOut)
                    Debug.LogError($"接收错误: {e}");
            }
        }
    }

    private void HandleData(string message)
    {
        if (message.StartsWith("Event|"))
        {
            var divs = message.Split('|');
            if(divs.Length <= 1)
                return;
            var method = divs[1];
            Loom.QueueOnMainThread(() =>
            {
                if (method == "seekvideo")
                {
                    float.TryParse(message.Split('|')[2],out float value);
                    EventDispatcher.Instance.Dispatch(method,value);
                }
                else
                {
                    EventDispatcher.Instance.Dispatch(method);
                }
            });
        }
    }

    void OnDestroy() {
        isReceiving = false;
        if (receiveThread != null && receiveThread.IsAlive)
            receiveThread.Join();
        udpClient.Close();
    }
}