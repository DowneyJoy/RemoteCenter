using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace RemoteCenter
{
    /// TCP 服务端：监听客户端连接，并把视频或图片批次发送给全部/指定客户端。
    /// 事件均在后台线程触发，UI 层需自行切回主线程。
    public sealed class VideoServer : IDisposable
    {
        sealed class ClientConnection
        {
            public TcpClient Tcp;
            public string Address;
            public string Endpoint; // IP:端口，同一 IP 多个客户端时仍可区分

            public ClientConnection(TcpClient tcp)
            {
                Tcp = tcp;
                try
                {
                    var ep = (IPEndPoint)tcp.Client.RemoteEndPoint;
                    Address = ep.Address.ToString();
                    Endpoint = ep.ToString();
                }
                catch
                {
                    Address = "未知";
                    Endpoint = "未知";
                }
            }
        }

        sealed class FileBatch
        {
            public string MediaType;   // "video" 或 "image"
            public string[] Paths;
            public long[] Sizes;
            public long TotalBytes;
            public float Interval;     // 图片轮播间隔（秒）
            public string BatchId;     // 图片批次标识
        }

        readonly object _gate = new object();
        readonly List<ClientConnection> _clients = new List<ClientConnection>();
        TcpListener _listener;
        Thread _acceptThread;
        volatile bool _running;
        volatile bool _sending;

        public event Action<string> Log;
        public event Action<List<string>> ClientsChanged; // 当前连接端点（IP:端口）快照列表
        public event Action<long, long, string> SendProgress; // 已发送字节, 总字节, 当前目标
        public event Action<string> SendCompleted;

        public int Port { get; }

        public VideoServer(int port)
        {
            Port = port;
        }

        public void Start()
        {
            if (_running) return;
            _running = true;
            _listener = new TcpListener(IPAddress.Any, Port);
            _listener.Start();
            _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "VideoServer.Accept" };
            _acceptThread.Start();
        }

        public void Dispose()
        {
            _running = false;
            try { _listener?.Stop(); } catch { }
            lock (_gate)
            {
                foreach (var client in _clients)
                {
                    try { client.Tcp.Close(); } catch { }
                }
                _clients.Clear();
            }
        }

        /// 返回当前在线客户端的端点（IP:端口）快照，并顺带剔除已断开的连接。
        public List<string> GetClientEndpoints()
        {
            lock (_gate)
            {
                _clients.RemoveAll(c => !c.Tcp.Connected);
                var list = new List<string>(_clients.Count);
                foreach (var c in _clients) list.Add(c.Endpoint);
                return list;
            }
        }

        void NotifyClientsChanged()
        {
            ClientsChanged?.Invoke(GetClientEndpoints());
        }

        void AcceptLoop()
        {
            while (_running)
            {
                TcpClient tcp;
                try { tcp = _listener.AcceptTcpClient(); }
                catch { break; } // Stop() 关闭监听时退出
                if (!_running)
                {
                    try { tcp.Close(); } catch { }
                    break;
                }
                tcp.NoDelay = true;
                var conn = new ClientConnection(tcp);
                lock (_gate) _clients.Add(conn);
                Log?.Invoke($"客户端已连接: {conn.Endpoint}");
                NotifyClientsChanged();
            }
        }

        /// 发送单个视频给全部已连接客户端。
        public void SendToClients(string filePath)
        {
            StartSend(null, BuildVideoBatch(filePath));
        }

        /// 发送单个视频给指定端点客户端。
        public void SendToEndpoint(string endpoint, string filePath)
        {
            StartSend(endpoint, BuildVideoBatch(filePath));
        }

        /// 发送图片批次（1..N 张）给全部已连接客户端；interval 为轮播间隔秒数。
        public void SendImagesToClients(string[] filePaths, float interval)
        {
            StartSend(null, BuildImageBatch(filePaths, interval));
        }

        /// 发送图片批次给指定端点客户端。
        public void SendImagesToEndpoint(string endpoint, string[] filePaths, float interval)
        {
            StartSend(endpoint, BuildImageBatch(filePaths, interval));
        }
        
        /// 请求指定 IP 的所有已连接客户端断开（客户端收到命令后自行停止重连）。
        public void DisconnectClients(string addressIp)
        {
            if (_sending)
            {
                Log?.Invoke("已有传输进行中，请等待完成后再断开客户端");
                return;
            }
            var thread = new Thread(() => 
                {
                    List<ClientConnection> targets;
                    lock (_gate)
                    {
                        _clients.RemoveAll(c => !c.Tcp.Connected);
                        targets = new List<ClientConnection>(_clients.FindAll(c => c.Address == addressIp));
                    }
                    if (targets.Count == 0)
                    {
                        Log?.Invoke($"没有 IP 为 {addressIp} 的已连接客户端");
                        return;
                    }
                    foreach (var conn in targets) RequestDisconnect(conn);
                    Log?.Invoke($"已请求断开 {addressIp} 的 {targets.Count} 个客户端");
                })
                { IsBackground = true, Name = "VideoServer.Disconnect" };    
            thread.Start();
        }
        
        /// 请求所有已连接客户端断开。
        public void DisconnectAll()
        {
            if (_sending)
            {
                Log?.Invoke("已有传输进行中，请等待完成后再断开客户端");
                return;
            }
            var thread = new Thread(() =>
                {
                    List<ClientConnection> targets;
                    lock (_gate)
                    {
                        _clients.RemoveAll(c => !c.Tcp.Connected);
                        targets = new List<ClientConnection>(_clients);
                    }
                    if (targets.Count == 0)
                    {
                        Log?.Invoke("当前没有已连接的客户端");
                        return;
                    }
                    foreach (var conn in targets) RequestDisconnect(conn);
                    Log?.Invoke($"已请求断开全部 {targets.Count} 个客户端");
                })
                { IsBackground = true, Name = "VideoServer.DisconnectAll" };
            thread.Start();
        }
        
        void RequestDisconnect(ClientConnection conn)
        {
            try
            {
                var message = new VideoHeaderMessage { type = "disconnect", name = "", size = 0 };
                byte[] frame = VideoTransferProtocol.EncodeFrame(JsonUtility.ToJson(message));
                var stream = conn.Tcp.GetStream();
                stream.Write(frame, 0, frame.Length);
                stream.Flush();
                Thread.Sleep(200); // 留出客户端接收时间后再关闭
            }
            catch { }
            try { conn.Tcp.Close(); } catch { }
            lock (_gate) _clients.Remove(conn);
            NotifyClientsChanged();
        }

        static FileBatch BuildVideoBatch(string filePath)
        {
            var batch = new FileBatch
            {
                MediaType = "video",
                Paths = new[] { filePath },
                Sizes = new[] { new FileInfo(filePath).Length },
                Interval = 0f,
                BatchId = null
            };
            batch.TotalBytes = batch.Sizes[0];
            return batch;
        }

        static FileBatch BuildImageBatch(string[] filePaths, float interval)
        {
            var paths = new List<string>(filePaths);
            var sizes = new long[paths.Count];
            long total = 0;
            for (int i = 0; i < paths.Count; i++)
            {
                sizes[i] = new FileInfo(paths[i]).Length;
                total += sizes[i];
            }
            return new FileBatch
            {
                MediaType = "image",
                Paths = paths.ToArray(),
                Sizes = sizes,
                TotalBytes = total,
                Interval = interval,
                BatchId = DateTime.Now.ToString("yyyyMMddHHmmssfff")
            };
        }

        void StartSend(string endpointFilter, FileBatch batch)
        {
            if (_sending)
            {
                Log?.Invoke("已有传输进行中，忽略本次发送");
                return;
            }
            _sending = true;
            var thread = new Thread(() => SendLoop(endpointFilter, batch)) { IsBackground = true, Name = "VideoServer.Send" };
            thread.Start();
        }

        void SendLoop(string endpointFilter, FileBatch batch)
        {
            try
            {
                List<ClientConnection> targets;
                lock (_gate)
                {
                    _clients.RemoveAll(c => !c.Tcp.Connected);
                    if (endpointFilter == null) targets = new List<ClientConnection>(_clients);
                    else targets = new List<ClientConnection>(_clients.FindAll(c => c.Endpoint == endpointFilter));
                }
                if (targets.Count == 0)
                {
                    SendCompleted?.Invoke(endpointFilter == null
                        ? "没有已连接的客户端，发送取消"
                        : $"目标客户端 {endpointFilter} 已断开，发送取消");
                    return;
                }

                byte[] buffer = new byte[VideoTransferProtocol.ChunkSize];
                long totalAll = (long)targets.Count * batch.TotalBytes;
                int okTargets = 0;

                for (int t = 0; t < targets.Count; t++)
                {
                    var conn = targets[t];
                    bool targetOk = true;
                    long sentInBatch = 0;

                    for (int f = 0; f < batch.Paths.Length && targetOk; f++)
                    {
                        long sent = 0;
                        long lastReport = -1024 * 1024;
                        string error = null;
                        bool ok = false;
                        try
                        {
                            var header = new VideoHeaderMessage
                            {
                                type = batch.MediaType,
                                name = Path.GetFileName(batch.Paths[f]),
                                size = batch.Sizes[f],
                                batchId = batch.BatchId,
                                index = f,
                                total = batch.Paths.Length,
                                interval = batch.Interval
                            };
                            byte[] headerFrame = VideoTransferProtocol.EncodeFrame(JsonUtility.ToJson(header));
                            var stream = conn.Tcp.GetStream();
                            stream.Write(headerFrame, 0, headerFrame.Length);

                            using (var file = new FileStream(batch.Paths[f], FileMode.Open, FileAccess.Read, FileShare.Read, VideoTransferProtocol.ChunkSize, FileOptions.SequentialScan))
                            {
                                int read;
                                while ((read = file.Read(buffer, 0, buffer.Length)) > 0)
                                {
                                    stream.Write(buffer, 0, read);
                                    sent += read;
                                    if (sent - lastReport >= 1024 * 1024 || sent == batch.Sizes[f])
                                    {
                                        lastReport = sent;
                                        SendProgress?.Invoke(
                                            t * batch.TotalBytes + sentInBatch + sent,
                                            totalAll,
                                            $"({t + 1}/{targets.Count}) → {conn.Endpoint}（{f + 1}/{batch.Paths.Length}）");
                                    }
                                }
                            }
                            stream.Flush();

                            // 等待客户端校验回执
                            stream.ReadTimeout = 20000;
                            var ack = JsonUtility.FromJson<VideoAckMessage>(VideoTransferProtocol.ReadFrame(stream));
                            if (ack != null && ack.ok && ack.received == batch.Sizes[f]) ok = true;
                            else error = ack == null ? "回执格式错误" : "客户端校验失败: " + ack.error;
                        }
                        catch (Exception ex) { error = ex.Message; }

                        if (ok)
                        {
                            sentInBatch += batch.Sizes[f];
                            if (batch.Paths.Length > 1)
                                Log?.Invoke($"已发送 {Path.GetFileName(batch.Paths[f])}（{f + 1}/{batch.Paths.Length}）到 {conn.Endpoint}");
                        }
                        else
                        {
                            targetOk = false;
                            Log?.Invoke($"发送 {Path.GetFileName(batch.Paths[f])} 到 {conn.Endpoint} 失败: {error}");
                        }
                    }

                    if (targetOk)
                    {
                        okTargets++;
                        if (batch.MediaType == "video")
                            Log?.Invoke($"已成功发送到 {conn.Endpoint}: {Path.GetFileName(batch.Paths[0])}");
                        else
                            Log?.Invoke($"{conn.Endpoint} 图片批次接收完成（{batch.Paths.Length} 张，每 {batch.Interval:0.#} 秒轮播）");
                    }
                    else
                    {
                        lock (_gate) _clients.Remove(conn);
                        try { conn.Tcp.Close(); } catch { }
                        NotifyClientsChanged();
                    }
                }

                SendCompleted?.Invoke($"发送结束：成功 {okTargets}/{targets.Count} 个客户端");
            }
            catch (Exception ex)
            {
                SendCompleted?.Invoke("发送线程异常: " + ex.Message);
            }
            finally
            {
                _sending = false;
            }
        }
    }
}
