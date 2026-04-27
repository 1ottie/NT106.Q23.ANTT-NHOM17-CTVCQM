using Dapper;
using BCrypt.Net;
using System.Security.Claims;

public class RoomService
{
    private readonly DbConnection _db;

    public RoomService(DbConnection db)
    {
        _db = db;
    }

    public RoomResponse CreateRoom(CreateRoomRequest req, int userId)
    {
        using var conn = _db.GetConnection();

        // check node hợp lệ
        var node = conn.QueryFirstOrDefault<Node>(@"
            SELECT * FROM Nodes 
            WHERE node_id = @id AND status = 'ACTIVE'",
            new { id = req.node_id });

        if (node == null)
            throw new Exception("Node not available");

        string hash = null;

        if (req.is_private)
        {
            if (string.IsNullOrEmpty(req.password))
                throw new Exception("Password required");

            hash = BCrypt.Net.BCrypt.HashPassword(req.password);
        }

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

        conn.Execute(@"
            INSERT INTO RoomMembers(user_id, room_id, is_online, role)
            VALUES(@user, @room, true, 'OWNER')",
            new { user = userId, room = roomId });

        return new RoomResponse
        {
            room_id = roomId,
            room_name = req.room_name,
            is_private = req.is_private,
            max_users = req.max_users,
            created_at = DateTime.Now,
            node = new
            {
                node_id = node.node_id,
                ip = node.ip_address,
                port = node.port
            }
        };
    }

    public RoomResponse JoinRoom(JoinRoomRequest req, int userId)
    {
        using var conn = _db.GetConnection();

        var room = conn.QueryFirstOrDefault<Room>(@"
            SELECT * FROM Rooms WHERE room_id = @id",
            new { id = req.room_id });

        if (room == null)
            return null;

        // check password
        if (room.is_private)
        {
            bool ok = BCrypt.Net.BCrypt.Verify(req.password, room.password_hash);
            if (!ok)
                throw new Exception("Wrong password");
        }

        // check node ACTIVE
        var node = conn.QueryFirstOrDefault<dynamic>(@"
            SELECT * FROM Nodes 
            WHERE node_id = @id AND status = 'ACTIVE'",
            new { id = room.node_id });

        if (node == null)
            throw new Exception("Node unavailable");

        // check full phòng
        var count = conn.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM RoomMembers 
            WHERE room_id = @room AND is_online = true",
            new { room = req.room_id });

        if (count >= room.max_users)
            throw new Exception("Room is full");

        // thêm user vào phòng
        conn.Execute(@"
            INSERT INTO RoomMembers(user_id, room_id, is_online)
            VALUES(@user, @room, true)
            ON DUPLICATE KEY UPDATE is_online = true",
            new { user = userId, room = req.room_id });

        return new RoomResponse
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
        using var conn = _db.GetConnection();

        conn.Execute(@"
            UPDATE RoomMembers 
            SET is_online = false
            WHERE room_id = @room AND user_id = @user",
            new { room = roomId, user = userId });
    }

    public List<RoomResponse> GetRooms()
    {
        using var conn = _db.GetConnection();

        var rooms = conn.Query<Room>(@"
            SELECT * FROM Rooms ORDER BY created_at DESC").ToList();

        return rooms.Select(r => new RoomResponse
        {
            room_id = r.room_id,
            room_name = r.room_name,
            is_private = r.is_private,
            max_users = r.max_users,
            created_at = r.created_at,
            node = new { node_id = r.node_id }
        }).ToList();
    }

    public List<RoomMemberResponse> GetMembers(int roomId)
    {
        using var conn = _db.GetConnection();

        return conn.Query<RoomMemberResponse>(@"
            SELECT rm.user_id, u.username, rm.is_online, rm.role
            FROM RoomMembers rm
            JOIN Users u ON rm.user_id = u.user_id
            WHERE rm.room_id = @room AND rm.is_online = true",
            new { room = roomId }).ToList();
    }
}