using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

public class ControllerTest : MonoBehaviour
{
    public InputField inputField;
    public InputField kaijiInputField;
    public InputField guanjiInputField;
    private string kaiji = "58-11-22-9B-E5-A6";
    private string guanji = "192.168.0.241";
    [Header("UDP字符串")] public InputField UDPStringIP;
    public InputField UDPStringPort;
    public InputField UDPStringMsg;
    public Button UDPStringButton;
    [Header("UDP字符串")] public InputField UDPHexIP;
    public InputField UDPHexPort;
    public InputField UDPHexMsg;
    public Button UDPHexButton;
    [Header("UDP字符串")] public InputField TCPHexIP;
    public InputField TCPHexPort;
    public InputField TCPHexMsg;
    public Button TCPHexButton;
    [Header("UDP字符串")] public InputField RemoteShutdownIP;
    public Button RemoteShutdownButton;

    
    private void Awake()
    {
        guanjiInputField.text = guanji;
        kaijiInputField.text = kaiji;
        UDPStringButton.onClick.AddListener(SendUdpString);
        UDPHexButton.onClick.AddListener(SendUDPHex);
        TCPHexButton.onClick.AddListener(SendTCPHex);
        RemoteShutdownButton.onClick.AddListener(RemoteShutdownEvent);
    }

    public void SendUdpString()
    {
        UDPUtility.SendUDP(UDPStringIP.text, int.Parse(UDPStringPort.text), UDPStringMsg.text);
    }

    public void SendUDPHex()
    {
        UDPUtility.SendUDP_16(UDPHexIP.text, int.Parse(UDPHexPort.text), UDPHexMsg.text);
    }

    public void SendTCPHex()
    {
        CentralControllerSystem.instance.ConectTCP_16(TCPHexIP.text, int.Parse(TCPHexPort.text), TCPHexMsg.text);
    }

    public void Shutdown()
    {
        if(!string.IsNullOrEmpty(guanjiInputField.text))
        //UdpSender.SendUDP("255.255.255.255", 7788, "guanji");
            UdpSender.SendUDP(guanjiInputField.text, 7788, "guanji");
    }

    public void ShutUp()
    {
        if(!string.IsNullOrEmpty(kaijiInputField.text))
            //UdpSender.WakeUp("58-11-22-9B-E5-A6"); 
            //UDPUtility.WakeUp(kaijiInputField.text,9); 
            WakeOnLan.WakeUp(kaijiInputField.text);
    }

    public void SendUDP(string message)
    {
        if(!string.IsNullOrEmpty(inputField.text))
            UdpSender.SendUDP(inputField.text, 8848, "Event|"+message);
    }

    public void RemoteShutdownEvent()
    {
        if(!string.IsNullOrEmpty(RemoteShutdownIP.text))
            RemoteShutdown.ShutdownTarget(RemoteShutdownIP.text);
    }
}
