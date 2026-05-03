namespace DrawServer
{
    public class DrawMessage
    {
        // Loại tin nhắn: "JOIN", "DRAW", "LEAVE", "CHAT"
        public string type { get; set; }
        public string roomId { get; set; }
        public int userId { get; set; } // ĐÃ THÊM: ID của người dùng để Node Server xử lý

        // Dữ liệu vẽ
        public double x1 { get; set; }
        public double y1 { get; set; }
        public double x2 { get; set; }
        public double y2 { get; set; }
        public string color { get; set; }
        public double thickness { get; set; }

        // Dữ liệu chat
        public string username { get; set; }
        public string content { get; set; }
    }
}