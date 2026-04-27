using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DrawClient.Models
{
    public class ServerConfig
    {
        public string Username { get; set; }
        public string ServerIp { get; set; }
        public string Port { get; set; }
        public bool IsHybridSecurity { get; set; }
    }
}
