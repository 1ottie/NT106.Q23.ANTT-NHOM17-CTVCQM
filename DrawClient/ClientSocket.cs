using System;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
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

                Console.WriteLine("Connecting to server...");
                client.Connect(ip, port);

                if (!client.Connected)
                {
                    Console.WriteLine("Connect failed!");
                    return false;
                }

                stream = client.GetStream();

                // FIX: gửi JOIN đúng protocol JSON
                var joinObj = new DrawMessage
                {
                    type = "JOIN",
                    roomId = "room1"
                };

                string json = JsonSerializer.Serialize(joinObj);
                Send(json + "\n");

                // FIX: start receive thread đúng chỗ
                receiveThread = new Thread(ReceiveData);
                receiveThread.IsBackground = true;
                receiveThread.Start();

                Console.WriteLine("Connected SUCCESS!");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Connect ERROR: " + ex.Message);
                return false;
            }
        }

        private void ReceiveData()
        {
            byte[] buffer = new byte[1024];
            StringBuilder dataBuffer = new StringBuilder();

            try
            {
                while (true)
                {
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);

                    if (bytesRead == 0)
                        break;

                    dataBuffer.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));

                    while (true)
                    {
                        string current = dataBuffer.ToString();
                        int index = current.IndexOf("\n");

                        if (index == -1) break;

                        string message = current.Substring(0, index);
                        dataBuffer.Remove(0, index + 1);

                        HandleMessage(message);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Receive ERROR: " + ex.Message);
            }
        }

        public void Send(string message)
        {
            try
            {
                if (stream != null && client != null && client.Connected)
                {
                    byte[] data = Encoding.UTF8.GetBytes(message);
                    stream.Write(data, 0, data.Length);

                    Console.WriteLine("Sent: " + message);
                }
                else
                {
                    Console.WriteLine("Send failed: Not connected");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Send ERROR: " + ex.Message);
            }
        }

        private void HandleMessage(string msg)
        {
            try
            {
                var data = JsonSerializer.Deserialize<DrawMessage>(msg);

                if (data == null) return;

                if (data.type == "DRAW")
                {
                    OnMessageReceived?.Invoke(msg);
                }
            }
            catch
            {
                Console.WriteLine("JSON parse lỗi");
            }
        }
    }
}