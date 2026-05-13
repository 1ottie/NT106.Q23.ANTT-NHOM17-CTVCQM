using System;

namespace DrawClient.Models
{
    public class ChatMessage
    {
        public string User { get; set; }

        public string UserInitials
        {
            get
            {
                if (string.IsNullOrWhiteSpace(User))
                    return "?";

                string[] parts = User.Split(' ');

                if (parts.Length == 1)
                    return parts[0][0].ToString().ToUpper();

                return (parts[0][0].ToString() +
                       parts[parts.Length - 1][0].ToString()).ToUpper();
            }
        }

        public string Initial =>
            string.IsNullOrWhiteSpace(User)
                ? "U"
                : User.Substring(0, 1).ToUpper();

        public string Message { get; set; }

        public DateTime Timestamp { get; set; }

        public string Time
        {
            get
            {
                return Timestamp.ToString("HH:mm");
            }
        }

        public string FullDateTime
        {
            get
            {
                return Timestamp.ToString("HH:mm - dd/MM/yyyy");
            }
        }

        public bool ShowSeparator { get; set; }

        public bool IsMine { get; set; }
    }
}