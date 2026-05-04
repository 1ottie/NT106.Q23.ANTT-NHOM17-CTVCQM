namespace DrawClient
{
    public class DrawMessage
    {
        public string type { get; set; }
        public string roomId { get; set; }
        public int userId { get; set; }

        public double x1 { get; set; }
        public double y1 { get; set; }
        public double x2 { get; set; }
        public double y2 { get; set; }

        public string color { get; set; }
        public double thickness { get; set; }

        // Thêm 2 dòng này để hứng dữ liệu Chat (nếu có) mà không bị crash
        public string username { get; set; }
        public string content { get; set; }
    }
}