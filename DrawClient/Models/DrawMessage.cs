namespace DrawClient
{
    public class DrawMessage
    {
        public string type { get; set; }
        public string roomId { get; set; }

        public double x1 { get; set; }
        public double y1 { get; set; }
        public double x2 { get; set; }
        public double y2 { get; set; }

        public string color { get; set; }
        public double thickness { get; set; }
    }
}