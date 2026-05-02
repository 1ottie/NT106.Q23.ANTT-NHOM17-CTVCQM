using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace DrawServer
{
    public class ServerSocket
    {
        private TcpListener server;

        // roomId -> clients
        private ConcurrentDictionary<string, ConcurrentDictionary<TcpClient, byte>> rooms
            = new ConcurrentDictionary<string, ConcurrentDictionary<TcpClient, byte>>();

        public void Start(int port)
        {
            server = new TcpListener(IPAddress.Any, port);
            server.Start();

            Console.WriteLine("Server started...");

            new Thread(() =>
            {
                while (true)
                {
                    var client = server.AcceptTcpClient();
                    client.NoDelay = true;

                    Console.WriteLine("Client connected");

                    new Thread(() => HandleClient(client)).Start();
                }
            })
            { IsBackground = true }.Start();
        }

        private void HandleClient(TcpClient client)
        {
            var stream = client.GetStream();
            byte[] buffer = new byte[4096];
            var sb = new StringBuilder();

            try
            {
                while (true)
                {
                    int len = stream.Read(buffer, 0, buffer.Length);
                    if (len <= 0) break;

                    sb.Append(Encoding.UTF8.GetString(buffer, 0, len));

                    while (true)
                    {
                        string data = sb.ToString();
                        int idx = data.IndexOf('\n');
                        if (idx < 0) break;

                        string msg = data.Substring(0, idx);
                        sb.Remove(0, idx + 1);

                        HandleMessage(client, msg);
                    }
                }
            }
            catch
            {
                Console.WriteLine("Client lost");
            }
            finally
            {
                RemoveClient(client);
                client.Close();
            }
        }

        private void HandleMessage(TcpClient client, string msg)
        {
            DrawMessage data;

            try
            {
                data = JsonSerializer.Deserialize<DrawMessage>(msg);
            }
            catch
            {
                return;
            }

            if (data == null) return;

            if (data.type == "JOIN")
            {
                var room = rooms.GetOrAdd(data.roomId,
                    _ => new ConcurrentDictionary<TcpClient, byte>());

                room[client] = 0;
            }

            else
            {
                Broadcast(data.roomId, msg + "\n", client);
            }
        }

        private void Broadcast(string roomId, string msg, TcpClient sender)
        {
            if (!rooms.ContainsKey(roomId)) return;

            byte[] data = Encoding.UTF8.GetBytes(msg);

            foreach (var client in rooms[roomId].Keys)
            {
                if (client == sender) continue;

                try
                {
                    client.GetStream().Write(data, 0, data.Length);
                }
                catch
                {
                    RemoveClient(client);
                }
            }
        }

        private void RemoveClient(TcpClient client)
        {
            foreach (var room in rooms.Values)
            {
                room.TryRemove(client, out _);
            }
        }
    }
}