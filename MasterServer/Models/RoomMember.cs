public class RoomMember
{
    public int id { get; set; }
    public int user_id { get; set; }
    public int room_id { get; set; }
    public bool is_online { get; set; }
    public string role { get; set; }  
    public DateTime joined_at { get; set; }
}