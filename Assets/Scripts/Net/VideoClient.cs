using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace RemoteCenter
{
    /// TCP 客户端：自动连接/重连服务端，接收视频或图片文件并落盘到 persistentDataPath。
    /// 事件均在后台线程触发，UI 层需自行切回主线程。
    public sealed class VideoClient : IDisposable
    {
        readonly Func<string> _hostProvider;
        Thread _thread;
        volatile bool _running;
        volatile bool _connected;
        readonly AutoResetEvent _wake = new AutoResetEvent(false);
        TcpClient _tcp;

        // 图片批次接收状态（仅接收线程访问）
        string _batchId;
        int _batchGot;
        int _batchTotal;
        float _batchInterval;
        readonly List<string> _batchPaths = new List<string>();

        public event Action<string> StateChanged;
        public event Action<string, long> ReceivingStarted;          // 文件名, 总大小
        public event Action<long, long> ReceivingProgress;          // 已接收, 总大小
        public event Action<string> VideoReceived;                   // 单个视频接收完成（落盘路径）
        public event Action<List<string>, float> ImagesReceived;    // 图片批次全部接收完成（路径列表, 轮播间隔）
        public event Action ServerDisconnect;                        // 服务端请求断开（此后停止重连，等待下次初始化）
        public event Action<string> Error;

        public int Port { get; }
        public string SaveDirectory { get; }
        public string ReceivedDirectory { get; }
        public string ReceivedImagesDirectory { get; }
        public bool IsConnected => _connected;
        public bool IsRunning => _running;

        /// 立即唤醒连接循环（例如收到初始化命令更新了目标 IP 时）。
        public void Wake() => _wake.Set();

        public VideoClient(Func<string> hostProvider, int port)
        {
            _hostProvider = hostProvider;
            Port = port;
            SaveDirectory = Path.Combine(Application.persistentDataPath, "RemoteVideos");
            ReceivedDirectory = Path.Combine(SaveDirectory, "received");
            ReceivedImagesDirectory = Path.Combine(SaveDirectory, "received_images");
        }

        /// 最近一次接收成功的视频（旧版本兼容；当前媒体改由 playlist.json 记录）。
        public string FindLatestReceivedVideo()
        {
            try
            {
                if (!Directory.Exists(ReceivedDirectory)) return null;
                string best = null;
                long bestTick = long.MinValue;
                foreach (string file in Directory.GetFiles(ReceivedDirectory, "*.mp4"))
                {
                    long tick = File.GetLastWriteTimeUtc(file).Ticks;
                    if (tick > bestTick)
                    {
                        bestTick = tick;
                        best = file;
                    }
                }
                return best;
            }
            catch { return null; }
        }

        public void Start()
        {
            if (_running) return;
            _running = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "VideoClient" };
            _thread.Start();
        }

        public void Dispose()
        {
            _running = false;
            try { _tcp?.Close(); } catch { }

            _wake.Set();
            try { _thread?.Join(600); } catch { }
        }

        void Loop()
        {
            while (_running)
            {
                string host = (_hostProvider() ?? string.Empty).Trim();
                if (host.Length == 0)
                {
                    _wake.WaitOne(1000);
                    continue;
                }

                _tcp = new TcpClient();
                bool connected = false;
                try
                {
                    StateChanged?.Invoke($"正在连接 {host}:{Port}...");
                    var async = _tcp.BeginConnect(host, Port, null, null);
                    connected = async.AsyncWaitHandle.WaitOne(3000) && SafeEndConnect(_tcp, async);
                }
                catch { }

                if (!connected)
                {
                    try { _tcp.Close(); } catch { }
                    _tcp = null;
                    if (!_running) break;
                    StateChanged?.Invoke($"无法连接 {host}:{Port}，3 秒后重试");
                    _wake.WaitOne(3000);
                    continue;
                }

                try
                {
                    _tcp.NoDelay = true;
                    _connected = true;
                    StateChanged?.Invoke($"已连接 {host}:{Port}");
                    ReceiveLoop(_tcp);
                }
                catch (Exception ex)
                {
                    Error?.Invoke("接收异常: " + ex.Message);
                }
                finally
                {
                    _connected = false;
                }

                try { _tcp.Close(); } catch { }
                _tcp = null;
                if (_running)
                {
                    StateChanged?.Invoke("连接断开，3 秒后重试");
                    _wake.WaitOne(3000);
                }
            }
        }

        static bool SafeEndConnect(TcpClient tcp, IAsyncResult async)
        {
            try { tcp.EndConnect(async); return true; }
            catch { return false; }
        }

        void ReceiveLoop(TcpClient tcp)
        {
            var stream = tcp.GetStream();
            stream.ReadTimeout = Timeout.Infinite;
            while (_running)
            {
                string json;
                try { json = VideoTransferProtocol.ReadFrame(stream); }
                catch { break; } // 服务端断开或读取异常，回到外层重连
                if (!_running) break;

                var header = JsonUtility.FromJson<VideoHeaderMessage>(json);
                
                // 断开命令：停止重连循环，等待下一次初始化再恢复
                if (header != null && header.type == "disconnect")
                {
                    _running = false;
                    _connected = false;
                    ServerDisconnect?.Invoke();
                    return;
                }
                
                if (header == null || header.size <= 0) continue;

                if (!ReceiveFile(stream, header)) break; // 传输失败后数据流不可靠，断开等待重连
            }
        }

        bool ReceiveFile(NetworkStream stream, VideoHeaderMessage header)
        {
            bool isImage = header.type == "image";
            string saveDir = isImage ? ReceivedImagesDirectory : ReceivedDirectory;
            Directory.CreateDirectory(saveDir);
            string tempPath = Path.Combine(SaveDirectory, "receiving.tmp");
            long received = 0;
            long lastReport = -1024 * 1024;
            bool ok = false;
            string error = null;

            ReceivingStarted?.Invoke(header.name, header.size);
            try
            {
                using (var file = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, VideoTransferProtocol.ChunkSize))
                {
                    byte[] buffer = new byte[VideoTransferProtocol.ChunkSize];
                    stream.ReadTimeout = 30000; // 传输期间静默 30 秒视为掉线
                    while (received < header.size)
                    {
                        int want = (int)Math.Min((long)buffer.Length, header.size - received);
                        int read = stream.Read(buffer, 0, want);
                        if (read <= 0) throw new EndOfStreamException("连接中断");
                        file.Write(buffer, 0, read);
                        received += read;
                        if (received - lastReport >= 1024 * 1024 || received == header.size)
                        {
                            lastReport = received;
                            ReceivingProgress?.Invoke(received, header.size);
                        }
                    }
                }
                ok = received == header.size;
                if (!ok) error = "数据不完整";
            }
            catch (Exception ex)
            {
                ok = false;
                error = ex.Message;
            }

            string finalPath = null;
            if (ok)
            {
                try
                {
                    // 每次接收生成新文件，避免删除正在使用的旧文件时被占用
                    finalPath = Path.Combine(saveDir, MakeFileName(header.name, isImage));
                    File.Move(tempPath, finalPath);
                }
                catch (Exception ex)
                {
                    ok = false;
                    error = "保存文件失败: " + ex.Message;
                }
            }
            if (!ok)
            {
                try { File.Delete(tempPath); } catch { }
            }

            // 尽力发送回执（失败不影响重连流程）
            try
            {
                var ack = new VideoAckMessage { type = header.type, name = header.name, received = received, ok = ok, error = error };
                byte[] frame = VideoTransferProtocol.EncodeFrame(JsonUtility.ToJson(ack));
                stream.Write(frame, 0, frame.Length);
                stream.Flush();
            }
            catch { }
            stream.ReadTimeout = Timeout.Infinite;

            if (ok)
            {
                if (isImage) OnImageReceived(header, finalPath);
                else VideoReceived?.Invoke(finalPath);
            }
            else
            {
                Error?.Invoke($"接收 {header.name} 失败: {error}");
            }
            return ok;
        }

        void OnImageReceived(VideoHeaderMessage header, string path)
        {
            // 新批次开始时重置批次状态（服务端重发同一批次会使用新的 batchId）
            if (header.batchId != _batchId)
            {
                _batchId = header.batchId;
                _batchGot = 0;
                _batchTotal = Math.Max(1, header.total);
                _batchPaths.Clear();
                _batchInterval = header.interval > 0f ? header.interval : 5f;
            }
            _batchPaths.Add(path);
            _batchGot++;

            if (_batchGot >= _batchTotal)
            {
                var paths = new List<string>(_batchPaths);
                float interval = _batchInterval;
                _batchId = null;
                _batchGot = 0;
                _batchTotal = 0;
                _batchPaths.Clear();
                ImagesReceived?.Invoke(paths, interval);
            }
        }

        static string MakeFileName(string original, bool isImage)
        {
            var sb = new StringBuilder();
            string raw = string.IsNullOrEmpty(original) ? (isImage ? "image.jpg" : "video.mp4") : original;
            foreach (char c in raw)
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.') sb.Append(c);
                else if (c == ' ') sb.Append('_');
            }
            string name = sb.ToString();
            if (name.Length == 0) name = isImage ? "image.jpg" : "video.mp4";

            // 校正扩展名
            string stem = name;
            string ext = Path.GetExtension(name).ToLowerInvariant();
            int dot = name.LastIndexOf('.');
            if (dot > 0) stem = name.Substring(0, dot);
            if (isImage)
            {
                if (ext != ".jpg" && ext != ".jpeg" && ext != ".png") ext = ".jpg";
            }
            else
            {
                if (ext != ".mp4" && ext != ".mov" && ext != ".avi" && ext != ".wmv" && ext != ".mkv" && ext != ".webm") ext = ".mp4";
            }
            if (stem.Length > 60) stem = stem.Substring(stem.Length - 60);
            return DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + stem + ext;
        }
    }
}
