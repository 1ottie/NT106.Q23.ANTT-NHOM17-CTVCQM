public class RoomConnectionResult
{
    public RoomResponse roomInfo { get; set; } = null!;
    public string nodeIp { get; set; } = string.Empty;
    public int nodePort { get; set; }
    public int nodeId { get; set; }
    public int currentUsers { get; set; }
}
