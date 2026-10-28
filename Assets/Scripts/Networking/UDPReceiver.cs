using UnityEngine;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

public class UDPReceiver : MonoBehaviour
{
    public static string LatestJson = "";
    public static volatile bool HasNewJson = false;

    private UdpClient client;
    private Thread receiveThread;

    public int port = 5055;

    void Start()
    {
        client = new UdpClient(port);

        receiveThread = new Thread(ReceiveData);
        receiveThread.IsBackground = true;
        receiveThread.Start();

        Debug.Log("UDP Receiver Started");
    }

    void ReceiveData()
    {
        IPEndPoint endPoint = new IPEndPoint(IPAddress.Any, port);

        try
        {
            while (true)
            {
                byte[] data = client.Receive(ref endPoint);
                LatestJson = Encoding.UTF8.GetString(data);
                HasNewJson = true;
            }
        }
        catch (SocketException)
        {
            // Socket closed while exiting Play Mode
        }
        catch (System.Exception e)
        {
            Debug.Log(e.Message);
        }
    }

    void OnDisable()
    {
        CloseSocket();
    }

    void OnDestroy()
    {
        CloseSocket();
    }

    void OnApplicationQuit()
    {
        CloseSocket();
    }

    private void CloseSocket()
    {
        try
        {
            client?.Close();
            client = null;
        }
        catch { }

        if (receiveThread != null && receiveThread.IsAlive)
        {
            try
            {
                receiveThread.Join(100);
            }
            catch { }
            receiveThread = null;
        }
    }
}