using System.Windows;
using System;
using System.Collections.Generic;
using System.Threading;

namespace DrawClient.Models
{
    public class DrawAction
    {
        private static long _idCounter = 0;

        public string Id { get; set; }
        public string ActionType { get; set; }
        public Point StartPoint { get; set; }
        public Point EndPoint { get; set; }
        public string penType { get; set; }
        public string Color { get; set; }
        public double Thickness { get; set; }
        public string ShapeType { get; set; }
        public string Text { get; set; }
        public double FontSize { get; set; }
        public string FontFamily { get; set; }
        public int UserId { get; set; }
        public string Username { get; set; }
        public DateTime Timestamp { get; set; }
        public string RoomId { get; set; }
        public bool IsUndone { get; set; } = false;
        public string StrokeGroupId { get; set; }

        // Dùng khi ActionType == "TRANSFORM": ghi lại action nào bị di chuyển và transform matrix
        public List<string> AffectedActionIds { get; set; }       // SHAPE / TEXT action IDs
        public List<string> AffectedStrokeGroupIds { get; set; }  // native PEN stroke group IDs
        public double TransformOldX { get; set; }
        public double TransformOldY { get; set; }
        public double TransformOldW { get; set; }
        public double TransformOldH { get; set; }
        public double TransformNewX { get; set; }
        public double TransformNewY { get; set; }
        public double TransformNewW { get; set; }
        public double TransformNewH { get; set; }

        public static string GenerateId()
        {
            long id = Interlocked.Increment(ref _idCounter);
            return $"{Environment.MachineName}_{id}_{DateTime.UtcNow.Ticks}";
        }

        public DrawAction()
        {
            Id = GenerateId();
            Timestamp = DateTime.Now;
        }

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
            Id = GenerateId();
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
