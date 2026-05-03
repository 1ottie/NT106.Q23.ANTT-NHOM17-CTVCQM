using System;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using DrawClient.Models;

namespace DrawClient
{
    public class ClientSocket
    {
        public static ClientSocket Instance { get; } = new ClientSocket();
        private TcpClient client;
        private NetworkStream stream;
        private Thread receiveThread;

        public Action<string> OnMessageReceived;
        private StringBuilder buffer = new StringBuilder();
        private string currentRoomId;

        // ĐÃ THÊM: Thuộc tính lưu trữ UserId hiện tại (Cần được gán sau khi login thành công)
        public int CurrentUserId { get; set; }

        public bool ConnectAndJoinRoomViaMaster(string masterIp, int masterPort, string roomId)
        {
            try
            {
                Console.WriteLine($"[CLIENT] Đang kết nối Master Server {masterIp}:{masterPort}...");
                using (TcpClient masterClient = new TcpClient(masterIp, masterPort))
                {
                    var masterStream = masterClient.GetStream();
                    var req = new MasterRequest { type = "JOIN_ROOM", roomId = roomId };
                    string reqJson = JsonSerializer.Serialize(req) + "\n";
                    byte[] reqData = Encoding.UTF8.GetBytes(reqJson);
                    masterStream.Write(reqData, 0, reqData.Length);

                    byte[] resBuffer = new byte[1024];
                    int bytesRead = masterStream.Read(resBuffer, 0, resBuffer.Length);

                    if (bytesRead > 0)
                    {
                        string resStr = Encoding.UTF8.GetString(resBuffer, 0, bytesRead).Trim();
                        var res = JsonSerializer.Deserialize<MasterResponse>(resStr);

                        if (res != null && res.success)
                        {
                            return ConnectToNodeAndJoin(res.nodeIp, res.nodePort, roomId);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("CONNECT MASTER ERROR: " + ex.Message);
            }
            return false;
        }

        private bool ConnectToNodeAndJoin(string nodeIp, int nodePort, string roomId)
        {
            try
            {
                if (client != null && client.Connected)
                {
                    stream?.Close();
                    client.Close();
                }

                client = new TcpClient();
                client.NoDelay = true;
                client.Connect(nodeIp, nodePort);
                stream = client.GetStream();

                receiveThread = new Thread(ReceiveLoop);
                receiveThread.IsBackground = true;
                receiveThread.Start();

                // ĐÃ SỬA: Gửi kèm UserId thực tế
                currentRoomId = roomId;
                Send(new DrawMessage
                {
                    type = "JOIN",
                    roomId = currentRoomId,
                    userId = CurrentUserId // Gửi ID người dùng thực tế
                });

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("CONNECT NODE ERROR: " + ex.Message);
                return false;
            }
        }

        public bool Connect(string ip, int port)
        {
            try
            {
                client = new TcpClient();
                client.NoDelay = true;
                client.Connect(ip, port);
                stream = client.GetStream();

                receiveThread = new Thread(ReceiveLoop);
                receiveThread.IsBackground = true;
                receiveThread.Start();

                return true;
            }
            catch (Exception ex)
            {
                return false;
            }
        }

        public void JoinRoom(string roomId)
        {
            if (client == null || !client.Connected) return;

            currentRoomId = roomId;
            // ĐÃ SỬA: Gửi kèm UserId khi Join qua danh sách
            Send(new DrawMessage
            {
                type = "JOIN",
                roomId = currentRoomId,
                userId = CurrentUserId
            });
        }

        #region RECEIVE
        private void ReceiveLoop()
        {
            byte[] receiveBuffer = new byte[4096];
            try
            {
                while (true)
                {
                    int len = stream.Read(receiveBuffer, 0, receiveBuffer.Length);
                    if (len <= 0) break;
                    buffer.Append(Encoding.UTF8.GetString(receiveBuffer, 0, len));
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
            }
            catch { }
        }
        #endregion

        #region SEND
        public void Send(object obj)
        {
            try
            {
                if (stream == null || !client.Connected) return;
                string json = JsonSerializer.Serialize(obj);
                byte[] data = Encoding.UTF8.GetBytes(json + "\n");
                stream.Write(data, 0, data.Length);
            }
            catch { }
        }
        #endregion

        #region HANDLE
        private void HandleMessage(string msg)
        {
            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var draw = JsonSerializer.Deserialize<DrawMessage>(msg, options);
                if (draw != null && !string.IsNullOrEmpty(draw.type))
                {
                    OnMessageReceived?.Invoke(msg);
                }
            }
            catch { }
        }
        #endregion
    }
}