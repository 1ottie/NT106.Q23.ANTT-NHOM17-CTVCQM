using Dapper;

public class NodeService
{
    private readonly DbConnection _db;

    public NodeService(DbConnection db)
    {
        _db = db;
    }

    public int RegisterNode(RegisterNodeRequest req)
    {
        using var conn = _db.GetConnection();

        // Keep the same node_id for the same port so existing rooms do not lose ownership.
        var existing = conn.QueryFirstOrDefault<Node>(
            "SELECT * FROM Nodes WHERE port = @port",
            new { port = req.port });

        if (existing != null)
        {
            conn.Execute(
                "DELETE FROM Nodes WHERE ip_address = @ip AND port = @port AND node_id != @id",
                new { ip = req.ip_address, port = req.port, id = existing.node_id });

            conn.Execute(
                @"UPDATE Nodes
                  SET ip_address = @ip,
                      status = 'ACTIVE',
                      last_heartbeat = CURRENT_TIMESTAMP
                  WHERE node_id = @id",
                new { ip = req.ip_address, id = existing.node_id });

            SyncCurrentUsersForNode(existing.node_id);
            return existing.node_id;
        }

        return conn.ExecuteScalar<int>(@"
            INSERT INTO Nodes(ip_address, port, status, last_heartbeat, current_users)
            VALUES(@ip, @port, 'ACTIVE', CURRENT_TIMESTAMP, 0);
            SELECT LAST_INSERT_ID();",
            new { ip = req.ip_address, port = req.port });
    }

    public Node? GetNodeById(int nodeId)
    {
        using var conn = _db.GetConnection();

        return conn.QueryFirstOrDefault<Node>(@"
            SELECT * FROM Nodes
            WHERE node_id = @id",
            new { id = nodeId });
    }

    public Node? GetLeastConnectionNode()
    {
        using var conn = _db.GetConnection();

        return conn.QueryFirstOrDefault<Node>(@"
            SELECT
                n.node_id,
                n.ip_address,
                n.port,
                n.status,
                n.last_heartbeat,
                CAST(COUNT(rm.id) AS SIGNED) AS current_users
            FROM Nodes n
            LEFT JOIN Rooms r ON r.node_id = n.node_id
            LEFT JOIN RoomMembers rm ON rm.room_id = r.room_id AND rm.is_online = 1
            WHERE n.status = 'ACTIVE'
            GROUP BY n.node_id, n.ip_address, n.port, n.status, n.last_heartbeat
            ORDER BY current_users ASC, n.node_id ASC
            LIMIT 1");
    }

    public Node? GetAnyActiveNode()
    {
        return GetLeastConnectionNode();
    }

    public List<Node> GetAllNodes()
    {
        using var conn = _db.GetConnection();

        return conn.Query<Node>(@"
            SELECT
                n.node_id,
                n.ip_address,
                n.port,
                n.status,
                n.last_heartbeat,
                CAST(COUNT(rm.id) AS SIGNED) AS current_users
            FROM Nodes n
            LEFT JOIN Rooms r ON r.node_id = n.node_id
            LEFT JOIN RoomMembers rm ON rm.room_id = r.room_id AND rm.is_online = 1
            GROUP BY n.node_id, n.ip_address, n.port, n.status, n.last_heartbeat
            ORDER BY n.node_id").ToList();
    }

    public void SyncCurrentUsersForNode(int nodeId)
    {
        using var conn = _db.GetConnection();

        conn.Execute(@"
            UPDATE Nodes
            SET current_users = (
                SELECT COUNT(*)
                FROM Rooms r
                JOIN RoomMembers rm ON rm.room_id = r.room_id
                WHERE r.node_id = @nodeId AND rm.is_online = 1
            )
            WHERE node_id = @nodeId",
            new { nodeId });
    }

    public void SyncAllCurrentUsers()
    {
        using var conn = _db.GetConnection();

        conn.Execute(@"
            UPDATE Nodes n
            SET current_users = (
                SELECT COUNT(*)
                FROM Rooms r
                JOIN RoomMembers rm ON rm.room_id = r.room_id
                WHERE r.node_id = n.node_id AND rm.is_online = 1
            )");
    }

    public bool UpdateHeartbeat(int nodeId)
    {
        using var conn = _db.GetConnection();

        var rows = conn.Execute(@"
            UPDATE Nodes
            SET status = 'ACTIVE',
                last_heartbeat = CURRENT_TIMESTAMP
            WHERE node_id = @nodeId",
            new { nodeId });

        return rows > 0;
    }
}
