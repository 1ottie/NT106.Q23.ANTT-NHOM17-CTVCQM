using System.Windows;
using System;

namespace DrawClient.Models
{
    /// <summary>
    /// Đại diện cho một hành động vẽ trên Canvas
    /// Dùng để lưu lịch sử Undo/Redo
    /// </summary>
    public class DrawAction
    {
        public string ActionType { get; set; } // "DRAW", "ERASE", "SHAPE", "TEXT", "CLEAR"
        public Point StartPoint { get; set; }
        public Point EndPoint { get; set; }
        public string penType { get; set; }
        public string Color { get; set; }
        public double Thickness { get; set; }
        public string ShapeType { get; set; } // "rectangle", "circle", etc.
        public string Text { get; set; }
        public double FontSize { get; set; }
        public int UserId { get; set; }
        public string Username { get; set; }
        public DateTime Timestamp { get; set; }
        public string RoomId { get; set; }

        // Constructor mặc định
        public DrawAction()
        {
            Timestamp = DateTime.Now;
        }

        // Constructor đầy đủ
        public DrawAction(
            string actionType,
            Point start,
            Point end,
            string color,
            double thickness,
            int userId,
            string username,
            string roomId)
        {
            ActionType = actionType;
            StartPoint = start;
            EndPoint = end;
            Color = color;
            Thickness = thickness;
            UserId = userId;
            Username = username;
            RoomId = roomId;
            Timestamp = DateTime.Now;
        }

        public override string ToString()
        {
            return $"{ActionType} at {Timestamp:HH:mm:ss} by {Username}";
        }
    }
}