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

        var existing = conn.QueryFirstOrDefault<Node>(@"
            SELECT * FROM Nodes 
            WHERE ip_address = @ip AND port = @port",
            new { ip = req.ip_address, port = req.port });

        if (existing != null)
        {
            conn.Execute(@"
                UPDATE Nodes
                SET status = 'ACTIVE'
                WHERE node_id = @id",
                new { id = existing.node_id });

            return existing.node_id;
        }

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