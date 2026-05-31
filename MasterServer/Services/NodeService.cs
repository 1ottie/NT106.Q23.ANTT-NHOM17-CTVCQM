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

        // Tìm node theo port — giữ nguyên node_id để rooms không bị mồ côi
        var existing = conn.QueryFirstOrDefault<Node>(
            "SELECT * FROM Nodes WHERE port = @port",
            new { port = req.port });

        if (existing != null)
        {
            // Xóa record conflict (cùng ip+port nhưng node_id khác) nếu có
            conn.Execute(
                "DELETE FROM Nodes WHERE ip_address = @ip AND port = @port AND node_id != @id",
                new { ip = req.ip_address, port = req.port, id = existing.node_id });

            // Update IP mới và set ACTIVE, giữ nguyên node_id
            conn.Execute(
                "UPDATE Nodes SET ip_address = @ip, status = 'ACTIVE' WHERE node_id = @id",
                new { ip = req.ip_address, id = existing.node_id });

            return existing.node_id;
        }

        // Chưa có node nào dùng port này — insert mới
        return conn.ExecuteScalar<int>(@"
            INSERT INTO Nodes(ip_address, port, status)
            VALUES(@ip, @port, 'ACTIVE');
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

    public Node? GetAnyActiveNode()
    {
        using var conn = _db.GetConnection();

        return conn.QueryFirstOrDefault<Node>(@"
            SELECT * FROM Nodes
            WHERE status = 'ACTIVE'
            LIMIT 1
        ");
    }

    public List<Node> GetAllNodes()
    {
        using var conn = _db.GetConnection();

        return conn.Query<Node>("SELECT * FROM Nodes").ToList();
    }

    public bool UpdateHeartbeat(int nodeId)
    {
        using var conn = _db.GetConnection();

        var rows = conn.Execute(@"
            UPDATE Nodes
            SET status = 'ACTIVE'
            WHERE node_id = @nodeId",
            new { nodeId });

        return rows > 0;
    }
}