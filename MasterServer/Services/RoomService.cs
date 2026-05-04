using Dapper;
using BCrypt.Net;
using System.Security.Claims;
using System;
using System.Collections.Generic;
using System.Linq;

public class RoomService
{
    private readonly DbConnection _db;

    public RoomService(DbConnection db)
    {
        _db = db;
    }

    // 1. Hàm cập nhật trạng thái 
    public void UpdateUserStatus(int userId, int roomId, int isOnline)
    {
        using var conn = _db.GetConnection();
        conn.Execute(@"
            UPDATE RoomMembers 
            SET is_online = @online 
            WHERE user_id = @user AND room_id = @room",
            new { online = isOnline, user = userId, room = roomId });
    }

    public object CreateRoom(CreateRoomRequest req, int userId)
    {
        using var conn = _db.GetConnection();

        // 1. Kiểm tra node hợp lệ
        var node = conn.QueryFirstOrDefault<Node>(@"
            SELECT * FROM Nodes 
            WHERE node_id = @id AND status = 'ACTIVE'",
            new { id = req.node_id });

        if (node == null)
            throw new Exception("Node not available");

        // 2. Xử lý mật khẩu
        string hash = null;
        if (req.is_private)
        {
            if (string.IsNullOrEmpty(req.password))
                throw new Exception("Password required");

            hash = BCrypt.Net.BCrypt.HashPassword(req.password);
        }

        // 3. Tạo phòng mới
        var roomId = conn.ExecuteScalar<int>(@"
            INSERT INTO Rooms(room_name, is_private, password_hash, owner_id, node_id, max_users)
            VALUES(@name, @private, @pass, @owner, @node, @max);
            SELECT LAST_INSERT_ID();",
            new
            {
                name = req.room_name,
                @private = req.is_private,
                pass = hash,
                owner = userId,
                node = req.node_id,
                max = req.max_users
            });

        // 4. Thêm người tạo vào phòng với role OWNER và is_online = 1
        conn.Execute(@"
            INSERT INTO RoomMembers(user_id, room_id, role, is_online)
            VALUES(@user, @room, 'OWNER', 1)",
            new { user = userId, room = roomId });

        // 5. Lấy lại thông tin phòng vừa tạo
        var room = conn.QueryFirstOrDefault<Room>("SELECT * FROM Rooms WHERE room_id = @id", new { id = roomId });

        return new
        {
            room_id = room.room_id,
            room_name = room.room_name,
            is_private = room.is_private,
            password = req.password,
            max_users = room.max_users,
            created_at = room.created_at,
            node = new
            {
                node_id = node.node_id,
                ip = node.ip_address,
                port = node.port
            }
        };
    }

    public object JoinRoom(JoinRoomRequest req, int userId)
    {
        using var conn = _db.GetConnection();

        var room = conn.QueryFirstOrDefault<Room>("SELECT * FROM Rooms WHERE room_id = @id", new { id = req.room_id });

        if (room == null)
            throw new Exception("Phòng không tồn tại");

        if (room.is_private)
        {
            if (string.IsNullOrEmpty(req.password) || !BCrypt.Net.BCrypt.Verify(req.password, room.password_hash))
                throw new Exception("Sai mật khẩu");
        }

        var node = conn.QueryFirstOrDefault<Node>("SELECT * FROM Nodes WHERE node_id = @id", new { id = room.node_id });
        if (node == null)
            throw new Exception("Node xử lý phòng này không hoạt động");

        var member = conn.QueryFirstOrDefault<RoomMember>("SELECT * FROM RoomMembers WHERE room_id = @room AND user_id = @user", new { room = req.room_id, user = userId });

        if (member == null)
        {
            // Thêm mới và đặt is_online = 1
            conn.Execute("INSERT INTO RoomMembers(user_id, room_id, role, is_online) VALUES(@user, @room, 'MEMBER', 1)", new { user = userId, room = req.room_id });
        }
        else
        {
            // Đã là thành viên thì chỉ cập nhật is_online = 1
            UpdateUserStatus(userId, req.room_id, 1);
        }

        return new
        {
            room_id = room.room_id,
            room_name = room.room_name,
            is_private = room.is_private,
            max_users = room.max_users,
            created_at = room.created_at,
            node = new
            {
                node_id = node.node_id,
                ip = node.ip_address,
                port = node.port
            }
        };
    }

    public void LeaveRoom(int roomId, int userId)
    {
        // Khi rời phòng chủ động, set is_online = 0
        UpdateUserStatus(userId, roomId, 0);
    }

    public object GetRooms()
    {
        using var conn = _db.GetConnection();

        var rooms = conn.Query(@"
        SELECT r.*, n.ip_address, n.port,
        (SELECT COUNT(*) FROM RoomMembers rm WHERE rm.room_id = r.room_id AND rm.is_online = 1) as player_count
        FROM Rooms r
        JOIN Nodes n ON r.node_id = n.node_id
        ORDER BY r.created_at DESC").ToList();

        return rooms.Select(r => new
        {
            room_id = r.room_id,
            room_name = r.room_name,
            is_private = r.is_private,
            max_users = r.max_users,
            created_at = r.created_at,
            player_count = r.player_count,
            node = new { node_id = r.node_id, ip = r.ip_address, port = r.port }
        }).ToList();
    }

    public object GetMembers(int roomId)
    {
        using var conn = _db.GetConnection();
        return conn.Query(@"
            SELECT rm.id, rm.user_id, u.username, rm.role, rm.joined_at, rm.is_online
            FROM RoomMembers rm
            JOIN Users u ON rm.user_id = u.user_id
            WHERE rm.room_id = @room",
            new { room = roomId }).ToList();
    }
}