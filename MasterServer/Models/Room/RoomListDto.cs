using System;

public class RoomListDto
{
    public int room_id { get; set; }
    public string room_name { get; set; }
    public bool is_private { get; set; }
    public int max_users { get; set; }
    public DateTime created_at { get; set; }
    public int player_count { get; set; }
    public string owner_name { get; set; }
}
