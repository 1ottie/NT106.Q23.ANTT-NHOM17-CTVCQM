public class RoomResponse
{
    public int room_id { get; set; }
    public string room_name { get; set; }
    public bool is_private { get; set; }
    public int max_users { get; set; }
    public DateTime created_at { get; set; }

    public object node { get; set; }
}