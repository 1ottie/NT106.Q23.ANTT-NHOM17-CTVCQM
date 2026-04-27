public class Room
{
    public int room_id { get; set; }
    public string room_name { get; set; }
    public bool is_private { get; set; }
    public string password_hash { get; set; }
    public int? owner_id { get; set; }
    public int? node_id { get; set; }
    public int max_users { get; set; }
    public DateTime created_at { get; set; }
}