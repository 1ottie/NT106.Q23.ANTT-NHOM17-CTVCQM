using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections.Generic;

namespace DrawServer
{
    public class ServerSocket
    {
        private TcpListener server;
        private List<TcpClient> clients =
            new List<TcpClient>();

        public Action<string> OnMessageReceived;

        // Khởi động Server
        public void Start(int port)
        {
            server = new TcpListener(
                IPAddress.Any,
                port);

            server.Start();

            Thread listenThread =
                new Thread(ListenClient);

            listenThread.IsBackground = true;
            listenThread.Start();
        }

        // Lắng nghe Client
        private void ListenClient()
        {
            while (true)
            {
                TcpClient client =
                    server.AcceptTcpClient();

                clients.Add(client);

                Thread clientThread =
                    new Thread(() =>
                        HandleClient(client));

                clientThread.IsBackground = true;
                clientThread.Start();
            }
        }

        // Xử lý từng Client
        private void HandleClient(
            TcpClient client)
        {
            NetworkStream stream =
                client.GetStream();

            byte[] buffer =
                new byte[1024];

            try
            {
                while (true)
                {
                    int bytesRead =
                        stream.Read(
                            buffer,
                            0,
                            buffer.Length);

                    if (bytesRead == 0)
                        break;

                    string message =
                        Encoding.UTF8
                        .GetString(
                            buffer,
                            0,
                            bytesRead);

                    // Gửi cho tất cả Client khác
                    Broadcast(message);

                    OnMessageReceived?.Invoke(
                        message);
                }
            }
            catch
            {
                clients.Remove(client);
            }
        }

        // Gửi dữ liệu cho tất cả Client
        private void Broadcast(string message)
        {
            byte[] data =
                Encoding.UTF8.GetBytes(message);

            foreach (var client in clients)
            {
                try
                {
                    NetworkStream stream =
                        client.GetStream();

                    stream.Write(
                        data,
                        0,
                        data.Length);
                }
                catch { }
            }
        }
    }
}