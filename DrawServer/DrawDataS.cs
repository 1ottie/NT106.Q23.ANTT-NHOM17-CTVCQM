public class DrawData
{
    public string Action { get; set; } // "START", "DRAW", "END"
    public double X { get; set; }
    public double Y { get; set; }
    public double OldX { get; set; } // Dùng để nối nét vẽ từ điểm cũ
    public double OldY { get; set; }
    public string Color { get; set; } // Ví dụ: "#000000"
    public double Thickness { get; set; }
}