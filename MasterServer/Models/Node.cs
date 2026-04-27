public class Node
{
    public int node_id { get; set; }
    public string ip_address { get; set; }
    public int port { get; set; }
    public string status { get; set; }
    public DateTime? last_heartbeat { get; set; }
    public int current_users { get; set; }
}