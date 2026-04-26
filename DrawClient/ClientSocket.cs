using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace DrawClient
{
    public class ClientSocket
    {
        private TcpClient client;
        private NetworkStream stream;
        private Thread receiveThread;

        public Action<string> OnMessageReceived;

        public bool Connect(string ip, int port)
        {
            try
            {
                client = new TcpClient();
                client.Connect(ip, port);

                stream = client.GetStream();

                receiveThread = new Thread(ReceiveData);
                receiveThread.IsBackground = true;
                receiveThread.Start();

                return true;
            }
            catch
            {
                return false;
            }
        }

        private void ReceiveData()
        {
            byte[] buffer = new byte[1024];

            while (true)
            {
                int bytesRead =
                    stream.Read(buffer, 0, buffer.Length);

                if (bytesRead == 0) break;

                string msg =
                    Encoding.UTF8.GetString(buffer, 0, bytesRead);

                OnMessageReceived?.Invoke(msg);
            }
        }
    }
}