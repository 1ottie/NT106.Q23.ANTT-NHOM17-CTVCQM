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

    public class DrawEvent
    {
        public string type { get; set; }   // start/move/end
        public string roomId { get; set; }
        public string strokeId { get; set; }
        public PointData data { get; set; }
    }

    public class PointData
    {
        public double x { get; set; }
        public double y { get; set; }
        public string color { get; set; }
        public double size { get; set; }
    }
}