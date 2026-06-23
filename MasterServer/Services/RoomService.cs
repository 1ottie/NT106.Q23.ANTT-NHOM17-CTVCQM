using Dapper;
using System.Data;

public class RoomService
{
    private readonly DbConnection _db;
    private readonly NodeService _nodeService;

    public RoomService(DbConnection db, NodeService nodeService)
    {
        _db = db;
        _nodeService = nodeService;
    }

    public void UpdateUserStatus(int userId, int roomId, int isOnline)
    {
        using var conn = _db.GetConnection();
        var online = isOnline == 1 ? 1 : 0;

        var nodeId = conn.QueryFirstOrDefault<int?>(@"
            SELECT node_id
            FROM Rooms
            WHERE room_id = @room",
            new { room = roomId });

        conn.Execute(@"
            UPDATE RoomMembers
            SET is_online = @online
            WHERE user_id = @user AND room_id = @room",
            new { online, user = userId, room = roomId });

        if (nodeId.HasValue)
        {
            _nodeService.SyncCurrentUsersForNode(nodeId.Value);
        }
    }

    public RoomConnectionResult CreateRoom(CreateRoomRequest req, int userId)
    {
        using var conn = _db.GetConnection();

        var node = _nodeService.GetLeastConnectionNode();
        if (node == null)
        {
            throw new Exception("Khong co node nao dang hoat dong");
        }

        if (!string.IsNullOrEmpty(req.password))
        {
            req.is_private = true;
        }

        string? hash = null;
        if (req.is_private)
        {
            if (string.IsNullOrEmpty(req.password))
            {
                throw new Exception("Password required");
            }

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
                node = node.node_id,
                max = req.max_users
            });

        conn.Execute(@"
            INSERT INTO RoomMembers(user_id, room_id, role, is_online)
            VALUES(@user, @room, 'OWNER', 1)",
            new { user = userId, room = roomId });

        _nodeService.SyncCurrentUsersForNode(node.node_id);

        var room = conn.QueryFirstOrDefault<Room>(
            "SELECT * FROM Rooms WHERE room_id = @id",
            new { id = roomId });

        if (room == null)
        {
            throw new Exception("Room create failed");
        }

        node = _nodeService.GetNodeById(node.node_id) ?? node;
        return BuildConnectionResult(room, node);
    }

    public RoomConnectionResult JoinRoom(JoinRoomRequest req, int userId)
    {
        using var conn = _db.GetConnection();

        var room = conn.QueryFirstOrDefault<Room>(
            "SELECT * FROM Rooms WHERE room_id = @id",
            new { id = req.room_id });

        if (room == null)
        {
            throw new Exception("Phong khong ton tai");
        }

        if (room.is_private)
        {
            if (string.IsNullOrEmpty(req.password)
                || string.IsNullOrEmpty(room.password_hash)
                || !BCrypt.Net.BCrypt.Verify(req.password, room.password_hash))
            {
                throw new Exception("Sai mat khau");
            }
        }

        var node = ResolveNodeForRoom(conn, room);

        var member = conn.QueryFirstOrDefault<RoomMember>(@"
            SELECT *
            FROM RoomMembers
            WHERE room_id = @room AND user_id = @user",
            new { room = req.room_id, user = userId });

        var isAlreadyOnline = member?.is_online == true;
        if (!isAlreadyOnline && room.max_users > 0)
        {
            var onlineUsers = conn.ExecuteScalar<int>(@"
                SELECT COUNT(*)
                FROM RoomMembers
                WHERE room_id = @room AND is_online = 1",
                new { room = req.room_id });

            if (onlineUsers >= room.max_users)
            {
                throw new Exception("Phong da du nguoi");
            }
        }

        if (member == null)
        {
            conn.Execute(@"
                INSERT INTO RoomMembers(user_id, room_id, role, is_online)
                VALUES(@user, @room, 'MEMBER', 1)",
                new { user = userId, room = req.room_id });
        }
        else if (!member.is_online)
        {
            conn.Execute(@"
                UPDATE RoomMembers
                SET is_online = 1
                WHERE user_id = @user AND room_id = @room",
                new { user = userId, room = req.room_id });
        }

        _nodeService.SyncCurrentUsersForNode(node.node_id);
        node = _nodeService.GetNodeById(node.node_id) ?? node;

        return BuildConnectionResult(room, node);
    }

    public void LeaveRoom(int roomId, int userId)
    {
        UpdateUserStatus(userId, roomId, 0);
    }

    public object GetRooms()
    {
        using var conn = _db.GetConnection();

        var rooms = conn.Query(@"
        SELECT r.*, n.ip_address, n.port,
        (SELECT COUNT(*) FROM RoomMembers rm WHERE rm.room_id = r.room_id AND rm.is_online = 1) as player_count,
        u.username as owner_name
        FROM Rooms r
        LEFT JOIN Nodes n ON r.node_id = n.node_id
        LEFT JOIN Users u ON r.owner_id = u.user_id
        ORDER BY r.created_at DESC").ToList();

        return rooms.Select(r => new RoomListDto
        {
            room_id = Convert.ToInt32(r.room_id),
            room_name = (string)r.room_name,
            is_private = Convert.ToBoolean(r.is_private),
            max_users = Convert.ToInt32(r.max_users),
            created_at = (DateTime)r.created_at,
            player_count = Convert.ToInt32(r.player_count),
            owner_name = (string)(r.owner_name ?? "Unknown")
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

    private Node ResolveNodeForRoom(IDbConnection conn, Room room)
    {
        Node? node = null;

        if (room.node_id.HasValue)
        {
            node = conn.QueryFirstOrDefault<Node>(@"
                SELECT *
                FROM Nodes
                WHERE node_id = @id AND status = 'ACTIVE'",
                new { id = room.node_id.Value });
        }

        if (node != null)
        {
            return node;
        }

        node = _nodeService.GetLeastConnectionNode();
        if (node == null)
        {
            throw new Exception("Khong co may chu ve nao dang hoat dong");
        }

        conn.Execute(@"
            UPDATE Rooms
            SET node_id = @nodeId
            WHERE room_id = @roomId",
            new { nodeId = node.node_id, roomId = room.room_id });

        room.node_id = node.node_id;
        return node;
    }

    private static RoomConnectionResult BuildConnectionResult(Room room, Node node)
    {
        return new RoomConnectionResult
        {
            roomInfo = new RoomResponse
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
                    port = node.port,
                    current_users = node.current_users
                }
            },
            nodeIp = node.ip_address,
            nodePort = node.port,
            nodeId = node.node_id,
            currentUsers = node.current_users
        };
    }
}
