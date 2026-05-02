using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Text.Json;

namespace DrawServer
{
    public class ServerSocket
    {
        // thread-safe collections
        private ConcurrentDictionary<string, List<TcpClient>> rooms
            = new ConcurrentDictionary<string, List<TcpClient>>();

        private TcpListener server;

        // thread-safe list
        private List<TcpClient> clients = new List<TcpClient>();
        private object clientLock = new object();
        private object roomLock = new object();

        public Action<string> OnMessageReceived;

        public void Start(int port)
        {
            server = new TcpListener(IPAddress.Any, port);
            server.Start();

            Console.WriteLine("Server started at port " + port);

            Thread listenThread = new Thread(ListenClient);
            listenThread.IsBackground = true;
            listenThread.Start();
        }

        private void ListenClient()
        {
            while (true)
            {
                TcpClient client = server.AcceptTcpClient();

                // disable Nagle for realtime drawing
                client.NoDelay = true;

                Console.WriteLine("Client connected!");

                lock (clientLock)
                {
                    clients.Add(client);
                }

                Thread clientThread = new Thread(() => HandleClient(client));
                clientThread.IsBackground = true;
                clientThread.Start();
            }
        }

        private void HandleClient(TcpClient client)
        {
            NetworkStream stream = client.GetStream();
            byte[] buffer = new byte[1024];

            // safer buffer handling
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

                        Console.WriteLine("Received: " + message);

                        DrawMessage data = null;

                        // avoid crash on bad JSON
                        try
                        {
                            data = JsonSerializer.Deserialize<DrawMessage>(message);
                        }
                        catch
                        {
                            continue;
                        }

                        if (data == null) continue;

                        if (data.type == "JOIN")
                        {
                            if (!rooms.ContainsKey(data.roomId))
                            {
                                rooms[data.roomId] = new List<TcpClient>();
                            }

                            lock (roomLock)
                            {
                                rooms[data.roomId].Add(client);
                            }
                        }
                        else if (data.type == "DRAW")
                        {
                            BroadcastToRoom(data.roomId, message + "\n");
                        }
                    }
                }
            }
            catch
            {
                Console.WriteLine("Client disconnected");
            }
            finally
            {
                // cleanup client everywhere

                lock (clientLock)
                {
                    clients.Remove(client);
                }

                foreach (var room in rooms)
                {
                    lock (roomLock)
                    {
                        room.Value.Remove(client);
                    }
                }

                try
                {
                    stream.Close();
                    client.Close();
                }
                catch { }
            }
        }

        private void Broadcast(string message)
        {
            byte[] data = Encoding.UTF8.GetBytes(message);

            lock (clientLock)
            {
                foreach (var client in clients.ToList())
                {
                    try
                    {
                        client.GetStream().Write(data, 0, data.Length);
                    }
                    catch
                    {
                        // remove dead client
                        clients.Remove(client);
                    }
                }
            }
        }

        private void BroadcastToRoom(string roomId, string message)
        {
            if (!rooms.ContainsKey(roomId)) return;

            byte[] data = Encoding.UTF8.GetBytes(message);

            lock (roomLock)
            {
                foreach (var client in rooms[roomId].ToList())
                {
                    try
                    {
                        client.GetStream().Write(data, 0, data.Length);
                    }
                    catch
                    {
                        //remove dead client
                        rooms[roomId].Remove(client);
                    }
                }

                // remove empty room
                if (rooms[roomId].Count == 0)
                {
                    rooms.TryRemove(roomId, out _);
                }
            }
        }
    }
}