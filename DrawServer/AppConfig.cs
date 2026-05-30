using System;
using System.IO;

namespace DrawServer
{
    public static class AppConfig
    {
        public static string NodeIp            { get; private set; }
        public static int    NodePort          { get; private set; }
        public static string MasterServerIp    { get; private set; }
        public static int    MasterServerPort  { get; private set; }
        public static string DbConnectionString { get; private set; }

        static AppConfig()
        {
            // Giá trị mặc định — dùng khi config.ini không tồn tại
            NodeIp             = "10.45.27.103";
            NodePort           = 6001;
            MasterServerIp     = "10.45.27.103";
            MasterServerPort   = 5274;
            DbConnectionString = "server=localhost;database=online_Drawing_DB;user=root;password=182806";

            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");
            if (!File.Exists(configPath)) return;

            foreach (string line in File.ReadAllLines(configPath))
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed[0] == ';' || trimmed[0] == '#' || trimmed[0] == '[')
                    continue;

                int eq = trimmed.IndexOf('=');
                if (eq < 0) continue;

                // Tách tại dấu '=' đầu tiên để giữ nguyên '=' trong connection string
                string key = trimmed.Substring(0, eq).Trim();
                string val = trimmed.Substring(eq + 1).Trim();

                switch (key)
                {
                    case "NodeIp":
                        if (!string.IsNullOrWhiteSpace(val)) NodeIp = val;
                        break;
                    case "NodePort":
                        if (int.TryParse(val, out int np)) NodePort = np;
                        break;
                    case "MasterServerIp":
                        if (!string.IsNullOrWhiteSpace(val)) MasterServerIp = val;
                        break;
                    case "MasterServerPort":
                        if (int.TryParse(val, out int mp)) MasterServerPort = mp;
                        break;
                    case "DbConnectionString":
                        if (!string.IsNullOrWhiteSpace(val)) DbConnectionString = val;
                        break;
                }
            }
        }
    }
}
