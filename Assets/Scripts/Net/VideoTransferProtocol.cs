using System;
using System.IO;
using System.Text;

namespace RemoteCenter
{
    /// 消息头：服务端通知客户端即将传输的媒体文件信息。
    [Serializable]
    public class VideoHeaderMessage
    {
        public string type;     // "video" 或 "image"
        public string name;
        public long size;
        public string batchId; // 图片批次标识（视频为 null）
        public int index;      // 批内序号（从 0 开始）
        public int total;      // 批内文件总数
        public float interval; // 图片轮播间隔（秒）
    }

    /// 客户端回执：确认文件是否接收完整。
    [Serializable]
    public class VideoAckMessage
    {
        public string type;
        public string name;
        public long received;
        public bool ok;
        public string error;
    }
    
    /// 初始化命令：服务端通过 UDP 告知客户端自己的地址。
    [Serializable]
    public class InitMessage
    {
        public string type;      // "init"
        public string serverIp;  // 服务端局域网 IP
        public int port;        // 服务端 TCP 端口
    }
    
    /// 传输协议：[4字节大端长度][UTF8 JSON 帧]；JSON 头之后紧跟 size 字节的原始文件数据。
    public static class VideoTransferProtocol
    {
        public const int Port = 9090;
        public const int InitPort = 9091; // UDP 初始化命令端口
        public const int ChunkSize = 64 * 1024;
        public const int MaxFrameSize = 64 * 1024;

        public static byte[] EncodeFrame(string json)
        {
            byte[] payload = Encoding.UTF8.GetBytes(json);
            byte[] frame = new byte[4 + payload.Length];
            frame[0] = (byte)(payload.Length >> 24);
            frame[1] = (byte)(payload.Length >> 16);
            frame[2] = (byte)(payload.Length >> 8);
            frame[3] = (byte)payload.Length;
            Buffer.BlockCopy(payload, 0, frame, 4, payload.Length);
            return frame;
        }

        public static string ReadFrame(Stream stream)
        {
            byte[] header = new byte[4];
            ReadExactly(stream, header, 4);
            int length = (header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3];
            if (length <= 0 || length > MaxFrameSize)
                throw new InvalidDataException("非法帧长度: " + length);
            byte[] payload = new byte[length];
            ReadExactly(stream, payload, length);
            return Encoding.UTF8.GetString(payload);
        }

        public static void ReadExactly(Stream stream, byte[] buffer, int count)
        {
            int offset = 0;
            while (offset < count)
            {
                int read = stream.Read(buffer, offset, count - offset);
                if (read <= 0)
                    throw new EndOfStreamException();
                offset += read;
            }
        }
    }
}
