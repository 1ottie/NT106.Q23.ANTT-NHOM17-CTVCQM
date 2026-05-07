using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using MySql.Data.MySqlClient;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DrawServer
{
    public class ServerSocket
    {
        private string connectionString =
            "server=localhost;database=online_Drawing_DB;user=root;password=";

        private TcpListener server;

        // Quản lý phòng: roomId -> danh sách các Client trong phòng đó
        private ConcurrentDictionary<string, ConcurrentDictionary<TcpClient, byte>> rooms
            = new ConcurrentDictionary<string, ConcurrentDictionary<TcpClient, byte>>();

        // Quản lý thông tin User trên mỗi Connection để biết ai vừa thoát
        private ConcurrentDictionary<TcpClient, (int UserId, string RoomId)> clientMetadata
            = new ConcurrentDictionary<TcpClient, (int UserId, string RoomId)>();

        private static readonly HttpClient _httpClient = new HttpClient();
        private const string MasterApiUrl = "http://localhost:5274/api/room/update-status";

        public void Start(int port)
        {
            server = new TcpListener(IPAddress.Any, port);
            server.Start();
            Console.WriteLine($"[NODE SERVER] Đang chạy tại cổng: {port}...");

            new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        var client = server.AcceptTcpClient();
                        client.NoDelay = true; // Gửi dữ liệu ngay lập tức, giảm delay khi vẽ
                        Console.WriteLine("Có một Client mới kết nối vào Node.");
                        new Thread(() => HandleClient(client)).Start();
                    }
                    catch (Exception ex) { Console.WriteLine("Lỗi Accept: " + ex.Message); }
                }
            })
            { IsBackground = true }.Start();
        }

        private void HandleClient(TcpClient client)
        {
            var stream = client.GetStream();
            byte[] buffer = new byte[8192];
            StringBuilder sb = new StringBuilder();

            try
            {
                while (true)
                {
                    int len = stream.Read(buffer, 0, buffer.Length);
                    if (len <= 0) break;

                    // Chuyển byte sang string và đưa vào buffer tạm
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, len));
                    string currentData = sb.ToString();

                    // Xử lý từng dòng (mỗi dòng là 1 gói JSON kết thúc bằng \n)
                    int newlineIndex;
                    while ((newlineIndex = currentData.IndexOf('\n')) >= 0)
                    {
                        string singleMsg = currentData.Substring(0, newlineIndex).Trim();
                        currentData = currentData.Substring(newlineIndex + 1);

                        // Cập nhật lại StringBuilder với phần dữ liệu còn sót lại
                        sb.Clear();
                        sb.Append(currentData);

                        if (!string.IsNullOrEmpty(singleMsg))
                        {
                            ProcessLogic(client, singleMsg);
                        }
                    }
                }
            }
            catch { }
            finally
            {
                // Khi ngắt kết nối, lấy thông tin để báo cho Master Server
                if (clientMetadata.TryRemove(client, out var metadata))
                {
                    Console.WriteLine($"User {metadata.UserId} đã rời phòng {metadata.RoomId}. Đang cập nhật Database...");
                    _ = NotifyMasterStatusChanged(metadata.UserId, int.Parse(metadata.RoomId), false);
                }

                RemoveClientFromAllRooms(client);
                client.Close();
                Console.WriteLine("Client đã ngắt kết nối.");
            }
        }

        private void ProcessLogic(TcpClient client, string jsonMsg)
        {
            try
            {
                Console.WriteLine("RAW JSON = " + jsonMsg);

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                var msg = JsonSerializer.Deserialize<DrawMessage>(jsonMsg, options);
                if (msg == null) return;

                if (msg.type == "JOIN")
                {
                    // Phân loại phòng: Lấy danh sách client của phòng này, hoặc tạo mới nếu phòng chưa tồn tại
                    var room = rooms.GetOrAdd(msg.roomId, _ => new ConcurrentDictionary<TcpClient, byte>());
                    room[client] = 0; // Thêm client vào phòng

                    // Lưu lại Metadata để xử lý khi thoát
                    // Lưu ý: Client cần gửi kèm userId trong gói tin JOIN
                    clientMetadata[client] = (msg.userId, msg.roomId);

                    // Cập nhật Online trong DB
                    _ = NotifyMasterStatusChanged(msg.userId, int.Parse(msg.roomId), true);

                    Console.WriteLine($"Client {msg.userId} vào phòng: {msg.roomId}");

                    SendHistoryToClient(client, msg.roomId);
                }
                else if (msg.type == "DRAW")
                {
                    // Lấy userId thật từ connection
                    if (clientMetadata.TryGetValue(client, out var metadata))
                    {
                        msg.userId = metadata.UserId;
                    }

                    Console.WriteLine(
                        "[SERVER] DRAW USER ID = "
                        + msg.userId);

                    SaveDrawAction(msg);

                    BroadcastToRoom(msg.roomId, jsonMsg, client);
                }
                else if (msg.type == "LEAVE")
                {
                    // Xử lý cập nhật DB khi nhận lệnh LEAVE chủ động
                    if (clientMetadata.TryRemove(client, out var metadata))
                    {
                        _ = NotifyMasterStatusChanged(metadata.UserId, int.Parse(metadata.RoomId), false);
                    }
                    RemoveClientFromRoom(msg.roomId, client);
                }
            }
            catch (Exception ex) { Console.WriteLine("Lỗi xử lý JSON: " + ex.Message); }
        }

        // Hàm gọi API báo cho Master Server cập nhật Database
        private async Task NotifyMasterStatusChanged(int userId, int roomId, bool isOnline)
        {
            try
            {
                var statusData = new { user_id = userId, room_id = roomId, is_online = isOnline ? 1 : 0 };
                var content = new StringContent(JsonSerializer.Serialize(statusData), Encoding.UTF8, "application/json");
                await _httpClient.PostAsync(MasterApiUrl, content);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Không thể báo cáo trạng thái tới Master: {ex.Message}");
            }
        }

        private void BroadcastToRoom(string roomId, string rawJson, TcpClient sender)
        {
            // Kiểm tra xem tòa nhà có phòng này không
            if (!rooms.TryGetValue(roomId, out var clients)) return;

            // Đảm bảo có ký tự \n ở cuối để Client cũng tách được gói tin
            if (!rawJson.EndsWith("\n")) rawJson += "\n";
            byte[] data = Encoding.UTF8.GetBytes(rawJson);

            // Chỉ duyệt qua những người đang ở trong đúng roomId này
            foreach (var client in clients.Keys)
            {
                // Không gửi lại cho chính người vừa vẽ
                if (client == sender || !client.Connected) continue;

                try
                {
                    // Dùng BeginWrite thay vì Write để không bị block luồng khi có Client mạng chậm
                    client.GetStream().BeginWrite(data, 0, data.Length, null, null);
                }
                catch { /* Client lỗi, sẽ được dọn dẹp ở vòng lặp sau */ }
            }
        }

        private void RemoveClientFromRoom(string roomId, TcpClient client)
        {
            if (rooms.TryGetValue(roomId, out var clients))
            {
                clients.TryRemove(client, out _);
            }
        }

        private void SaveDrawAction(DrawMessage msg)
        {
            Console.WriteLine("===== SAVE DRAW =====");
            Console.WriteLine("roomId = " + msg.roomId);
            Console.WriteLine("userId = " + msg.userId);
            Console.WriteLine("type = " + msg.type);
            try
            {
                using (MySqlConnection conn =
                    new MySqlConnection(connectionString))
                {
                    conn.Open();

                    string sql = @"
        INSERT INTO DrawActions
        (user_id, room_id, type, data)
        VALUES
        (@user_id, @room_id, @type, @data)";

                    using (MySqlCommand cmd =
                        new MySqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue(
                            "@user_id",
                            msg.userId);

                        cmd.Parameters.AddWithValue(
                            "@room_id",
                            int.Parse(msg.roomId));

                        cmd.Parameters.AddWithValue(
                            "@type",
                            msg.type);

                        string jsonData =
                            JsonSerializer.Serialize(msg);

                        cmd.Parameters.AddWithValue(
                            "@data",
                            jsonData);

                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }

        private List<DrawMessage> LoadHistory(string roomId)
        {
            List<DrawMessage> history =
                new List<DrawMessage>();

            try
            {
                using (MySqlConnection conn =
                    new MySqlConnection(connectionString))
                {
                    conn.Open();

                    string sql = @"
        SELECT data
        FROM DrawActions
        WHERE room_id = @room_id
        ORDER BY created_at ASC";

                    using (MySqlCommand cmd =
                        new MySqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue(
                            "@room_id",
                            int.Parse(roomId));

                        using (var reader =
                            cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                string json =
                                    reader.GetString("data");

                                var draw =
                                    JsonSerializer.Deserialize<DrawMessage>(json);

                                if (draw != null)
                                {
                                    history.Add(draw);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    "[LOAD HISTORY ERROR] " + ex.Message);
            }

            return history;
        }

        private void SendHistoryToClient(TcpClient client, string roomId)
        {
            try
            {
                var history = LoadHistory(roomId);

                var packet = new
                {
                    type = "HISTORY",
                    roomId = roomId,
                    actions = history
                };

                string json =
                    JsonSerializer.Serialize(packet)
                    + "\n";

                byte[] data =
                    Encoding.UTF8.GetBytes(json);

                client.GetStream()
                    .Write(data, 0, data.Length);

                Console.WriteLine(
                    $"Đã gửi {history.Count} history actions");
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    "[SEND HISTORY ERROR] " + ex.Message);
            }
        }

        private void RemoveClientFromAllRooms(TcpClient client)
        {
            foreach (var room in rooms.Values)
            {
                room.TryRemove(client, out _);
            }
        }
    }
}