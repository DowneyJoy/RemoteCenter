using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class CentralControllerSystem : MonoBehaviour
{
    public static CentralControllerSystem instance = null;
    public int port_me = 8849;

    static Socket server;
    private UdpClient client; // udp客户端
    private Socket tcpClient;   //tcp客户端
    private IPEndPoint remotePoint_me;
    private Thread thread = null;
    private string receiveString = null;

    // Start is called before the first frame update
    void Start()
    {
        instance = this;
        // socket 配置，使用线程接收数据
        remotePoint_me = new IPEndPoint(IPAddress.Loopback, port_me); //在这儿端口是0也可以。。  
        thread = new Thread(ReceiveData);
        thread.Start();
    }

    void Test()
    {
        //开灯
        LightControl("192.168.1.231",new int[]{3,5},1);
        //关灯
        LightControl("192.168.1.231",new int[]{3,5},0);
        
        //内蒙
        //全部磁保持模块，顺序全开；
        UDPUtility.SendUDP("255.255.255.255", 2001, "{\"brake00\":\"1\"}"); 
        //全部磁保持模块，顺序全关；
        UDPUtility.SendUDP("255.255.255.255", 2001, "{\"brake00\":\"0\"}"); 
        
        //咸阳
        //全部磁保持模块，顺序全开；
        UDPUtility.SendUDP_16("255.255.255.255", 50000, "01 05 00 14 FF 00 CC 3E"); 
        //全部磁保持模块，顺序全关；
        UDPUtility.SendUDP_16("255.255.255.255", 50000, "01 05 00 15 FF 00 9D FE"); 
        
        //时序控制器全开
        ConectTCP_16("192.168.1.234", 5000, "43 4A 59 04 01 0D 1F AA "); 
        //时序控制器全关
        ConectTCP_16("192.168.1.234", 5000, "43 4A 59 04 01 0D 0F AA "); 
        //时序控制器 1路开
        ConectTCP_16("192.168.1.234", 5000, "43 4A 59 04 01 01 1F AA "); 
        //时序控制器 1路关
        ConectTCP_16("192.168.1.234", 5000, "43 4A 59 04 01 01 0F AA "); 
        
        //投影仪开
        ConectTCP_16("192.168.1.241", 4352, "25 31 50 4F 57 52 20 31 0D");  //tcp那边有携程延时，这边发的不能太快。
        //投影仪关
        ConectTCP_16("192.168.1.241", 4352, "25 31 50 4F 57 52 20 30 0D"); 
        
        //55寸触摸一体机 开
        UDPUtility.WakeUp("8C-C5-8C-17-35-A5"); 
        //所有的电脑（仰望动力、弧形屏幕电脑、飞机之最等）
        UDPUtility.SendUDP("255.255.255.255", 7788, "guanji");
    }

    void Play(string ip) //播放
    {
        UDPUtility.SendUDP(ip, 8848, "Event|"+"play"); 
        UDPUtility.SendUDP(ip, 9001, "play"); //融合播放；
        UDPUtility.SendUDP_16(ip, 10623, "04 00 03 00"); //广告机播放；
        UDPUtility.SendUDP(ip, 5008, "play"); //播放器播放；
        UDPUtility.SendUDP(ip, 8848, "playaudio"); //序厅音频播放
        UDPUtility.SendUDP(ip, 8848, "bf"); //播放；
    }

    void Pause(string ip) //暂停
    {
        UDPUtility.SendUDP(ip, 8848, "Event|"+"pause");
        UDPUtility.SendUDP(ip, 9001, "paue"); //融合；
        UDPUtility.SendUDP_16(ip, 10623, "04 00 04 00"); //广告机；
        UDPUtility.SendUDP(ip, 5008, "pause"); //播放器；
        UDPUtility.SendUDP(ip, 8848, "zt"); //播放；
    }
    void VolumeAdd(string ip) //音量+
    {
        UDPUtility.SendUDP(ip, 8848, "Event|" + "volumeadd"); 
        UDPUtility.SendUDP(ip, 9001, "volu"); //融合
        UDPUtility.SendUDP_16(ip, 10623, "04 00 0A 00"); //广告机；
        UDPUtility.SendUDP(ip, 5008, "volume+"); //播放器；
        UDPUtility.SendUDP(ip, 8848, "yljia");
    }
    void VolumeMinus(string ip) //音量-
    {
        UDPUtility.SendUDP(ip, 8848, "Event|" + "volumeminus"); 
        UDPUtility.SendUDP(ip, 9001, "vold"); //融合
        UDPUtility.SendUDP_16(ip, 10623, "04 00 0B 00"); //广告机；
        UDPUtility.SendUDP(ip, 5008, "volume-"); //播放器；
        UDPUtility.SendUDP(ip, 8848, "yljian");
    }
    void Replay(string ip) // 重播 
    {
        UDPUtility.SendUDP(ip, 5008, "stop"); //播放器；
        UDPUtility.SendUDP(ip, 9001, "stop"); //融合
        Thread.Sleep(50); //简单丑陋的延时，卡死主线程；
        UDPUtility.SendUDP(ip, 8848, "Event|" + "replay");

        UDPUtility.SendUDP(ip, 9001, "play"); //融合播放，实现重播；
        UDPUtility.SendUDP(ip, 5008, "play"); //播放器播放,实现重播；
        
        UDPUtility.SendUDP(ip, 8848, "qh");
    }
    void Update()
    {
        if (!string.IsNullOrEmpty(receiveString))
        {
            Debug.Log("Udp receive:" + receiveString);
            receiveString = null;
        }
    }
    public void ConectTCP_16(string ip, int port, string _msg)
    {
        IPEndPoint remotePoint_he = new IPEndPoint(IPAddress.Parse(ip),port);//实例化一个远程端点 

        byte[] sendData = UDPUtility.textWork16(_msg); //调用函数，转为byte;
        if (sendData != null)
        {
            tcpClient = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                IAsyncResult result = tcpClient.BeginConnect(IPAddress.Parse(ip) ,port, null, null);
                // 等待0.2秒，超时则自动跳过
                bool connectSuccess = result.AsyncWaitHandle.WaitOne(200);
                if (connectSuccess)
                {
                    // 连接成功
                    Debug.Log("TCP连接成功！");
                    StartCoroutine(SendTCP_16(_msg));
                }
                else
                {
                    // 【0.2秒超时】直接关闭客户端，忽略异常
                    tcpClient.Close();
                    Debug.LogWarning("TCP连接超时(0.2s)，已忽略，程序继续运行");
                }
            }
            catch (Exception e)
            {
                if (tcpClient != null)
                {
                    tcpClient.Close();
                    tcpClient = null;
                }
               
                Console.WriteLine($"Send error：{e.Message}");
                Debug.Log("TCP连接异常！！");
            }
        }
    }
    public IEnumerator SendTCP_16(string _msg)
    {
        yield return new WaitForSeconds(0.2f);
        byte[] sendData = UDPUtility.textWork16(_msg); //调用函数，转为byte;
        tcpClient.Send(sendData);
        yield return new WaitForSeconds(0.1f);
        Debug.Log("SendTcp:" + _msg );

        if (tcpClient != null)
        {
            tcpClient.Close();
            tcpClient = null;
        }
    }

    private void ReceiveData()
    {
        while (true)
        {
            if (string.IsNullOrEmpty(receiveString))
            {
                try
                {
                    //Setup UDP client.
                    client = new UdpClient(port_me);
                    //Grab the data.
                    byte[] data = client.Receive(ref remotePoint_me);
                    receiveString = Encoding.UTF8.GetString(data);
                    //Debug.Log(data.Length);
                    client.Close();
                }
                catch (System.Exception e)
                {
                    Debug.Log(e.ToString());
                }
            }
        }
    }

    public void LightControl(string ip, int[] port, int state)
    {
        StartCoroutine(LightControlString(ip, port, state));
    }

    /// <summary>
    /// 灯光控制模块 字符串
    /// </summary>
    /// <param name="ip"></param>
    /// <param name="ports"></param>
    /// <param name="a"></param>
    /// <returns></returns>
    IEnumerator LightControlString(string ip, int[] ports, int a)
    {
        string[] codes;
        if (a == 1) //开灯
        {
            codes = new string[]
            {
                "{\"brake01\":\"1\"}", "{\"brake02\":\"1\"}", 
                "{\"brake03\":\"1\"}", "{\"brake04\":\"1\"}", 
                "{\"brake05\":\"1\"}", "{\"brake06\":\"1\"}", 
                "{\"brake07\":\"1\"}", "{\"brake08\":\"1\"}"
            };
            for (int i = 0; i < ports.Length; i++)
            {
                UDPUtility.SendUDP(ip, 2001, codes[ports[i] - 1]);
                yield return new WaitForSeconds(0.3f); //太行模块2路以上控制，在这个通用函数里间隔需要延时1s才行。 原因未知。
                Debug.Log(ip + ":" + codes[ports[i] - 1]);
            }
        }
        if (a == 0) //关灯
        {
            codes = new string[]
            {
                "{\"brake01\":\"0\"}", "{\"brake02\":\"0\"}", 
                "{\"brake03\":\"0\"}", "{\"brake04\":\"0\"}", 
                "{\"brake05\":\"0\"}", "{\"brake06\":\"0\"}", 
                "{\"brake07\":\"0\"}", "{\"brake08\":\"0\"}"
            };
            for (int i = 0; i < ports.Length; i++)
            {
                UDPUtility.SendUDP(ip, 2001, codes[ports[i] - 1]);
                yield return new WaitForSeconds(0.3f); //太行模块2路以上控制，在这个通用函数里间隔需要延时1s才行。 原因未知。
                Debug.Log(ip + ":" + codes[ports[i] - 1]);
            }
        }
    }
    public void LightControlHex(string ip, int[] port, int state)
    {
        StartCoroutine(LightControlStringHex(ip, port, state));
    }

    IEnumerator LightControlStringHex(string ip, int[] ports, int a) // 太行模块 16进制
    {
        string[] codes;
        if (a == 1) //开灯
        {
            codes = new string[]
            {
                "CCDDA10100010001A448", "CCDDA10100020002A64C", "CCDDA10100040004AA54", "CCDDA10100080008B264",
                "CCDDA10100100010C284", "CCDDA10100200020E2C4", "CCDDA101004000402244", "CCDDA10100800080A244",
                "CCDDA10101000100A448", "CCDDA10102000200A64C", "CCDDA10104000400AA54", "CCDDA10108000800B264"
            };
            for (int i = 0; i < ports.Length; i++)
            {

                UDPUtility.SendUDP_16(ip, 50000, codes[ports[i] - 1]);
                yield return new WaitForSeconds(1.6f); //太行模块2路以上控制，在这个通用函数里间隔需要延时1s才行。 原因未知。
                Debug.Log(ip + ":" + codes[ports[i] - 1]);
            }
        }

        if (a == 0) //关灯
        {
            codes = new string[]
            {
                "CCDDA10100000001A346", "CCDDA10100000002A448", "CCDDA10100000004A64C", "CCDDA10100000008AA54",
                "CCDDA10100000010B264", "CCDDA10100000020C284", "CCDDA10100000040E2C4", "CCDDA101000000802244",
                "CCDDA10100000100A346", "CCDDA10100000200A448", "CCDDA10100000400A64C", "CCDDA10100000800AA54"
            };
            for (int i = 0; i < ports.Length; i++)
            {
                UDPUtility.SendUDP_16(ip, 50000, codes[ports[i] - 1]);
                yield return new WaitForSeconds(1.6f);
            }
        }
    }
    void OnApplicationQuit()
    {
        SocketQuit();
    }

    void OnDestroy()
    {
        SocketQuit();
    }

    // 终止线程，关闭client
    void SocketQuit()
    {
        thread.Abort();
        thread.Interrupt();
        client.Close();
    }
}
