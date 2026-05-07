using System.Collections.Generic;

namespace DrawClient.Models
{
    public class HistoryMessage
    {
        public string type { get; set; }

        public string roomId { get; set; }

        public List<DrawMessage> actions { get; set; }
    }
}