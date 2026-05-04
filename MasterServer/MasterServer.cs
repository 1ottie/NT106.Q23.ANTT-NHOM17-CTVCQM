using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace MasterServer
{
    public class MasterServer
    {
        private TcpListener listener;

        // Danh sách các Node (Game/Drawing Server) đang chạy
        private List<NodeInfo> nodes = new()
        {
            new NodeInfo { Ip="127.0.0.1", Port=6001, MaxUsers=100, CurrentUsers=0 }
        };

        public void Start(int port)
        {
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start();

            Console.WriteLine($"[MASTER SERVER] Đã khởi động. Lắng nghe Client tại port {port}...");

            while (true)
            {
                var client = listener.AcceptTcpClient();
                new Thread(() => HandleClient(client)).Start();
            }
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
                    if (len <= 0) break; // Client đóng kết nối

                    sb.Append(Encoding.UTF8.GetString(buffer, 0, len));

                    while (true)
                    {
                        string s = sb.ToString();
                        int i = s.IndexOf('\n');
                        if (i < 0) break;

                        string msg = s.Substring(0, i);
                        sb.Remove(0, i + 1);

                        var req = JsonSerializer.Deserialize<MasterRequest>(msg);

                        if (req?.type == "JOIN_ROOM")
                        {
                            Console.WriteLine($"[MASTER SERVER] Nhận yêu cầu JOIN_ROOM ({req.roomId}) từ Client.");
                            var node = GetBestNode();

                            // 1. Mở kết nối TCP tới Node Server để yêu cầu chuẩn bị phòng
                            bool isNodeReady = NotifyNodeServer(node, req.roomId);

                            if (isNodeReady)
                            {
                                // 2. Nếu Node ok, trả về cấu hình Node để Client tự kết nối thẳng tới Node
                                var res = new MasterResponse
                                {
                                    success = true,
                                    nodeIp = node.Ip,
                                    nodePort = node.Port,
                                    roomId = req.roomId
                                };
                                Send(stream, res);
                                Console.WriteLine($"[MASTER SERVER] Đã điều hướng Client tới Node {node.Ip}:{node.Port}");
                            }
                            else
                            {
                                // Node sập hoặc không phản hồi
                                var res = new MasterResponse
                                {
                                    success = false,
                                    nodeIp = null,
                                    nodePort = 0,
                                    roomId = req.roomId
                                };
                                Send(stream, res);
                                Console.WriteLine($"[MASTER SERVER] LỖI: Node {node.Ip}:{node.Port} không sẵn sàng.");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MASTER SERVER] Lỗi xử lý client: {ex.Message}");
            }
            finally
            {
                client.Close();
            }
        }

        // Hàm xử lý kết nối TCP từ MasterServer tới NodeServer
        private bool NotifyNodeServer(NodeInfo node, string roomId)
        {
            try
            {
                using (var nodeClient = new TcpClient(node.Ip, node.Port))
                {
                    var stream = nodeClient.GetStream();

                    // Gửi thông điệp báo Node chuẩn bị phòng
                    var nodeReq = new NodeControlRequest
                    {
                        type = "PREPARE_ROOM",
                        roomId = roomId
                    };

                    Send(stream, nodeReq);

                    // Đợi Node Server phản hồi xác nhận (tùy chọn)
                    byte[] buffer = new byte[1024];
                    int len = stream.Read(buffer, 0, buffer.Length);
                    if (len > 0)
                    {
                        string responseStr = Encoding.UTF8.GetString(buffer, 0, len);
                        // Ở đây bạn có thể Deserialize JSON phản hồi từ Node để kiểm tra
                        // Tạm thời cứ có phản hồi là coi như Node đã sẵn sàng
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MASTER SERVER] Không thể kết nối tới Node {node.Ip}:{node.Port}. Lỗi: {ex.Message}");
                return false;
            }
            return false;
        }

        private NodeInfo GetBestNode()
        {
            // Tương lai bạn có thể check node.CurrentUsers so với node.MaxUsers ở đây
            return nodes[0];
        }

        private void Send(NetworkStream stream, object obj)
        {
            string json = JsonSerializer.Serialize(obj) + "\n";
            byte[] data = Encoding.UTF8.GetBytes(json);
            stream.Write(data, 0, data.Length);
        }
    }

    // --- CÁC LỚP DATA MODEL ---

    public class MasterRequest
    {
        public string type { get; set; }
        public string roomId { get; set; }
    }

    public class MasterResponse
    {
        public bool success { get; set; }
        public string nodeIp { get; set; }
        public int nodePort { get; set; }
        public string roomId { get; set; }
    }

    // Thêm model này để giao tiếp với Node Server
    public class NodeControlRequest
    {
        public string type { get; set; }
        public string roomId { get; set; }
    }

    public class NodeInfo
    {
        public string Ip { get; set; }
        public int Port { get; set; }
        public int MaxUsers { get; set; }
        public int CurrentUsers { get; set; }
    }
}