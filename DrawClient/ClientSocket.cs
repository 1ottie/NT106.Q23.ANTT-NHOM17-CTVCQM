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

        private StringBuilder buffer = new StringBuilder();

        public bool Connect(string ip, int port)
        {
            try
            {
                client = new TcpClient();
                client.Connect(ip, port);

                stream = client.GetStream();

                SendInternal(new DrawMessage
                {
                    type = "JOIN",
                    roomId = "room1"
                });

                receiveThread = new Thread(ReceiveLoop);
                receiveThread.IsBackground = true;
                receiveThread.Start();

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("CONNECT ERROR: " + ex.Message);
                return false;
            }
        }

        #region RECEIVE
        private void ReceiveLoop()
        {
            byte[] data = new byte[4096];

            try
            {
                while (client.Connected)
                {
                    int len = stream.Read(data, 0, data.Length);
                    if (len <= 0) continue;

                    buffer.Append(Encoding.UTF8.GetString(data, 0, len));

                    ProcessBuffer();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("RECEIVE ERROR: " + ex.Message);
            }
        }

        private void ProcessBuffer()
        {
            while (true)
            {
                string content = buffer.ToString();
                int index = content.IndexOf('\n');

                if (index < 0) break;

                string msg = content.Substring(0, index);
                buffer.Remove(0, index + 1);

                HandleMessage(msg);
            }
        }
        #endregion

        #region SEND
        public void Send(object obj)
        {
            string json = JsonSerializer.Serialize(obj);
            SendInternal(json);
        }

        private void SendInternal(object obj)
        {
            try
            {
                if (stream == null) return;

                string json = obj is string s ? s : JsonSerializer.Serialize(obj);

                byte[] data = Encoding.UTF8.GetBytes(json + "\n");
                stream.Write(data, 0, data.Length);
            }
            catch (Exception ex)
            {
                Console.WriteLine("SEND ERROR: " + ex.Message);
            }
        }
        #endregion

        #region HANDLE
        private void HandleMessage(string msg)
        {
            try
            {
                var data = JsonSerializer.Deserialize<DrawMessage>(msg);

                if (data == null) return;

                switch (data.type)
                {
                    case "DRAW":
                    case "stroke_move":
                    case "stroke_start":
                    case "stroke_end":
                        OnMessageReceived?.Invoke(msg);
                        break;
                }
            }
            catch
            {
                Console.WriteLine("INVALID JSON: " + msg);
            }
        }
        #endregion
    }
}